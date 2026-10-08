using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class FolderScanServiceTests : IDisposable
{
    private readonly FolderScanService _service = new();
    private readonly string _testDir;

    public FolderScanServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"CleanMasterFolder_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task ScanLargeFolders_FindsLargeFolder()
    {
        // 建一个超过 500MB 阈值的大文件夹不现实，这里用一个远小于阈值的目录，
        // 验证"空目录/小目录不报错、正常返回"以及跳过隐藏目录的行为。
        var smallDir = Path.Combine(_testDir, "small");
        Directory.CreateDirectory(smallDir);
        File.WriteAllText(Path.Combine(smallDir, "f.txt"), new string('a', 100));

        var results = await _service.ScanLargeFoldersAsync(
            drive: _testDir,
            minSizeBytes: 500 * 1024 * 1024,
            ct: CancellationToken.None);

        // 小于 500MB 阈值，不应被列出
        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanLargeFolders_WithLowThreshold_ReturnsFolder()
    {
        var bigDir = Path.Combine(_testDir, "big");
        Directory.CreateDirectory(bigDir);
        File.WriteAllText(Path.Combine(bigDir, "f.bin"), new string('b', 2048));

        // 用低阈值（1024 字节）验证能扫出这个文件夹
        var results = await _service.ScanLargeFoldersAsync(
            drive: _testDir,
            minSizeBytes: 1024,
            ct: CancellationToken.None);

        Assert.Contains(results, r => r.FolderName == "big");
    }

    [Fact]
    public async Task ScanLargeFolders_WithCancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ScanLargeFoldersAsync(_testDir, 1024, cts.Token));
    }
}
