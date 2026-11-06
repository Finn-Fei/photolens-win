namespace PhotoLensWin.Core;

/// <summary>仿射系数：(A,B,C,D,E,F) 表示 x' = A·x + C·y + E；y' = B·x + D·y + F。</summary>
public readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    /// <summary>先应用 other，再应用 this。</summary>
    public Affine After(Affine o) => new(
        A * o.A + C * o.B,
        B * o.A + D * o.B,
        A * o.C + C * o.D,
        B * o.C + D * o.D,
        A * o.E + C * o.F + E,
        B * o.E + D * o.F + F);
}

/// <summary>EXIF 方向（1–8）到显示坐标的变换。</summary>
public static class ExifOrientation
{
    /// <summary>方向 5-8 会交换宽高</summary>
    public static bool SwapsSides(int orientation) => orientation is >= 5 and <= 8;

    /// <summary>
    /// EXIF 方向矩阵（源像素坐标 → 摆正后显示坐标）。
    /// 系数约定：x' = A·x + C·y + E；y' = B·x + D·y + F，平移量为像素单位。
    /// </summary>
    public static Affine Matrix(int orientation, int width, int height) => orientation switch
    {
        2 => new(-1, 0, 0, 1, width, 0),    // 水平镜像
        3 => new(-1, 0, 0, -1, width, height),   // 旋转 180°
        4 => new(1, 0, 0, -1, 0, height),   // 垂直镜像
        5 => new(0, 1, 1, 0, 0, 0),         // 转置
        6 => new(0, 1, -1, 0, height, 0),   // 顺时针 90°
        7 => new(0, -1, -1, 0, height, width),   // 副对角线翻转（水平镜像后顺时针 90°）
        8 => new(0, -1, 1, 0, 0, width),    // 逆时针 90°
        _ => new(1, 0, 0, 1, 0, 0),
    };
}
