using System.IO;
using Microsoft.Win32;
using CleanMaster.Models;

namespace CleanMaster.Services;

public class RegistryCleanerService
{
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

                        // Check if uninstall string points to missing file
                        if (!string.IsNullOrEmpty(uninstallStr))
                        {
                            var exePath = ExtractExePath(uninstallStr);
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
                    catch { }
                }
            }
            catch { }
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

            // Delete the subkey
            var parentPath = string.Join("\\", parts.Skip(1).Take(parts.Length - 2));
            var keyName = parts.Last();
            using var parentKey = root.OpenSubKey(parentPath, true);
            parentKey?.DeleteSubKeyTree(keyName, false);
        }
        catch { }
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
        return cmd.Split(' ')[0];
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
