namespace CleanMaster.Services.Interfaces;

public interface IFolderScanService
{
    event Action<string>? ProgressChanged;

    Task<List<LargeFolderItem>> ScanLargeFoldersAsync(string drive, long minSizeBytes, CancellationToken ct = default);
}
