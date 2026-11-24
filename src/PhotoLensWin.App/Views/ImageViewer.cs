using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PhotoLensWin.App.Services;
using PhotoLensWin.Core;

namespace PhotoLensWin.App.Views;

/// <summary>
/// 画布控件：Image 自动居中 + Uniform 缩放（Avalonia 布局系统负责），
/// 外层容器 RenderTransform 负责旋转/缩放/平移。
/// 职责分离后，Image 永远在正确的显示尺寸上，RenderTransform 只是数学变换链。
/// </summary>
public class ImageViewer : Control
{
    private const double LoupeDiameter = 150;
    private const double LoupeZoom = 3;
    /// <summary>放大镜相对光标的偏移：避免圆框正好压住被观察的像素。</summary>
    private const double LoupeOffset = 18;

    // 三层结构：_transformLayer（Canvas 容器，用 SetLeft/SetTop 定位）→ _layoutLayer（Image 按 Uniform 自动居中）→ Image
    private readonly Grid _root = new();
    private readonly Canvas _transformLayer = new();  // 用 Canvas 才能 SetLeft/SetTop 定位子元素
    private readonly Border _layoutLayer = new();    // Image 在这里按 Uniform 自动居中
    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,                    // 按比例缩放适应父容器
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        IsVisible = false,
    };
    private readonly TextBlock _message = new();
    private readonly Border _dropHighlight = new();
    private readonly Border _loupe = new();
    // 自绘控件没有 FocusAdorner：用一层描边自己做可见的键盘焦点提示
    private readonly Border _focusRing = new()
    {
        BorderBrush = Brushes.DodgerBlue,
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(4),
        Margin = new Thickness(2),
        IsHitTestVisible = false,
        IsVisible = false,
    };
    // 放大镜内容直接取当前渲染层做局部放大，缩放/旋转/平移后也保持一致
    private readonly VisualBrush _loupeBrush = new() { Stretch = Stretch.Fill };

    private ViewTransform _transform = new();
    private bool _isFitted = true;
    private int _rotationTurns;
    private FullImage? _current;
    private bool _pointerPanning;
    private Point _panLast;
    private Point _lastPointer;
    private bool _loupeActive;
    private bool _pinchActive;
    private double _pinchBaseScale = 1;
    private string _emptyHint = "把图片或文件夹拖到这里\n或者按 Ctrl+O 打开";
    private double _wheelAccum;

    /// <summary>滚轮是否缩放；false 时滚轮切换上一张/下一张（Ctrl+滚轮始终缩放）。</summary>
    public bool WheelZooms { get; set; } = true;

    /// <summary>拖入文件/文件夹（主画布载入、对比画布装图）</summary>
    public event Action<string[]>? OnDropFiles;
    /// <summary>缩放比例变化（更新底部百分比）</summary>
    public event Action<double>? ScaleChanged;
    public event Action<bool, bool>? ArrowsChanged;
    /// <summary>滚轮切换图片：-1 上一张，+1 下一张</summary>
    public event Action<int>? WheelStepped;

    public ImageViewer()
    {
        ClipToBounds = true;
        Focusable = true;

        _message.TextAlignment = TextAlignment.Center;
        _message.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _message.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        _message.Classes.Add("emptyHint");
        _message.IsVisible = true;
        _message.Text = _emptyHint;

        _dropHighlight.Classes.Add("dropHighlight");
        _dropHighlight.IsHitTestVisible = false;
        _dropHighlight.IsVisible = false;

        _loupe.Width = LoupeDiameter;
        _loupe.Height = LoupeDiameter;
        _loupe.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        _loupe.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        _loupe.CornerRadius = new CornerRadius(LoupeDiameter / 2);
        _loupe.Classes.Add("loupe");
        _loupe.IsHitTestVisible = false;
        _loupe.IsVisible = false;
        _loupe.Background = _loupeBrush;
        // 浅色照片上纯白圆环不可见：加一圈硬边深色外描边
        _loupe.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetX = 0, OffsetY = 0, Blur = 0, Spread = 1, Color = Color.Parse("#99000000"),
        });

        _layoutLayer.Child = _image;
        _transformLayer.Children.Add(_layoutLayer);
        _root.Children.Add(_transformLayer);
        _root.Children.Add(_dropHighlight);
        _root.Children.Add(_loupe);
        _root.Children.Add(_message);
        _root.Children.Add(_focusRing);
        VisualChildren.Add(_root);
        LogicalChildren.Add(_root);

        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerExited += (_, _) => { if (_loupeActive) ToggleLoupe(false); };
        PointerCaptureLost += (_, _) => _pointerPanning = false;
        GotFocus += (_, _) => _focusRing.IsVisible = true;
        LostFocus += (_, _) => _focusRing.IsVisible = false;
        PointerWheelChanged += OnWheel;
        DoubleTapped += OnDoubleTapped;
        SizeChanged += OnResized;
        AddHandler(Gestures.PinchEvent, OnPinch);
        AddHandler(Gestures.PinchEndedEvent, OnPinchEnded);

        RegisterDrop();
    }

    // MARK: 展示

    public void Present(FullImage image)
    {
        _current = image;
        _rotationTurns = 0;
        _image.Source = image.Bitmap;
        _image.IsVisible = true;
        _message.IsVisible = false;
        _isFitted = true;
        SyncTransformSize();
        _transform.Fit();
        ApplyTransform();
        ScaleChanged?.Invoke(_transform.Scale);
    }

    public void ShowMessage(string text)
    {
        _current = null;
        _image.IsVisible = false;
        _image.Source = null;
        _emptyHint = text;
        _message.Text = text;
        _message.IsVisible = true;
        ScaleChanged?.Invoke(0);
    }

    public FullImage? Current => _current;
    public bool IsFitted => _isFitted;
    /// <summary>用户顺时针旋转的 90° 圈数（0–3）。</summary>
    public int RotationTurns => _rotationTurns;
    /// <summary>把当前 EXIF 方向 + 用户旋转的复合变换与目标显示尺寸交给导出（另存为）。</summary>
    public (Affine Matrix, int Width, int Height) ExportTransform()
    {
        var (m, dw, dh) = EffectiveMatrix();
        return (m, (int)Math.Round(dw), (int)Math.Round(dh));
    }
    public double Scale => _transform.Scale;
    public double FitScale => _transform.FitScale;
    public bool CanGoPrevious { get; private set; }
    public bool CanGoNext { get; private set; }

    public void SetArrows(bool canPrev, bool canNext)
    {
        CanGoPrevious = canPrev;
        CanGoNext = canNext;
        ArrowsChanged?.Invoke(canPrev, canNext);
    }

    public void FitToWindow()
    {
        _isFitted = true;
        SyncTransformSize();
        _transform.Fit();
        ApplyTransform();
        ScaleChanged?.Invoke(_transform.Scale);
    }

    public void SetActualSize()
    {
        _isFitted = false;
        SyncTransformSize();
        _transform.SetScale(1, resetOffset: false);
        ApplyTransform();
        ScaleChanged?.Invoke(_transform.Scale);
    }

    public void ZoomBy(double factor) =>
        ZoomTo(_transform.Scale * factor, Bounds.Width / 2, Bounds.Height / 2);

    private void ZoomTo(double newScale, double anchorX, double anchorY)
    {
        _isFitted = false;
        SyncTransformSize();
        _transform.ZoomTo(newScale, anchorX, anchorY);
        ApplyTransform();
        ScaleChanged?.Invoke(_transform.Scale);
    }

    /// <summary>旋转仅作用于当前画面，不持久化（对应 Mac 版语义）</summary>
    public void RotateClockwise()
    {
        if (_current is null) return;
        _rotationTurns = (_rotationTurns + 1) % 4;
        SyncTransformSize();
        if (_isFitted) _transform.Fit(); else _transform.Clamp();
        ApplyTransform();
        ScaleChanged?.Invoke(_transform.Scale);
    }

    // MARK: 变换

    /// <summary>用户旋转 k 个 90°（顺时针）叠加在 EXIF 方向之上的复合矩阵与显示尺寸</summary>
    private (Affine Matrix, double Width, double Height) EffectiveMatrix()
    {
        if (_current is null) return (Affine.Identity, 0, 0);
        // EXIF 方向 × 用户旋转的复合逻辑收敛到 Core（DisplayOrientation），此处只取用，
        // 于是同一份数学可以被单测，也能被导出（另存为）复用。
        return DisplayOrientation.Compose(
            _current.Orientation, _rotationTurns, _current.PixelWidth, _current.PixelHeight);
    }

    private void SyncTransformSize()
    {
        _transform.ViewWidth = Bounds.Width;
        _transform.ViewHeight = Bounds.Height;
        if (_current is not null)
        {
            var (_, dw, dh) = EffectiveMatrix();
            _transform.ImageWidth = dw;
            _transform.ImageHeight = dh;
        }
    }

    /// <summary>
    /// 变换链：Image（Uniform 自动居中显示 DisplayWidth×DisplayHeight）
    /// → _layoutLayer 用 Canvas.SetLeft/SetTop 定位到居中+偏移位置，Width/Height 设为 scaled 尺寸
    /// → _transformLayer.RenderTransform 做 EXIF + 用户旋转。
    /// 
    /// 关键：Canvas 是唯一能对子元素用 SetLeft/SetTop 精确定位的容器。
    /// Grid/Panel 默认拉伸填满，layoutX/layoutY 偏移会被忽略，Pan 永远无效。
    /// </summary>
    private void ApplyTransform()
    {
        SyncTransformSize();
        var (m, dw, dh) = EffectiveMatrix();

        double scaledW = dw * _transform.Scale;
        double scaledH = dh * _transform.Scale;
        double layoutX = _transform.ViewWidth / 2 - scaledW / 2 + _transform.OffsetX;
        double layoutY = _transform.ViewHeight / 2 - scaledH / 2 + _transform.OffsetY;

        // 1. 位置按摆正后的显示尺寸计算；层盒子用「未旋转像素尺寸 × 缩放」，
        //    保证后续旋转矩阵（平移量以像素为单位）与层坐标同一量纲。
        Canvas.SetLeft(_layoutLayer, layoutX);
        Canvas.SetTop(_layoutLayer, layoutY);
        double rawW = _current?.PixelWidth ?? 0;
        double rawH = _current?.PixelHeight ?? 0;
        _layoutLayer.Width = Math.Max(0, rawW * _transform.Scale);
        _layoutLayer.Height = Math.Max(0, rawH * _transform.Scale);

        // 2. 旋转：EXIF + 用户旋转叠加在 layoutLayer 上；
        //    矩阵的平移项是像素，必须同步乘 Scale，原点固定在左上角（0,0）。
        double s = _transform.Scale;
        var rotMatrix = new Matrix(m.A, m.B, m.C, m.D, m.E * s, m.F * s);
        _layoutLayer.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        _layoutLayer.RenderTransform = new MatrixTransform(rotMatrix);
    }

    // MARK: 交互

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            _pointerPanning = true;
            _panLast = point.Position;
            e.Pointer.Capture(this);
            Focus();
            e.Handled = true;
        }
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        _lastPointer = e.GetPosition(this);
        if (_loupeActive) UpdateLoupe();

        if (!_pointerPanning) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pointerPanning = false;
            return;
        }
        var pos = e.GetPosition(this);
        _transform.Pan(pos.X - _panLast.X, pos.Y - _panLast.Y);
        _panLast = pos;
        ApplyTransform();
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pointerPanning)
        {
            _pointerPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_current is null) return;
        if (_isFitted) SetActualSize(); else FitToWindow();
        e.Handled = true;
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_current is null) return;
        // 设置成「滚轮切换图片」时，滚轮翻页；Ctrl+滚轮仍按 Windows 惯例缩放
        if (!WheelZooms && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _wheelAccum += e.Delta.Y;
            if (Math.Abs(_wheelAccum) >= 1)
            {
                WheelStepped?.Invoke(_wheelAccum < 0 ? 1 : -1);
                _wheelAccum = 0;
            }
            e.Handled = true;
            return;
        }
        if (e.Delta.Y == 0) { e.Handled = true; return; } // 横向/倾斜滚轮不应触发缩放
        var pos = e.GetPosition(this);
        double factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        SyncTransformSize();
        _transform.ZoomBy(factor, pos.X, pos.Y);
        _isFitted = false;
        ApplyTransform();
        ScaleChanged?.Invoke(_transform.Scale);
        e.Handled = true;
    }

    // MARK: 触屏捏合

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (_current is null) return;
        if (!_pinchActive)
        {
            _pinchActive = true;
            _pinchBaseScale = _transform.Scale;
            _isFitted = false;
        }
        SyncTransformSize();
        _transform.ZoomTo(_pinchBaseScale * e.Scale, e.ScaleOrigin.X, e.ScaleOrigin.Y);
        ApplyTransform();
        ScaleChanged?.Invoke(_transform.Scale);
        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _pinchActive = false;
        e.Handled = true;
    }

    // MARK: 放大镜（按住 Z）

    public void ToggleLoupe(bool active)
    {
        if (active && _current is null) return;
        _loupeActive = active;
        _loupe.IsVisible = active;
        if (active) UpdateLoupe();
    }

    private void UpdateLoupe()
    {
        if (_current is null || !_loupeActive) return;

        // 圆框贴边时钳制在画布内，避免被 ClipToBounds 裁成半圆
        double x = Math.Clamp(_lastPointer.X - LoupeDiameter / 2 + LoupeOffset, 0, Math.Max(0, Bounds.Width - LoupeDiameter));
        double y = Math.Clamp(_lastPointer.Y - LoupeDiameter / 2 + LoupeOffset, 0, Math.Max(0, Bounds.Height - LoupeDiameter));
        _loupe.Margin = new Thickness(x, y, 0, 0);

        // 以光标为中心取 1/Zoom 大小的源区域，铺满圆框
        double viewW = Math.Max(1, Bounds.Width);
        double viewH = Math.Max(1, Bounds.Height);
        double relW = Math.Min(1, LoupeDiameter / (viewW * LoupeZoom));
        double relH = Math.Min(1, LoupeDiameter / (viewH * LoupeZoom));
        double cx = Math.Clamp(_lastPointer.X / viewW, 0, 1);
        double cy = Math.Clamp(_lastPointer.Y / viewH, 0, 1);
        double left = Math.Clamp(cx - relW / 2, 0, 1 - relW);
        double top = Math.Clamp(cy - relH / 2, 0, 1 - relH);

        _loupeBrush.Visual = _transformLayer;
        _loupeBrush.SourceRect = new RelativeRect(left, top, relW, relH, RelativeUnit.Relative);
        _loupeBrush.DestinationRect = new RelativeRect(0, 0, 1, 1, RelativeUnit.Relative);
    }

    // MARK: 拖放

    public static string[] ExtractPaths(IDataObject data)
    {
        // 优先读内部拖放自定义格式（Filmstrip 等 Avalonia 控件发起的 DoDragDrop）
        try
        {
            if (data.Contains("photolens-path") && data.Get("photolens-path") is string p && p.Length > 0)
                return new[] { p };
        }
        catch { }
        // 外部文件拖放：GetFiles 依赖 Win32 剪贴板格式，内部 Avalonia 拖放时可能抛异常
        try
        {
            var files = data.GetFiles();
            if (files is not null)
            {
                var result = files
                    .Select(f => f?.Path.LocalPath)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Cast<string>()
                    .ToArray();
                if (result.Length > 0) return result;
            }
        }
        catch { }
        return Array.Empty<string>();
    }

    private void RegisterDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        bool acceptable = false;
        AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            acceptable = ExtractPaths(e.Data).Length > 0;
            _dropHighlight.IsVisible = acceptable;
            e.Handled = true;
        });
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            acceptable = ExtractPaths(e.Data).Length > 0;
            _dropHighlight.IsVisible = acceptable;
            e.DragEffects = acceptable ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DragLeaveEvent, (_, _) =>
        {
            _dropHighlight.IsVisible = false;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            _dropHighlight.IsVisible = false;
            var names = ExtractPaths(e.Data);
            if (names.Length > 0) OnDropFiles?.Invoke(names);
            e.Handled = true;
        });
    }

    // MARK: 布局

    private void OnResized(object? sender, SizeChangedEventArgs e)
    {
        if (_current is not null)
        {
            SyncTransformSize();
            if (_isFitted) _transform.Fit(); else _transform.Clamp();
            ApplyTransform();
            ScaleChanged?.Invoke(_transform.Scale);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _root.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _root.Measure(availableSize);
        return availableSize;
    }
}
