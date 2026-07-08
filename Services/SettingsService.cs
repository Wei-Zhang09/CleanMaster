using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace CleanMaster.Services;

public class AppSettings
{
    public string WebsiteUrl { get; set; } = "https://awe-software-production.up.railway.app";
    public string ApiBaseUrl { get; set; } = "https://awe-software-production.up.railway.app/api";
}

public static class SettingsService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CleanMaster");

    private static readonly string SettingsFile = Path.Combine(ConfigDir, "settings.json");
    private static AppSettings? _cached;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static AppSettings Get()
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
        catch { }

        _cached = new AppSettings();
        return _cached;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            _cached = settings;
        }
        catch { }
    }

    public static async Task<string> GetWebsiteUrlAsync()
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
        catch { }

        return Get().WebsiteUrl;
    }

    public static async Task SyncFromServerAsync()
    {
        try
        {
            var url = await GetWebsiteUrlAsync();
            var settings = Get();
            settings.WebsiteUrl = url;
            Save(settings);
        }
        catch { }
    }
}
