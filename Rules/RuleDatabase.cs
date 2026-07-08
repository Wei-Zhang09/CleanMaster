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
    public Func<string>? PathFactory { get; set; }
    public string[]? FilePatterns { get; set; }
    public int MaxDepth { get; set; } = -1;
    public bool IsEnabled { get; set; } = true;

    public string GetResolvedPath()
    {
        return PathFactory?.Invoke() ?? Path;
    }
}

public static class RuleDatabase
{
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
        return rules;
    }

    private static List<CleanupRule> GetRecycleBinRules() =>
    [
        new()
        {
            Name = "Recycle Bin",
            Path = @"C:\$Recycle.Bin",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.RecycleBin,
            Description = "Files in the Recycle Bin. These have already been deleted by the user."
        }
    ];

    private static List<CleanupRule> GetTempRules() =>
    [
        new()
        {
            Name = "User Temp",
            PathFactory = () => Path.GetTempPath(),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.TempFiles,
            Description = "Temporary files created by applications. Safe to delete."
        },
        new()
        {
            Name = "Windows Temp",
            Path = @"C:\Windows\Temp",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.TempFiles,
            Description = "System temporary files."
        },
        new()
        {
            Name = "Prefetch",
            Path = @"C:\Windows\Prefetch",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.TempFiles,
            Description = "Application launch cache. Windows will rebuild automatically."
        }
    ];

    private static List<CleanupRule> GetWindowsUpdateRules() =>
    [
        new()
        {
            Name = "WU Download Cache",
            Path = @"C:\Windows\SoftwareDistribution\Download",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsUpdate,
            Description = "Downloaded Windows Update files. Already installed updates can be cleaned."
        },
        new()
        {
            Name = "WU DataStore",
            Path = @"C:\Windows\SoftwareDistribution\DataStore",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.WindowsUpdate,
            Description = "Windows Update database. Cleaning may reset update history."
        }
    ];

    private static List<CleanupRule> GetWindowsLogRules() =>
    [
        new()
        {
            Name = "Windows Logs",
            Path = @"C:\Windows\Logs",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "System log files."
        },
        new()
        {
            Name = "Event Logs",
            Path = @"C:\Windows\System32\winevt\Logs",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.WindowsLogs,
            Description = "Windows Event Viewer logs. May be useful for troubleshooting."
        },
        new()
        {
            Name = "CBS Logs",
            Path = @"C:\Windows\Logs\CBS",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "Component-Based Servicing logs."
        },
        new()
        {
            Name = "Setup Logs",
            Path = @"C:\Windows\Panther",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "Windows setup and upgrade logs."
        },
        new()
        {
            Name = "SysReset Logs",
            Path = @"C:\$SysReset",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "System reset logs."
        }
    ];

    private static List<CleanupRule> GetBrowserCacheRules() =>
    [
        new()
        {
            Name = "Edge Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\User Data\Default\Cache"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.BrowserCache,
            Description = "Microsoft Edge browser cache. Pages will reload when visited."
        },
        new()
        {
            Name = "Edge Code Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\User Data\Default\Code Cache"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.BrowserCache,
            Description = "Microsoft Edge code cache."
        },
        new()
        {
            Name = "Chrome Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data\Default\Cache"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.BrowserCache,
            Description = "Google Chrome browser cache."
        },
        new()
        {
            Name = "Chrome Code Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data\Default\Code Cache"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.BrowserCache,
            Description = "Google Chrome code cache."
        }
    ];

    private static List<CleanupRule> GetDevToolCacheRules() =>
    [
        new()
        {
            Name = "Gradle Caches",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".gradle\caches"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.DevToolCache,
            Description = "Gradle build system cache. Will rebuild on next build."
        },
        new()
        {
            Name = "Gradle Daemon Logs",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".gradle\daemon"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.DevToolCache,
            Description = "Gradle daemon process logs."
        },
        new()
        {
            Name = "NuGet Packages",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".nuget\packages"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.DevToolCache,
            Description = "NuGet package cache. Will re-download on next restore."
        },
        new()
        {
            Name = "npm Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm-cache"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.DevToolCache,
            Description = "npm package cache."
        },
        new()
        {
            Name = "pip Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"pip\cache"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.DevToolCache,
            Description = "Python pip download cache."
        },
        new()
        {
            Name = "Maven Repository",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".m2\repository"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.DevToolCache,
            Description = "Maven local repository cache."
        },
        new()
        {
            Name = "JetBrains Caches",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"JetBrains"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.DevToolCache,
            Description = "IntelliJ/PyCharm/DataGrip caches and indexes. Will rebuild on next launch.",
            FilePatterns = ["caches", "index", "log", "local", "extInstalledPlugins"]
        }
    ];

    private static List<CleanupRule> GetAppCacheRules() =>
    [
        new()
        {
            Name = "Lingma Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".lingma\cache"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Alibaba Lingma AI assistant cache."
        },
        new()
        {
            Name = "Lingma Index",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".lingma\index"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Lingma code index."
        },
        new()
        {
            Name = "Lingma Logs",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".lingma\logs"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Lingma log files."
        },
        new()
        {
            Name = "Codex Temp",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".codex\.tmp"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "OpenAI Codex temporary files."
        },
        new()
        {
            Name = "MarsCode Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".marscode"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "ByteDance MarsCode AI cache."
        },
        new()
        {
            Name = "Qoder Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".qoder-cn\shared_client"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Qoder AI client cache."
        },
        new()
        {
            Name = "Chroma Vector DB",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".cache\chroma"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Chroma vector database cache."
        },
        new()
        {
            Name = "Codex Runtimes",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".cache\codex-runtimes"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Codex runtime cache."
        },
        new()
        {
            Name = "Hyperframes Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".cache\hyperframes"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Hyperframes cache."
        },
        new()
        {
            Name = "Baidu Netdisk Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"baidu\BaiduYunKernel"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Baidu Netdisk download cache."
        },
        new()
        {
            Name = "Tencent Video Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Tencent\QQLive"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Tencent Video cache."
        },
        new()
        {
            Name = "WPS Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"kingsoft\wps"),
            Safety = CleanSafety.Caution,
            Category = CleanCategory.AppCache,
            Description = "WPS Office cache. May contain auto-save documents."
        },
        new()
        {
            Name = "WeGame Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Tencent\WeGame"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "WeGame platform cache."
        }
    ];

    private static List<CleanupRule> GetInstallerCacheRules() =>
    [
        new()
        {
            Name = "System Package Cache",
            Path = @"C:\ProgramData\Package Cache",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.InstallerCache,
            Description = "Visual Studio and other installer caches. Uninstallers may need these."
        },
        new()
        {
            Name = "User Package Cache",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Package Cache"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.InstallerCache,
            Description = "User installer caches."
        }
    ];

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
            Description = "Application crash dump files."
        },
        new()
        {
            Name = "Windows Error Reporting",
            Path = @"C:\ProgramData\Microsoft\Windows\WER",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "Windows Error Reporting data."
        }
    ];

    private static List<CleanupRule> GetWindowsCacheRules() =>
    [
        new()
        {
            Name = "缩略图缓存",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Explorer"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "文件缩略图缓存，删除后Windows会自动重建",
            FilePatterns = new[] { "thumbcache_*.db", "iconcache_*.db" }
        },
        new()
        {
            Name = "Windows更新备份",
            Path = @"C:\Windows\SoftwareDistribution\DataStore",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.WindowsUpdate,
            Description = "Windows更新数据库，清理后更新历史记录将丢失"
        },
        new()
        {
            Name = "Delivery Optimization",
            Path = @"C:\Windows\SoftwareDistribution\DeliveryOptimization",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsUpdate,
            Description = "Windows更新分发优化文件，可安全删除"
        },
        new()
        {
            Name = "Windows.old",
            Path = @"C:\Windows.old",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.WindowsUpdate,
            Description = "Windows升级备份，清理后无法回退到旧版本"
        },
        new()
        {
            Name = "DirectX着色器缓存",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"D3DSCache"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "DirectX着色器缓存，删除后游戏首次启动可能变慢"
        },
        new()
        {
            Name = "Windows Defender日志",
            Path = @"C:\ProgramData\Microsoft\Windows Defender\Scans\History",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "Windows Defender扫描历史记录"
        },
        new()
        {
            Name = "内存转储文件",
            Path = @"C:\Windows\MEMORY.DMP",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "系统崩溃时的内存转储文件，用于调试分析"
        },
        new()
        {
            Name = "小型转储文件",
            Path = @"C:\Windows\Minidump",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.CrashDumps,
            Description = "系统崩溃时的小型转储文件"
        }
    ];

    private static List<CleanupRule> GetSystemCacheRules() =>
    [
        new()
        {
            Name = "最近文档记录",
            PathFactory = () => Environment.GetFolderPath(Environment.SpecialFolder.Recent),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Windows最近打开的文件快捷方式"
        },
        new()
        {
            Name = "Windows搜索缓存",
            PathFactory = () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\ConnectedSearch"),
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "Windows搜索索引缓存"
        },
        new()
        {
            Name = "程序兼容性缓存",
            Path = @"C:\Windows\AppCompat\Programs",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.AppCache,
            Description = "程序兼容性助手缓存"
        },
        new()
        {
            Name = "Windows日志压缩",
            Path = @"C:\Windows\Logs\Compressed",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "已压缩的旧日志文件"
        },
        new()
        {
            Name = "操作中心日志",
            Path = @"C:\Windows\System32\wpestate.json",
            Safety = CleanSafety.Safe,
            Category = CleanCategory.WindowsLogs,
            Description = "Windows操作中心状态日志"
        },
        new()
        {
            Name = "已安装程序缓存",
            Path = @"C:\Windows\Installer\$PatchCache$",
            Safety = CleanSafety.Caution,
            Category = CleanCategory.InstallerCache,
            Description = "Windows Installer补丁缓存，清理后修复安装可能需要源文件"
        }
    ];
}