using System.Diagnostics;
using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

/// <summary>
/// ST-1 扫描稳定性：大文件夹扫描要能取消、要在"某个子目录读不了"时保住兄弟目录的数据。
/// </summary>
public class FolderScanStabilityTests : IDisposable
{
    private readonly FolderScanService _service = new();
    private readonly string _root;

    public FolderScanStabilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cm-stab-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private void CreateFile(string relative, int size)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    [Fact]
    public async Task ScanLargeFolders_Cancelled_ThrowsInsteadOfRunningToCompletion()
    {
        for (var i = 0; i < 5; i++) CreateFile(Path.Combine($"d{i}", "f.bin"), 1024);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ScanLargeFoldersAsync(_root, minSizeBytes: 0, ct: cts.Token));
    }

    [Fact]
    public async Task ScanLargeFolders_DeepTree_CountsAllNestedFiles()
    {
        // 5 层深、每层 3 个文件 × 4KB = 15 个文件 60KB
        for (var i = 0; i < 3; i++)
            CreateFile(Path.Combine("top", "a", "b", "c", "d", $"f{i}.bin"), 4096);

        var results = await _service.ScanLargeFoldersAsync(_root, minSizeBytes: 0);

        var top = results.Single(r => r.FolderName == "top");
        Assert.Equal(3, top.FileCount);
        Assert.Equal(3 * 4096, top.TotalSize);
    }

    [Fact]
    public async Task ScanLargeFolders_UnreadableSubdirectory_KeepsSiblingSizes()
    {
        // 兄弟目录里各放一个 8KB 文件；其中一个目录设成拒绝读取。
        // 旧实现（RecurseSubdirectories=true + 单个 try/catch）会让整个父目录统计归零。
        CreateFile(Path.Combine("parent", "ok1", "f.bin"), 8192);
        CreateFile(Path.Combine("parent", "ok2", "f.bin"), 8192);
        var blocked = Path.Combine(_root, "parent", "blocked");
        CreateFile(Path.Combine("parent", "blocked", "f.bin"), 8192);

        if (!TryDenyRead(blocked)) return;   // 环境不支持设置 ACL 时跳过

        // 自检：确认拒绝真的生效（否则这条测试会变成"假绿"）
        if (CanReadDirectory(blocked)) { TryRemoveDeny(blocked); return; }

        try
        {
            var results = await _service.ScanLargeFoldersAsync(_root, minSizeBytes: 0);

            var parent = results.Single(r => r.FolderName == "parent");
            Assert.True(parent.TotalSize >= 2 * 8192,
                $"读不了的子目录不应让兄弟目录的数据丢失，实际统计 {parent.TotalSize} 字节");
        }
        finally
        {
            TryRemoveDeny(blocked);
        }
    }

    private static bool CanReadDirectory(string dir)
    {
        try
        {
            using var e = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
            e.MoveNext();
            return true;
        }
        catch { return false; }
    }

    /// <summary>用 icacls 给当前用户加拒绝读取 ACE（不需要管理员，目录所有者即可）。</summary>
    private static bool TryDenyRead(string dir)
    {
        try
        {
            var user = Environment.UserName;
            var (code, _) = Run("icacls", $"\"{dir}\" /deny \"{user}:(OI)(CI)(RX)\"");
            return code == 0;
        }
        catch { return false; }
    }

    private static void TryRemoveDeny(string dir)
    {
        try
        {
            var user = Environment.UserName;
            Run("icacls", $"\"{dir}\" /remove:d \"{user}\"");
        }
        catch { }
    }

    private static (int Code, string Output) Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15_000);
        return (p.ExitCode, output);
    }
}
