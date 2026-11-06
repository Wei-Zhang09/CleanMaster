using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

/// <summary>更新源。Gitee 面向国内用户（直连可用），GitHub 作为兜底与国际发布。</summary>
public enum UpdateSource
{
    Gitee,
    GitHub
}

/// <summary>检查更新时的网络/解析失败。与"无更新"（返回 null）区分开，界面才能如实提示。</summary>
public class UpdateCheckException : Exception
{
    public UpdateCheckException(string message) : base(message) { }
}

public class UpdateService : IUpdateService
{
    // Gitee：国内直连可用，主力更新源（公开仓库无需 token）
    private const string GiteeOwnerRepo = "zhang_we/clean-master";
    private const string GiteeApi = "https://gitee.com/api/v5/repos/" + GiteeOwnerRepo + "/releases/latest";
    private const string GiteeReleasePage = "https://gitee.com/" + GiteeOwnerRepo + "/releases/tag/";

    // GitHub：兜底（无梯子时通常只有 API 能通，下载会被掐断）
    private const string GitHubApi = "https://api.github.com/repos/Wei-Zhang09/CleanMaster/releases/latest";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    /// <summary>发布说明里可写一行 <c>SHA256: &lt;64 位十六进制&gt;</c>；Gitee 不提供 digest，靠它做校验。</summary>
    private static readonly Regex BodySha256 = new(
        @"SHA-?256\s*[:：]\s*([0-9a-fA-F]{64})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// 检查更新：**两个源都查**，取版本更高者；任一源可用即可。
    /// 全部失败时抛 <see cref="UpdateCheckException"/>，让界面显示"网络不可用"而不是谎报"已是最新"。
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CleanMaster-Updater");

        var found = new List<UpdateInfo>();
        var errors = new List<string>();

        var sources = new[]
        {
            (Source: UpdateSource.Gitee, Url: GiteeApi),
            (Source: UpdateSource.GitHub, Url: GitHubApi)
        };

        foreach (var (source, url) in sources)
        {
            try
            {
                var json = await _http.GetStringAsync(url);
                var info = ParseRelease(json, source, CurrentVersion);
                App.Log($"[UPD] {source} -> {(info == null ? "no update" : info.Version)}");
                if (info != null) found.Add(info);
            }
            catch (Exception ex)
            {
                errors.Add($"{source}: {ex.Message}");
                App.Log($"[UPD] {source} 检查失败: {ex.Message}");
            }
        }

        // 关键：**按优先级取第一个可用的源**，而不是"取版本最高的那个"。
        // 反例：GitHub 有 3.4.2、Gitee 只有 3.4.1 时，若按版本取高会选 GitHub，
        // 而国内用户根本下载不动 GitHub 资产 —— 明明 Gitee 上有能用的版本却更新失败。
        // 因此 Gitee 有更新就用 Gitee；Gitee 没有（或没传）才回落 GitHub。
        if (found.Count > 0)
            return found[0];

        // 两个源都失败才算失败；都通但都没新版本 → 返回 null（界面显示"已是最新"）
        if (errors.Count == sources.Length)
            throw new UpdateCheckException("无法连接更新服务器：" + string.Join(" | ", errors));

        return null;
    }

    /// <summary>
    /// 解析单个源的 release JSON。纯函数，便于用真实响应做单测。
    /// 兼容 GitHub（有 digest/html_url/size）与 Gitee（无 digest、size 可能缺失、tag 常不带 v）。
    /// </summary>
    public static UpdateInfo? ParseRelease(string json, UpdateSource source, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "";
        var latest = ParseVersion(tagName);
        if (latest == null) return null;                                   // tag 不是 X.Y.Z，跳过

        var current = ParseVersion(currentVersion);
        if (current == null || !IsNewer(latest, current)) return null;      // 没有更新

        var notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
        var bodySha = ExtractBodySha256(notes);

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(url)) continue;

            var size = asset.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number
                ? sz.GetInt64()
                : 0;

            var digest = asset.TryGetProperty("digest", out var dg) ? dg.GetString() ?? "" : "";
            var sha = ExtractSha256(digest);
            if (string.IsNullOrEmpty(sha)) sha = bodySha;

            var pageUrl = root.TryGetProperty("html_url", out var hu)
                ? hu.GetString() ?? ""
                : source == UpdateSource.Gitee ? GiteeReleasePage + tagName : "";

            return new UpdateInfo
            {
                Version = latest,
                TagName = tagName,
                Source = source,
                DownloadUrl = url,
                FileName = name,
                SizeBytes = size,
                Sha256 = sha,
                ReleasePageUrl = pageUrl,
                Notes = notes
            };
        }

        return null; // 有新版但没有 exe 资产
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

    private static string UniquePath(string dir, string fileName, string suffix)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        return Path.Combine(dir, $"{name}-{DateTime.Now:HHmmss}{suffix}{ext}");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(path);
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    /// <summary>从 GitHub 的 digest（"sha256:xxx"）取十六进制部分；空值返回空串。</summary>
    private static string ExtractSha256(string digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return "";
        var idx = digest.IndexOf(':');
        return (idx >= 0 ? digest[(idx + 1)..] : digest).Trim();
    }

    /// <summary>从发布说明里取 <c>SHA256: &lt;hex&gt;</c>（Gitee 没有 digest，用这个兜底）。</summary>
    public static string ExtractBodySha256(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        var m = BodySha256.Match(notes);
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>从 tag 解析版本号，接受 vX.Y.Z 或 X.Y.Z（Gitee 的 tag 常不带 v）。</summary>
    public static string? ParseVersion(string tag)
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
    public static bool IsNewer(string latest, string current)
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
