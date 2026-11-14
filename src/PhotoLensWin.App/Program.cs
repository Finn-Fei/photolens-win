using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;

namespace PhotoLensWin.App;

internal static class Program
{
    private const string InstanceMutexName = @"Local\PhotoLensWin.App.SingleInstance";

    [STAThread]
    public static void Main(string[] args)
    {
        // 日志文件在上一实例仍运行时会被占用，删除失败不能让进程在无窗口状态静默退出
        var logPath = Path.Combine(Path.GetTempPath(), "photolens-crash.log");
        try { File.Delete(logPath); } catch { }

        void Log(string msg)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); }
            catch { }
        }

        // 单实例：再次双击 exe 时把已有窗口唤到前台，而不是起一个什么都不显示的新进程
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Log("检测到已有实例，尝试激活窗口后退出");
            ActivateExistingWindow();
            return;
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
        finally
        {
            try { mutex.ReleaseMutex(); } catch { }
        }
        Log("=== PhotoLensWin 正常退出 ===");
    }

    /// <summary>找到已在运行的同一程序窗口并置前（纯 Win32，无额外依赖）。</summary>
    private static void ActivateExistingWindow()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            int self = Environment.ProcessId;
            var other = Process.GetProcessesByName("PhotoLensWin.App").FirstOrDefault(p => p.Id != self);
            if (other is null) return;

            var handle = other.MainWindowHandle;
            // 上一实例可能还在初始化，窗口句柄稍后才出现
            for (int i = 0; i < 20 && handle == IntPtr.Zero; i++)
            {
                Thread.Sleep(100);
                other.Refresh();
                handle = other.MainWindowHandle;
            }
            if (handle == IntPtr.Zero) return;

            ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
        }
        catch
        {
            // 激活失败不影响正常启动（下次仍会尝试）
        }
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
