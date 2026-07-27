using System.Diagnostics;
using System.IO;
using CleanMaster.Models;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

public class CleanProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string CurrentFile { get; set; } = "";
    public string CurrentPath { get; set; } = "";
    public double Percent => Total > 0 ? (double)Current / Total * 100 : 0;
}

public class CleanService : ICleanService
{
    public event Action<string>? ProgressChanged;
    public event Action<CleanProgress>? ProgressUpdated;

    public async Task<CleanResult> CleanAsync(List<ScanCategoryResult> categories, CancellationToken ct = default)
    {
        var result = new CleanResult();
        var allItems = categories.Where(c => c.IsSelected)
            .SelectMany(c => c.Items.Where(i => i.IsSelected))
            .ToList();
        var total = allItems.Count;
        var current = 0;

        await Task.Run(() =>
        {
            foreach (var item in allItems)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                try
                {
                    ProgressChanged?.Invoke(item.Name);
                    ProgressUpdated?.Invoke(new CleanProgress { Current = current, Total = total, CurrentFile = item.Name, CurrentPath = item.FullPath });

                    if (item.IsDirectory)
                    {
                        var freed = DeleteDirectoryAndAccount(item.FullPath, item.SizeBytes);
                        if (freed > 0)
                        {
                            result.BytesFreed += freed;
                            result.FoldersDeleted++;
                            result.DeletedItems.Add(item);
                        }
                        else if (freed == 0 && !Directory.Exists(item.FullPath))
                        {
                            // Directory fully removed but size unknown — count as deleted with scanned size
                            result.BytesFreed += Math.Max(0, item.SizeBytes);
                            result.FoldersDeleted++;
                            result.DeletedItems.Add(item);
                        }
                    }
                    else
                    {
                        if (!File.Exists(item.FullPath))
                        {
                            // File is already gone — surface as error so user knows the
                            // cleanup didn't actually remove anything for this item.
                            result.Errors.Add($"{item.Name}: 文件不存在，无法删除");
                            continue;
                        }
                        var size = DeleteFileWithSize(item.FullPath);
                        if (size > 0)
                        {
                            result.BytesFreed += size;
                            result.FilesDeleted++;
                            result.DeletedItems.Add(item);
                        }
                        else
                        {
                            result.Errors.Add($"{item.Name}: 删除失败");
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{item.Name}: {ex.Message}");
                }
            }
        }, ct);

        return result;
    }

    public async Task<CleanResult> CleanLargeFilesAsync(List<LargeFileItem> files, CancellationToken ct = default)
    {
        var result = new CleanResult();
        var selected = files.Where(f => f.IsSelected).ToList();
        var total = selected.Count;
        var current = 0;

        await Task.Run(() =>
        {
            foreach (var file in selected)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                try
                {
                    ProgressChanged?.Invoke(file.FileName);
                    ProgressUpdated?.Invoke(new CleanProgress { Current = current, Total = total, CurrentFile = file.FileName, CurrentPath = file.FullPath });

                    // Safety guard: refuse to delete danger-level files automatically
                    if (string.Equals(file.SafetyHint, "danger", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Errors.Add($"{file.FileName}: 安全级别为 danger，已跳过删除（请手动处理）");
                        continue;
                    }

                    var size = DeleteFileWithSize(file.FullPath);
                    if (size > 0)
                    {
                        result.BytesFreed += size;
                        result.FilesDeleted++;
                    }
                    else if (!File.Exists(file.FullPath))
                    {
                        result.BytesFreed += Math.Max(0, file.SizeBytes);
                        result.FilesDeleted++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{file.FileName}: {ex.Message}");
                }
            }
        }, ct);

        return result;
    }

    /// <summary>
    /// Deletes a file and returns its size in bytes prior to deletion.
    /// Returns 0 if the file did not exist or could not be sized.
    /// </summary>
    private static long DeleteFileWithSize(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return 0;
            long size;
            try
            {
                size = new FileInfo(fullPath).Length;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteFileWithSize: cannot size {fullPath}: {ex.Message}");
                size = 0;
            }

            // Clear read-only/system attributes before delete
            try
            {
                File.SetAttributes(fullPath, FileAttributes.Normal);
            }
            catch (Exception ex) { Debug.WriteLine($"DeleteFileWithSize: SetAttributes failed: {ex.Message}"); }

            File.Delete(fullPath);
            return size;
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("DeleteFileWithSize", ex);
            return 0;
        }
    }

    /// <summary>
    /// Recursively deletes a directory, clearing file attributes along the way.
    /// Returns bytes freed (computed from delete successes). Fallback to scannedSize if provided.
    /// </summary>
    private static long DeleteDirectoryAndAccount(string path, long scannedSizeFallback)
    {
        long freed = 0;

        try
        {
            if (!Directory.Exists(path)) return 0;

            // First pass: clear attributes on all files and capture sizes
            var files = Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            }).ToList();

            long deletedFilesBytes = 0;
            foreach (var file in files)
            {
                try
                {
                    var fi = new FileInfo(file);
                    long sz = fi.Length;
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                    File.Delete(file);
                    deletedFilesBytes += sz;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DeleteDirectoryAndAccount: file {file}: {ex.Message}");
                }
            }

            // Second pass: remove subdirectories deepest-first (recursive=true allows framework to clean remaining)
            var dirs = Directory.EnumerateDirectories(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            })
            .OrderByDescending(d => d.Length)
            .ToList();

            foreach (var dir in dirs)
            {
                try { Directory.Delete(dir, true); }
                catch (Exception ex) { Debug.WriteLine($"DeleteDirectoryAndAccount: dir {dir}: {ex.Message}"); }
            }

            // Finally remove the root directory itself
            bool rootRemoved = false;
            try
            {
                Directory.Delete(path, true);
                rootRemoved = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DeleteDirectoryAndAccount: root {path}: {ex.Message}");
            }

            if (rootRemoved)
            {
                freed = deletedFilesBytes;
            }
            else if (deletedFilesBytes > 0)
            {
                // Partial cleanup: only count bytes from files we actually removed
                freed = deletedFilesBytes;
            }

            // If we have no measured bytes but root is gone, fall back to scanned size
            if (freed == 0 && rootRemoved && scannedSizeFallback > 0)
                freed = scannedSizeFallback;
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("DeleteDirectoryAndAccount", ex);
        }

        return freed;
    }

    [Obsolete("Kept for backward-compat; replaced by DeleteDirectoryAndAccount")]
    private static void DeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;

            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); } catch (Exception ex) { Debug.WriteLine($"DeleteDirectory: {ex.Message}"); }
            }

            foreach (var dir in Directory.EnumerateDirectories(path, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }).OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(dir, true); } catch (Exception ex) { Debug.WriteLine($"DeleteDirectory: {ex.Message}"); }
            }

            Directory.Delete(path, true);
        }
        catch (Exception ex) { CleanMaster.App.LogError("DeleteDirectory", ex); }
    }
}
