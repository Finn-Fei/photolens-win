using System;
using System.IO;
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
}
