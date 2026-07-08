using System.IO;
using System.Windows;

namespace CleanMaster;

public partial class App : Application
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CleanMaster", "logs");

    private static readonly string LogFile = Path.Combine(LogDir, "startup.log");

    public App()
    {
        // Global exception handler
        DispatcherUnhandledException += (s, e) =>
        {
            LogError("UI Exception", e.Exception);
            MessageBox.Show($"发生错误：{e.Exception.Message}\n\n详情已记录到日志文件：\n{LogFile}", "CleanMaster", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogError("AppDomain Exception", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogError("Task Exception", e.Exception);
            e.SetObserved();
        };

        // Startup log
        Log("Application starting...");
        Log($"OS: {Environment.OSVersion}");
        Log($"Runtime: {Environment.Version}");
        Log($"Path: {Environment.ProcessPath}");
    }

    public static void Log(string message)
    {
        try
        {
            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    public static void LogError(string context, Exception? ex)
    {
        Log($"ERROR [{context}]: {ex?.Message}");
        if (ex?.StackTrace != null) Log(ex.StackTrace);
        if (ex?.InnerException != null) Log($"  Inner: {ex.InnerException.Message}");
    }
}
