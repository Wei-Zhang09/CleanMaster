using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class SoftwareServiceTests
{
    private readonly SoftwareService _service = new();

    [Theory]
    [InlineData("C:\\app\\uninstall.exe & del C:\\*")]
    [InlineData("C:\\app\\uninstall.exe | cmd.exe")]
    [InlineData("C:\\app\\uninstall.exe ; rm -rf /")]
    public async Task UninstallSoftware_SuspiciousCommand_ReturnsError(string uninstallString)
    {
        var software = new InstalledSoftware
        {
            Name = "Test",
            UninstallString = uninstallString
        };

        var (started, exitCode, message) = await _service.UninstallSoftwareAsync(software);

        Assert.False(started);
        Assert.Null(exitCode);
        Assert.Contains("suspicious", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UninstallSoftware_EmptyUninstallString_ReturnsError()
    {
        var software = new InstalledSoftware { Name = "Test", UninstallString = "" };

        var (started, exitCode, message) = await _service.UninstallSoftwareAsync(software);

        Assert.False(started);
        Assert.Null(exitCode);
    }

    [Fact]
    public async Task UninstallSoftware_NonExistentExe_ReturnsNotFound()
    {
        var software = new InstalledSoftware
        {
            Name = "Test",
            UninstallString = @"""C:\NonExistentPath\uninstall.exe"" /S"
        };

        var (started, exitCode, message) = await _service.UninstallSoftwareAsync(software);

        Assert.False(started);
        Assert.Null(exitCode);
    }

    [Fact]
    public void ScanLeftovers_NoInstallLocation_ReturnsEmpty()
    {
        var software = new InstalledSoftware
        {
            Name = "NonExistent Software XYZ",
            Publisher = "NonExistent Publisher XYZ",
            InstallLocation = ""
        };

        var result = _service.ScanLeftovers(software);

        Assert.NotNull(result);
        Assert.Empty(result.LeftoverFolders);
        Assert.Empty(result.LeftoverRegistryKeys);
    }
}
