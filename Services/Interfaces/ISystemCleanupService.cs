namespace CleanMaster.Services.Interfaces;

public interface ISystemCleanupService
{
    event Action<string>? ProgressChanged;

    Task<SystemCleanupResult> RunDismCleanupAsync(CancellationToken ct = default);
    Task<SystemCleanupResult> RunSfcScanAsync(CancellationToken ct = default);
    Task<SystemCleanupResult> FlushDnsCacheAsync(CancellationToken ct = default);
}
