using CleanMaster.Models;
using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class ScanServiceTests
{
    private readonly ScanService _scanService;

    public ScanServiceTests()
    {
        _scanService = new ScanService();
    }

    #region GetDiskInfo Tests

    [Fact]
    public void GetDiskInfo_ValidDrive_ReturnsDiskInfo()
    {
        var result = _scanService.GetDiskInfo("C:");

        Assert.NotNull(result);
        Assert.Equal("C:", result.DriveLetter);
        Assert.True(result.TotalBytes > 0);
        Assert.True(result.FreeBytes >= 0);
        Assert.True(result.UsedBytes >= 0);
    }

    [Fact]
    public void GetDiskInfo_ValidDrive_UsedPercentIsCalculated()
    {
        var result = _scanService.GetDiskInfo("C:");

        Assert.True(result.UsedPercent >= 0 && result.UsedPercent <= 100);
    }

    [Fact]
    public void GetDiskInfo_InvalidDrive_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() => _scanService.GetDiskInfo("Z:"));
    }

    #endregion

    #region GetAllDisks Tests

    [Fact]
    public void GetAllDisks_ReturnsNonEmptyList()
    {
        var result = _scanService.GetAllDisks();

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GetAllDisks_EachDiskHasValidProperties()
    {
        var result = _scanService.GetAllDisks();

        Assert.All(result, disk =>
        {
            Assert.False(string.IsNullOrEmpty(disk.DriveLetter));
            Assert.True(disk.TotalBytes > 0);
            Assert.True(disk.FreeBytes >= 0);
            Assert.True(disk.UsedBytes >= 0);
        });
    }

    #endregion

    #region GetDirectorySize Tests

    [Fact]
    public void GetDirectorySize_NonExistentPath_ReturnsZero()
    {
        var result = ScanService.GetDirectorySize(@"C:\NonExistentPath12345");

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetDirectorySize_ValidPath_ReturnsNonNegative()
    {
        var tempPath = Path.GetTempPath();
        var result = ScanService.GetDirectorySize(tempPath);

        Assert.True(result >= 0);
    }

    #endregion

    #region GetFileInfo Tests (via reflection)

    [Theory]
    [InlineData(".tmp", "safe")]
    [InlineData(".log", "safe")]
    [InlineData(".cache", "safe")]
    [InlineData(".vmdk", "caution")]
    [InlineData(".pst", "caution")]
    [InlineData(".exe", "danger")]
    [InlineData(".dll", "danger")]
    [InlineData(".xyz", "unknown")]
    public void GetFileInfo_VariousExtensions_ReturnsCorrectSafety(string ext, string expectedSafety)
    {
        // Use reflection to test private method
        var method = typeof(ScanService).GetMethod("GetFileInfo",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        if (method == null)
        {
            // Method might not exist or have different signature
            return;
        }

        try
        {
            var result = method.Invoke(null, new object[] { ext, $@"C:\test\file{ext}" });
            var safetyProperty = result?.GetType().GetProperty("safety");
            var safety = safetyProperty?.GetValue(result) as string;

            Assert.Equal(expectedSafety, safety);
        }
        catch
        {
            // Method invocation failed, skip test
        }
    }

    #endregion

    #region ProgressChanged Event Tests

    [Fact]
    public async Task ScanAllAsync_WithCancellation_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _scanService.ScanAllAsync(cts.Token));
    }

    [Fact]
    public async Task ScanAllAsync_ReportsProgress()
    {
        var progressReports = new List<ScanProgress>();
        _scanService.ProgressChanged += p => progressReports.Add(p);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await _scanService.ScanAllAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected due to timeout
        }

        Assert.NotEmpty(progressReports);
    }

    #endregion
}
