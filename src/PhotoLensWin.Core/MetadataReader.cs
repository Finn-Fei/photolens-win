using System.Collections.Generic;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MexDirectory = MetadataExtractor.Directory;

namespace PhotoLensWin.Core;

/// <summary>
/// EXIF 元数据的轻量读取器：方向 + 原始 directories 缓存。
/// 替代 App 层里 ImageStore.ExifHelper 的零散实现，Core/App 共用。
/// </summary>
public static class MetadataReader
{
    public static int Orientation(IReadOnlyList<MexDirectory>? directories)
    {
        if (directories is null) return 1;
        try
        {
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is not null && ifd0.TryGetInt32(0x0112, out int orientation))
                return orientation;
        }
        catch
        {
        }
        return 1;
    }

    public static int OrientationFromPath(string path)
    {
        var dirs = ExifLines.ReadDirectories(path);
        return Orientation(dirs);
    }
}
