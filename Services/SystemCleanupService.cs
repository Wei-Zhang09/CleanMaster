using System.Diagnostics;

namespace CleanMaster.Services;

public class SystemCleanupResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string Output { get; set; } = "";
    public long FreedBytes { get; set; }

    public string FreedText => FreedBytes switch
    {
        >= 1_073_741_824 => $"{FreedBytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{FreedBytes / 1_048_576.0:F1} MB",
        _ => $"{FreedBytes / 1024.0:F1} KB"
    };
}

public class SystemCleanupService
{
    public event Action<string>? ProgressChanged;

    public async Task<SystemCleanupResult> RunDismCleanupAsync(CancellationToken ct = default)
    {
        return await RunCommandAsync(
            "Dism.exe",
            "/Online /Cleanup-Image /StartComponentCleanup",
            "Windows 组件清理",
            ct
        );
    }

    public async Task<SystemCleanupResult> RunSfcScanAsync(CancellationToken ct = default)
    {
        return await RunCommandAsync(
            "sfc.exe",
            "/scannow",
            "系统文件扫描修复",
            ct
        );
    }

    public async Task<SystemCleanupResult> FlushDnsCacheAsync(CancellationToken ct = default)
    {
        return await RunCommandAsync(
            "ipconfig.exe",
            "/flushdns",
            "DNS 缓存清理",
            ct
        );
    }

    public async Task<SystemCleanupResult> ResetStoreCacheAsync(CancellationToken ct = default)
    {
        return await RunCommandAsync(
            "wsreset.exe",
            "",
            "应用商店缓存重置",
            ct
        );
    }

    private async Task<SystemCleanupResult> RunCommandAsync(string fileName, string arguments, string operationName, CancellationToken ct)
    {
        var result = new SystemCleanupResult();

        try
        {
            ProgressChanged?.Invoke($"正在执行: {operationName}...");

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Success = false;
                result.Message = "无法启动进程";
                return result;
            }

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            result.Output = output;
            result.Success = process.ExitCode == 0;
            result.Message = result.Success ? $"{operationName}完成" : $"{operationName}失败 (退出码: {process.ExitCode})";

            ProgressChanged?.Invoke(result.Message);
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Message = "操作已取消";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"操作失败: {ex.Message}";
        }

        return result;
    }
}
