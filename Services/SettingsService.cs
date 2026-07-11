using System.IO;
using System.Net.Http;
using System.Text.Json;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

public class AppSettings
{
    public string WebsiteUrl { get; set; } = "https://awe-software-production.up.railway.app";
    public string ApiBaseUrl { get; set; } = "https://awe-software-production.up.railway.app/api";
    public bool EnableRemoteSync { get; set; } = false;
    public string LicenseApiUrl { get; set; } = "https://awe-software-production.up.railway.app/api";
}

public class SettingsService : ISettingsService
{
    private readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CleanMaster");

    private readonly string SettingsFile;
    private AppSettings? _cached;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public SettingsService()
    {
        SettingsFile = Path.Combine(ConfigDir, "settings.json");
    }

    public AppSettings Get()
    {
        if (_cached != null) return _cached;

        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                _cached = JsonSerializer.Deserialize<AppSettings>(json);
                return _cached!;
            }
        }
        catch (Exception ex) { CleanMaster.App.LogError("SettingsService.Get", ex); }

        _cached = new AppSettings();
        return _cached;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            _cached = settings;
        }
        catch (Exception ex) { CleanMaster.App.LogError("SettingsService.Save", ex); }
    }

    public async Task<string> GetWebsiteUrlAsync()
    {
        try
        {
            var apiUrl = Get().ApiBaseUrl + "/settings/website-url";
            var response = await _httpClient.GetStringAsync(apiUrl);
            var result = JsonSerializer.Deserialize<JsonElement>(response);
            var url = result.GetProperty("url").GetString();

            if (!string.IsNullOrEmpty(url))
            {
                var settings = Get();
                settings.WebsiteUrl = url;
                Save(settings);
                return url;
            }
        }
        catch (Exception ex) { CleanMaster.App.LogError("GetWebsiteUrlAsync", ex); }

        return Get().WebsiteUrl;
    }

    public async Task SyncFromServerAsync()
    {
        var settings = Get();
        if (!settings.EnableRemoteSync)
            return;

        try
        {
            var url = await GetWebsiteUrlAsync();
            settings.WebsiteUrl = url;
            Save(settings);
        }
        catch (Exception ex) { CleanMaster.App.LogError("SyncFromServerAsync", ex); }
    }
}
