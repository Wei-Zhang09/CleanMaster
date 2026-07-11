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

    public string WastedSpaceText => FormatSize(WastedSpace);
    public string FileSizeText => FormatSize(FileSize);

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };
}

public class DuplicateFile
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsSelected { get; set; }
    public bool IsKept { get; set; }

    public string SizeText => SizeBytes switch
    {
        >= 1_073_741_824 => $"{SizeBytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{SizeBytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{SizeBytes / 1024.0:F1} KB",
        _ => $"{SizeBytes} B"
    };
}

public class DuplicateScanProgress
{
    public string CurrentFile { get; set; } = "";
    public int FilesScanned { get; set; }
    public long BytesProcessed { get; set; }
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

    public async Task<List<DuplicateGroup>> FindDuplicatesAsync(
        string[]? scanPaths = null,
        long minFileSize = 1024 * 1024, // 1MB default
        CancellationToken ct = default)
    {
        var pathsToScan = scanPaths ?? ScanPaths;

        return await Task.Run(() =>
        {
            // Phase 1: Group files by size
            var sizeGroups = new Dictionary<long, List<string>>();
            int filesScanned = 0;

            foreach (var scanPath in pathsToScan)
            {
                if (!Directory.Exists(scanPath)) continue;

                foreach (var file in EnumerateFilesSafe(scanPath))
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length < minFileSize) continue;
                        if (ExcludedExtensions.Contains(fi.Extension.ToLower())) continue;

                        if (!sizeGroups.ContainsKey(fi.Length))
                            sizeGroups[fi.Length] = new List<string>();
                        sizeGroups[fi.Length].Add(file);

                        filesScanned++;
                        if (filesScanned % 50 == 0)
                        {
                            ProgressChanged?.Invoke(new DuplicateScanProgress
                            {
                                CurrentFile = fi.Name,
                                FilesScanned = filesScanned,
                                BytesProcessed = fi.Length
                            });
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"FindDuplicatesAsync: {ex.Message}"); }
                }
            }

            // Phase 2: Hash files with same size
            var hashGroups = new Dictionary<string, List<DuplicateFile>>();
            int groupsToHash = sizeGroups.Count(kv => kv.Value.Count > 1);
            int groupsHashed = 0;

            foreach (var group in sizeGroups.Where(kv => kv.Value.Count > 1))
            {
                ct.ThrowIfCancellationRequested();

                foreach (var filePath in group.Value)
                {
                    try
                    {
                        var hash = ComputeFileHash(filePath);
                        if (hash == null) continue;

                        if (!hashGroups.ContainsKey(hash))
                            hashGroups[hash] = new List<DuplicateFile>();

                        var fi = new FileInfo(filePath);
                        hashGroups[hash].Add(new DuplicateFile
                        {
                            FileName = fi.Name,
                            FullPath = fi.FullName,
                            SizeBytes = fi.Length,
                            LastModified = fi.LastWriteTime
                        });
                    }
                    catch (Exception ex) { Debug.WriteLine($"FindDuplicatesAsync: {ex.Message}"); }
                }

                groupsHashed++;
                if (groupsHashed % 10 == 0)
                {
                    ProgressChanged?.Invoke(new DuplicateScanProgress
                    {
                        CurrentFile = $"Checking group {groupsHashed}/{groupsToHash}",
                        FilesScanned = filesScanned
                    });
                }
            }

            // Phase 3: Build result
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

            return result;
        }, ct);
    }

    public async Task<long> DeleteDuplicatesAsync(
        List<DuplicateGroup> groups, CancellationToken ct = default)
    {
        long freedBytes = 0;

        await Task.Run(() =>
        {
            foreach (var group in groups)
            {
                foreach (var file in group.Files.Where(f => f.IsSelected && !f.IsKept))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        File.Delete(file.FullPath);
                        freedBytes += file.SizeBytes;
                    }
                    catch (Exception ex) { CleanMaster.App.LogError("DeleteDuplicatesAsync", ex); }
                }
            }
        }, ct);

        return freedBytes;
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

    private static IEnumerable<string> EnumerateFilesSafe(string path)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(path); }
        catch (Exception ex) { CleanMaster.App.LogError("EnumerateFilesSafe", ex); yield break; }

        foreach (var file in files) yield return file;

        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(path); }
        catch (Exception ex) { CleanMaster.App.LogError("EnumerateFilesSafe", ex); yield break; }

        foreach (var dir in dirs)
        {
            // Skip hidden and system directories
            try
            {
                var dirInfo = new DirectoryInfo(dir);
                if (dirInfo.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                if (dirInfo.Name.StartsWith(".")) continue;
            }
            catch (Exception ex) { Debug.WriteLine($"EnumerateFilesSafe: {ex.Message}"); continue; }

            foreach (var file in EnumerateFilesSafe(dir))
            {
                yield return file;
            }
        }
    }
}
