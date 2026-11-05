using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CleanMaster.Services;

/// <summary>
/// 身份判定的抽象：让"用另一个管理员账户提权"这种本机难以复现的场景可以被测试覆盖。
/// </summary>
public interface IUserContext
{
    string? GetCurrentUserSid();
    string? GetInteractiveUserSid();
    bool ShouldBlockUserScopeUninstall(string hive);
}

/// <summary>默认实现：读真实进程令牌。</summary>
public sealed class DefaultUserContext : IUserContext
{
    public static readonly DefaultUserContext Instance = new();

    public string? GetCurrentUserSid() => UserContext.GetCurrentUserSid();

    public string? GetInteractiveUserSid() => UserContext.GetInteractiveUserSid();

    public bool ShouldBlockUserScopeUninstall(string hive)
        => UserContext.ShouldBlockUserScopeUninstall(hive, GetCurrentUserSid(), GetInteractiveUserSid());
}

/// <summary>
/// 判断"当前进程是否运行在交互式桌面用户身份下"。
///
/// 为什么需要它：本程序是 requireAdministrator。管理员账户点"是"提权时，令牌换了、用户没换，
/// HKCU 还是同一个人的——没问题。但如果是**用另一个管理员账户提权**（over-the-shoulder，
/// 非管理员用户或 UAC 输别的账号），当前进程的 HKCU 就变成了那个管理员的配置：
/// 此时卸载一个"用户级安装"的软件，卸载器会去清管理员的注册表，而真正用户的数据原样留着，
/// 用户看到的是"卸载了但没卸干净"，严重时会把管理员的用户级软件列出来让用户误卸。
///
/// 所以用户级（HKCU）软件只在"当前身份 == 交互式桌面用户"时才允许卸载，否则如实拒绝并说明原因。
/// </summary>
public static class UserContext
{
    /// <summary>当前进程所属用户的 SID。</summary>
    public static string? GetCurrentUserSid()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value; }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("UserContext.GetCurrentUserSid", ex);
            return null;
        }
    }

    /// <summary>
    /// 交互式桌面用户（explorer.exe）的 SID。取不到返回 null。
    /// 优先取本会话的 explorer，避免多用户同时登录时拿到别人的令牌。
    /// </summary>
    public static string? GetInteractiveUserSid()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var sessionId = Process.GetCurrentProcess().SessionId;
            var candidates = Process.GetProcessesByName("explorer")
                .Where(p => p.SessionId == sessionId)
                .ToList();

            foreach (var proc in candidates)
            {
                using (proc)
                {
                    var sid = TryGetProcessUserSid(proc);
                    if (!string.IsNullOrEmpty(sid)) return sid;
                }
            }
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("UserContext.GetInteractiveUserSid", ex);
        }

        return null;
    }

    /// <summary>当前进程是否就是交互式桌面用户（SID 未知时按"是"处理，不阻断正常用户）。</summary>
    public static bool IsRunningAsInteractiveUser()
        => !ShouldBlockUserScopeUninstall("HKCU", GetCurrentUserSid(), GetInteractiveUserSid());

    /// <summary>
    /// 纯判定：用户级（HKCU）软件在当前身份与桌面用户不一致时应拒绝卸载。
    /// 抽成纯函数是为了能覆盖"另一个账户提权"这种本机难以复现的场景。
    /// </summary>
    public static bool ShouldBlockUserScopeUninstall(string hive, string? currentSid, string? interactiveSid)
    {
        // 机器级安装不依赖 HKCU，谁提权都能正常卸载
        if (!string.Equals(hive, "HKCU", StringComparison.OrdinalIgnoreCase)) return false;

        // 任何一边拿不到 SID 都不阻断：宁可让用户试，也不要因为查不到就把功能锁死
        if (string.IsNullOrEmpty(currentSid) || string.IsNullOrEmpty(interactiveSid)) return false;

        return !string.Equals(currentSid, interactiveSid, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetProcessUserSid(Process proc)
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(proc.Handle, TokenQuery, out token)) return null;

            GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var needed);
            if (needed <= 0) return null;

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetTokenInformation(token, TokenUser, buffer, needed, out _)) return null;

                // TOKEN_USER 的第一个字段是 SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; }
                var sidPtr = Marshal.ReadIntPtr(buffer);
                return new SecurityIdentifier(sidPtr).Value;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("UserContext.TryGetProcessUserSid", ex);
            return null;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
        }
    }

    private const uint TokenQuery = 0x0008;
    private const int TokenUser = 1;

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
