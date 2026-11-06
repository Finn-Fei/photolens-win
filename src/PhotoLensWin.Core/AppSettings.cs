using System;
using System.IO;
using System.Text.Json;

namespace PhotoLensWin.Core;

/// <summary>
/// 用户设置：排序规则、列表显隐、上次打开的文件夹。
/// JSON 存放在 %APPDATA%/PhotoLensWin（mac 本地测试时为 Application Support）。
/// </summary>
public sealed class AppSettings
{
    public string SortRule { get; set; } = nameof(PhotoSort.NameAscending);
    public bool ShowFilmstrip { get; set; } = true;
    public string? LastFolder { get; set; }
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
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
