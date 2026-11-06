using PhotoLensWin.Core;
using Xunit;

namespace PhotoLensWin.Tests;

public class NaturalNameComparerTests
{
    [Theory]
    [InlineData("img2.jpg", "img10.jpg")]           // 数字按数值比较
    [InlineData("IMG1.jpg", "img2.jpg")]            // 大小写不敏感
    [InlineData("图2.jpg", "图10.jpg")]              // 中文文件名数字自然序
    [InlineData("a.jpg", "b.jpg")]
    [InlineData("img1a.jpg", "img1b.jpg")]
    [InlineData("1.jpg", "2.jpg")]
    public void LessThan_ReturnsNegative(string left, string right)
    {
        Assert.True(NaturalNameComparer.Compare(left, right) < 0);
        Assert.True(NaturalNameComparer.Compare(right, left) > 0);
    }

    [Fact]
    public void EqualValues_AreEqual()
    {
        Assert.Equal(0, NaturalNameComparer.Compare("img1.jpg", "img1.jpg"));
        Assert.Equal(0, NaturalNameComparer.Compare("IMG1.jpg", "img1.jpg"));
    }

    [Fact]
    public void NumericRuns_WithDifferentWidths_CompareByValue()
    {
        Assert.True(NaturalNameComparer.Compare("pic0002.jpg", "pic10.jpg") < 0);
    }

    [Fact]
    public void MixedDigitsAndLetters_SortNaturally()
    {
        var files = new[] { "10.jpg", "2.jpg", "1.jpg", "img3.jpg", "img21.jpg", "img1.jpg" };
        var sorted = new List<string>(files);
        sorted.Sort(NaturalNameComparer.Compare);
        Assert.Equal(
            new[] { "1.jpg", "2.jpg", "10.jpg", "img1.jpg", "img3.jpg", "img21.jpg" },
            sorted);
    }

    [Fact]
    public void Null_IsLessThanAny()
    {
        Assert.True(NaturalNameComparer.Compare(null, "a.jpg") < 0);
        Assert.True(NaturalNameComparer.Compare("a.jpg", null) > 0);
        Assert.Equal(0, NaturalNameComparer.Compare(null, null));
    }
}
