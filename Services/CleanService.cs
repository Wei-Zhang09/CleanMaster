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
    public double Percent => Total > 0 ? (double)Current / Total * 100 : 0;
}

public class CleanService : ICleanService
{
    public event Action<string>? ProgressChanged;
    public event Action<CleanProgress>? ProgressUpdated;

    public async Task<CleanResult> CleanAsync(List<ScanCategoryResult> categories, CancellationToken ct = default)
    {
        var result = new CleanResult();
        var allItems = categories.Where(c => c.IsSelected).SelectMany(c => c.Items.Where(i => i.IsSelected)).ToList();
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
                    ProgressUpdated?.Invoke(new CleanProgress { Current = current, Total = total, CurrentFile = item.Name });

                    if (item.IsDirectory)
                    {
                        var sizeBefore = FileSystemUtils.GetDirectorySize(item.FullPath);
                        DeleteDirectory(item.FullPath);
                        var sizeAfter = Directory.Exists(item.FullPath) ? FileSystemUtils.GetDirectorySize(item.FullPath) : 0;
                        var freed = sizeBefore - sizeAfter;
                        if (freed > 0)
                        {
                            result.BytesFreed += freed;
                            result.FoldersDeleted++;
                            result.DeletedItems.Add(item);
                        }
                    }
                    else
                    {
                        var size = new FileInfo(item.FullPath).Length;
                        File.Delete(item.FullPath);
                        result.BytesFreed += size;
                        result.FilesDeleted++;
                        result.DeletedItems.Add(item);
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
        var total = files.Count(f => f.IsSelected);
        var current = 0;

        await Task.Run(() =>
        {
            foreach (var file in files.Where(f => f.IsSelected))
            {
                ct.ThrowIfCancellationRequested();
                current++;

                try
                {
                    ProgressChanged?.Invoke(file.FileName);
                    ProgressUpdated?.Invoke(new CleanProgress { Current = current, Total = total, CurrentFile = file.FileName });
                    File.Delete(file.FullPath);
                    result.BytesFreed += file.SizeBytes;
                    result.FilesDeleted++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{file.FileName}: {ex.Message}");
                }
            }
        }, ct);

        return result;
    }

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
                try { Directory.Delete(dir, false); } catch (Exception ex) { Debug.WriteLine($"DeleteDirectory: {ex.Message}"); }
            }

            Directory.Delete(path, false);
        }
        catch (Exception ex) { CleanMaster.App.LogError("DeleteDirectory", ex); }
    }
}
