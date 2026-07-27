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

        // Should be exactly: "WeChat Cache", "WeChat Files Cache", "WeChat Temp"
        Assert.True(weChatRules.Count >= 2, $"Expected at least 2 WeChat rules, got {weChatRules.Count}");
        Assert.True(weChatRules.Count <= 3, $"Expected at most 3 WeChat rules, got {weChatRules.Count}");

        var names = weChatRules.Select(r => r.Name).Distinct().ToList();
        Assert.Equal(weChatRules.Count, names.Count); // no duplicate names

        // No two rules may resolve to the same path
        var paths = weChatRules.Select(r => r.GetResolvedPath()).ToList();
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
