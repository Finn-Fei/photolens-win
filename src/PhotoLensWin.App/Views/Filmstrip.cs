using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhotoLensWin.App.Services;
using PhotoLensWin.Core;

namespace PhotoLensWin.App.Views;

/// <summary>
/// 胶片缩略图栏：外层 Border 负责拖拽/点击事件，内部 ScrollViewer 负责滚动。
/// 两者职责分离，互不抢占事件路由。
/// </summary>
public class Filmstrip : Border
{
    private const double DragThreshold = 4;
    private const int HighPriorityCount = 20;

    private readonly ScrollViewer _scroller = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(10),
    };
    private readonly StackPanel _list = new();
    private readonly List<FilmItem> _items = new();
    private bool _pointerDown;
    private bool _dragging;
    private Point _pressPoint;
    private FilmItem? _pressItem;

    public event Action<ImageEntry>? SelectionChanged;

    public ImageEntry? Selected { get; private set; }

    /// <summary>该缩略图位图是否仍被某个条目显示中（供缓存淘汰时保护）。</summary>
    public bool IsThumbnailInUse(Bitmap bitmap) =>
        _items.Any(i => ReferenceEquals(i.Thumbnail, bitmap));

    public ThumbnailStore Store { get; set; } = new();

    public Filmstrip()
    {
        _scroller.Content = _list;
        Child = _scroller;

        // 卡片宽度锁定为列表可视宽度：StackPanel 会按子项 DesiredSize 排列，
        // 横图缩略图天然宽度大于列宽时会被排到列外、右侧被裁掉。
        _list.SizeChanged += (_, e) => SyncItemWidths(e.NewSize.Width);

        // 外层 Border 处理拖拽/点击：Capture(this) 后事件都路由到 Border，
        // 内部 ScrollViewer 不会被干扰（滚轮由 ScrollViewer 基类自己处理）
        PointerPressed += OnPress;
        PointerMoved += OnMove;
        PointerReleased += OnRelease;
    }

    // MARK: 对外 API

    public void SetItems(IReadOnlyList<ImageEntry> entries, string? selectedPath, bool scrollIntoView)
    {
        foreach (var item in _items) item.Detach();
        _items.Clear();
        _list.Children.Clear();

        for (int i = 0; i < entries.Count; i++)
        {
            var item = new FilmItem(entries[i], Store, i < HighPriorityCount);
            if (_list.Bounds.Width > 0) item.Width = _list.Bounds.Width;
            _list.Children.Add(item);
            _items.Add(item);
        }

        Selected = null;
        var target = selectedPath is null ? null : _items.FirstOrDefault(i =>
            string.Equals(i.Entry.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (target is not null)
        {
            Select(target, scrollIntoView: false, notify: false);
        }
    }

    /// <summary>卡片宽度跟随列宽，避免横图缩略图撑出列外被裁。</summary>
    private void SyncItemWidths(double width)
    {
        if (width <= 0) return;
        foreach (var item in _items) item.Width = width;
    }

    /// <summary>外部切换当前图（方向键/拖入预览）时同步选中态与滚动位置。
    /// notify:false —— 不回调宿主，避免"宿主改选中 → 列表回调宿主"的递归。</summary>
    public void SelectPath(string fullPath, bool scrollIntoView)
    {
        var target = _items.FirstOrDefault(i =>
            string.Equals(i.Entry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (target is null) return;

        bool same = Selected is not null
            && string.Equals(Selected.FullPath, target.Entry.FullPath, StringComparison.OrdinalIgnoreCase);
        if (same)
        {
            if (scrollIntoView)
            {
                try { target.BringIntoView(); } catch { }
            }
            return;
        }
        Select(target, scrollIntoView: scrollIntoView, notify: false);
    }

    public void RefreshSelectionBorder()
    {
        foreach (var item in _items)
        {
            item.SetSelected(item.Entry == Selected);
        }
    }

    // MARK: 选中

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

    // MARK: 拖拽/点击

    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        var item = ItemUnder(e.Source as Visual);
        if (item is null) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

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
        if (_pressItem is not null && !_dragging &&
            e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
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

    // MARK: FilmItem

    private sealed class FilmItem : Border
    {

        // UniformToFill + Stretch 对齐：缩略图铺满卡片并裁掉溢出部分，
        // 竖图不再缩成细条；配合 ClipToBounds 卡片圆角不会被方角盖住。
        private readonly Image _thumb = new()
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            ClipToBounds = true,
        };
        private readonly TextBlock _placeholder = new()
        {
            Text = "无法预览",
            FontSize = 11,
            Opacity = 0.7,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = false,
        };
        private readonly ThumbnailStore _thumbs;
        private readonly bool _highPriority;
        private bool _attached;

        public FilmItem(ImageEntry entry, ThumbnailStore thumbs, bool highPriority)
        {
            Entry = entry;
            _thumbs = thumbs;
            _highPriority = highPriority;
            // 宽度随胶片栏列宽自适应：列拖窄时缩略图跟着缩，而不是被裁掉两边
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            MinHeight = 60;
            MaxHeight = 180;
            CornerRadius = new CornerRadius(6);
            Margin = new Thickness(0, 0, 0, 10);
            Padding = new Thickness(0);
            // 子项（UniformToFill 的缩略图）不得溢出卡片，否则圆角会被方角盖住
            ClipToBounds = true;
            // 配色交给 Border.filmItem / Border.filmItem.selected 样式，亮暗主题都能跟随
            Classes.Add("filmItem");
            SetSelected(false);
            Avalonia.Automation.AutomationProperties.SetName(this, entry.Name);
            Child = new Panel { Children = { _thumb, _placeholder } };
        }

        public ImageEntry Entry { get; }

        /// <summary>当前显示的缩略图位图，用于缓存淘汰时判断该图是否还在屏幕上。</summary>
        public Bitmap? Thumbnail => _thumb.Source as Bitmap;

        public void SetSelected(bool selected)
        {
            if (selected) Classes.Add("selected");
            else Classes.Remove("selected");
        }

        public void Detach()
        {
            _attached = false;
            _thumb.Source = null;
            _placeholder.IsVisible = false;
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
            var bitmap = await _thumbs.LoadAsync(path, _highPriority);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_attached || Entry.FullPath != path) return;
                if (bitmap is null)
                {
                    // 解码失败时给出明确占位，而不是留一张空底卡
                    _placeholder.IsVisible = true;
                }
                else
                {
                    _thumb.Source = bitmap;
                }
            });
        }
    }
}
