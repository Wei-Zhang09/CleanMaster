using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class SettingsServiceTests
{
    private readonly SettingsService _service = new();

    #region AppSettings Model Tests

    [Fact]
    public void AppSettings_DefaultWebsiteUrl_IsCorrect()
    {
        var settings = new AppSettings();

        Assert.Equal("https://awe-software-production.up.railway.app", settings.WebsiteUrl);
    }

    [Fact]
    public void AppSettings_DefaultApiBaseUrl_IsNotEmpty()
    {
        var settings = new AppSettings();

        Assert.False(string.IsNullOrEmpty(settings.ApiBaseUrl));
    }

    [Fact]
    public void AppSettings_DefaultEnableRemoteSync_IsFalse()
    {
        var settings = new AppSettings();

        Assert.False(settings.EnableRemoteSync);
    }

    #endregion

    #region SettingsService Static Method Tests

    [Fact]
    public void Get_ReturnsNonNullAppSettings()
    {
        var settings = _service.Get();

        Assert.NotNull(settings);
    }

    [Fact]
    public void Get_ReturnsCachedInstance_OnSecondCall()
    {
        var first = _service.Get();
        var second = _service.Get();

        Assert.Same(first, second);
    }

    [Fact]
    public void Save_UpdatesTheCachedInstance()
    {
        var testSettings = new AppSettings
        {
            WebsiteUrl = "https://test-save.example.com",
            EnableRemoteSync = true,
        };

        _service.Save(testSettings);
        var retrieved = _service.Get();

        Assert.Same(testSettings, retrieved);
    }

    [Fact]
    public void Save_ThenGet_ReturnsUpdatedSettings()
    {
        var original = new AppSettings
        {
            WebsiteUrl = "https://original.example.com",
            EnableRemoteSync = false,
        };

        _service.Save(original);
        var retrieved = _service.Get();

        Assert.Equal("https://original.example.com", retrieved.WebsiteUrl);
        Assert.False(retrieved.EnableRemoteSync);

        // Save new values and verify they are reflected
        var updated = new AppSettings
        {
            WebsiteUrl = "https://updated.example.com",
            EnableRemoteSync = true,
        };

        _service.Save(updated);
        var retrievedUpdated = _service.Get();

        Assert.Equal("https://updated.example.com", retrievedUpdated.WebsiteUrl);
        Assert.True(retrievedUpdated.EnableRemoteSync);
    }

    #endregion
}
