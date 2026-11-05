using CleanMaster.Models;
using CleanMaster.Rules;
using CleanMaster.Rules.Data;

namespace CleanMaster.Tests.Rules;

/// <summary>
/// RC-6 第 1-2 步的验收：规则目录能加载、能校验、能与硬编码规则**逐字段等价**。
///
/// 试点文件 Rules/Catalog/system/temp.toml 含 5 条临时文件规则。
/// 等价性断言是迁移的核心证据：任何字段（分类/安全/深度/模式/路径）不一致都会红。
/// </summary>
public class RuleCatalogTests
{
    // ---------------- 加载与等价 ----------------

    [Fact]
    public void EmbeddedCatalog_LoadsWithoutErrorsOrWarnings()
    {
        var result = RuleCatalogLoader.LoadEmbedded();

        Assert.True(result.IsValid, "规则目录存在校验错误：\n" + string.Join("\n", result.Errors));
        Assert.Empty(result.Warnings);            // 试点规则都写了 evidence
        Assert.Equal(102, result.Rules.Count);
        Assert.All(result.Rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Id)));
    }

    [Fact]
    public void EmbeddedCatalog_ConvertsToWorkingCleanupRules()
    {
        var catalog = RuleCatalogLoader.LoadEmbedded();
        var userTemp = catalog.Rules.Single(r => r.Id == "system.temp.user");
        var rule = RuleCatalogLoader.ToCleanupRule(userTemp);

        Assert.Equal("User Temp", rule.Name);
        Assert.Equal(CleanCategory.TempFiles, rule.Category);
        Assert.Equal(CleanSafety.Safe, rule.Safety);
        Assert.Equal(-1, rule.MaxDepth);
        Assert.True(rule.IsEnabled);

        // 路径按当前环境展开，且与 Path.GetTempPath() 指向同一目录（只差结尾分隔符）
        var resolved = rule.GetResolvedPath();
        Assert.Equal(Normalize(Path.GetTempPath()), Normalize(resolved));
    }

    /// <summary>
    /// 迁移黄金表：每条已迁移规则的**完整定义**锁在这里。
    ///
    /// 为什么不能跟硬编码规则比：迁移完成后硬编码那条已被删除，没有对照物了。
    /// 所以把迁移当时的定义固化成表 —— 之后任何人改 TOML 的路径模板/模式/安全等级都会被抓住。
    /// 新增迁移批次时，把新规则的期望值加进这张表。
    /// </summary>
    private static readonly (string Id, string Name, CleanCategory Category, CleanSafety Safety,
        string PathTemplate, string[] Patterns, bool PatternsAreDirectories, int MaxDepth)[] Golden =
    {
        ("system.logs.cbs", "CBS Logs", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\Logs\CBS", Array.Empty<string>(), true, -1),
        ("system.logs.compressed", "Windows Logs Compressed", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\Logs\Compressed", Array.Empty<string>(), true, -1),
        ("system.logs.debug", "Windows Debug", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\debug", Array.Empty<string>(), true, -1),
        ("system.logs.defender-history", "Windows Defender History", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\ProgramData\Microsoft\Windows Defender\Scans\History", Array.Empty<string>(), true, -1),
        ("system.logs.dism", "DISM Logs", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\Logs\DISM", Array.Empty<string>(), true, -1),
        ("system.logs.dpx", "DPX Logs", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\Logs\DPX", Array.Empty<string>(), true, -1),
        ("system.logs.logfiles", "Windows LogFiles", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\System32\LogFiles", Array.Empty<string>(), true, -1),
        ("system.logs.panther", "Setup Logs (Panther)", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\Panther", Array.Empty<string>(), true, -1),
        ("system.logs.security", "Security Logs", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\security\logs", Array.Empty<string>(), true, -1),
        ("system.logs.sysreset", "SysReset Logs", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\$SysReset", Array.Empty<string>(), true, -1),
        ("system.logs.top-level", "Windows Logs (top-level)", CleanCategory.WindowsLogs, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\Logs", new[] { "*.log", "*.etl", "*.cab", "*.xml" }, false, -1),
        ("system.temp.local-service", "LocalService Temp", CleanCategory.TempFiles, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\ServiceProfiles\LocalService\AppData\Local\Temp", Array.Empty<string>(), true, -1),
        ("system.temp.network-service", "NetworkService Temp", CleanCategory.TempFiles, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\ServiceProfiles\NetworkService\AppData\Local\Temp", Array.Empty<string>(), true, -1),
        ("system.temp.prefetch", "Prefetch", CleanCategory.TempFiles, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\Prefetch", Array.Empty<string>(), true, -1),
        ("system.temp.user", "User Temp", CleanCategory.TempFiles, CleanSafety.Safe, @"{TEMP}", Array.Empty<string>(), true, -1),
        ("system.temp.windows", "Windows Temp", CleanCategory.TempFiles, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\Temp", Array.Empty<string>(), true, -1),
        ("system.update.datastore", "WU DataStore", CleanCategory.WindowsUpdate, CleanSafety.Caution, @"{SYSTEMDRIVE}\Windows\SoftwareDistribution\DataStore", Array.Empty<string>(), true, -1),
        ("system.update.delivery-optimization", "Delivery Optimization", CleanCategory.WindowsUpdate, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\SoftwareDistribution\DeliveryOptimization", Array.Empty<string>(), true, -1),
        ("system.update.download-cache", "WU Download Cache", CleanCategory.WindowsUpdate, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\SoftwareDistribution\Download", Array.Empty<string>(), true, -1),
        ("system.update.windows-old", "Windows.old", CleanCategory.WindowsUpdate, CleanSafety.Caution, @"{SYSTEMDRIVE}\Windows.old", Array.Empty<string>(), true, -1),
        ("browser.inetcache", "INetCache (IE/Edge legacy)", CleanCategory.BrowserCache, CleanSafety.Safe, @"{LOCAL}\Microsoft\Windows\INetCache", Array.Empty<string>(), true, -1),
        ("browser.opera.cache", "Opera Cache", CleanCategory.BrowserCache, CleanSafety.Safe, @"{LOCAL}\Opera Software\Opera Stable\Cache", Array.Empty<string>(), true, -1),
        ("browser.webcache", "WebCache", CleanCategory.BrowserCache, CleanSafety.Caution, @"{LOCAL}\Microsoft\Windows\WebCache", Array.Empty<string>(), true, -1),
        ("devtools.gradle-caches", "Gradle Caches", CleanCategory.DevToolCache, CleanSafety.Safe, @"{USER}\.gradle\caches", Array.Empty<string>(), true, -1),
        ("devtools.gradle-daemon-logs", "Gradle Daemon Logs", CleanCategory.DevToolCache, CleanSafety.Safe, @"{USER}\.gradle\daemon", Array.Empty<string>(), true, -1),
        ("devtools.nuget-packages", "NuGet Packages", CleanCategory.DevToolCache, CleanSafety.Caution, @"{USER}\.nuget\packages", Array.Empty<string>(), true, -1),
        ("devtools.nuget-http-cache", "NuGet HTTP Cache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{LOCAL}\NuGet\v3-cache", Array.Empty<string>(), true, -1),
        ("devtools.npm-cache", "npm Cache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{ROAMING}\npm-cache", Array.Empty<string>(), true, -1),
        ("devtools.pip-cache", "pip Cache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{LOCAL}\pip\cache", Array.Empty<string>(), true, -1),
        ("devtools.maven-repository", "Maven Repository", CleanCategory.DevToolCache, CleanSafety.Caution, @"{USER}\.m2\repository", Array.Empty<string>(), true, -1),
        ("devtools.vs-code-cache", "VS Code Cache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{ROAMING}\Code\Cache", Array.Empty<string>(), true, -1),
        ("devtools.vs-code-cacheddata", "VS Code CachedData", CleanCategory.DevToolCache, CleanSafety.Safe, @"{ROAMING}\Code\CachedData", Array.Empty<string>(), true, -1),
        ("devtools.vs-code-gpucache", "VS Code GPUCache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{ROAMING}\Code\GPUCache", Array.Empty<string>(), true, -1),
        ("devtools.vs-code-logs", "VS Code logs", CleanCategory.DevToolCache, CleanSafety.Safe, @"{ROAMING}\Code\logs", Array.Empty<string>(), true, -1),
        ("devtools.vs-code-workspacestorage", "VS Code workspaceStorage", CleanCategory.DevToolCache, CleanSafety.Safe, @"{ROAMING}\Code\User\workspaceStorage", Array.Empty<string>(), true, -1),
        ("devtools.cargo-cache-rust", "Cargo Cache (Rust)", CleanCategory.DevToolCache, CleanSafety.Safe, @"{USER}\.cargo\registry\cache", Array.Empty<string>(), true, -1),
        ("devtools.conda-pkgs", "Conda pkgs", CleanCategory.DevToolCache, CleanSafety.Caution, @"{USER}\.conda\pkgs", Array.Empty<string>(), true, -1),
        ("devtools.yarn-cache", "Yarn Cache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{LOCAL}\Yarn\Cache", Array.Empty<string>(), true, -1),
        ("devtools.pnpm-store", "pnpm store", CleanCategory.DevToolCache, CleanSafety.Safe, @"{USER}\.pnpm-store", Array.Empty<string>(), true, -1),
        ("devtools.bun-install-cache", "Bun install cache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{USER}\.bun\install\cache", Array.Empty<string>(), true, -1),
        ("devtools.go-mod-cache", "Go mod cache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{USER}\go\pkg\mod", Array.Empty<string>(), true, -1),
        ("devtools.android-build-cache", "Android build-cache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{USER}\.android\build-cache", Array.Empty<string>(), true, -1),
        ("devtools.docker-desktop-logs", "Docker Desktop logs", CleanCategory.DevToolCache, CleanSafety.Safe, @"{LOCAL}\Docker\log", Array.Empty<string>(), true, -1),
        ("devtools.postman-cache", "Postman Cache", CleanCategory.DevToolCache, CleanSafety.Safe, @"{ROAMING}\Postman\Cache", Array.Empty<string>(), true, -1),
        ("app.codex-temp", "Codex Temp", CleanCategory.AppCache, CleanSafety.Safe, @"{USER}\.codex\.tmp", Array.Empty<string>(), true, -1),
        ("app.lingma-cache", "Lingma Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{USER}\.lingma\cache", Array.Empty<string>(), true, -1),
        ("app.lingma-index", "Lingma Index", CleanCategory.AppCache, CleanSafety.Safe, @"{USER}\.lingma\index", Array.Empty<string>(), true, -1),
        ("app.lingma-logs", "Lingma Logs", CleanCategory.AppCache, CleanSafety.Safe, @"{USER}\.lingma\logs", Array.Empty<string>(), true, -1),
        ("app.marscode-cache", "MarsCode Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{USER}\.marscode\cache", Array.Empty<string>(), true, -1),
        ("app.qoder-shared-client", "Qoder shared_client", CleanCategory.AppCache, CleanSafety.Safe, @"{USER}\.qoder-cn\shared_client", Array.Empty<string>(), true, -1),
        ("app.wechat-4-0-logs-xwechat", "WeChat 4.0 Logs (xwechat)", CleanCategory.AppCache, CleanSafety.Safe, @"{ROAMING}\Tencent\xwechat\log", Array.Empty<string>(), true, -1),
        ("app.wechat-4-0-plugin-cache-xwechat", "WeChat 4.0 Plugin Cache (xwechat)", CleanCategory.AppCache, CleanSafety.Caution, @"{ROAMING}\Tencent\xwechat\xplugin", Array.Empty<string>(), true, -1),
        ("app.qq-cache", "QQ Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{ROAMING}\Tencent\QQ\Cache|{ROAMING}\Tencent\QQ\GPUCache|{ROAMING}\Tencent\QQ\Code Cache|{ROAMING}\Tencent\QQ\logs|{ROAMING}\Tencent\QQ\Temp", Array.Empty<string>(), true, -1),
        ("app.qqnt-cache", "QQNT Cache", CleanCategory.AppCache, CleanSafety.Caution, @"{ROAMING}\Tencent\QQNT\Cache|{ROAMING}\Tencent\QQNT\GPUCache|{ROAMING}\Tencent\QQNT\Code Cache|{ROAMING}\Tencent\QQNT\logs", Array.Empty<string>(), true, -1),
        ("app.tencent-video-cache", "Tencent Video Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Tencent\QQLive\Cache|{LOCAL}\Tencent\QQLive\GPUCache|{LOCAL}\Tencent\QQLive\logs", Array.Empty<string>(), true, -1),
        ("app.wps-cache", "WPS Cache", CleanCategory.AppCache, CleanSafety.Caution, @"{ROAMING}\kingsoft\wps\Cache|{ROAMING}\kingsoft\wps\backup", Array.Empty<string>(), true, -1),
        ("app.wechat-4-0-network-cache-xwechat", "WeChat 4.0 Network Cache (xwechat)", CleanCategory.AppCache, CleanSafety.Safe, @"{ROAMING}\Tencent\xwechat\net|{ROAMING}\Tencent\xwechat\net_1|{ROAMING}\Tencent\xwechat\net_2|{ROAMING}\Tencent\xwechat\net_3|{ROAMING}\Tencent\xwechat\net_4|{ROAMING}\Tencent\xwechat\net_5|{ROAMING}\Tencent\xwechat\net_6|{ROAMING}\Tencent\xwechat\net_7|{ROAMING}\Tencent\xwechat\net_8|{ROAMING}\Tencent\xwechat\crashinfo|{ROAMING}\Tencent\xwechat\update", Array.Empty<string>(), true, -1),
        ("app.wechat-cache", "WeChat Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{ROAMING}\Tencent\WeChat\Cache|{ROAMING}\Tencent\WeChat\GPUCache|{ROAMING}\Tencent\WeChat\Code Cache|{ROAMING}\Tencent\WeChat\logs|{ROAMING}\Tencent\WeChat\Temp", Array.Empty<string>(), true, -1),
        ("app.wegame-cache", "WeGame Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{ROAMING}\Tencent\WeGame\Cache|{ROAMING}\Tencent\WeGame\logs", Array.Empty<string>(), true, -1),
        ("app.iqiyi-cache", "iQiyi (爱奇艺) Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\iqiyi\Cache|{LOCAL}\iqiyi\GPUCache|{LOCAL}\iqiyi\Code Cache|{LOCAL}\iqiyi\logs", Array.Empty<string>(), true, -1),
        ("app.aliyunpan-cache", "AliyunPan (阿里云盘) Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Alibaba\AliyunPan\Cache|{LOCAL}\Alibaba\AliyunPan\GPUCache|{LOCAL}\Alibaba\AliyunPan\logs|{LOCAL}\Alibaba\AliyunPan\tmp", Array.Empty<string>(), true, -1),
        ("app.bilibili-cache", "Bilibili Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Bilibili\Cache|{LOCAL}\Bilibili\GPUCache|{LOCAL}\Bilibili\Code Cache|{LOCAL}\Bilibili\logs", Array.Empty<string>(), true, -1),
        ("app.douyin-cache", "Douyin (抖音) Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Douyin\Cache|{LOCAL}\Douyin\GPUCache|{LOCAL}\Douyin\Code Cache", Array.Empty<string>(), true, -1),
        ("app.kugou-cache", "Kugou (酷狗) Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Kugou\Cache|{LOCAL}\Kugou\GPUCache", Array.Empty<string>(), true, -1),
        ("app.kuwo-cache", "Kuwo (酷我) Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Kuwo\Cache|{LOCAL}\Kuwo\GPUCache", Array.Empty<string>(), true, -1),
        ("app.netease-music-cache", "NetEase Music (网易云音乐) Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\NetEase\CloudMusic\Cache|{LOCAL}\NetEase\CloudMusic\GPUCache|{LOCAL}\NetEase\CloudMusic\WebStorage", Array.Empty<string>(), true, -1),
        ("app.qq-music-cache", "QQ Music Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Tencent\QQMusic\Cache|{LOCAL}\Tencent\QQMusic\GPUCache", Array.Empty<string>(), true, -1),
        ("app.youku-cache", "Youku (优酷) Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Youku\Cache|{LOCAL}\Youku\GPUCache|{LOCAL}\Youku\Code Cache|{LOCAL}\Youku\logs", Array.Empty<string>(), true, -1),
        ("app.115-cache", "115 网盘 Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\115\Cache|{LOCAL}\115\GPUCache|{LOCAL}\115\logs", Array.Empty<string>(), true, -1),
        ("app.baidu-netdisk-cache", "Baidu Netdisk (百度网盘) Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\BaiduNetdisk\Cache|{LOCAL}\BaiduNetdisk\GPUCache|{LOCAL}\BaiduNetdisk\logs|{LOCAL}\BaiduNetdisk\tmp", Array.Empty<string>(), true, -1),
        ("app.directx", "DirectX 着色器缓存", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\D3DSCache", Array.Empty<string>(), true, -1),
        ("app.discord-cache", "Discord Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{ROAMING}\discord\Cache|{ROAMING}\discord\GPUCache|{ROAMING}\discord\Code Cache", Array.Empty<string>(), true, -1),
        ("app.iconcache-db", "IconCache.db", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\IconCache.db", Array.Empty<string>(), true, -1),
        ("app.notion-cache", "Notion Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{ROAMING}\Notion\Cache|{ROAMING}\Notion\GPUCache|{ROAMING}\Notion\Code Cache", Array.Empty<string>(), true, -1),
        ("app.recent-documents", "最近文档记录", CleanCategory.AppCache, CleanSafety.Safe, @"{RECENT}", Array.Empty<string>(), true, -1),
        ("app.thumbnail-cache", "缩略图缓存", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Microsoft\Windows\Explorer", new[] { "iconcache_*.db", "thumbcache_*.db" }, false, -1),
        ("app.crash-dumps", "Crash Dumps", CleanCategory.CrashDumps, CleanSafety.Safe, @"{LOCAL}\CrashDumps", Array.Empty<string>(), true, -1),
        ("app.livekernelreports", "LiveKernelReports", CleanCategory.CrashDumps, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\LiveKernelReports", Array.Empty<string>(), true, -1),
        ("app.memory-dmp", "MEMORY.DMP", CleanCategory.CrashDumps, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\MEMORY.DMP", Array.Empty<string>(), true, -1),
        ("app.minidump", "Minidump", CleanCategory.CrashDumps, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\Minidump", Array.Empty<string>(), true, -1),
        ("app.wer-reportarchive", "WER ReportArchive", CleanCategory.CrashDumps, CleanSafety.Safe, @"{SYSTEMDRIVE}\ProgramData\Microsoft\Windows\WER\ReportArchive", Array.Empty<string>(), true, -1),
        ("app.wer-reportqueue", "WER ReportQueue", CleanCategory.CrashDumps, CleanSafety.Safe, @"{SYSTEMDRIVE}\ProgramData\Microsoft\Windows\WER\ReportQueue", Array.Empty<string>(), true, -1),
        ("app.windows-error-reporting-root", "Windows Error Reporting (root)", CleanCategory.CrashDumps, CleanSafety.Safe, @"{SYSTEMDRIVE}\ProgramData\Microsoft\Windows\WER", new[] { "*.cab", "*.dmp", "*.tmp", "*.wer" }, false, -1),
        ("app.font-cache", "Font Cache", CleanCategory.AppCache, CleanSafety.Safe, @"{SYSTEMDRIVE}\Windows\ServiceProfiles\LocalService\AppData\Local\FontCache", Array.Empty<string>(), true, -1),
        ("app.windows-search-index", "Windows Search Index", CleanCategory.AppCache, CleanSafety.Caution, @"{SYSTEMDRIVE}\ProgramData\Microsoft\Search\Data", Array.Empty<string>(), true, -1),
        ("app.windows", "Windows 通知缓存", CleanCategory.AppCache, CleanSafety.Safe, @"{LOCAL}\Microsoft\Windows\Notifications", Array.Empty<string>(), true, -1),
        ("app.appcompat-cache", "程序兼容性缓存 (AppCompat)", CleanCategory.AppCache, CleanSafety.Dangerous, @"{SYSTEMDRIVE}\Windows\AppCompat\Programs", Array.Empty<string>(), true, -1),
    };

    [Fact]
    public void CatalogRules_MatchLockedGoldenTable()
    {
        var catalog = RuleCatalogLoader.LoadEmbedded();
        Assert.True(catalog.IsValid, string.Join("\n", catalog.Errors));

        // 只校验静态路径规则；resolver 规则由 RuleResolverTests 的黄金表负责
        var pathRules = catalog.Rules.Where(r => !r.UsesResolver).ToList();
        Assert.Equal(Golden.Length, pathRules.Count);

        foreach (var expected in Golden)
        {
            var actual = pathRules.Single(r => r.Id == expected.Id);

            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Category, Enum.Parse<CleanCategory>(actual.Category, true));
            Assert.Equal(expected.Safety, Enum.Parse<CleanSafety>(actual.Safety, true));
            // 多路径规则用 | 连接成一个字符串比较（单路径规则不受影响）
            Assert.Equal(expected.PathTemplate, string.Join("|", actual.EffectivePaths));
            Assert.Equal(expected.Patterns, actual.Patterns);
            Assert.Equal(expected.PatternsAreDirectories, actual.PatternsAreDirectories);
            Assert.Equal(expected.MaxDepth, actual.MaxDepth);
            Assert.True(actual.Enabled);
            Assert.False(string.IsNullOrWhiteSpace(actual.Evidence), $"{expected.Id}: 迁移规则必须写 evidence");
            Assert.False(string.IsNullOrWhiteSpace(actual.VerifiedAt), $"{expected.Id}: 迁移规则必须写 verified_at");
        }
    }

    // ---------------- 校验：错误必须被抓住，不能静默降级 ----------------

    [Fact]
    public void Parse_MissingRequiredField_ReportsError()
    {
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.missing-name"
            category = "TempFiles"
            safety = "Safe"
            path = '{TEMP}'
            """, "test.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("name"));
    }

    [Fact]
    public void Parse_InvalidCategory_ReportsErrorWithAllowedValues()
    {
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.bad-category"
            name = "Bad"
            category = "NoSuchCategory"
            safety = "Safe"
            path = '{TEMP}'
            """, "test.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("category") && e.Contains("TempFiles"));
    }

    [Fact]
    public void Parse_UnknownPlaceholder_ReportsError()
    {
        // 拼错的占位符不能当字面量：否则规则永远扫不到东西，表现为"这个缓存项从不出现"
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.bad-placeholder"
            name = "Bad"
            category = "TempFiles"
            safety = "Safe"
            path = '{NOPE}\Temp'
            """, "test.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("{NOPE}"));
    }

    [Fact]
    public void Parse_BothPathAndPaths_ReportsError()
    {
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.both"
            name = "Both"
            category = "TempFiles"
            safety = "Safe"
            path = '{TEMP}'
            paths = ['{TEMP}']
            """, "test.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("不能同时"));
    }

    [Fact]
    public void Parse_NoPath_ReportsError()
    {
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.no-path"
            name = "NoPath"
            category = "TempFiles"
            safety = "Safe"
            """, "test.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("path"));
    }

    [Fact]
    public void Parse_TypeMismatch_ReportsFieldName()
    {
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.bad-type"
            name = "BadType"
            category = "TempFiles"
            safety = "Safe"
            path = '{TEMP}'
            max_depth = "deep"
            """, "test.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("max_depth"));
    }

    [Fact]
    public void Parse_SyntaxError_ReportsLineNumber()
    {
        var result = RuleCatalogLoader.ParseDocument("[[rule]\nid = \"x\"", "broken.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("broken.toml"));
    }

    [Fact]
    public void Parse_MissingEvidence_WarnsButStillLoads()
    {
        var result = RuleCatalogLoader.ParseDocument("""
            [[rule]]
            id = "test.no-evidence"
            name = "NoEvidence"
            category = "TempFiles"
            safety = "Safe"
            path = '{TEMP}'
            """, "test.toml");

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("evidence"));
    }

    [Fact]
    public void ValidateCrossFile_DuplicateIdOrName_ReportsError()
    {
        var a = new RuleDefinition { Id = "dup.id", Name = "A", Category = "TempFiles", Safety = "Safe", Path = "{TEMP}", Source = "a.toml" };
        var b = new RuleDefinition { Id = "dup.id", Name = "B", Category = "TempFiles", Safety = "Safe", Path = "{TEMP}", Source = "b.toml" };
        var c = new RuleDefinition { Id = "other.id", Name = "A", Category = "TempFiles", Safety = "Safe", Path = "{TEMP}", Source = "c.toml" };

        var result = RuleCatalogLoader.ValidateCrossFile(new[] { a, b, c });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("id 重复"));
        Assert.Contains(result.Errors, e => e.Contains("规则名重复"));
    }

    // ---------------- 占位符 ----------------

    [Fact]
    public void Resolver_ExpandsKnownPlaceholders()
    {
        var local = RuleTemplateResolver.Resolve(@" {LOCAL}\Temp".Trim());

        Assert.Equal(Normalize(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")), Normalize(local));
        Assert.Empty(RuleTemplateResolver.FindUnknownPlaceholders(@" {LOCAL}\Temp".Trim()));
    }

    [Fact]
    public void Resolver_DocumentsAndRecentPlaceholders_ResolveToRealFolders()
    {
        // {RECENT} 与 {DOCUMENTS} 是为下一批（最近文档记录等）加的占位符，先在这里锁住行为
        foreach (var placeholder in new[] { "{RECENT}", "{DOCUMENTS}" })
        {
            var resolved = RuleTemplateResolver.Resolve(placeholder);

            Assert.False(string.IsNullOrEmpty(resolved), $"{placeholder} 解析为空");
            Assert.Empty(RuleTemplateResolver.FindUnknownPlaceholders(placeholder));
        }
    }

    [Fact]
    public void Resolver_UnknownPlaceholderResolvesToEmpty()
    {
        Assert.Empty(RuleTemplateResolver.Resolve(@" {NOPE}\Temp".Trim()));
        Assert.Equal(new[] { "NOPE" }, RuleTemplateResolver.FindUnknownPlaceholders(@" {NOPE}\Temp".Trim()));
    }

    [Fact]
    public void Resolver_SystemDriveHasNoTrailingSlash()
    {
        var resolved = RuleTemplateResolver.Resolve(@" {SYSTEMDRIVE}\Windows\Temp".Trim());

        Assert.DoesNotContain("\\\\", resolved);            // 不出现双反斜杠
        Assert.EndsWith(@"\Windows\Temp", resolved);
    }

    private static string Normalize(string path)
        => (path ?? "").TrimEnd('\\', '/').Replace('/', '\\');
}
