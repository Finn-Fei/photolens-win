using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;

namespace PhotoLensWin.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "photolens-crash.log");
        File.Delete(logPath);

        void Log(string msg)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); }
            catch { }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log($"[AppDomain] {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log($"[UnobservedTask] {e.Exception}");
            e.SetObserved();
        };

        Log("=== PhotoLensWin 启动 ===");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log($"[Main] {ex}");
            throw;
        }
        Log("=== PhotoLensWin 正常退出 ===");
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
