namespace CleanMaster.Services.Interfaces;

public interface ISoftwareService
{
    List<InstalledSoftware> GetInstalledSoftware();
    List<StartupItem> GetStartupItems();
    void UninstallSoftware(InstalledSoftware software);
    bool DisableStartupItem(StartupItem item);
    UninstallResult ScanLeftovers(InstalledSoftware software);
    void CleanupLeftovers(UninstallResult result);
}
