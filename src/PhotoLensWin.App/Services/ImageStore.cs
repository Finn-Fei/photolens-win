using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MetadataExtractor;
using PhotoLensWin.Core;
using SkiaSharp;

namespace PhotoLensWin.App.Services;

/// <summary>
/// 已解码大图缓存：容量 3 张、按字节成本淘汰，对应 Mac 版 ImageCache。
/// 所有缓存读写都发生在 UI 线程（代码里没有 ConfigureAwait(false)），故未对
/// Put/淘汰加锁；若将来改到后台线程调用，这里必须重新加锁。
/// </summary>
public sealed class ImageStore : IDisposable
{
    // 路径按 Windows 语义大小写不敏感，否则同一文件会因大小写差异缓存两份
    private readonly ConcurrentDictionary<string, (FullImage Image, long Cost)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>同一路径的解码单飞：并发请求共享同一次解码，避免同图被并行解码两份。</summary>
    private static readonly ConcurrentDictionary<string, Task<FullImage?>> InFlight =
        new(StringComparer.OrdinalIgnoreCase);

    public int Capacity { get; set; } = 3;

    public FullImage? Get(string path) =>
        _cache.TryGetValue(path, out var hit) ? hit.Image : null;

    /// <summary>从缓存摘除并返回实例（由调用方决定何时释放）。
    /// 文件被删除/替换时必须走这里，否则缓存里会留下已释放的位图，后续 Get 会取到死对象。</summary>
    public FullImage? Remove(string path) =>
        _cache.TryRemove(path, out var hit) ? hit.Image : null;

    /// <summary>正在显示的图（主图/对比图）由宿主标记，淘汰时跳过并保持存活。</summary>
    public Func<FullImage, bool>? IsProtected { get; set; }

    public void Put(FullImage image)
    {
        // 同键覆盖必须显式释放旧实例：ConcurrentDictionary 索引器会丢弃被替换的值，
        // 不释放就永久泄漏一份全尺寸位图（含 SKBitmap 原生像素），单张 24MP ≈ 192MB。
        FullImage? replaced = null;
        if (_cache.TryGetValue(image.Path, out var existing) &&
            existing.Image is not null && !ReferenceEquals(existing.Image, image))
        {
            replaced = existing.Image;
        }

        _cache[image.Path] = (image, image.ByteCost);

        if (replaced is not null) SafeDispose(replaced);

        while (_cache.Count > Capacity)
        {
            string? victim = null;
            long min = long.MaxValue;
            foreach (var (key, value) in _cache)
            {
                if (string.Equals(key, image.Path, StringComparison.OrdinalIgnoreCase)) continue; // 刚放入的项不立即淘汰
                if (IsProtected?.Invoke(value.Image) == true) continue;
                if (value.Cost < min)
                {
                    min = value.Cost;
                    victim = key;
                }
            }
            if (victim is null) break;
            if (_cache.TryRemove(victim, out var removed))
            {
                SafeDispose(removed.Image);
            }
        }
    }

    /// <summary>释放缓存中的全部实例（窗口关闭时调用）。</summary>
    public void Dispose()
    {
        foreach (var (_, value) in _cache)
        {
            SafeDispose(value.Image);
        }
        _cache.Clear();
    }

    private static void SafeDispose(FullImage image)
    {
        try { image.Dispose(); }
        catch { /* 释放失败不影响退出 */ }
    }

    /// <summary>
    /// 全量解码：Skia 一次解码同时服务显示与画质评分，
    /// EXIF 方向以显示矩阵呈现（不重写像素），与 Windows 照片应用惯例一致。
    /// 同时缓存 metadata directories 避免后续重复读盘。
    /// 同一路径的并发调用共享同一次解码（单飞），避免并行解码两份 + 缓存覆盖丢引用。
    /// </summary>
    public static Task<FullImage?> LoadAsync(string path)
    {
        while (true)
        {
            if (InFlight.TryGetValue(path, out var running)) return running;

            // 用 TCS 占位，竞争成功后才有线程开始解码——避免"抢输的请求白解码一次"
            var tcs = new TaskCompletionSource<FullImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!InFlight.TryAdd(path, tcs.Task)) continue;

            _ = Task.Run(() =>
            {
                try { tcs.SetResult(LoadCore(path)); }
                catch { tcs.SetResult(null); }
                finally { InFlight.TryRemove(path, out _); }
            });
            return tcs.Task;
        }
    }

    private static FullImage? LoadCore(string path)
    {
        SKBitmap? sk = null;
        Bitmap? bitmap = null;
        try
        {
            // 先按像素上限拒绝畸形/炸弹图，避免一次性申请 GB 级缓冲
            if (!ImageGuards.WithinPixelBudget(path)) return null;

            using var stream = File.OpenRead(path);
            sk = SKBitmap.Decode(stream);
            if (sk is null) return null;

            // Skia 解码色型随平台/来源而变；显示端固定按 Bgra8888 解释，先统一转换
            if (sk.ColorType != SKColorType.Bgra8888)
            {
                var converted = sk.Copy(SKColorType.Bgra8888);
                if (converted is null)
                {
                    // 转换失败时不能把原色型缓冲按 Bgra 解释：会 R/B 通道互换出偏色
                    sk.Dispose();
                    return null;
                }
                sk.Dispose();
                sk = converted;
            }

            bitmap = new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                sk.GetPixels(),
                new PixelSize(sk.Width, sk.Height),
                new Vector(96, 96),
                sk.RowBytes);

            var quality = SampleQuality(sk);

            // 一次性读 metadata，方向 + directories 全缓存
            var directories = ExifLines.ReadDirectories(path);
            int orientation = MetadataReader.Orientation(directories);
            bool swapped = ExifOrientation.SwapsSides(orientation);

            return new FullImage(
                path, bitmap, sk, quality, orientation,
                swapped ? sk.Height : sk.Width,
                swapped ? sk.Width : sk.Height,
                sk.Width, sk.Height,
                File.GetLastWriteTimeUtc(path),
                directories);
        }
        catch
        {
            // 解码成功但后续步骤失败：释放已分配的原生资源，避免泄漏
            bitmap?.Dispose();
            sk?.Dispose();
            return null;
        }
    }

    /// <summary>缩到最长边 320px 后做像素统计评分，与 Mac 版一致。</summary>
    private static QualityReport SampleQuality(SKBitmap bitmap)
    {
        try
        {
            // 尺寸计算复用 Core 的 StatsSize，避免 320 / Math.Max(16, …) 在两处各写一份
            var (sw, sh) = QualityScorer.StatsSize(bitmap.Width, bitmap.Height);
            var info = new SKImageInfo(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var small = new SKBitmap(info);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
            using (var canvas = new SKCanvas(small))
            {
                canvas.DrawBitmap(
                    bitmap,
                    new SKRect(0, 0, bitmap.Width, bitmap.Height),
                    new SKRect(0, 0, sw, sh),
                    paint);
            }
            byte[] pixels = small.Bytes;
            // 分辨率分要用原图尺寸，像素统计才用缩小后的缓冲
            return QualityScorer.AssessQuality(
                bitmap.Width, bitmap.Height, pixels, PixelOrder.Bgra, sw, sh);
        }
        catch
        {
            // 评分失败不影响查看：返回空条目，信息面板显示「不可用」而不是误导性的低分
            return new QualityReport(0, Array.Empty<QualityItem>());
        }
    }
}

/// <summary>加载完成的大图：位图 + 画质 + 方向 + 展示尺寸 + 预缓存的 EXIF 元数据。</summary>
public sealed record FullImage(
    string Path,
    Bitmap Bitmap,
    SKBitmap Source,
    QualityReport Quality,
    int Orientation,
    int DisplayWidth,
    int DisplayHeight,
    int PixelWidth,
    int PixelHeight,
    DateTime ModifiedUtc,
    IReadOnlyList<MetadataExtractor.Directory>? CachedDirectories) : IDisposable
{
    public long ByteCost => (long)PixelWidth * PixelHeight * 4;

    /// <summary>先释放 Avalonia 包装层，再释放像素源（Bitmap 引用 Source 的像素指针）。</summary>
    public void Dispose()
    {
        Bitmap.Dispose();
        Source.Dispose();
    }
}
