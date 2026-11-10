using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace PhotoLensWin.App.Services;

/// <summary>
/// 缩略图缓存：异步生成 + 有上限（300 张），LRU 淘汰，并发 4。
/// 对应 Mac 版 ThumbnailStore。
/// </summary>
public sealed class ThumbnailStore : IDisposable
{
    private sealed class Entry
    {
        public Bitmap? Bitmap;
        public DateTime LastAccessTime = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    // 解码放后台线程池；高优先级（前 N 张）用独立通道（2 并发，跳过普通队列）
    private readonly SemaphoreSlim _gate = new(4, 4);
    private readonly SemaphoreSlim _highPriorityGate = new(2, 2);
    private readonly int _maxCount = 300;
    private bool _disposed;

    /// <summary>加载并缓存缩略图；已缓存则立即返回，否则后台解码。
    /// highPriority=true 时绕过普通队列，用独立的 2 并发通道，优先完成前 N 张。</summary>
    public async Task<Bitmap?> LoadAsync(string path, bool highPriority = false)
    {
        if (_disposed) return null;

        var cached = GetCached(path);
        if (cached is not null) return cached;

        var gate = highPriority ? _highPriorityGate : _gate;
        await gate.WaitAsync();
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
                var entry = new Entry { Bitmap = result, LastAccessTime = DateTime.UtcNow };
                _cache[path] = entry;
                EvictIfNeeded();
            }
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>LRU 淘汰：按 LastAccessTime 最旧的先删。</summary>
    private void EvictIfNeeded()
    {
        while (_cache.Count > _maxCount)
        {
            string? oldest = null;
            DateTime oldestTime = DateTime.MaxValue;
            foreach (var (key, entry) in _cache)
            {
                if (entry.Bitmap is null) continue;
                if (entry.LastAccessTime < oldestTime)
                {
                    oldestTime = entry.LastAccessTime;
                    oldest = key;
                }
            }
            if (oldest is null) break;
            if (_cache.TryRemove(oldest, out var removed))
            {
                try { removed.Bitmap?.Dispose(); } catch { }
            }
        }
    }

    /// <summary>同步查询缓存，未命中返回 null。命中时更新访问时间。</summary>
    public Bitmap? GetCached(string path)
    {
        if (_cache.TryGetValue(path, out var entry))
        {
            lock (entry)
            {
                if (entry.Bitmap is not null)
                {
                    entry.LastAccessTime = DateTime.UtcNow;
                    return entry.Bitmap;
                }
            }
        }
        return null;
    }

    public void Prefetch(string path) => _ = LoadAsync(path);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        _highPriorityGate.Dispose();
        foreach (var (_, entry) in _cache)
        {
            try { entry.Bitmap?.Dispose(); } catch { }
        }
        _cache.Clear();
    }
}
