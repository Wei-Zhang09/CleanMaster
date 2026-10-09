using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class InstalledAppServiceTests
{
    private readonly InstalledAppService _service = new();

    [Fact]
    public void EnumerateApps_ReturnsNonNullList()
    {
        var apps = _service.EnumerateApps();
        Assert.NotNull(apps);
    }

    [Fact]
    public void EnumerateApps_DoesNotThrow()
    {
        // 真实注册表枚举不应抛异常（内部已 try-catch 隔离）
        var apps = _service.EnumerateApps();
        Assert.All(apps, a =>
        {
            Assert.False(string.IsNullOrEmpty(a.DisplayName), "应用名不应为空");
        });
    }

    [Fact]
    public void EnumerateApps_NoDuplicateDisplayNames()
    {
        var apps = _service.EnumerateApps();
        var names = apps.Select(a => a.DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EnumerateApps_FiltersWindowsUpdateKbEntries()
    {
        var apps = _service.EnumerateApps();

        // KB 开头的 Windows Update 补丁不应出现在清单里
        Assert.DoesNotContain(apps, a => a.DisplayName.StartsWith("KB", StringComparison.OrdinalIgnoreCase)
                                        && a.DisplayName.Length >= 4
                                        && char.IsDigit(a.DisplayName[2]));
    }

    [Fact]
    public void EnumerateApps_FiltersRedistributables()
    {
        var apps = _service.EnumerateApps();
        Assert.DoesNotContain(apps, a => a.DisplayName.Contains("Redistributable", StringComparison.OrdinalIgnoreCase));
    }
}
