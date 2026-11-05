using System.Diagnostics;
using CleanMaster.Services;
using Microsoft.Win32;
using Moq;

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

        var attempt = await _service.UninstallSoftwareAsync(software);

        Assert.Equal(UninstallOutcome.NotStarted, attempt.Outcome);
        Assert.Null(attempt.ExitCode);
        Assert.Contains("suspicious", attempt.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UninstallSoftware_EmptyUninstallString_ReturnsError()
    {
        var software = new InstalledSoftware { Name = "Test", UninstallString = "" };

        var attempt = await _service.UninstallSoftwareAsync(software);

        Assert.Equal(UninstallOutcome.NotStarted, attempt.Outcome);
        Assert.Null(attempt.ExitCode);
    }

    [Fact]
    public async Task UninstallSoftware_NonExistentExe_ReturnsNotFound()
    {
        var software = new InstalledSoftware
        {
            Name = "Test",
            UninstallString = @"""C:\NonExistentPath\uninstall.exe"" /S"
        };

        var attempt = await _service.UninstallSoftwareAsync(software);

        Assert.Equal(UninstallOutcome.NotStarted, attempt.Outcome);
        Assert.Null(attempt.ExitCode);
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

    [Fact]
    public void TryGetRegistrationState_ExistingKeyTrue_DeletedKeyFalse()
    {
        var sub = @"Software\CleanMasterTests\" + Guid.NewGuid().ToString("N");
        using (var key = Registry.CurrentUser.CreateSubKey(sub))
            key.SetValue("DisplayName", "TestApp");

        var software = new InstalledSoftware { Name = "TestApp", RegistryHive = "HKCU", RegistryPath = sub };

        Assert.True(_service.TryGetRegistrationState(software));

        Registry.CurrentUser.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
        Assert.False(_service.TryGetRegistrationState(software));
    }

    [Fact]
    public void TryGetRegistrationState_NoRegistryPath_ReturnsNull()
    {
        Assert.Null(_service.TryGetRegistrationState(new InstalledSoftware { Name = "NoPath" }));
    }

    [Fact]
    public async Task Uninstall_UninstallerExitsButKeyRemains_ReportsStillRegistered()
    {
        var (sub, bat) = CreateFakeUninstaller("exit 0", deleteKey: false);
        try
        {
            var software = new InstalledSoftware
            {
                Name = "FakeApp",
                UninstallString = bat,
                RegistryHive = "HKCU",
                RegistryPath = sub
            };

            var attempt = await _service.UninstallSoftwareAsync(software, default, TimeSpan.FromSeconds(5));

            Assert.Equal(UninstallOutcome.StillRegistered, attempt.Outcome);
            Assert.Equal(0, attempt.ExitCode);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(sub, false); }
    }

    [Fact]
    public async Task Uninstall_UninstallerRemovesKey_ReportsVerifiedRemoved()
    {
        var (sub, bat) = CreateFakeUninstaller("exit 0", deleteKey: true);
        try
        {
            var software = new InstalledSoftware
            {
                Name = "FakeApp",
                UninstallString = bat,
                RegistryHive = "HKCU",
                RegistryPath = sub
            };

            var attempt = await _service.UninstallSoftwareAsync(software, default, TimeSpan.FromSeconds(5));

            Assert.Equal(UninstallOutcome.VerifiedRemoved, attempt.Outcome);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(sub, false); }
    }

    [Fact]
    public async Task Uninstall_ExitCode3010_ReportsRestartRequired()
    {
        var (sub, bat) = CreateFakeUninstaller("exit 3010", deleteKey: false);
        try
        {
            var software = new InstalledSoftware
            {
                Name = "FakeApp",
                UninstallString = bat,
                RegistryHive = "HKCU",
                RegistryPath = sub
            };

            var attempt = await _service.UninstallSoftwareAsync(software, default, TimeSpan.FromSeconds(5));

            Assert.Equal(UninstallOutcome.RestartRequired, attempt.Outcome);
            Assert.Equal(3010, attempt.ExitCode);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(sub, false); }
    }

    [Fact]
    public async Task ProcessTree_WaitsForChildThatOutlivesParent()
    {
        // cmd 立刻退出，但它 start /b 出来的 ping 还在跑 —— 只等父进程会提前返回。
        var psi = new ProcessStartInfo("cmd.exe", "/c start \"\" /b ping -n 4 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var parent = Process.Start(psi)!;
        var pid = parent.Id;
        await parent.WaitForExitAsync();

        var descendants = ProcessTree.GetLiveDescendants(pid);
        Assert.NotEmpty(descendants);

        var sw = Stopwatch.StartNew();
        var allExited = await ProcessTree.WaitForDescendantsAsync(pid, TimeSpan.FromSeconds(20));
        sw.Stop();

        Assert.True(allExited);
        Assert.True(sw.ElapsedMilliseconds >= 1000,
            $"应在子进程退出后才返回，实际只等了 {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Uninstall_UserScopeWithWrongIdentity_RefusedWithoutLaunching()
    {
        // 用另一个管理员账户提权：必须在启动卸载器之前就拒绝，绝不能"跑一遍再说"
        var context = new Mock<IUserContext>();
        context.Setup(c => c.ShouldBlockUserScopeUninstall("HKCU")).Returns(true);

        var service = new SoftwareService(context.Object);
        var software = new InstalledSoftware
        {
            Name = "UserScopedApp",
            UninstallString = @"C:\Windows\System32\cmd.exe /c exit",
            RegistryHive = "HKCU",
            RegistryPath = @"Software\CleanMasterTests\NeverExists"
        };

        var attempt = await service.UninstallSoftwareAsync(software, default, TimeSpan.FromSeconds(5));

        Assert.Equal(UninstallOutcome.WrongUserContext, attempt.Outcome);
        Assert.Null(attempt.ExitCode);
        context.Verify(c => c.ShouldBlockUserScopeUninstall("HKCU"), Times.Once);
    }

    [Fact]
    public async Task Uninstall_MachineScopeNotAffectedByIdentityCheck()
    {
        // 机器级安装不受身份校验影响：检查函数对 HKLM 返回 false，卸载流程照常往下走
        var context = new Mock<IUserContext>();
        context.Setup(c => c.ShouldBlockUserScopeUninstall("HKLM")).Returns(false);

        var service = new SoftwareService(context.Object);
        var software = new InstalledSoftware
        {
            Name = "MachineScopedApp",
            UninstallString = @"C:\NonExistentPath\uninstall.exe"
        };

        var attempt = await service.UninstallSoftwareAsync(software, default, TimeSpan.FromSeconds(5));

        // 走到"找不到卸载器"这一步，说明没有被身份校验拦下
        Assert.Equal(UninstallOutcome.NotStarted, attempt.Outcome);
        Assert.Contains("Cannot find uninstaller", attempt.Message);
    }

    /// <summary>造一个假的卸载器 bat + 一个 HKCU 登记项。bat 必须纯 ASCII。</summary>
    private static (string SubKey, string BatPath) CreateFakeUninstaller(string exitLine, bool deleteKey)
    {
        var sub = @"Software\CleanMasterTests\" + Guid.NewGuid().ToString("N");
        using (var key = Registry.CurrentUser.CreateSubKey(sub))
            key.SetValue("DisplayName", "FakeApp");

        var dir = Path.Combine(Path.GetTempPath(), "cm-uninst-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var bat = Path.Combine(dir, "uninstall.bat");

        var body = "@echo off\r\n";
        if (deleteKey)
            body += $"reg delete \"HKCU\\{sub}\" /f >nul 2>&1\r\n";
        body += exitLine + "\r\n";

        File.WriteAllText(bat, body, System.Text.Encoding.ASCII);
        return (sub, bat);
    }
}
