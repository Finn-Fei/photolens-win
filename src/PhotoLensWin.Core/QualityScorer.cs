using System;

namespace PhotoLensWin.Core;

public sealed record QualityItem(string Name, double Score);

public sealed record QualityReport(double Score, QualityItem[] Items)
{
    /// <summary>信息面板里的两行摘要</summary>
    public string[] Lines
    {
        get
        {
            if (Items.Length == 0)
            {
                // 评分不可用（解码统计失败）时不要输出误导性的 0 分条目
                return new[] { "画质评分 不可用" };
            }
            string parts = string.Join(" · ", Items.Select(i => $"{i.Name} {i.Score:F1}"));
            return new[]
            {
                $"画质评分 {Score:F1} / 10（基于像素统计）",
                "  " + parts,
                "  注：非 AI 评分，锐度/曝光/对比/色彩加权",
            };
        }
    }
}

public enum PixelOrder
{
    Rgba,
    Bgra,
}

/// <summary>
/// 规则型画质评分：分辨率 / 锐度 / 曝光 / 对比 / 色彩，全部由像素统计得出。
/// 从 Mac 版 assessQuality 逐行移植；夜景图偏暗是正常表现，
/// 曝光按「高光溢出 / 暗部死黑」判，而不是平均亮度。
/// </summary>
public static class QualityScorer
{
    private const int StatsMaxSide = 320;

    public static QualityReport AssessQuality(int width, int height, byte[] pixels, PixelOrder order = PixelOrder.Rgba)
        => AssessQuality(width, height, pixels, order, width, height);

    /// <summary>
    /// 像素统计用缩小后的缓冲，分辨率分必须用原图尺寸——否则高分辨率图会被判成低分。
    /// </summary>
    public static QualityReport AssessQuality(
        int imageWidth, int imageHeight, byte[] pixels, PixelOrder order, int pixelWidth, int pixelHeight)
    {
        double minSide = Math.Min(imageWidth, imageHeight);
        double resolutionScore = Math.Min(10.0, minSide / 1600.0 * 10.0);

        double contrastScore = 5.0;
        double exposureScore = 5.0;
        double sharpnessScore = 5.0;
        double colorScore = 5.0;

        var stats = PixelStatistics(pixelWidth, pixelHeight, pixels, order);
        if (stats is not null)
        {
            double std = stats.Value.Std;
            // 高光侧封顶 4 分：白底产品图、文档扫描、雪景的大面积高光不该把曝光直接判成 0
            exposureScore = Math.Clamp(
                10 - Math.Min(4.0, Math.Max(0, stats.Value.ClippedHigh - 0.02) * 40)
                  - Math.Max(0, stats.Value.ClippedLow - 0.08) * 40,
                0, 10);
            contrastScore = Math.Clamp(10 - Math.Abs(std - 50) / 6, 0, 10);
            sharpnessScore = Math.Clamp(stats.Value.LaplacianVariance / 1000 * 10, 0, 10);
            colorScore = Math.Clamp(10 - Math.Abs(stats.Value.Saturation - 0.30) / 0.08, 0, 10);
        }

        double total = 0.30 * sharpnessScore + 0.20 * exposureScore + 0.20 * contrastScore
            + 0.18 * resolutionScore + 0.12 * colorScore;
        var items = new[]
        {
            new QualityItem("分辨率", resolutionScore),
            new QualityItem("锐度", sharpnessScore),
            new QualityItem("曝光", exposureScore),
            new QualityItem("对比", contrastScore),
            new QualityItem("色彩", colorScore),
        };
        return new QualityReport(total, items);
    }

    private static (double Std, double ClippedHigh, double ClippedLow, double LaplacianVariance, double Saturation)?
        PixelStatistics(int width, int height, byte[] pixels, PixelOrder order)
    {
        if (width <= 0 || height <= 0 || pixels.LongLength < (long)width * height * 4) return null;

        // 缩到最长边 320px 后统计亮度/对比/锐度/饱和度，避免在大图上做全量计算
        double ratio = Math.Min(1.0, StatsMaxSide / (double)Math.Max(width, height));
        int w = Math.Max(16, (int)(width * ratio));
        int h = Math.Max(16, (int)(height * ratio));

        var luminance = new double[w * h];
        var visible = new bool[w * h];
        double saturationSum = 0;
        int visibleCount = 0;
        int clipped = 0;
        int crushed = 0;

        // 最近邻采样近似（Swift 版是高质量插值缩小，统计结果差异可忽略）
        for (int y = 0; y < h; y++)
        {
            int srcY = Math.Min(height - 1, (int)(y * (double)height / h));
            for (int x = 0; x < w; x++)
            {
                int srcX = Math.Min(width - 1, (int)(x * (double)width / w));
                int offset = (srcY * width + srcX) * 4;
                // 透明像素不参与任何统计：去背人像、图标的透明区不是「死黑」
                if (pixels[offset + 3] < 26) continue;
                double r, g, b;
                if (order == PixelOrder.Rgba)
                {
                    r = pixels[offset]; g = pixels[offset + 1]; b = pixels[offset + 2];
                }
                else
                {
                    b = pixels[offset]; g = pixels[offset + 1]; r = pixels[offset + 2];
                }
                double luma = 0.299 * r + 0.587 * g + 0.114 * b;
                int index = y * w + x;
                luminance[index] = luma;
                visible[index] = true;
                visibleCount++;
                double maxChannel = Math.Max(r, Math.Max(g, b));
                double minChannel = Math.Min(r, Math.Min(g, b));
                if (maxChannel > 10) saturationSum += (maxChannel - minChannel) / maxChannel;
                if (luma > 250) clipped++;
                if (luma < 8) crushed++;
            }
        }

        if (visibleCount == 0) return null;
        double count = visibleCount;

        double mean = 0;
        for (int i = 0; i < luminance.Length; i++)
        {
            if (visible[i]) mean += luminance[i];
        }
        mean /= count;

        double variance = 0;
        for (int i = 0; i < luminance.Length; i++)
        {
            if (!visible[i]) continue;
            variance += (luminance[i] - mean) * (luminance[i] - mean);
        }
        variance /= count;

        double laplacianSum = 0;
        double laplacianSquareSum = 0;
        double laplacianCount = 0;
        if (w > 2 && h > 2)
        {
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int index = y * w + x;
                    // 只有中心与四邻都不透明时才参与锐度统计，避免透明边界被当成边缘
                    if (!visible[index] || !visible[index - 1] || !visible[index + 1]
                        || !visible[index - w] || !visible[index + w]) continue;
                    double value = 4 * luminance[index]
                        - luminance[index - 1] - luminance[index + 1]
                        - luminance[index - w] - luminance[index + w];
                    laplacianSum += value;
                    laplacianSquareSum += value * value;
                    laplacianCount++;
                }
            }
        }
        double laplacianMean = laplacianCount > 0 ? laplacianSum / laplacianCount : 0;
        double laplacianVariance = laplacianCount > 0
            ? laplacianSquareSum / laplacianCount - laplacianMean * laplacianMean
            : 0;

        return (Math.Sqrt(Math.Max(0, variance)),
                clipped / count,
                crushed / count,
                laplacianVariance,
                saturationSum / count);
    }
}
