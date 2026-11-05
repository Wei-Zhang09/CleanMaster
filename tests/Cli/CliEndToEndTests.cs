using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CleanMaster.Tests.Cli;

/// <summary>
/// CLI 端到端测试：真的启动 CleanMaster.Cli.exe 并解析它的输出。
/// 只测只读命令（version / largefiles / duplicates / help / 未知命令），
/// 不触碰真实用户目录 —— 路径都指向临时目录，保证可重复。
/// </summary>
public class CliEndToEndTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _cliExe;

    public CliEndToEndTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "cm-cli-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workDir);
        _cliExe = Path.Combine(AppContext.BaseDirectory, "CleanMaster.Cli.exe");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true); } catch { }
    }

    private (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(_cliExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);
        return (p.ExitCode, stdout, stderr);
    }

    [Fact]
    public void CliExecutableExists()
    {
        Assert.True(File.Exists(_cliExe), $"未找到 {_cliExe}（应随 ProjectReference 复制到测试输出目录）");
    }

    [Fact]
    public void Version_Json_ReportsAppVersionNotCliVersion()
    {
        var (code, stdout, _) = Run("version", "--json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(stdout);
        var version = doc.RootElement.GetProperty("version").GetString();

        // 版本号单一来源：必须等于主程序集版本（CleanMaster.csproj 的 <Version>）
        var appVersion = typeof(CleanMaster.Services.ScanService).Assembly.GetName().Version!.ToString(3);
        Assert.Equal(appVersion, version);
    }

    [Fact]
    public void Help_PrintsUsageAndExitsZero()
    {
        var (code, stdout, _) = Run("--help");

        Assert.Equal(0, code);
        Assert.Contains("scan", stdout);
        Assert.Contains("--json", stdout);
    }

    [Fact]
    public void NoArguments_PrintsUsageAndExitsUsageError()
    {
        var (code, stdout, _) = Run();

        Assert.Equal(2, code);
        Assert.Contains("用法", stdout);
    }

    [Fact]
    public void UnknownCommand_ExitsUsageError()
    {
        var (code, _, stderr) = Run("explode");

        Assert.Equal(2, code);
        Assert.Contains("未知命令", stderr);
    }

    [Fact]
    public void LargeFiles_Json_ReportsOnlyFilesAboveThreshold()
    {
        Directory.CreateDirectory(Path.Combine(_workDir, "sub"));
        File.WriteAllBytes(Path.Combine(_workDir, "small.bin"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(_workDir, "sub", "big.bin"), new byte[64 * 1024]);

        var (code, stdout, _) = Run("largefiles", _workDir, "--min-size", "4096", "--json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(stdout);
        var files = doc.RootElement.GetProperty("files");

        Assert.Equal(1, files.GetArrayLength());
        Assert.Equal("big.bin", files[0].GetProperty("name").GetString());
        Assert.Equal(64 * 1024, files[0].GetProperty("sizeBytes").GetInt64());
        Assert.Equal(1, doc.RootElement.GetProperty("totals").GetProperty("files").GetInt32());
    }

    [Fact]
    public void Duplicates_Json_ReportsGroupAndWastedBytes()
    {
        var content = new string('d', 4096);
        File.WriteAllText(Path.Combine(_workDir, "a.bin"), content);
        File.WriteAllText(Path.Combine(_workDir, "b.bin"), content);

        var (code, stdout, _) = Run("duplicates", _workDir, "--min-size", "1024", "--json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(stdout);
        var totals = doc.RootElement.GetProperty("totals");

        Assert.Equal(1, totals.GetProperty("groups").GetInt32());
        Assert.Equal(4096, totals.GetProperty("wastedBytes").GetInt64());
        // 硬链接剔除计数必须出现在 JSON 里，便于迁移期对比
        Assert.True(doc.RootElement.TryGetProperty("linkedPathsSkipped", out _));
    }

    [Fact]
    public void Scan_Json_ExposesTotalsAndItems()
    {
        // 全量扫描真实规则库较慢（约 5-10 秒），但这是 CLI 的核心用途，必须真跑一次
        var (code, stdout, _) = Run("scan", "--json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        Assert.Equal("scan", root.GetProperty("command").GetString());
        var totals = root.GetProperty("totals");
        Assert.True(totals.GetProperty("categories").GetInt32() > 0, "至少应扫描到一个分类");
        Assert.True(totals.GetProperty("items").GetInt32() > 0, "至少应扫描到一个可清理项");

        var items = root.GetProperty("items");
        var first = items[0];
        Assert.False(string.IsNullOrEmpty(first.GetProperty("path").GetString()));
        Assert.True(first.GetProperty("reclaimableBytes").GetInt64() >= 0);
    }
}
