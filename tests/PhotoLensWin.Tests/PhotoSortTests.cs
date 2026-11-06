using System;
using System.Collections.Generic;
using PhotoLensWin.Core;
using Xunit;

namespace PhotoLensWin.Tests;

public class PhotoSortTests
{
    private static ImageEntry Entry(string name, long size = 100, DateTime? modified = null) =>
        new($"/tmp/{name}", name, size, modified ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void ThereAreSixRules_WithChineseTitles()
    {
        var sorts = Enum.GetValues<PhotoSort>();
        Assert.Equal(6, sorts.Length);
        foreach (var sort in sorts)
        {
            var title = sort.Title();
            Assert.False(string.IsNullOrWhiteSpace(title));
            Assert.Contains("→", title); // 全部形如「名称 A → Z」
        }
    }

    [Fact]
    public void NameAscending_IsNaturalOrder()
    {
        var sorted = PhotoSort.NameAscending.Apply(new[]
        {
            Entry("b10.jpg"), Entry("b2.jpg"), Entry("a1.jpg"),
        });
        Assert.Equal(new[] { "a1.jpg", "b2.jpg", "b10.jpg" },
                     MapNames(sorted));
    }

    [Fact]
    public void NameDescending_Reverses()
    {
        var sorted = PhotoSort.NameDescending.Apply(new[]
        {
            Entry("a1.jpg"), Entry("b2.jpg"), Entry("b10.jpg"),
        });
        Assert.Equal(new[] { "b10.jpg", "b2.jpg", "a1.jpg" },
                     MapNames(sorted));
    }

    [Fact]
    public void DateNewest_PutsNewFirst_WithNaturalNameTiebreak()
    {
        var old = Entry("old.jpg", modified: Utc(2025, 5, 1));
        var new1 = Entry("新.jpg", modified: Utc(2026, 3, 2));
        var new2 = Entry("新2.jpg", modified: Utc(2026, 3, 2));
        var sorted = PhotoSort.DateNewest.Apply(new[] { old, new2, new1 });
        Assert.Equal(new[] { "新.jpg", "新2.jpg", "old.jpg" }, MapNames(sorted));
    }

    [Fact]
    public void DateOldest_PutsOldFirst()
    {
        var old = Entry("old.jpg", modified: Utc(2025, 5, 1));
        var new1 = Entry("新.jpg", modified: Utc(2026, 3, 2));
        var sorted = PhotoSort.DateOldest.Apply(new[] { new1, old });
        Assert.Equal(new[] { "old.jpg", "新.jpg" }, MapNames(sorted));
    }

    [Fact]
    public void SizeLarge_PutsBigFirst_WithNaturalNameTiebreak()
    {
        var small = Entry("s.jpg", size: 10);
        var big1 = Entry("大10.jpg", size: 1000);
        var big2 = Entry("大2.jpg", size: 1000);
        var sorted = PhotoSort.SizeLarge.Apply(new[] { small, big2, big1 });
        // 同大小按文件名自然序兜底：大2 在 大10 前
        Assert.Equal(new[] { "大2.jpg", "大10.jpg", "s.jpg" }, MapNames(sorted));
    }

    [Fact]
    public void SizeSmall_PutsTinyFirst()
    {
        var small = Entry("s.jpg", size: 10);
        var big = Entry("big.jpg", size: 1000);
        var sorted = PhotoSort.SizeSmall.Apply(new[] { big, small });
        Assert.Equal(new[] { "s.jpg", "big.jpg" }, MapNames(sorted));
    }

    [Fact]
    public void Sort_IsDeterministic_RegardlessOfInputOrder()
    {
        var entries = new[]
        {
            Entry("c.jpg"), Entry("a10.jpg"), Entry("a2.jpg"), Entry("b.jpg"),
        };
        var first = PhotoSort.NameAscending.Apply(entries);
        var second = PhotoSort.NameAscending.Apply(entries);
        Assert.Equal(MapNames(first), MapNames(second));
    }

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static string[] MapNames(IReadOnlyList<ImageEntry> entries)
    {
        var names = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++) names[i] = entries[i].Name;
        return names;
    }
}
