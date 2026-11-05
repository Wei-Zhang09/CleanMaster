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

    /// <summary>全部规则定义（规范化后）的 SHA256。</summary>
    private const string ExpectedFingerprint = "72C55DC81784E164B284369FF226FA6F8B38482EA027CB6EDDA9112F29B5D54F";

    [Fact]
    public void RuleDefinitions_MatchLockedFingerprint()
    {
        var rules = RuleDatabase.GetAllRules();

        Assert.Equal(ExpectedRuleCount, rules.Count);

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
            rule.Path ?? "");
    }
}
