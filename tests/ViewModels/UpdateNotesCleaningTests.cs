using System.Reflection;
using CleanMaster.ViewModels;

namespace CleanMaster.Tests.ViewModels;

/// <summary>验证更新弹窗的 release notes 清洗逻辑。</summary>
public class UpdateNotesCleaningTests
{
    private static string InvokeClean(string notes)
    {
        var method = typeof(SettingsViewModel).GetMethod(
            "CleanNotes", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, new object[] { notes })!;
    }

    [Fact]
    public void CleanNotes_StripsGithubBoilerplate()
    {
        const string raw =
            "## 更新内容\n\n" +
            "- 修复启动时检查更新失败，现在能正常提示新版本\n" +
            "- 扫描进度条分段显示，不再卡在 100%\n\n" +
            "**Full Changelog**: https://github.com/Wei-Zhang09/CleanMaster/compare/v3.1.0...v3.1.1";

        var cleaned = InvokeClean(raw);

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
        Assert.Equal("", InvokeClean(raw));
    }

    [Fact]
    public void CleanNotes_StripsAuthorSuffix()
    {
        const string raw = "* v3.1.1: 修复启动检查 by @Wei-Zhang09 in https://github.com/x/y/pull/1";
        var cleaned = InvokeClean(raw);

        Assert.Contains("v3.1.1: 修复启动检查", cleaned);
        Assert.DoesNotContain("by @", cleaned);
        Assert.DoesNotContain("github.com", cleaned);
    }

    [Fact]
    public void CleanNotes_LimitsToSixItems()
    {
        var raw = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"- 条目 {i}"));
        var cleaned = InvokeClean(raw);
        Assert.Equal(6, cleaned.Split('\n').Length);
    }
}
