using System.Diagnostics;
using System.IO;
using CleanMaster.Services.Interfaces;

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

public class FolderScanService : IFolderScanService
{
    public event Action<string>? ProgressChanged;

    /// <summary>
    /// Returns the set of protected directory roots for a given drive.
    /// Drive-relative so scanning D: no longer accidentally matches C:\ paths.
    /// </summary>
    private static HashSet<string> GetSkipDirs(string drive)
    {
        var root = Path.GetPathRoot(drive) ?? drive;
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(root, "Windows").TrimEnd('\\'),
            Path.Combine(root, "$Recycle.Bin"),
            Path.Combine(root, "System Volume Information"),
            Path.Combine(root, @"ProgramData\Microsoft"),
            Path.Combine(root, "Program Files").TrimEnd('\\'),
            Path.Combine(root, "Program Files (x86)")
        };
        return skip;
    }

    private static bool IsInSkipDir(string path, HashSet<string> skipDirs)
    {
        foreach (var skip in skipDirs)
        {
            if (path.Equals(skip, StringComparison.OrdinalIgnoreCase)) return true;
            if (path.StartsWith(skip + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public async Task<List<LargeFolderItem>> ScanLargeFoldersAsync(
        string drive = @"C:\",
        long minSizeBytes = 500 * 1024 * 1024,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<LargeFolderItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skipDirs = GetSkipDirs(drive);

            // Scan top-level directories first, then one level deeper.
            // Use seenPaths to avoid adding both parent and child if both qualify
            // (prefer the larger one; if equal, prefer parent).
            var topDirs = GetDirectoriesSafe(drive).ToList();

            foreach (var dir in topDirs)
            {
                ct.ThrowIfCancellationRequested();
                if (IsInSkipDir(dir, skipDirs)) continue;

                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                    if (dirInfo.Attributes.HasFlag(FileAttributes.System)) continue;

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
                        seenPaths.Add(dir);
                    }
                }
                catch (Exception ex) { CleanMaster.App.LogError("ScanLargeFoldersAsync", ex); }
            }

            // Second pass: one level deeper, but skip directories that are descendants
            // of already-listed ones (we don't want both parent and child on screen).
            foreach (var dir in topDirs)
            {
                if (IsInSkipDir(dir, skipDirs)) continue;
                foreach (var subDir in GetDirectoriesSafe(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var subDirInfo = new DirectoryInfo(subDir);
                        if (subDirInfo.Attributes.HasFlag(FileAttributes.Hidden)) continue;

                        // Skip if subDir is inside an already-listed top-level result
                        if (seenPaths.Any(parent => subDir.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        ProgressChanged?.Invoke(subDirInfo.Name);
                        var (size, count) = GetDirectoryInfo(subDir);
                        if (size >= minSizeBytes && !seenPaths.Contains(subDir))
                        {
                            results.Add(new LargeFolderItem
                            {
                                FolderPath = subDir,
                                FolderName = subDirInfo.Name,
                                TotalSize = size,
                                FileCount = count,
                                Description = GetFolderDescription(subDir)
                            });
                            seenPaths.Add(subDir);
                        }
                    }
                    catch (Exception ex) { CleanMaster.App.LogError("ScanLargeFoldersAsync", ex); }
                }
            }

            return results.OrderByDescending(r => r.TotalSize).Take(50).ToList();
        }, ct);
    }

    private static (long size, int count) GetDirectoryInfo(string path)
    {
        long size = 0;
        int count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }))
            {
                try { size += new FileInfo(file).Length; count++; } catch (Exception ex) { Debug.WriteLine($"GetDirectoryInfo: {ex.Message}"); }
            }
        }
        catch (Exception ex) { CleanMaster.App.LogError("GetDirectoryInfo", ex); }
        return (size, count);
    }

    private static IEnumerable<string> GetDirectoriesSafe(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch (Exception ex) { CleanMaster.App.LogError("GetDirectoriesSafe", ex); return Enumerable.Empty<string>(); }
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
