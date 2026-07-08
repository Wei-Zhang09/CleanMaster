using System.IO;

namespace CleanMaster.Services;

public class LargeFolderItem
{
    public string FolderPath { get; set; } = "";
    public string FolderName { get; set; } = "";
    public long TotalSize { get; set; }
    public int FileCount { get; set; }
    public string Description { get; set; } = "";

    public string SizeText => TotalSize switch
    {
        >= 1_073_741_824 => $"{TotalSize / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{TotalSize / 1_048_576.0:F1} MB",
        _ => $"{TotalSize / 1024.0:F1} KB"
    };
}

public class EmptyFolderItem
{
    public string FolderPath { get; set; } = "";
    public string FolderName { get; set; } = "";
    public bool IsSelected { get; set; } = true;
}

public class FolderScanService
{
    public event Action<string>? ProgressChanged;

    private static readonly string[] SkipDirs = new[]
    {
        @"C:\Windows", @"C:\$Recycle.Bin", @"C:\System Volume Information",
        @"C:\ProgramData\Microsoft", @"C:\Program Files", @"C:\Program Files (x86)"
    };

    public async Task<List<LargeFolderItem>> ScanLargeFoldersAsync(
        string drive = @"C:\",
        long minSizeBytes = 500 * 1024 * 1024,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<LargeFolderItem>();

            // Scan top-level directories
            foreach (var dir in GetDirectoriesSafe(drive))
            {
                ct.ThrowIfCancellationRequested();
                if (SkipDirs.Any(s => dir.Equals(s, StringComparison.OrdinalIgnoreCase))) continue;

                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if ((dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) continue;
                    if ((dirInfo.Attributes & FileAttributes.System) == FileAttributes.System) continue;

                    ProgressChanged?.Invoke(dirInfo.Name);

                    var (size, count) = GetDirectoryInfo(dir);
                    if (size >= minSizeBytes)
                    {
                        results.Add(new LargeFolderItem
                        {
                            FolderPath = dir,
                            FolderName = dirInfo.Name,
                            TotalSize = size,
                            FileCount = count,
                            Description = GetFolderDescription(dir)
                        });
                    }
                }
                catch { }
            }

            // Also scan one level deeper for key directories
            foreach (var dir in GetDirectoriesSafe(drive))
            {
                if (SkipDirs.Any(s => dir.Equals(s, StringComparison.OrdinalIgnoreCase))) continue;
                foreach (var subDir in GetDirectoriesSafe(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var subDirInfo = new DirectoryInfo(subDir);
                        if ((subDirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) continue;

                        ProgressChanged?.Invoke(subDirInfo.Name);
                        var (size, count) = GetDirectoryInfo(subDir);
                        if (size >= minSizeBytes && !results.Any(r => r.FolderPath == subDir))
                        {
                            results.Add(new LargeFolderItem
                            {
                                FolderPath = subDir,
                                FolderName = subDirInfo.Name,
                                TotalSize = size,
                                FileCount = count,
                                Description = GetFolderDescription(subDir)
                            });
                        }
                    }
                    catch { }
                }
            }

            return results.OrderByDescending(r => r.TotalSize).Take(50).ToList();
        }, ct);
    }

    public async Task<List<EmptyFolderItem>> ScanEmptyFoldersAsync(string drive = @"C:\", CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<EmptyFolderItem>();

            foreach (var dir in GetDirectoriesSafe(drive))
            {
                ct.ThrowIfCancellationRequested();
                if (SkipDirs.Any(s => dir.Equals(s, StringComparison.OrdinalIgnoreCase))) continue;

                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if ((dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden) continue;
                    if ((dirInfo.Attributes & FileAttributes.System) == FileAttributes.System) continue;

                    ProgressChanged?.Invoke(dirInfo.Name);

                    if (IsEmptyDirectory(dir))
                    {
                        results.Add(new EmptyFolderItem
                        {
                            FolderPath = dir,
                            FolderName = dirInfo.Name
                        });
                    }
                }
                catch { }
            }

            return results.OrderBy(r => r.FolderPath).ToList();
        }, ct);
    }

    public int DeleteEmptyFolders(List<EmptyFolderItem> folders)
    {
        int deleted = 0;
        foreach (var folder in folders.Where(f => f.IsSelected))
        {
            try
            {
                if (Directory.Exists(folder.FolderPath) && IsEmptyDirectory(folder.FolderPath))
                {
                    Directory.Delete(folder.FolderPath);
                    deleted++;
                }
            }
            catch { }
        }
        return deleted;
    }

    private static (long size, int count) GetDirectoryInfo(string path)
    {
        long size = 0;
        int count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }))
            {
                try { size += new FileInfo(file).Length; count++; } catch { }
            }
        }
        catch { }
        return (size, count);
    }

    private static bool IsEmptyDirectory(string path)
    {
        try
        {
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch { return false; }
    }

    private static IEnumerable<string> GetDirectoriesSafe(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Enumerable.Empty<string>(); }
    }

    private static string GetFolderDescription(string path)
    {
        var lower = path.ToLower();
        if (lower.Contains("temp")) return "临时文件夹";
        if (lower.Contains("cache")) return "缓存文件夹";
        if (lower.Contains("download")) return "下载文件夹";
        if (lower.Contains("appdata")) return "应用程序数据";
        if (lower.Contains("programdata")) return "程序数据";
        if (lower.Contains("windows")) return "系统文件夹";
        return "用户文件夹";
    }
}
