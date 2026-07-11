using CleanMaster.Models;

namespace CleanMaster.Services.Interfaces;

public interface ICleanService
{
    event Action<string>? ProgressChanged;
    event Action<CleanProgress>? ProgressUpdated;

    Task<CleanResult> CleanAsync(List<ScanCategoryResult> categories, CancellationToken ct = default);
    Task<CleanResult> CleanLargeFilesAsync(List<LargeFileItem> files, CancellationToken ct = default);
}
