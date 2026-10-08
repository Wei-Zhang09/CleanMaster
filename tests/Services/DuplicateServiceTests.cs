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
}
