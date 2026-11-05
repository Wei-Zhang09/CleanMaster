using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

public class UpdateService : IUpdateService
{
    private const string ReleasesUrl = "https://api.github.com/repos/Wei-Zhang09/CleanMaster/releases/latest";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CleanMaster-Updater");
            var json = await _http.GetStringAsync(ReleasesUrl);
            using var doc = JsonDocument.Parse(json);

            var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latest = ParseVersion(tagName);
            if (latest == null) return null; // tag 不是 vX.Y.Z 格式，跳过

            var current = ParseVersion(CurrentVersion);
            if (current == null || !IsNewer(latest, current)) return null;

            // 找 .exe 安装包资产
            var assets = doc.RootElement.GetProperty("assets");
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                var size = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                var digest = asset.TryGetProperty("digest", out var dg) ? dg.GetString() ?? "" : "";

                return new UpdateInfo
                {
                    Version = latest,
                    TagName = tagName,
                    DownloadUrl = downloadUrl,
                    FileName = name,
                    SizeBytes = size,
                    Sha256 = ExtractSha256(digest),
                    ReleasePageUrl = doc.RootElement.GetProperty("html_url").GetString() ?? "",
                    Notes = doc.RootElement.TryGetProperty("body", out var body) ? body.GetString() ?? "" : ""
                };
            }

            return null; // 有新版但无 exe 资产
        }
        catch (Exception ex)
        {
            App.LogError("CheckForUpdateAsync", ex);
            return null;
        }
    }

    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "CleanMasterUpdate");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, info.FileName);

        // 先写 .part，校验通过后再改名：避免半截文件占用正式文件名，也便于清理上次残留
        var part = target + ".part";
        try { if (File.Exists(part)) File.Delete(part); } catch { part = UniquePath(dir, info.FileName, ".part"); }

        using var response = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? info.SizeBytes;
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        {
            await WriteAndVerifyAsync(source, part, total, info.Sha256, progress, ct);
        }

        // 写入句柄已在 WriteAndVerifyAsync 内释放，这里才能安全改名。
        // 目标文件可能被上一次失败的安装器占用 —— 退回带时间戳的文件名，不让更新直接失败。
        try
        {
            File.Move(part, target, overwrite: true);
            return target;
        }
        catch (IOException)
        {
            var fallback = UniquePath(dir, info.FileName, "");
            File.Move(part, fallback, overwrite: true);
            App.Log($"更新安装包 {target} 被占用，改用 {fallback}");
            return fallback;
        }
    }

    /// <summary>
    /// 写入并校验安装包。
    ///
    /// 关键：**写完必须先释放写入句柄，再计算 SHA256**。之前用 <c>File.Create</c>（FileShare.None）
    /// 持有句柄的同时去 File.OpenRead 校验，Windows 直接报「文件正被另一个进程使用」，
    /// 表现为"更新失败：无法访问 xxx.exe" —— 3.3.0 实测就是这个原因。
    /// </summary>
    public static async Task WriteAndVerifyAsync(
        Stream source, string target, long totalBytes, string? expectedSha256,
        IProgress<double>? progress, CancellationToken ct)
    {
        await using (var dest = File.Create(target))
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (totalBytes > 0) progress?.Report((double)downloaded / totalBytes * 100);
            }
        }

        if (string.IsNullOrEmpty(expectedSha256)) return;

        var actual = await ComputeSha256Async(target, ct);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(target); } catch { }
            throw new InvalidDataException($"安装包校验失败（期望 {expectedSha256}，实际 {actual}）");
        }
    }

    private static string UniquePath(string dir, string fileName, string suffix)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        return Path.Combine(dir, $"{name}-{DateTime.Now:HHmmss}{suffix}{ext}");
    }

    public void InstallAndRestart(string installerPath)
    {
        // 静默安装（Inno Setup 原生 /VERYSILENT + /NORESTART）。
        // 先启动安装器，再退出当前进程，由安装器接管覆盖与重启。
        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /NORESTART /SP-",
            UseShellExecute = true,
            Verb = "runas" // 安装需要管理员权限
        };
        Process.Start(psi);

        // 给安装器一点启动时间，然后退出主程序
        System.Windows.Application.Current?.Shutdown();
        Environment.Exit(0);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(path);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private static string ExtractSha256(string digest)
    {
        // digest 形如 "sha256:9d37eaf4..."，取冒号后部分
        var idx = digest.IndexOf(':');
        return idx >= 0 ? digest[(idx + 1)..] : digest;
    }

    /// <summary>从 tag 解析版本号，仅接受 vX.Y.Z 或 X.Y.Z 格式。</summary>
    private static string? ParseVersion(string tag)
    {
        var t = tag.Trim().TrimStart('v', 'V');
        var parts = t.Split('.');
        if (parts.Length < 3) return null;
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], out _)) return null;
        }
        return t;
    }

    /// <summary>判断 latest 是否严格大于 current（按 X.Y.Z 数值比较）。</summary>
    private static bool IsNewer(string latest, string current)
    {
        var l = latest.Split('.').Select(int.Parse).ToArray();
        var c = current.Split('.').Select(int.Parse).ToArray();
        for (var i = 0; i < Math.Min(3, Math.Min(l.Length, c.Length)); i++)
        {
            if (l[i] > c[i]) return true;
            if (l[i] < c[i]) return false;
        }
        return false;
    }
}
