using CleanMaster.Models;
using CleanMaster.Rules;

namespace CleanMaster.Tests.Rules;

public class RuleDatabaseTests
{
    [Fact]
    public void GetAllRules_ReturnsNonEmptyList_WithMoreThanTenRules()
    {
        var rules = RuleDatabase.GetAllRules();

        Assert.NotNull(rules);
        Assert.NotEmpty(rules);
        Assert.True(rules.Count > 10, $"Expected more than 10 rules, but got {rules.Count}");
    }

    [Fact]
    public void GetAllRules_EveryRuleHasNonEmptyName()
    {
        var rules = RuleDatabase.GetAllRules();

        Assert.All(rules, r => Assert.False(string.IsNullOrEmpty(r.Name),
            $"Rule with empty name found: {r.Description}"));
    }

    [Fact]
    public void GetAllRules_EveryRuleHasValidCleanCategory()
    {
        var rules = RuleDatabase.GetAllRules();

        Assert.All(rules, r => Assert.True(Enum.IsDefined(typeof(CleanCategory), r.Category),
            $"Rule '{r.Name}' has invalid CleanCategory: {r.Category}"));
    }

    [Fact]
    public void GetAllRules_EveryRuleHasValidCleanSafety()
    {
        var rules = RuleDatabase.GetAllRules();

        Assert.All(rules, r => Assert.True(Enum.IsDefined(typeof(CleanSafety), r.Safety),
            $"Rule '{r.Name}' has invalid CleanSafety: {r.Safety}"));
    }

    [Fact]
    public void GetAllRules_GetResolvedPath_ReturnsNonNullForEveryRule()
    {
        var rules = RuleDatabase.GetAllRules();

        Assert.All(rules, r =>
        {
            var path = r.GetResolvedPath();
            Assert.NotNull(path);
        });
    }

    [Fact]
    public void GetAllRules_AllRulesWithPathFactory_HaveNonNullPath()
    {
        var rules = RuleDatabase.GetAllRules().Where(r => r.PathFactory != null);

        Assert.All(rules, r =>
        {
            var path = r.PathFactory!();
            Assert.NotNull(path);
            Assert.False(string.IsNullOrEmpty(path),
                $"Rule '{r.Name}' PathFactory returned null or empty path");
        });
    }

    [Theory]
    [InlineData("Recycle Bin")]
    [InlineData("User Temp")]
    [InlineData("Windows Temp")]
    [InlineData("Edge Cache")]
    [InlineData("Chrome Cache")]
    public void GetAllRules_ContainsSpecificRuleName(string expectedName)
    {
        var rules = RuleDatabase.GetAllRules();

        Assert.Contains(rules, r => r.Name == expectedName);
    }

    [Fact]
    public void GetAllRules_RecycleBinRule_HasCorrectProperties()
    {
        var rules = RuleDatabase.GetAllRules();
        var rule = rules.First(r => r.Name == "Recycle Bin");

        Assert.Equal(CleanSafety.Safe, rule.Safety);
        Assert.Equal(CleanCategory.RecycleBin, rule.Category);
        Assert.True(rule.IsEnabled);
    }

    [Fact]
    public void GetAllRules_HasRulesFromAllCategories()
    {
        var rules = RuleDatabase.GetAllRules();
        var categories = rules.Select(r => r.Category).Distinct().ToList();

        // Should have at least several different categories
        Assert.Contains(CleanCategory.RecycleBin, categories);
        Assert.Contains(CleanCategory.TempFiles, categories);
        Assert.Contains(CleanCategory.BrowserCache, categories);
    }
}
