using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class SystemCleanupResultTests
{
    [Fact]
    public void SystemCleanupResult_DefaultValues_AreCorrect()
    {
        var result = new SystemCleanupResult();

        Assert.False(result.Success);
        Assert.Equal("", result.Message);
        Assert.Equal("", result.Output);
        Assert.Equal(0, result.FreedBytes);
    }

    [Theory]
    [InlineData(2_147_483_648, "2.00 GB")]
    [InlineData(1_073_741_824, "1.00 GB")]
    [InlineData(1_500_000_000, "1.40 GB")]
    [InlineData(500_000_000, "476.8 MB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(100_000, "97.7 KB")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(512, "0.5 KB")]
    [InlineData(0, "0.0 KB")]
    public void SystemCleanupResult_FreedText_FormatsCorrectly(long freedBytes, string expected)
    {
        var result = new SystemCleanupResult { FreedBytes = freedBytes };

        Assert.Equal(expected, result.FreedText);
    }

    [Fact]
    public void SystemCleanupResult_PropertiesCanBeSetAndRead()
    {
        var result = new SystemCleanupResult
        {
            Success = true,
            Message = "Cleanup completed",
            Output = "All components cleaned",
            FreedBytes = 1_000_000,
        };

        Assert.True(result.Success);
        Assert.Equal("Cleanup completed", result.Message);
        Assert.Equal("All components cleaned", result.Output);
        Assert.Equal(1_000_000, result.FreedBytes);
    }
}
