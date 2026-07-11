using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

public class LicenseInfo
{
    public string KeyCode { get; set; } = "";
    public string SoftwareName { get; set; } = "";
    public DateTime ActivatedAt { get; set; }
    public bool IsValid { get; set; }
}

public class LicenseService : ILicenseService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CleanMaster");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "license.json");

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly MachineIdService _machineIdService = new();
    private readonly ISettingsService _settingsService;

    public LicenseService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    private string ApiBase => _settingsService.Get().LicenseApiUrl;

    public LicenseInfo? LoadLocal()
    {
        try
        {
            if (!File.Exists(ConfigFile)) return null;
            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<LicenseInfo>(json);
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("LoadLocal", ex);
            return null;
        }
    }

    private void SaveLocal(LicenseInfo info)
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);

            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch (Exception ex) { CleanMaster.App.LogError("SaveLocal", ex); }
    }

    public void ClearLocal()
    {
        try
        {
            if (File.Exists(ConfigFile))
                File.Delete(ConfigFile);
        }
        catch (Exception ex) { CleanMaster.App.LogError("ClearLocal", ex); }
    }

    public async Task<(bool Success, string Message, LicenseInfo? Info)> VerifyKeyAsync(string keyCode)
    {
        try
        {
            var machineId = _machineIdService.GetMachineId();

            var payload = new
            {
                key_code = keyCode.Trim().ToUpper(),
                machine_id = machineId,
                software_slug = "cleanmaster"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{ApiBase}/keys/verify", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

            var valid = result.GetProperty("valid").GetBoolean();
            var message = result.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "" : "";
            var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() ?? "" : "";
            var softwareName = result.TryGetProperty("software_name", out var nameProp) ? nameProp.GetString() ?? "" : "";

            if (valid)
            {
                var info = new LicenseInfo
                {
                    KeyCode = keyCode.Trim().ToUpper(),
                    SoftwareName = softwareName,
                    ActivatedAt = DateTime.Now,
                    IsValid = true
                };
                SaveLocal(info);
                return (true, message, info);
            }

            return (false, error, null);
        }
        catch (HttpRequestException)
        {
            return (false, "无法连接到服务器，请检查网络", null);
        }
        catch (TaskCanceledException)
        {
            return (false, "连接超时，请稍后重试", null);
        }
        catch (Exception ex)
        {
            return (false, $"验证失败: {ex.Message}", null);
        }
    }

    public async Task<(bool IsValid, string Message)> CheckActivationAsync()
    {
        var local = LoadLocal();
        if (local == null || string.IsNullOrEmpty(local.KeyCode))
            return (false, "未激活");

        try
        {
            var machineId = _machineIdService.GetMachineId();
            var payload = new
            {
                key_code = local.KeyCode,
                machine_id = machineId,
                software_slug = "cleanmaster"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{ApiBase}/keys/verify", content);
            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

            var valid = result.GetProperty("valid").GetBoolean();
            if (valid)
            {
                local.IsValid = true;
                SaveLocal(local);
                return (true, "已激活");
            }
            else
            {
                var error = result.TryGetProperty("error", out var errProp) ? errProp.GetString() ?? "" : "";
                ClearLocal();
                return (false, error);
            }
        }
        catch (Exception ex)
        {
            // Offline - trust local cache
            CleanMaster.App.LogError("CheckActivationAsync", ex);
            return (local.IsValid, local.IsValid ? "已激活(离线)" : "验证失败");
        }
    }
}
