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
        Assert.Equal(33, result.Rules.Count);
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
        ("browser.webcache", "WebCache", CleanCategory.BrowserCache, CleanSafety.Caution, @"{LOCAL}\Microsoft\Windows\WebCache", Array.Empty<string>(), true, -1)
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
            Assert.Equal(expected.PathTemplate, actual.EffectivePaths.Single());
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
