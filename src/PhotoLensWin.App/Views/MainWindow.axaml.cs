using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using PhotoLensWin.App.Services;
using PhotoLensWin.Core;
using SkiaSharp;

namespace PhotoLensWin.App.Views;

public partial class MainWindow : Window
{
    private const string DefaultHint = "滚轮缩放 · 拖动平移 · 按住 Z 放大镜 · ←/→ 切换";

    private readonly ThumbnailStore _thumbs = new();
    private readonly ImageStore _imageStore = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _slideshowTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly MenuFlyout SortFlyout = new();

    private List<ImageEntry> _images = new();
    private int _currentIndex = -1;
    private PhotoSort _sort = PhotoSort.NameAscending;
    private bool _showStrip = true;
    private bool _compareOn;
    private bool _infoOn;
    private string? _comparePath;
    private FullImage? _current;
    private AppSettings _settings = new();
    private MemoryStream? _clipboardPng;
    private int _loadVersion;
    private bool _rebuildingPicker;
    private bool _slideshowOn;
    /// <summary>用户拖好的胶片栏宽度，隐藏/显示时保持。</summary>
    private double _stripWidth = 232;

    public MainWindow()
    {
        InitializeComponent();
        if (GenerateAppIcon() is { } icon) Icon = new WindowIcon(icon);
        Strip.Store = _thumbs;
        // 淘汰缩略图时跳过仍在屏幕上的图，否则会释放正被渲染的位图
        _thumbs.IsProtected = Strip.IsThumbnailInUse;
        WireControls();
        ApplyColumnConstraints();
        _toastTimer.Tick += (_, _) => ResetHint();
        _slideshowTimer.Tick += (_, _) => Step(1);
    }

    /// <summary>设置 Grid 列宽约束：列表 120~480，主图不低于 200</summary>
    private void ApplyColumnConstraints()
    {
        var grid = Strip.Parent as Grid;
        if (grid is null || grid.ColumnDefinitions.Count < 3) return;
        grid.ColumnDefinitions[0].MinWidth = 120;
        grid.ColumnDefinitions[0].MaxWidth = 480;
        grid.ColumnDefinitions[2].MinWidth = 200;
    }

    // MARK: 启动

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        try
        {
            _settings = SettingsStore.Load();
            _sort = PhotoSortExtensions.FromName(_settings.SortRule) ?? PhotoSort.NameAscending;
            _showStrip = _settings.ShowFilmstrip;
            ApplyListVisibility();
            UpdateSortButton();
            ApplyTheme(_settings.Theme);
            RestoreWindowBounds();

            var args = Environment.GetCommandLineArgs().Skip(1).Where(a => !a.StartsWith('-')).ToArray();
            var snapshotPath = Environment.GetEnvironmentVariable("PHOTOLENS_SNAPSHOT");
            var snapshotFolder = Environment.GetEnvironmentVariable("PHOTOLENS_SNAPSHOT_FOLDER");

            if (args.Length > 0)
            {
                LoadFromPaths(args);
            }
            else if (!string.IsNullOrEmpty(_settings.LastFolder) && Directory.Exists(_settings.LastFolder))
            {
                LoadFolder(_settings.LastFolder);
            }

            if (snapshotPath is not null)
            {
                await RunSelfCheckSnapshotAsync(snapshotPath, snapshotFolder);
            }
        }
        catch (Exception ex)
        {
            // async void 里未捕获的异常会直接结束进程，必须在这里兜住
            LogError("OnOpened failed: " + ex);
            ShowToast("启动时出错：" + ex.Message);
        }
    }

    /// <summary>自验模式：环境变量触发，渲染窗口截图后自动退出。</summary>
    private async Task RunSelfCheckSnapshotAsync(string snapshotPath, string? snapshotFolder)
    {
        if (!string.IsNullOrEmpty(snapshotFolder))
        {
            LoadFolder(snapshotFolder);
        }
        await Task.Delay(2500);

        if (Environment.GetEnvironmentVariable("PHOTOLENS_SNAPSHOT_BROWSE") == "1" && _images.Count > 0)
        {
            for (int i = 0; i < _images.Count; i++)
            {
                SelectIndex(i, scrollStrip: false);
                await Task.Delay(350);
            }
            await Task.Delay(600);
        }

        if (!string.IsNullOrEmpty(snapshotFolder))
        {
            bool wantInfo = Environment.GetEnvironmentVariable("PHOTOLENS_SNAPSHOT_INFO") == "1";
            bool wantCompare = Environment.GetEnvironmentVariable("PHOTOLENS_SNAPSHOT_COMPARE") == "1";
            if (wantInfo) ToggleInfo();
            if (wantCompare) ToggleCompare();
            if (wantInfo || wantCompare) await Task.Delay(500);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                double scaling = RenderScaling;
                using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(
                    new PixelSize((int)(Bounds.Width * scaling), (int)(Bounds.Height * scaling)),
                    new Vector(96, 96) * scaling);
                rtb.Render(this);
                using var fs = File.Create(snapshotPath);
                rtb.Save(fs);
            }
            catch (Exception ex)
            {
                LogError("snapshot failed: " + ex.Message);
            }
        });
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(0);
    }

    // MARK: 窗口持久化

    private void SaveWindowBounds()
    {
        try
        {
            _settings.WindowLeft = (int)(Position.X + 0.5);
            _settings.WindowTop = (int)(Position.Y + 0.5);
            _settings.WindowWidth = (int)(Bounds.Width + 0.5);
            _settings.WindowHeight = (int)(Bounds.Height + 0.5);
        }
        catch { /* 位置可能未设置（首次启动），忽略 */ }
    }

    private void RestoreWindowBounds()
    {
        if (_settings.WindowLeft < 0 || _settings.WindowTop < 0) return;
        // 简单校验：确保恢复的位置不会完全在屏幕外
        try
        {
            Position = new PixelPoint(_settings.WindowLeft, _settings.WindowTop);
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
        }
        catch { /* 无效值忽略 */ }
    }

    // MARK: 主题

    private void ApplyTheme(string? themeName)
    {
        var variant = string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = variant;
    }

    private void ToggleTheme()
    {
        bool isLight = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        _settings.Theme = isLight ? "Dark" : "Light";
        SettingsStore.Save(_settings);
        ApplyTheme(_settings.Theme);
        UpdateRecentMenu();
        ShowToast($"已切换到{_settings.Theme}主题");
    }

    // MARK: 控件接线

    private void WireControls()
    {
        _imageStore.IsProtected = img =>
            ReferenceEquals(img, _current) || ReferenceEquals(img, CompareViewer.Current);

        BtnOpenFolder.Click += async (_, _) => await OpenFolderAsync();
        BtnOpenImages.Click += async (_, _) => await OpenFilesAsync();
        BuildMenuBar();

        BtnSort.Click += (_, _) =>
        {
            BuildSortFlyout();
            SortFlyout.ShowAt(BtnSort);
        };
        BtnCompare.Click += (_, _) => ToggleCompare();
        BtnInfo.Click += (_, _) => ToggleInfo();
        BtnList.Click += (_, _) => ToggleList();
        BtnSlideshow.Click += (_, _) => ToggleSlideshow();

        // 对比模式下同时作用于右图，避免"右图没有任何适配控件"的割裂感
        BtnFit.Click += (_, _) => {
            MainViewer.FitToWindow();
            if (_compareOn) CompareViewer.FitToWindow();
        };
        BtnActual.Click += (_, _) => {
            MainViewer.SetActualSize();
            if (_compareOn) CompareViewer.SetActualSize();
        };
        BtnPrev.Click += (_, _) => Step(-1);
        BtnNext.Click += (_, _) => Step(1);

        MainViewer.OnDropFiles += HandleMainDrop;
        CompareViewer.OnDropFiles += HandleCompareDrop;
        MainViewer.ScaleChanged += scale => {
            UpdateZoomLabel(scale, MainViewer.IsFitted);
            UpdateFitButtonState();
        };
        MainViewer.ArrowsChanged += (prev, next) =>
        {
            // 对比模式下箭头仍作用于左图（右侧用下拉框选图），不再整体隐藏
            BtnPrev.IsVisible = prev;
            BtnNext.IsVisible = next;
        };

        Strip.SelectionChanged += entry =>
        {
            int index = _images.FindIndex(e => ReferenceEquals(e, entry) || PathEquals(e.FullPath, entry.FullPath));
            if (index >= 0) SelectIndex(index);
        };
        ComparePicker.SelectionChanged += OnComparePicked;

        ResetHint();
        // 信息面板浮在整窗右上角，窗口较矮时会把右箭头盖住；布局变化后重新让位
        InfoPanel.LayoutUpdated += (_, _) => UpdateNextArrowMargin();
        MainViewer.SizeChanged += (_, _) => UpdateNextArrowMargin();

        MainViewer.ContextMenu = BuildViewerMenu(isMain: true);
        CompareViewer.ContextMenu = BuildViewerMenu(isMain: false);

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Closed += (_, _) =>
        {
            StopSlideshow();
            SaveWindowBounds();
            SaveSettings();
        };
    }

    /// <summary>菜单在代码中构建：复用已有的操作方法，快捷键文案与 OnKeyDown 保持一处维护。</summary>
    private void BuildMenuBar()
    {
        static MenuItem Item(string header, Action click)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => click();
            return item;
        }

        // 文件
        var file = new MenuItem { Header = "文件(_F)" };
        file.Items.Add(Item("打开文件夹…　Ctrl+O", () => _ = OpenFolderAsync()));
        file.Items.Add(Item("打开图片…　Ctrl+Shift+O", () => _ = OpenFilesAsync()));

        // 最近文件夹（动态重建）
        var recentSub = new MenuItem { Header = "最近打开" };
        recentSub.Items.Add(Item("（加载中…）", () => { }));
        file.Items.Add(recentSub);
        UpdateRecentMenu(recentSub);

        file.Items.Add(new Separator());
        file.Items.Add(Item("退出", () => Close()));

        // 视图
        var view = new MenuItem { Header = "视图(_V)" };
        view.Items.Add(Item("显示/隐藏列表　L", ToggleList));
        view.Items.Add(Item("适合窗口　9", MainViewer.FitToWindow));
        view.Items.Add(Item("实际大小　0", MainViewer.SetActualSize));
        view.Items.Add(Item("放大　+", () => MainViewer.ZoomBy(1.25)));
        view.Items.Add(Item("缩小　-", () => MainViewer.ZoomBy(0.8)));
        view.Items.Add(Item("顺时针旋转　R", MainViewer.RotateClockwise));
        view.Items.Add(new Separator());
        view.Items.Add(Item("图片信息　I", ToggleInfo));
        view.Items.Add(Item("全屏　F11", ToggleFullscreen));
        view.Items.Add(new Separator());
        view.Items.Add(Item("主题：暗色/亮色", ToggleTheme));
        view.Items.Add(new Separator());
        view.Items.Add(Item("上一张　[", () => Step(-1)));
        view.Items.Add(Item("下一张　]", () => Step(1)));
        view.Items.Add(Item("开始幻灯片", ToggleSlideshow));

        TopMenu.Items.Add(file);
        TopMenu.Items.Add(view);
    }

    /// <summary>更新最近文件夹子菜单。传入指定 sub 或自动找。</summary>
    private void UpdateRecentMenu(MenuItem? targetSub = null)
    {
        MenuItem? sub = targetSub ?? FindRecentSub();
        if (sub is null) return;
        sub.Items.Clear();

        var folders = _settings.RecentFolders.Where(Directory.Exists).Take(AppSettings.MaxRecentFolders).ToList();
        if (folders.Count == 0)
        {
            sub.Items.Add(new MenuItem { Header = "（暂无）", IsEnabled = false });
            return;
        }
        foreach (var folder in folders)
        {
            var captured = folder;
            var item = new MenuItem { Header = captured };
            item.Click += (_, _) => LoadFolder(captured);
            sub.Items.Add(item);
        }
        // 清理不存在的目录
        _settings.RecentFolders = folders;
        if (targetSub is null) SaveSettings();
    }

    private MenuItem? FindRecentSub()
    {
        var fileMenu = TopMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString()?.Contains("文件") == true);
        return fileMenu?.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString()?.Contains("最近") == true);
    }

    // MARK: 打开与载入

    private async Task OpenFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "选择文件夹",
        });
        if (folders.Count > 0)
        {
            LoadFolder(folders[0].Path.LocalPath);
        }
    }

    private async Task OpenFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "选择图片",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图片文件")
                {
                    Patterns = ImageCatalog.ImageExtensions.Select(e => "*." + e).ToArray(),
                },
            },
        });
        if (files.Count > 0)
        {
            LoadFromPaths(files.Select(f => f.Path.LocalPath).ToArray());
        }
    }

    private void LoadFolder(string folder)
    {
        _settings.LastFolder = folder;
        SettingsStore.TouchRecentFolder(_settings, folder);
        SaveSettings();
        UpdateRecentMenu();
        LoadFromPaths(new[] { folder }, keepCurrent: true);
    }

    /// <summary>统一入口：文件/文件夹混合载入（拖放、菜单、启动参数共用）。</summary>
    private void LoadFromPaths(string[] paths, bool keepCurrent = false)
    {
        string? currentPath = keepCurrent && _current is not null ? _current.Path : null;
        var sort = _sort;
        int version = ++_loadVersion;
        _ = LoadFromPathsAsync(paths, currentPath, sort, version);
    }

    private async Task LoadFromPathsAsync(string[] paths, string? currentPath, PhotoSort sort, int version)
    {
        List<ImageEntry> collected;
        try
        {
            collected = await Task.Run(() => sort.Apply(ImageCatalog.CollectFromPaths(paths)));
        }
        catch (Exception ex)
        {
            LogError("LoadFromPathsAsync failed: " + ex.Message);
            if (version == _loadVersion) ShowToast("载入失败：" + ex.Message);
            return;
        }
        if (version != _loadVersion) return;
        if (collected.Count == 0)
        {
            // 空文件夹必须清空界面，否则主图/文件名/序号还挂着上一个文件夹的内容，看起来像没响应
            _images = new List<ImageEntry>();
            Strip.SetItems(_images, null, scrollIntoView: false);
            ShowEmpty();
            ShowToast("这个文件夹里没有可显示的图片");
            return;
        }
        PresentList(collected, currentPath);
    }

    private void PresentList(List<ImageEntry> collected, string? currentPath)
    {
        _images = collected;
        int startIndex = ImageCatalog.StartIndexFor(_images, currentPath);
        Strip.SetItems(_images, startIndex >= 0 ? _images[startIndex].FullPath : null, scrollIntoView: false);
        RebuildComparePicker();
        if (_compareOn && _comparePath is not null &&
            !_images.Any(i => PathEquals(i.FullPath, _comparePath)))
        {
            _comparePath = null;
            ComparePicker.SelectedIndex = -1;
            CompareViewer.ShowMessage("从上方下拉框选择对比图，或把缩略图拖到这里");
        }
        SelectIndex(startIndex, scrollStrip: false);
    }

    // MARK: 排序

    private void UpdateSortButton()
    {
        BtnSort.Content = "排序：" + _sort.Title();
    }

    private void BuildSortFlyout()
    {
        SortFlyout.Items.Clear();
        foreach (var sort in Enum.GetValues<PhotoSort>())
        {
            var item = new MenuItem
            {
                Header = (sort == _sort ? "✓　" : "　　") + sort.Title(),
            };
            var captured = sort;
            item.Click += (_, _) => ApplySort(captured);
            SortFlyout.Items.Add(item);
        }
    }

    private void ApplySort(PhotoSort sort)
    {
        if (_sort == sort) return;
        _sort = sort;
        _settings.SortRule = sort.ToString();
        SaveSettings();
        UpdateSortButton();

        string? currentPath = _current?.Path;
        _images = sort.Apply(_images);
        int index = ImageCatalog.StartIndexFor(_images, currentPath);
        Strip.SetItems(_images, currentPath, scrollIntoView: false);
        RebuildComparePicker();
        SelectIndex(index, scrollStrip: false);
    }

    // MARK: 选择与展示

    private void SelectIndex(int index, bool scrollStrip = true)
    {
        if (_images.Count == 0)
        {
            ShowEmpty();
            return;
        }
        if (index < 0) index = 0;
        if (index >= _images.Count) index = _images.Count - 1;

        _currentIndex = index;
        var entry = _images[index];
        NameLabel.Text = entry.Name;
        IndexLabel.Text = $"{index + 1} / {_images.Count}";
        // Windows 惯例：标题栏带当前文件名，Alt+Tab / 任务栏可辨认
        Title = $"{entry.Name} — PhotoLens";

        _ = LoadAndPresentAsync(entry);

        if (_compareOn && (_comparePath is null || PathEquals(_comparePath, entry.FullPath)))
        {
            int candidate = index + 1 < _images.Count ? index + 1 : index - 1;
            if (candidate >= 0 && candidate < _images.Count)
            {
                SetCompareImage(_images[candidate].FullPath, updatePicker: true);
            }
        }

        _thumbs.Prefetch(_images[(index + 1) % _images.Count].FullPath);
        _thumbs.Prefetch(_images[(index - 1 + _images.Count) % _images.Count].FullPath);

        Strip.SelectPath(entry.FullPath, scrollIntoView: scrollStrip);
        MainViewer.SetArrows(CanStep(-1), CanStep(1));
        if (_infoOn) RefreshInfo();
    }

    private async Task LoadAndPresentAsync(ImageEntry entry)
    {
        var cached = _imageStore.Get(entry.FullPath);
        if (cached is not null)
        {
            PresentCurrent(cached);
            return;
        }

        NameLabel.Text = entry.Name;
        var loaded = await ImageStore.LoadAsync(entry.FullPath);
        if (loaded is null)
        {
            if (IsStillCurrent(entry))
            {
                _current = null;
                MainViewer.ShowMessage("这张图片无法解码：" + entry.Name);
                if (_infoOn) RefreshInfo();
                ShowToast("无法解码 " + entry.Name);
            }
            return;
        }
        _imageStore.Put(loaded);
        if (IsStillCurrent(entry))
        {
            PresentCurrent(loaded);
        }
    }

    private bool IsStillCurrent(ImageEntry entry) =>
        _currentIndex >= 0 && _currentIndex < _images.Count
        && PathEquals(_images[_currentIndex].FullPath, entry.FullPath);

    private void PresentCurrent(FullImage image)
    {
        _current = image;
        MainViewer.Present(image);
        UpdateZoomLabel(MainViewer.Scale, MainViewer.IsFitted);
        if (_infoOn) RefreshInfo();
    }

    private void ShowEmpty()
    {
        _current = null;
        _currentIndex = -1;
        NameLabel.Text = "PhotoLens";
        IndexLabel.Text = "";
        ZoomLabel.Text = "";
        Title = "PhotoLens";
        MainViewer.ShowMessage("把图片或文件夹拖到这里\n或者按 Ctrl+O 打开");
        MainViewer.SetArrows(false, false);
        RebuildComparePicker();
    }

    private bool CanStep(int direction)
    {
        if (_images.Count == 0) return false;
        int next = _currentIndex + direction;
        return next >= 0 && next < _images.Count;
    }

    private void Step(int direction)
    {
        if (!CanStep(direction))
        {
            if (_slideshowOn && _images.Count > 0)
            {
                // 幻灯片模式：到末尾自动回到开头
                int next = _currentIndex + direction;
                if (next < 0) next = _images.Count - 1;
                if (next >= _images.Count) next = 0;
                SelectIndex(next);
            }
            return;
        }
        SelectIndex(_currentIndex + direction);
    }

    // MARK: 幻灯片

    private void ToggleSlideshow()
    {
        if (_images.Count < 2)
        {
            ShowToast("至少需要两张图片才能幻灯片");
            return;
        }
        if (_slideshowOn) StopSlideshow(); else StartSlideshow();
    }

    private void StartSlideshow()
    {
        _slideshowOn = true;
        BtnSlideshow.Classes.Add("toggleOn");
        _slideshowTimer.Start();
        ShowToast("开始幻灯片（3 秒/张，按 Esc 停止）");
    }

    private void StopSlideshow()
    {
        _slideshowOn = false;
        BtnSlideshow.Classes.Remove("toggleOn");
        _slideshowTimer.Stop();
    }

    // MARK: 对比模式

    private void ToggleCompare()
    {
        if (!_compareOn)
        {
            if (_images.Count < 2)
            {
                ShowToast("至少需要两张图片才能对比");
                return;
            }
            _compareOn = true;
            CompareArea.IsVisible = true;
            CompareDivider.IsVisible = true;
            ComparePicker.IsVisible = true;
            MainSplit.ColumnDefinitions = new ColumnDefinitions("1*,Auto,1*");
            BtnCompare.Classes.Add("toggleOn");

            // 自动选下一张作为对比（同图也允许，用户可能想对比不同缩放级别）
            int candidate = _currentIndex + 1 < _images.Count ? _currentIndex + 1 : 0;
            SetCompareImage(_images[candidate].FullPath, updatePicker: true);
        }
        else
        {
            _compareOn = false;
            _comparePath = null;
            CompareViewer.ShowMessage("");
            CompareArea.IsVisible = false;
            CompareDivider.IsVisible = false;
            ComparePicker.IsVisible = false;
            MainSplit.ColumnDefinitions = new ColumnDefinitions("1*,0,0");
            BtnCompare.Classes.Remove("toggleOn");
        }
        MainViewer.SetArrows(CanStep(-1), CanStep(1));
    }

    private void OnComparePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (!_compareOn || _rebuildingPicker) return;
        if (ComparePicker.SelectedItem is ImageEntry entry)
        {
            SetCompareImage(entry.FullPath, updatePicker: false);
        }
    }

    private void SetCompareImage(string path, bool updatePicker)
    {
        try
        {
            _comparePath = path;
            if (updatePicker)
            {
                int index = _images.FindIndex(i => PathEquals(i.FullPath, path));
                ComparePicker.SelectedIndex = index;
            }
            _ = PresentCompareAsync(path);
        }
        catch (Exception ex)
        {
            ShowToast($"对比图设置失败：{ex.Message}");
        }
    }

    private async Task PresentCompareAsync(string path)
    {
        try
        {
            var cached = _imageStore.Get(path);
            if (cached is null)
            {
                cached = await ImageStore.LoadAsync(path);
                if (cached is null)
                {
                    if (IsCurrentCompare(path))
                    {
                        _comparePath = null;
                        ComparePicker.SelectedIndex = -1;
                        CompareViewer.ShowMessage("这张图片无法解码：" + Path.GetFileName(path));
                    }
                    return;
                }
                _imageStore.Put(cached);
            }
            if (!IsCurrentCompare(path)) return;
            CompareViewer.Present(cached);
        }
        catch (Exception ex)
        {
            ShowToast($"对比展示失败：{ex.Message}");
        }
    }

    private bool IsCurrentCompare(string path) => _compareOn && PathEquals(_comparePath, path);

    private void HandleCompareDrop(string[] paths)
    {
        if (!_compareOn || paths.Length == 0) return;
        _ = HandleCompareDropAsync(paths);
    }

    private async Task HandleCompareDropAsync(string[] paths)
    {
        try
        {
            // 目录展开可能很慢，放后台线程，避免拖入大文件夹时 UI 假死
            var files = await Task.Run(() => ImageCatalog.CollectFromPaths(paths));
            if (files.Count == 0)
            {
                ShowToast("没有找到可对比的图片");
                return;
            }
            SetCompareImage(files[0].FullPath, updatePicker: false);
            ShowToast("已放入对比栏");
        }
        catch (Exception ex)
        {
            ShowToast($"拖入对比视图失败：{ex.Message}");
        }
    }

    /// <summary>拖入主画布：列表里有的就选中，没有的只展示不改列表（对齐 Mac 原版行为）。</summary>
    private void HandleMainDrop(string[] paths)
    {
        if (paths.Length == 0) return;
        _ = HandleMainDropAsync(paths);
    }

    private async Task HandleMainDropAsync(string[] paths)
    {
        try
        {
            var files = await Task.Run(() => ImageCatalog.CollectFromPaths(paths));
            if (files.Count == 0) { ShowToast("没有找到可显示的图片"); return; }

            string targetPath = files[0].FullPath;
            int idx = _images.FindIndex(i => PathEquals(i.FullPath, targetPath));
            if (idx >= 0) SelectIndex(idx);
            else await LoadAndDropInAsync(targetPath);
        }
        catch (Exception ex)
        {
            ShowToast($"拖入失败：{ex.Message}");
        }
    }

    private async Task LoadAndDropInAsync(string path)
    {
        try
        {
            var loaded = await ImageStore.LoadAsync(path);
            if (loaded is null)
            {
                ShowToast("无法解码 " + Path.GetFileName(path));
                return;
            }
            _imageStore.Put(loaded);
            // 必须登记为当前图：删除/另存为/复制都按 _current 定位，
            // 否则拖入预览后这些操作会作用到列表里上一张图上（误删真实文件）。
            _current = loaded;
            MainViewer.Present(loaded);
            NameLabel.Text = Path.GetFileName(path);
            IndexLabel.Text = "拖入预览";
            Title = $"{Path.GetFileName(path)} — PhotoLens";
            if (_infoOn) RefreshInfo();
        }
        catch (Exception ex)
        {
            ShowToast($"无法显示 {Path.GetFileName(path)}：{ex.Message}");
        }
    }

    private void RebuildComparePicker()
    {
        _rebuildingPicker = true;
        try
        {
            ComparePicker.ItemTemplate = new FuncDataTemplate<ImageEntry>(
                (entry, _) => new TextBlock
                {
                    Text = entry.Name,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                });
            ComparePicker.ItemsSource = _images;
            if (_comparePath is not null)
            {
                int index = _images.FindIndex(i => PathEquals(i.FullPath, _comparePath));
                ComparePicker.SelectedIndex = index;
                if (index < 0) ComparePicker.SelectedIndex = -1;
            }
            else
            {
                ComparePicker.SelectedIndex = -1;
            }
        }
        finally
        {
            _rebuildingPicker = false;
        }
    }

    // MARK: 信息面板

    private void ToggleInfo()
    {
        _infoOn = !_infoOn;
        InfoPanel.IsVisible = _infoOn;
        if (_infoOn) BtnInfo.Classes.Add("toggleOn"); else BtnInfo.Classes.Remove("toggleOn");
        if (_infoOn) RefreshInfo();
        UpdateNextArrowMargin();
    }

    /// <summary>信息面板高度随内容自适应，窗口较矮时会压住右箭头；此时把箭头左移让开面板。</summary>
    private void UpdateNextArrowMargin()
    {
        double arrowHeight = BtnNext.Bounds.Height > 0 ? BtnNext.Bounds.Height : 44;
        double arrowTop = (MainViewer.Bounds.Height - arrowHeight) / 2;
        // 面板与箭头都用整窗行的坐标比较：对比模式下画布变窄，面板可能已经不在箭头正上方
        bool covered = false;
        if (InfoPanel.IsVisible && InfoPanel.Bounds.Height > 0 && InfoPanel.Parent is Visual row
            && MainViewer.TranslatePoint(new Point(0, 0), row) is { } origin)
        {
            double arrowRight = origin.X + MainViewer.Bounds.Width - 14;
            bool vertical = origin.Y + arrowTop < InfoPanel.Bounds.Top + InfoPanel.Bounds.Height;
            bool horizontal = InfoPanel.Bounds.X < arrowRight;
            covered = vertical && horizontal;
        }
        double right = covered ? InfoPanel.Bounds.Width + 18 + 10 : 14;
        // LayoutUpdated 会高频触发，值没变就不要改，避免反复重新布局
        if (Math.Abs(BtnNext.Margin.Right - right) < 0.5) return;
        BtnNext.Margin = new Thickness(0, 0, right, 0);
    }

    private void RefreshInfo()
    {
        if (_current is null)
        {
            InfoText.Text = "";
            return;
        }
        var image = _current;
        var lines = new List<string>();

        // 评分条目按两列排版：原来一行塞 5 项，窄面板里会被拆得断词
        var items = image.Quality.Items;
        if (items.Length == 0)
        {
            lines.Add("画质评分 不可用");
        }
        else
        {
            lines.Add($"画质评分 {image.Quality.Score:F1} / 10");
            for (int i = 0; i < items.Length; i += 2)
            {
                string left = $"{items[i].Name} {items[i].Score:F1}";
                string right = i + 1 < items.Length ? $"    {items[i + 1].Name} {items[i + 1].Score:F1}" : "";
                lines.Add("  " + left + right);
            }
        }
        lines.Add("");
        // 用解码时预缓存的 directories，避免重复磁盘读取
        lines.AddRange(ExifLines.Build(image.Path, image.PixelWidth, image.PixelHeight, image.ModifiedUtc, image.CachedDirectories));
        InfoText.Text = string.Join('\n', lines);
    }

    // MARK: 列表显隐

    private void ToggleList()
    {
        _showStrip = !_showStrip;
        ApplyListVisibility();
        _settings.ShowFilmstrip = _showStrip;
        SaveSettings();
    }

    private void ApplyListVisibility()
    {
        Strip.IsVisible = _showStrip;
        var grid = Strip.Parent as Grid;
        if (grid is null) return;
        var splitter = grid.Children.OfType<GridSplitter>().FirstOrDefault();
        if (splitter is not null) splitter.IsVisible = _showStrip;

        if (_showStrip)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("232,Auto,1*");
            if (grid.ColumnDefinitions[0].Width.Value >= 120)
                grid.ColumnDefinitions[0].Width = new GridLength(_stripWidth);
            ApplyColumnConstraints();
            BtnList.Content = "隐藏列表";
        }
        else
        {
            // 记住用户拖好的宽度，重新显示时不要跳回默认值
            if (grid.ColumnDefinitions.Count > 0 && grid.ColumnDefinitions[0].ActualWidth >= 120)
                _stripWidth = grid.ColumnDefinitions[0].ActualWidth;
            grid.ColumnDefinitions = new ColumnDefinitions("0,0,1*");
            BtnList.Content = "显示列表";
        }
    }

    // MARK: 提示与底部栏

    private void ShowToast(string message)
    {
        HintLabel.Text = message;
        HintLabel.Classes.Add("hintToast");
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ResetHint()
    {
        _toastTimer.Stop();
        HintLabel.Classes.Remove("hintToast");
        HintLabel.Text = DefaultHint;
    }

    private void UpdateZoomLabel(double scale, bool fitted)
    {
        ZoomLabel.Text = scale <= 0
            ? ""
            : (fitted ? $"适合窗口 {Math.Round(scale * 100)}%" : $"{Math.Round(scale * 100)}%");
    }

    /// <summary>适合窗口/实际大小按钮激活态：IsFitted 决定哪个高亮</summary>
    private void UpdateFitButtonState()
    {
        // 只有真正处在 100% 才点亮"实际大小"；滚动缩放到 37% 之类不应算激活
        bool isActual = !MainViewer.IsFitted && Math.Abs(MainViewer.Scale - 1.0) < 1e-3;
        bool isFit = MainViewer.IsFitted;
        if (isFit) BtnFit.Classes.Add("toggleOn"); else BtnFit.Classes.Remove("toggleOn");
        if (isActual) BtnActual.Classes.Add("toggleOn"); else BtnActual.Classes.Remove("toggleOn");
    }

    // MARK: 键盘

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Step(-1); e.Handled = true; break;
            case Key.Right: Step(1); e.Handled = true; break;
            // Windows 惯例：Home / End 跳首尾，Delete 删除到回收站，F5 幻灯片
            case Key.Home: if (_images.Count > 0) { SelectIndex(0); e.Handled = true; } break;
            case Key.End: if (_images.Count > 0) { SelectIndex(_images.Count - 1); e.Handled = true; } break;
            case Key.Delete: _ = DeleteCurrentFileAsync(); e.Handled = true; break;
            case Key.F5: ToggleSlideshow(); e.Handled = true; break;
            case Key.Space:
                if (MainViewer.Current is not null)
                {
                    if (MainViewer.IsFitted) MainViewer.SetActualSize(); else MainViewer.FitToWindow();
                }
                e.Handled = true; break;
            case Key.F: MainViewer.FitToWindow(); e.Handled = true; break;
            case Key.F11: ToggleFullscreen(); e.Handled = true; break;
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                _ = OpenFilesAsync(); e.Handled = true; break;
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _ = OpenFolderAsync(); e.Handled = true; break;
            case Key.I: ToggleInfo(); e.Handled = true; break;
            case Key.L: ToggleList(); e.Handled = true; break;
            case Key.R when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                // Windows 照片用 Ctrl+R 旋转；保留单键 R 兼容原快捷键
                MainViewer.RotateClockwise(); e.Handled = true; break;
            case Key.R: MainViewer.RotateClockwise(); e.Handled = true; break;
            case Key.D9: MainViewer.FitToWindow(); e.Handled = true; break;
            case Key.D0 or Key.D1: MainViewer.SetActualSize(); e.Handled = true; break;
            case Key.OemCloseBrackets or Key.PageDown: Step(1); e.Handled = true; break;
            case Key.OemOpenBrackets or Key.PageUp: Step(-1); e.Handled = true; break;
            case Key.OemPlus or Key.Add: MainViewer.ZoomBy(1.25); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract: MainViewer.ZoomBy(0.8); e.Handled = true; break;
            case Key.Z when e.KeyModifiers == KeyModifiers.None:
                MainViewer.ToggleLoupe(true); e.Handled = true; break;
            case Key.Escape:
                if (_slideshowOn) { StopSlideshow(); e.Handled = true; break; }
                if (WindowState == WindowState.FullScreen)
                {
                    WindowState = WindowState.Normal;
                    e.Handled = true;
                    break;
                }
                if (_compareOn) { ToggleCompare(); e.Handled = true; }
                break;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.None)
        {
            MainViewer.ToggleLoupe(false);
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    // MARK: 右键菜单

    private ContextMenu BuildViewerMenu(bool isMain)
    {
        var menu = new ContextMenu();
        var items = new List<MenuItem>();

        var copyBitmap = new MenuItem { Header = "复制图像" };
        copyBitmap.Click += async (_, _) => await CopyCurrentBitmapAsync(isMain);
        items.Add(copyBitmap);

        var copyFile = new MenuItem { Header = "复制文件" };
        copyFile.Click += async (_, _) => await CopyCurrentFile(isMain);
        items.Add(copyFile);

        var copyPath = new MenuItem { Header = "复制文件路径" };
        copyPath.Click += async (_, _) => await CopyTextAsync(PathOf(isMain) ?? "");
        items.Add(copyPath);

        var reveal = new MenuItem { Header = "打开文件所在的位置" };
        reveal.Click += (_, _) => RevealInExplorer(isMain);
        items.Add(reveal);

        // Windows 资源管理器右键惯例：打开方式 / 属性
        var openWith = new MenuItem { Header = "打开方式…" };
        openWith.Click += (_, _) => LaunchShellVerb(isMain, "openas");
        items.Add(openWith);

        var properties = new MenuItem { Header = "属性" };
        properties.Click += (_, _) => LaunchShellVerb(isMain, "properties");
        items.Add(properties);

        var saveJpeg = new MenuItem { Header = "另存为 JPEG…" };
        saveJpeg.Click += async (_, _) => await SaveCurrentAsAsync(isMain, jpeg: true);
        items.Add(saveJpeg);

        var savePng = new MenuItem { Header = "另存为 PNG…" };
        savePng.Click += async (_, _) => await SaveCurrentAsAsync(isMain, jpeg: false);
        items.Add(savePng);

        if (isMain)
        {
            // 删除只在主菜单出现，对比栏不需要
            var deleteItem = new MenuItem { Header = "移到回收站" };
            deleteItem.Click += async (_, _) => await DeleteCurrentFileAsync();
            items.Add(deleteItem);
        }

        foreach (var item in items)
        {
            menu.Items.Add(item);
        }
        menu.Opening += (_, _) =>
        {
            bool hasImage = PathOf(isMain) is not null;
            foreach (var item in items) item.IsEnabled = hasImage;
        };
        return menu;
    }

    private string? PathOf(bool isMain) => (isMain ? MainViewer.Current : CompareViewer.Current)?.Path;

    private async Task CopyTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(text);
        ShowToast("已复制文件路径");
    }

    private async Task CopyCurrentFile(bool isMain)
    {
        var path = PathOf(isMain);
        if (path is null) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        try
        {
            var data = new DataObject();
            data.Set(DataFormats.FileNames, new[] { path });
            await clipboard.SetDataObjectAsync(data);
            ShowToast("已复制文件");
        }
        catch
        {
            ShowToast("复制文件失败");
        }
    }

    private async Task CopyCurrentBitmapAsync(bool isMain)
    {
        var image = isMain ? MainViewer.Current : CompareViewer.Current;
        if (image is null) return;
        MemoryStream? ms = null;
        try
        {
            ms = new MemoryStream();
            image.Bitmap.Save(ms);
            ms.Position = 0;
            var data = new DataObject();
            data.Set("PNG", ms);
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                ms.Dispose();
                return;
            }
            await clipboard.SetDataObjectAsync(data);
            _clipboardPng?.Dispose();
            _clipboardPng = ms;
            ShowToast("已复制图像");
        }
        catch
        {
            if (!ReferenceEquals(_clipboardPng, ms)) ms?.Dispose();
            ShowToast("复制图像失败（剪贴板不支持）");
        }
    }

    private void RevealInExplorer(bool isMain)
    {
        var path = PathOf(isMain);
        if (path is null) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path) ?? "/") { UseShellExecute = true });
            }
        }
        catch
        {
            ShowToast("无法打开文件所在位置");
        }
    }

    /// <summary>调用 Windows Shell 动词（openas / properties），与资源管理器右键菜单一致。</summary>
    private void LaunchShellVerb(bool isMain, string verb)
    {
        var path = PathOf(isMain);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = verb });
        }
        catch (Exception ex)
        {
            LogError($"shell verb {verb} failed: {ex.Message}");
            ShowToast("操作失败：" + ex.Message);
        }
    }

    private async Task SaveCurrentAsAsync(bool isMain, bool jpeg)
    {
        var image = isMain ? MainViewer.Current : CompareViewer.Current;
        if (image is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存为 " + (jpeg ? "JPEG" : "PNG"),
            SuggestedFileName = Path.GetFileNameWithoutExtension(image.Path) + (jpeg ? ".jpg" : ".png"),
            FileTypeChoices = new[]
            {
                new FilePickerFileType(jpeg ? "JPEG 图像" : "PNG 图像")
                {
                    Patterns = new[] { jpeg ? "*.jpg" : "*.png" },
                },
            },
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            using var skImage = SkiaSharp.SKImage.FromBitmap(image.Source);
            var encoded = skImage.Encode(
                jpeg ? SkiaSharp.SKEncodedImageFormat.Jpeg : SkiaSharp.SKEncodedImageFormat.Png,
                92);
            using (encoded) { encoded.SaveTo(stream); }
            ShowToast("已保存 " + file.Name);
        }
        catch (Exception ex)
        {
            LogError("SaveCurrentAsAsync failed: " + ex.Message);
            ShowToast("保存失败：" + file.Name);
        }
    }

    /// <summary>删除当前图片到回收站，并从列表同步移除。</summary>
    private async Task DeleteCurrentFileAsync()
    {
        if (_current is null || string.IsNullOrEmpty(_current.Path)) return;
        var path = _current.Path;
        // 按路径定位列表行：当前画面可能是拖入的预览图，_currentIndex 并不指向它
        int listIndex = _images.FindIndex(i => PathEquals(i.FullPath, path));
        int versionAtStart = _loadVersion;
        bool canceled = false;
        try
        {
            await Task.Run(() =>
            {
                // Windows 照片删除到回收站时不弹系统确认框（可回收，失败/误删有 Toast 与回收站兜底）
                int result = NativeMethods.DeleteFileToRecycleBin(path, confirm: false);
                if (result == 2) canceled = true; // DE_RESABORTED / 用户取消
                if (result != 0 && result != 2)
                    throw new InvalidOperationException("SHFileOperation failed: " + result);
            });
            if (canceled) return;

            // 顺序很关键：先把画面从位图上摘掉，再摘缓存并释放。
            // 反过来会在一帧渲染里用到已释放的位图（同图对比时两个视图共享同一实例，必崩）。
            DetachImage(path);
            _imageStore.Remove(path)?.Dispose();

            bool listRowStillValid = listIndex >= 0 && versionAtStart == _loadVersion
                && listIndex < _images.Count && PathEquals(_images[listIndex].FullPath, path);

            if (listRowStillValid)
            {
                _images.RemoveAt(listIndex);
                if (_comparePath is not null && PathEquals(_comparePath, path)) _comparePath = null;
                Strip.SetItems(_images, null, scrollIntoView: false);
                RebuildComparePicker();
                if (_images.Count > 0) SelectIndex(Math.Min(listIndex, _images.Count - 1));
                else ShowEmpty();
            }
            else
            {
                // 拖入预览图，或列表在确认框期间已被切换：只删文件，不动当前列表
                if (_comparePath is not null && PathEquals(_comparePath, path))
                {
                    _comparePath = null;
                    RebuildComparePicker();
                }
                if (_images.Count > 0 && _currentIndex >= 0 && _currentIndex < _images.Count)
                    SelectIndex(_currentIndex);
            }
            ShowToast("已移到回收站：" + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            LogError("DeleteCurrentFileAsync failed: " + ex.Message);
            ShowToast("删除失败：" + ex.Message);
        }
    }

    /// <summary>把指定路径从两个画布上撤下来（只清 Source，不释放位图）。</summary>
    private void DetachImage(string path)
    {
        if (MainViewer.Current is { } main && PathEquals(main.Path, path))
            MainViewer.ShowMessage("已移到回收站");
        if (CompareViewer.Current is { } compare && PathEquals(compare.Path, path))
            CompareViewer.ShowMessage("对比图已移除");
        if (_current is not null && PathEquals(_current.Path, path)) _current = null;
    }

    /// <summary>Windows 路径比较统一忽略大小写，避免不同来源的大小写差异导致选中/删除错位。</summary>
    private static bool PathEquals(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>WindowIcon 从 app.png 文件读取，与 exe 的 ApplicationIcon (app.ico) 同源。
    /// 改图标只需换 app.ico + app.png 两个文件，两边自动同步。</summary>
    private static Bitmap? GenerateAppIcon()
    {
        // 单文件发布时 Assembly.Location 为空，必须用 AppContext.BaseDirectory
        try
        {
            var pngPath = Path.Combine(AppContext.BaseDirectory, "app.png");
            if (!File.Exists(pngPath)) return null;
            using var stream = File.OpenRead(pngPath);
            return new Bitmap(stream);
        }
        catch
        {
            // 图标缺失不应阻止启动（历史上有过因此白屏的情况）
            return null;
        }
    }

    /// <summary>Windows Shell API：SHFileOperation 删除到回收站。零 NuGet 依赖。</summary>
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public int wFunc;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string pFrom;
            public IntPtr pTo;
            public int fFlags;
            public int fAnyOps;
            public IntPtr hNameMappings;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        private const int FO_DELETE = 3;
        private const int FOF_ALLOWUNDO = 0x40;
        private const int FOF_NOCONFIRMATION = 0x10;   // 跳过确认框（我们自己做）
        private const int FOF_SILENT = 0x0004;

        [System.Runtime.InteropServices.DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCTW FileOp);

        /// <returns>0=成功；2=用户取消（FOF_NOCONFIRMATION 下不会出现）；其他=错误码</returns>
        public static int DeleteFileToRecycleBin(string path, bool confirm)
        {
            var op = new SHFILEOPSTRUCTW
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = path + "\0\0", // 双 null 结尾
                fFlags = FOF_ALLOWUNDO | (confirm ? 0 : FOF_NOCONFIRMATION) | FOF_SILENT,
            };
            return SHFileOperation(ref op);
        }
    }

    // MARK: 设置

    private void SaveSettings()
    {
        try
        {
            SaveWindowBounds();
            SettingsStore.Save(_settings);
        }
        catch
        {
            // 持久化失败不影响使用
        }
    }

    // MARK: 日志

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PhotoLensWin", "error.log");

    private static void LogError(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* 日志写不了就算了 */ }
    }
}
