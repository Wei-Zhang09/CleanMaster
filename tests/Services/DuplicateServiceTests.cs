using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class DuplicateServiceTests : IDisposable
{
    private readonly DuplicateService _service = new();
    private readonly string _testDir;

    public DuplicateServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"CleanMasterDup_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task FindDuplicates_IdenticalContent_ReturnsGroup()
    {
        // 两个完全相同内容的文件（> 1MB 默认阈值，这里传小阈值）
        var f1 = Path.Combine(_testDir, "a.bin");
        var f2 = Path.Combine(_testDir, "b.bin");
        var content = new string('x', 2048);
        File.WriteAllText(f1, content);
        File.WriteAllText(f2, content);

        var groups = await _service.FindDuplicatesAsync(
            scanPaths: new[] { _testDir },
            minFileSize: 1024);

        Assert.NotEmpty(groups);
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Files.Count);
        Assert.True(groups[0].Files.Any(f => f.IsKept));
        Assert.True(groups[0].Files.Any(f => !f.IsKept && f.IsSelected));
    }

    [Fact]
    public async Task FindDuplicates_SameSizeDifferentContent_ReturnsNoGroup()
    {
        var f1 = Path.Combine(_testDir, "a.bin");
        var f2 = Path.Combine(_testDir, "b.bin");
        File.WriteAllText(f1, new string('a', 2048));
        File.WriteAllText(f2, new string('b', 2048));

        var groups = await _service.FindDuplicatesAsync(
            scanPaths: new[] { _testDir },
            minFileSize: 1024);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicates_ThreeCopies_MarksOneKeptTwoSelected()
    {
        var content = new string('z', 2048);
        for (var i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(_testDir, $"f{i}.bin"), content);
        }

        var groups = await _service.FindDuplicatesAsync(
            scanPaths: new[] { _testDir },
            minFileSize: 1024);

        Assert.Single(groups);
        var group = groups[0];
        Assert.Equal(3, group.Files.Count);
        Assert.Equal(1, group.Files.Count(f => f.IsKept));
        Assert.Equal(2, group.Files.Count(f => f.IsSelected && !f.IsKept));
        // 浪费空间 = 单文件大小 × (副本数 - 1)
        Assert.Equal(2048 * 2, group.WastedSpace);
    }

    [Fact]
    public async Task FindDuplicates_SameHeadDifferentTail_ReturnsNoGroup()
    {
        // 前 8KB 完全相同、尾部不同：快速指纹会放行成候选，全量 SHA256 必须纠正为不同。
        var head = new string('h', 8 * 1024);
        File.WriteAllText(Path.Combine(_testDir, "head_a.bin"), head + "AAAA");
        File.WriteAllText(Path.Combine(_testDir, "head_b.bin"), head + "BBBB");

        var groups = await _service.FindDuplicatesAsync(
            scanPaths: new[] { _testDir },
            minFileSize: 1024);

        Assert.Empty(groups);
    }

    [Fact]
    public async Task FindDuplicates_ReportsProgressEndingAt100()
    {
        var content = new string('q', 4096);
        File.WriteAllText(Path.Combine(_testDir, "p1.bin"), content);
        File.WriteAllText(Path.Combine(_testDir, "p2.bin"), content);

        var percents = new List<double>();
        _service.ProgressChanged += p => percents.Add(p.Percent);

        await _service.FindDuplicatesAsync(
            scanPaths: new[] { _testDir },
            minFileSize: 1024);

        Assert.NotEmpty(percents);
        Assert.All(percents, p => Assert.InRange(p, 0, 100));
        Assert.Equal(100, percents[^1], 3);
    }
}
