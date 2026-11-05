using System.IO;
using CleanMaster.Models;
using CleanMaster.Services;

namespace CleanMaster.Rules;

public class CleanupRule
{
    /// <summary>稳定标识（数据驱动规则来自 TOML 的 id；硬编码规则留空）。</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// 数据驱动规则的路径模板（含 {LOCAL} 等占位符）；硬编码规则为空。
    /// 与 <see cref="Path"/> 分开存放：模板是"定义"，Path 是"已解析的静态路径"。
    /// </summary>
    public string PathTemplate { get; set; } = "";

    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public CleanSafety Safety { get; set; }
    public CleanCategory Category { get; set; }
    public string Description { get; set; } = "";

    /// <summary>
    /// 软件名（如 "微信"、"Chrome"）。显式填写优先；为空时 ScanService 回退到按规则名推断。
    /// </summary>
    public string? SoftwareName { get; set; }

    /// <summary>
    /// 文件类型（如 "缓存"、"日志"、"临时文件"）。显式填写优先；为空时回退到推断。
    /// </summary>
    public string? FileType { get; set; }

    /// <summary>
    /// Returns a single resolved path. Mutually exclusive with <see cref="PathFactoryMulti"/>.
    /// </summary>
    public Func<string>? PathFactory { get; set; }

    /// <summary>
    /// Returns multiple resolved paths (e.g. all browser profiles, all Electron app caches).
    /// When set, takes precedence over <see cref="PathFactory"/> and <see cref="Path"/>.
    /// </summary>
    public Func<IEnumerable<string>>? PathFactoryMulti { get; set; }

    /// <summary>
    /// When set, only files/subdirectories matching these glob patterns are scanned/cleaned
    /// within the resolved path. Patterns are interpreted by <see cref="System.IO.Directory.EnumerateFiles"/>
    /// (e.g. "thumbcache_*.db", "Cache", "Code Cache").
    /// </summary>
    public string[]? FilePatterns { get; set; }

    /// <summary>
    /// When true, FilePatterns match directory names rather than file names.
    /// Default is false (file names).
    /// </summary>
    public bool PatternsAreDirectories { get; set; } = true;

    public int MaxDepth { get; set; } = -1;
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Returns the primary resolved path (first one if multi). Used for display and tests.
    /// </summary>
    public string GetResolvedPath()
    {
        if (PathFactoryMulti != null)
        {
            try { return PathFactoryMulti().FirstOrDefault() ?? ""; }
            catch { return ""; }
        }
        return PathFactory?.Invoke() ?? Path;
    }

    /// <summary>
    /// Returns all resolved paths for the rule. Filters out null/empty/non-existent paths
    /// so the scanner can iterate without re-checking.
    /// </summary>
    public IEnumerable<string> GetResolvedPaths()
    {
        if (PathFactoryMulti != null)
        {
            foreach (var p in PathFactoryMulti())
            {
                if (!string.IsNullOrEmpty(p)) yield return p;
            }
            yield break;
        }

        var single = PathFactory?.Invoke() ?? Path;
        if (!string.IsNullOrEmpty(single)) yield return single;
    }
}

public static class RuleDatabase
{
    private static readonly string SystemDrive = SystemPaths.SystemDrive;
    public static List<CleanupRule> GetAllRules()
    {
        var rules = new List<CleanupRule>();
        rules.AddRange(GetRecycleBinRules());

        // RC-6：已迁移到 Rules/Catalog/*.toml 的规则（数据驱动）在硬编码规则之后合并。
        // 规则文件编译期嵌入程序集，校验由 RuleCatalogTests 保证；这里只做防御性日志。
        var catalog = Rules.Data.RuleCatalogLoader.LoadEmbedded();
        if (catalog.Errors.Count > 0)
            CleanMaster.App.LogError("RuleCatalog", new InvalidOperationException(string.Join(" | ", catalog.Errors)));
        rules.AddRange(catalog.Rules.Select(Rules.Data.RuleCatalogLoader.ToCleanupRule));

        rules.AddRange(GetAppCacheRules());
        rules.AddRange(GetInstallerCacheRules());
        rules.AddRange(GetCrashDumpRules());
        rules.AddRange(GetWindowsCacheRules());
        rules.AddRange(GetSystemCacheRules());
        rules.AddRange(GetAiToolCacheRules());
        rules.AddRange(GetGameCacheRules());
        rules.AddRange(GetElectronCacheRules());
        return rules;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Recycle Bin
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetRecycleBinRules() =>
    [
        new()
        {
            Name = "Recycle Bin",
            Path = $@"{SystemDrive}$Recycle.Bin",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.RecycleBin,
            Description = "回收站中的文件。这些是用户主动删除的内容，清空后无法恢复。",
            SoftwareName = "",
            FileType = "回收站"
        }
    ];

    // ──────────────────────────────────────────────────────────────────
    //  App Cache (Chinese apps, Electron apps, etc.)
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetAppCacheRules()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        // Helper: returns all subdirectories matching the given name(s) under a parent.
        IEnumerable<string> FindSubdirs(string parent, params string[] names)
        {
            if (!Directory.Exists(parent)) yield break;
            foreach (var name in names)
            {
                var p = Path.Combine(parent, name);
                if (Directory.Exists(p)) yield return p;
            }
        }

        return
        [
            new()
            {
                Name = "WeChat 4.0 Radium Cache (xwechat)",
                PathFactory = () => Path.Combine(roaming, "Tencent", "xwechat", "radium"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.AppCache,
                Description = "微信 4.0 小程序/渲染缓存。删除后小程序需重新加载，不影响聊天记录。"
            },
            new()
            {
                Name = "WeChat Files Cache",
                PathFactoryMulti = () =>
                {
                    // Documents\WeChat Files\<用户id>\ 下有 FileStorage 等子目录,
                    // 这里只清明确的缓存: FileStorage\Temp、TempFromPhone、Cache 等子目录。
                    var root = Path.Combine(docs, @"WeChat Files");
                    if (!Directory.Exists(root)) return Enumerable.Empty<string>();
                    var results = new List<string>();
                    foreach (var userDir in Directory.GetDirectories(root))
                    {
                        foreach (var sub in new[] { @"FileStorage\Temp", @"FileStorage\TempFromPhone", @"FileStorage\Cache", @"FileStorage\CDNFileStorage\Temp" })
                        {
                            var p = Path.Combine(userDir, sub);
                            if (Directory.Exists(p)) results.Add(p);
                        }
                    }
                    return results;
                },
                Safety = CleanSafety.Caution,
                Category = CleanCategory.AppCache,
                Description = "微信文档目录中的临时文件缓存。已排除已接收的文件（FileStorage/File、Image、Video）。删除前请确认没有正在传输的文件。"
            },
            new()
            {
                Name = "WeCom (企业微信) Cache",
                PathFactoryMulti = () =>
                {
                    var wxwork = Path.Combine(roaming, @"Tencent\WXWork");
                    if (!Directory.Exists(wxwork)) return Enumerable.Empty<string>();
                    return FindSubdirs(wxwork, "Cache", "GPUCache", "logs", "tmp");
                },
                Safety = CleanSafety.Caution,
                Category = CleanCategory.AppCache,
                Description = "企业微信缓存。仅清理 GPUCache/Code Cache 等子目录，不会删除聊天记录数据库和已接收文件。"
            },
            new()
            {
                Name = "迅雷 cache",
                PathFactoryMulti = () => FindSubdirs(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        @"Thunder Network\Downloader"), "cache", "logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "迅雷下载器缓存与日志。下载中的任务不会受影响。"
            },
            new()
            {
                Name = "DingTalk (钉钉) Cache",
                PathFactoryMulti = () =>
                {
                    var root = Path.Combine(local, "DingTalk");
                    if (!Directory.Exists(root)) return Enumerable.Empty<string>();
                    var results = new List<string>();
                    foreach (var userDir in Directory.GetDirectories(root))
                    {
                        foreach (var sub in new[] { "logs", "tmp", "Cache", "GPUCache" })
                        {
                            var p = Path.Combine(userDir, sub);
                            if (Directory.Exists(p)) results.Add(p);
                        }
                    }
                    return results;
                },
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "钉钉各用户目录的缓存与日志。不会删除聊天记录数据库和已接收文件。"
            },
            new()
            {
                Name = "Feishu (飞书) Cache",
                PathFactoryMulti = () =>
                {
                    var root = Path.Combine(local, "Feishu");
                    if (!Directory.Exists(root)) return Enumerable.Empty<string>();
                    return FindSubdirs(root, "LarkShell\\cache", "LarkShell\\logs", "Cache", "GPUCache")
                        .SelectMany(p => Directory.Exists(p) ? new[] { p } : Array.Empty<string>());
                },
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "飞书客户端缓存。删除后下次打开会重新加载资源，不影响聊天记录和文件。"
            },
            new()
            {
                Name = "Slack Cache",
                PathFactoryMulti = () =>
                {
                    var root = Path.Combine(roaming, "Slack");
                    if (!Directory.Exists(root)) return Enumerable.Empty<string>();
                    var results = new List<string>();
                    // Slack stores per-version subdirectories
                    foreach (var ver in Directory.GetDirectories(root))
                    {
                        foreach (var sub in new[] { "Cache", "GPUCache", "Code Cache" })
                        {
                            var p = Path.Combine(ver, sub);
                            if (Directory.Exists(p)) results.Add(p);
                        }
                    }
                    return results;
                },
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Slack 桌面客户端缓存。删除后工作区数据会重新加载。"
            }
        ];
    }

    // ──────────────────────────────────────────────────────────────────
    //  Installer Cache
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetInstallerCacheRules() =>
    [

    ];

    // ──────────────────────────────────────────────────────────────────
    //  Crash Dumps
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetCrashDumpRules() =>
    [

    ];

    // ──────────────────────────────────────────────────────────────────
    //  Windows Cache (system-level caches not covered elsewhere)
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetWindowsCacheRules() =>
    [

    ];

    // ──────────────────────────────────────────────────────────────────
    //  System Cache (legacy remaining rules)
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetSystemCacheRules() =>
    [
        // "操作中心日志" removed: previously pointed at wpestate.json which is unrelated.
        // "Windows搜索缓存 (ConnectedSearch)" removed: Win8/8.1 only, Win10/11 uses Search\Data (above).
    ];

    // ──────────────────────────────────────────────────────────────────
    //  AI Tool Cache — model caches can be huge (GB+)
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetAiToolCacheRules()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return
        [

        ];
    }

    // ──────────────────────────────────────────────────────────────────
    //  Game Cache — shader caches grow into GBs
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetGameCacheRules()
    {
        var results = new List<CleanupRule>();

        // Steam: discover via registry (common) or fallback to default path
        var steamPath = TryGetSteamPath();
        if (!string.IsNullOrEmpty(steamPath))
        {
            results.Add(new CleanupRule
            {
                Name = "Steam ShaderCache",
                PathFactory = () => Path.Combine(steamPath, "steamapps", "shadercache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Steam 着色器缓存。删除后首次启动游戏会重新编译。"
            });
            results.Add(new CleanupRule
            {
                Name = "Steam httpcache",
                PathFactory = () => Path.Combine(steamPath, "appcache", "httpcache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Steam HTTP 缓存。"
            });
            results.Add(new CleanupRule
            {
                Name = "Steam logs",
                PathFactory = () => Path.Combine(steamPath, "logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.WindowsLogs,
                Description = "Steam 客户端日志。"
            });
        }

        // Epic Games Launcher
        var epicPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games");
        if (Directory.Exists(epicPath))
        {
            results.Add(new CleanupRule
            {
                Name = "Epic VaultCache",
                PathFactory = () => Path.Combine(epicPath, @"Launcher\VaultCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Epic Games 启动器 VaultCache。"
            });
            results.Add(new CleanupRule
            {
                Name = "Epic Logs",
                PathFactory = () => Path.Combine(epicPath, @"Launcher\Portal\Logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.WindowsLogs,
                Description = "Epic Games 启动器日志。"
            });
        }

        // Battle.net
        var bnetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net");
        if (Directory.Exists(bnetPath))
        {
            results.Add(new CleanupRule
            {
                Name = "Battle.net Cache",
                PathFactory = () => Path.Combine(bnetPath, "Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Battle.net 启动器缓存。"
            });
        }

        // HoYo (Genshin / Star Rail)
        var hoYoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoYo");
        if (Directory.Exists(hoYoPath))
        {
            results.Add(new CleanupRule
            {
                Name = "HoYo Cache",
                PathFactoryMulti = () =>
                {
                    var root = hoYoPath;
                    var items = new List<string>();
                    foreach (var gameDir in Directory.GetDirectories(root))
                    {
                        foreach (var sub in new[] { "Cache", "GPUCache", "logs" })
                        {
                            var p = Path.Combine(gameDir, sub);
                            if (Directory.Exists(p)) items.Add(p);
                        }
                    }
                    return items;
                },
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "米哈游游戏缓存（原神/星穹铁道等）。"
            });
        }

        return results;
    }

    private static string? TryGetSteamPath()
    {
        try
        {
            // Registry: HKCU\Software\Valve\Steam\SteamPath
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string p && Directory.Exists(p))
                return p;
        }
        catch { }
        // Fallback to default
        var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(def) ? def : null;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Electron Generic — scan all Electron apps for Cache/GPUCache/Code Cache
    //  Detects unknown Electron apps by looking for "Local State" + "Preferences"
    //  fingerprint inside %APPDATA%/<App>/
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetElectronCacheRules()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Known set we already cover explicitly — skip to avoid double counting.
        var knownApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Code", "discord", "Slack", "Notion", "Postman",
            "Tencent\\WeChat", "Tencent\\QQ", "Tencent\\WeGame",
            "Microsoft\\Edge", "Google\\Chrome", "BraveSoftware", "Vivaldi", "Opera Software"
        };

        bool IsElectronApp(string appDir)
        {
            // 收紧指纹: 要求同时存在 Local State + Preferences (Chromium 内核应用都有),
            // 并且至少存在 GPUCache 或 Code Cache 之一 (这两者是 Electron 应用特有的,
            // CEF/WebView2 应用通常没有). 这样能避免误识别:
            //   - CEF 应用 (QQ 音乐早期版本、网易云早期版本)
            //   - WebView2 应用 (新版微信、Office、Teams)
            //   - 纯 Chromium 浏览器外壳
            try
            {
                if (!File.Exists(Path.Combine(appDir, "Local State"))) return false;
                if (!File.Exists(Path.Combine(appDir, "Preferences"))) return false;

                var hasGpuCache = Directory.Exists(Path.Combine(appDir, "GPUCache"));
                var hasCodeCache = Directory.Exists(Path.Combine(appDir, "Code Cache"));
                if (!hasGpuCache && !hasCodeCache) return false;

                // 进一步排除 WebView2: WebView2 应用通常有 EBWebView 子目录而不是 GPUCache
                // (即便有 GPUCache, 也是放在 EBWebView\GPUCache 下)
                if (Directory.Exists(Path.Combine(appDir, "EBWebView"))) return false;

                return true;
            }
            catch { return false; }
        }

        bool IsKnown(string appRel)
        {
            foreach (var k in knownApps)
            {
                if (appRel.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase)
                    || appRel.Equals(k, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        return
        [
            new()
            {
                Name = "Electron App Cache (auto-discovered)",
                PathFactoryMulti = () =>
                {
                    var results = new List<string>();
                    foreach (var root in new[] { roaming, local })
                    {
                        if (!Directory.Exists(root)) continue;
                        foreach (var vendorDir in Directory.GetDirectories(root))
                        {
                            // Support one level of nesting (e.g. Roaming\SomeVendor\SomeApp)
                            foreach (var appDir in EnumerateAppCandidates(vendorDir))
                            {
                                var rel = Path.GetRelativePath(root, appDir);
                                if (IsKnown(rel)) continue;
                                if (!IsElectronApp(appDir)) continue;

                                foreach (var sub in new[] { "Cache", "GPUCache", "Code Cache", "logs" })
                                {
                                    var p = Path.Combine(appDir, sub);
                                    if (Directory.Exists(p)) results.Add(p);
                                }
                            }
                        }
                    }
                    return results.Distinct(StringComparer.OrdinalIgnoreCase);
                },
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "自动发现的 Electron 应用缓存（Code Cache/GPUCache/Cache/logs）。已排除显式覆盖的应用。"
            }
        ];
    }

    private static IEnumerable<string> EnumerateAppCandidates(string dir)
    {
        // 使用收紧后的指纹判断当前目录是否为 Electron 应用根目录
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
        catch
        {
            isElectronRoot = false;
        }

        if (isElectronRoot)
        {
            yield return dir;
            yield break;
        }

        // Otherwise descend one level
        string[] subs;
        try { subs = Directory.GetDirectories(dir); }
        catch { yield break; }

        foreach (var sub in subs)
            yield return sub;
    }
}
