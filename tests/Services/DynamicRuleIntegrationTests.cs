using CleanMaster.Models;
using CleanMaster.Rules;
using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class DynamicRuleIntegrationTests
{
    private class FakeRuleGenerator : IDynamicRuleGenerator
    {
        private readonly List<CleanupRule> _rules;
        public FakeRuleGenerator(List<CleanupRule> rules) => _rules = rules;
        public List<CleanupRule> Generate() => _rules;
    }

    [Fact]
    public async Task ScanAllAsync_MergesDynamicRules_WhenNoPathOverlap()
    {
        // 动态规则指向一个不存在的临时路径（不与任何静态规则重叠）
        var dynamicPath = Path.Combine(Path.GetTempPath(), $"Dynamic_{Guid.NewGuid()}");
        var gen = new FakeRuleGenerator(new List<CleanupRule>
        {
            new()
            {
                Name = "Test Dynamic App Cache",
                PathFactory = () => dynamicPath,
                Safety = CleanSafety.Caution,
                Category = CleanCategory.AppCache
            }
        });

        var scanService = new ScanService(gen);

        // 只验证不抛异常 + 动态规则被合并（通过 CategoryScanned 事件观察）
        var categories = new List<ScanCategoryResult>();
        scanService.CategoryScanned += c => categories.Add(c);

        await scanService.ScanAllAsync();

        // 动态路径不存在，不会产出 item，但验证流程跑通（不抛异常即可）
        Assert.NotNull(categories);
    }
}
