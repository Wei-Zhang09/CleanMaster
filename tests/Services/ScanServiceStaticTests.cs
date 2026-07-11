using CleanMaster.Models;
using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class GetCategoryNameTests
{
    [Theory]
    [InlineData(CleanCategory.RecycleBin, "Recycle Bin")]
    [InlineData(CleanCategory.TempFiles, "Temporary Files")]
    [InlineData(CleanCategory.WindowsUpdate, "Windows Update")]
    [InlineData(CleanCategory.WindowsLogs, "System Logs")]
    [InlineData(CleanCategory.BrowserCache, "Browser Cache")]
    [InlineData(CleanCategory.DevToolCache, "Dev Tool Cache")]
    [InlineData(CleanCategory.AppCache, "App Cache")]
    [InlineData(CleanCategory.InstallerCache, "Installer Cache")]
    [InlineData(CleanCategory.CrashDumps, "Crash Dumps")]
    [InlineData(CleanCategory.DesktopInstallers, "Desktop Installers")]
    [InlineData(CleanCategory.LargeFiles, "Large Files")]
    [InlineData(CleanCategory.DuplicateFiles, "Duplicate Files")]
    public void GetCategoryName_EachKnownCategory_ReturnsCorrectName(CleanCategory category, string expected)
    {
        var result = ScanService.GetCategoryName(category);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetCategoryName_UnknownCategory_ReturnsUnknown()
    {
        var result = ScanService.GetCategoryName((CleanCategory)99);
        Assert.Equal("Unknown", result);
    }

    [Fact]
    public void GetCategoryName_AllValuesAreNonEmpty()
    {
        foreach (CleanCategory category in Enum.GetValues<CleanCategory>())
        {
            var name = ScanService.GetCategoryName(category);
            Assert.False(string.IsNullOrEmpty(name), $"Category {category} returned empty name");
        }
    }
}

public class GetCategoryIconTests
{
    [Fact]
    public void GetCategoryIcon_RecycleBin_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.RecycleBin);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_TempFiles_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.TempFiles);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_WindowsUpdate_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.WindowsUpdate);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_WindowsLogs_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.WindowsLogs);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_BrowserCache_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.BrowserCache);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_DevToolCache_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.DevToolCache);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_AppCache_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.AppCache);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_InstallerCache_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.InstallerCache);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_CrashDumps_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.CrashDumps);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_DesktopInstallers_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.DesktopInstallers);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_LargeFiles_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.LargeFiles);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_DuplicateFiles_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon(CleanCategory.DuplicateFiles);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_UnknownCategory_ReturnsNonEmptyIcon()
    {
        var icon = ScanService.GetCategoryIcon((CleanCategory)99);
        Assert.False(string.IsNullOrEmpty(icon));
    }

    [Fact]
    public void GetCategoryIcon_AllIconsAreSingleChar()
    {
        foreach (CleanCategory category in Enum.GetValues<CleanCategory>())
        {
            var icon = ScanService.GetCategoryIcon(category);
            Assert.Single(icon); // Each icon is a single Unicode character
        }
    }

    [Fact]
    public void GetCategoryIcon_AllValuesAreNonEmpty()
    {
        foreach (CleanCategory category in Enum.GetValues<CleanCategory>())
        {
            var icon = ScanService.GetCategoryIcon(category);
            Assert.False(string.IsNullOrEmpty(icon), $"Category {category} returned empty icon");
        }
    }
}
