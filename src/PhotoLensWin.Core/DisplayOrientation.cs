using System;

namespace PhotoLensWin.Core;

/// <summary>
/// 显示方向：把 EXIF 方向（1–8）与用户旋转（顺时针 90° 的圈数）合成为
/// 一个仿射变换 + 摆正后的显示尺寸。纯函数、可单测；
/// ImageViewer 只负责把结果套到渲染层，导出（另存为）也复用同一份数学。
/// </summary>
public static class DisplayOrientation
{
    /// <summary>在显示坐标系里顺时针旋转 90°：x' = height - y，y' = x。</summary>
    public static Affine ClockwiseTurn(double height) => new(0, 1, -1, 0, height, 0);

    /// <summary>
    /// 合成 EXIF 方向 + 用户旋转。
    /// 约定与 EXIF 矩阵一致：x' = A·x + C·y + E；y' = B·x + D·y + F。
    /// </summary>
    /// <param name="exifOrientation">EXIF 方向 1–8</param>
    /// <param name="rotationTurns">用户顺时针旋转的 90° 次数（自动对 4 取模）</param>
    /// <param name="pixelWidth">原始像素宽</param>
    /// <param name="pixelHeight">原始像素高</param>
    public static (Affine Matrix, double Width, double Height) Compose(
        int exifOrientation, int rotationTurns, double pixelWidth, double pixelHeight)
    {
        var m = ExifOrientation.Matrix(
            exifOrientation, (int)Math.Round(pixelWidth), (int)Math.Round(pixelHeight));

        bool swapped = ExifOrientation.SwapsSides(exifOrientation);
        double dw = swapped ? pixelHeight : pixelWidth;
        double dh = swapped ? pixelWidth : pixelHeight;

        int turns = ((rotationTurns % 4) + 4) % 4;
        for (int i = 0; i < turns; i++)
        {
            m = ClockwiseTurn(dh).After(m);
            (dw, dh) = (dh, dw);
        }
        return (m, dw, dh);
    }
}
