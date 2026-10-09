using CleanMaster.Models;
using CleanMaster.Services;

namespace CleanMaster.Rules;

/// <summary>
/// 动态规则生成器（路线 C 整合层）。
///
/// 把三层数据源串成扫描规则：
///   1. 注册表已装软件（InstalledAppService）
///   2. 缓存路径映射表（AppCacheMap）——命中则按 InstallLocation + 环境变量生成精确规则
///   3. 启发式指纹发现（CacheFingerprintService）——未命中的兜底，标 Caution
///
/// 与 RuleDatabase 的硬编码规则并存（去重），待验证稳定后 RC-6 再删硬编码。
/// </summary>
public class DynamicRuleGenerator : IDynamicRuleGenerator
{
    private readonly InstalledAppService _installedAppService;
    private readonly CacheFingerprintService _fingerprintService;

    public DynamicRuleGenerator(InstalledAppService installedAppService, CacheFingerprintService fingerprintService)
    {
        _installedAppService = installedAppService;
        _fingerprintService = fingerprintService;
    }

    /// <summary>生成基于已装软件的动态缓存规则。</summary>
    public List<CleanupRule> Generate()
    {
        var rules = new List<CleanupRule>();

        // 1) 注册表已装软件 → 映射表命中 → 生成规则
        var apps = _installedAppService.EnumerateApps();
        var matchedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in apps)
        {
            var entry = AppCacheMap.Entries.FirstOrDefault(e =>
                app.DisplayName.Contains(e.DisplayNameKeyword, StringComparison.OrdinalIgnoreCase));
            if (entry == null) continue;

            matchedNames.Add(app.DisplayName);

            foreach (var pattern in entry.Patterns)
            {
                var resolvedPath = AppCacheMap.ResolvePath(pattern.Template, app.InstallLocation);
                if (string.IsNullOrEmpty(resolvedPath)) continue;

                rules.Add(new CleanupRule
                {
                    Name = $"{app.DisplayName} Cache",
                    PathFactory = () => resolvedPath,
                    Safety = entry.Caution ? CleanSafety.Caution : CleanSafety.Safe,
                    Category = CleanCategory.AppCache,
                    Description = $"「{app.DisplayName}」的缓存目录。",
                    SoftwareName = app.DisplayName,
                    FileType = "缓存"
                });
            }
        }

        // 2) 启发式指纹兜底（未命中映射表的应用）
        var discoveries = _fingerprintService.Discover();
        foreach (var d in discoveries)
        {
            // 跳过已被映射表覆盖的应用
            var alreadyCovered = matchedNames.Any(n => d.AppName.Contains(n, StringComparison.OrdinalIgnoreCase)
                                                    || n.Contains(d.AppName, StringComparison.OrdinalIgnoreCase));
            if (alreadyCovered) continue;

            rules.Add(new CleanupRule
            {
                Name = $"{d.AppName} Cache (自动发现)",
                PathFactory = () => d.Path,
                Safety = CleanSafety.Caution, // 保守：自动识别标 Caution
                Category = CleanCategory.AppCache,
                Description = $"自动发现的「{d.AppName}」缓存目录，请确认后再清理。",
                SoftwareName = d.AppName,
                FileType = "缓存"
            });
        }

        return rules;
    }
}
