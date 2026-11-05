using CleanMaster.Models;
using CleanMaster.Rules.Data;

namespace CleanMaster.Tests.Rules;

/// <summary>
/// 命名解析器机制（RC-6 批次 4）：浏览器缓存这类"路径要运行时枚举"的规则，
/// 用 resolver + 参数表达，解析逻辑留在代码里。
///
/// 这一组测试锁三件事：
/// 1. resolver 规则的定义（名字 + 参数签名）不被悄悄改动；
/// 2. 校验器能抓住未知 resolver / 缺参数 / 与 path 冲突；
/// 3. 解析器本身的行为（跳过非 profile 目录、目录不存在时返回空而不是抛）。
/// </summary>
public class RuleResolverTests
{
    /// <summary>
    /// resolver 规则黄金表：路径由代码算出，无法用路径模板锁定，
    /// 因此锁定「解析器名字 + 参数签名」。参数变了（比如漏掉某个 profile 根目录）就会红。
    /// </summary>
    private static readonly (string Id, string Name, CleanCategory Category, CleanSafety Safety,
        string Resolver, string ArgSignature)[] GoldenResolvers =
    {
        ("browser.brave.cache", "Brave Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe, "chromium-profile-subdir", @"sub=Cache;user_data={LOCAL}\BraveSoftware\Brave-Browser\User Data"),
        ("browser.brave.gpu-cache", "Brave GPUCache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe, "chromium-profile-subdir", @"sub=GPUCache;user_data={LOCAL}\BraveSoftware\Brave-Browser\User Data"),
        ("browser.chrome.cache", "Chrome Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe, "chromium-profile-subdir", @"sub=Cache;user_data={LOCAL}\Google\Chrome\User Data"),
        ("browser.chrome.code-cache", "Chrome Code Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe, "chromium-profile-subdir", @"sub=Code Cache;user_data={LOCAL}\Google\Chrome\User Data"),
        ("browser.chrome.gpu-cache", "Chrome GPUCache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe, "chromium-profile-subdir", @"sub=GPUCache;user_data={LOCAL}\Google\Chrome\User Data"),
        ("browser.edge.cache", "Edge Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe, "chromium-profile-subdir", @"sub=Cache;user_data={LOCAL}\Microsoft\Edge\User Data"),
        ("browser.edge.code-cache", "Edge Code Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe, "chromium-profile-subdir", @"sub=Code Cache;user_data={LOCAL}\Microsoft\Edge\User Data"),
        ("browser.edge.gpu-cache", "Edge GPUCache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe, "chromium-profile-subdir", @"sub=GPUCache;user_data={LOCAL}\Microsoft\Edge\User Data"),
        ("browser.firefox.cache2", "Firefox Cache2", CleanCategory.BrowserCache, CleanSafety.Safe, "firefox-profile-subdir", @"profiles_root={LOCAL}\Mozilla\Firefox\Profiles;sub=cache2"),
        ("browser.vivaldi.cache", "Vivaldi Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe, "chromium-profile-subdir", @"sub=Cache;user_data={LOCAL}\Vivaldi\User Data"),
        ("devtools.jetbrains-caches", "JetBrains Caches", CleanCategory.DevToolCache, CleanSafety.Safe, "children-subdirs", @"parent={LOCAL}\JetBrains;subs=caches,index,log,tmp"),
        ("devtools.vs-code-extension-cache-all-extensions", "VS Code Extension Cache (all extensions)", CleanCategory.DevToolCache, CleanSafety.Safe, "children-subdir", @"parent={USER}\.vscode\extensions;sub=cache"),
        ("devtools.docker-desktop-cache", "Docker Desktop cache", CleanCategory.DevToolCache, CleanSafety.Caution, "parent-subdirs", @"parent={LOCAL}\Docker;subs=cache,tmp"),
        ("app.edge-webview2-cache", "Edge WebView2 Cache", CleanCategory.AppCache, CleanSafety.Safe, "children-subdirs", @"parent={LOCAL}\Microsoft\EdgeWebView\User Data;subs=Cache,Code Cache,GPUCache,Service Worker"),
        ("app.uwp-localcache", "UWP/Store 应用 LocalCache", CleanCategory.AppCache, CleanSafety.Safe, "children-subdir", @"parent={LOCAL}\Packages;sub=LocalCache\Local"),
        ("app.xunlei-cache", "迅雷 cache", CleanCategory.AppCache, CleanSafety.Safe, "parent-subdirs", @"parent={PROGRAMDATA}\Thunder Network\Downloader;subs=cache,logs"),
        ("app.wechat-files-cache", "WeChat Files Cache", CleanCategory.AppCache, CleanSafety.Caution, "children-subdirs", @"parent={DOCUMENTS}\WeChat Files;subs=FileStorage\Temp,FileStorage\TempFromPhone,FileStorage\Cache,FileStorage\CDNFileStorage\Temp"),
        ("app.wecom-cache", "WeCom (企业微信) Cache", CleanCategory.AppCache, CleanSafety.Caution, "parent-subdirs", @"parent={ROAMING}\Tencent\WXWork;subs=Cache,GPUCache,logs,tmp"),
        ("app.dingtalk-cache", "DingTalk (钉钉) Cache", CleanCategory.AppCache, CleanSafety.Safe, "children-subdirs", @"parent={LOCAL}\DingTalk;subs=logs,tmp,Cache,GPUCache"),
        ("app.feishu-cache", "Feishu (飞书) Cache", CleanCategory.AppCache, CleanSafety.Safe, "parent-subdirs", @"parent={LOCAL}\Feishu;subs=LarkShell\cache,LarkShell\logs,Cache,GPUCache"),
        ("app.slack-cache", "Slack Cache", CleanCategory.AppCache, CleanSafety.Safe, "children-subdirs", @"parent={ROAMING}\Slack;subs=Cache,GPUCache,Code Cache"),
    };

    [Fact]
    public void ResolverRules_MatchLockedGoldenTable()
    {
        var catalog = RuleCatalogLoader.LoadEmbedded();
        Assert.True(catalog.IsValid, string.Join("\n", catalog.Errors));

        var resolverRules = catalog.Rules.Where(r => r.UsesResolver).ToList();
        Assert.Equal(GoldenResolvers.Length, resolverRules.Count);

        foreach (var expected in GoldenResolvers)
        {
            var actual = resolverRules.Single(r => r.Id == expected.Id);

            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Category, Enum.Parse<CleanCategory>(actual.Category, true));
            Assert.Equal(expected.Safety, Enum.Parse<CleanSafety>(actual.Safety, true));
            Assert.Equal(expected.Resolver, actual.Resolver);

            var signature = string.Join(";", actual.ResolverArgs
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));
            Assert.Equal(expected.ArgSignature, signature);

            Assert.Empty(actual.EffectivePaths);          // resolver 规则不得再写 path
            Assert.False(string.IsNullOrWhiteSpace(actual.Evidence));
        }
    }

    [Fact]
    public void ResolverRule_ConvertsToRuleThatEnumeratesRealProfiles()
    {
        var catalog = RuleCatalogLoader.LoadEmbedded();
        var edgeCache = catalog.Rules.Single(r => r.Id == "browser.edge.cache");
        var rule = RuleCatalogLoader.ToCleanupRule(edgeCache);

        Assert.Equal("resolver:chromium-profile-subdir", rule.PathTemplate);
        Assert.Equal(CleanCategory.BrowserCache, rule.Category);

        // 本机装了 Edge 时应该能枚举出至少一个 profile 的 Cache 目录
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Edge", "User Data");
        if (!Directory.Exists(userData)) return;

        var resolved = rule.GetResolvedPaths().ToList();
        Assert.All(resolved, p => Assert.EndsWith("Cache", p));
        Assert.All(resolved, p => Assert.True(Directory.Exists(p)));
    }

    [Fact]
    public void Parse_UnknownResolver_ReportsRegisteredNames()
    {
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.unknown-resolver"
            name = "Unknown"
            category = "BrowserCache"
            safety = "Safe"
            resolver = "no-such-resolver"
            """, "test.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("未注册的 resolver") && e.Contains("chromium-profile-subdir"));
    }

    [Fact]
    public void Parse_ResolverMissingRequiredArg_ReportsArgName()
    {
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.missing-arg"
            name = "MissingArg"
            category = "BrowserCache"
            safety = "Safe"
            resolver = "chromium-profile-subdir"

            [rule.resolver_args]
            user_data = '{LOCAL}\\Foo'
            """, "test.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("缺少必需参数 sub"));
    }

    [Fact]
    public void Parse_ResolverWithPath_ReportsConflict()
    {
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.conflict"
            name = "Conflict"
            category = "BrowserCache"
            safety = "Safe"
            path = '{LOCAL}\\Foo'
            resolver = "chromium-profile-subdir"
            """, "test.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("不能同时提供"));
    }

    [Fact]
    public void Resolver_ChromiumProfileSubdir_SkipsNonProfileDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-resolver-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            foreach (var profile in new[] { "Default", "Profile 1", "Snapshots", "Crashpad", "GrShaderCache" })
                Directory.CreateDirectory(Path.Combine(root, profile, "Cache"));

            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["user_data"] = root,
                ["sub"] = "Cache"
            };

            var profiles = RuleResolvers.Resolve("chromium-profile-subdir", args)
                .Select(p => Path.GetFileName(Path.GetDirectoryName(p)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(new[] { "Default", "Profile 1" }, profiles);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Resolver_MissingDirectory_ReturnsEmptyNotThrow()
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["user_data"] = @"C:\NoSuchUserDataRoot_CM",
            ["sub"] = "Cache"
        };

        Assert.Empty(RuleResolvers.Resolve("chromium-profile-subdir", args));
    }

    [Fact]
    public void ResolverRegistry_DeclaresRequiredArgsForEveryResolver()
    {
        // 每个注册的解析器都必须声明必需参数，否则校验器无法在构建期抓住"漏参数"
        foreach (var name in RuleResolvers.Names)
        {
            Assert.NotEmpty(RuleResolvers.RequiredArgs(name));
        }
    }
}
