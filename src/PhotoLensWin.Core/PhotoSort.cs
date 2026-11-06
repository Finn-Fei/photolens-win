using System;
using System.Collections.Generic;

namespace PhotoLensWin.Core;

/// <summary>图片文件条目：排序所需的元数据快照。</summary>
public sealed record ImageEntry(string FullPath, string Name, long Size, DateTime ModifiedUtc)
{
    public ImageEntry(string fullPath)
        : this(fullPath,
               System.IO.Path.GetFileName(fullPath),
               0, DateTime.MinValue) { }
}

/// <summary>
/// 排序规则：6 种，与 Mac 版 PhotoLens 完全一致。
/// 同值时按文件名自然排序兜底，保证顺序稳定不抖动。
/// </summary>
public enum PhotoSort
{
    NameAscending,
    NameDescending,
    DateNewest,
    DateOldest,
    SizeLarge,
    SizeSmall,
}

public static class PhotoSortExtensions
{
    public static string Title(this PhotoSort sort) => sort switch
    {
        PhotoSort.NameAscending => "名称 A → Z",
        PhotoSort.NameDescending => "名称 Z → A",
        PhotoSort.DateNewest => "修改时间 新 → 旧",
        PhotoSort.DateOldest => "修改时间 旧 → 新",
        PhotoSort.SizeLarge => "文件大小 大 → 小",
        PhotoSort.SizeSmall => "文件大小 小 → 大",
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };

    public static PhotoSort? FromName(string? name) =>
        Enum.TryParse<PhotoSort>(name, out var sort) ? sort : null;

    public static List<ImageEntry> Apply(this PhotoSort sort, IEnumerable<ImageEntry> entries)
    {
        var list = new List<ImageEntry>(entries);
        list.Sort((x, y) => sort switch
        {
            PhotoSort.NameAscending => ByName(x, y, descending: false),
            PhotoSort.NameDescending => ByName(x, y, descending: true),
            PhotoSort.DateNewest => Compare(x.ModifiedUtc, y.ModifiedUtc, x, y, descending: true),
            PhotoSort.DateOldest => Compare(x.ModifiedUtc, y.ModifiedUtc, x, y, descending: false),
            PhotoSort.SizeLarge => Compare(x.Size, y.Size, x, y, descending: true),
            PhotoSort.SizeSmall => Compare(x.Size, y.Size, x, y, descending: false),
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        });
        return list;
    }

    private static int Compare<TValue>(TValue a, TValue b, ImageEntry ea, ImageEntry eb, bool descending)
        where TValue : IComparable<TValue>
    {
        int cmp = a.CompareTo(b);
        return cmp != 0 ? (descending ? -cmp : cmp) : ByName(ea, eb, descending: false);
    }

    /// <summary>
    /// 文件名自然比较；同名（不同子目录）时用完整路径兜底。
    /// List.Sort 不稳定，缺少最终兜底会让同名条目在每次切换排序后位置抖动。
    /// </summary>
    private static int ByName(ImageEntry a, ImageEntry b, bool descending)
    {
        int cmp = NaturalNameComparer.Compare(a.Name, b.Name);
        if (descending) cmp = -cmp;
        return cmp != 0 ? cmp : string.CompareOrdinal(a.FullPath, b.FullPath);
    }
}
