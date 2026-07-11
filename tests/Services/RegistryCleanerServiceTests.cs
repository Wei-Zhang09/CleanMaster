using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class RegistryCleanerServiceTests
{
    #region RegistryIssue Model Tests

    [Fact]
    public void RegistryIssue_DefaultProperties_AreEmptyStrings()
    {
        var issue = new RegistryIssue();

        Assert.Equal("", issue.Key);
        Assert.Equal("", issue.Name);
        Assert.Equal("", issue.Type);
        Assert.Equal("", issue.Description);
    }

    [Fact]
    public void RegistryIssue_CanClean_DefaultsToFalse()
    {
        var issue = new RegistryIssue();

        Assert.False(issue.CanClean);
    }

    [Fact]
    public void RegistryIssue_SettingProperties_WorksCorrectly()
    {
        var issue = new RegistryIssue
        {
            Key = @"HKLM\SOFTWARE\Test",
            Name = "Test Entry",
            Type = "Orphaned Uninstall Entry",
            Description = "Test description",
            CanClean = true,
        };

        Assert.Equal(@"HKLM\SOFTWARE\Test", issue.Key);
        Assert.Equal("Test Entry", issue.Name);
        Assert.Equal("Orphaned Uninstall Entry", issue.Type);
        Assert.Equal("Test description", issue.Description);
        Assert.True(issue.CanClean);
    }

    #endregion

    #region RegistryCleanerService Tests

    [Fact]
    public void ScanForIssues_ReturnsNonNullList()
    {
        var service = new RegistryCleanerService();

        var issues = service.ScanForIssues();

        Assert.NotNull(issues);
    }

    [Fact]
    public void ScanForIssues_DoesNotThrowException()
    {
        var service = new RegistryCleanerService();

        var exception = Record.Exception(() => service.ScanForIssues());

        Assert.Null(exception);
    }

    #endregion
}
