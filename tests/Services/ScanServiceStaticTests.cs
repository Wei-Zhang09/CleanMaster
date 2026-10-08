using CleanMaster.Models;
using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class GetCategoryNameTests
{
    [Theory]
    [InlineData(CleanCategory.RecycleBin, "回收站")]
    [InlineData(CleanCategory.TempFiles, "临时文件")]
    [InlineData(CleanCategory.WindowsUpdate, "Windows 更新")]
    [InlineData(CleanCategory.WindowsLogs, "系统日志")]
    [InlineData(CleanCategory.BrowserCache, "浏览器缓存")]
    [InlineData(CleanCategory.DevToolCache, "开发工具缓存")]
    [InlineData(CleanCategory.AppCache, "应用缓存")]
    [InlineData(CleanCategory.InstallerCache, "安装程序缓存")]
    [InlineData(CleanCategory.CrashDumps, "崩溃转储")]
    [InlineData(CleanCategory.DesktopInstallers, "桌面安装包")]
    [InlineData(CleanCategory.LargeFiles, "大文件")]
    [InlineData(CleanCategory.DuplicateFiles, "重复文件")]
    public void GetCategoryName_EachKnownCategory_ReturnsCorrectName(CleanCategory category, string expected)
    {
        var result = ScanService.GetCategoryName(category);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetCategoryName_UnknownCategory_ReturnsUnknown()
    {
        var result = ScanService.GetCategoryName((CleanCategory)99);
        Assert.Equal("未知", result);
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
