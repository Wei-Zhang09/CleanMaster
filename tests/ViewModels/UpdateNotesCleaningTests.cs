using CleanMaster.ViewModels;

namespace CleanMaster.Tests.ViewModels;

/// <summary>
/// 验证更新弹窗的 release notes 清洗逻辑：只留用户看得懂的条目。
/// Markdown 标题、GitHub 的 compare 链接、by @user 后缀、以及为校验而写的 SHA256 行都不能进弹窗。
/// </summary>
public class UpdateNotesCleaningTests
{
    [Fact]
    public void CleanNotes_StripsGithubBoilerplate()
    {
        const string raw =
            "## 更新内容\n\n" +
            "- 修复启动时检查更新失败，现在能正常提示新版本\n" +
            "- 扫描进度条分段显示，不再卡在 100%\n\n" +
            "**Full Changelog**: https://github.com/Wei-Zhang09/CleanMaster/compare/v3.1.0...v3.1.1";

        var cleaned = SettingsViewModel.CleanNotes(raw);

        Assert.Contains("修复启动时检查更新失败", cleaned);
        Assert.Contains("扫描进度条分段显示", cleaned);
        Assert.DoesNotContain("github.com", cleaned);
        Assert.DoesNotContain("Full Changelog", cleaned);
        Assert.DoesNotContain("##", cleaned);
    }

    [Fact]
    public void CleanNotes_OnlyCompareLink_ReturnsEmpty()
    {
        const string raw = "**Full Changelog**: https://github.com/a/b/compare/v1...v2";
        Assert.Equal("", SettingsViewModel.CleanNotes(raw));
    }

    [Fact]
    public void CleanNotes_StripsAuthorSuffix()
    {
        const string raw = "* v3.1.1: 修复启动检查 by @Wei-Zhang09 in https://github.com/x/y/pull/1";
        var cleaned = SettingsViewModel.CleanNotes(raw);

        Assert.Contains("v3.1.1: 修复启动检查", cleaned);
        Assert.DoesNotContain("by @", cleaned);
        Assert.DoesNotContain("github.com", cleaned);
    }

    [Fact]
    public void CleanNotes_LimitsToSixItems()
    {
        var raw = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"- 条目 {i}"));
        var cleaned = SettingsViewModel.CleanNotes(raw);

        Assert.Equal(6, cleaned.Split('\n').Length);
    }

    [Fact]
    public void CleanNotes_DropsSha256VerificationLine()
    {
        // 发布说明里的 SHA256 是给更新器校验用的，不该出现在"更新内容"里让用户困惑
        var hex = new string('a', 64);
        var raw = $"修复更新失败\nSHA256: {hex}\n优化扫描速度";

        var cleaned = SettingsViewModel.CleanNotes(raw);

        Assert.DoesNotContain(hex, cleaned);
        Assert.DoesNotContain("SHA256", cleaned);
        Assert.Contains("修复更新失败", cleaned);
        Assert.Contains("优化扫描速度", cleaned);
    }

    [Fact]
    public void CleanNotes_DropsSingleHashHeadings()
    {
        var raw = "# 一级标题\n- 真正的更新条目";

        var cleaned = SettingsViewModel.CleanNotes(raw);

        Assert.DoesNotContain("一级标题", cleaned);
        Assert.Contains("真正的更新条目", cleaned);
    }

    [Fact]
    public void CleanNotes_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", SettingsViewModel.CleanNotes(""));
        Assert.Equal("", SettingsViewModel.CleanNotes("   "));
    }
}
