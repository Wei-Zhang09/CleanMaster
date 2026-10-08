using CleanMaster.Models;
using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class CleanServiceTests : IDisposable
{
    private readonly CleanService _cleanService;
    private readonly string _testDirectory;

    public CleanServiceTests()
    {
        _cleanService = new CleanService();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"CleanMasterTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, true);
        }
        catch { }
    }

    #region CleanAsync Tests

    [Fact]
    public async Task CleanAsync_EmptyCategories_ReturnsZeroFreed()
    {
        var categories = new List<ScanCategoryResult>();

        var result = await _cleanService.CleanAsync(categories);

        Assert.Equal(0, result.BytesFreed);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(0, result.FoldersDeleted);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task CleanAsync_NothingSelected_ReturnsZeroFreed()
    {
        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = false,
                Items = new List<CleanableItem>
                {
                    new() { Name = "test.txt", FullPath = "test.txt", IsSelected = true }
                }
            }
        };

        var result = await _cleanService.CleanAsync(categories);

        Assert.Equal(0, result.BytesFreed);
    }

    [Fact]
    public async Task CleanAsync_WithTempFiles_DeletesFiles()
    {
        // Create test files
        var tempDir = Path.Combine(_testDirectory, "temp");
        Directory.CreateDirectory(tempDir);
        var testFile = Path.Combine(tempDir, "test.txt");
        File.WriteAllText(testFile, "test content");

        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new()
                    {
                        Name = "test.txt",
                        FullPath = testFile,
                        SizeBytes = new FileInfo(testFile).Length,
                        IsSelected = true,
                        IsDirectory = false
                    }
                }
            }
        };

        var result = await _cleanService.CleanAsync(categories);

        Assert.True(result.BytesFreed > 0);
        Assert.Equal(1, result.FilesDeleted);
        Assert.False(File.Exists(testFile));
    }

    [Fact]
    public async Task CleanAsync_WithDirectory_DeletesDirectory()
    {
        // Create test directory with files
        var testDir = Path.Combine(_testDirectory, "testdir");
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "file1.txt"), "content1");
        File.WriteAllText(Path.Combine(testDir, "file2.txt"), "content2");

        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new()
                    {
                        Name = "testdir",
                        FullPath = testDir,
                        SizeBytes = 100,
                        IsSelected = true,
                        IsDirectory = true
                    }
                }
            }
        };

        var result = await _cleanService.CleanAsync(categories);

        Assert.True(result.BytesFreed > 0);
        Assert.Equal(1, result.FoldersDeleted);
        Assert.False(Directory.Exists(testDir));
    }

    [Fact]
    public async Task CleanAsync_WithCancellation_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new() { Name = "test", FullPath = "test", IsSelected = true }
                }
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _cleanService.CleanAsync(categories, null, cts.Token));
    }

    [Fact]
    public async Task CleanAsync_WithNonExistentFile_AddsError()
    {
        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new()
                    {
                        Name = "nonexistent.txt",
                        FullPath = @"C:\NonExistentPath\nonexistent.txt",
                        IsSelected = true,
                        IsDirectory = false
                    }
                }
            }
        };

        var result = await _cleanService.CleanAsync(categories);

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task CleanAsync_EmptyFile_DeletesSuccessfully()
    {
        // 空文件（0 字节）删除成功不应被误报为"删除失败"
        var emptyFile = Path.Combine(_testDirectory, "empty.txt");
        File.WriteAllText(emptyFile, "");

        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new()
                    {
                        Name = "empty.txt",
                        FullPath = emptyFile,
                        SizeBytes = 0,
                        IsSelected = true,
                        IsDirectory = false
                    }
                }
            }
        };

        var result = await _cleanService.CleanAsync(categories);

        Assert.Equal(1, result.FilesDeleted);
        Assert.Empty(result.Errors);
        Assert.False(File.Exists(emptyFile));
    }

    [Fact]
    public async Task CleanAsync_DirectoryDeleteFailed_AddsError()
    {
        // 指向一个不存在但标记为目录的项：删除失败且目录仍不存在 → 应报错（而非静默）
        var missingDir = Path.Combine(_testDirectory, "missing_dir");

        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new()
                    {
                        Name = "missing_dir",
                        FullPath = missingDir,
                        SizeBytes = 100,
                        IsSelected = true,
                        IsDirectory = true
                    }
                }
            }
        };

        var result = await _cleanService.CleanAsync(categories);

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task CleanAsync_DeletedPaths_IsRecorded()
    {
        var testFile = Path.Combine(_testDirectory, "deleted_paths.txt");
        File.WriteAllText(testFile, "content");

        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new()
                    {
                        Name = "deleted_paths.txt",
                        FullPath = testFile,
                        SizeBytes = new FileInfo(testFile).Length,
                        IsSelected = true,
                        IsDirectory = false
                    }
                }
            }
        };

        var result = await _cleanService.CleanAsync(categories);

        Assert.Contains(testFile, result.DeletedPaths);
    }

    [Fact]
    public async Task CleanLargeFilesAsync_DangerLevel_NowDeletes_WhenSelected()
    {
        // P0-7：危险项不再被 service 显式跳过，统一为"默认不勾选 + 用户主动勾选"。
        // 这里验证主动勾选的 danger 项会被删除（拦截由 VM 的确认框负责）。
        var testFile = Path.Combine(_testDirectory, "danger.bin");
        File.WriteAllText(testFile, "danger-content");

        var files = new List<LargeFileItem>
        {
            new()
            {
                FileName = "danger.bin",
                FullPath = testFile,
                SizeBytes = new FileInfo(testFile).Length,
                SafetyHint = "danger",
                IsSelected = true
            }
        };

        var result = await _cleanService.CleanLargeFilesAsync(files);

        Assert.Equal(1, result.FilesDeleted);
        Assert.False(File.Exists(testFile));
    }

    [Fact]
    public async Task CleanAsync_RecycleBin_UsesShellEmpty()
    {
        // 回收站分类项不应走 Directory.Delete（必失败），而应调用 SHEmptyRecycleBin。
        // 这里只验证：构造一个回收站分类项，清理后不抛异常，且不产生"目录未能删除"类错误。
        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.RecycleBin,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new()
                    {
                        Name = "Recycle Bin",
                        FullPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "$Recycle.Bin"),
                        SizeBytes = 0,
                        IsSelected = true,
                        IsDirectory = true,
                        Category = CleanCategory.RecycleBin
                    }
                }
            }
        };

        var result = await _cleanService.CleanAsync(categories);

        // 不要求必然成功（回收站可能为空/权限差异），但不应有"目录未能删除"这类走错路径的错误。
        Assert.DoesNotContain(result.Errors, e => e.Contains("目录未能删除"));
    }

    #endregion

    #region CleanLargeFilesAsync Tests

    [Fact]
    public async Task CleanLargeFilesAsync_EmptyList_ReturnsZeroFreed()
    {
        var files = new List<LargeFileItem>();

        var result = await _cleanService.CleanLargeFilesAsync(files);

        Assert.Equal(0, result.BytesFreed);
        Assert.Equal(0, result.FilesDeleted);
    }

    [Fact]
    public async Task CleanLargeFilesAsync_WithSelectedFiles_DeletesFiles()
    {
        // Create test files
        var file1 = Path.Combine(_testDirectory, "large1.tmp");
        var file2 = Path.Combine(_testDirectory, "large2.tmp");
        File.WriteAllText(file1, new string('x', 1000));
        File.WriteAllText(file2, new string('y', 2000));

        var files = new List<LargeFileItem>
        {
            new()
            {
                FileName = "large1.tmp",
                FullPath = file1,
                SizeBytes = 1000,
                IsSelected = true
            },
            new()
            {
                FileName = "large2.tmp",
                FullPath = file2,
                SizeBytes = 2000,
                IsSelected = false
            }
        };

        var result = await _cleanService.CleanLargeFilesAsync(files);

        Assert.Equal(1000, result.BytesFreed);
        Assert.Equal(1, result.FilesDeleted);
        Assert.False(File.Exists(file1));
        Assert.True(File.Exists(file2));
    }

    #endregion

    #region Progress Tests

    [Fact]
    public async Task CleanAsync_ReportsProgress()
    {
        // Create test file
        var testFile = Path.Combine(_testDirectory, "progress_test.txt");
        File.WriteAllText(testFile, "test");

        var progressReports = new List<CleanProgress>();
        var progress = new Progress<CleanProgress>(p => progressReports.Add(p));

        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new()
                    {
                        Name = "progress_test.txt",
                        FullPath = testFile,
                        SizeBytes = 4,
                        IsSelected = true,
                        IsDirectory = false
                    }
                }
            }
        };

        await _cleanService.CleanAsync(categories, progress);

        Assert.NotEmpty(progressReports);
    }

    [Fact]
    public async Task CleanAsync_ReportsProgressUpdated()
    {
        // Create test file
        var testFile = Path.Combine(_testDirectory, "progress_updated_test.txt");
        File.WriteAllText(testFile, "test");

        CleanProgress? lastProgress = null;
        var progress = new Progress<CleanProgress>(p => lastProgress = p);

        var categories = new List<ScanCategoryResult>
        {
            new()
            {
                Category = CleanCategory.TempFiles,
                IsSelected = true,
                Items = new List<CleanableItem>
                {
                    new()
                    {
                        Name = "progress_updated_test.txt",
                        FullPath = testFile,
                        SizeBytes = 4,
                        IsSelected = true,
                        IsDirectory = false
                    }
                }
            }
        };

        await _cleanService.CleanAsync(categories, progress);

        Assert.NotNull(lastProgress);
        Assert.Equal(1, lastProgress.Total);
        Assert.Equal(1, lastProgress.Current);
    }

    #endregion
}
