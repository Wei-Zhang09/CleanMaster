using System.IO;
using CleanMaster.Models;
using CleanMaster.Rules;

namespace CleanMaster.Tests.Rules;

/// <summary>
/// Validates rule paths and safety classifications for the C-drive cleanup tool.
/// Goal: every rule must point at a path that makes sense on a real Windows system,
/// and safety levels must reflect actual user risk.
/// </summary>
public class RuleDatabasePathAndSafetyTests
{
    [Fact]
    public void GetAllRules_NoRulePointsAtWholeUserProfileRoot()
    {
        // Several past bugs had rules pointing at ~/.something or %APPDATA%\<App> (whole dir),
        // which would wipe user config along with cache. These rules must use subdirectories.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var riskyRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Never allow pointing at a whole vendor root that contains user configs
            Path.Combine(roaming, "Tencent"),   // covers WeChat/QQ config roots
            Path.Combine(local, "Tencent"),
            Path.Combine(roaming, "kingsoft"),  // WPS - contains settings + templates
            Path.Combine(roaming, "Notion"),    // contains auth + workspace db
            Path.Combine(roaming, "discord"),
            Path.Combine(roaming, "Slack"),
            Path.Combine(roaming, "Microsoft\\Edge\\User Data"),  // contains Login Data, Bookmarks
            Path.Combine(roaming, "Google\\Chrome\\User Data"),
        };

        var rules = RuleDatabase.GetAllRules();
        foreach (var rule in rules)
        {
            foreach (var p in rule.GetResolvedPaths())
            {
                if (string.IsNullOrEmpty(p)) continue;
                var full = Path.GetFullPath(p.TrimEnd('\\', '/'));
                foreach (var root in riskyRoots)
                {
                    Assert.False(
                        string.Equals(full, root, StringComparison.OrdinalIgnoreCase),
                        $"Rule '{rule.Name}' points at whole-vendor root: {p}. Use Cache/GPUCache subdirectory instead.");
                }
            }
        }
    }

    [Fact]
    public void GetAllRules_DangerousRules_AreMarkedDangerous()
    {
        var rules = RuleDatabase.GetAllRules();

        // AppCompat Programs contains .sdb databases — deleting breaks compatibility settings.
        var appCompat = rules.FirstOrDefault(r => r.Name.Contains("AppCompat") || r.Name.Contains("程序兼容性"));
        Assert.NotNull(appCompat);
        Assert.Equal(CleanSafety.Dangerous, appCompat!.Safety);
    }

    [Fact]
    public void GetAllRules_NuGetAndMaven_AreCautionNotSafe()
    {
        var rules = RuleDatabase.GetAllRules();

        var nuget = rules.FirstOrDefault(r => r.Name.Contains("NuGet Packages"));
        Assert.NotNull(nuget);
        Assert.Equal(CleanSafety.Caution, nuget!.Safety);

        var maven = rules.FirstOrDefault(r => r.Name.Contains("Maven Repository"));
        Assert.NotNull(maven);
        Assert.Equal(CleanSafety.Caution, maven!.Safety);
    }

    [Fact]
    public void GetAllRules_SystemPackageCache_IsCaution()
    {
        // Was previously marked Safe; should be Caution because uninstallers/repairs need it.
        var rules = RuleDatabase.GetAllRules();
        var spc = rules.FirstOrDefault(r => r.Name == "System Package Cache");
        Assert.NotNull(spc);
        Assert.Equal(CleanSafety.Caution, spc!.Safety);
    }

    [Fact]
    public void GetAllRules_BrowserRules_SupportMultipleProfiles()
    {
        var rules = RuleDatabase.GetAllRules();
        var chrome = rules.First(r => r.Name.StartsWith("Chrome Cache", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(chrome.PathFactoryMulti);

        var edge = rules.First(r => r.Name.StartsWith("Edge Cache", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(edge.PathFactoryMulti);
    }

    [Fact]
    public void GetAllRules_FirefoxPointsAtCache2NotProfilesRoot()
    {
        var rules = RuleDatabase.GetAllRules();
        var firefox = rules.First(r => r.Name.StartsWith("Firefox", StringComparison.OrdinalIgnoreCase));

        // Rule must not point at the whole Profiles root (contains places.sqlite, cookies, etc.)
        foreach (var p in firefox.GetResolvedPaths())
        {
            Assert.False(
                p.EndsWith(@"Mozilla\Firefox\Profiles", StringComparison.OrdinalIgnoreCase),
                $"Firefox rule must point at cache2 subdirectory, not the Profiles root. Got: {p}");
        }
    }

    [Fact]
    public void GetAllRules_NoDeletedRulesArePresent()
    {
        var rules = RuleDatabase.GetAllRules();
        var names = rules.Select(r => r.Name).ToList();

        // "操作中心日志" pointed at wpestate.json which is unrelated — must be removed.
        Assert.DoesNotContain(names, n => n.Contains("操作中心", StringComparison.Ordinal));
        // "Windows搜索缓存 (ConnectedSearch)" was Win8/8.1 only — removed in favor of Search\Data.
        Assert.DoesNotContain(names, n => n == "Windows搜索缓存");
        // Old single-profile "Chrome Cache" / "Edge Cache" replaced by multi-profile versions.
        Assert.DoesNotContain(names, n => n == "Chrome Cache");
        Assert.DoesNotContain(names, n => n == "Edge Cache");
    }

    [Fact]
    public void GetAllRules_ThumbnailCacheRule_UsesFilePatternsForDbFiles()
    {
        var rules = RuleDatabase.GetAllRules();
        var thumb = rules.First(r => r.Name == "缩略图缓存");

        Assert.NotNull(thumb.FilePatterns);
        Assert.Contains("thumbcache_*.db", thumb.FilePatterns!);
        Assert.False(thumb.PatternsAreDirectories);
    }

    [Fact]
    public void GetAllRules_WerRules_SplitIntoArchiveAndQueue()
    {
        var rules = RuleDatabase.GetAllRules();
        Assert.Contains(rules, r => r.Name == "WER ReportArchive");
        Assert.Contains(rules, r => r.Name == "WER ReportQueue");
    }

    [Fact]
    public void GetAllRules_NoOverlappingWindowsLogsRules()
    {
        // The old "Windows Logs" rule scanned the whole C:\Windows\Logs directory, which
        // already contains CBS, DISM, Compressed etc. Now it must use FilePatterns for
        // top-level *.log only to avoid double-counting.
        var rules = RuleDatabase.GetAllRules();
        var winLogs = rules.First(r => r.Name.StartsWith("Windows Logs", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(winLogs.FilePatterns);
        Assert.True(winLogs.FilePatterns!.Contains("*.log"));
        Assert.False(winLogs.PatternsAreDirectories);
    }

    [Fact]
    public void GetAllRules_NewSystemRules_Present()
    {
        var rules = RuleDatabase.GetAllRules();
        var names = rules.Select(r => r.Name).ToList();

        Assert.Contains("INetCache (IE/Edge legacy)", names);
        Assert.Contains("WebCache", names);
        Assert.Contains("Windows LogFiles", names);
        Assert.Contains("Windows Debug", names);
        Assert.Contains("Security Logs", names);
        Assert.Contains("LiveKernelReports", names);
        Assert.Contains("LocalService Temp", names);
        Assert.Contains("NetworkService Temp", names);
    }

    [Fact]
    public void GetAllRules_NewDevToolRules_Present()
    {
        var names = RuleDatabase.GetAllRules().Select(r => r.Name).ToList();

        Assert.Contains("Cargo Cache (Rust)", names);
        Assert.Contains("Conda pkgs", names);
        Assert.Contains("Yarn Cache", names);
        Assert.Contains("pnpm store", names);
        Assert.Contains("Bun install cache", names);
        Assert.Contains("Go mod cache", names);
        Assert.Contains("Android build-cache", names);
        Assert.Contains("VS Code logs", names);
        Assert.Contains("VS Code workspaceStorage", names);
        Assert.Contains("VS Code GPUCache", names);
        Assert.Contains("JetBrains Caches", names);
    }

    [Fact]
    public void GetAllRules_AiToolRules_Present()
    {
        var names = RuleDatabase.GetAllRules().Select(r => r.Name).ToList();

        Assert.Contains("Huggingface Model Cache", names);
        Assert.Contains("PyTorch Model Cache", names);
        Assert.Contains("Ollama Models", names);
    }

    [Fact]
    public void GetAllRules_ElectronGenericRule_Present()
    {
        var rules = RuleDatabase.GetAllRules();
        var electron = rules.FirstOrDefault(r => r.Name.Contains("Electron App Cache", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(electron);
        Assert.NotNull(electron!.PathFactoryMulti);
    }

    [Fact]
    public void GetAllRules_ChineseAppsUseSubdirectories()
    {
        // Critical: Chinese apps must NOT clean whole-vendor directories.
        // Each rule should resolve to Cache/GPUCache/Code Cache/logs subdirs only.
        var rules = RuleDatabase.GetAllRules();
        var chineseAppNames = new[]
        {
            "iQiyi", "Youku", "Bilibili", "NetEase Music", "QQ Music",
            "Kuwo", "Kugou", "Douyin", "AliyunPan", "Baidu Netdisk"
        };

        foreach (var keyword in chineseAppNames)
        {
            var rule = rules.FirstOrDefault(r => r.Name.Contains(keyword));
            Assert.NotNull(rule);

            foreach (var p in rule!.GetResolvedPaths())
            {
                if (string.IsNullOrEmpty(p)) continue;
                // Must end with one of the safe subdirectory names
                var endsWithSafe = p.EndsWith("\\Cache", StringComparison.OrdinalIgnoreCase)
                                || p.EndsWith("\\GPUCache", StringComparison.OrdinalIgnoreCase)
                                || p.EndsWith("\\Code Cache", StringComparison.OrdinalIgnoreCase)
                                || p.EndsWith("\\logs", StringComparison.OrdinalIgnoreCase)
                                || p.EndsWith("\\WebStorage", StringComparison.OrdinalIgnoreCase);
                Assert.True(endsWithSafe,
                    $"Chinese app rule '{rule.Name}' resolved to {p}, which is not a Cache/GPUCache/logs subdirectory.");
            }
        }
    }

    [Fact]
    public void GetAllRules_EachRuleHasAtLeastOnePathSource()
    {
        var rules = RuleDatabase.GetAllRules();
        foreach (var r in rules)
        {
            var hasSource = !string.IsNullOrEmpty(r.Path)
                          || r.PathFactory != null
                          || r.PathFactoryMulti != null;
            Assert.True(hasSource, $"Rule '{r.Name}' has no path source.");
        }
    }

    [Fact]
    public void GetAllRules_NewRuleCountIsHigherThanBaseline()
    {
        // Baseline was 84 rules. After cleanup + additions, expect at least 90.
        var count = RuleDatabase.GetAllRules().Count;
        Assert.True(count >= 90, $"Expected at least 90 rules after expansion, got {count}");
    }
}
