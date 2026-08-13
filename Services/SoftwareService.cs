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

    public string SizeText => EstimatedSize switch
    {
        >= 1073741824 => $"{EstimatedSize / 1073741824.0:F2} GB",
        >= 1048576 => $"{EstimatedSize / 1048576.0:F1} MB",
        >= 1024 => $"{EstimatedSize / 1024.0:F1} KB",
        _ => "未知"
    };
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

    public string LeftoverSizeText => LeftoverSize switch
    {
        >= 1073741824 => $"{LeftoverSize / 1073741824.0:F2} GB",
        >= 1048576 => $"{LeftoverSize / 1048576.0:F1} MB",
        >= 1024 => $"{LeftoverSize / 1024.0:F1} KB",
        _ => "0 B"
    };
}

public class SoftwareService : ISoftwareService
{
    private static readonly HashSet<string> KnownInstallerExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "msiexec.exe", "rundll32.exe", "reg.exe", "regsvr32.exe", "wmic.exe"
    };

    public List<InstalledSoftware> GetInstalledSoftware()
    {
        var software = new List<InstalledSoftware>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var paths = new[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine),
            (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Registry.LocalMachine),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Registry.CurrentUser)
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
                            InstallDate = installDate
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
                    var command = key.GetValue(name) as string ?? "";
                    items.Add(new StartupItem
                    {
                        Name = name,
                        Command = command,
                        Location = path,
                        IsEnabled = true,
                        Source = source,
                        // 图标延迟加载, 加快列表呈现
                        IconPath = ""
                    });
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("GetStartupItems", ex); }
        }

        // Startup folder (always enabled)
        try
        {
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    // Skip .disabled backups — they're handled separately below
                    if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        continue;

                    items.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Location = "Startup Folder",
                        IsEnabled = true,
                        Source = "StartupFolder",
                        IconPath = ""
                    });
                }
            }

            // Also check for .disabled files (previously disabled via this app)
            var disabledFiles = Directory.GetFiles(startupFolder, "*.disabled");
            foreach (var file in disabledFiles)
            {
                items.Add(new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file)),
                    Command = file,
                    Location = "Startup Folder",
                    IsEnabled = false,
                    Source = "StartupFolder",
                    IconPath = ""
                });
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
            if (item.Source == "StartupFolder")
            {
                if (!File.Exists(item.Command)) return false;
                var disabledPath = item.Command + ".disabled";
                if (File.Exists(disabledPath)) File.Delete(disabledPath);
                File.Move(item.Command, disabledPath);
                return true;
            }

            var regInfo = ResolveStartupRegPath(item.Source);
            if (regInfo == null) return false;

            using var key = regInfo.Value.Root.OpenSubKey(regInfo.Value.Path, true);
            if (key == null) return false;

            // Back up the value under a "Disabled_" name so we can re-enable later
            var existing = key.GetValue(item.Name);
            if (existing == null) return false;

            var backupName = "Disabled_" + item.Name;
            key.SetValue(backupName, existing, key.GetValueKind(item.Name));
            key.DeleteValue(item.Name, false);
            return true;
        }
        catch (Exception ex) { CleanMaster.App.LogError("DisableStartupItem", ex); }
        return false;
    }

    /// <summary>
    /// Restores a previously-disabled startup item.
    /// </summary>
    public bool EnableStartupItem(StartupItem item)
    {
        try
        {
            if (item.Source == "StartupFolder")
            {
                // item.Command points to the .disabled path when disabled
                var disabledPath = item.Command;
                if (disabledPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(disabledPath))
                {
                    var restored = disabledPath[..^(".disabled".Length)];
                    if (File.Exists(restored)) File.Delete(restored);
                    File.Move(disabledPath, restored);
                    return true;
                }
                return false;
            }

            var regInfo = ResolveStartupRegPath(item.Source);
            if (regInfo == null) return false;

            using var key = regInfo.Value.Root.OpenSubKey(regInfo.Value.Path, true);
            if (key == null) return false;

            var backupName = "Disabled_" + item.Name;
            var backup = key.GetValue(backupName);
            if (backup == null) return false;

            key.SetValue(item.Name, backup, key.GetValueKind(backupName));
            key.DeleteValue(backupName, false);
            return true;
        }
        catch (Exception ex) { CleanMaster.App.LogError("EnableStartupItem", ex); }
        return false;
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
    /// Starts the uninstaller and awaits its exit. Returns the exit code (or null if start failed).
    /// Caller controls the wait timeout via the cancellationToken.
    /// </summary>
    public async Task<(bool Started, int? ExitCode, string Message)> UninstallSoftwareAsync(
        InstalledSoftware software, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(software.UninstallString))
            return (false, null, "No uninstall command found");

        var cmd = software.UninstallString.Trim();

        if (ContainsSuspiciousCharacters(cmd))
            return (false, null, "Uninstall command contains suspicious characters");

        try
        {
            ProcessStartInfo psi;
            string resolvedExe;

            if (cmd.StartsWith("\""))
            {
                var endQuote = cmd.IndexOf('"', 1);
                if (endQuote <= 0)
                    return (false, null, "Invalid uninstall string format");
                resolvedExe = cmd.Substring(1, endQuote - 1);
                var args = cmd.Substring(endQuote + 1).Trim();

                if (!IsAllowedInstallerExe(resolvedExe) && !File.Exists(resolvedExe))
                    return (false, null, $"Uninstaller not found: {resolvedExe}");

                psi = new ProcessStartInfo
                {
                    FileName = resolvedExe,
                    Arguments = args,
                    UseShellExecute = true
                };
            }
            else
            {
                var parts = cmd.Split(' ', 2);
                var exePath = parts[0];
                var args = parts.Length > 1 ? parts[1] : "";

                resolvedExe = ResolveExecutablePath(exePath);
                if (string.IsNullOrEmpty(resolvedExe))
                    return (false, null, $"Cannot find uninstaller: {exePath}");

                psi = new ProcessStartInfo
                {
                    FileName = resolvedExe,
                    Arguments = args,
                    UseShellExecute = true
                };
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!process.Start()) return (false, null, "Failed to start uninstaller process");

            try
            {
                await process.WaitForExitAsync(ct);
                return (true, process.ExitCode, "Uninstaller exited");
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (true, null, "Uninstaller execution was cancelled");
            }
        }
        catch (Exception ex) when (ex.Message.Contains("Cannot find")
                                 || ex.Message.Contains("Invalid")
                                 || ex.Message.Contains("suspicious"))
        {
            return (false, null, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, null, $"Failed to start uninstaller: {ex.Message}");
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
