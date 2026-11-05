using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

/// <summary>
/// 真实网络冒烟：从 GitHub Releases 下载最新安装包并校验 SHA256。
///
/// 默认跳过（会访问外网、下载 40+ MB）；设置 CLEANMASTER_NETTEST=1 才跑。
/// 用途：验证"下载 → 校验 → 文件可读"这条真实链路 —— 3.3.0 的更新失败就是这个环节的
/// 句柄共享冲突，本地 MemoryStream 用例覆盖了逻辑，这条用例覆盖真实 URL + 真实 digest。
/// </summary>
public class UpdateNetworkSmokeTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("CLEANMASTER_NETTEST") == "1";

    [Fact]
    public async Task DownloadLatestRelease_PassesSha256Verification()
    {
        if (!Enabled) return;

        var service = new UpdateService();
        var info = await service.CheckForUpdateAsync();

        // 当前版本已是最新时 CheckForUpdateAsync 返回 null —— 这时直接拉最新 release 的资产来测
        if (info == null)
        {
            info = await LatestReleaseAssetAsync();
        }

        Assert.NotNull(info);
        Assert.False(string.IsNullOrEmpty(info!.DownloadUrl));
        Assert.False(string.IsNullOrEmpty(info.Sha256), "GitHub 应提供 digest，否则校验链路未被覆盖");

        var path = await service.DownloadAsync(info);

        Assert.True(File.Exists(path), $"下载文件不存在: {path}");
        Assert.Equal(info.SizeBytes, new FileInfo(path).Length);

        // 关键：校验通过后文件必须仍可读（句柄已释放）
        using var fs = File.OpenRead(path);
        Assert.Equal(info.SizeBytes, fs.Length);

        try { File.Delete(path); } catch { }
    }

    /// <summary>Gitee 源：验证国内 API 可达 + 真实响应能被解析（不需要有更新版本）。</summary>
    [Fact]
    public async Task GiteeLatestRelease_IsReachableAndParsable()
    {
        if (!Enabled) return;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CleanMaster-Updater-Test");
        var json = await http.GetStringAsync("https://gitee.com/api/v5/repos/zhang_we/clean-master/releases/latest");

        // 用一个极低版本当"当前版本"，确保能解析出 UpdateInfo 而不是 null
        var info = UpdateService.ParseRelease(json, UpdateSource.Gitee, currentVersion: "0.0.1");

        Assert.NotNull(info);
        Assert.Equal(UpdateSource.Gitee, info!.Source);
        Assert.EndsWith(".exe", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://gitee.com/", info.DownloadUrl);
    }

    /// <summary>
    /// Gitee 全链路：模拟"旧版本客户端"（3.3.1）解析真实响应 → 从 Gitee 下载 → 校验。
    /// 用模拟版本是因为测试程序集本身已是当前版本，直接 CheckForUpdateAsync 会得到"无更新"。
    /// </summary>
    [Fact]
    public async Task GiteeFullChain_DownloadsAndVerifiesInstaller()
    {
        if (!Enabled) return;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CleanMaster-Updater-Test");
        var json = await http.GetStringAsync("https://gitee.com/api/v5/repos/zhang_we/clean-master/releases/latest");

        var info = UpdateService.ParseRelease(json, UpdateSource.Gitee, currentVersion: "3.3.1");
        Assert.NotNull(info);

        var service = new UpdateService();
        var path = await service.DownloadAsync(info!);

        Assert.True(File.Exists(path));
        var size = new FileInfo(path).Length;
        Assert.True(size > 10 * 1024 * 1024, $"安装包应大于 10MB，实际 {size}");

        // 校验通过（若发布说明里带了 SHA256）后文件必须仍可读
        using var fs = File.OpenRead(path);
        Assert.Equal(size, fs.Length);

        Console.WriteLine($"GITEE_CHAIN | version={info.Version} | file={info.FileName} | bytes={size} | sha_from_release={(string.IsNullOrEmpty(info.Sha256) ? "(未提供)" : info.Sha256[..12] + "...")}");

        try { File.Delete(path); } catch { }
    }

    private static async Task<UpdateInfo?> LatestReleaseAssetAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CleanMaster-Updater-Test");
        var json = await http.GetStringAsync("https://api.github.com/repos/Wei-Zhang09/CleanMaster/releases/latest");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var digest = asset.TryGetProperty("digest", out var dg) ? dg.GetString() ?? "" : "";
            return new UpdateInfo
            {
                Version = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v'),
                TagName = root.GetProperty("tag_name").GetString() ?? "",
                FileName = name,
                DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "",
                SizeBytes = asset.GetProperty("size").GetInt64(),
                Sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : ""
            };
        }
        return null;
    }
}
