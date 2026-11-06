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
    public void Exposure_BlackOrWhite_ClippedToZero()
    {
        var black = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 0, 0, 0));
        Assert.Equal(0.0, Item(black, "曝光"), precision: 1);

        var white = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 255, 255, 255));
        Assert.Equal(0.0, Item(white, "曝光"), precision: 1);
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
    public void Contrast_FlatGrayIsLow_ReasonableSpreadIsHigh()
    {
        // 纯色图 std=0：对比分 10 - 50/6 ≈ 1.67（与 Mac 版一致）
        var flat = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 128, 128, 128));
        Assert.Equal(10 - 50.0 / 6, Item(flat, "对比"), precision: 1);
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
        // 纯色图两种字节序结果一致；用非对称色验证转换路径
        var rgba = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 255, 0, 0), PixelOrder.Rgba);
        var bgra = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 255, 0, 0), PixelOrder.Bgra);
        Assert.Equal(rgba.Score, bgra.Score, precision: 6);
    }

    [Fact]
    public void Lines_ProvidesSummaryText()
    {
        var report = QualityScorer.AssessQuality(320, 240, Solid(320, 240, 128, 128, 128));
        Assert.Equal(2, report.Lines.Length);
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
