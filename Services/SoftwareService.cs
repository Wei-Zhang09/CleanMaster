using System.IO;
using Microsoft.Win32;

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

public class StartupItem
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Location { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string Source { get; set; } = "";
    public string IconPath { get; set; } = "";
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

public class SoftwareService
{
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
                        if (name.StartsWith("KB") && name.Contains("Update")) continue;
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
                    catch { }
                }
            }
            catch { }
        }

        return software.OrderByDescending(s => s.EstimatedSize).ToList();
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
                    var iconPath = ExtractExePath(command);
                    items.Add(new StartupItem
                    {
                        Name = name,
                        Command = command,
                        Location = path,
                        IsEnabled = true,
                        Source = source,
                        IconPath = iconPath
                    });
                }
            }
            catch { }
        }

        try
        {
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    items.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Location = "Startup Folder",
                        IsEnabled = true,
                        Source = "StartupFolder",
                        IconPath = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : ""
                    });
                }
            }
        }
        catch { }

        return items.OrderBy(i => i.Name).ToList();
    }

    public bool DisableStartupItem(StartupItem item)
    {
        try
        {
            if (item.Source == "StartupFolder")
            {
                var disabledPath = item.Command + ".disabled";
                File.Move(item.Command, disabledPath);
                return true;
            }
            else
            {
                var path = item.Source switch
                {
                    "HKLM" => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    "HKLM64" => @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                    "HKCU" => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                    _ => ""
                };
                var root = item.Source.StartsWith("HKLM") ? Registry.LocalMachine : Registry.CurrentUser;

                if (!string.IsNullOrEmpty(path))
                {
                    using var key = root.OpenSubKey(path, true);
                    key?.DeleteValue(item.Name, false);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    public void UninstallSoftware(InstalledSoftware software)
    {
        if (string.IsNullOrEmpty(software.UninstallString))
            throw new Exception("No uninstall command found");

        try
        {
            var cmd = software.UninstallString.Trim();
            if (cmd.StartsWith("\""))
            {
                var endQuote = cmd.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    var exe = cmd.Substring(1, endQuote - 1);
                    var args = cmd.Substring(endQuote + 1).Trim();
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
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {cmd}",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to start uninstaller: {ex.Message}");
        }
    }

    public UninstallResult ScanLeftovers(InstalledSoftware software)
    {
        var result = new UninstallResult { Success = true };

        // Scan leftover folders
        var possibleLocations = new List<string>();

        if (!string.IsNullOrEmpty(software.InstallLocation) && Directory.Exists(software.InstallLocation))
            possibleLocations.Add(software.InstallLocation);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var nameParts = software.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in nameParts)
        {
            if (part.Length < 3) continue;
            var lower = part.ToLower();

            foreach (var baseDir in new[] { appData, localAppData, programFiles, programFilesX86 })
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(baseDir, $"*{lower}*", SearchOption.TopDirectoryOnly))
                    {
                        if (!possibleLocations.Contains(dir))
                            possibleLocations.Add(dir);
                    }
                }
                catch { }
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
            catch { }
        }

        // Scan leftover registry keys
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
                    catch { }
                }
            }
            catch { }
        }

        return result;
    }

    public void CleanupLeftovers(UninstallResult result)
    {
        // Delete leftover folders
        foreach (var folder in result.LeftoverFolders)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    {
                        try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); } catch { }
                    }
                    Directory.Delete(folder, true);
                }
            }
            catch { }
        }

        // Clean orphaned registry keys
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
            catch { }
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
        catch
        {
            return "";
        }
    }

    private static string ExtractExePath(string command)
    {
        if (string.IsNullOrEmpty(command)) return "";

        try
        {
            var cmd = command.Trim();

            if (cmd.StartsWith("\""))
            {
                var endQuote = cmd.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    var path = cmd.Substring(1, endQuote - 1);
                    if (File.Exists(path)) return path;
                }
            }

            var parts = cmd.Split(' ', 2);
            var possiblePath = Environment.ExpandEnvironmentVariables(parts[0]);

            if (File.Exists(possiblePath)) return possiblePath;

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            foreach (var baseDir in new[] { programFiles, programFilesX86, windows })
            {
                var fullPath = Path.Combine(baseDir, possiblePath);
                if (File.Exists(fullPath)) return fullPath;
            }
        }
        catch { }

        return "";
    }

    private static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            }))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        catch { }
        return size;
    }
}
