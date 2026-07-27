namespace CleanMaster.Services.Interfaces;

public interface ISoftwareService
{
    List<InstalledSoftware> GetInstalledSoftware();
    List<StartupItem> GetStartupItems();
    /// <summary>Lazy icon path resolution for a startup item (called from UI thread).</summary>
    string GetStartupItemIconPath(StartupItem item);
    void UninstallSoftware(InstalledSoftware software);
    Task<(bool Started, int? ExitCode, string Message)> UninstallSoftwareAsync(InstalledSoftware software, CancellationToken ct = default);
    bool DisableStartupItem(StartupItem item);
    bool EnableStartupItem(StartupItem item);
    UninstallResult ScanLeftovers(InstalledSoftware software);
    void CleanupLeftovers(UninstallResult result);
}
