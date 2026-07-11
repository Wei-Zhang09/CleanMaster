using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using CleanMaster.Services.Interfaces;

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

public class SystemCleanupService : ISystemCleanupService
{
    public event Action<string>? ProgressChanged;

    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public async Task<SystemCleanupResult> RunDismCleanupAsync(CancellationToken ct = default)
    {
        if (!IsRunningAsAdmin())
        {
            return new SystemCleanupResult
            {
                Success = false,
                Message = "Windows 组件清理需要管理员权限，请以管理员身份运行本软件后重试。"
            };
        }

        var result = await RunCommandWithUtf8Async(
            "Dism.exe",
            "/Online /Cleanup-Image /StartComponentCleanup",
            "Windows 组件清理",
            ct
        );

        // Try to parse freed space from DISM output
        if (result.Success && !string.IsNullOrEmpty(result.Output))
        {
            result.FreedBytes = ParseDismFreedSpace(result.Output);
        }

        return result;
    }

    public async Task<SystemCleanupResult> RunSfcScanAsync(CancellationToken ct = default)
    {
        if (!IsRunningAsAdmin())
        {
            return new SystemCleanupResult
            {
                Success = false,
                Message = "系统文件扫描修复需要管理员权限，请以管理员身份运行本软件后重试。"
            };
        }

        // Use PowerShell with forced UTF-8 encoding for SFC
        var result = await RunWithPowerShellUtf8Async(
            "sfc.exe",
            "/scannow",
            "系统文件扫描修复",
            ct
        );

        // Parse SFC output for detailed result message
        if (!string.IsNullOrEmpty(result.Output))
        {
            var sfcMessage = ParseSfcOutput(result.Output);
            if (!string.IsNullOrEmpty(sfcMessage))
            {
                result.Message = result.Success
                    ? $"系统文件扫描完成 - {sfcMessage}"
                    : result.Message;
            }
        }

        return result;
    }

    public async Task<SystemCleanupResult> FlushDnsCacheAsync(CancellationToken ct = default)
    {
        return await RunCommandWithUtf8Async(
            "ipconfig.exe",
            "/flushdns",
            "DNS 缓存清理",
            ct
        );
    }

    public async Task<SystemCleanupResult> ResetStoreCacheAsync(CancellationToken ct = default)
    {
        return await RunCommandWithUtf8Async(
            "wsreset.exe",
            "",
            "应用商店缓存重置",
            ct
        );
    }

    /// <summary>
    /// Runs a command via cmd.exe with UTF-8 code page (65001) to ensure correct encoding.
    /// </summary>
    private async Task<SystemCleanupResult> RunCommandWithUtf8Async(
        string fileName, string arguments, string operationName, CancellationToken ct)
    {
        var result = new SystemCleanupResult();

        try
        {
            ProgressChanged?.Invoke($"正在执行: {operationName}...");

            // Use cmd.exe with chcp 65001 to force UTF-8 encoding
            var cmdArguments = $"/c \"chcp 65001 >nul && {fileName} {arguments}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var outputBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    var line = e.Data.Length > 100 ? e.Data[..100] + "..." : e.Data;
                    ProgressChanged?.Invoke(line);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    outputBuilder.AppendLine("[ERR] " + e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                result.Success = false;
                result.Message = "操作已取消";
                ProgressChanged?.Invoke(result.Message);
                return result;
            }

            process.WaitForExit();
            result.Output = outputBuilder.ToString();
            result.Success = process.ExitCode == 0;

            if (result.Success)
            {
                result.Message = $"{operationName}完成";
            }
            else
            {
                var code = process.ExitCode;
                result.Message = code switch
                {
                    5 => $"{operationName}失败 (拒绝访问)。请以管理员身份运行本软件。",
                    740 => $"{operationName}失败 (需要提权)。请以管理员身份运行本软件。",
                    _ => $"{operationName}失败 (退出码: {code})"
                };
            }

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
            ProgressChanged?.Invoke(result.Message);
        }

        return result;
    }

    /// <summary>
    /// Runs a command via PowerShell with forced UTF-8 encoding.
    /// This ensures the output is correctly encoded regardless of the child process's encoding.
    /// </summary>
    private async Task<SystemCleanupResult> RunWithPowerShellUtf8Async(
        string fileName, string arguments, string operationName, CancellationToken ct)
    {
        var result = new SystemCleanupResult();

        try
        {
            ProgressChanged?.Invoke($"正在执行: {operationName}...");

            // Use PowerShell to run the command with forced UTF-8 output encoding
            var psCommand = $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; & '{fileName}' {arguments}";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{psCommand}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var outputBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    var line = e.Data.Length > 100 ? e.Data[..100] + "..." : e.Data;
                    ProgressChanged?.Invoke(line);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    outputBuilder.AppendLine("[ERR] " + e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                result.Success = false;
                result.Message = "操作已取消";
                ProgressChanged?.Invoke(result.Message);
                return result;
            }

            process.WaitForExit();
            result.Output = outputBuilder.ToString();
            result.Success = process.ExitCode == 0;

            if (result.Success)
            {
                result.Message = $"{operationName}完成";
            }
            else
            {
                var code = process.ExitCode;
                result.Message = code switch
                {
                    5 => $"{operationName}失败 (拒绝访问)。请以管理员身份运行本软件。",
                    740 => $"{operationName}失败 (需要提权)。请以管理员身份运行本软件。",
                    _ => $"{operationName}失败 (退出码: {code})"
                };
            }

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
            ProgressChanged?.Invoke(result.Message);
        }

        return result;
    }

    /// <summary>
    /// Runs a command directly without cmd.exe wrapper.
    /// Reads raw bytes and tries multiple decodings.
    /// </summary>
    private async Task<SystemCleanupResult> RunDirectCommandAsync(
        string fileName, string arguments, string operationName, CancellationToken ct)
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
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.Start();

            // Read ALL raw bytes first
            using var stdout = process.StandardOutput.BaseStream;
            using var ms = new MemoryStream();
            await stdout.CopyToAsync(ms, ct);
            var rawBytes = ms.ToArray();

            await process.WaitForExitAsync(ct);

            // Try ALL common encodings and log results
            var encodings = new[]
            {
                ("UTF-8", Encoding.UTF8),
                ("GBK-936", Encoding.GetEncoding(936)),
                ("GB2312", Encoding.GetEncoding(20936)),
                ("Big5-950", Encoding.GetEncoding(950)),
                ("Default", Encoding.Default),
                ("ASCII", Encoding.ASCII)
            };

            var sb = new StringBuilder();
            sb.AppendLine($"[Raw bytes length: {rawBytes.Length}]");

            if (rawBytes.Length > 0)
            {
                // Show first 100 bytes as hex
                sb.AppendLine($"[Hex: {BitConverter.ToString(rawBytes[..Math.Min(100, rawBytes.Length)])}]");
                sb.AppendLine();

                foreach (var (name, encoding) in encodings)
                {
                    try
                    {
                        var text = encoding.GetString(rawBytes);
                        // Only show first 200 chars
                        var preview = text.Length > 200 ? text[..200] : text;
                        sb.AppendLine($"[{name}]: {preview}");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"[{name}]: ERROR - {ex.Message}");
                    }
                }
            }

            result.Output = sb.ToString();
            result.Success = process.ExitCode == 0;
            result.Message = result.Success ? $"{operationName}完成" : $"{operationName}失败 (退出码: {process.ExitCode})";

            // Show raw output for debugging
            ProgressChanged?.Invoke("=== 编码测试输出 ===");
            foreach (var line in result.Output.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    ProgressChanged?.Invoke(line);
            }

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
            ProgressChanged?.Invoke(result.Message);
        }

        return result;
    }

    /// <summary>
    /// Parses DISM output to extract freed space information.
    /// </summary>
    private static long ParseDismFreedSpace(string output)
    {
        try
        {
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var lowerLine = line.ToLower();

                if (lowerLine.Contains("reclaimed") || lowerLine.Contains("freed") ||
                    lowerLine.Contains("deleted") || lowerLine.Contains("已释放"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        line,
                        @"(\d[\d,.]*)\s*(MB|GB|KB|TB)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (match.Success)
                    {
                        var numberStr = match.Groups[1].Value.Replace(",", "");
                        if (double.TryParse(numberStr, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var number))
                        {
                            var unit = match.Groups[2].Value.ToUpper();
                            return unit switch
                            {
                                "TB" => (long)(number * 1_099_511_627_776),
                                "GB" => (long)(number * 1_073_741_824),
                                "MB" => (long)(number * 1_048_576),
                                "KB" => (long)(number * 1024),
                                _ => 0
                            };
                        }
                    }
                }
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Parses SFC output to extract scan result message.
    /// </summary>
    private static string ParseSfcOutput(string output)
    {
        try
        {
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("找到了损坏文件并成功修复") || line.Contains("found corrupt files and successfully repaired"))
                    return "找到并修复了损坏文件";

                if (line.Contains("找到了损坏文件但无法修复") || line.Contains("found corrupt files but was unable to fix"))
                    return "找到损坏文件但部分无法修复";

                if (line.Contains("未找到任何完整性冲突") || line.Contains("did not find any integrity violations"))
                    return "未发现系统文件问题";

                if (line.Contains("Windows 资源保护无法执行请求的操作") || line.Contains("Windows Resource Protection could not perform"))
                    return "无法完成操作，请在安全模式下重试";
            }
        }
        catch { }

        return null;
    }
}
