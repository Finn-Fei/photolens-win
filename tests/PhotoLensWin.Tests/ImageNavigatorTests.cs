using PhotoLensWin.Core;
using Xunit;

namespace PhotoLensWin.Tests;

/// <summary>
/// 图片列表下标运算（上下张步进 / 边界钳制 / 幻灯片回绕）——
/// 从 MainWindow 抽到 Core 后第一次获得单测覆盖。
/// </summary>
public class ImageNavigatorTests
{
    [Theory]
    [InlineData(3, 0, -1, false)]
    [InlineData(3, 0, 1, true)]
    [InlineData(3, 2, 1, false)]
    [InlineData(3, 2, -1, true)]
    [InlineData(1, 0, -1, false)]
    [InlineData(1, 0, 1, false)]
    [InlineData(0, 0, 1, false)]
    public void CanStep_RespectsBounds(int count, int index, int direction, bool expected)
    {
        Assert.Equal(expected, ImageNavigator.CanStep(count, index, direction));
    }

    [Theory]
    [InlineData(-5, 3, 0)]
    [InlineData(0, 3, 0)]
    [InlineData(1, 3, 1)]
    [InlineData(2, 3, 2)]
    [InlineData(99, 3, 2)]
    [InlineData(0, 0, -1)]
    public void ClampIndex_ClampsIntoRange(int index, int count, int expected)
    {
        Assert.Equal(expected, ImageNavigator.ClampIndex(index, count));
    }

    [Theory]
    [InlineData(0, 3, -1, 2)] // 第一张往前 → 最后一张
    [InlineData(2, 3, 1, 0)]  // 最后一张往后 → 第一张
    [InlineData(1, 3, 1, 2)]
    [InlineData(0, 1, 1, 0)]
    [InlineData(0, 0, 1, -1)]
    public void WrapStep_WrapsAround(int index, int count, int direction, int expected)
    {
        Assert.Equal(expected, ImageNavigator.WrapStep(index, count, direction));
    }
}
