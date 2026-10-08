using CleanMaster.Models;

namespace CleanMaster.Services.Interfaces;

public interface ICleanService
{
    Task<CleanResult> CleanAsync(
        List<ScanCategoryResult> categories,
        IProgress<CleanProgress>? progress = null,
        CancellationToken ct = default);

    Task<CleanResult> CleanLargeFilesAsync(
        List<LargeFileItem> files,
        IProgress<CleanProgress>? progress = null,
        CancellationToken ct = default);
}
