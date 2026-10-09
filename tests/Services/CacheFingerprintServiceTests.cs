using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class CacheFingerprintServiceTests : IDisposable
{
    private readonly CacheFingerprintService _service = new();
    private readonly string _testRoot;

    public CacheFingerprintServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"CleanMasterFingerprint_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true); } catch { }
    }

    [Fact]
    public void Discover_ElectronApp_ReturnsCacheDirs()
    {
        // 构造一个 Electron 应用指纹
        var appDir = Path.Combine(_testRoot, "MyElectronApp");
        Directory.CreateDirectory(Path.Combine(appDir, "Cache"));
        Directory.CreateDirectory(Path.Combine(appDir, "GPUCache"));
        Directory.CreateDirectory(Path.Combine(appDir, "Code Cache"));
        File.WriteAllText(Path.Combine(appDir, "Local State"), "{}");
        File.WriteAllText(Path.Combine(appDir, "Preferences"), "{}");

        var results = _service.Discover(new[] { _testRoot });

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Path.EndsWith(@"\Cache"));
        Assert.Contains(results, r => r.Path.EndsWith(@"\GPUCache"));
        Assert.All(results, r => Assert.True(r.IsCaution, "启发式发现应标 Caution"));
    }

    [Fact]
    public void Discover_NonElectronApp_NoLocalState_ReturnsNothing()
    {
        var appDir = Path.Combine(_testRoot, "PlainApp");
        Directory.CreateDirectory(Path.Combine(appDir, "Cache"));
        // 无 Local State / Preferences，不构成 Electron 指纹

        var results = _service.Discover(new[] { _testRoot });

        Assert.Empty(results);
    }

    [Fact]
    public void Discover_WebView2_Excluded()
    {
        var appDir = Path.Combine(_testRoot, "WebView2App");
        Directory.CreateDirectory(Path.Combine(appDir, "GPUCache"));
        Directory.CreateDirectory(Path.Combine(appDir, "EBWebView"));
        File.WriteAllText(Path.Combine(appDir, "Local State"), "{}");
        File.WriteAllText(Path.Combine(appDir, "Preferences"), "{}");

        var results = _service.Discover(new[] { _testRoot });

        Assert.Empty(results); // WebView2 应被排除
    }

    [Fact]
    public void Discover_KnownApp_Excluded()
    {
        var appDir = Path.Combine(_testRoot, "Cursor");
        Directory.CreateDirectory(Path.Combine(appDir, "Cache"));
        File.WriteAllText(Path.Combine(appDir, "Local State"), "{}");
        File.WriteAllText(Path.Combine(appDir, "Preferences"), "{}");

        var results = _service.Discover(new[] { _testRoot });

        Assert.DoesNotContain(results, r => r.AppName == "Cursor");
    }

    [Fact]
    public void Discover_NoDuplicatePaths()
    {
        var appDir = Path.Combine(_testRoot, "DupApp");
        Directory.CreateDirectory(Path.Combine(appDir, "Cache"));
        File.WriteAllText(Path.Combine(appDir, "Local State"), "{}");
        File.WriteAllText(Path.Combine(appDir, "Preferences"), "{}");

        var results = _service.Discover(new[] { _testRoot });
        var paths = results.Select(r => r.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
