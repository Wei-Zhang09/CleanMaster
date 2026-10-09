using CleanMaster.Rules;

namespace CleanMaster.Tests.Rules;

public class AppCacheMapTests
{
    [Fact]
    public void ResolvePath_LocalPlaceholder_ExpandsToLocalAppData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var result = AppCacheMap.ResolvePath(@"{LOCAL}\Foo\Bar", "");
        Assert.Equal(Path.Combine(local, "Foo", "Bar"), result);
    }

    [Fact]
    public void ResolvePath_RoamingPlaceholder_ExpandsToAppData()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var result = AppCacheMap.ResolvePath(@"{ROAMING}\Foo", "");
        Assert.Equal(Path.Combine(roaming, "Foo"), result);
    }

    [Fact]
    public void ResolvePath_UserPlaceholder_ExpandsToUserProfile()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = AppCacheMap.ResolvePath(@"{USER}\.ollama\models", "");
        Assert.Equal(Path.Combine(user, ".ollama", "models"), result);
    }

    [Fact]
    public void ResolvePath_InstallPlaceholder_UsesInstallLocation()
    {
        var result = AppCacheMap.ResolvePath(@"{INSTALL}\steamapps\shadercache", @"D:\Games\Steam");
        Assert.Equal(@"D:\Games\Steam\steamapps\shadercache", result);
    }

    [Fact]
    public void ResolvePath_InstallPlaceholder_TrimsQuotes()
    {
        // 注册表 InstallLocation 常见格式："D:\MySQL Server"（引号包裹路径，无尾斜杠）
        var result = AppCacheMap.ResolvePath(@"{INSTALL}\data", "\"D:\\MySQL Server\"");
        Assert.Equal(@"D:\MySQL Server\data", result);
    }

    [Fact]
    public void ResolvePath_InstallPlaceholder_TrimsTrailingSlash()
    {
        var result = AppCacheMap.ResolvePath(@"{INSTALL}\data", @"D:\MySQL Server\");
        Assert.Equal(@"D:\MySQL Server\data", result);
    }

    [Fact]
    public void Entries_AllHaveKeywordAndPattern()
    {
        Assert.NotEmpty(AppCacheMap.Entries);
        Assert.All(AppCacheMap.Entries, e =>
        {
            Assert.False(string.IsNullOrEmpty(e.DisplayNameKeyword), "关键词不应为空");
            Assert.NotEmpty(e.Patterns);
        });
    }

    [Fact]
    public void Entries_NoDuplicateKeywords()
    {
        var keywords = AppCacheMap.Entries.Select(e => e.DisplayNameKeyword).ToList();
        Assert.Equal(keywords.Count, keywords.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Entries_CoverKnownValuableApps()
    {
        var keywords = AppCacheMap.Entries.Select(e => e.DisplayNameKeyword).ToList();
        Assert.Contains("Clash Verge", keywords);
        Assert.Contains("Cursor", keywords);
        Assert.Contains("Ollama", keywords);
        Assert.Contains("NVIDIA App", keywords);
        Assert.Contains("企业微信", keywords);
    }
}
