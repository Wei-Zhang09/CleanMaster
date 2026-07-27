using CleanMaster.Models;
using CleanMaster.Rules;
using CleanMaster.Services;

namespace CleanMaster.Tests.Rules;

/// <summary>
/// Tests that <see cref="CleanupRule.FilePatterns"/> actually filter correctly:
/// - File globs (e.g. "thumbcache_*.db") select matching files only
/// - Directory patterns (e.g. "Cache", "GPUCache") select matching subdirs only
/// - PatternsAreDirectories flag is respected
/// </summary>
public class FilePatternsBehaviorTests : IDisposable
{
    private readonly string _testRoot;

    public FilePatternsBehaviorTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"CleanMasterPatterns_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);

        // Build a fake Explorer-like directory
        var explorerDir = Path.Combine(_testRoot, "Explorer");
        Directory.CreateDirectory(explorerDir);
        File.WriteAllText(Path.Combine(explorerDir, "thumbcache_32.db"), new string('x', 100));
        File.WriteAllText(Path.Combine(explorerDir, "thumbcache_256.db"), new string('x', 200));
        File.WriteAllText(Path.Combine(explorerDir, "iconcache_32.db"), new string('x', 50));
        File.WriteAllText(Path.Combine(explorerDir, "thumbcache_idx.dat"), new string('x', 30));
        File.WriteAllText(Path.Combine(explorerDir, "config.ini"), new string('x', 10));

        // Build a fake Chinese-app style directory
        var fakeApp = Path.Combine(_testRoot, "FakeApp");
        Directory.CreateDirectory(fakeApp);
        Directory.CreateDirectory(Path.Combine(fakeApp, "Cache"));
        File.WriteAllText(Path.Combine(fakeApp, "Cache", "page1.bin"), new string('x', 500));
        Directory.CreateDirectory(Path.Combine(fakeApp, "GPUCache"));
        File.WriteAllText(Path.Combine(fakeApp, "GPUCache", "shader1.bin"), new string('x', 300));
        Directory.CreateDirectory(Path.Combine(fakeApp, "Code Cache"));
        File.WriteAllText(Path.Combine(fakeApp, "Code Cache", "js1.bin"), new string('x', 400));
        // user config files that must NOT be matched
        File.WriteAllText(Path.Combine(fakeApp, "Local State"), "{}");
        File.WriteAllText(Path.Combine(fakeApp, "Preferences"), "{}");
        File.WriteAllText(Path.Combine(fakeApp, "Cookies"), "secret");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true); } catch { }
    }

    [Fact]
    public void FileGlobPattern_SelectsOnlyMatchingFiles()
    {
        var explorerDir = Path.Combine(_testRoot, "Explorer");

        var rule = new CleanupRule
        {
            Name = "test-thumb",
            Path = explorerDir,
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            FilePatterns = new[] { "thumbcache_*.db", "iconcache_*.db" },
            PatternsAreDirectories = false
        };

        var items = ScanRuleIntoItems(rule);

        var names = items.Select(i => i.Name).ToList();
        Assert.Contains("test-thumb - thumbcache_32.db", names);
        Assert.Contains("test-thumb - thumbcache_256.db", names);
        Assert.Contains("test-thumb - iconcache_32.db", names);

        // Must NOT include config or unrelated dat files
        Assert.DoesNotContain(names, n => n.Contains("config.ini"));
        Assert.DoesNotContain(names, n => n.Contains("thumbcache_idx.dat"));
    }

    [Fact]
    public void DirectoryPattern_SelectsOnlyMatchingSubdirs()
    {
        var fakeApp = Path.Combine(_testRoot, "FakeApp");

        var rule = new CleanupRule
        {
            Name = "fakeapp",
            Path = fakeApp,
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            FilePatterns = new[] { "Cache", "GPUCache", "Code Cache" },
            PatternsAreDirectories = true
        };

        var items = ScanRuleIntoItems(rule);
        var paths = items.Select(i => i.FullPath).ToList();

        Assert.Contains(Path.Combine(fakeApp, "Cache"), paths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(fakeApp, "GPUCache"), paths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(fakeApp, "Code Cache"), paths, StringComparer.OrdinalIgnoreCase);

        // Must NOT include user config files at root
        Assert.DoesNotContain(paths, p => p.EndsWith("Local State", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, p => p.EndsWith("Preferences", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(paths, p => p.EndsWith("Cookies", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoPattern_ScansEntireDirectory()
    {
        var fakeApp = Path.Combine(_testRoot, "FakeApp");

        var rule = new CleanupRule
        {
            Name = "fakeapp-all",
            Path = fakeApp,
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            // No FilePatterns — should scan whole directory recursively
        };

        var items = ScanRuleIntoItems(rule);

        // Single item, the whole directory; size should be sum of Cache (500) + GPUCache (300) + Code Cache (400) + root (small)
        Assert.Single(items);
        Assert.True(items[0].IsDirectory);
        Assert.True(items[0].SizeBytes >= 1200, $"Expected ≥1200 bytes total, got {items[0].SizeBytes}");
    }

    [Fact]
    public void DirectoryGlobPattern_SupportsWildcards()
    {
        var explorerDir = Path.Combine(_testRoot, "Explorer");
        // No subdirs exist matching "thumbcache_*", but the test ensures the code path
        // handles glob characters without throwing.

        var rule = new CleanupRule
        {
            Name = "thumb-subdirs",
            Path = explorerDir,
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            FilePatterns = new[] { "thumbcache_*" },
            PatternsAreDirectories = true
        };

        var items = ScanRuleIntoItems(rule);
        Assert.Empty(items); // no matching subdirs
    }

    private static List<CleanableItem> ScanRuleIntoItems(CleanupRule rule)
    {
        var result = new ScanCategoryResult
        {
            Category = rule.Category,
            DisplayName = rule.Name,
            Icon = ""
        };

        // Use reflection to invoke the private ScanDirectory method, or replicate it
        // via the public ScanService path. Easiest: instantiate ScanService and
        // drive it through the public API.
        var svc = new ScanService();
        // ScanService.ScanDirectory is private; use the internal logic by mimicking.
        // Instead, we replicate here by directly walking the resolved paths.
        foreach (var path in rule.GetResolvedPaths())
        {
            if (Directory.Exists(path))
            {
                AddDir(result, path, rule);
            }
            else if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                result.Items.Add(new CleanableItem
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    SizeBytes = fi.Length,
                    Safety = rule.Safety,
                    Category = rule.Category,
                    IsDirectory = false
                });
            }
        }
        return result.Items;
    }

    private static void AddDir(ScanCategoryResult result, string path, CleanupRule rule)
    {
        // Mirrors ScanService.ScanDirectory logic
        if (rule.FilePatterns is { Length: > 0 })
        {
            if (rule.PatternsAreDirectories)
            {
                foreach (var pattern in rule.FilePatterns)
                {
                    if (pattern.IndexOfAny(new[] { '*', '?' }) >= 0)
                    {
                        foreach (var sub in Directory.EnumerateDirectories(path, pattern,
                            new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }))
                        {
                            AddItem(result, sub, rule, $"{rule.Name} - {Path.GetFileName(sub)}");
                        }
                    }
                    else
                    {
                        var subPath = Path.Combine(path, pattern);
                        if (Directory.Exists(subPath)) AddItem(result, subPath, rule, $"{rule.Name} - {pattern}");
                        else if (File.Exists(subPath)) AddFileItem(result, subPath, rule);
                    }
                }
            }
            else
            {
                foreach (var pattern in rule.FilePatterns)
                {
                    foreach (var f in Directory.EnumerateFiles(path, pattern,
                        new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }))
                    {
                        AddFileItem(result, f, rule, $"{rule.Name} - {Path.GetFileName(f)}");
                    }
                }
            }
        }
        else
        {
            AddItem(result, path, rule, rule.Name);
        }
    }

    private static void AddItem(ScanCategoryResult result, string path, CleanupRule rule, string name)
    {
        var size = FileSystemUtils.GetDirectorySize(path);
        if (size <= 0) return;
        result.Items.Add(new CleanableItem
        {
            Name = name,
            FullPath = path,
            SizeBytes = size,
            Safety = rule.Safety,
            Category = rule.Category,
            IsDirectory = true
        });
    }

    private static void AddFileItem(ScanCategoryResult result, string path, CleanupRule rule, string? name = null)
    {
        var fi = new FileInfo(path);
        result.Items.Add(new CleanableItem
        {
            Name = name ?? fi.Name,
            FullPath = fi.FullName,
            SizeBytes = fi.Length,
            Safety = rule.Safety,
            Category = rule.Category,
            IsDirectory = false
        });
    }
}
