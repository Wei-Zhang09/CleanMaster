using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using CleanMaster.Models;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

public class CleanProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string CurrentFile { get; set; } = "";
    public string CurrentPath { get; set; } = "";
    /// <summary>当前操作描述（如"正在清理: xxx"），承载原 ProgressChanged 的字符串。</summary>
    public string Message { get; set; } = "";
    public double Percent => Total > 0 ? (double)Current / Total * 100 : 0;
}

public class CleanService : ICleanService
{
    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI = 0x2;
    private const uint SHERB_NOSOUND = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public async Task<CleanResult> CleanAsync(
        List<ScanCategoryResult> categories,
        IProgress<CleanProgress>? progress = null,
        CancellationToken ct = default)
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
                    progress?.Report(new CleanProgress { Current = current, Total = total, CurrentFile = item.Name, CurrentPath = item.FullPath, Message = item.Name });

                    // 回收站：目录本身受系统保护无法用 Directory.Delete，改走 SHEmptyRecycleBin。
                    if (item.Category == CleanCategory.RecycleBin)
                    {
                        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                        if (hr == 0) // S_OK
                        {
                            result.BytesFreed += Math.Max(0, item.SizeBytes);
                            result.FoldersDeleted++;
                            result.DeletedItems.Add(item);
                            result.DeletedPaths.Add(item.FullPath);
                        }
                        else
                        {
                            result.Errors.Add($"{item.Name}: 清空回收站失败 (HRESULT 0x{hr:X8})");
                        }
                        continue;
                    }

                    // PathGuard：删除前最后一道防线
                    if (!PathGuard.IsDeletable(item.FullPath, out var guardReason))
                    {
                        result.Errors.Add($"{item.Name}: 已拦截 ({guardReason})");
                        continue;
                    }

                    if (item.IsDirectory)
                    {
                        // 目录本就不存在：与文件分支一致，报"目录不存在"而非静默算成功。
                        if (!Directory.Exists(item.FullPath))
                        {
                            result.Errors.Add($"{item.Name}: 目录不存在，无法删除");
                            continue;
                        }

                        var freed = DeleteDirectoryAndAccount(item.FullPath, item.SizeBytes);
                        var stillExists = Directory.Exists(item.FullPath);

                        if (freed > 0 || !stillExists)
                        {
                            result.BytesFreed += freed > 0 ? freed : Math.Max(0, item.SizeBytes);
                            result.FoldersDeleted++;
                            result.DeletedItems.Add(item);
                            result.DeletedPaths.Add(item.FullPath);
                            if (stillExists)
                                result.Warnings.Add($"{item.Name}: 部分文件被占用，未能完全删除");
                        }
                        else
                        {
                            result.Errors.Add($"{item.Name}: 目录未能删除（可能被占用或权限不足）");
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

                        if (TryDeleteFile(item.FullPath, out var size))
                        {
                            result.BytesFreed += size;
                            result.FilesDeleted++;
                            result.DeletedItems.Add(item);
                            result.DeletedPaths.Add(item.FullPath);
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

    public async Task<CleanResult> CleanLargeFilesAsync(
        List<LargeFileItem> files,
        IProgress<CleanProgress>? progress = null,
        CancellationToken ct = default)
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
                    progress?.Report(new CleanProgress { Current = current, Total = total, CurrentFile = file.FileName, CurrentPath = file.FullPath, Message = file.FileName });

                    // PathGuard：删除前最后一道防线
                    if (!PathGuard.IsDeletable(file.FullPath, out var guardReason))
                    {
                        result.Errors.Add($"{file.FileName}: 已拦截 ({guardReason})");
                        continue;
                    }

                    if (TryDeleteFile(file.FullPath, out var size))
                    {
                        result.BytesFreed += size;
                        result.FilesDeleted++;
                        result.DeletedPaths.Add(file.FullPath);
                    }
                    else if (!File.Exists(file.FullPath))
                    {
                        result.BytesFreed += Math.Max(0, file.SizeBytes);
                        result.FilesDeleted++;
                        result.DeletedPaths.Add(file.FullPath);
                    }
                    else
                    {
                        result.Errors.Add($"{file.FileName}: 删除失败");
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
    /// 删除文件，用 bool 返回值表示成功与否（0 字节文件删除成功也返回 true），
    /// 文件大小通过 out 参数返回。文件不存在返回 false。
    /// </summary>
    private static bool TryDeleteFile(string fullPath, out long size)
    {
        size = 0;
        try
        {
            var fi = new FileInfo(fullPath);
            if (!fi.Exists) return false;
            size = fi.Length;

            // 清除只读/系统属性
            try { fi.Attributes = FileAttributes.Normal; }
            catch (Exception ex) { Debug.WriteLine($"TryDeleteFile: SetAttributes failed: {ex.Message}"); }

            fi.Delete();
            return true;
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("TryDeleteFile", ex);
            return false;
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
}
