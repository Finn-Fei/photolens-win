using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PhotoLensWin.App.Views;

namespace PhotoLensWin.App;

internal static class Program
{
    private const string InstanceMutexName = @"Local\PhotoLensWin.App.SingleInstance";
    private const string OpenRequestPipeName = "PhotoLensWin.OpenRequest";

    [STAThread]
    public static void Main(string[] args)
    {
        // 日志文件在上一实例仍运行时会被占用，删除失败不能让进程在无窗口状态静默退出
        var logPath = Path.Combine(Path.GetTempPath(), "photolens-crash.log");
        try { File.Delete(logPath); } catch { }

        void Log(string msg)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {Sanitize(msg)}{Environment.NewLine}"); }
            catch { }
        }

        // 单实例：再次双击 exe 时把已有窗口唤到前台，而不是起一个什么都不显示的新进程。
        // 注意用 WaitOne(0) 抢所有权而不是看 createdNew：任何同会话进程只要先创建同名 Mutex
        // （不需要持有）就能让正版 isFirstInstance=false，从而"静默不显示窗口"。
        using var mutex = new Mutex(initiallyOwned: false, InstanceMutexName, out _);
        bool isFirstInstance;
        try
        {
            isFirstInstance = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // 上一个实例崩溃时被遗弃：所有权已归本进程
            isFirstInstance = true;
        }
        if (!isFirstInstance)
        {
            Log("检测到已有实例，转发打开请求后退出");
            // 有路径参数时先尝试转交给首实例，转交失败再退回"只激活窗口"
            if (!TrySendOpenRequest(args)) ActivateExistingWindow();
            return;
        }

        StartOpenRequestServer(Log);

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

    /// <summary>第二实例：把路径参数通过命名管道交给首实例；没有参数或不成功时返回 false。</summary>
    private static bool TrySendOpenRequest(string[] args)
    {
        if (args.Length == 0) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", OpenRequestPipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write(string.Join('\n', args));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>首实例：监听第二实例的打开请求，收到后交给主窗口载入并激活。</summary>
    private static void StartOpenRequestServer(Action<string> log)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    // CurrentUserOnly：把管道 ACL 限制为当前用户，避免其它本地进程往首实例投递任意路径
                    await using var server = new NamedPipeServerStream(
                        OpenRequestPipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server);
                    // 定长读取：ReadToEndAsync 没有上限，恶意对端可以灌入 GB 级数据或
                    // 干脆不发 EOF 把服务端永久挂起
                    var payload = await ReadPayloadAsync(reader);
                    var paths = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    if (paths.Length == 0) continue;
                    log("收到外部打开请求：" + string.Join(" | ", paths));
                    await Dispatcher.UIThread.InvokeAsync(() => DispatchOpenRequest(paths, log));
                }
                catch (Exception ex)
                {
                    log("[OpenRequest] " + ex.Message);
                    await Task.Delay(1000);
                }
            }
        });
    }

    /// <summary>首实例可能还在启动，窗口出现前先等一小会儿再载入。</summary>
    private static async void DispatchOpenRequest(string[] paths, Action<string> log)
    {
        for (int i = 0; i < 30; i++)
        {
            if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
                is MainWindow window)
            {
                window.OpenPathsFromExternal(paths);
                return;
            }
            await Task.Delay(100);
        }
        log("未找到主窗口，放弃外部打开请求");
    }

    /// <summary>打开请求的最大字符数（防御性上限，正常路径参数远小于此）。</summary>
    private const int MaxOpenRequestChars = 64 * 1024;

    /// <summary>带上限地读取请求内容，避免无界限的 ReadToEndAsync。
    /// 读操作带超时：同会话任意进程连上后不发数据即可永久占用唯一管道实例，
    /// 之后"双击图片打开"会静默失效。</summary>
    private static async Task<string> ReadPayloadAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var sb = new System.Text.StringBuilder();
        while (sb.Length < MaxOpenRequestChars)
        {
            int free = MaxOpenRequestChars - sb.Length;
            int read;
            try
            {
                read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, free)))
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                break; // 对端迟迟不发数据：按空请求处理，释放管道实例
            }
            if (read <= 0) break;
            sb.Append(buffer, 0, read);
        }
        return sb.ToString();
    }

    /// <summary>剥离控制字符并截断，避免日志行被注入/无限增长。</summary>
    private static string Sanitize(string message)
    {
        const int maxLength = 4000;
        if (message.Length > maxLength) message = message[..maxLength] + "…";
        var sb = new System.Text.StringBuilder(message.Length);
        foreach (char c in message)
        {
            sb.Append(char.IsControl(c) && c != '\n' && c != '\r' && c != '\t' ? ' ' : c);
        }
        return sb.ToString();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
