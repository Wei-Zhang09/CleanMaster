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
            () => _cleanService.CleanAsync(categories, cts.Token));
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

    #region ProgressChanged Event Tests

    [Fact]
    public async Task CleanAsync_ReportsProgress()
    {
        // Create test file
        var testFile = Path.Combine(_testDirectory, "progress_test.txt");
        File.WriteAllText(testFile, "test");

        var progressMessages = new List<string>();
        _cleanService.ProgressChanged += msg => progressMessages.Add(msg);

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

        await _cleanService.CleanAsync(categories);

        Assert.NotEmpty(progressMessages);
    }

    [Fact]
    public async Task CleanAsync_ReportsProgressUpdated()
    {
        // Create test file
        var testFile = Path.Combine(_testDirectory, "progress_updated_test.txt");
        File.WriteAllText(testFile, "test");

        CleanProgress? lastProgress = null;
        _cleanService.ProgressUpdated += p => lastProgress = p;

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

        await _cleanService.CleanAsync(categories);

        Assert.NotNull(lastProgress);
        Assert.Equal(1, lastProgress.Total);
        Assert.Equal(1, lastProgress.Current);
    }

    #endregion
}
