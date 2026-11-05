using System.Security.Cryptography;
using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

/// <summary>
/// 更新下载 + 校验（3.3.0 实测 bug 的回归测试）。
///
/// 现场症状：点更新后弹「更新失败：无法访问 C:\...\Temp\CleanMasterUpdate\CleanMaster-Setup-v3.3.0.exe，
/// 因为它正被另一个进程使用」。根因是写入用 File.Create（FileShare.None）持有句柄时，
/// 同一个方法里又去 File.OpenRead 计算 SHA256 —— Windows 判定为共享冲突。
/// 下面的用例会直接复现该路径：写文件 → 校验哈希 → 文件可用。
/// </summary>
public class UpdateDownloadTests : IDisposable
{
    private readonly string _dir;

    public UpdateDownloadTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cm-update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static string Sha256Of(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    [Fact]
    public async Task WriteAndVerify_CorrectHash_SucceedsAndLeavesReadableFile()
    {
        var payload = new byte[300 * 1024];
        new Random(42).NextBytes(payload);
        var target = Path.Combine(_dir, "setup.exe");

        await UpdateService.WriteAndVerifyAsync(
            new MemoryStream(payload), target, payload.Length, Sha256Of(payload), null, default);

        Assert.True(File.Exists(target));
        Assert.Equal(payload.Length, new FileInfo(target).Length);

        // 校验后文件必须可再次读取（句柄已释放，不会再出现"被另一个进程使用"）
        using var fs = File.OpenRead(target);
        Assert.Equal(payload.Length, fs.Length);
    }

    [Fact]
    public async Task WriteAndVerify_NoHash_SkipsVerification()
    {
        var payload = new byte[1024];
        var target = Path.Combine(_dir, "no-hash.exe");

        await UpdateService.WriteAndVerifyAsync(
            new MemoryStream(payload), target, payload.Length, expectedSha256: null, null, default);

        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task WriteAndVerify_WrongHash_ThrowsAndDeletesFile()
    {
        var payload = new byte[2048];
        var target = Path.Combine(_dir, "bad.exe");
        var wrong = new string('A', 64);

        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateService.WriteAndVerifyAsync(
            new MemoryStream(payload), target, payload.Length, wrong, null, default));

        Assert.False(File.Exists(target), "校验失败必须删除半成品，不能留在磁盘上");
    }

    [Fact]
    public async Task WriteAndVerify_ReportsProgressUpTo100()
    {
        var payload = new byte[100 * 1024];
        var reports = new List<double>();
        var progress = new Progress<double>(p => reports.Add(p));

        await UpdateService.WriteAndVerifyAsync(
            new MemoryStream(payload), Path.Combine(_dir, "progress.exe"),
            payload.Length, null, progress, default);

        // Progress<T> 是异步投递的，给它一点时间
        await Task.Delay(150);

        Assert.NotEmpty(reports);
        Assert.All(reports, p => Assert.InRange(p, 0, 100));
        Assert.Equal(100, reports[^1], 1);
    }
}
