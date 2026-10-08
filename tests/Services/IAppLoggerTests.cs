using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class IAppLoggerTests
{
    // FileAppLogger 写入 %APPDATA%，在单元测试里不宜真实写用户目录。
    // 这里用一个内存实现验证 IAppLogger 接口契约，FileAppLogger 的滚动逻辑
    // 依赖真实文件系统，属于集成测试范畴，暂不在此覆盖。
    private class MemoryLogger : IAppLogger
    {
        public List<string> Messages { get; } = new();
        public void Info(string message) => Messages.Add($"INFO {message}");
        public void Error(string context, Exception? ex) => Messages.Add($"ERROR {context}: {ex?.Message}");
        public void Diagnostic(string message) => Messages.Add($"DIAG {message}");
    }

    [Fact]
    public void Info_RecordsMessage()
    {
        var log = new MemoryLogger();
        log.Info("hello");
        Assert.Contains("INFO hello", log.Messages);
    }

    [Fact]
    public void Error_IncludesContextAndMessage()
    {
        var log = new MemoryLogger();
        log.Error("Scan", new InvalidOperationException("boom"));
        Assert.Contains(log.Messages, m => m.Contains("ERROR Scan") && m.Contains("boom"));
    }

    [Fact]
    public void Diagnostic_RecordsMessage()
    {
        var log = new MemoryLogger();
        log.Diagnostic("startup");
        Assert.Contains("DIAG startup", log.Messages);
    }
}
