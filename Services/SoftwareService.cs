using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

public class InstalledSoftware
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string UninstallString { get; set; } = "";
    public string IconPath { get; set; } = "";
    public long EstimatedSize { get; set; }
    public DateTime? InstallDate { get; set; }
    public bool IsSelected { get; set; }

    /// <summary>注册表根："HKLM" 或 "HKCU"。用于卸载后回读验证。</summary>
    public string RegistryHive { get; set; } = "";

    /// <summary>卸载登记项的完整注册表路径（含子键名），卸载后据此判断软件是否真的消失。</summary>
    public string RegistryPath { get; set; } = "";

    public string SizeText => EstimatedSize > 0 ? ByteSizeFormatter.Format(EstimatedSize) : "未知";
}

/// <summary>
/// 卸载结果。不能再只用"卸载器进程是否退出"当成功依据：
/// 卸载器可能被 UAC 拒掉、闪退、或用户中途关掉弹窗，退出码照样是 0。
/// </summary>
public enum UninstallOutcome
{
    /// <summary>卸载命令无效或进程起不来。</summary>
    NotStarted,

    /// <summary>等待被取消 / 超时。</summary>
    Cancelled,

    /// <summary>卸载登记项已消失 —— 真的卸载了。</summary>
    VerifiedRemoved,

    /// <summary>卸载器退出了，但注册表里还登记着这个软件。</summary>
    StillRegistered,

    /// <summary>卸载器要求重启才能完成（3010 / 1641）。</summary>
    RestartRequired,

    /// <summary>卸载器退出了，但拿不到登记项路径，无法验证。</summary>
    Unverified,

    /// <summary>用户级安装的软件，但当前身份不是交互式桌面用户（用别的管理员账户提权），拒绝卸载。</summary>
    WrongUserContext
}

public sealed record UninstallAttempt(
    UninstallOutcome Outcome,
    int? ExitCode,
    string Message,
    bool DescendantsStillRunning = false)
{
    public bool Started => Outcome != UninstallOutcome.NotStarted;
}

public class StartupItem : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Location { get; set; } = "";
    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }
    public string Source { get; set; } = "";

    /// <summary>注册表里的原始值名（可能带历史 Disabled_ 前缀）；启动文件夹项为文件名。</summary>
    public string ValueName { get; set; } = "";

    /// <summary>旧机制（改值名 / 改 .lnk 后缀）造成的禁用，迁移期仍要能识别。</summary>
    public bool IsLegacyDisabled { get; set; }

    private string _signatureStatus = "";
    /// <summary>数字签名状态（延迟解析）：Signed / Unsigned / Unknown。</summary>
    public string SignatureStatus
    {
        get => _signatureStatus;
        set
        {
            if (_signatureStatus == value) return;
            _signatureStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SignatureText));
            OnPropertyChanged(nameof(HasNoSignature));
        }
    }

    public string SignatureText => SignatureStatus switch
    {
        "Signed" => "有签名",
        "Unsigned" => "无签名",
        _ => ""
    };

    /// <summary>未签名项给 UI 上醒目提示用。</summary>
    public bool HasNoSignature => SignatureStatus == "Unsigned";

    private string _iconPath = "";
    public string IconPath
    {
        get => _iconPath;
        set { _iconPath = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class UninstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<string> LeftoverFolders { get; set; } = new();
    public List<string> LeftoverRegistryKeys { get; set; } = new();
    public long LeftoverSize { get; set; }

    public string LeftoverSizeText => ByteSizeFormatter.Format(LeftoverSize);
}

public class SoftwareService : ISoftwareService
{
    private static readonly HashSet<string> KnownInstallerExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "msiexec.exe", "rundll32.exe", "reg.exe", "regsvr32.exe", "wmic.exe"
    };

    private readonly IUserContext _userContext;

    public SoftwareService(IUserContext? userContext = null)
    {
        _userContext = userContext ?? DefaultUserContext.Instance;
    }

    public List<InstalledSoftware> GetInstalledSoftware()
    {
        var software = new List<InstalledSoftware>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var paths = new[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine, "HKLM"),
            (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine, "HKLM"),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.CurrentUser, "HKCU")
        };

        foreach (var (path, root, hive) in paths)
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
                        if (IsWindowsUpdateKbEntry(name, subKey)) continue;
                        if (seen.Contains(name)) continue;
                        seen.Add(name);

                        var uninstallStr = subKey.GetValue("UninstallString") as string;
                        if (string.IsNullOrEmpty(uninstallStr)) continue;

                        var installLoc = subKey.GetValue("InstallLocation") as string ?? "";
                        var iconPath = subKey.GetValue("DisplayIcon") as string ?? "";

                        // Clean up icon path
                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            var commaIdx = iconPath.LastIndexOf(',');
                            if (commaIdx > 5) iconPath = iconPath.Substring(0, commaIdx);
                            iconPath = iconPath.Trim('"').Trim();
                            iconPath = Environment.ExpandEnvironmentVariables(iconPath);
                            if (!File.Exists(iconPath)) iconPath = "";
                        }

                        // If no icon, try to find one in install location
                        if (string.IsNullOrEmpty(iconPath) && !string.IsNullOrEmpty(installLoc) && Directory.Exists(installLoc))
                        {
                            iconPath = FindIconInDirectory(installLoc, name);
                        }

                        // If still no icon, try from uninstall string
                        if (string.IsNullOrEmpty(iconPath) && !string.IsNullOrEmpty(uninstallStr))
                        {
                            var exePath = ExtractExePath(uninstallStr);
                            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath)) iconPath = exePath;
                        }

                        long size = 0;
                        var sizeObj = subKey.GetValue("EstimatedSize");
                        if (sizeObj is int sizeInt)
                            size = sizeInt * 1024L;
                        else if (sizeObj is long sizeLong)
                            size = sizeLong * 1024L;
                        else if (sizeObj is string sizeStr && long.TryParse(sizeStr, out var sizeParsed))
                            size = sizeParsed * 1024L;

                        if (size == 0 && !string.IsNullOrEmpty(installLoc) && Directory.Exists(installLoc))
                        {
                            size = GetDirectorySize(installLoc);
                        }

                        DateTime? installDate = null;
                        var dateStr = subKey.GetValue("InstallDate") as string;
                        if (!string.IsNullOrEmpty(dateStr) && dateStr.Length == 8)
                        {
                            if (DateTime.TryParseExact(dateStr, "yyyyMMdd", null,
                                System.Globalization.DateTimeStyles.None, out var dt))
                                installDate = dt;
                        }

                        software.Add(new InstalledSoftware
                        {
                            Name = name,
                            Publisher = subKey.GetValue("Publisher") as string ?? "",
                            Version = subKey.GetValue("DisplayVersion") as string ?? "",
                            InstallLocation = installLoc,
                            UninstallString = uninstallStr,
                            IconPath = iconPath,
                            EstimatedSize = size,
                            InstallDate = installDate,
                            RegistryHive = hive,
                            RegistryPath = path + "\\" + subKeyName
                        });
                    }
                    catch (Exception ex) { CleanMaster.App.LogError("GetInstalledSoftware", ex); }
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("GetInstalledSoftware", ex); }
        }

        return software.OrderByDescending(s => s.EstimatedSize).ToList();
    }

    /// <summary>
    /// Identifies Windows Update / hotfix entries (KB-prefixed and similar) that should
    /// not be exposed as user-uninstallable software.
    /// </summary>
    private static bool IsWindowsUpdateKbEntry(string displayName, RegistryKey subKey)
    {
        // Microsoft's own convention: DisplayName starts with "KB" followed by digits,
        // and ParentKeyName = "OperatingSystem" or "Hotfix" indicates a Windows Update.
        if (string.IsNullOrEmpty(displayName)) return false;

        // Match "KB" + digits (e.g. "KB5031456" or "KB5031456 Update")
        if (displayName.Length >= 4
            && (displayName[0] == 'K' || displayName[0] == 'k')
            && (displayName[1] == 'B' || displayName[1] == 'b')
            && char.IsDigit(displayName[2])
            && char.IsDigit(displayName[3]))
        {
            return true;
        }

        try
        {
            var parent = subKey.GetValue("ParentKeyName") as string;
            if (!string.IsNullOrEmpty(parent)
                && (parent.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0
                    || parent.IndexOf("OperatingSystem", StringComparison.OrdinalIgnoreCase) >= 0
                    || parent.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0
                    || parent.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    public List<StartupItem> GetStartupItems()
    {
        var items = new List<StartupItem>();

        var regPaths = new[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM", Registry.LocalMachine),
            (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM64", Registry.LocalMachine),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU", Registry.CurrentUser),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM-Once", Registry.LocalMachine),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU-Once", Registry.CurrentUser)
        };

        foreach (var (path, source, root) in regPaths)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null) continue;

                foreach (var name in key.GetValueNames())
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    var command = key.GetValue(name) as string ?? "";

                    // 剥离所有历史遗留的 "Disabled_" 前缀，得到规范化显示名。
                    // 早期版本反复禁用/启用在注册表里堆积出 Disabled_Disabled_... 脏键，
                    // 只剥一层会导致启用/禁用时按错名找备份，反复切换最终报"需要管理员权限"。
                    var isLegacyDisabled = name.StartsWith("Disabled_", StringComparison.OrdinalIgnoreCase);
                    var displayName = name;
                    while (displayName.StartsWith("Disabled_", StringComparison.OrdinalIgnoreCase))
                        displayName = displayName.Substring("Disabled_".Length);

                    // 新机制：读任务管理器同一份 StartupApproved 状态（RunOnce 不支持，只能看旧机制）。
                    var approval = StartupApproval.Read(source, displayName);

                    items.Add(new StartupItem
                    {
                        Name = displayName,
                        ValueName = name,
                        Command = command,
                        Location = path,
                        IsEnabled = !isLegacyDisabled && approval != StartupApprovalState.Disabled,
                        IsLegacyDisabled = isLegacyDisabled,
                        Source = source,
                        // 图标延迟加载, 加快列表呈现
                        IconPath = ""
                    });
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("GetStartupItems", ex); }
        }

        // 启动文件夹：同样走 StartupApproved\StartupFolder（任务管理器也是这么管的）。
        // 历史版本用改名 .lnk -> .lnk.disabled 的方式禁用，这里仍要识别出来。
        try
        {
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    var fileName = Path.GetFileName(file);

                    // desktop.ini 是文件夹的元数据文件，不是启动项（冒烟测试里真的被列出来过）
                    if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var attrs = File.GetAttributes(file);
                        if (attrs.HasFlag(FileAttributes.Hidden) && attrs.HasFlag(FileAttributes.System)) continue;
                    }
                    catch { }

                    var isLegacyDisabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    if (isLegacyDisabled) fileName = fileName.Substring(0, fileName.Length - ".disabled".Length);

                    var approval = StartupApproval.Read("StartupFolder", fileName);

                    items.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(fileName),
                        ValueName = fileName,
                        Command = file,
                        Location = "Startup Folder",
                        IsEnabled = !isLegacyDisabled && approval != StartupApprovalState.Disabled,
                        IsLegacyDisabled = isLegacyDisabled,
                        Source = "StartupFolder",
                        IconPath = ""
                    });
                }
            }
        }
        catch (Exception ex) { CleanMaster.App.LogError("GetStartupItems", ex); }

        return items.OrderBy(i => i.Name).ToList();
    }

    /// <summary>
    /// Computes the icon path for a startup item by extracting the executable
    /// from its command line. Called lazily by the UI so the initial list load
    /// is fast and icon extraction doesn't block the registry read.
    /// </summary>
    public string GetStartupItemIconPath(StartupItem item)
    {
        if (item == null) return "";
        try
        {
            if (!string.IsNullOrEmpty(item.IconPath)) return item.IconPath;

            // Startup folder items are .lnk shortcuts — resolve to target .exe
            if (item.Source == "StartupFolder"
                && !string.IsNullOrEmpty(item.Command)
                && item.Command.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                && File.Exists(item.Command))
            {
                var target = ResolveShortcutTarget(item.Command);
                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                {
                    item.IconPath = target;
                    return item.IconPath;
                }
            }

            // Direct .exe in startup folder
            if (item.Source == "StartupFolder"
                && !string.IsNullOrEmpty(item.Command)
                && item.Command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && File.Exists(item.Command))
            {
                item.IconPath = item.Command;
                return item.IconPath;
            }

            // Registry-based startup: extract exe from command line
            item.IconPath = ExtractExePath(item.Command);
            return item.IconPath;
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("GetStartupItemIconPath", ex);
            return "";
        }
    }

    /// <summary>
    /// Resolves a .lnk shortcut file to its target exe using Shell32 COM.
    /// </summary>
    private static string? ResolveShortcutTarget(string lnkPath)
    {
        try
        {
            // Use dynamic COM to avoid adding a project reference to IWshRuntimeLibrary
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            return shortcut.TargetPath as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Disables a startup item by moving it aside (folder) or backing up + removing
    /// the registry value. The original command is preserved so EnableStartupItem can restore it.
    /// </summary>
    public bool DisableStartupItem(StartupItem item)
    {
        try
        {
            // 已经被旧机制禁用过的项，状态本来就是"禁用"，不需要再写一次。
            if (item.IsLegacyDisabled) return true;

            // 支持 StartupApproved 的来源：写任务管理器同一份状态（内部会回读验证）。
            if (StartupApproval.IsSupported(item.Source))
                return StartupApproval.Write(item.Source, ResolveApprovalValueName(item), enabled: false);

            // RunOnce 等没有审批键的来源：只能沿用旧的改名机制。
            return LegacyDisable(item);
        }
        catch (Exception ex) { CleanMaster.App.LogError("DisableStartupItem", ex); }
        return false;
    }

    /// <summary>审批状态用的值名：注册表项用干净值名，启动文件夹用文件名。</summary>
    private static string ResolveApprovalValueName(StartupItem item)
    {
        if (item.Source == "StartupFolder")
            return item.ValueName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? item.ValueName[..^".disabled".Length]
                : item.ValueName;

        var name = string.IsNullOrEmpty(item.ValueName) ? item.Name : item.ValueName;
        while (name.StartsWith("Disabled_", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("Disabled_".Length);
        return name;
    }

    /// <summary>旧机制禁用：注册表改值名 / 启动文件夹改后缀。仅 RunOnce 与迁移路径使用。</summary>
    private bool LegacyDisable(StartupItem item)
    {
        if (item.Source == "StartupFolder")
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var lnkPath = Path.Combine(folder, ResolveApprovalValueName(item));
            var disabledPath = lnkPath + ".disabled";

            if (!File.Exists(lnkPath)) return false;
            if (File.Exists(disabledPath)) File.Delete(disabledPath);
            File.Move(lnkPath, disabledPath);
            return true;
        }

        var regInfo = ResolveStartupRegPath(item.Source);
        if (regInfo == null) return false;

        using var key = regInfo.Value.Root.OpenSubKey(regInfo.Value.Path, true);
        if (key == null) return false;

        var cleanName = ResolveApprovalValueName(item);
        var existing = key.GetValue(cleanName);
        if (existing == null) return false;

        var backupName = "Disabled_" + cleanName;
        key.SetValue(backupName, existing, key.GetValueKind(cleanName));
        key.DeleteValue(cleanName, false);

        // 清理历史遗留的多层前缀脏键（如 Disabled_Disabled_xxx），只保留单层备份键。
        foreach (var candidate in key.GetValueNames().ToList())
        {
            if (!candidate.StartsWith("Disabled_", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(candidate, backupName, StringComparison.OrdinalIgnoreCase)) continue;
            var stripped = candidate;
            while (stripped.StartsWith("Disabled_", StringComparison.OrdinalIgnoreCase))
                stripped = stripped.Substring("Disabled_".Length);
            if (string.Equals(stripped, cleanName, StringComparison.OrdinalIgnoreCase))
                key.DeleteValue(candidate, false);
        }
        return true;
    }

    public bool EnableStartupItem(StartupItem item)
    {
        try
        {
            // 旧机制禁用的项：先把名字/后缀还原（迁移），再保证审批状态为启用。
            if (item.IsLegacyDisabled)
            {
                if (!LegacyRestore(item)) return false;
                if (StartupApproval.IsSupported(item.Source))
                    StartupApproval.Write(item.Source, ResolveApprovalValueName(item), enabled: true);
                return true;
            }

            if (StartupApproval.IsSupported(item.Source))
                return StartupApproval.Write(item.Source, ResolveApprovalValueName(item), enabled: true);

            // RunOnce：只存在旧机制，禁用状态即"值名带前缀"，还原即可。
            return LegacyRestore(item);
        }
        catch (Exception ex) { CleanMaster.App.LogError("EnableStartupItem", ex); }
        return false;
    }

    /// <summary>旧机制还原：注册表改回原名 / 启动文件夹去掉 .disabled 后缀。</summary>
    private bool LegacyRestore(StartupItem item)
    {
        if (item.Source == "StartupFolder")
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var lnkPath = Path.Combine(folder, ResolveApprovalValueName(item));
            var disabledPath = lnkPath + ".disabled";

            if (!File.Exists(disabledPath)) return false;
            if (File.Exists(lnkPath)) File.Delete(lnkPath);
            File.Move(disabledPath, lnkPath);
            return true;
        }

        var regInfo = ResolveStartupRegPath(item.Source);
        if (regInfo == null) return false;

        using var key = regInfo.Value.Root.OpenSubKey(regInfo.Value.Path, true);
        if (key == null) return false;

        var cleanName = ResolveApprovalValueName(item);

        // 优先找单层备份键；找不到则扫描所有 Disabled_ 前缀键（容忍历史多层脏数据）。
        string? backupKeyName = null;
        object? backup = null;
        var expected = "Disabled_" + cleanName;
        if (key.GetValue(expected) != null)
        {
            backupKeyName = expected;
            backup = key.GetValue(expected);
        }
        else
        {
            foreach (var candidate in key.GetValueNames())
            {
                if (!candidate.StartsWith("Disabled_", StringComparison.OrdinalIgnoreCase)) continue;
                var stripped = candidate;
                while (stripped.StartsWith("Disabled_", StringComparison.OrdinalIgnoreCase))
                    stripped = stripped.Substring("Disabled_".Length);
                if (string.Equals(stripped, cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    backupKeyName = candidate;
                    backup = key.GetValue(candidate);
                    break;
                }
            }
        }

        if (backupKeyName == null || backup == null) return false;

        key.SetValue(cleanName, backup, key.GetValueKind(backupKeyName));
        key.DeleteValue(backupKeyName, false);
        return true;
    }

    private static readonly Dictionary<string, string> SignatureCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 启动项对应程序是否有数字签名（延迟解析，结果缓存）。
    /// 只回答"有没有签名"，不回答"可不可信" —— 未签名不等于恶意，别对用户过度承诺。
    /// </summary>
    public string GetStartupItemSignature(StartupItem item)
    {
        var exe = ExtractExePath(item.Command);
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return "Unknown";

        var ext = Path.GetExtension(exe);
        if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            return "Unknown";

        lock (SignatureCache)
        {
            if (SignatureCache.TryGetValue(exe, out var cached)) return cached;
        }

        string status;
        try
        {
            status = AuthenticodeChecker.Check(exe) switch
            {
                SignatureState.Signed => "Signed",
                SignatureState.Unsigned => "Unsigned",
                _ => "Unknown"
            };
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("GetStartupItemSignature", ex);
            status = "Unknown";
        }

        lock (SignatureCache) { SignatureCache[exe] = status; }
        return status;
    }

    private static (RegistryKey Root, string Path)? ResolveStartupRegPath(string source)
    {
        return source switch
        {
            "HKLM" => (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            "HKLM64" => (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
            "HKCU" => (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            "HKLM-Once" => (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            "HKCU-Once" => (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            _ => null
        };
    }

    /// <summary>
    /// 启动卸载器，等**整棵进程树**退出，再回读注册表确认软件是否真的消失。
    /// 返回三态结果而不是"进程退出了就算成功"：卸载器被拒、闪退、用户中途关掉弹窗时退出码同样是 0。
    /// </summary>
    public async Task<UninstallAttempt> UninstallSoftwareAsync(
        InstalledSoftware software,
        CancellationToken ct = default,
        TimeSpan? descendantWaitTimeout = null)
    {
        if (string.IsNullOrEmpty(software.UninstallString))
            return new UninstallAttempt(UninstallOutcome.NotStarted, null, "No uninstall command found");

        var cmd = software.UninstallString.Trim();

        if (ContainsSuspiciousCharacters(cmd))
            return new UninstallAttempt(UninstallOutcome.NotStarted, null, "Uninstall command contains suspicious characters");

        // 用户级安装：卸载器只会清当前令牌对应的 HKCU。若当前身份不是桌面用户（用别的管理员账户提权），
        // 它清的是那个管理员的配置，用户的数据原样留着 —— 直接拒绝并说明，比"卸了个寂寞"诚实。
        if (_userContext.ShouldBlockUserScopeUninstall(software.RegistryHive))
        {
            return new UninstallAttempt(UninstallOutcome.WrongUserContext, null,
                "当前以其他管理员账户运行，无法卸载该用户安装的软件");
        }

        ProcessStartInfo psi;
        try
        {
            string? resolvedExe;

            if (cmd.StartsWith("\""))
            {
                var endQuote = cmd.IndexOf('"', 1);
                if (endQuote <= 0)
                    return new UninstallAttempt(UninstallOutcome.NotStarted, null, "Invalid uninstall string format");

                resolvedExe = cmd.Substring(1, endQuote - 1);
                var args = cmd.Substring(endQuote + 1).Trim();

                if (!IsAllowedInstallerExe(resolvedExe) && !File.Exists(resolvedExe))
                    return new UninstallAttempt(UninstallOutcome.NotStarted, null, $"Uninstaller not found: {resolvedExe}");

                psi = new ProcessStartInfo { FileName = resolvedExe, Arguments = args, UseShellExecute = true };
            }
            else
            {
                var parts = cmd.Split(' ', 2);
                var exePath = parts[0];
                var args = parts.Length > 1 ? parts[1] : "";

                resolvedExe = ResolveExecutablePath(exePath);
                if (string.IsNullOrEmpty(resolvedExe))
                    return new UninstallAttempt(UninstallOutcome.NotStarted, null, $"Cannot find uninstaller: {exePath}");

                psi = new ProcessStartInfo { FileName = resolvedExe, Arguments = args, UseShellExecute = true };
            }
        }
        catch (Exception ex)
        {
            return new UninstallAttempt(UninstallOutcome.NotStarted, null, $"Failed to start uninstaller: {ex.Message}");
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
            return new UninstallAttempt(UninstallOutcome.NotStarted, null, "Failed to start uninstaller process");

        var pid = process.Id;
        int exitCode;

        try
        {
            await process.WaitForExitAsync(ct);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new UninstallAttempt(UninstallOutcome.Cancelled, null, "Uninstaller execution was cancelled");
        }

        // 父进程退出 ≠ 卸载结束：很多卸载器 fork 子进程后立刻退出。
        var descendantsRunning = false;
        try
        {
            var wait = descendantWaitTimeout ?? TimeSpan.FromSeconds(90);
            var allExited = await ProcessTree.WaitForDescendantsAsync(pid, wait, ct);
            descendantsRunning = !allExited;
        }
        catch (OperationCanceledException)
        {
            return new UninstallAttempt(UninstallOutcome.Cancelled, exitCode, "Cancelled while waiting for uninstaller child processes");
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("UninstallSoftwareAsync-waitTree", ex);
        }

        var registration = TryGetRegistrationState(software);

        // 3010 = ERROR_SUCCESS_REBOOT_REQUIRED, 1641 = ERROR_SUCCESS_REBOOT_INITIATED
        if (exitCode is 3010 or 1641)
            return new UninstallAttempt(UninstallOutcome.RestartRequired, exitCode,
                "卸载器要求重启系统才能完成卸载", descendantsRunning);

        if (registration == false)
            return new UninstallAttempt(UninstallOutcome.VerifiedRemoved, exitCode,
                "已在注册表中确认该软件被移除", descendantsRunning);

        if (registration == true)
            return new UninstallAttempt(UninstallOutcome.StillRegistered, exitCode,
                "卸载器已结束，但系统里仍登记着该软件", descendantsRunning);

        return new UninstallAttempt(UninstallOutcome.Unverified, exitCode,
            "卸载器已结束，但无法验证是否卸载完成", descendantsRunning);
    }

    /// <summary>
    /// 回读卸载登记项。true = 仍登记着（软件还在），false = 已消失，null = 无法验证（没有登记路径）。
    /// </summary>
    public bool? TryGetRegistrationState(InstalledSoftware software)
    {
        if (string.IsNullOrEmpty(software.RegistryPath)) return null;

        try
        {
            using var root = software.RegistryHive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            using var key = root.OpenSubKey(software.RegistryPath);
            return key != null;
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("TryGetRegistrationState", ex);
            return null;
        }
    }

    /// <summary>
    /// Synchronous legacy entry. Prefer <see cref="UninstallSoftwareAsync"/>.
    /// </summary>
    public void UninstallSoftware(InstalledSoftware software)
    {
        if (string.IsNullOrEmpty(software.UninstallString))
            throw new Exception("No uninstall command found");

        var cmd = software.UninstallString.Trim();

        if (ContainsSuspiciousCharacters(cmd))
            throw new Exception("Uninstall command contains suspicious characters");

        try
        {
            if (cmd.StartsWith("\""))
            {
                var endQuote = cmd.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    var exe = cmd.Substring(1, endQuote - 1);
                    var args = cmd.Substring(endQuote + 1).Trim();

                    if (!IsAllowedInstallerExe(exe) && !File.Exists(exe))
                        throw new Exception("Invalid uninstaller executable path");

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = args,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                var parts = cmd.Split(' ', 2);
                var exePath = parts[0];

                var resolvedPath = ResolveExecutablePath(exePath);
                if (string.IsNullOrEmpty(resolvedPath))
                    throw new Exception($"Cannot find uninstaller: {exePath}");

                var args = parts.Length > 1 ? parts[1] : "";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = resolvedPath,
                    Arguments = args,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex) when (ex.Message.Contains("Cannot find") || ex.Message.Contains("Invalid") || ex.Message.Contains("suspicious"))
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to start uninstaller: {ex.Message}");
        }
    }

    /// <summary>
    /// Shell-metacharacter filter. Allows parentheses (used by many MSI/Inno setups),
    /// but blocks shell separators that could chain commands.
    /// </summary>
    private static bool ContainsSuspiciousCharacters(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        // Block shell command separators only. Allow () {} as they appear in legit GUIDs/paths.
        var suspiciousPatterns = new[] { "&", "|", ";", "`", "$", "<", ">", "^", "\n", "\r" };
        return suspiciousPatterns.Any(p => input.Contains(p));
    }

    private static bool IsAllowedInstallerExe(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return false;
        var fileName = Path.GetFileName(exePath);
        return KnownInstallerExes.Contains(fileName);
    }

    private static string? ResolveExecutablePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // Known installer stubs - run via shell
        if (KnownInstallerExes.Contains(Path.GetFileName(path)))
            return path;

        if (File.Exists(path)) return path;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        foreach (var baseDir in new[] { programFiles, programFilesX86, windows })
        {
            var fullPath = Path.Combine(baseDir, path);
            if (File.Exists(fullPath)) return fullPath;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (File.Exists(expanded)) return expanded;

        return null;
    }

    public UninstallResult ScanLeftovers(InstalledSoftware software)
    {
        var result = new UninstallResult { Success = true };

        var possibleLocations = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(software.InstallLocation) && Directory.Exists(software.InstallLocation))
        {
            possibleLocations.Add(software.InstallLocation);
            seen.Add(software.InstallLocation);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // Build precise matching tokens from the install location (preferred) or software name.
        // The previous implementation split the name by spaces and matched any 3+ char part,
        // which caused false positives (e.g. "Microsoft Visual C++" matched "Microsoft Edge").
        // We now require matches against the most-specific token (Publisher + Name) and
        // explicitly exclude directories that look like other vendors.
        var tokens = ExtractMatchTokens(software);

        foreach (var token in tokens)
        {
            foreach (var baseDir in new[] { appData, localAppData, programFiles, programFilesX86 })
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(baseDir, $"*{token}*", SearchOption.TopDirectoryOnly))
                    {
                        if (seen.Contains(dir)) continue;
                        // Only accept if directory name actually starts with or equals the token-ish
                        if (!IsLikelyLeftover(dir, software, token)) continue;
                        possibleLocations.Add(dir);
                        seen.Add(dir);
                    }
                }
                catch (Exception ex) { CleanMaster.App.LogError("ScanLeftovers", ex); }
            }
        }

        foreach (var loc in possibleLocations)
        {
            try
            {
                if (Directory.Exists(loc))
                {
                    var size = GetDirectorySize(loc);
                    result.LeftoverFolders.Add(loc);
                    result.LeftoverSize += size;
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("ScanLeftovers", ex); }
        }

        var regPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var regPath in regPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key == null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        var displayName = subKey?.GetValue("DisplayName") as string;
                        if (displayName == software.Name)
                        {
                            result.LeftoverRegistryKeys.Add($@"HKLM\{regPath}\{subKeyName}");
                        }
                    }
                    catch (Exception ex) { CleanMaster.App.LogError("ScanLeftovers", ex); }
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("ScanLeftovers", ex); }
        }

        return result;
    }

    /// <summary>
    /// Produces precise tokens to match against leftover folder names.
    /// Priority: install location basename > publisher > most specific name token.
    /// </summary>
    private static List<string> ExtractMatchTokens(InstalledSoftware software)
    {
        var tokens = new List<string>();

        if (!string.IsNullOrEmpty(software.InstallLocation))
        {
            var basename = Path.GetFileName(software.InstallLocation.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(basename) && basename.Length >= 3)
                tokens.Add(basename);
        }

        if (!string.IsNullOrEmpty(software.Publisher))
        {
            // Only use publisher if it doesn't equal generic terms
            var p = software.Publisher.Trim();
            if (p.Length >= 3 && !IsGenericPublisher(p))
                tokens.Add(p);
        }

        // Use the longest token of the name as a last resort (min 4 chars)
        var nameParts = software.Name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in nameParts.OrderByDescending(p => p.Length))
        {
            if (part.Length < 4) continue;
            if (IsGenericNameToken(part)) continue;
            tokens.Add(part);
            break; // only the longest specific token
        }

        return tokens.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsGenericPublisher(string p)
    {
        var lowered = p.ToLowerInvariant();
        return lowered == "microsoft corporation" || lowered == "microsoft" || lowered == "windows";
    }

    private static bool IsGenericNameToken(string t)
    {
        var lowered = t.ToLowerInvariant();
        return lowered switch
        {
            "update" or "version" or "x64" or "x86" or "32" or "64" or "the" => true,
            _ => false
        };
    }

    private static bool IsLikelyLeftover(string dir, InstalledSoftware software, string token)
    {
        var dirName = Path.GetFileName(dir);
        if (dirName.Length < 3) return false;

        // Require the token to appear in the directory name (already enforced by glob),
        // but additionally disallow obvious other-vendor matches when the publisher
        // token wasn't used.
        if (dirName.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) return false;

        // Exclude common system folders
        var lower = dirName.ToLowerInvariant();
        if (lower == "microsoft" || lower == "windows" || lower == "common files"
            || lower == "package cache" || lower == "installer")
            return false;

        return true;
    }

    public void CleanupLeftovers(UninstallResult result)
    {
        foreach (var folder in result.LeftoverFolders)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    // Clear attributes first so read-only files can be deleted
                    foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    {
                        try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); } catch (Exception ex) { Debug.WriteLine($"CleanupLeftovers: {ex.Message}"); }
                    }
                    Directory.Delete(folder, true);
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("CleanupLeftovers", ex); }
        }

        foreach (var regKey in result.LeftoverRegistryKeys)
        {
            try
            {
                var parts = regKey.Split('\\');
                if (parts.Length < 3) continue;

                var rootStr = parts[0];
                var subPath = string.Join("\\", parts.Skip(1));

                RegistryKey? root = rootStr switch
                {
                    "HKLM" => Registry.LocalMachine,
                    "HKCU" => Registry.CurrentUser,
                    _ => null
                };
                if (root == null) continue;

                var parentPath = string.Join("\\", parts.Skip(1).Take(parts.Length - 2));
                var keyName = parts.Last();
                using var parentKey = root.OpenSubKey(parentPath, true);
                parentKey?.DeleteSubKeyTree(keyName, false);
            }
            catch (Exception ex) { CleanMaster.App.LogError("CleanupLeftovers", ex); }
        }
    }

    private static string FindIconInDirectory(string dirPath, string softwareName)
    {
        try
        {
            var exes = Directory.GetFiles(dirPath, "*.exe", new EnumerationOptions { IgnoreInaccessible = true });
            if (exes.Length == 0) return "";

            var normalizedName = softwareName.Replace(" ", "").ToLower();
            foreach (var exe in exes)
            {
                var exeName = Path.GetFileNameWithoutExtension(exe).Replace(" ", "").ToLower();
                if (exeName.Contains(normalizedName) || normalizedName.Contains(exeName))
                    return exe;
            }

            return exes[0];
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("FindIconInDirectory", ex);
            return "";
        }
    }

    private static string ExtractExePath(string command)
    {
        if (string.IsNullOrEmpty(command)) return "";

        try
        {
            var cmd = command.Trim();

            // Handle quoted paths: "C:\Program Files\App\app.exe" /arg
            if (cmd.StartsWith("\""))
            {
                var endQuote = cmd.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    var path = cmd.Substring(1, endQuote - 1);
                    // Expand environment variables first
                    var expanded = Environment.ExpandEnvironmentVariables(path);
                    if (File.Exists(expanded)) return expanded;
                    if (File.Exists(path)) return path;
                }
            }

            // Handle unquoted paths: app.exe /arg or %ProgramFiles%\App\app.exe
            var parts = cmd.Split(' ', 2);
            var possiblePath = Environment.ExpandEnvironmentVariables(parts[0]);

            if (File.Exists(possiblePath)) return possiblePath;

            // Search common program directories
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var system32 = Path.Combine(windows, "System32");
            var syswow64 = Path.Combine(windows, "SysWOW64");

            foreach (var baseDir in new[] { programFiles, programFilesX86, windows, system32, syswow64 })
            {
                var fullPath = Path.Combine(baseDir, possiblePath);
                if (File.Exists(fullPath)) return fullPath;
            }

            // Fallback: return expanded path even if file doesn't exist yet
            // (IconExtractor will handle the actual icon extraction with fallback)
            if (!string.IsNullOrEmpty(possiblePath) && possiblePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return possiblePath;
        }
        catch (Exception ex) { CleanMaster.App.LogError("ExtractExePath", ex); }

        return "";
    }

    private static long GetDirectorySize(string path)
        => FileSystemUtils.GetDirectorySize(path);
}
