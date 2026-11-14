using PhotoLensWin.Core;
using Xunit;

namespace PhotoLensWin.Tests;

public class ViewTransformTests
{
    private static ViewTransform Make(double viewW, double viewH, double imgW, double imgH) =>
        new(viewW, viewH, imgW, imgH);

    [Fact]
    public void FitScale_UpscalesSmallImages_LikeSystemPreview()
    {
        // 对齐 Mac 版：小图也放大铺满可用区域（不受 1 倍上限约束）
        var t = Make(800, 600, 100, 100);
        Assert.Equal((600 - 2 * ViewTransform.FitPadding) / 100.0, t.FitScale, precision: 6);
    }

    [Fact]
    public void FitScale_LeavesPadding_OnBothSides()
    {
        // 视口 800×600、图片 800×600：可用区要扣掉四周 FitPadding，避免把常量硬编码进断言
        var t = Make(800, 600, 800, 600);
        double expected = (600 - 2 * ViewTransform.FitPadding) / 600.0;
        Assert.Equal(expected, t.FitScale, precision: 6);

        // 等比缩放后只有"受限边"的留白等于 FitPadding，另一侧会按比例多留一点
        t.Fit();
        var r = t.ImageRect;
        Assert.Equal(ViewTransform.FitPadding, r.Y, precision: 6);
        Assert.Equal(600 - 2 * ViewTransform.FitPadding, r.Height, precision: 6);
        Assert.Equal((800 - 800 * expected) / 2, r.X, precision: 6);
        Assert.Equal(800 * expected, r.Width, precision: 6);
        Assert.True(r.X >= ViewTransform.FitPadding);
    }

    [Fact]
    public void Fit_CentersAndResetsOffset()
    {
        var t = Make(100, 100, 50, 50);
        t.ZoomTo(3, 50, 50);   // 放大到超出视口，平移才有空间
        t.Pan(5, 5);           // 制造非零偏移
        Assert.NotEqual(0, t.ImageRect.X, 3); // 先确认偏移真的生效，否则下面的重置断言没有意义
        t.Fit();
        // 50×50 的小图会放大到铺满可用区：scale = (100 - 2*padding) / 50
        double scale = (100 - 2 * ViewTransform.FitPadding) / 50.0;
        Assert.Equal(scale, t.Scale, precision: 6);
        var r = t.ImageRect;
        double expectedSide = 50 * scale;
        Assert.Equal(expectedSide, r.Width, precision: 6);
        Assert.Equal(expectedSide, r.Height, precision: 6);
        Assert.Equal(50 - expectedSide / 2, r.X, precision: 6);
        Assert.Equal(50 - expectedSide / 2, r.Y, precision: 6);
    }

    [Fact]
    public void ZoomTo_AnchorKeepsImagePointSteady()
    {
        // 视口 100×100、图片 100×100：以 (75,50) 为锚放大 2 倍
        // 锚点下方图像左缘坐标 75 的点，缩放后仍应位于视图 (75,50)
        var t = Make(100, 100, 100, 100);
        t.ZoomTo(2, 75, 50);
        var rect = t.ImageRect;
        double screenX = rect.X + 75 * 2;
        double screenY = rect.Y + 50 * 2;
        Assert.Equal(75, screenX, precision: 6);
        Assert.Equal(50, screenY, precision: 6);
    }

    [Fact]
    public void ZoomTo_IsClampedToMinAndMax()
    {
        var t = Make(100, 100, 100, 100);
        t.ZoomTo(1000, 50, 50);
        Assert.Equal(ViewTransform.MaxScale, t.Scale, precision: 6);
        t.ZoomTo(0.00001, 50, 50);
        Assert.Equal(ViewTransform.MinScale, t.Scale, precision: 6);
    }

    [Fact]
    public void Pan_AccumulatesAndClamps()
    {
        var t = Make(100, 100, 200, 200);
        t.Pan(30, 0);
        t.Pan(30, 0); // 位移累加，不是每次重置
        Assert.Equal(50, t.OffsetX, precision: 6); // 限位 (200-100)/2 = 50
        t.Pan(-500, 0);
        Assert.Equal(-50, t.OffsetX, precision: 6);
        t.Pan(0, 500);
        Assert.Equal(50, t.OffsetY, precision: 6);
    }

    [Fact]
    public void Pan_IsZero_WhenImageSmallerThanView()
    {
        var t = Make(800, 600, 100, 100);
        t.SetScale(2, resetOffset: false);
        t.Pan(100, 100);
        Assert.Equal(0, t.OffsetX, precision: 6);
        Assert.Equal(0, t.OffsetY, precision: 6);
    }

    [Fact]
    public void ImageRect_IsCentered_WhenOffsetZero()
    {
        var t = Make(100, 100, 60, 40);
        var rect = t.ImageRect;
        Assert.Equal(20, rect.X, precision: 6);
        Assert.Equal(30, rect.Y, precision: 6);
    }

    [Fact]
    public void DegenerateImage_ResetsOffset()
    {
        var t = Make(100, 100, 0, 0);
        t.SetScale(5, resetOffset: false);
        t.Pan(10, 10);
        var rect = t.ImageRect;
        Assert.Equal(0, rect.Width, precision: 6);
        Assert.Equal(0, rect.Height, precision: 6);
        Assert.Equal(0, t.OffsetX, precision: 6);
        Assert.Equal(0, t.OffsetY, precision: 6);
    }
}
