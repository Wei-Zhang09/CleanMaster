using System.IO;
using System.Text;

namespace CleanMaster.Services;

/// <summary>
/// 基于文件的日志实现，支持滚动：单个日志文件超过 <see cref="MaxFileBytes"/> 后，
/// 重命名为 startup.log.1（保留最近 <see cref="MaxBackups"/> 份），再开新文件。
/// 线程安全（内部锁），写失败静默吞掉（日志不应拖垮清理主流程）。
/// </summary>
public class FileAppLogger : IAppLogger
{
    private const long MaxFileBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxBackups = 3;

    private readonly string _logDir;
    private readonly string _logFile;
    private readonly string _crashFile;
    private readonly object _lock = new();

    public FileAppLogger()
    {
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CleanMaster", "logs");
        _logFile = Path.Combine(_logDir, "startup.log");
        _crashFile = Path.Combine(_logDir, "crash.log");
    }

    public void Info(string message) => Append($"{message}");

    public void Diagnostic(string message) => Append($"[DIAG] {message}", withMillis: true);

    public void Error(string context, Exception? ex)
    {
        var msg = $"ERROR [{context}]: {ex?.Message}";
        if (ex?.StackTrace != null) msg += "\n" + ex.StackTrace;
        if (ex?.InnerException != null) msg += $"\n  Inner: {ex.InnerException.Message}";
        Append(msg);
    }

    /// <summary>写崩溃快照（AppDomain 未处理异常时同步调用）。</summary>
    public void WriteCrashSnapshot(string content)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_logDir);
                File.WriteAllText(_crashFile, content, Encoding.UTF8);
            }
        }
        catch { }
    }

    private void Append(string line, bool withMillis = false)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_logDir);
                RollIfNeeded();
                var stamp = withMillis
                    ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.AppendAllText(_logFile, $"[{stamp}] {line}\n", Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写入失败不应影响主流程。
        }
    }

    private void RollIfNeeded()
    {
        try
        {
            var info = new FileInfo(_logFile);
            if (!info.Exists || info.Length < MaxFileBytes) return;

            // 滚动：删除最旧，逐级后移
            var oldest = $"{_logFile}.{MaxBackups}";
            if (File.Exists(oldest)) File.Delete(oldest);
            for (var i = MaxBackups - 1; i >= 1; i--)
            {
                var src = $"{_logFile}.{i}";
                var dst = $"{_logFile}.{i + 1}";
                if (File.Exists(src)) File.Move(src, dst, overwrite: true);
            }
            File.Move(_logFile, $"{_logFile}.1", overwrite: true);
        }
        catch
        {
            // 滚动失败不影响写入。
        }
    }
}
