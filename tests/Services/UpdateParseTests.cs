using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

/// <summary>
/// 双源更新的解析逻辑（Gitee + GitHub）。
///
/// 用真实响应结构做固定样本：Gitee 的 tag 不带 v、assets 没有 size/digest；
/// GitHub 的 assets 带 size 与 digest。解析器必须两者都吃得下，否则国内用户
/// 要么看不到更新，要么拿到空的下载地址。
/// </summary>
public class UpdateParseTests
{
    // 真实 Gitee 响应（zhang_we/clean-master 的 v3.1.2 release，字段按实际抓取裁剪）
    private const string GiteeJson = """
    {
      "id": 1242928,
      "tag_name": "3.1.2",
      "target_commitish": "abb591716ec55415f4647878d7953c3651916ecd",
      "prerelease": false,
      "name": "版本更新",
      "body": "修复已知bug\r\n优化软件性能",
      "author": { "id": 15403466, "login": "zhang_we" },
      "assets": [
        {
          "name": "CleanMaster-Setup-v3.1.2.exe",
          "browser_download_url": "https://gitee.com/zhang_we/clean-master/releases/download/3.1.2/CleanMaster-Setup-v3.1.2.exe"
        },
        {
          "name": "3.1.2.zip",
          "browser_download_url": "https://gitee.com/zhang_we/clean-master/archive/refs/tags/3.1.2.zip"
        }
      ]
    }
    """;

    // 真实 GitHub 响应结构（含 digest / size / html_url）
    private const string GitHubJson = """
    {
      "tag_name": "v3.3.1",
      "html_url": "https://github.com/Wei-Zhang09/CleanMaster/releases/tag/v3.3.1",
      "body": "## 修复内容\n- 自动更新失败修复",
      "assets": [
        {
          "name": "CleanMaster-Setup-v3.3.1.exe",
          "size": 45252984,
          "digest": "sha256:f9bd291fd10008c94b0f2d7aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "browser_download_url": "https://github.com/Wei-Zhang09/CleanMaster/releases/download/v3.3.1/CleanMaster-Setup-v3.3.1.exe"
        }
      ]
    }
    """;

    [Fact]
    public void ParseVersion_AcceptsBothTagStyles()
    {
        Assert.Equal("3.3.0", UpdateService.ParseVersion("v3.3.0"));
        Assert.Equal("3.1.2", UpdateService.ParseVersion("3.1.2"));   // Gitee 常见写法
        Assert.Null(UpdateService.ParseVersion("nightly"));
        Assert.Null(UpdateService.ParseVersion("v3.3"));
    }

    [Theory]
    [InlineData("3.3.1", "3.3.0", true)]
    [InlineData("3.3.0", "3.3.1", false)]
    [InlineData("3.10.0", "3.9.9", true)]     // 数值比较，不是字符串比较
    [InlineData("3.3.1", "3.3.1", false)]
    public void IsNewer_ComparesNumerically(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(latest, current));
    }

    [Fact]
    public void ParseGitee_NewerVersion_ReturnsInfoWithoutSizeOrDigest()
    {
        var info = UpdateService.ParseRelease(GiteeJson, UpdateSource.Gitee, currentVersion: "3.0.0");

        Assert.NotNull(info);
        Assert.Equal("3.1.2", info!.Version);
        Assert.Equal(UpdateSource.Gitee, info.Source);
        Assert.Equal("CleanMaster-Setup-v3.1.2.exe", info.FileName);
        Assert.StartsWith("https://gitee.com/zhang_we/clean-master/releases/download/", info.DownloadUrl);
        Assert.Equal(0, info.SizeBytes);                 // Gitee 不返回 size
        Assert.Equal("", info.Sha256);                   // 也没有 digest
        Assert.Contains("releases/tag/3.1.2", info.ReleasePageUrl);
        Assert.Contains("修复已知bug", info.Notes);
    }

    [Fact]
    public void ParseGitee_SameOrOlderVersion_ReturnsNull()
    {
        Assert.Null(UpdateService.ParseRelease(GiteeJson, UpdateSource.Gitee, "3.1.2"));
        Assert.Null(UpdateService.ParseRelease(GiteeJson, UpdateSource.Gitee, "3.3.0"));
    }

    [Fact]
    public void ParseGitHub_UsesAssetDigestAndSize()
    {
        var info = UpdateService.ParseRelease(GitHubJson, UpdateSource.GitHub, currentVersion: "3.3.0");

        Assert.NotNull(info);
        Assert.Equal("3.3.1", info!.Version);
        Assert.Equal(UpdateSource.GitHub, info.Source);
        Assert.Equal(45252984, info.SizeBytes);
        Assert.StartsWith("F9BD291F", info.Sha256, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, info.Sha256.Length);
        Assert.Contains("github.com/Wei-Zhang09/CleanMaster/releases/tag/v3.3.1", info.ReleasePageUrl);
    }

    [Fact]
    public void ParseRelease_BodySha256_UsedWhenNoDigest()
    {
        // Gitee 发布说明里写一行 SHA256，客户端据此校验（防镜像/中间人换包）
        var json = GiteeJson.Replace("\"body\": \"修复已知bug\\r\\n优化软件性能\"",
            "\"body\": \"修复已知bug\\r\\nSHA256: " + new string('a', 64) + "\"");

        var info = UpdateService.ParseRelease(json, UpdateSource.Gitee, "3.0.0");

        Assert.NotNull(info);
        Assert.Equal(new string('a', 64), info!.Sha256);
    }

    [Fact]
    public void ExtractBodySha256_HandlesFormats()
    {
        var hex = new string('b', 64);
        Assert.Equal(hex, UpdateService.ExtractBodySha256($"更新\nSHA256: {hex}"), ignoreCase: true);
        Assert.Equal(hex, UpdateService.ExtractBodySha256($"更新\nsha-256：{hex.ToUpperInvariant()}"), ignoreCase: true);  // 全角冒号
        Assert.Equal("", UpdateService.ExtractBodySha256("没有任何哈希"));
        Assert.Equal("", UpdateService.ExtractBodySha256(null));
    }

    [Fact]
    public void ParseRelease_NoExeAsset_ReturnsNull()
    {
        var json = GiteeJson.Replace(".exe", ".zip");
        Assert.Null(UpdateService.ParseRelease(json, UpdateSource.Gitee, "3.0.0"));
    }
}
