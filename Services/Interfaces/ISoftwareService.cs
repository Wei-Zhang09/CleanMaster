namespace CleanMaster.Services.Interfaces;

public interface ISoftwareService
{
    List<InstalledSoftware> GetInstalledSoftware();
    List<StartupItem> GetStartupItems();
    /// <summary>Lazy icon path resolution for a startup item (called from UI thread).</summary>
    string GetStartupItemIconPath(StartupItem item);
    /// <summary>启动项对应程序是否有数字签名：Signed / Unsigned / Unknown（延迟解析，已缓存）。</summary>
    string GetStartupItemSignature(StartupItem item);
    void UninstallSoftware(InstalledSoftware software);
    Task<UninstallAttempt> UninstallSoftwareAsync(InstalledSoftware software, CancellationToken ct = default, TimeSpan? descendantWaitTimeout = null);
    /// <summary>回读卸载登记项：true=仍登记（软件还在），false=已消失，null=无法验证。</summary>
    bool? TryGetRegistrationState(InstalledSoftware software);
    bool DisableStartupItem(StartupItem item);
    bool EnableStartupItem(StartupItem item);
    UninstallResult ScanLeftovers(InstalledSoftware software);
    void CleanupLeftovers(UninstallResult result);
}
