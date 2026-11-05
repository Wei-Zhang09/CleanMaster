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
        Assert.Equal(5, result.Rules.Count);
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
    /// 迁移等价性：TOML 里的 5 条规则必须与 RuleDatabase 里的同名硬编码规则逐字段一致。
    /// 这条测试红了，说明迁移改变了行为，不能合并。
    /// </summary>
    [Fact]
    public void CatalogRules_AreEquivalentToHardcodedRules()
    {
        var catalog = RuleCatalogLoader.LoadEmbedded();
        Assert.True(catalog.IsValid, string.Join("\n", catalog.Errors));

        var hardcoded = RuleDatabase.GetAllRules().ToDictionary(r => r.Name, r => r, StringComparer.Ordinal);

        foreach (var definition in catalog.Rules)
        {
            Assert.True(hardcoded.TryGetValue(definition.Name, out var expected),
                $"硬编码规则库里找不到同名规则：{definition.Name}");

            var actual = RuleCatalogLoader.ToCleanupRule(definition);

            Assert.Equal(expected!.Category, actual.Category);
            Assert.Equal(expected.Safety, actual.Safety);
            Assert.Equal(expected.MaxDepth, actual.MaxDepth);
            Assert.Equal(expected.IsEnabled, actual.IsEnabled);
            Assert.Equal(expected.PatternsAreDirectories, actual.PatternsAreDirectories);
            Assert.Equal(expected.SoftwareName ?? "", actual.SoftwareName ?? "");
            Assert.Equal(expected.FileType ?? "", actual.FileType ?? "");

            var expectedPatterns = expected.FilePatterns ?? Array.Empty<string>();
            var actualPatterns = actual.FilePatterns ?? Array.Empty<string>();
            Assert.Equal(expectedPatterns, actualPatterns);

            var expectedPaths = expected.GetResolvedPaths().Select(Normalize).OrderBy(p => p).ToList();
            var actualPaths = actual.GetResolvedPaths().Select(Normalize).OrderBy(p => p).ToList();
            Assert.Equal(expectedPaths, actualPaths);
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
