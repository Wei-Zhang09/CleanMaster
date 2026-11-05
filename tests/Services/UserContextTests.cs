using CleanMaster.Services;

namespace CleanMaster.Tests.Services;

public class UserContextTests
{
    [Fact]
    public void ShouldBlockUserScopeUninstall_MachineScopeNeverBlocked()
    {
        // 机器级安装不依赖 HKCU，换个管理员提权也照样能正常卸载
        Assert.False(UserContext.ShouldBlockUserScopeUninstall("HKLM", "S-1-5-21-AAA", "S-1-5-21-BBB"));
    }

    [Fact]
    public void ShouldBlockUserScopeUninstall_SameUserNotBlocked()
    {
        Assert.False(UserContext.ShouldBlockUserScopeUninstall("HKCU", "S-1-5-21-AAA", "S-1-5-21-AAA"));
    }

    [Fact]
    public void ShouldBlockUserScopeUninstall_DifferentUserBlocked()
    {
        // 用另一个管理员账户提权：卸载器会去清那个账户的 HKCU，必须拒绝
        Assert.True(UserContext.ShouldBlockUserScopeUninstall("HKCU", "S-1-5-21-ADMIN", "S-1-5-21-USER"));
    }

    [Fact]
    public void ShouldBlockUserScopeUninstall_UnknownSidNeverBlocks()
    {
        // 查不到 SID 时不能把功能锁死（例如 explorer 未运行的精简环境）
        Assert.False(UserContext.ShouldBlockUserScopeUninstall("HKCU", null, "S-1-5-21-USER"));
        Assert.False(UserContext.ShouldBlockUserScopeUninstall("HKCU", "S-1-5-21-ADMIN", null));
        Assert.False(UserContext.ShouldBlockUserScopeUninstall("HKCU", "", ""));
    }

    [Fact]
    public void GetCurrentUserSid_ReturnsSid()
    {
        var sid = UserContext.GetCurrentUserSid();

        Assert.False(string.IsNullOrEmpty(sid));
        Assert.StartsWith("S-1-", sid);
    }

    [Fact]
    public void InteractiveUser_MatchesCurrentUser_OnNormalDesktopSession()
    {
        // 常规桌面会话里，交互式用户就是当前用户 —— 这条断言同时验证了
        // "读 explorer 进程令牌取 SID" 这条链路在本机真的能跑通
        var hasExplorer = System.Diagnostics.Process.GetProcessesByName("explorer").Length > 0;
        if (!hasExplorer) return; // 无 explorer（服务/精简会话）时不适用

        var interactive = UserContext.GetInteractiveUserSid();

        Assert.False(string.IsNullOrEmpty(interactive), "有 explorer 进程却读不到 SID，说明令牌读取链路有问题");
        Assert.Equal(UserContext.GetCurrentUserSid(), interactive);
        Assert.True(UserContext.IsRunningAsInteractiveUser());
    }
}
