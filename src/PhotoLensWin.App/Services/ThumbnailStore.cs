using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace PhotoLensWin.App.Services;

/// <summary>
/// 缩略图缓存：异步生成 + 有上限（300 张），对应 Mac 版 ThumbnailStore。
/// 加载统一走 LoadAsync（缓存命中立即返回）；UI 侧在 Dispatcher 上应用结果。
/// </summary>
public sealed class ThumbnailStore
{
    private sealed class Entry
    {
        public Bitmap? Bitmap;
    }

    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    // 解码放后台线程池，同时最多 4 个并发；此前在 UI 线程同步解码，大目录会逐张卡顿
    private readonly SemaphoreSlim _gate = new(4, 4);
    private readonly int _maxCount = 300;

    /// <summary>加载并缓存缩略图；已缓存则立即返回，否则后台解码（最多 4 个并发）。</summary>
    public async Task<Bitmap?> LoadAsync(string path)
    {
        var cached = GetCached(path);
        if (cached is not null) return cached;

        await _gate.WaitAsync();
        try
        {
            cached = GetCached(path);
            if (cached is not null) return cached;

            Bitmap? result = await Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, 220, BitmapInterpolationMode.HighQuality);
                }
                catch
                {
                    return null; // 解码失败：不缓存，调用方显示占位
                }
            });

            if (result is not null)
            {
                _cache[path] = new Entry { Bitmap = result };
                while (_cache.Count > _maxCount)
                {
                    // 淘汰任意一个已完成条目（无时间戳，取遍历序即可）
                    foreach (var (key, value) in _cache)
                    {
                        if (value.Bitmap is not null)
                        {
                            _cache.TryRemove(key, out _);
                            break;
                        }
                    }
                    break;
                }
            }
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>同步查询缓存，未命中返回 null。</summary>
    public Bitmap? GetCached(string path)
    {
        var entry = _cache.GetOrAdd(path, _ => new Entry());
        lock (entry)
        {
            return entry.Bitmap;
        }
    }

    public void Prefetch(string path) => _ = LoadAsync(path);
}
