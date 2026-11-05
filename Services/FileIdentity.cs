using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CleanMaster.Services;

/// <summary>
/// 物理文件身份：卷序列号 + 文件索引（NTFS FileId）。
/// 硬链接、目录 junction 指向同一份数据时，多个路径读出的身份完全相同。
/// 重复文件扫描必须按它去重，否则同一个物理文件的多条路径会被判成"重复"，
/// 而删除硬链接并不释放空间 —— 用户看到可回收几个 GB，实际一个字节都没少。
/// </summary>
public readonly record struct FileIdentity(uint VolumeSerialNumber, ulong FileIndex)
{
    /// <summary>读取失败时全为 0，视为"身份未知"，此时不做去重（宁可多报，不可漏报）。</summary>
    public bool IsKnown => VolumeSerialNumber != 0 && FileIndex != 0;
}

/// <summary>从已打开的文件句柄读取物理身份，避免为同一文件再开一次句柄。</summary>
public static class FileIdentityReader
{
    public static bool TryRead(SafeFileHandle handle, out FileIdentity identity)
    {
        identity = default;
        if (!OperatingSystem.IsWindows() || handle.IsInvalid || handle.IsClosed) return false;

        if (!GetFileInformationByHandle(handle, out var info)) return false;

        var index = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
        identity = new FileIdentity(info.VolumeSerialNumber, index);
        return identity.IsKnown;
    }

    // 逐字段用 uint 声明：FILETIME 在结构里是 2×DWORD，若写成 long 会被 CLR 按 8 字节对齐，
    // 整个结构偏移错位，读出来的身份全是垃圾值。这里保持 4 字节对齐，总长 52 字节。
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);
}
