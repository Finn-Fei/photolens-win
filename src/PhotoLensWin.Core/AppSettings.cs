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

    // 新增：暗色/亮色/跟随系统主题（System / Dark / Light）
    public string Theme { get; set; } = "Dark";

    /// <summary>鼠标滚轮行为：Zoom=缩放，Navigate=上一张/下一张（对齐 Windows 照片的可选项）。</summary>
    public string WheelMode { get; set; } = "Zoom";

    // 新增：最近打开的文件夹（上限 10）
    public List<string> RecentFolders { get; set; } = new();

    public const int MaxRecentFolders = 10;

    /// <summary>设置文件大小上限：异常或被填充的巨大文件直接忽略，避免启动即读入超大内容。</summary>
    public const long MaxSettingsFileBytes = 1024 * 1024;
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
            // 超出上限的设置文件按损坏处理：不把它整份读进内存
            if (new FileInfo(path).Length > AppSettings.MaxSettingsFileBytes) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            if (settings is null) return new AppSettings();
            // 旧版本设置文件无 RecentFolders / Theme：补默认值
            settings.RecentFolders = NormalizeRecentFolders(settings.RecentFolders ?? new List<string>());
            if (string.IsNullOrEmpty(settings.Theme)) settings.Theme = "Dark";
            if (string.IsNullOrEmpty(settings.WheelMode)) settings.WheelMode = "Zoom";
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
        settings.RecentFolders = NormalizeRecentFolders(list);
    }

    /// <summary>丢弃空项、按大小写不敏感去重、截断到上限，并保持原有顺序。</summary>
    public static List<string> NormalizeRecentFolders(IEnumerable<string> folders)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            if (!seen.Add(folder)) continue;
            result.Add(folder);
            if (result.Count >= AppSettings.MaxRecentFolders) break;
        }
        return result;
    }
}
