using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PhotoLensWin.Core;

/// <summary>
/// 用户设置：排序规则、列表显隐、上次打开的文件夹、窗口位置、主题。
/// JSON 存放在 %APPDATA%/PhotoLensWin（mac 本地测试时为 Application Support）。
/// </summary>
public sealed class AppSettings
{
    public string SortRule { get; set; } = nameof(PhotoSort.NameAscending);
    public bool ShowFilmstrip { get; set; } = true;
    public string? LastFolder { get; set; }

    // 新增：窗口位置/尺寸持久化
    public int WindowLeft { get; set; } = -1;
    public int WindowTop { get; set; } = -1;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;

    // 新增：暗色/亮色主题
    public string Theme { get; set; } = "Dark";

    // 新增：最近打开的文件夹（上限 10）
    public List<string> RecentFolders { get; set; } = new();

    public const int MaxRecentFolders = 10;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhotoLensWin",
            "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            if (settings is null) return new AppSettings();
            // 旧版本设置文件无 RecentFolders / Theme：补默认值
            settings.RecentFolders ??= new List<string>();
            if (string.IsNullOrEmpty(settings.Theme)) settings.Theme = "Dark";
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // 持久化失败不影响使用
        }
    }

    /// <summary>打开文件夹时调用：把它顶到最近列表第一位，去重，保持上限。</summary>
    public static void TouchRecentFolder(AppSettings settings, string folder)
    {
        var list = settings.RecentFolders;
        list.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, folder);
        if (list.Count > AppSettings.MaxRecentFolders)
            list.RemoveRange(AppSettings.MaxRecentFolders, list.Count - AppSettings.MaxRecentFolders);
    }
}
