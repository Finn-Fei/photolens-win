using System;
using System.IO;
using System.Linq;
using PhotoLensWin.Core;
using Xunit;

namespace PhotoLensWin.Tests;

public class SettingsAndFormatTests : IDisposable
{
    private readonly string _path;

    public SettingsAndFormatTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "photolenswin-tests",
            Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var settings = SettingsStore.Load(_path);
        Assert.Equal(nameof(PhotoSort.NameAscending), settings.SortRule);
        Assert.True(settings.ShowFilmstrip);
        Assert.Null(settings.LastFolder);
    }

    [Fact]
    public void CorruptFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not valid json !!!");
        var settings = SettingsStore.Load(_path);
        Assert.Equal(nameof(PhotoSort.NameAscending), settings.SortRule);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var settings = new AppSettings
        {
            SortRule = nameof(PhotoSort.DateNewest),
            ShowFilmstrip = false,
            LastFolder = "/tmp/photos",
        };
        SettingsStore.Save(settings, _path);

        var loaded = SettingsStore.Load(_path);
        Assert.Equal(nameof(PhotoSort.DateNewest), loaded.SortRule);
        Assert.False(loaded.ShowFilmstrip);
        Assert.Equal("/tmp/photos", loaded.LastFolder);
    }

    [Fact]
    public void SortRule_FromName_Parses()
    {
        Assert.Equal(PhotoSort.SizeLarge, PhotoSortExtensions.FromName("SizeLarge"));
        Assert.Null(PhotoSortExtensions.FromName("nonsense"));
        Assert.Null(PhotoSortExtensions.FromName(null));
    }

    [Theory]
    [InlineData(500, "1 KB")]
    [InlineData(5000, "5 KB")]
    [InlineData(1024, "1 KB")]
    [InlineData(1_500_000, "1.4 MB")]
    [InlineData(5_000_000, "4.8 MB")]
    public void SizeText_FormatsKbAndMb(long bytes, string expected)
    {
        Assert.Equal(expected, ExifLines.SizeText(bytes));
    }

    // ── 最近文件夹：上限 / 去重 / 顺序 ──────────────────────────────

    [Fact]
    public void TouchRecentFolder_KeepsCapAndMovesToFront()
    {
        var settings = new AppSettings();
        for (int i = 0; i < 15; i++) SettingsStore.TouchRecentFolder(settings, "C:\\photos\\" + i);

        Assert.Equal(AppSettings.MaxRecentFolders, settings.RecentFolders.Count);
        Assert.Equal("C:\\photos\\14", settings.RecentFolders[0]);
    }

    [Fact]
    public void TouchRecentFolder_DedupesCaseInsensitively()
    {
        var settings = new AppSettings();
        SettingsStore.TouchRecentFolder(settings, "C:\\photos");
        SettingsStore.TouchRecentFolder(settings, "c:\\PHOTOS");

        Assert.Single(settings.RecentFolders);
        Assert.Equal("c:\\PHOTOS", settings.RecentFolders[0]);
    }

    [Fact]
    public void Load_TruncatesDedupesAndDropsEmptyRecentFolders()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path,
            "{\"RecentFolders\":[\"a\",\"A\",\"\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\",\"h\",\"i\",\"j\",\"k\"]}");

        var settings = SettingsStore.Load(_path);
        Assert.Equal(AppSettings.MaxRecentFolders, settings.RecentFolders.Count);
        Assert.Equal(new[] { "a", "b", "c" }, settings.RecentFolders.Take(3).ToArray());
    }

    [Fact]
    public void Load_OversizedFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, new string('x', (int)AppSettings.MaxSettingsFileBytes + 1));

        var settings = SettingsStore.Load(_path);
        Assert.Equal(nameof(PhotoSort.NameAscending), settings.SortRule);
        Assert.Empty(settings.RecentFolders);
    }

    // ── 主目录缩写：大小写不敏感 + 目录边界 ────────────────────────

    [Theory]
    [InlineData(@"C:\Users\Bob\Pictures\a.jpg", @"C:\Users\Bob", @"~\Pictures\a.jpg")]
    [InlineData(@"c:\users\bob\Pictures\a.jpg", @"C:\Users\Bob", @"~\Pictures\a.jpg")] // 大小写不敏感
    [InlineData(@"C:\Users\Bob2\a.jpg", @"C:\Users\Bob", @"C:\Users\Bob2\a.jpg")]        // 边界：Bob2 不该被缩写
    [InlineData(@"C:\Users\Bob", @"C:\Users\Bob", "~")]
    [InlineData(@"D:\x\a.jpg", @"C:\Users\Bob", @"D:\x\a.jpg")]
    public void AbbreviateHome_IsCaseInsensitiveAndBoundaryAware(string path, string home, string expected)
    {
        Assert.Equal(expected, ExifLines.AbbreviateHome(path, home));
    }
}
