using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using CleanMaster.Models;

namespace CleanMaster.Services;

public class DuplicateGroup
{
    public string Hash { get; set; } = "";
    public List<DuplicateFile> Files { get; set; } = new();
    public long FileSize => Files.FirstOrDefault()?.SizeBytes ?? 0;
    public long WastedSpace => FileSize * Math.Max(0, Files.Count - 1);

    public string WastedSpaceText => ByteSizeFormatter.Format(WastedSpace);
    public string FileSizeText => ByteSizeFormatter.Format(FileSize);
}

public class DuplicateFile
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsSelected { get; set; }
    public bool IsKept { get; set; }

    public string SizeText => ByteSizeFormatter.Format(SizeBytes);
}

public class DuplicateScanProgress
{
    public string CurrentFile { get; set; } = "";
    public int FilesScanned { get; set; }
    public long BytesProcessed { get; set; }

    /// <summary>整个重复扫描阶段的进度（0-100），供 UI 映射进度条。</summary>
    public double Percent { get; set; }
}

public class DuplicateService
{
    public event Action<DuplicateScanProgress>? ProgressChanged;

    // Only scan user directories for speed
    private static readonly string[] ScanPaths = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music"),
    };

    private static readonly string[] ExcludedExtensions = new[]
    {
        ".sys", ".dll", ".drv", ".inf", ".ini", ".log", ".tmp"
    };

    /// <summary>快速指纹读取的头部字节数：同大小文件先比前 8KB，避免对大文件做全量哈希。</summary>
    private const int QuickHashBytes = 8 * 1024;

    /// <summary>递归深度上限，防止异常深的目录树拖慢扫描。</summary>
    private const int MaxScanDepth = 16;

    // IgnoreInaccessible: 无权限目录直接跳过，不再逐个 try-catch。
    // AttributesToSkip 含 ReparsePoint: 跳过 junction/符号链接，避免目录环导致无限递归。
    private static readonly EnumerationOptions FileEnumOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchType = MatchType.Simple,
    };

    private static readonly EnumerationOptions DirEnumOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        MatchType = MatchType.Simple,
    };

    public async Task<List<DuplicateGroup>> FindDuplicatesAsync(
        string[]? scanPaths = null,
        long minFileSize = 1024 * 1024, // 1MB default
        CancellationToken ct = default)
    {
        var pathsToScan = scanPaths ?? ScanPaths;

        return await Task.Run(() =>
        {
            // ---- Phase 1: 枚举并按大小分组（0-30%） ----
            var sizeGroups = new Dictionary<long, List<string>>();
            int filesScanned = 0;
            long bytesSeen = 0;

            for (int pathIndex = 0; pathIndex < pathsToScan.Length; pathIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var scanPath = pathsToScan[pathIndex];
                if (!Directory.Exists(scanPath)) continue;

                double pathBase = (double)pathIndex / Math.Max(1, pathsToScan.Length) * 30;
                double pathSpan = 30.0 / Math.Max(1, pathsToScan.Length);
                int filesInPath = 0;

                foreach (var file in EnumerateFilesSafe(scanPath))
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length < minFileSize) continue;
                        if (ExcludedExtensions.Contains(fi.Extension.ToLowerInvariant())) continue;

                        if (!sizeGroups.TryGetValue(fi.Length, out var sameSize))
                        {
                            sameSize = new List<string>();
                            sizeGroups[fi.Length] = sameSize;
                        }
                        sameSize.Add(file);

                        filesScanned++;
                        filesInPath++;
                        bytesSeen += fi.Length;
                        if (filesInPath % 100 == 0)
                        {
                            // 在当前路径的区间内渐进逼近（指数饱和），避免整段目录扫描时进度条不动。
                            double fraction = 1.0 - Math.Exp(-filesInPath / 500.0);
                            Report(pathBase + pathSpan * fraction * 0.95,
                                   fi.Name, filesScanned, bytesSeen);
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"FindDuplicatesAsync: {ex.Message}"); }
                }

                Report(pathBase + pathSpan, "", filesScanned, bytesSeen);
            }

            // ---- Phase 2: 前 8KB 快速指纹（30-60%） ----
            var sizeCandidates = sizeGroups.Where(kv => kv.Value.Count > 1).ToList();
            var quickGroups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            int quickDone = 0;

            foreach (var group in sizeCandidates)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var filePath in group.Value)
                {
                    ct.ThrowIfCancellationRequested();

                    var quick = ComputeQuickHash(filePath);
                    if (quick == null) continue;

                    // 大小 + 头部指纹共同作为键，进一步缩小候选集
                    var key = group.Key.ToString() + ":" + quick;
                    if (!quickGroups.TryGetValue(key, out var bucket))
                    {
                        bucket = new List<string>();
                        quickGroups[key] = bucket;
                    }
                    bucket.Add(filePath);
                }

                quickDone++;
                if (quickDone % 10 == 0 || quickDone == sizeCandidates.Count)
                {
                    Report(30 + (double)quickDone / Math.Max(1, sizeCandidates.Count) * 30,
                           "", filesScanned, bytesSeen);
                }
            }

            // ---- Phase 3: 全量 SHA256 确认，仅对快速指纹碰撞的组并行计算（60-95%） ----
            var hashGroups = new Dictionary<string, List<DuplicateFile>>(StringComparer.Ordinal);
            var quickCandidates = quickGroups.Where(kv => kv.Value.Count > 1).ToList();
            int hashedGroups = 0;
            var sync = new object();

            Parallel.ForEach(
                quickCandidates,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
                    CancellationToken = ct
                },
                group =>
                {
                    var local = new List<(string Hash, DuplicateFile File)>(group.Value.Count);

                    foreach (var filePath in group.Value)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var hash = ComputeFileHash(filePath);
                            if (hash == null) continue;

                            var fi = new FileInfo(filePath);
                            local.Add((hash, new DuplicateFile
                            {
                                FileName = fi.Name,
                                FullPath = fi.FullName,
                                SizeBytes = fi.Length,
                                LastModified = fi.LastWriteTime
                            }));
                        }
                        catch (Exception ex) { Debug.WriteLine($"FindDuplicatesAsync: {ex.Message}"); }
                    }

                    lock (sync)
                    {
                        foreach (var (hash, file) in local)
                        {
                            if (!hashGroups.TryGetValue(hash, out var bucket))
                            {
                                bucket = new List<DuplicateFile>();
                                hashGroups[hash] = bucket;
                            }
                            bucket.Add(file);
                        }

                        hashedGroups++;
                        if (hashedGroups % 5 == 0 || hashedGroups == quickCandidates.Count)
                        {
                            Report(60 + (double)hashedGroups / Math.Max(1, quickCandidates.Count) * 35,
                                   "", filesScanned, bytesSeen);
                        }
                    }
                });

            // ---- Phase 4: 构建结果（95-100%） ----
            var result = hashGroups
                .Where(kv => kv.Value.Count > 1)
                .Select(kv => new DuplicateGroup
                {
                    Hash = kv.Key,
                    Files = kv.Value.OrderBy(f => f.LastModified).ToList()
                })
                .OrderByDescending(g => g.WastedSpace)
                .ToList();

            // Mark first file in each group as "kept"
            foreach (var group in result)
            {
                if (group.Files.Count > 0)
                {
                    group.Files[0].IsKept = true;
                    group.Files[0].IsSelected = false;
                    for (int i = 1; i < group.Files.Count; i++)
                    {
                        group.Files[i].IsSelected = true;
                    }
                }
            }

            Report(100, "", filesScanned, bytesSeen);
            return result;
        }, ct);
    }

    private void Report(double percent, string currentFile, int filesScanned, long bytesProcessed)
    {
        ProgressChanged?.Invoke(new DuplicateScanProgress
        {
            CurrentFile = currentFile,
            FilesScanned = filesScanned,
            BytesProcessed = bytesProcessed,
            Percent = Math.Clamp(percent, 0, 100)
        });
    }

    /// <summary>读取文件头部 8KB 计算快速指纹。读取失败返回 null（该文件不参与重复判定）。</summary>
    private static string? ComputeQuickHash(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                QuickHashBytes, FileOptions.SequentialScan);

            var buffer = new byte[QuickHashBytes];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read <= 0) break;
                total += read;
            }

            if (total == 0) return null;
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, total)));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ComputeQuickHash: {ex.Message}");
            return null;
        }
    }

    private static string? ComputeFileHash(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            const long largeFileThreshold = 100 * 1024 * 1024; // 100MB
            if (stream.Length <= largeFileThreshold)
            {
                var hash = sha256.ComputeHash(stream);
                return Convert.ToHexString(hash);
            }

            // For large files, read in chunks to avoid memory pressure
            var buffer = new byte[81920]; // 80KB chunks
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return sha256.Hash != null ? Convert.ToHexString(sha256.Hash) : null;
        }
        catch (Exception ex)
        {
            App.LogError("ComputeFileHash", ex);
            return null;
        }
    }

    /// <summary>
    /// 安全递归枚举文件。使用 EnumerationOptions 跳过不可访问目录与 reparse point（junction/符号链接），
    /// 避免目录环导致无限递归；限制递归深度；跳过 . 开头的目录（.git/.cache 等）。
    /// </summary>
    private static IEnumerable<string> EnumerateFilesSafe(string path, int depth = 0)
    {
        if (depth > MaxScanDepth) yield break;

        // C# 不允许在 try-catch 中 yield return，这里手动推进枚举器兜住中途异常
        // （目录被删除、权限变化等），避免整个扫描中断。
        using (var fileEnum = OpenEnumerator(() => Directory.EnumerateFiles(path, "*", FileEnumOptions)))
        {
            while (true)
            {
                string current;
                try
                {
                    if (!fileEnum.MoveNext()) break;
                    current = fileEnum.Current;
                }
                catch (Exception ex) { App.LogError("EnumerateFilesSafe", ex); break; }

                yield return current;
            }
        }

        using (var dirEnum = OpenEnumerator(() => Directory.EnumerateDirectories(path, "*", DirEnumOptions)))
        {
            while (true)
            {
                string dir;
                try
                {
                    if (!dirEnum.MoveNext()) break;
                    dir = dirEnum.Current;
                }
                catch (Exception ex) { App.LogError("EnumerateFilesSafe", ex); break; }

                // 跳过 .git / .cache / .vscode 等工具目录
                if (Path.GetFileName(dir).StartsWith(".")) continue;

                foreach (var file in EnumerateFilesSafe(dir, depth + 1))
                {
                    yield return file;
                }
            }
        }
    }

    private static IEnumerator<string> OpenEnumerator(Func<IEnumerable<string>> factory)
    {
        try { return factory().GetEnumerator(); }
        catch (Exception ex)
        {
            App.LogError("EnumerateFilesSafe", ex);
            return Enumerable.Empty<string>().GetEnumerator();
        }
    }
}
