using PhotoLensWin.Core;
using Xunit;

namespace PhotoLensWin.Tests;

public class QualityScorerTests
{
    /// <summary>构造纯色图（RGBA）</summary>
    private static byte[] Solid(int w, int h, byte r, byte g, byte b)
    {
        var pixels = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            pixels[i * 4] = r;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = b;
            pixels[i * 4 + 3] = 255;
        }
        return pixels;
    }

    private static byte[] Checkerboard(int w, int h, int cell = 4)
    {
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool white = ((x / cell) + (y / cell)) % 2 == 0;
                byte v = white ? (byte)255 : (byte)0;
                int o = (y * w + x) * 4;
                pixels[o] = v;
                pixels[o + 1] = v;
                pixels[o + 2] = v;
                pixels[o + 3] = 255;
            }
        }
        return pixels;
    }

    /// <summary>左半 / 右半两种颜色的图（按 RGBA 字节序写入）</summary>
    private static byte[] Halves(int w, int h, (byte R, byte G, byte B) left, (byte R, byte G, byte B) right)
    {
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = x < w / 2 ? left : right;
                int o = (y * w + x) * 4;
                pixels[o] = c.R;
                pixels[o + 1] = c.G;
                pixels[o + 2] = c.B;
                pixels[o + 3] = 255;
            }
        }
        return pixels;
    }

    private static double Item(QualityReport report, string name)
    {
        foreach (var item in report.Items)
        {
            if (item.Name == name) return item.Score;
        }
        throw new KeyNotFoundException(name);
    }

    [Fact]
    public void Report_HasFiveItems_WithKnownNames()
    {
        var report = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 128, 128, 128));
        Assert.Equal(5, report.Items.Length);
        Assert.Equal(new[] { "分辨率", "锐度", "曝光", "对比", "色彩" },
                     report.Items.Select(i => i.Name).ToArray());
        Assert.InRange(report.Score, 0, 10);
    }

    [Fact]
    public void Resolution_ScalesWithMinSide()
    {
        var mid = QualityScorer.AssessQuality(800, 800, Solid(800, 800, 100, 100, 100));
        Assert.Equal(5.0, Item(mid, "分辨率"), precision: 2); // 800/1600*10

        var full = QualityScorer.AssessQuality(1600, 1600, Solid(1600, 1600, 100, 100, 100));
        Assert.Equal(10.0, Item(full, "分辨率"), precision: 2);
    }

    [Fact]
    public void Exposure_DarkButUnclipped_NightSceneStaysHigh()
    {
        // 关键回归：夜景整体偏暗是正常表现，均值暗不应惩罚曝光分
        // 亮度 30（RGB 30,30,30）不触顶也不死黑 → 曝光分应为满分
        var night = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 30, 30, 30));
        Assert.Equal(10.0, Item(night, "曝光"), precision: 1);
    }

    [Fact]
    public void Exposure_CrushedBlackIsZero_ClippedWhiteIsCappedAtFour()
    {
        // 暗部死黑按比例重罚：全黑 → 0
        var black = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 0, 0, 0));
        Assert.Equal(0.0, Item(black, "曝光"), precision: 1);

        // 高光侧封顶 4 分（Mac 版行为）：白底/雪景/文档扫描不该被判成 0
        var white = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 255, 255, 255));
        Assert.Equal(6.0, Item(white, "曝光"), precision: 1);
    }

    [Fact]
    public void Resolution_UsesOriginalSize_NotStatsBufferSize()
    {
        // 传给像素统计的缓冲只有 32×24，但原图 1600×1600 → 分辨率分必须按原图算
        var report = QualityScorer.AssessQuality(
            1600, 1600, Solid(32, 24, 128, 128, 128), PixelOrder.Rgba, 32, 24);
        Assert.Equal(10.0, Item(report, "分辨率"), precision: 2);
    }

    [Fact]
    public void TransparentPixels_AreExcludedFromStatistics()
    {
        // 全透明图没有任何可见像素 → 像素统计不可用，其余项回退为中性 5 分，
        // 而不是把透明区当成死黑/纯色扣分（对齐 Mac 版 alpha < 26 的过滤）。
        var pixels = new byte[320 * 240 * 4];
        var report = QualityScorer.AssessQuality(320, 240, pixels);
        Assert.Equal(5.0, Item(report, "曝光"), precision: 6);
        Assert.Equal(5.0, Item(report, "对比"), precision: 6);
        Assert.Equal(5.0, Item(report, "锐度"), precision: 6);
        Assert.Equal(5.0, Item(report, "色彩"), precision: 6);
    }

    [Fact]
    public void Sharpness_FlatIsZero_CheckerIsTen()
    {
        var flat = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 128, 128, 128));
        Assert.Equal(0.0, Item(flat, "锐度"), precision: 1);

        var sharp = QualityScorer.AssessQuality(320, 240, Checkerboard(320, 240));
        Assert.Equal(10.0, Item(sharp, "锐度"), precision: 1);
    }

    [Fact]
    public void Contrast_PrefersMidSpread_ExtremeSpreadIsPenalized()
    {
        // 纯色图 std=0：对比分 10 - 50/6 ≈ 1.67（与 Mac 版一致）
        var flat = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 128, 128, 128));
        Assert.Equal(10 - 50.0 / 6, Item(flat, "对比"), precision: 1);

        // 公式 10 - |σ-50|/6 偏好"中等反差"：双色灰阶 77/179 的 σ≈51，接近满分
        var spread = QualityScorer.AssessQuality(320, 240, Halves(320, 240, (77, 77, 77), (179, 179, 179)));
        Assert.True(Item(spread, "对比") > 9, $"中等反差应接近满分，实际 {Item(spread, "对比")}");

        // 极端反差（黑白棋盘 σ≈127.5）被公式压到 0——这是 Mac 版既有行为，显式钉住防回归
        var extreme = QualityScorer.AssessQuality(320, 240, Checkerboard(320, 240));
        Assert.Equal(0.0, Item(extreme, "对比"), precision: 6);
    }

    [Fact]
    public void Color_AchromaticScoresAgainstThirtyPercentTarget()
    {
        var gray = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 128, 128, 128));
        Assert.Equal(10 - 0.30 / 0.08, Item(gray, "色彩"), precision: 1); // 6.25
    }

    [Fact]
    public void BgraOrder_IsHandledCorrectly()
    {
        // 纯色图无法区分字节序（std/饱和度与通道顺序无关），必须用亮度分布不同的非均匀图：
        // 左半红、右半绿，Rgba 与 Bgra 下亮度集合不同 → σ 不同 → 对比分必然不同。
        // 若 order 参数被忽略，两者分数会完全相同，本断言即可拦截。
        var pixels = Halves(320, 240, (255, 0, 0), (0, 255, 0));
        var rgba = QualityScorer.AssessQuality(320, 240, pixels, PixelOrder.Rgba);
        var bgra = QualityScorer.AssessQuality(320, 240, pixels, PixelOrder.Bgra);
        Assert.NotEqual(rgba.Score, bgra.Score, precision: 3);

        // 同色不同字节序：语义应等价（Rgba 的红/绿 == Bgra 字节序下同一组红/绿）
        var redGreenRgba = QualityScorer.AssessQuality(320, 240, pixels, PixelOrder.Rgba);
        var redGreenBgra = QualityScorer.AssessQuality(
            320, 240, Halves(320, 240, (0, 0, 255), (0, 255, 0)), PixelOrder.Bgra);
        Assert.Equal(redGreenRgba.Score, redGreenBgra.Score, precision: 6);
    }

    [Fact]
    public void Lines_ProvidesSummaryText()
    {
        var report = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 128, 128, 128));
        Assert.Equal(3, report.Lines.Length);
        Assert.StartsWith("画质评分", report.Lines[0]);
        Assert.Contains("分辨率", report.Lines[1]);
    }

    [Fact]
    public void Lines_EmptyItems_ReportsUnavailable()
    {
        // 评分失败的回退值：空条目时只显示「不可用」，不显示误导性的低分
        var report = new QualityReport(0, System.Array.Empty<QualityItem>());
        Assert.Single(report.Lines);
        Assert.Contains("不可用", report.Lines[0]);
    }
}
