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

public class EmptyFolderItem
{
    public string FolderPath { get; set; } = "";
    public string FolderName { get; set; } = "";
    public bool IsSelected { get; set; } = true;
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

    public async Task<List<EmptyFolderItem>> ScanEmptyFoldersAsync(string drive = @"C:\", CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<EmptyFolderItem>();
            var skipDirs = GetSkipDirs(drive);

            // Walk recursively, but only report a directory if it AND all its descendants
            // are empty (i.e. it's a leaf-empty subtree). We collapse chains of empty folders
            // so the user sees the topmost empty directory instead of just the deepest one.
            foreach (var dir in GetDirectoriesSafe(drive))
            {
                ct.ThrowIfCancellationRequested();
                if (IsInSkipDir(dir, skipDirs)) continue;

                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                    if (dirInfo.Attributes.HasFlag(FileAttributes.System)) continue;

                    ProgressChanged?.Invoke(dirInfo.Name);

                    // Find topmost empty directories within this subtree
                    foreach (var empty in FindTopmostEmptyDirs(dir))
                    {
                        results.Add(new EmptyFolderItem
                        {
                            FolderPath = empty,
                            FolderName = Path.GetFileName(empty)
                        });
                    }
                }
                catch (Exception ex) { CleanMaster.App.LogError("ScanEmptyFoldersAsync", ex); }
            }

            return results.OrderBy(r => r.FolderPath).ToList();
        }, ct);
    }

    /// <summary>
    /// Returns directories that are empty (no files anywhere in their subtree).
    /// If a directory is empty, its ancestors are not returned (we return the topmost).
    /// </summary>
    private static IEnumerable<string> FindTopmostEmptyDirs(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!Directory.Exists(current)) continue;

            bool hasFiles = false;
            var subdirs = Array.Empty<string>();
            try
            {
                // Check direct file entries only (fast)
                if (Directory.EnumerateFiles(current, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }).Any())
                    hasFiles = true;
            }
            catch (Exception ex) { CleanMaster.App.LogError("FindTopmostEmptyDirs-files", ex); }

            if (!hasFiles)
            {
                // Whole subtree has no files. Verify directory exists and yield it.
                yield return current;
                continue; // don't descend into children — they'd all be empty too
            }

            try { subdirs = Directory.GetDirectories(current); }
            catch (Exception ex) { CleanMaster.App.LogError("FindTopmostEmptyDirs-dirs", ex); }

            foreach (var sd in subdirs)
            {
                try
                {
                    var info = new DirectoryInfo(sd);
                    if (info.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                    if (info.Attributes.HasFlag(FileAttributes.System)) continue;
                }
                catch { continue; }
                stack.Push(sd);
            }
        }
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
            catch (Exception ex) { CleanMaster.App.LogError("DeleteEmptyFolders", ex); }
        }

        // After deletion, attempt to remove now-empty parents (best-effort, no error reporting)
        var parentsToDelete = folders
            .Where(f => f.IsSelected)
            .Select(f => Path.GetDirectoryName(f.FolderPath))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var parent in parentsToDelete)
        {
            try
            {
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent) && IsEmptyDirectory(parent))
                {
                    // Only delete if parent is also under user folder (safety)
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (parent.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(parent);
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("DeleteEmptyFolders-parent", ex); }
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
                try { size += new FileInfo(file).Length; count++; } catch (Exception ex) { Debug.WriteLine($"GetDirectoryInfo: {ex.Message}"); }
            }
        }
        catch (Exception ex) { CleanMaster.App.LogError("GetDirectoryInfo", ex); }
        return (size, count);
    }

    private static bool IsEmptyDirectory(string path)
    {
        try
        {
            // Empty means: no files anywhere in the subtree AND no non-empty subdirectories
            return !Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }).Any();
        }
        catch (Exception ex) { CleanMaster.App.LogError("IsEmptyDirectory", ex); return false; }
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
