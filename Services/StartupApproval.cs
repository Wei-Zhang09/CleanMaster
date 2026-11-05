using Microsoft.Win32;

namespace CleanMaster.Services;

public enum StartupApprovalState
{
    /// <summary>没有登记记录（Windows 语义：视为启用）。</summary>
    Unknown,
    Enabled,
    Disabled
}

/// <summary>
/// 启动项的"启用/禁用"状态读写，走 Windows 任务管理器自己用的那套机制：
/// <c>Explorer\StartupApproved\Run</c>（32 位程序在 <c>Run32</c>，启动文件夹在 <c>StartupFolder</c>）
/// 下的 <c>REG_BINARY</c> 值，12 字节 = 4 字节状态 + 8 字节 FILETIME。
///
/// 为什么不用"改注册表值名"那套：改名后任务管理器会显示成一个叫 <c>Disabled_xxx</c> 的怪条目，
/// 别的工具也不认，用户没法自己恢复。写 StartupApproved 之后，任务管理器里的开关和本程序一致。
/// </summary>
public static class StartupApproval
{
    private const byte StateEnabled = 0x02;
    private const byte StateEnabledVariant = 0x06;
    private const byte StateDisabled = 0x03;
    private const byte StateDisabledVariant = 0x07;
    private const int BlobLength = 12;

    /// <summary>某个来源对应的 StartupApproved 子键路径；不支持（如 RunOnce）返回 null。</summary>
    public static string? GetApprovedPath(string source) => source switch
    {
        "HKCU" => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        "HKLM" => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        "HKLM64" => @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
        "StartupFolder" => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
        _ => null
    };

    /// <summary>该来源是否支持 StartupApproved（RunOnce 不支持，只能沿用旧机制）。</summary>
    public static bool IsSupported(string source) => GetApprovedPath(source) != null;

    private static RegistryKey? OpenRoot(string source) => source switch
    {
        "HKCU" or "StartupFolder" => Registry.CurrentUser,
        "HKLM" or "HKLM64" => Registry.LocalMachine,
        _ => null
    };

    public static StartupApprovalState Read(string source, string valueName)
    {
        var path = GetApprovedPath(source);
        var root = OpenRoot(source);
        if (path == null || root == null || string.IsNullOrEmpty(valueName)) return StartupApprovalState.Unknown;

        try
        {
            using var key = root.OpenSubKey(path);
            if (key == null) return StartupApprovalState.Unknown;
            return ParseState(key.GetValue(valueName) as byte[]);
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("StartupApproval.Read", ex);
            return StartupApprovalState.Unknown;
        }
    }

    /// <summary>
    /// 写状态并回读验证。返回 false = 写失败或回读结果与期望不一致（调用方必须如实告诉用户，不能假装成功）。
    /// </summary>
    public static bool Write(string source, string valueName, bool enabled)
    {
        var path = GetApprovedPath(source);
        var root = OpenRoot(source);
        if (path == null || root == null || string.IsNullOrEmpty(valueName)) return false;

        try
        {
            using var key = root.CreateSubKey(path, writable: true);
            if (key == null) return false;

            var existing = key.GetValue(valueName) as byte[];
            var blob = BuildBlob(existing, enabled);
            if (blob == null) return false; // 已有记录格式不认识，宁可失败也不覆盖用户数据

            key.SetValue(valueName, blob, RegistryValueKind.Binary);

            var actual = ParseState(key.GetValue(valueName) as byte[]);
            return actual == (enabled ? StartupApprovalState.Enabled : StartupApprovalState.Disabled);
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("StartupApproval.Write", ex);
            return false;
        }
    }

    /// <summary>解析 12 字节状态块。缺失视为 Unknown（Windows 默认启用）。</summary>
    public static StartupApprovalState ParseState(byte[]? blob)
    {
        if (blob == null || blob.Length < 4) return StartupApprovalState.Unknown;

        return blob[0] switch
        {
            StateEnabled or StateEnabledVariant => StartupApprovalState.Enabled,
            StateDisabled or StateDisabledVariant => StartupApprovalState.Disabled,
            _ => StartupApprovalState.Unknown
        };
    }

    /// <summary>
    /// 生成新的状态块：保留原有的 8 字节时间戳，只改状态字节，并保留 0x06/0x07 这一"变体"，
    /// 避免把 Windows 自己写的记录改成另一种语义。已有记录短于 12 字节则返回 null（不猜格式）。
    /// </summary>
    public static byte[]? BuildBlob(byte[]? existing, bool enabled)
    {
        if (existing != null && existing.Length < BlobLength) return null;

        var blob = existing != null ? (byte[])existing.Clone() : new byte[BlobLength];
        var current = blob[0];

        blob[0] = enabled
            ? (current == StateDisabledVariant ? StateEnabledVariant : StateEnabled)
            : (current == StateEnabledVariant ? StateDisabledVariant : StateDisabled);

        return blob;
    }
}
