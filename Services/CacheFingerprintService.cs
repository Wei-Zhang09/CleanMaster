using System.IO;

namespace CleanMaster.Services;

/// <summary>启发式发现的缓存目录。</summary>
public class CacheDiscovery
{
    public string AppName { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>启发式发现一律标 Caution（保守策略：自动识别，请确认）。</summary>
    public bool IsCaution => true;
}

/// <summary>
/// 启发式缓存指纹发现（路线 C 的兜底）。
///
/// 不依赖应用名硬编码，直接扫描 %APPDATA% / %LOCALAPPDATA%，按目录特征识别缓存：
///   - Chromium/Electron 内核指纹：存在 "Local State" + "Preferences" + GPUCache/Code Cache
///   - 常见缓存子目录名：Cache / GPUCache / Code Cache / logs / crashpad / Temp
///
/// 发现的项一律标 Caution、默认不勾选（保守策略），避免误删用户数据。
/// </summary>
public class CacheFingerprintService
{
    /// <summary>已知已显式覆盖的应用（避免与映射表/现有规则重复计算）。</summary>
    private static readonly HashSet<string> KnownAppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "discord", "Slack", "Notion", "Postman", "Cursor", "obsidian",
        "Typora", "draw.io", "Apifox", "Codex", "Clash Verge",
        "Tencent", "Microsoft", "Google", "BraveSoftware", "Vivaldi",
        "Opera Software", "Mozilla", "JetBrains", "DingTalk", "Feishu",
    };

    /// <summary>扫描并返回启发式发现的缓存目录。可传入自定义根目录（测试用）。</summary>
    public List<CacheDiscovery> Discover(IReadOnlyList<string>? roots = null)
    {
        var results = new List<CacheDiscovery>();
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var scanRoots = roots ?? new[] { roaming, local };

        foreach (var root in scanRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var vendorDir in SafeGetDirectories(root))
            {
                // 支持一级嵌套（Roaming\SomeVendor\SomeApp）
                foreach (var appDir in EnumerateAppCandidates(vendorDir))
                {
                    var appName = Path.GetFileName(appDir);
                    if (IsKnown(appName)) continue;

                    // Electron 指纹
                    if (IsElectronApp(appDir))
                    {
                        foreach (var sub in new[] { "Cache", "GPUCache", "Code Cache", "logs" })
                        {
                            var p = Path.Combine(appDir, sub);
                            if (Directory.Exists(p))
                                results.Add(new CacheDiscovery { AppName = appName, Path = p });
                        }
                        continue;
                    }

                    // 通用缓存子目录（非 Electron 应用也可能有）
                    foreach (var sub in new[] { "GPUCache", "Code Cache" })
                    {
                        var p = Path.Combine(appDir, sub);
                        if (Directory.Exists(p))
                            results.Add(new CacheDiscovery { AppName = appName, Path = p });
                    }
                }
            }
        }

        return results
            .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(r => r.AppName)
            .ToList();
    }

    /// <summary>Chromium/Electron 内核指纹判断。</summary>
    private static bool IsElectronApp(string appDir)
    {
        try
        {
            if (!File.Exists(Path.Combine(appDir, "Local State"))) return false;
            if (!File.Exists(Path.Combine(appDir, "Preferences"))) return false;

            var hasGpuCache = Directory.Exists(Path.Combine(appDir, "GPUCache"));
            var hasCodeCache = Directory.Exists(Path.Combine(appDir, "Code Cache"));
            if (!hasGpuCache && !hasCodeCache) return false;

            // 排除 WebView2（有 EBWebView 子目录）
            if (Directory.Exists(Path.Combine(appDir, "EBWebView"))) return false;

            return true;
        }
        catch { return false; }
    }

    private static bool IsKnown(string appName)
    {
        foreach (var k in KnownAppNames)
        {
            if (appName.StartsWith(k, StringComparison.OrdinalIgnoreCase)
                || appName.Equals(k, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> EnumerateAppCandidates(string dir)
    {
        bool isElectronRoot;
        try
        {
            isElectronRoot =
                File.Exists(Path.Combine(dir, "Local State")) &&
                File.Exists(Path.Combine(dir, "Preferences")) &&
                (Directory.Exists(Path.Combine(dir, "GPUCache")) ||
                 Directory.Exists(Path.Combine(dir, "Code Cache"))) &&
                !Directory.Exists(Path.Combine(dir, "EBWebView"));
        }
        catch { isElectronRoot = false; }

        if (isElectronRoot)
        {
            yield return dir;
            yield break;
        }

        foreach (var sub in SafeGetDirectories(dir))
            yield return sub;
    }

    private static string[] SafeGetDirectories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }
}
