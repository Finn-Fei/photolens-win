using PhotoLensWin.Core;
using Xunit;

namespace PhotoLensWin.Tests;

public class ExifOrientationTests
{
    // 非方形尺寸：方向 5–8 的宽高互换必须能看出来
    private const int W = 4;
    private const int H = 2;

    private static void AssertMaps(int orientation, params (double X, double Y, double EX, double EY)[] cases)
    {
        var m = ExifOrientation.Matrix(orientation, W, H);
        foreach (var (x, y, ex, ey) in cases)
        {
            Assert.Equal(ex, m.A * x + m.C * y + m.E);
            Assert.Equal(ey, m.B * x + m.D * y + m.F);
        }
    }

    [Fact]
    public void Orientation1_IsIdentity()
    {
        AssertMaps(1, (0, 0, 0, 0), (4, 0, 4, 0), (0, 2, 0, 2), (4, 2, 4, 2));
    }

    [Fact]
    public void Orientation2_MirrorsHorizontally()
    {
        AssertMaps(2, (0, 0, 4, 0), (4, 0, 0, 0), (0, 2, 4, 2), (4, 2, 0, 2));
    }

    [Fact]
    public void Orientation3_Rotates180()
    {
        AssertMaps(3, (0, 0, 4, 2), (4, 0, 0, 2), (0, 2, 4, 0), (4, 2, 0, 0));
    }

    [Fact]
    public void Orientation4_MirrorsVertically()
    {
        AssertMaps(4, (0, 0, 0, 2), (4, 0, 4, 2), (0, 2, 0, 0), (4, 2, 4, 0));
    }

    [Fact]
    public void Orientation5_Transposes()
    {
        AssertMaps(5, (0, 0, 0, 0), (4, 0, 0, 4), (0, 2, 2, 0), (4, 2, 2, 4));
    }

    [Fact]
    public void Orientation6_Rotates90Clockwise()
    {
        AssertMaps(6, (0, 0, 2, 0), (4, 0, 2, 4), (0, 2, 0, 0), (4, 2, 0, 4));
    }

    [Fact]
    public void Orientation7_MirrorsHorizontallyThenRotates270()
    {
        // 关键回归：原实现写成 (0,1,-1,0,W,0)，与方向 5 混淆，实拍方向 7 会转置错误
        AssertMaps(7, (0, 0, 2, 4), (4, 0, 2, 0), (0, 2, 0, 4), (4, 2, 0, 0));
    }

    [Fact]
    public void Orientation8_Rotates90CounterClockwise()
    {
        AssertMaps(8, (0, 0, 0, 4), (4, 0, 0, 0), (0, 2, 2, 4), (4, 2, 2, 0));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    public void SwapsSides_OnlyForOrientations5To8(int orientation, bool expected)
    {
        Assert.Equal(expected, ExifOrientation.SwapsSides(orientation));
    }
}
