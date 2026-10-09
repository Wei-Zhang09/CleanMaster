using CleanMaster.Models;
using CleanMaster.Rules;

namespace CleanMaster.Tests.Rules;

/// <summary>
/// Regression tests for the WeChat rule de-duplication fix.
/// Before the fix, three "WeChat Cache" rules pointed at the same Roaming\Tencent\WeChat
/// directory, causing triple-counting in scan results.
/// </summary>
public class RuleDatabaseRegressionTests
{
    [Fact]
    public void GetAllRules_WeChatRules_AreNotDuplicated()
    {
        var rules = RuleDatabase.GetAllRules();

        var weChatRules = rules.Where(r => r.Name.Contains("WeChat", StringComparison.OrdinalIgnoreCase)).ToList();

        // 微信有多个缓存规则（3.x 的 WeChat Cache/Files Cache + 4.0 的 xwechat 系列），
        // 核心约束是「无重复名、无重复路径」，而非固定数量。
        Assert.True(weChatRules.Count >= 2, $"Expected at least 2 WeChat rules, got {weChatRules.Count}");

        var names = weChatRules.Select(r => r.Name).Distinct().ToList();
        Assert.Equal(weChatRules.Count, names.Count); // no duplicate names

        // No two rules may resolve to the same NON-EMPTY path.
        // 注意：微信目录在 CI 环境不存在时，多条规则的 GetResolvedPath() 都返回空串，
        // 空串不代表"重复路径"，应排除后再去重比较。
        var paths = weChatRules
            .Select(r => r.GetResolvedPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
        var distinctPaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(paths.Count, distinctPaths.Count);
    }

    [Fact]
    public void GetAllRules_AllRulesHaveDistinctNamePathPairs()
    {
        var rules = RuleDatabase.GetAllRules();

        var keys = rules.Select(r => $"{r.Name}|{r.GetResolvedPath()}").ToList();
        var distinct = keys.Distinct().ToList();

        // Rules may legitimately share a name if paths differ, but never both
        Assert.Equal(keys.Count, distinct.Count);
    }
}
