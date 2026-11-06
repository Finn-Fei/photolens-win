using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MexDirectory = MetadataExtractor.Directory;

namespace PhotoLensWin.Core;

/// <summary>
/// 图片元数据展示行：像素尺寸、文件大小、机身、拍摄参数、拍摄时间、修改时间、路径。
/// 从 Mac 版 metadataLines 移植，EXIF 解码改用 MetadataExtractor（纯 .NET）。
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

    public static string[] Build(string path, int pixelWidth, int pixelHeight, DateTime modifiedUtc)
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
            var directories = ImageMetadataReader.ReadMetadata(path);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            string camera = $"{Str(ifd0, ExifDirectoryBase.TagMake)} {Str(ifd0, ExifDirectoryBase.TagModel)}".Trim();
            if (camera.Length > 0) lines.Add("机身 " + camera);

            if (subIfd is not null)
            {
                var shooting = new List<string>();
                if (subIfd.TryGetDouble(ExifDirectoryBase.TagExposureTime, out var exp) && exp > 0)
                {
                    shooting.Add(exp < 1
                        ? $"1/{(int)Math.Round(1 / exp)}s"
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

    private static string? Str(MexDirectory? dir, int tag)
    {
        if (dir is null) return null;
        try { return dir.GetString(tag); } catch { return null; }
    }
    private static string AbbreviateHome(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.Ordinal))
        {
            return "~" + path[home.Length..];
        }
        return path;
    }
}
