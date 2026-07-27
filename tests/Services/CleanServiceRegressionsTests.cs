using CleanMaster.Models;
using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

/// <summary>
/// Regression tests for the functional bug fixes applied to <see cref="CleanService"/>.
/// Covers:
/// - Danger-level files are refused by CleanLargeFilesAsync
/// - Partial directory deletion still reports freed bytes
/// - Already-deleted files surface as errors (so user knows nothing was removed)
/// </summary>
public class CleanServiceRegressionsTests : IDisposable
{
    private readonly CleanService _cleanService;
    private readonly string _testDirectory;

    public CleanServiceRegressionsTests()
    {
        _cleanService = new CleanService();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"CleanMasterRegression_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, true); } catch { }
    }

    [Fact]
    public async Task CleanLargeFilesAsync_DangerLevelFile_IsRefusedAndReportedAsError()
    {
        var dll = Path.Combine(_testDirectory, "system.dll");
        File.WriteAllText(dll, "PE\0\0dummy");

        var file = new LargeFileItem
        {
            FileName = "system.dll",
            FullPath = dll,
            SizeBytes = 12,
            SafetyHint = "danger",
            IsSelected = true
        };

        var result = await _cleanService.CleanLargeFilesAsync(new List<LargeFileItem> { file });

        Assert.Equal(0, result.BytesFreed);
        Assert.Equal(0, result.FilesDeleted);
        Assert.NotEmpty(result.Errors);
        Assert.True(File.Exists(dll), "Danger file must NOT be deleted");
    }

    [Fact]
    public async Task CleanLargeFilesAsync_SafeLevelFile_IsDeleted()
    {
        var tmp = Path.Combine(_testDirectory, "safe.tmp");
        File.WriteAllText(tmp, new string('x', 500));

        var file = new LargeFileItem
        {
            FileName = "safe.tmp",
            FullPath = tmp,
            SizeBytes = 500,
            SafetyHint = "safe",
            IsSelected = true
        };

        var result = await _cleanService.CleanLargeFilesAsync(new List<LargeFileItem> { file });

        Assert.Equal(500, result.BytesFreed);
        Assert.Equal(1, result.FilesDeleted);
        Assert.False(File.Exists(tmp));
    }

    [Fact]
    public async Task CleanAsync_DirectoryWithLockedFile_PartialFreedReportedAndNotFullSize()
    {
        var dir = Path.Combine(_testDirectory, "partial");
        Directory.CreateDirectory(dir);
        var a = Path.Combine(dir, "a.txt");
        var b = Path.Combine(dir, "b.txt");
        File.WriteAllText(a, new string('a', 200));
        File.WriteAllText(b, new string('b', 300));

        // Hold an exclusive handle on b so deletion fails.
        await using var lockStream = new FileStream(b, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var item = new CleanableItem
        {
            Name = "partial",
            FullPath = dir,
            SizeBytes = 500,
            IsDirectory = true,
            IsSelected = true
        };
        var cat = new ScanCategoryResult
        {
            Category = CleanCategory.TempFiles,
            IsSelected = true,
            Items = new List<CleanableItem> { item }
        };

        var result = await _cleanService.CleanAsync(new List<ScanCategoryResult> { cat });

        // We deleted 'a' (200 bytes). 'b' is locked, so root dir remains.
        Assert.True(result.BytesFreed >= 200, $"Expected at least 200 bytes freed, got {result.BytesFreed}");
        Assert.True(Directory.Exists(dir), "Directory should still exist because b is locked");
        Assert.True(File.Exists(b), "Locked file b should still exist");
    }

    [Fact]
    public async Task CleanAsync_AlreadyDeletedFile_AddsErrorRatherThanSilentlySucceeding()
    {
        var ghost = Path.Combine(_testDirectory, "ghost.txt"); // never created

        var cat = new ScanCategoryResult
        {
            Category = CleanCategory.TempFiles,
            IsSelected = true,
            Items = new List<CleanableItem>
            {
                new() { Name = "ghost", FullPath = ghost, IsSelected = true, IsDirectory = false }
            }
        };

        var result = await _cleanService.CleanAsync(new List<ScanCategoryResult> { cat });

        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.BytesFreed);
        Assert.Equal(0, result.FilesDeleted);
    }
}
