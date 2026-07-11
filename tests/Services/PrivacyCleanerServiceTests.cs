using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class PrivacyCleanerServiceTests
{
    private readonly PrivacyCleanerService _service = new();

    [Fact]
    public void Scan_ReturnsNonEmptyList()
    {
        var items = _service.Scan();

        Assert.NotNull(items);
        Assert.NotEmpty(items);
    }

    [Fact]
    public void Scan_ContainsRecentDocsItem()
    {
        var items = _service.Scan();

        Assert.Contains(items, i => i.Type == "RecentDocs");
    }

    [Fact]
    public void Scan_ContainsDnsCacheItem()
    {
        var items = _service.Scan();

        Assert.Contains(items, i => i.Type == "DnsCache");
    }

    [Fact]
    public void Scan_AllItemsHaveNonEmptyNameTypeDescription()
    {
        var items = _service.Scan();

        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.Name)));
        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.Type)));
        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.Description)));
    }

    [Fact]
    public void Scan_DnsCacheItem_HasEmptyPath()
    {
        var items = _service.Scan();
        var dnsCache = items.First(i => i.Type == "DnsCache");

        Assert.Equal("", dnsCache.Path);
    }

    [Fact]
    public void Scan_AllItemsHaveCanCleanTrue()
    {
        var items = _service.Scan();

        Assert.All(items, i => Assert.True(i.CanClean));
    }

    #region PrivacyItem Model Tests

    [Fact]
    public void PrivacyItem_DefaultProperties_AreEmptyStrings()
    {
        var item = new PrivacyItem();

        Assert.Equal("", item.Name);
        Assert.Equal("", item.Path);
        Assert.Equal("", item.Type);
        Assert.Equal("", item.Description);
    }

    [Fact]
    public void PrivacyItem_CanClean_DefaultsToFalse()
    {
        var item = new PrivacyItem();

        Assert.False(item.CanClean);
    }

    #endregion
}
