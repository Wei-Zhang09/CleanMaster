using System.IO;
using CleanMaster.Models;

namespace CleanMaster.Rules;

public class CleanupRule
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public CleanSafety Safety { get; set; }
    public CleanCategory Category { get; set; }
    public string Description { get; set; } = "";

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
    private static readonly string SystemDrive = Path.GetPathRoot(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:";
    public static List<CleanupRule> GetAllRules()
    {
        var rules = new List<CleanupRule>();
        rules.AddRange(GetRecycleBinRules());
        rules.AddRange(GetTempRules());
        rules.AddRange(GetWindowsUpdateRules());
        rules.AddRange(GetWindowsLogRules());
        rules.AddRange(GetBrowserCacheRules());
        rules.AddRange(GetDevToolCacheRules());
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
            Description = "回收站中的文件。这些是用户主动删除的内容，清空后无法恢复。"        }
    ];

    // ──────────────────────────────────────────────────────────────────
    //  Temp / Prefetch
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetTempRules() =>
    [
        new()
        {
            Name = "User Temp",
            PathFactory = () => Path.GetTempPath(),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.TempFiles,
            Description = "应用程序产生的临时文件。这些是软件运行时写入的临时数据，关闭对应软件后可安全删除。"
        },
        new()
        {
            Name = "Windows Temp",
            Path = $@"{SystemDrive}Windows\Temp",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.TempFiles,
            Description = "Windows 系统临时文件。系统服务和后台进程产生，可安全删除。"
        },
        new()
        {
            Name = "Prefetch",
            Path = $@"{SystemDrive}Windows\Prefetch",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.TempFiles,
            Description = "应用启动预读取缓存。Windows 会根据使用习惯自动重建，删除后前几次启动略慢。"
        },
        new()
        {
            Name = "LocalService Temp",
            Path = $@"{SystemDrive}Windows\ServiceProfiles\LocalService\AppData\Local\Temp",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.TempFiles,
            Description = "LocalService 系统账户的临时文件。系统服务运行时产生，可安全删除。"
        },
        new()
        {
            Name = "NetworkService Temp",
            Path = $@"{SystemDrive}Windows\ServiceProfiles\NetworkService\AppData\Local\Temp",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.TempFiles,
            Description = "NetworkService 系统账户的临时文件。网络相关服务运行时产生，可安全删除。"
        }
    ];

    // ──────────────────────────────────────────────────────────────────
    //  Windows Update
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetWindowsUpdateRules() =>
    [
        new()
        {
            Name = "WU Download Cache",
            Path = $@"{SystemDrive}Windows\SoftwareDistribution\Download",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsUpdate,
            Description = "Windows 更新下载缓存。已安装完成的更新对应的下载文件，可安全删除。未安装完的更新会重新下载。"
        },
        new()
        {
            Name = "WU DataStore",
            Path = $@"{SystemDrive}Windows\SoftwareDistribution\DataStore",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.WindowsUpdate,
            Description = "Windows 更新数据库。记录已安装更新列表。清理后更新历史记录会被清空，但不影响已安装的更新本身。"
        },
        new()
        {
            Name = "Delivery Optimization",
            Path = $@"{SystemDrive}Windows\SoftwareDistribution\DeliveryOptimization",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsUpdate,
            Description = "Windows 更新分发优化文件。用于局域网内分摊更新下载流量，可安全删除。"
        },
        new()
        {
            Name = "Windows.old",
            Path = $@"{SystemDrive}Windows.old",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.WindowsUpdate,
            Description = "Windows 升级备份（Windows.old）。用于回退到旧版本。清理后无法回退，建议升级满 10 天后再清理。"
        }
    ];

    // ──────────────────────────────────────────────────────────────────
    //  Windows Logs
    //  Note: CBS and Compressed are subdirectories of C:\Windows\Logs.
    //  We list them separately so users can choose granularity, but to avoid
    //  double-scanning we point "Windows Logs" at the parent and use FilePatterns
    //  to scan only top-level *.log files (leaving subdirs to their own rules).
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetWindowsLogRules() =>
    [
        new()
        {
            Name = "Windows Logs (top-level)",
            Path = $@"{SystemDrive}Windows\Logs",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "C:\\Windows\\Logs 下的顶层日志文件。",
            FilePatterns = new[] { "*.log", "*.etl", "*.cab", "*.xml" },
            PatternsAreDirectories = false
        },
        new()
        {
            Name = "CBS Logs",
            Path = $@"{SystemDrive}Windows\Logs\CBS",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "Component-Based Servicing 日志。"
        },
        new()
        {
            Name = "Windows Logs Compressed",
            Path = $@"{SystemDrive}Windows\Logs\Compressed",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "已压缩的旧日志文件。"
        },
        new()
        {
            Name = "DISM Logs",
            Path = $@"{SystemDrive}Windows\Logs\DISM",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "DISM 工具日志。"
        },
        new()
        {
            Name = "DPX Logs",
            Path = $@"{SystemDrive}Windows\Logs\DPX",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "DPX 文件日志。"
        },
        new()
        {
            Name = "Setup Logs (Panther)",
            Path = $@"{SystemDrive}Windows\Panther",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "Windows 安装与升级日志。"
        },
        new()
        {
            Name = "SysReset Logs",
            Path = $@"{SystemDrive}$SysReset",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "系统重置日志。"
        },
        new()
        {
            Name = "Windows LogFiles",
            Path = $@"{SystemDrive}Windows\System32\LogFiles",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "HTTP/sys、IIS 等系统服务的日志文件。"
        },
        new()
        {
            Name = "Windows Debug",
            Path = $@"{SystemDrive}Windows\debug",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "系统调试转储文件。"
        },
        new()
        {
            Name = "Security Logs",
            Path = $@"{SystemDrive}Windows\security\logs",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "Windows 安全日志文件。"
        },
        new()
        {
            Name = "Windows Defender History",
            Path = $@"{SystemDrive}ProgramData\Microsoft\Windows Defender\Scans\History",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "Windows Defender 扫描历史记录。"
        }
    ];

    // ──────────────────────────────────────────────────────────────────
    //  Browser Cache — multi-profile aware
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetBrowserCacheRules()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Returns all profile cache directories under a browser's "User Data" root.
        // Matches Default, Profile 1, Profile 2, Guest, etc.
        IEnumerable<string> EnumerateProfiles(string userDataRoot, string subRel)
        {
            if (!Directory.Exists(userDataRoot)) yield break;
            foreach (var profile in Directory.GetDirectories(userDataRoot))
            {
                var profileName = Path.GetFileName(profile);
                // Skip non-profile entries like "Snapshots", "Crashpad", "SmartScreen"
                if (profileName.Equals("Snapshots", StringComparison.OrdinalIgnoreCase)) continue;
                if (profileName.Equals("Crashpad", StringComparison.OrdinalIgnoreCase)) continue;
                if (profileName.Equals("SmartScreen", StringComparison.OrdinalIgnoreCase)) continue;
                if (profileName.Equals("GrShaderCache", StringComparison.OrdinalIgnoreCase)) continue;

                var candidate = Path.Combine(profile, subRel);
                if (Directory.Exists(candidate)) yield return candidate;
            }
        }

        return
        [
            new()
            {
                Name = "Edge Cache (all profiles)",
                PathFactoryMulti = () => EnumerateProfiles(
                    Path.Combine(local, @"Microsoft\Edge\User Data"), "Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Microsoft Edge 浏览器缓存（所有用户配置）。包含网页图片、脚本、样式等资源。删除后下次访问页面会重新从网络加载。"
            },
            new()
            {
                Name = "Edge Code Cache (all profiles)",
                PathFactoryMulti = () => EnumerateProfiles(
                    Path.Combine(local, @"Microsoft\Edge\User Data"), "Code Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Microsoft Edge JavaScript 代码缓存。Edge 编译后的 JS 代码缓存，删除后网页首次打开略慢，会自动重建。"
            },
            new()
            {
                Name = "Edge GPUCache (all profiles)",
                PathFactoryMulti = () => EnumerateProfiles(
                    Path.Combine(local, @"Microsoft\Edge\User Data"), "GPUCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Microsoft Edge GPU 着色器缓存。GPU 渲染网页时编译的着色器，删除后游戏/视频首帧渲染略慢。"
            },
            new()
            {
                Name = "Chrome Cache (all profiles)",
                PathFactoryMulti = () => EnumerateProfiles(
                    Path.Combine(local, @"Google\Chrome\User Data"), "Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Google Chrome 浏览器缓存（所有用户配置）。包含网页资源。删除后下次访问页面会重新加载。"
            },
            new()
            {
                Name = "Chrome Code Cache (all profiles)",
                PathFactoryMulti = () => EnumerateProfiles(
                    Path.Combine(local, @"Google\Chrome\User Data"), "Code Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Google Chrome JavaScript 代码缓存。删除后网页首次打开略慢，会自动重建。"
            },
            new()
            {
                Name = "Chrome GPUCache (all profiles)",
                PathFactoryMulti = () => EnumerateProfiles(
                    Path.Combine(local, @"Google\Chrome\User Data"), "GPUCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Google Chrome GPU 着色器缓存。删除后视频/游戏渲染略慢，会自动重建。"
            },
            new()
            {
                Name = "Brave Cache (all profiles)",
                PathFactoryMulti = () => EnumerateProfiles(
                    Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"), "Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Brave 浏览器缓存。删除后网页资源会重新从网络加载。"
            },
            new()
            {
                Name = "Brave GPUCache (all profiles)",
                PathFactoryMulti = () => EnumerateProfiles(
                    Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"), "GPUCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Brave GPU 着色器缓存。删除后视频渲染略慢，会自动重建。"
            },
            new()
            {
                Name = "Opera Cache",
                PathFactory = () => Path.Combine(local, @"Opera Software\Opera Stable\Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Opera 浏览器缓存。删除后网页资源会重新加载。"
            },
            new()
            {
                Name = "Vivaldi Cache (all profiles)",
                PathFactoryMulti = () => EnumerateProfiles(
                    Path.Combine(local, @"Vivaldi\User Data"), "Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Vivaldi 浏览器缓存。删除后网页资源会重新加载。"
            },
            new()
            {
                Name = "Firefox Cache2",
                PathFactoryMulti = () =>
                {
                    var profilesRoot = Path.Combine(local, @"Mozilla\Firefox\Profiles");
                    if (!Directory.Exists(profilesRoot)) return Enumerable.Empty<string>();
                    return Directory.GetDirectories(profilesRoot)
                        .Select(p => Path.Combine(p, "cache2"))
                        .Where(Directory.Exists);
                },
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "Firefox 浏览器缓存（cache2 子目录）。仅清理网页资源缓存，不影响书签、密码和历史记录。"
            },
            new()
            {
                Name = "INetCache (IE/Edge legacy)",
                PathFactory = () => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\INetCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.BrowserCache,
                Description = "WinINet 缓存。IE 和旧版 Edge 兼容层使用的网页缓存，可安全删除。"
            },
            new()
            {
                Name = "WebCache",
                PathFactory = () => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\WebCache"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.BrowserCache,
                Description = "WinINet WebCache 数据库。可能包含 IE/Edge 历史记录索引。建议关闭浏览器后清理，否则可能被占用无法删除。"
            }
        ];
    }

    // ──────────────────────────────────────────────────────────────────
    //  Dev Tool Cache
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetDevToolCacheRules()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return
        [
            new()
            {
                Name = "Gradle Caches",
                PathFactory = () => Path.Combine(user, @".gradle\caches"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Gradle 构建工具的下载缓存。下次构建会重新从网络下载依赖，可能耗时较长。"
            },
            new()
            {
                Name = "Gradle Daemon Logs",
                PathFactory = () => Path.Combine(user, @".gradle\daemon"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Gradle daemon 进程日志。Gradle 后台进程运行时产生的日志，可安全删除。"
            },
            new()
            {
                Name = "NuGet Packages",
                PathFactory = () => Path.Combine(user, @".nuget\packages"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.DevToolCache,
                Description = "NuGet 包缓存（global packages 目录）。.NET 项目还原时会重新下载，可能耗时较长。"
            },
            new()
            {
                Name = "NuGet HTTP Cache",
                PathFactory = () => Path.Combine(local, @"NuGet\v3-cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "NuGet HTTP 下载临时缓存。仅缓存下载过程的临时文件，不影响已解压到 global packages 的包。"
            },
            new()
            {
                Name = "npm Cache",
                PathFactory = () => Path.Combine(roaming, "npm-cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "npm 包缓存。Node.js 项目安装依赖时会重新下载。"
            },
            new()
            {
                Name = "pip Cache",
                PathFactory = () => Path.Combine(local, @"pip\cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Python pip 下载缓存。下次安装 Python 包时会重新下载。"
            },
            new()
            {
                Name = "Maven Repository",
                PathFactory = () => Path.Combine(user, @".m2\repository"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.DevToolCache,
                Description = "Maven 本地仓库。Java 项目构建时会重新下载依赖，可能耗时较长。"
            },
            new()
            {
                Name = "JetBrains Caches",
                PathFactoryMulti = () =>
                {
                    if (!Directory.Exists(local + @"\JetBrains")) return Enumerable.Empty<string>();
                    // Iterate all IDE versions (e.g. IntelliJIdea2024.3, PyCharm2024.3)
                    var results = new List<string>();
                    foreach (var ide in Directory.GetDirectories(local + @"\JetBrains"))
                    {
                        foreach (var sub in new[] { "caches", "index", "log", "tmp" })
                        {
                            var p = Path.Combine(ide, sub);
                            if (Directory.Exists(p)) results.Add(p);
                        }
                    }
                    return results;
                },
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "IntelliJ 系列产品（IDEA/PyCharm/DataGrip 等）的缓存、索引和日志。删除后下次启动会重建索引，大型项目可能耗时数分钟。"
            },
            new()
            {
                Name = "VS Code Cache",
                PathFactory = () => Path.Combine(roaming, @"Code\Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Visual Studio Code 缓存（VSCache 子目录）。包含扩展和编辑器的临时数据，删除后自动重建。"
            },
            new()
            {
                Name = "VS Code CachedData",
                PathFactory = () => Path.Combine(roaming, @"Code\CachedData"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Visual Studio Code CacheData 子目录。包含 WebView 缓存数据，删除后自动重建。"
            },
            new()
            {
                Name = "VS Code GPUCache",
                PathFactory = () => Path.Combine(roaming, @"Code\GPUCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Visual Studio Code GPU 着色器缓存。删除后界面渲染略慢，会自动重建。"
            },
            new()
            {
                Name = "VS Code logs",
                PathFactory = () => Path.Combine(roaming, @"Code\logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Visual Studio Code 日志文件。记录扩展运行日志、错误日志等。"
            },
            new()
            {
                Name = "VS Code workspaceStorage",
                PathFactory = () => Path.Combine(roaming, @"Code\User\workspaceStorage"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "VS Code 工作区临时存储。包含每个项目的撤销历史和临时状态。删除影响：未保存的撤销历史会丢失，已保存的代码不受影响。"
            },
            new()
            {
                Name = "VS Code Extension Cache (all extensions)",
                PathFactoryMulti = () =>
                {
                    var extRoot = Path.Combine(user, @".vscode\extensions");
                    if (!Directory.Exists(extRoot)) return Enumerable.Empty<string>();
                    return Directory.GetDirectories(extRoot).Select(d => Path.Combine(d, "cache")).Where(Directory.Exists);
                },
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "VS Code 各扩展的 cache 子目录（覆盖 cpptools、Java、C# 等）。删除后扩展会重建缓存。"
            },
            new()
            {
                Name = "Cargo Cache (Rust)",
                PathFactory = () => Path.Combine(user, @".cargo\registry\cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Rust Cargo 包缓存。下次构建 Rust 项目时会重新下载依赖。"
            },
            new()
            {
                Name = "Conda pkgs",
                PathFactory = () => Path.Combine(user, @".conda\pkgs"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.DevToolCache,
                Description = "Conda 包缓存。下次安装 Python 包时会重新下载。"
            },
            new()
            {
                Name = "Yarn Cache",
                PathFactory = () => Path.Combine(local, @"Yarn\Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Yarn 包缓存。Node.js 项目安装依赖时会重新下载。"
            },
            new()
            {
                Name = "pnpm store",
                PathFactory = () => Path.Combine(user, @".pnpm-store"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "pnpm 内容寻址存储。pnpm 全局包存储，删除后所有依赖项目需要重新安装。"
            },
            new()
            {
                Name = "Bun install cache",
                PathFactory = () => Path.Combine(user, @".bun\install\cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Bun 包安装缓存。Bun 运行时下载的包缓存，可安全删除。"
            },
            new()
            {
                Name = "Go mod cache",
                PathFactory = () => Path.Combine(user, @"go\pkg\mod"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Go modules 缓存。Go 项目构建时会重新下载依赖。"
            },
            new()
            {
                Name = "Android build-cache",
                PathFactory = () => Path.Combine(user, @".android\build-cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Android 构建中间产物缓存（.gradle/caches/transforms 等）。下次构建会重新生成。"
            },
            new()
            {
                Name = "Docker Desktop logs",
                PathFactory = () => Path.Combine(local, @"Docker\log"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Docker Desktop 日志文件。"
            },
            new()
            {
                Name = "Docker Desktop cache",
                PathFactoryMulti = () =>
                {
                    var results = new List<string>();
                    var dl = Path.Combine(local, "Docker");
                    if (Directory.Exists(dl))
                    {
                        foreach (var sub in new[] { "cache", "tmp" })
                        {
                            var p = Path.Combine(dl, sub);
                            if (Directory.Exists(p)) results.Add(p);
                        }
                    }
                    return results;
                },
                Safety = CleanSafety.Caution,
                Category = CleanCategory.DevToolCache,
                Description = "Docker Desktop 临时下载与缓存。仅清理客户端缓存，不会删除 WSL 镜像和容器数据。"
            },
            new()
            {
                Name = "Postman Cache",
                PathFactory = () => Path.Combine(roaming, @"Postman\Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.DevToolCache,
                Description = "Postman API 客户端缓存。删除后收藏的请求和环境变量不受影响。"
            }
        ];
    }

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
            // ── Lingma / MarsCode / Qoder / Codex ──
            new()
            {
                Name = "Lingma Cache",
                PathFactory = () => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".lingma\cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "阿里巴巴通义灵码 AI 助手缓存。"            },
            new()
            {
                Name = "Lingma Index",
                PathFactory = () => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".lingma\index"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "通义灵码代码索引。删除后 AI 补全首次启用会重新索引项目。"
            },
            new()
            {
                Name = "Lingma Logs",
                PathFactory = () => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".lingma\logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "通义灵码日志文件。"
            },
            new()
            {
                Name = "Codex Temp",
                PathFactory = () => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".codex\.tmp"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "OpenAI Codex CLI 临时文件。"
            },
            new()
            {
                Name = "MarsCode Cache",
                PathFactory = () => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".marscode\cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "字节豆包 MarsCode AI 助手缓存。删除后扩展会重建索引。"
            },
            new()
            {
                Name = "Qoder shared_client",
                PathFactory = () => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".qoder-cn\shared_client"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Qoder AI 客户端缓存。"
            },

            // ── WeChat / QQ ──
            // 注意: 不再扫描整个 WeChat / QQ 根目录, 防止误删配置/聊天数据库。
            // 只枚举明确的缓存子目录: Cache / GPUCache / Code Cache / logs / Temp 等。
            new()
            {
                Name = "WeChat Cache",
                PathFactoryMulti = () => FindSubdirs(
                    Path.Combine(roaming, @"Tencent\WeChat"),
                    "Cache", "GPUCache", "Code Cache", "logs", "Temp"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "微信缓存子目录。删除不影响聊天记录和历史文件，下次使用时缓存会重新生成。"
            },
            new()
            {
                Name = "WeChat Files Cache",
                PathFactoryMulti = () =>
                {
                    // Documents\WeChat Files\<用户id>\ 下有 FileStorage 等子目录,
                    // 这里只清明确的缓存: FileStorage\Tmp、FileStorage\Cache 等子目录。
                    var root = Path.Combine(docs, @"WeChat Files");
                    if (!Directory.Exists(root)) return Enumerable.Empty<string>();
                    var results = new List<string>();
                    foreach (var userDir in Directory.GetDirectories(root))
                    {
                        foreach (var sub in new[] { @"FileStorage\Tmp", @"FileStorage\Cache", @"FileStorage\CDNFileStorage\Temp" })
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
                Name = "QQ Cache",
                PathFactoryMulti = () => FindSubdirs(
                    Path.Combine(local, @"Tencent\QQ"),
                    "Cache", "GPUCache", "Code Cache", "logs", "Temp"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "QQ 缓存子目录。删除不影响聊天记录，下次使用时缓存会重新生成。"
            },
            new()
            {
                Name = "QQNT Cache",
                PathFactoryMulti = () => FindSubdirs(
                    Path.Combine(docs, @"Tencent Files\QQNT"),
                    "Cache", "GPUCache", "Code Cache", "logs"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.AppCache,
                Description = "新版 QQ (QQNT) 缓存子目录。删除不影响聊天记录，但下次启动时部分历史媒体需重新下载。"
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
                Name = "Tencent Video Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"Tencent\QQLive"), "Cache", "GPUCache", "logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "腾讯视频客户端缓存。包含播放器临时数据，删除后自动重建。"
            },
            new()
            {
                Name = "WeGame Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(roaming, @"Tencent\WeGame"), "Cache", "logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "WeGame 平台缓存。包含游戏更新临时文件，删除后自动重建。"
            },

            // ── Office / WPS ──
            new()
            {
                Name = "WPS Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(roaming, @"kingsoft\wps"), "Cache", "backup"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.AppCache,
                Description = "WPS Office 缓存与备份。注意：backup 目录可能包含未保存文档的自动保存版本，建议确认后再清理。"
            },

            // ── 国产视频 / 音乐 / 网盘 ──
            new()
            {
                Name = "iQiyi (爱奇艺) Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"iqiyi"), "Cache", "GPUCache", "Code Cache", "logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "爱奇艺客户端缓存。删除后下次播放视频会重新加载资源。"
            },
            new()
            {
                Name = "Youku (优酷) Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"Youku"), "Cache", "GPUCache", "Code Cache", "logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "优酷客户端缓存。删除后下次播放视频会重新加载资源。"
            },
            new()
            {
                Name = "Bilibili Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"Bilibili"), "Cache", "GPUCache", "Code Cache", "logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "哔哩哔哩客户端缓存。删除后下次播放视频会重新加载资源。"
            },
            new()
            {
                Name = "NetEase Music (网易云音乐) Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"NetEase\CloudMusic"), "Cache", "GPUCache", "WebStorage"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "网易云音乐客户端缓存。包含播放缓存和封面图，删除后播放歌曲会重新加载。"
            },
            new()
            {
                Name = "QQ Music Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"Tencent\QQMusic"), "Cache", "GPUCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "QQ音乐客户端缓存。删除后播放歌曲会重新加载。"
            },
            new()
            {
                Name = "Kuwo (酷我) Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"Kuwo"), "Cache", "GPUCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "酷我音乐客户端缓存。删除后播放歌曲会重新加载。"
            },
            new()
            {
                Name = "Kugou (酷狗) Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"Kugou"), "Cache", "GPUCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "酷狗音乐客户端缓存。删除后播放歌曲会重新加载。"
            },
            new()
            {
                Name = "Douyin (抖音) Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"Douyin"), "Cache", "GPUCache", "Code Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "抖音客户端缓存。删除后下次打开会重新加载视频资源。"
            },
            new()
            {
                Name = "AliyunPan (阿里云盘) Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"Alibaba\AliyunPan"), "Cache", "GPUCache", "logs", "tmp"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "阿里云盘客户端缓存。删除后下次访问文件会重新加载缩略图。"
            },
            new()
            {
                Name = "Baidu Netdisk (百度网盘) Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"BaiduNetdisk"), "Cache", "GPUCache", "logs", "tmp"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "百度网盘客户端缓存。删除后下次访问文件会重新加载缩略图，下载中的文件不会被删除。"
            },
            new()
            {
                Name = "115 网盘 Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(local, @"115"), "Cache", "GPUCache", "logs"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "115 网盘客户端缓存。删除后下次访问文件会重新加载缩略图。"
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

            // ── 即时通讯 / 协作 ──
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

            // ── Electron 通用 ──
            new()
            {
                Name = "Notion Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(roaming, @"Notion"), "Cache", "GPUCache", "Code Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Notion 桌面客户端缓存。删除后已同步的笔记不受影响。"
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
            },
            new()
            {
                Name = "Discord Cache",
                PathFactoryMulti = () => FindSubdirs(Path.Combine(roaming, @"discord"), "Cache", "GPUCache", "Code Cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Discord 桌面客户端缓存。删除后聊天和好友列表不受影响。"
            },

            // ── Windows 资源管理器 ──
            new()
            {
                Name = "缩略图缓存",
                PathFactory = () => Path.Combine(local, @"Microsoft\Windows\Explorer"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "文件资源管理器缩略图缓存。删除后浏览图片/视频文件夹时会自动重建，首次浏览略慢。",
                FilePatterns = new[] { "thumbcache_*.db", "iconcache_*.db" },
                PatternsAreDirectories = false
            },
            new()
            {
                Name = "IconCache.db",
                Path = "",  // will be filled by factory
                PathFactory = () => Path.Combine(local, "IconCache.db"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Windows 全局图标缓存。删除后桌面/开始菜单图标短暂显示异常，会自动重建。"
            },
            new()
            {
                Name = "最近文档记录",
                PathFactory = () => Environment.GetFolderPath(Environment.SpecialFolder.Recent),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Windows 最近打开文件的快捷方式。删除后「最近访问」列表清空，不影响原文件。"
            },
            new()
            {
                Name = "DirectX 着色器缓存",
                PathFactory = () => Path.Combine(local, "D3DSCache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "DirectX 着色器缓存。删除后游戏首次启动时编译着色器略慢，会自动重建。"
            }
        ];
    }

    // ──────────────────────────────────────────────────────────────────
    //  Installer Cache
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetInstallerCacheRules() =>
    [
        new()
        {
            Name = "System Package Cache",
            Path = $@"{SystemDrive}ProgramData\Package Cache",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.InstallerCache,
            Description = "Visual Studio 等安装程序缓存。卸载或修复软件时可能需要这些文件，建议空间紧张时再清理。"
        },
        new()
        {
            Name = "User Package Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Package Cache"),
            Safety = CleanSafety.Caution,
            Category = CleanCategory.InstallerCache,
            Description = "用户级安装程序缓存（%LocalAppData%\\Package Cache）。修复或卸载软件时可能需要。"
        },
        new()
        {
            Name = "Windows Installer Patch Cache",
            Path = $@"{SystemDrive}Windows\Installer\$PatchCache$",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.InstallerCache,
            Description = "Windows Installer 补丁缓存。清理后修复已安装软件可能需要原始安装包。"
        },
        new()
        {
            Name = "Downloaded Program Files",
            Path = $@"{SystemDrive}Windows\Downloaded Program Files",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.InstallerCache,
            Description = "IE/旧版 ActiveX 下载的程序文件。现代浏览器已不使用，可安全删除。"
        }
    ];

    // ──────────────────────────────────────────────────────────────────
    //  Crash Dumps
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetCrashDumpRules() =>
    [
        new()
        {
            Name = "Crash Dumps",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CrashDumps"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "应用程序崩溃转储文件。包含崩溃时的内存快照，普通用户可安全删除。"
        },
        new()
        {
            Name = "Windows Error Reporting (root)",
            Path = $@"{SystemDrive}ProgramData\Microsoft\Windows\WER",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "Windows 错误报告根目录临时文件。",
            FilePatterns = new[] { "*.dmp", "*.tmp", "*.cab", "*.wer" },
            PatternsAreDirectories = false
        },
        new()
        {
            Name = "WER ReportArchive",
            Path = $@"{SystemDrive}ProgramData\Microsoft\Windows\WER\ReportArchive",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "Windows 错误报告归档。"
        },
        new()
        {
            Name = "WER ReportQueue",
            Path = $@"{SystemDrive}ProgramData\Microsoft\Windows\WER\ReportQueue",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "Windows 错误报告队列。"
        },
        new()
        {
            Name = "LiveKernelReports",
            Path = $@"{SystemDrive}Windows\LiveKernelReports",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "实时内核错误报告。"
        },
        new()
        {
            Name = "MEMORY.DMP",
            PathFactory = () => $@"{SystemDrive}Windows\MEMORY.DMP",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "系统崩溃时的完整内存转储文件，用于调试分析。"
        },
        new()
        {
            Name = "Minidump",
            Path = $@"{SystemDrive}Windows\Minidump",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "系统崩溃时的小型转储文件。"
        }
    ];

    // ──────────────────────────────────────────────────────────────────
    //  Windows Cache (system-level caches not covered elsewhere)
    // ──────────────────────────────────────────────────────────────────
    private static List<CleanupRule> GetWindowsCacheRules() =>
    [
        new()
        {
            Name = "程序兼容性缓存 (AppCompat)",
            Path = $@"{SystemDrive}Windows\AppCompat\Programs",
            Safety = CleanSafety.Dangerous,
            Category = CleanCategory.AppCache,
            Description = "兼容性数据库 (.sdb)。删除可能破坏已注册的程序兼容性设置，谨慎操作。"
        },
        new()
        {
            Name = "Windows Search Index",
            Path = $@"{SystemDrive}ProgramData\Microsoft\Search\Data",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.AppCache,
            Description = "Windows 搜索索引数据。删除后索引会重建（搜索会暂时变慢）。"
        },
        new()
        {
            Name = "Font Cache",
            Path = $@"{SystemDrive}Windows\ServiceProfiles\LocalService\AppData\Local\FontCache",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Windows 字体缓存。删除后会自动重建。"
        },
        // ── 新增高价值低风险规则 ──
        new()
        {
            Name = "Edge WebView2 Cache",
            PathFactoryMulti = () =>
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var root = Path.Combine(local, @"Microsoft\EdgeWebView\User Data");
                if (!Directory.Exists(root)) return Enumerable.Empty<string>();
                var results = new List<string>();
                // WebView2 用户数据: Default / Prof-XX / WebViewProfile-XX 等多个 profile
                foreach (var profile in Directory.GetDirectories(root))
                {
                    foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", "Service Worker" })
                    {
                        var p = Path.Combine(profile, sub);
                        if (Directory.Exists(p)) results.Add(p);
                    }
                }
                // 也包含 EBWebView 子目录 (部分应用嵌套)
                var eb = Path.Combine(root, "EBWebView");
                if (Directory.Exists(eb))
                {
                    foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                    {
                        var p = Path.Combine(eb, sub);
                        if (Directory.Exists(p)) results.Add(p);
                    }
                }
                return results.Distinct(StringComparer.OrdinalIgnoreCase);
            },
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Edge WebView2 运行时缓存。越来越多应用（新版微信、Office、Teams 等）使用 WebView2 替代 Electron。删除后嵌入 WebView2 的应用首次启动略慢。"
        },
        new()
        {
            Name = "UWP/Store 应用 LocalCache",
            PathFactoryMulti = () =>
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var packages = Path.Combine(local, "Packages");
                if (!Directory.Exists(packages)) return Enumerable.Empty<string>();
                var results = new List<string>();
                // 每个 UWP 应用目录下有 <PackageFamilyName>\LocalCache\Local\... 结构
                foreach (var pkg in Directory.GetDirectories(packages))
                {
                    var localCache = Path.Combine(pkg, @"LocalCache\Local");
                    if (Directory.Exists(localCache))
                    {
                        // 只清常见的缓存子目录, 避免误删应用数据
                        foreach (var sub in new[] { "Temp", "cache", "Cache", "GPUCache", "WebView" })
                        {
                            var p = Path.Combine(localCache, sub);
                            if (Directory.Exists(p)) results.Add(p);
                        }
                    }
                    // 也清理 AC\Temp (临时数据)
                    var acTemp = Path.Combine(pkg, @"AC\Temp");
                    if (Directory.Exists(acTemp)) results.Add(acTemp);
                    // INetCache (IE 兼容层缓存, UWP 内嵌 WebView 使用)
                    var inetCache = Path.Combine(pkg, @"AC\INetCache");
                    if (Directory.Exists(inetCache)) results.Add(inetCache);
                }
                return results.Distinct(StringComparer.OrdinalIgnoreCase);
            },
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "UWP/Store 应用本地缓存。包含临时数据和 WebView 缓存。删除后下次启动应用会重建，不影响应用设置和保存的数据。"
        },
        new()
        {
            Name = "Windows 通知缓存",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Notifications"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Windows 通知中心缓存。包含通知图标和临时数据，不影响通知历史记录本身。"
        }
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
            new()
            {
                Name = "Huggingface Model Cache",
                PathFactory = () => Path.Combine(user, @".cache\huggingface"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.AppCache,
                Description = "Huggingface 模型缓存。下次使用会重新下载，可能数 GB。"
            },
            new()
            {
                Name = "PyTorch Model Cache",
                PathFactory = () => Path.Combine(user, @".cache\torch"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.AppCache,
                Description = "PyTorch 模型缓存。"
            },
            new()
            {
                Name = "Ollama Models",
                PathFactory = () => Path.Combine(user, @".ollama\models"),
                Safety = CleanSafety.Caution,
                Category = CleanCategory.AppCache,
                Description = "Ollama 本地模型文件。删除后需要重新下载（耗时长）。"
            },
            new()
            {
                Name = "Claude Code Cache",
                PathFactory = () => Path.Combine(user, @".claude\cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Claude Code CLI 缓存。"
            },
            new()
            {
                Name = "Gemini CLI Cache",
                PathFactory = () => Path.Combine(user, @".gemini\cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Gemini CLI 缓存。"
            },
            new()
            {
                Name = "Continue.dev Cache",
                PathFactory = () => Path.Combine(user, @".continue\cache"),
                Safety = CleanSafety.Safe,
                Category = CleanCategory.AppCache,
                Description = "Continue.dev AI 助手缓存。"
            }
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
