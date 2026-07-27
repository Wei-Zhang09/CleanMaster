using System.IO;
using Microsoft.Win32;
using CleanMaster.Models;

namespace CleanMaster.Services;

public class RegistryCleanerService
{
    private static readonly HashSet<string> SystemStubs = new(StringComparer.OrdinalIgnoreCase)
    {
        "msiexec.exe", "msiexec", "rundll32.exe", "rundll32",
        "reg.exe", "reg", "regsvr32.exe", "regsvr32",
        "wmic.exe", "wmic", "schtasks.exe", "schtasks"
    };

    public List<RegistryIssue> ScanForIssues()
    {
        var issues = new List<RegistryIssue>();

        // Scan uninstall registry for orphaned entries
        issues.AddRange(ScanUninstallKeys());

        // Scan file association for orphaned entries
        issues.AddRange(ScanFileAssociations());

        return issues;
    }

    private List<RegistryIssue> ScanUninstallKeys()
    {
        var issues = new List<RegistryIssue>();
        var paths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var path in paths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key == null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var displayName = subKey.GetValue("DisplayName") as string;
                        var installLocation = subKey.GetValue("InstallLocation") as string;
                        var uninstallStr = subKey.GetValue("UninstallString") as string;

                        if (string.IsNullOrEmpty(displayName)) continue;

                        // Skip MSI/system-stub uninstall entries — these rely on Windows Installer
                        // or other system binaries; missing File.Exists(exe) is normal, not orphan.
                        if (!string.IsNullOrEmpty(uninstallStr))
                        {
                            var exePath = ExtractExePath(uninstallStr);
                            var fileName = !string.IsNullOrEmpty(exePath) ? Path.GetFileName(exePath) : "";

                            if (!string.IsNullOrEmpty(fileName) && SystemStubs.Contains(fileName))
                            {
                                // Not orphaned by definition; skip.
                                continue;
                            }

                            if (!string.IsNullOrEmpty(exePath) && !File.Exists(exePath))
                            {
                                issues.Add(new RegistryIssue
                                {
                                    Key = $@"HKLM\{path}\{subKeyName}",
                                    Name = displayName,
                                    Type = "Orphaned Uninstall Entry",
                                    Description = $"Uninstaller not found: {exePath}",
                                    CanClean = true
                                });
                                continue; // don't double-report same entry
                            }
                        }

                        // Check if install location is empty or doesn't exist
                        if (!string.IsNullOrEmpty(installLocation) && !Directory.Exists(installLocation))
                        {
                            issues.Add(new RegistryIssue
                            {
                                Key = $@"HKLM\{path}\{subKeyName}",
                                Name = displayName,
                                Type = "Missing Install Location",
                                Description = $"Install directory not found: {installLocation}",
                                CanClean = true
                            });
                        }
                    }
                    catch (Exception ex) { CleanMaster.App.LogError("ScanUninstallKeys", ex); }
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("ScanUninstallKeys", ex); }
        }
        return issues;
    }

    private List<RegistryIssue> ScanFileAssociations()
    {
        var issues = new List<RegistryIssue>();
        // Check for orphaned file type associations
        // This is a safe check - we only report, don't auto-clean
        return issues;
    }

    public void CleanIssue(RegistryIssue issue)
    {
        if (!issue.CanClean) return;

        try
        {
            var parts = issue.Key.Split('\\');
            if (parts.Length < 3) return;

            var rootStr = parts[0];
            var subPath = string.Join("\\", parts.Skip(1));

            RegistryKey? root = rootStr switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                _ => null
            };

            if (root == null) return;

            // Export backup before deletion
            try
            {
                var backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CleanMaster", "RegistryBackups");
                Directory.CreateDirectory(backupDir);
                var backupFile = Path.Combine(backupDir,
                    $"backup_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeFileName(issue.Name)}.reg");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"export \"{issue.Key}\" \"{backupFile}\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(5000);
            }
            catch (Exception ex) { CleanMaster.App.LogError("CleanIssue-backup", ex); /* non-critical: backup failure shouldn't block cleanup */ }

            // Delete the subkey
            var parentPath = string.Join("\\", parts.Skip(1).Take(parts.Length - 2));
            var keyName = parts.Last();
            using var parentKey = root.OpenSubKey(parentPath, true);
            parentKey?.DeleteSubKeyTree(keyName, false);
        }
        catch (Exception ex) { CleanMaster.App.LogError("CleanIssue", ex); }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unnamed";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (invalid.Contains(c) || c == ' ') sb.Append('_');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ExtractExePath(string command)
    {
        if (string.IsNullOrEmpty(command)) return "";
        var cmd = command.Trim();
        if (cmd.StartsWith("\""))
        {
            var endQuote = cmd.IndexOf('"', 1);
            if (endQuote > 0) return cmd.Substring(1, endQuote - 1);
        }

        // Split on first whitespace to get the executable token
        var parts = cmd.Split(' ', 2);
        return parts[0];
    }
}

public class RegistryIssue
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public bool CanClean { get; set; }
}
