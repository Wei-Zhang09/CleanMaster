namespace CleanMaster.Services;

/// <summary>一台机器上"有缓存价值"的已装应用（过滤掉系统组件/运行库/SDK 等噪音）。</summary>
public class InstalledApp
{
    public string DisplayName { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string Publisher { get; set; } = "";
}

/// <summary>
/// 枚举注册表卸载键，得到用户实际安装的应用清单，并过滤掉与缓存扫描无关的噪音
/// （运行库、SDK、驱动、Windows Update 补丁、Visual Studio 组件等）。
///
/// 这是「路线 C」扫描规则重构的核心数据源：扫描规则不再硬编码应用名，
/// 而是基于这里枚举到的真实已装应用动态生成。
/// </summary>
public class InstalledAppService
{
    // 命中即过滤的噪音关键字（DisplayName 包含任意一个就跳过）
    private static readonly string[] NoiseKeywords =
    {
        // 运行库 / SDK / 目标包
        "Redistributable", "Runtime", "Targeting Pack", "AppHost Pack",
        "Shared Framework", "Windows SDK", "Software Development Kit",
        "Templates", "SDK ", "Toolset", "Workload", "Manifest",
        // 驱动 / 硬件组件
        "Driver", "HAL", "Controller",
        // Windows 更新 / 补丁
        "Update for", "Hotfix", "Security Update", "KB",
        // Visual Studio / 开发组件
        "Visual Studio", "vs_", "IntelliTrace", "Diagnostics", "ClickOnce",
        "TestPlatform", "Intellisense", "Certification Kit", "WinAppDeploy",
        // NVIDIA 驱动组件（但保留 NVIDIA App 本体的 shader cache）
        "NVIDIA AIUser Container", "NVIDIA LocalSystem Container",
        "NVIDIA Session Container", "NVIDIA User Container", "NVIDIA Telemetry",
        "NVIDIA Backend", "NVIDIA Container", "NVIDIA Install Application",
        "NVIDIA NvDLISR", "NVIDIA MessageBus", "NVIDIA Watchdog", "NVIDIA HD 音频",
        // 其他系统/安装器噪音
        "Language Pack", "MUI", "Proofing", "Office 16 Click-to-Run",
        "Bootstrapper", "Extended Survey", "Setup Configuration", "WMI Provider",
        "SP1", "Service Pack", "Update Helper",
    };

    // 命中即过滤的 Publisher（微软/系统厂商的组件）
    private static readonly string[] NoisePublishers =
    {
        "Microsoft Corporation", "Microsoft",
    };

    /// <summary>枚举所有已装应用（含 InstallLocation），过滤系统噪音。</summary>
    public List<InstalledApp> EnumerateApps()
    {
        var apps = new List<InstalledApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var paths = new[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Microsoft.Win32.Registry.LocalMachine),
            (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Microsoft.Win32.Registry.LocalMachine),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Microsoft.Win32.Registry.CurrentUser),
        };

        foreach (var (path, root) in paths)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var name = subKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (seen.Contains(name)) continue;

                        if (IsNoise(name, subKey.GetValue("Publisher") as string)) continue;

                        seen.Add(name);
                        var installLoc = subKey.GetValue("InstallLocation") as string ?? "";
                        apps.Add(new InstalledApp
                        {
                            DisplayName = name,
                            InstallLocation = Environment.ExpandEnvironmentVariables(installLoc),
                            Publisher = subKey.GetValue("Publisher") as string ?? ""
                        });
                    }
                    catch (Exception ex) { App.LogError("EnumerateApps-inner", ex); }
                }
            }
            catch (Exception ex) { App.LogError("EnumerateApps", ex); }
        }

        return apps.OrderBy(a => a.DisplayName).ToList();
    }

    /// <summary>判断是否为缓存扫描无关的噪音条目。</summary>
    private static bool IsNoise(string displayName, string? publisher)
    {
        // 发布者为微软 → 噪音（但保留 Microsoft Edge 等有缓存价值的，通过下面的白名单）
        if (!string.IsNullOrEmpty(publisher)
            && NoisePublishers.Any(p => publisher.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            // 微软系里有缓存价值的产品（浏览器等）
            var microsoftValuable = new[] { "Edge", "OneDrive", "Office", "Microsoft 365" };
            if (!microsoftValuable.Any(v => displayName.Contains(v, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // 关键字命中 → 噪音
        foreach (var kw in NoiseKeywords)
        {
            if (displayName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
