using System;

namespace PhotoLensWin.Core;

/// <summary>
/// 缩放/平移的纯数学模型，从 Mac 版 ViewTransform 逐行移植，可单测。
/// </summary>
public struct ViewTransform
{
    public const double MinScale = 0.02;
    public const double MaxScale = 20;
    /// <summary>适合窗口时四周留白</summary>
    public const double FitPadding = 24;

    public double ViewWidth;
    public double ViewHeight;
    public double ImageWidth;
    public double ImageHeight;
    public double Scale { get; private set; }
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    public ViewTransform(double viewWidth = 0, double viewHeight = 0,
                         double imageWidth = 0, double imageHeight = 0)
    {
        ViewWidth = viewWidth;
        ViewHeight = viewHeight;
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        Scale = 1;
        OffsetX = 0;
        OffsetY = 0;
    }

    /// <summary>适合窗口的缩放比（图片比窗口小时不超过 1，避免糊化放大）</summary>
    public double FitScale
    {
        get
        {
            if (ImageWidth <= 0 || ImageHeight <= 0 ||
                ViewWidth <= 2 || ViewHeight <= 2) return 1;
            double availW = Math.Max(1, ViewWidth - FitPadding * 2);
            double availH = Math.Max(1, ViewHeight - FitPadding * 2);
            double scale = Math.Min(availW / ImageWidth, availH / ImageHeight);
            return Math.Min(scale, 1);
        }
    }

    public void Fit()
    {
        Scale = Math.Max(MinScale, FitScale);
        OffsetX = 0;
        OffsetY = 0;
    }

    public void SetScale(double newScale, bool resetOffset)
    {
        Scale = Math.Clamp(newScale, MinScale, MaxScale);
        if (resetOffset)
        {
            OffsetX = 0;
            OffsetY = 0;
        }
        Clamp();
    }

    /// <summary>以视图中某个点为中心缩放：缩放前后该点下方的图像位置保持不动</summary>
    public void ZoomTo(double newScale, double anchorX, double anchorY)
    {
        double target = Math.Clamp(newScale, MinScale, MaxScale);
        if (Scale <= 0 || Math.Abs(target - Scale) < double.Epsilon) return;
        double relX = anchorX - ViewWidth / 2;
        double relY = anchorY - ViewHeight / 2;
        double imageX = (relX - OffsetX) / Scale;
        double imageY = (relY - OffsetY) / Scale;
        Scale = target;
        OffsetX = relX - imageX * Scale;
        OffsetY = relY - imageY * Scale;
        Clamp();
    }

    public void ZoomBy(double factor, double anchorX, double anchorY) =>
        ZoomTo(Scale * factor, anchorX, anchorY);

    /// <summary>拖动平移：位移累加，而不是每次从起点重置</summary>
    public void Pan(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
        Clamp();
    }

    public void Clamp()
    {
        if (ImageWidth <= 0 || ImageHeight <= 0)
        {
            OffsetX = 0;
            OffsetY = 0;
            return;
        }
        double width = ImageWidth * Scale;
        double height = ImageHeight * Scale;
        double limitX = Math.Max(0, (width - ViewWidth) / 2);
        double limitY = Math.Max(0, (height - ViewHeight) / 2);
        OffsetX = Math.Clamp(OffsetX, -limitX, limitX);
        OffsetY = Math.Clamp(OffsetY, -limitY, limitY);
    }

    /// <summary>图像在视图坐标系中的绘制矩形（左上角原点）</summary>
    public (double X, double Y, double Width, double Height) ImageRect
    {
        get
        {
            double width = ImageWidth * Scale;
            double height = ImageHeight * Scale;
            return (ViewWidth / 2 - width / 2 + OffsetX,
                    ViewHeight / 2 - height / 2 + OffsetY,
                    width, height);
        }
    }
}
