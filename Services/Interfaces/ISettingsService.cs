namespace CleanMaster.Services.Interfaces;

public interface ISettingsService
{
    AppSettings Get();
    void Save(AppSettings settings);
    Task SyncFromServerAsync();
    Task<string> GetWebsiteUrlAsync();
}
