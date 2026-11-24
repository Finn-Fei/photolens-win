using PhotoLensWin.Core;
using Xunit;

namespace PhotoLensWin.Tests;

/// <summary>
/// 显示方向复合（EXIF 方向 × 用户旋转）—— 原来只住在 ImageViewer 私有方法里，
/// 无法单测；1.2.6 的"旋转偏移"回归就发生在这块，现在钉住。
/// </summary>
public class DisplayOrientationTests
{
    // 4×2 的非方形尺寸：方向 5–8 的宽高互换必须能看出来
    private const double W = 4;
    private const double H = 2;

    [Fact]
    public void Identity_ForOrientation1_WithoutUserRotation()
    {
        var (m, w, h) = DisplayOrientation.Compose(1, 0, W, H);
        Assert.Equal(W, w);
        Assert.Equal(H, h);
        AssertMap(m, 0, 0, 0, 0);
        AssertMap(m, W, 0, W, 0);
        AssertMap(m, 0, H, 0, H);
    }

    [Fact]
    public void Orientation6_SwapsSides()
    {
        var (_, w, h) = DisplayOrientation.Compose(6, 0, W, H);
        Assert.Equal(H, w);
        Assert.Equal(W, h);
    }

    [Fact]
    public void UserRotation_ComposesOnTopOfExifOrientation()
    {
        // 方向 6 已摆正为 H×W，再顺时针转 1 圈 → 回到 W×H
        var (_, w1, h1) = DisplayOrientation.Compose(6, 1, W, H);
        Assert.Equal(W, w1);
        Assert.Equal(H, h1);

        // 四圈等于没转
        var (_, w4, h4) = DisplayOrientation.Compose(6, 4, W, H);
        Assert.Equal(H, w4);
        Assert.Equal(W, h4);
    }

    [Fact]
    public void NegativeTurns_AreNormalised()
    {
        var (_, wPos, hPos) = DisplayOrientation.Compose(1, 3, W, H);
        var (_, wNeg, hNeg) = DisplayOrientation.Compose(1, -1, W, H);
        Assert.Equal(wPos, wNeg);
        Assert.Equal(hPos, hNeg);
    }

    [Fact]
    public void ComposedMatrix_AlwaysMapsCornersInsideDisplayBox()
    {
        foreach (int orientation in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            for (int turns = 0; turns < 4; turns++)
            {
                var (m, dw, dh) = DisplayOrientation.Compose(orientation, turns, W, H);
                foreach (var (x, y) in new[] { (0.0, 0.0), (W, 0.0), (0.0, H), (W, H) })
                {
                    double px = m.A * x + m.C * y + m.E;
                    double py = m.B * x + m.D * y + m.F;
                    Assert.InRange(px, -1e-9, dw + 1e-9);
                    Assert.InRange(py, -1e-9, dh + 1e-9);
                }
            }
        }
    }

    private static void AssertMap(
        Affine m, double x, double y, double expectedX, double expectedY)
    {
        Assert.Equal(expectedX, m.A * x + m.C * y + m.E, precision: 9);
        Assert.Equal(expectedY, m.B * x + m.D * y + m.F, precision: 9);
    }
}
