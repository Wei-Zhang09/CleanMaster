using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class FindLargeFilesTests : IDisposable
{
    private readonly ScanService _service = new();
    private readonly string _root;

    public FindLargeFilesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cm-largefiles-{Guid.NewGuid():N}"[..24]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private string CreateFile(string relativePath, int sizeBytes)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    [Fact]
    public async Task FindLargeFiles_ReturnsOnlyFilesAboveThreshold_SortedDescending()
    {
        CreateFile("small.bin", 1024);
        CreateFile("mid.bin", 8 * 1024);
        CreateFile("big.bin", 32 * 1024);

        var result = await _service.FindLargeFilesAsync(_root, minSizeBytes: 4 * 1024);

        Assert.Equal(2, result.Count);
        Assert.Equal("big.bin", result[0].FileName);
        Assert.Equal("mid.bin", result[1].FileName);
        Assert.True(result[0].SizeBytes >= result[1].SizeBytes);
    }

    [Fact]
    public async Task FindLargeFiles_TraversesNestedDirectories()
    {
        CreateFile(Path.Combine("a", "b", "c", "deep.bin"), 16 * 1024);

        var result = await _service.FindLargeFilesAsync(_root, minSizeBytes: 1024);

        Assert.Single(result);
        Assert.EndsWith("deep.bin", result[0].FullPath);
    }

    [Fact]
    public async Task FindLargeFiles_SkipsSystemAttributeFiles()
    {
        // 与旧实现保持一致：带 System 属性的文件（如 pagefile.sys 一类）不参与大文件列表
        var systemFile = CreateFile("system.bin", 16 * 1024);
        File.SetAttributes(systemFile, FileAttributes.System);
        CreateFile("normal.bin", 16 * 1024);

        try
        {
            var result = await _service.FindLargeFilesAsync(_root, minSizeBytes: 1024);

            Assert.Single(result);
            Assert.Equal("normal.bin", result[0].FileName);
        }
        finally
        {
            try { File.SetAttributes(systemFile, FileAttributes.Normal); } catch { }
        }
    }

    [Fact]
    public async Task FindLargeFiles_CapsAt500Items()
    {
        for (var i = 0; i < 520; i++)
            CreateFile(Path.Combine("bulk", $"f{i:D4}.bin"), 2048);

        var result = await _service.FindLargeFilesAsync(_root, minSizeBytes: 1024);

        Assert.Equal(500, result.Count);
    }

    [Fact]
    public async Task FindLargeFiles_Cancelled_Throws()
    {
        CreateFile(Path.Combine("a", "big.bin"), 16 * 1024);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.FindLargeFilesAsync(_root, minSizeBytes: 1024, ct: cts.Token));
    }

    [Fact]
    public async Task FindLargeFiles_MissingPath_ReturnsEmpty()
    {
        var result = await _service.FindLargeFilesAsync(
            Path.Combine(_root, "no-such-dir"), minSizeBytes: 1024);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindLargeFiles_RootFilesAndSubdirFiles_BothIncluded()
    {
        CreateFile("root-level.bin", 8 * 1024);
        CreateFile(Path.Combine("sub", "nested.bin"), 8 * 1024);

        var result = await _service.FindLargeFilesAsync(_root, minSizeBytes: 1024);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.FileName == "root-level.bin");
        Assert.Contains(result, f => f.FileName == "nested.bin");
    }
}
