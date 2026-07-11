using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class SoftwareModelsTests
{
    #region InstalledSoftware Tests

    [Fact]
    public void InstalledSoftware_SizeText_ReturnsUnknown_WhenEstimatedSizeIsZero()
    {
        var software = new InstalledSoftware { EstimatedSize = 0 };

        Assert.Equal("未知", software.SizeText);
    }

    [Theory]
    [InlineData(2_147_483_648, "2.00 GB")]
    [InlineData(1_073_741_824, "1.00 GB")]
    [InlineData(100_000_000, "95.4 MB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(10_000, "9.8 KB")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(512, "未知")] // below 1024 KB threshold returns "未知"
    public void InstalledSoftware_SizeText_FormatsCorrectly(long estimatedSize, string expected)
    {
        var software = new InstalledSoftware { EstimatedSize = estimatedSize };

        Assert.Equal(expected, software.SizeText);
    }

    [Fact]
    public void InstalledSoftware_DefaultPropertyValues_AreCorrect()
    {
        var software = new InstalledSoftware();

        Assert.Equal("", software.Name);
        Assert.Equal("", software.Publisher);
        Assert.Equal("", software.Version);
        Assert.Equal("", software.InstallLocation);
        Assert.Equal("", software.UninstallString);
        Assert.Equal("", software.IconPath);
        Assert.Equal(0, software.EstimatedSize);
        Assert.Null(software.InstallDate);
        Assert.False(software.IsSelected);
    }

    #endregion

    #region StartupItem Tests

    [Fact]
    public void StartupItem_IsEnabled_DefaultsToFalse()
    {
        var item = new StartupItem();

        Assert.False(item.IsEnabled);
    }

    [Fact]
    public void StartupItem_DefaultStrings_AreEmpty()
    {
        var item = new StartupItem();

        Assert.Equal("", item.Name);
        Assert.Equal("", item.Command);
        Assert.Equal("", item.Location);
        Assert.Equal("", item.Source);
        Assert.Equal("", item.IconPath);
    }

    #endregion

    #region UninstallResult Tests

    [Fact]
    public void UninstallResult_LeftoverFolders_IsEmptyByDefault()
    {
        var result = new UninstallResult();

        Assert.NotNull(result.LeftoverFolders);
        Assert.Empty(result.LeftoverFolders);
    }

    [Fact]
    public void UninstallResult_LeftoverRegistryKeys_IsEmptyByDefault()
    {
        var result = new UninstallResult();

        Assert.NotNull(result.LeftoverRegistryKeys);
        Assert.Empty(result.LeftoverRegistryKeys);
    }

    [Theory]
    [InlineData(2_147_483_648, "2.00 GB")]
    [InlineData(1_073_741_824, "1.00 GB")]
    [InlineData(100_000_000, "95.4 MB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(100_000, "97.7 KB")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(0, "0 B")]
    public void UninstallResult_LeftoverSizeText_FormatsCorrectly(long leftoverSize, string expected)
    {
        var result = new UninstallResult { LeftoverSize = leftoverSize };

        Assert.Equal(expected, result.LeftoverSizeText);
    }

    [Fact]
    public void UninstallResult_Success_DefaultsToFalse()
    {
        var result = new UninstallResult();

        Assert.False(result.Success);
    }

    [Fact]
    public void UninstallResult_Message_DefaultsToEmptyString()
    {
        var result = new UninstallResult();

        Assert.Equal("", result.Message);
    }

    #endregion
}
