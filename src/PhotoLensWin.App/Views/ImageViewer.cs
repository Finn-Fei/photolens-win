using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PhotoLensWin.App.Services;
using PhotoLensWin.Core;

namespace PhotoLensWin.App.Views;

/// <summary>
/// 画布控件：缩放平移、双击切换、Ctrl+滚轮缩放、滚轮平移、触屏捏合、
/// 按住 Z 放大镜、拖放高亮、空态提示。从 Mac 版 CanvasView 移植。
/// </summary>
public class ImageViewer : Control
{
    private const double LoupeDiameter = 150;

    private readonly Grid _root = new();
    private readonly Image _image = new() { Stretch = Stretch.None, IsVisible = false };
    private readonly TextBlock _message = new();
    private readonly Border _dropHighlight = new();
    private readonly Border _loupe = new();
    private readonly Image _loupeImage = new() { Stretch = Stretch.None };

    private ViewTransform _transform = new();
    private bool _isFitted = true;
    private Affine _orientation = Affine.Identity;       // 源像素 → 摆正后显示坐标
    private int _rotationTurns;                          // 用户旋转（仅当前画面，不持久化）
    private FullImage? _current;
    private bool _pointerPanning;
    private Point _panLast;
    private Point _lastPointer;
    private bool _loupeActive;
    private bool _pinchActive;
    private double _pinchBaseScale = 1;
    private string _emptyHint = "把图片或文件夹拖到这里\n或者按 Ctrl+O 打开";

    /// <summary>拖入文件/文件夹（主画布载入、对比画布装图）</summary>
    public event Action<string[]>? OnDropFiles;
    /// <summary>缩放比例变化（更新底部百分比）</summary>
    public event Action<double>? ScaleChanged;
    public event Action<bool, bool>? ArrowsChanged;

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
        // Margin 定位需要左上对齐，默认 Stretch 会把放大镜居中到可用区域
        _loupe.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        _loupe.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        _loupe.CornerRadius = new CornerRadius(LoupeDiameter / 2);
        _loupe.Classes.Add("loupe");
        _loupe.IsHitTestVisible = false;
        _loupe.IsVisible = false;
        _loupe.Child = _loupeImage;

        _root.Children.Add(_image);
        _root.Children.Add(_dropHighlight);
        _root.Children.Add(_loupe);
        _root.Children.Add(_message);
        VisualChildren.Add(_root);
        LogicalChildren.Add(_root);

        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerExited += (_, _) => { if (_loupeActive) ToggleLoupe(false); };
        PointerCaptureLost += (_, _) => _pointerPanning = false;
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
        _image.Width = image.PixelWidth;
        _image.Height = image.PixelHeight;
        _image.IsVisible = true;
        _message.IsVisible = false;
        _orientation = ExifOrientation.Matrix(image.Orientation, image.PixelWidth, image.PixelHeight);
        _isFitted = true;
        SyncTransformSize();
        _transform.Fit();
        ApplyMatrix();
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
        ApplyMatrix();
        ScaleChanged?.Invoke(_transform.Scale);
    }

    public void SetActualSize()
    {
        _isFitted = false;
        SyncTransformSize();
        _transform.SetScale(1, resetOffset: false);
        ApplyMatrix();
        ScaleChanged?.Invoke(_transform.Scale);
    }

    public void ZoomBy(double factor) =>
        ZoomTo(_transform.Scale * factor, Bounds.Width / 2, Bounds.Height / 2);

    private void ZoomTo(double newScale, double anchorX, double anchorY)
    {
        _isFitted = false;
        SyncTransformSize();
        _transform.ZoomTo(newScale, anchorX, anchorY);
        ApplyMatrix();
        ScaleChanged?.Invoke(_transform.Scale);
    }

    /// <summary>旋转仅作用于当前画面，不持久化（对应 Mac 版语义）</summary>
    public void RotateClockwise()
    {
        if (_current is null) return;
        _rotationTurns = (_rotationTurns + 1) % 4;
        SyncTransformSize();
        if (_isFitted) _transform.Fit(); else _transform.Clamp();
        ApplyMatrix();
        ScaleChanged?.Invoke(_transform.Scale);
    }

    // MARK: 变换

    /// <summary>用户旋转 k 个 90°（顺时针）叠加在 EXIF 方向之上的复合矩阵与显示尺寸</summary>
    private (Affine Matrix, double Width, double Height) EffectiveMatrix()
    {
        var m = _orientation;
        // 摆正后尺寸已随解码结果算好；此前每次调用都重新解析 EXIF 文件，
        // 使它成为拖动/滚轮每帧 2 次的磁盘读取
        (double dw, double dh) = _current is null
            ? (0, 0)
            : (_current.DisplayWidth, _current.DisplayHeight);
        for (int i = 0; i < _rotationTurns; i++)
        {
            // 显示空间内顺时针 90°：x' = dh - y, y' = x；宽高互换
            m = new Affine(0, 1, -1, 0, dh, 0).After(m);
            (dw, dh) = (dh, dw);
        }
        return (m, dw, dh);
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

    private void ApplyMatrix()
    {
        SyncTransformSize();
        var (m, _, _) = EffectiveMatrix();
        var rect = _transform.ImageRect;
        double s = _transform.Scale;
        var matrix = new Matrix(
            s * m.A, s * m.B,
            s * m.C, s * m.D,
            rect.X + s * m.E, rect.Y + s * m.F);
        _image.RenderTransform = new MatrixTransform(matrix);
        _image.InvalidateVisual();
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
        // 捕获可能在拖动中途被系统夺走（Alt-Tab、手势等）；左键已松开就结束本次平移
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pointerPanning = false;
            return;
        }
        var pos = e.GetPosition(this);
        // 拖动平移：位移累加，而不是每次从起点重置
        _transform.Pan(pos.X - _panLast.X, pos.Y - _panLast.Y);
        _panLast = pos;
        ApplyMatrix();
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
        var pos = e.GetPosition(this);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl+滚轮：以光标为锚缩放
            double factor = e.Delta.Y > 0 ? 1.25 : 0.8;
            SyncTransformSize();
            _transform.ZoomBy(factor, pos.X, pos.Y);
            _isFitted = false;
            ApplyMatrix();
            ScaleChanged?.Invoke(_transform.Scale);
        }
        else
        {
            double dx = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? e.Delta.Y * 24 : 0;
            double dy = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0 : e.Delta.Y * 24;
            _transform.Pan(dx, dy);
            ApplyMatrix();
        }
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
        ApplyMatrix();
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
        if (_image.RenderTransform is not MatrixTransform baseTransform) return;
        _loupe.Margin = new Thickness(_lastPointer.X - LoupeDiameter / 2, _lastPointer.Y - LoupeDiameter / 2, 0, 0);
        _loupeImage.Source = _image.Source;
        _loupeImage.Width = _image.Width;
        _loupeImage.Height = _image.Height;

        // 放大镜 = 当前视图矩阵以光标为锚再放大 3 倍。
        // 平移量必须换算到 Border 的局部坐标：圆心 75px + m·(屏幕平移 - 光标位置)。
        double m = 3;
        var mt = baseTransform.Matrix;
        var zoomed = new Matrix(
            mt.M11 * m, mt.M12 * m,
            mt.M21 * m, mt.M22 * m,
            LoupeDiameter / 2 + m * (mt.M31 - _lastPointer.X),
            LoupeDiameter / 2 + m * (mt.M32 - _lastPointer.Y));
        _loupeImage.RenderTransform = new MatrixTransform(zoomed);
    }

    // MARK: 拖放

    /// <summary>从拖拽数据提取文件路径：应用内拖拽优先，其次系统文件列表。</summary>
    public static string[] ExtractPaths(IDataObject data)
    {
        try
        {
            if (data.Contains("photolens-path") && data.Get("photolens-path") is string p && p.Length > 0)
            {
                return new[] { p };
            }
        }
        catch
        {
        }
        return data.GetFiles()?
            .Select(f => f?.Path.LocalPath)
            .Where(s => !string.IsNullOrEmpty(s))
            .Cast<string>()
            .ToArray() ?? Array.Empty<string>();
    }

    private void RegisterDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        bool acceptable = false;
        AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            acceptable = ExtractPaths(e.Data).Length > 0;
            // 对应 Mac 版 draggingEntered/draggingExited 的高亮反馈
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
            ApplyMatrix();
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
