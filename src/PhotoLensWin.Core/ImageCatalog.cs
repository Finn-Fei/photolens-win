using System;
using System.Collections.Generic;
using System.IO;

namespace PhotoLensWin.Core;

/// <summary>
/// 图片目录收集：递归展开子文件夹、跳过隐藏项、按扩展名过滤。
/// Windows 版按约定不含 HEIC/HEIF/AVIF（Windows 解码栈不支持）。
/// </summary>
public static class ImageCatalog
{
    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "jpe", "jfif", "png", "tif", "tiff", "gif", "bmp", "webp", "ico",
    };

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).TrimStart('.'));

    /// <summary>递归收集文件夹内图片；访问异常的子目录静默跳过。</summary>
    public static List<ImageEntry> CollectImages(string folder, PhotoSort sort = PhotoSort.NameAscending)
    {
        var files = new List<ImageEntry>();
        CollectRecursive(folder, files);
        return sort.Apply(files);
    }

    /// <summary>从混合路径（文件/文件夹）中挑出图片文件，文件夹展开递归收集；同一路径只保留一条。</summary>
    public static List<ImageEntry> CollectFromPaths(IEnumerable<string> paths)
    {
        var files = new List<ImageEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                CollectRecursive(path, files, seen);
            }
            else if (File.Exists(path) && IsImageFile(path))
            {
                // 文件与其父目录同时拖入时，目录递归已收集过同一条目
                if (seen.Add(Path.GetFullPath(path))) files.Add(Describe(path));
            }
        }
        return files;
    }

    public static ImageEntry Describe(string path)
    {
        var info = new FileInfo(path);
        return new ImageEntry(
            info.FullName,
            info.Name,
            info.Exists ? info.Length : 0,
            info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue);
    }

    /// <summary>排序切换后定位当前图片的新下标；找不到回退到 0。</summary>
    public static int StartIndexFor(IReadOnlyList<ImageEntry> sorted, string? currentPath)
    {
        if (currentPath is null || sorted.Count == 0) return 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            if (string.Equals(
                    Path.GetFullPath(sorted[i].FullPath),
                    Path.GetFullPath(currentPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return 0;
    }

    private static void CollectRecursive(string folder, List<ImageEntry> sink, HashSet<string>? seen = null)
    {
        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(folder);
            if (!dir.Exists) return;
        }
        catch
        {
            return;
        }

        // 按目录内的自然顺序预排，保证递归结果与最终排序无关时仍然稳定
        // EnumerateFileSystemInfos 是延迟执行：构造时不抛，迭代时才抛。
        // 必须把 foreach 一起包进 try，否则遇到无权限目录会在调用方（async void）里崩溃
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo sub)
                {
                    if (!IsHidden(sub)) CollectRecursive(sub.FullName, sink, seen);
                }
                else if (entry is FileInfo file
                         && !IsHidden(file)
                         && ImageExtensions.Contains(file.Extension.TrimStart('.')))
                {
                    if (seen is not null && !seen.Add(file.FullName)) continue;
                    sink.Add(new ImageEntry(file.FullName, file.Name, file.Length, file.LastWriteTimeUtc));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 该目录不可读：跳过，已收集的条目保留
        }
        catch (IOException)
        {
            // 目录被移走/设备离线：跳过
        }
    }

    private static bool IsHidden(FileSystemInfo entry)
    {
        // dot 开头的文件按隐藏处理（兼容类 Unix 习惯），Windows 隐藏属性同样跳过
        if (entry.Name.StartsWith('.')) return true;
        try
        {
            return entry.Attributes.HasFlag(FileAttributes.Hidden);
        }
        catch
        {
            return true;
        }
    }
}
