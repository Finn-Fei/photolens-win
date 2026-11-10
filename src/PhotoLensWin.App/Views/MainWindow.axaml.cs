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

namespace PhotoLensWin.App.Views;

public partial class MainWindow : Window
{
    private const string DefaultHint = "←/→ 切换图片 · 双击缩放 · Ctrl+滚轮缩放 · 拖入图片或文件夹";

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

    public MainWindow()
    {
        InitializeComponent();
        Strip.Store = _thumbs;
        WireControls();
        _toastTimer.Tick += (_, _) => ResetHint();
        _slideshowTimer.Tick += (_, _) => Step(1);
    }

    // MARK: 启动

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
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

        BtnFit.Click += (_, _) => MainViewer.FitToWindow();
        BtnActual.Click += (_, _) => MainViewer.SetActualSize();
        BtnPrev.Click += (_, _) => Step(-1);
        BtnNext.Click += (_, _) => Step(1);

        MainViewer.OnDropFiles += paths => LoadFromPaths(paths);
        CompareViewer.OnDropFiles += HandleCompareDrop;
        MainViewer.ScaleChanged += scale => UpdateZoomLabel(scale, MainViewer.IsFitted);
        MainViewer.ArrowsChanged += (prev, next) =>
        {
            BtnPrev.IsVisible = prev && !_compareOn;
            BtnNext.IsVisible = next && !_compareOn;
        };

        Strip.SelectionChanged += entry =>
        {
            int index = _images.FindIndex(e => ReferenceEquals(e, entry) || e.FullPath == entry.FullPath);
            if (index >= 0) SelectIndex(index);
        };
        ComparePicker.SelectionChanged += OnComparePicked;

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
            ShowToast("没有找到可显示的图片");
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
            _images.All(i => i.FullPath != _comparePath))
        {
            _comparePath = null;
            ComparePicker.SelectedIndex = -1;
            CompareViewer.ShowMessage("在下方列表中选择一张图片进行对比");
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

        _ = LoadAndPresentAsync(entry);

        if (_compareOn && (_comparePath is null || _comparePath == entry.FullPath))
        {
            int candidate = index + 1 < _images.Count ? index + 1 : index - 1;
            if (candidate >= 0 && candidate < _images.Count)
            {
                SetCompareImage(_images[candidate].FullPath, updatePicker: true);
            }
        }

        _thumbs.Prefetch(_images[(index + 1) % _images.Count].FullPath);
        _thumbs.Prefetch(_images[(index - 1 + _images.Count) % _images.Count].FullPath);

        if (scrollStrip) Strip.ScrollToSelection();
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
        && _images[_currentIndex].FullPath == entry.FullPath;

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
            MainSplit.ColumnDefinitions = new ColumnDefinitions("1*,Auto,1*");
            BtnCompare.Classes.Add("toggleOn");

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
        _comparePath = path;
        if (updatePicker)
        {
            int index = _images.FindIndex(i => i.FullPath == path);
            ComparePicker.SelectedIndex = index;
        }
        _ = PresentCompareAsync(path);
    }

    private async Task PresentCompareAsync(string path)
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

    private bool IsCurrentCompare(string path) => _compareOn && _comparePath == path;

    private void HandleCompareDrop(string[] paths)
    {
        if (!_compareOn) return;
        var files = ImageCatalog.CollectFromPaths(paths);
        if (files.Count == 0)
        {
            ShowToast("没有找到可对比的图片");
            return;
        }
        SetCompareImage(files[0].FullPath, updatePicker: false);
        ComparePicker.SelectedIndex = -1;
        ShowToast("已放入对比栏（未加入列表）");
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
                int index = _images.FindIndex(i => i.FullPath == _comparePath);
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
        lines.AddRange(image.Quality.Lines);
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
        StripDivider.IsVisible = _showStrip;
        Strip.Width = _showStrip ? 232 : 0;
        if (_showStrip)
        {
            Strip.Margin = new Thickness(10, 10, 0, 10);
            BtnList.Content = "隐藏列表";
        }
        else
        {
            Strip.Margin = new Thickness(0);
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

    // MARK: 键盘

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Step(-1); e.Handled = true; break;
            case Key.Right: Step(1); e.Handled = true; break;
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
                }
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

        var reveal = new MenuItem { Header = "打开文件所在位置" };
        reveal.Click += (_, _) => RevealInExplorer(isMain);
        items.Add(reveal);

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
        bool canceled = false;
        try
        {
            await Task.Run(() =>
            {
                int result = NativeMethods.DeleteFileToRecycleBin(path, confirm: true);
                if (result == 2) canceled = true; // DE_RESABORTED / 用户取消
                if (result != 0 && result != 2)
                    throw new InvalidOperationException("SHFileOperation failed: " + result);
            });
            if (canceled) return;

            _images.RemoveAt(_currentIndex);
            Strip.SetItems(_images, null, scrollIntoView: false);
            RebuildComparePicker();

            _imageStore.Get(path)?.Dispose();
            if (_images.Count > 0)
            {
                int next = Math.Min(_currentIndex, _images.Count - 1);
                SelectIndex(next);
            }
            else
            {
                ShowEmpty();
            }
            ShowToast("已移到回收站：" + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            LogError("DeleteCurrentFileAsync failed: " + ex.Message);
            ShowToast("删除失败：" + ex.Message);
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
