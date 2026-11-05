using System.Security.Cryptography;
using System.Text;
using CleanMaster.Rules;

namespace CleanMaster.Tests.Rules;

/// <summary>
/// 规则定义指纹：把每条规则的**全部可审计字段**规范化后求哈希。
///
/// 为什么需要它：`RuleSnapshotTests` 只锁定 名称/分类/安全等级，够防"规则丢了"，但防不住
/// "同一条规则的路径模板、匹配模式、最大深度被悄悄改了"。RC-6（规则外置为 TOML）要动的正是
/// 这些字段，必须有东西能证明"迁移前后完全等价"。
///
/// 只取与机器无关的字段（不含解析后的真实路径），因此在任何机器上结果一致。
/// 有意修改规则时：先跑测试拿到新的指纹，确认 diff 只包含你想要的改动，再更新下面的常量。
/// </summary>
public class RuleDefinitionFingerprintTests
{
    /// <summary>规则总数。新增/删除规则必须同时改这里和指纹。</summary>
    private const int ExpectedRuleCount = 124;

    /// <summary>
    /// 已迁移到 Rules/Catalog/*.toml 的规则数（只有数据驱动规则才带 Id）。
    /// 每迁移一批就更新这里 —— 强制迁移进度可见，避免"以为迁完了其实只迁了 5 条"。
    /// </summary>
    private const int ExpectedMigratedRuleCount = 112;

    /// <summary>
    /// 全部规则定义（规范化后）的 SHA256。
    ///
    /// 迁移批次会**有意**改变这个值：路径从代码表达式变成数据模板（如 <c>C:\Windows\Temp</c> → <c>{SYSTEMDRIVE}\Windows\Temp</c>）。
    /// 真正的等价性证据是 RuleCatalogTests.CatalogRules_AreEquivalentToHardcodedRules（逐字段 + 解析后路径对比），
    /// 指纹只负责"除此之外没有别的改动"。
    /// </summary>
    private const string ExpectedFingerprint = "3DAC2CF96C7A15FD063D9024C13CABAB89A0AF722EE9471733A977D4011654D1";

    [Fact]
    public void RuleDefinitions_MatchLockedFingerprint()
    {
        var rules = RuleDatabase.GetAllRules();

        Assert.Equal(ExpectedRuleCount, rules.Count);
        Assert.Equal(ExpectedMigratedRuleCount, rules.Count(r => !string.IsNullOrEmpty(r.Id)));

        var canonical = string.Join("\n", rules
            .Select(Canonicalize)
            .OrderBy(s => s, StringComparer.Ordinal));

        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        Assert.True(actual == ExpectedFingerprint,
            $"规则定义指纹不一致（期望 {ExpectedFingerprint}，实际 {actual}）。\n" +
            "若改动是有意的，请核对后更新 ExpectedFingerprint；否则说明规则被意外修改。\n" +
            "排查建议：用 CleanMaster.Cli.exe scan --json 对比迁移前后输出。");
    }

    [Fact]
    public void RuleDefinitions_AllHaveStableIdentity()
    {
        // 外置成数据文件的前提：每条规则都能用稳定字段唯一标识，且字段本身合法
        var rules = RuleDatabase.GetAllRules();

        foreach (var rule in rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Name), "规则缺少名称，无法外置");
            Assert.True(Enum.IsDefined(rule.Category), $"{rule.Name}: 分类非法");
            Assert.True(Enum.IsDefined(rule.Safety), $"{rule.Name}: 安全等级非法");
            Assert.True(rule.MaxDepth >= -1, $"{rule.Name}: MaxDepth 非法 ({rule.MaxDepth})");
        }

        var duplicates = rules.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "规则名重复，外置后无法唯一定位：" + string.Join(", ", duplicates));
    }

    /// <summary>把规则投影成与机器无关的稳定字符串（不含解析后的路径）。</summary>
    private static string Canonicalize(CleanupRule rule)
    {
        var patterns = rule.FilePatterns == null ? "" : string.Join(",", rule.FilePatterns.OrderBy(p => p, StringComparer.Ordinal));

        // 数据驱动规则用 PathTemplate（含占位符，机器无关）；硬编码规则退回 Path。
        var pathIdentity = string.IsNullOrEmpty(rule.PathTemplate) ? rule.Path : rule.PathTemplate;

        return string.Join("|",
            rule.Name,
            rule.Category.ToString(),
            rule.Safety.ToString(),
            rule.SoftwareName ?? "",
            rule.FileType ?? "",
            rule.MaxDepth.ToString(),
            rule.PatternsAreDirectories ? "dir" : "file",
            rule.IsEnabled ? "on" : "off",
            patterns,
            pathIdentity ?? "");
    }
}
