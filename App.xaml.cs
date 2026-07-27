using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace CleanMaster;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    // 诊断日志统一写到 %APPDATA%\CleanMaster\logs\startup.log
    // 故意写两份路径变量: LogDir 是用户可见目录, CrashFile 是崩溃快照
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CleanMaster", "logs");

    private static readonly string LogFile = Path.Combine(LogDir, "startup.log");
    private static readonly string CrashFile = Path.Combine(LogDir, "crash.log");

    // 互斥锁保证同一时刻只有一个 CleanMaster 实例在写日志 (多线程异常并行场景)
    private static readonly object LogLock = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // [诊断点 1] 最早日志: WPF 启动回调进入
        DiagnosticLog("OnStartup enter");

        try
        {
            base.OnStartup(e);
            DiagnosticLog("OnStartup base called");

            // [诊断点 2] 注册全局异常处理器 (放在 CompositionRoot 之前, 防止 DI 阶段崩溃丢失)
            EnsureGlobalExceptionHandlers();

            // [诊断点 3] 检查关键运行环境前置条件
            DiagnosticLogEnvironment();

            // [诊断点 4] DI 容器构建
            DiagnosticLog("Building DI container...");
            Services = CompositionRoot.Configure();
            DiagnosticLog("DI container built");

            // [诊断点 5] 创建主窗口
            DiagnosticLog("Creating MainWindow...");
            var mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<ViewModels.MainViewModel>()
            };
            DiagnosticLog("MainWindow created, showing...");

            mainWindow.Show();
            DiagnosticLog("MainWindow shown - startup complete");
        }
        catch (Exception ex)
        {
            // 启动阶段任何异常都立即记录, 然后弹窗给用户一个明确反馈
            LogError("OnStartup", ex);
            try
            {
                MessageBox.Show(
                    $"CleanMaster 启动失败:\n\n{ex.Message}\n\n" +
                    $"详细日志已保存到:\n{LogFile}\n\n" +
                    $"请把日志文件发送给作者以便排查问题。",
                    "CleanMaster 启动错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
            Shutdown(1);
        }
    }

    public App()
    {
        // [诊断点 0] App 构造函数 - 这是托管代码能跑的最早位置
        // 单文件模式下, 到这里说明 hostfxr 已经成功加载了 .NET 运行时
        DiagnosticLog("==== App constructor enter ====");

        try
        {
            // Register encoding provider for GBK and other non-Unicode encodings
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            DiagnosticLog("Encoding provider registered");

            EnsureGlobalExceptionHandlers();

            // Startup log
            Log($"Application starting...");
            Log($"OS: {Environment.OSVersion}");
            Log($"Runtime: {Environment.Version}");
            Log($"ProcessPath: {Environment.ProcessPath}");
            Log($"ProcessId: {Environment.ProcessId}");
            Log($"Is64BitProcess: {Environment.Is64BitProcess}");
            Log($"CommandLine: {Environment.CommandLine}");

            DiagnosticLog("App constructor complete");
        }
        catch (Exception ex)
        {
            LogError("App constructor", ex);
            throw;
        }
    }

    /// <summary>
    /// 注册三个层级的全局异常处理器:
    /// 1. DispatcherUnhandledException - UI 线程异常 (最常见)
    /// 2. AppDomain.CurrentDomain.UnhandledException - 任何线程的未处理异常 (后台线程崩溃)
    /// 3. TaskScheduler.UnobservedTaskException - 未观察的 Task 异常
    /// </summary>
    private void EnsureGlobalExceptionHandlers()
    {
        // 防重复注册 (App 构造函数 + OnStartup 都会调)
        if (_handlersRegistered) return;
        _handlersRegistered = true;

        DispatcherUnhandledException += (s, e) =>
        {
            LogError("UI Exception (Dispatcher)", e.Exception);
            try
            {
                MessageBox.Show(
                    $"发生错误:\n\n{e.Exception.Message}\n\n" +
                    $"详情已记录到日志文件:\n{LogFile}",
                    "CleanMaster",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            LogError("AppDomain UnhandledException", ex);
            // 同步写一份崩溃快照, 方便用户反馈时定位
            try
            {
                var isTerminating = e.IsTerminating;
                File.WriteAllText(CrashFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AppDomain UnhandledException (Terminating={isTerminating})\n" +
                    $"{ex}\n\n" +
                    $"--- Runtime Info ---\n{GetRuntimeSnapshot()}\n");
            }
            catch { }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogError("TaskScheduler UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        DiagnosticLog("Global exception handlers registered");
    }

    private bool _handlersRegistered;

    /// <summary>
    /// 把当前进程的关键运行环境信息打到一个紧凑字符串里, 用于崩溃诊断。
    /// </summary>
    private static string GetRuntimeSnapshot()
    {
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"Runtime: {Environment.Version}");
            sb.AppendLine($"Is64BitOS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($"Is64BitProcess: {Environment.Is64BitProcess}");
            sb.AppendLine($"ProcessPath: {Environment.ProcessPath}");
            sb.AppendLine($"ProcessId: {Environment.ProcessId}");
            sb.AppendLine($"WorkingSet: {Environment.WorkingSet / 1024 / 1024} MB");
            sb.AppendLine($"ThreadCount: {Process.GetCurrentProcess().Threads.Count}");
            try
            {
                var asm = Assembly.GetEntryAssembly();
                sb.AppendLine($"EntryAssembly: {asm?.Location ?? "(null)"}");
                sb.AppendLine($"TargetFramework: {asm?.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName ?? "(unknown)"}");
            }
            catch { }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(failed to gather runtime info: {ex.Message})");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 单独写一条环境信息诊断日志 (启动时记录一次, 后续崩溃时可对照)
    /// </summary>
    private void DiagnosticLogEnvironment()
    {
        try
        {
            var entryAsm = Assembly.GetEntryAssembly();
            var tfm = entryAsm?.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName;

            DiagnosticLog($"--- Environment ---");
            DiagnosticLog($"OSVersion: {Environment.OSVersion}");
            DiagnosticLog($"Is64BitOS: {Environment.Is64BitOperatingSystem}");
            DiagnosticLog($"Is64BitProcess: {Environment.Is64BitProcess}");
            DiagnosticLog($"RuntimeVersion: {Environment.Version}");
            DiagnosticLog($"TargetFramework: {tfm ?? "(unknown)"}");
            DiagnosticLog($"EntryAssembly: {entryAsm?.Location ?? "(null)"}");
            DiagnosticLog($"ProcessPath: {Environment.ProcessPath}");
            DiagnosticLog($"ProcessId: {Environment.ProcessId}");
            DiagnosticLog($"BaseDirectory: {AppContext.BaseDirectory}");
            DiagnosticLog($"WorkingSet: {Environment.WorkingSet / 1024 / 1024} MB");
            DiagnosticLog($"IsAdmin: {IsRunningAsAdmin()}");
            DiagnosticLog($"--------------------");
        }
        catch (Exception ex)
        {
            DiagnosticLog($"Failed to log environment: {ex.Message}");
        }
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>
    /// 诊断日志: 每条都带时间戳和阶段标记, 用于排查"双击无反应"类问题。
    /// 即使 Application 还没完全初始化也能写, 因为只依赖文件 IO。
    /// </summary>
    public static void DiagnosticLog(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [DIAG] {message}\n",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // 故意吞掉, 诊断日志不能让进程崩
        }
    }

    public static void Log(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n",
                    Encoding.UTF8);
            }
        }
        catch { }
    }

    public static void LogError(string context, Exception? ex)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogDir);
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR [{context}]: {ex?.Message}\n";
                if (ex?.StackTrace != null) msg += ex.StackTrace + "\n";
                if (ex?.InnerException != null) msg += $"  Inner: {ex.InnerException.Message}\n";
                File.AppendAllText(LogFile, msg + "\n", Encoding.UTF8);
            }
        }
        catch { }
    }
}
