using System;
using System.IO;
using PhotoLensWin.Core;
using Xunit;

namespace PhotoLensWin.Tests;

public class ImageCatalogTests : IDisposable
{
    private readonly string _root;

    public ImageCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "photolenswin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Touch(params string[] relative)
    {
        var path = Path.Combine(new[] { _root }.Concat(relative).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void CollectImages_RecursesSubFolders()
    {
        var a = Touch("1.jpg");
        var b = Touch("sub", "2.png");
        var c = Touch("sub", "deep", "3.bmp");
        Touch("notes.txt"); // 非图片应被过滤

        var result = ImageCatalog.CollectImages(_root);
        Assert.Equal(3, result.Count);
        Assert.Equal(
            new[] { a, b, c }, // 名称 A→Z：1.jpg < 2.png < 3.bmp
            result.ConvertAll(e => e.FullPath));
    }

    [Fact]
    public void CollectImages_SkipsHiddenDotFiles()
    {
        Touch(".hidden.jpg");
        var visible = Touch("visible.jpg");
        var result = ImageCatalog.CollectImages(_root);
        Assert.Single(result);
        Assert.Equal(visible, result[0].FullPath);
    }

    [Fact]
    public void CollectImages_IsCaseInsensitiveOnExtensions()
    {
        var upper = Touch("A.JPG");
        var lower = Touch("b.jpg");
        var result = ImageCatalog.CollectImages(_root);
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { upper, lower }, result.ConvertAll(e => e.FullPath));
    }

    [Fact]
    public void CollectFromPaths_MixesFilesAndFolders()
    {
        var inFolder = Touch("sub", "2.png");
        var single = Touch("solo.jpg");
        var ignored = Touch("skip.txt");

        var result = ImageCatalog.CollectFromPaths(new[] { single, _root, ignored });
        Assert.Contains(single, result.ConvertAll(e => e.FullPath));
        Assert.Contains(inFolder, result.ConvertAll(e => e.FullPath));
        Assert.DoesNotContain(ignored, result.ConvertAll(e => e.FullPath));
    }

    [Fact]
    public void CollectFromPaths_DeduplicatesFileAndItsParentFolder()
    {
        var file = Touch("dup.jpg");
        var result = ImageCatalog.CollectFromPaths(new[] { file, _root });
        Assert.Single(result);
        Assert.Equal(file, result[0].FullPath);
    }

    [Fact]
    public void StartIndexFor_LocatesCurrentFile_AndFallsBackToZero()
    {
        var entries = new List<ImageEntry>
        {
            new("/x/a.jpg", "a.jpg", 1, DateTime.UtcNow),
            new("/x/b.jpg", "b.jpg", 1, DateTime.UtcNow),
            new("/x/c.jpg", "c.jpg", 1, DateTime.UtcNow),
        };
        Assert.Equal(1, ImageCatalog.StartIndexFor(entries, "/x/b.jpg"));
        Assert.Equal(0, ImageCatalog.StartIndexFor(entries, "/x/missing.jpg"));
        Assert.Equal(0, ImageCatalog.StartIndexFor(entries, null));
        Assert.Equal(0, ImageCatalog.StartIndexFor(new List<ImageEntry>(), "/x/a.jpg"));
    }

    [Fact]
    public void WindowsSupportedExtensions_ExcludeHeicFamily()
    {
        Assert.Contains("jpg", ImageCatalog.ImageExtensions);
        Assert.DoesNotContain(ImageCatalog.ImageExtensions, e => e is "heic" or "heif" or "avif");
    }
}
