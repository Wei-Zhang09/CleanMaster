using System.Diagnostics;
using System.IO;

namespace CleanMaster.Services;

public class PrivacyCleanerService
{
    public List<PrivacyItem> Scan()
    {
        var items = new List<PrivacyItem>();

        // Recent documents
        items.Add(new PrivacyItem
        {
            Name = "最近文档记录",
            Path = Environment.GetFolderPath(Environment.SpecialFolder.Recent),
            Type = "RecentDocs",
            Description = "Windows 最近打开的文件记录",
            CanClean = true
        });

        // Windows Explorer recent
        var explorerRecent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent");
        if (Directory.Exists(explorerRecent))
        {
            items.Add(new PrivacyItem
            {
                Name = "资源管理器历史",
                Path = explorerRecent,
                Type = "ExplorerRecent",
                Description = "资源管理器最近访问记录",
                CanClean = true
            });
        }

        // Thumbnail cache (file-pattern targeted)
        var thumbCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
        if (Directory.Exists(thumbCache))
        {
            items.Add(new PrivacyItem
            {
                Name = "缩略图缓存",
                Path = thumbCache,
                Type = "ThumbCache",
                Description = "文件缩略图缓存，删除后会自动重建",
                CanClean = true
            });
        }

        // Windows Search history
        var searchHistory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\ConnectedSearch");
        if (Directory.Exists(searchHistory))
        {
            items.Add(new PrivacyItem
            {
                Name = "搜索历史",
                Path = searchHistory,
                Type = "SearchHistory",
                Description = "Windows 搜索历史记录",
                CanClean = true
            });
        }

        // Edge browsing history (cache only, not bookmarks)
        var edgeCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data\Default\Cache");
        if (Directory.Exists(edgeCache))
        {
            items.Add(new PrivacyItem
            {
                Name = "Edge 浏览缓存",
                Path = edgeCache,
                Type = "BrowserCache",
                Description = "Edge 浏览器缓存，不影响书签和密码",
                CanClean = true
            });
        }

        // Chrome browsing history (cache only)
        var chromeCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Default\Cache");
        if (Directory.Exists(chromeCache))
        {
            items.Add(new PrivacyItem
            {
                Name = "Chrome 浏览缓存",
                Path = chromeCache,
                Type = "BrowserCache",
                Description = "Chrome 浏览器缓存，不影响书签和密码",
                CanClean = true
            });
        }

        // DNS Cache
        items.Add(new PrivacyItem
        {
            Name = "DNS 缓存",
            Path = "",
            Type = "DnsCache",
            Description = "DNS 解析缓存，清除后需重新解析域名",
            CanClean = true
        });

        // Windows Error Reporting
        var werPath = @"C:\ProgramData\Microsoft\Windows\WER";
        if (Directory.Exists(werPath))
        {
            items.Add(new PrivacyItem
            {
                Name = "错误报告",
                Path = werPath,
                Type = "ErrorReporting",
                Description = "Windows 错误报告数据",
                CanClean = true
            });
        }

        return items;
    }

    public bool Clean(PrivacyItem item)
    {
        try
        {
            switch (item.Type)
            {
                case "DnsCache":
                    return FlushDns();
                case "ThumbCache":
                    return DeleteFilesByPattern(item.Path, "thumbcache_*.db", "iconcache_*.db");
                default:
                    if (!string.IsNullOrEmpty(item.Path) && Directory.Exists(item.Path))
                    {
                        return DeleteAllInDirectory(item.Path);
                    }
                    return false;
            }
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("Clean", ex);
            return false;
        }
    }

    /// <summary>
    /// Runs ipconfig /flushdns and waits for exit so the UI status reflects actual completion.
    /// </summary>
    private static bool FlushDns()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("FlushDns", ex);
            return false;
        }
    }

    /// <summary>
    /// Deletes files matching the given patterns inside a directory (non-recursive).
    /// Clears read-only/system attributes first.
    /// </summary>
    private static bool DeleteFilesByPattern(string directory, params string[] patterns)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;

        bool anyDeleted = false;
        foreach (var pattern in patterns)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, pattern,
                    new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        anyDeleted = true;
                    }
                    catch (Exception ex) { Debug.WriteLine($"DeleteFilesByPattern: {file}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("DeleteFilesByPattern", ex); }
        }
        return anyDeleted;
    }

    /// <summary>
    /// Deletes all files (recursively) inside a directory but leaves the directory itself.
    /// Clears read-only/system attributes before deletion.
    /// </summary>
    private static bool DeleteAllInDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;

        bool anyDeleted = false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    anyDeleted = true;
                }
                catch (Exception ex) { Debug.WriteLine($"DeleteAllInDirectory: {file}: {ex.Message}"); }
            }

            // Remove now-empty subdirectories (deepest first)
            var subdirs = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length);
            foreach (var dir in subdirs)
            {
                try { Directory.Delete(dir, false); }
                catch (Exception ex) { Debug.WriteLine($"DeleteAllInDirectory: dir {dir}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { CleanMaster.App.LogError("DeleteAllInDirectory", ex); }

        return anyDeleted;
    }
}

public class PrivacyItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public bool CanClean { get; set; }
}
