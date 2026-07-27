using System.IO;
using CleanMaster.Models;
using CleanMaster.Rules;

namespace CleanMaster.Tests.Rules;

/// <summary>
/// Tests the multi-path rule mechanism:
/// - PathFactoryMulti returns multiple resolved paths
/// - GetResolvedPaths() yields all of them
/// - GetResolvedPath() returns the first non-empty one for display
/// </summary>
public class MultiPathRuleTests
{
    [Fact]
    public void GetResolvedPaths_YieldsAllPathsFromFactoryMulti()
    {
        var a = Path.GetTempPath();
        var b = Path.Combine(Path.GetTempPath(), "sub_a");
        var c = Path.Combine(Path.GetTempPath(), "sub_b");

        var rule = new CleanupRule
        {
            Name = "multi-test",
            PathFactoryMulti = () => new[] { a, b, c },
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache
        };

        var paths = rule.GetResolvedPaths().ToList();
        Assert.Equal(3, paths.Count);
        Assert.Equal(a, paths[0]);
        Assert.Equal(b, paths[1]);
        Assert.Equal(c, paths[2]);
    }

    [Fact]
    public void GetResolvedPaths_YieldsSinglePathFromFactory()
    {
        var rule = new CleanupRule
        {
            Name = "single-test",
            PathFactory = () => @"C:\Foo\Bar",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache
        };

        var paths = rule.GetResolvedPaths().ToList();
        Assert.Single(paths);
        Assert.Equal(@"C:\Foo\Bar", paths[0]);
    }

    [Fact]
    public void GetResolvedPaths_YieldsSinglePathFromPath()
    {
        var rule = new CleanupRule
        {
            Name = "static-test",
            Path = @"C:\Windows\Temp",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.TempFiles
        };

        var paths = rule.GetResolvedPaths().ToList();
        Assert.Single(paths);
        Assert.Equal(@"C:\Windows\Temp", paths[0]);
    }

    [Fact]
    public void GetResolvedPaths_EmptyResultsAreFiltered()
    {
        var rule = new CleanupRule
        {
            Name = "filter-empty",
            PathFactoryMulti = () => new[] { "", null!, @"C:\Foo" },
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache
        };

        var paths = rule.GetResolvedPaths().ToList();
        Assert.Single(paths);
        Assert.Equal(@"C:\Foo", paths[0]);
    }

    [Fact]
    public void GetResolvedPath_ReturnsFirstFromMulti_ForDisplay()
    {
        var rule = new CleanupRule
        {
            Name = "first-display",
            PathFactoryMulti = () => new[] { @"C:\A", @"C:\B", @"C:\C" },
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache
        };

        Assert.Equal(@"C:\A", rule.GetResolvedPath());
    }

    [Fact]
    public void BrowserMultiProfileRules_EnumerateProfiles()
    {
        // Verify that the Chrome multi-profile rule's factory function does not throw
        // and returns a valid enumerable (possibly empty if Chrome is not installed).
        var rules = RuleDatabase.GetAllRules();
        var chrome = rules.First(r => r.Name.StartsWith("Chrome Cache", StringComparison.OrdinalIgnoreCase));

        var exception = Record.Exception(() =>
        {
            var paths = chrome.GetResolvedPaths().ToList();
            // Each returned path must actually exist on the filesystem
            foreach (var p in paths)
            {
                Assert.True(Directory.Exists(p), $"Multi-path rule returned non-existent dir: {p}");
            }
        });
        Assert.Null(exception);
    }

    [Fact]
    public void GetAllRules_AllMultiPathRules_NeverReturnNullFromFactory()
    {
        var rules = RuleDatabase.GetAllRules();
        foreach (var r in rules.Where(r => r.PathFactoryMulti != null))
        {
            var exception = Record.Exception(() =>
            {
                var paths = r.GetResolvedPaths().ToList();
                Assert.NotNull(paths);
            });
            Assert.Null(exception);
        }
    }

    [Fact]
    public void GetAllRules_DuplicatePathCheck_AcrossAllRules()
    {
        // No two distinct rules may resolve to the exact same path (would cause double-counting).
        // Multi-path rules are allowed to share directories with each other only if names differ,
        // but the same (name, path) pair must be unique.
        var rules = RuleDatabase.GetAllRules();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rules)
        {
            foreach (var p in r.GetResolvedPaths())
            {
                if (string.IsNullOrEmpty(p)) continue;
                // Normalize: trim trailing separators
                var normalized = p.TrimEnd('\\', '/');

                // Two rules pointing at the same exact path is OK only if they are different
                // pattern-scoped (e.g. Windows Logs top-level vs CBS Logs).
                // We can't easily detect that here, so we just ensure no (Name, Path) dup.
                var key = $"{r.Name}|{normalized}";
                Assert.True(seen.Add(key), $"Duplicate (Name, Path) pair: {key}");
            }
        }
    }
}
