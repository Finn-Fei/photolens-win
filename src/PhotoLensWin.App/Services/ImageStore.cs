using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
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
/// </summary>
public sealed class ImageStore
{
    private readonly ConcurrentDictionary<string, (FullImage Image, long Cost)> _cache = new();

    public int Capacity { get; set; } = 3;

    public FullImage? Get(string path) =>
        _cache.TryGetValue(path, out var hit) ? hit.Image : null;

    /// <summary>正在显示的图（主图/对比图）由宿主标记，淘汰时跳过并保持存活。</summary>
    public Func<FullImage, bool>? IsProtected { get; set; }

    public void Put(FullImage image)
    {
        _cache[image.Path] = (image, image.ByteCost);
        while (_cache.Count > Capacity)
        {
            string? victim = null;
            long min = long.MaxValue;
            foreach (var (key, value) in _cache)
            {
                if (key == image.Path) continue;                  // 刚放入的项不立即淘汰
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
                removed.Image.Dispose();
            }
        }
    }

    /// <summary>
    /// 全量解码：Skia 一次解码同时服务显示与画质评分，
    /// EXIF 方向以显示矩阵呈现（不重写像素），与 Windows 照片应用惯例一致。
    /// 同时缓存 metadata directories 避免后续重复读盘。
    /// </summary>
    public static async Task<FullImage?> LoadAsync(string path)
    {
        return await Task.Run(() =>
        {
            SKBitmap? sk = null;
            Bitmap? bitmap = null;
            try
            {
                using var stream = File.OpenRead(path);
                sk = SKBitmap.Decode(stream);
                if (sk is null) return null;

                // Skia 解码色型随平台/来源而变；显示端固定按 Bgra8888 解释，先统一转换
                if (sk.ColorType != SKColorType.Bgra8888)
                {
                    var converted = sk.Copy(SKColorType.Bgra8888);
                    if (converted is not null)
                    {
                        sk.Dispose();
                        sk = converted;
                    }
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
        });
    }

    /// <summary>缩到最长边 320px 后做像素统计评分，与 Mac 版一致。</summary>
    private static QualityReport SampleQuality(SKBitmap bitmap)
    {
        try
        {
            double ratio = Math.Min(1.0, 320.0 / Math.Max(bitmap.Width, bitmap.Height));
            int sw = Math.Max(1, (int)(bitmap.Width * ratio));
            int sh = Math.Max(1, (int)(bitmap.Height * ratio));
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
            return QualityScorer.AssessQuality(sw, sh, pixels, PixelOrder.Bgra);
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
