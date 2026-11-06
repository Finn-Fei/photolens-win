using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using PhotoLensWin.App.Services;
using PhotoLensWin.Core;

namespace PhotoLensWin.App.Views;

/// <summary>
/// 胶片缩略图栏。事件语义与 Mac 版一致：
/// 按下仅记录位置不选中；位移超过阈值进入拖拽会话；松开时未拖拽才判定为点击选中。
/// </summary>
public class Filmstrip : ScrollViewer
{
    private const double DragThreshold = 4;

    private readonly StackPanel _list = new();
    private readonly List<FilmItem> _items = new();
    private bool _pointerDown;
    private bool _dragging;
    private Point _pressPoint;
    private FilmItem? _pressItem;

    public event Action<ImageEntry>? SelectionChanged;

    public ImageEntry? Selected { get; private set; }

    /// <summary>XAML 无参构造；宿主可注入共享的缩略图缓存。</summary>
    public ThumbnailStore Store { get; set; } = new();

    public Filmstrip()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Padding = new Thickness(10);
        Content = _list;

        // 事件必须挂在捕获目标（this）上：OnPress 里 Capture(this) 后，
        // 指针事件只路由到 this 及其祖先，挂在子元素 _list 上会永远收不到 Moved/Released
        PointerPressed += OnPress;
        PointerMoved += OnMove;
        PointerReleased += OnRelease;
    }

    public void SetItems(IReadOnlyList<ImageEntry> entries, string? selectedPath, bool scrollIntoView)
    {
        foreach (var item in _items) item.Detach();
        _items.Clear();
        _list.Children.Clear();

        foreach (var entry in entries)
        {
            var item = new FilmItem(entry, Store);
            _list.Children.Add(item);
            _items.Add(item);
        }

        Selected = null;
        var target = selectedPath is null ? null : _items.FirstOrDefault(i => i.Entry.FullPath == selectedPath);
        if (target is not null)
        {
            // 静默初始化选中：宿主随后会显式 SelectIndex，重复广播会导致同一图解码两次
            Select(target, scrollIntoView: false, notify: false);
        }
    }

    public void ScrollToSelection()
    {
        var item = _items.FirstOrDefault(i => ReferenceEquals(i.Entry, Selected));
        item?.BringIntoView();
    }

    public void RefreshSelectionBorder()
    {
        foreach (var item in _items)
        {
            item.SetSelected(item.Entry == Selected);
        }
    }

    private void Select(FilmItem item, bool scrollIntoView = true, bool notify = true)
    {
        Selected = item.Entry;
        RefreshSelectionBorder();
        if (scrollIntoView) item.BringIntoView();
        if (notify) SelectionChanged?.Invoke(item.Entry);
    }

    private FilmItem? ItemUnder(Visual? source)
    {
        while (source is not null && source is not FilmItem)
        {
            source = source.GetVisualParent();
        }
        return source as FilmItem;
    }

    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        var item = ItemUnder(e.Source as Visual);
        if (item is null) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        // 按下仅记录，不触发选中
        _pointerDown = true;
        _dragging = false;
        _pressItem = item;
        _pressPoint = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        if (!_pointerDown || _dragging || _pressItem is null) return;
        var pos = e.GetPosition(this);
        if (Distance(pos, _pressPoint) < DragThreshold) return;

        // 位移超过阈值：进入拖拽会话（DoDragDrop 同步阻塞到拖放结束）
        _dragging = true;
        var path = _pressItem.Entry.FullPath;
        var data = new DataObject();
        data.Set(DataFormats.FileNames, new[] { path });
        data.Set("photolens-path", path);
        DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        e.Handled = true;
    }

    private void OnRelease(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressItem is not null && !_dragging && e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            // 未进入拖拽：判定为点击选中
            Select(_pressItem);
        }
        _pointerDown = false;
        _dragging = false;
        _pressItem = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private static double Distance(Point a, Point b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    /// <summary>单个缩略图卡片。</summary>
    private sealed class FilmItem : Border
    {
        private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#16181A"));
        private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#225C99FF"));
        private static readonly IBrush IdleBorderBrush = new SolidColorBrush(Color.Parse("#2A2D30"));
        private static readonly IBrush SelectedBorderBrush = new SolidColorBrush(Color.Parse("#5C99FF"));

        private readonly Image _thumb = new() { Stretch = Stretch.Uniform };
        private readonly ThumbnailStore _thumbs;
        private bool _attached;

        public FilmItem(ImageEntry entry, ThumbnailStore thumbs)
        {
            Entry = entry;
            _thumbs = thumbs;
            // 尺寸/外观用本地值：Window.Styles 的 filmItem 样式 setter 对此卡片未命中
            Width = 208;
            Height = 92;
            CornerRadius = new CornerRadius(8);
            Margin = new Thickness(0, 0, 0, 10);
            Padding = new Thickness(6);
            SetSelected(false);
            Child = _thumb;
        }

        public ImageEntry Entry { get; }

        public void SetSelected(bool selected)
        {
            Background = selected ? SelectedBrush : IdleBrush;
            BorderBrush = selected ? SelectedBorderBrush : IdleBorderBrush;
            BorderThickness = new Thickness(selected ? 2 : 1);
        }

        public void Detach()
        {
            _attached = false;
            _thumb.Source = null;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            var path = Entry.FullPath;
            var cached = _thumbs.GetCached(path);
            if (cached is not null)
            {
                _thumb.Source = cached;
                return;
            }
            _ = LoadThumbAsync(path);
        }

        private async Task LoadThumbAsync(string path)
        {
            var bitmap = await _thumbs.LoadAsync(path);
            if (bitmap is null) return;
            // 解码在后台完成，回到 UI 线程再应用到仍挂着同一文件的卡片
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_attached && Entry.FullPath == path) _thumb.Source = bitmap;
            });
        }
    }
}
