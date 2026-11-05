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

    public string SizeText => ByteSizeFormatter.Format(TotalSize);
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

                    var (size, count) = GetDirectoryInfo(dir, ct);
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
                        var (size, count) = GetDirectoryInfo(subDir, ct);
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

    // 目录枚举参数：跳过 reparse point，避免 junction 造成的目录环
    private static readonly EnumerationOptions FileEnumOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchType = MatchType.Simple
    };

    private static readonly EnumerationOptions DirEnumOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchType = MatchType.Simple
    };

    /// <summary>
    /// 统计目录大小与文件数。
    ///
    /// 为什么要手动递归而不是 RecurseSubdirectories=true：后者在枚举过程中遇到"文件/目录被删"
    /// 会从 MoveNext 抛出，整个子树的统计直接归零（表现为"某个文件夹大小莫名其妙是 0"）。
    /// 这里每个目录独立 try/catch，一个目录炸掉只丢它自己，兄弟目录照常统计。
    /// 同时支持取消：以前只在顶层目录之间检查 ct，扫描大目录时点"取消"要等很久。
    /// </summary>
    private static (long size, int count) GetDirectoryInfo(string path, CancellationToken ct)
    {
        long size = 0;
        int count = 0;
        Collect(path, ct, ref size, ref count);
        return (size, count);

        static void Collect(string dir, CancellationToken token, ref long totalSize, ref int totalCount)
        {
            token.ThrowIfCancellationRequested();

            var info = new DirectoryInfo(dir);

            // 当前目录的文件：DirectoryInfo.EnumerateFiles 返回的 FileInfo 已带大小，取 Length 无额外 stat
            try
            {
                foreach (var file in info.EnumerateFiles("*", FileEnumOptions))
                {
                    token.ThrowIfCancellationRequested();
                    try { totalSize += file.Length; totalCount++; }
                    catch (Exception ex) { Debug.WriteLine($"GetDirectoryInfo-file: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                // 枚举中途失败（目录被删/权限变化）：丢掉本目录剩余条目，但不要连累兄弟目录
                Debug.WriteLine($"GetDirectoryInfo-files: {ex.Message}");
            }

            DirectoryInfo[] subs;
            try { subs = info.GetDirectories("*", DirEnumOptions); }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetDirectoryInfo-dirs: {ex.Message}");
                return;
            }

            foreach (var sub in subs)
            {
                token.ThrowIfCancellationRequested();
                Collect(sub.FullName, token, ref totalSize, ref totalCount);
            }
        }
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
