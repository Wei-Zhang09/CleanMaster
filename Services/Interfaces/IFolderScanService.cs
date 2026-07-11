namespace CleanMaster.Services.Interfaces;

public interface IFolderScanService
{
    event Action<string>? ProgressChanged;

    Task<List<LargeFolderItem>> ScanLargeFoldersAsync(string drive, long minSizeBytes, CancellationToken ct = default);
    Task<List<EmptyFolderItem>> ScanEmptyFoldersAsync(string drive, CancellationToken ct = default);
    int DeleteEmptyFolders(List<EmptyFolderItem> folders);
}
