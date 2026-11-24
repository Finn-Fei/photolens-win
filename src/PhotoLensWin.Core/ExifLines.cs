using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MexDirectory = MetadataExtractor.Directory;

namespace PhotoLensWin.Core;

/// <summary>
/// 图片元数据展示行：像素尺寸、文件大小、机身、拍摄参数、拍摄时间、修改时间、路径。
/// 从 Mac 版 metadataLines 移植，EXIF 解码改用 MetadataExtractor（纯 .NET）。
/// 支持传入预缓存的 directories 避免重复读盘。
/// </summary>
public static class ExifLines
{
    public static string SizeText(long bytes)
    {
        const double mb = 1024 * 1024;
        return bytes >= mb
            ? $"{bytes / mb:F1} MB"
            : $"{Math.Max(1, Math.Round(bytes / 1024.0)):F0} KB";
    }

    /// <summary>便捷入口：内部自己读 metadata（首次载入或无缓存时用）。</summary>
    public static string[] Build(string path, int pixelWidth, int pixelHeight, DateTime modifiedUtc)
        => Build(path, pixelWidth, pixelHeight, modifiedUtc, null);

    /// <summary>主入口：可传入预缓存的 directories，避免重复磁盘读取。</summary>
    public static string[] Build(
        string path,
        int pixelWidth,
        int pixelHeight,
        DateTime modifiedUtc,
        IReadOnlyList<MexDirectory>? cachedDirectories)
    {
        var lines = new List<string>
        {
            $"{pixelWidth} × {pixelHeight} 像素",
        };
        var file = new FileInfo(path);
        if (file.Exists)
        {
            lines.Add($"文件 {SizeText(file.Length)} · {file.Extension.TrimStart('.').ToUpperInvariant()}");
        }

        try
        {
            var directories = cachedDirectories ?? ImageMetadataReader.ReadMetadata(path);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            string camera = $"{Str(ifd0, ExifDirectoryBase.TagMake)} {Str(ifd0, ExifDirectoryBase.TagModel)}".Trim();
            if (camera.Length > 0) lines.Add("机身 " + camera);

            if (subIfd is not null)
            {
                var shooting = new List<string>();
                if (subIfd.TryGetDouble(ExifDirectoryBase.TagExposureTime, out var exp) && exp > 0)
                {
                    // 0.4s 的倒数 2.5 必须进到 3（1/3s）：默认银行家舍入会得到 1/2s
                    shooting.Add(exp < 1
                        ? $"1/{(int)Math.Round(1 / exp, MidpointRounding.AwayFromZero)}s"
                        : string.Format(CultureInfo.InvariantCulture, "{0:F1}s", exp));
                }
                if (subIfd.TryGetDouble(ExifDirectoryBase.TagFNumber, out var f) && f > 0)
                {
                    shooting.Add("f/" + f.ToString("F1", CultureInfo.InvariantCulture));
                }
                string? iso = Str(subIfd, 0x8827); // ISO Speed Ratings
                if (!string.IsNullOrEmpty(iso) && iso.All(char.IsDigit)) shooting.Add($"ISO {iso}");
                if (subIfd.TryGetDouble(ExifDirectoryBase.TagFocalLength, out var mm) && mm > 0)
                {
                    shooting.Add($"{mm:F0}mm");
                }
                if (shooting.Count > 0) lines.Add(string.Join(" · ", shooting));

                string? original = Str(subIfd, ExifDirectoryBase.TagDateTimeOriginal);
                if (!string.IsNullOrEmpty(original)) lines.Add(original);
            }
        }
        catch
        {
            // 无 EXIF 或解析失败：只保留基础行，不影响查看
        }

        lines.Add("修改于 " + modifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        lines.Add("路径 " + AbbreviateHome(path));
        return lines.ToArray();
    }

    /// <summary>读取文件的全部 metadata directories（ImageStore 解码时缓存用）。</summary>
    public static IReadOnlyList<MexDirectory>? ReadDirectories(string path)
    {
        try { return ImageMetadataReader.ReadMetadata(path); }
        catch { return null; }
    }

    private static string? Str(MexDirectory? dir, int tag)
    {
        if (dir is null) return null;
        try { return dir.GetString(tag); } catch { return null; }
    }
    /// <summary>
    /// 把用户主目录缩写成 ~。必须大小写不敏感（Windows 路径语义），且要落在目录边界上——
    /// 否则 home=C:\Users\Bob 会错误匹配并缩写 C:\Users\Bob2\...（既显示错误又泄露用户名）。
    /// </summary>
    public static string AbbreviateHome(string path, string? homeOverride = null)
    {
        string home = homeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) return path;
        home = home.TrimEnd('\\', '/');
        if (home.Length == 0) return path;
        if (!path.StartsWith(home, StringComparison.OrdinalIgnoreCase)) return path;
        if (path.Length > home.Length && path[home.Length] is not ('\\' or '/')) return path;
        return "~" + path[home.Length..];
    }
}
