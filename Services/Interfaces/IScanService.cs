using CleanMaster.Models;

namespace CleanMaster.Services.Interfaces;

public interface IScanService
{
    event Action<ScanProgress>? ProgressChanged;
    event Action<ScanCategoryResult>? CategoryScanned;
    event Action<string>? AccessDenied;

    Task<List<ScanCategoryResult>> ScanAllAsync(CancellationToken ct = default);
    DiskInfo GetDiskInfo(string drive);
    List<DiskInfo> GetAllDisks();
    Task<List<LargeFileItem>> FindLargeFilesAsync(string drive, long minSizeBytes, CancellationToken ct = default);
}
