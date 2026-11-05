using System.IO;
using System.Runtime.InteropServices;

namespace CleanMaster.Services;

/// <summary>
/// 文件的两种大小口径：
/// <list type="bullet">
/// <item><see cref="LogicalBytes"/>：逻辑大小（<see cref="FileInfo.Length"/>），即"文件看起来多大"。</item>
/// <item><see cref="AllocatedBytes"/>：实际占用的磁盘空间（按簇向上取整，稀疏/压缩/去重后可能远小于逻辑大小）。</item>
/// </list>
/// 清理工具必须按 <see cref="AllocatedBytes"/> 报"可回收空间"：一个 10 GB 的稀疏虚拟机磁盘文件，
/// 逻辑大小 10 GB、实际占用可能只有 200 MB，按逻辑大小报会严重高估，用户清完发现没腾出空间。
/// </summary>
public readonly record struct FileSizeInfo(
    long LogicalBytes,
    long AllocatedBytes,
    bool IsSparse,
    bool IsCompressed)
{
}

/// <summary>
/// 读取文件的"实际占用"大小。Windows 用 <c>GetCompressedFileSizeW</c>（对稀疏/压缩文件返回真实分配量），
/// 非 Windows 平台退化为逻辑大小（本产品只发布 Windows 版，这里只是让单元测试与将来移植不必分叉）。
/// </summary>
public static class AllocatedSizeReader
{
    /// <summary>GetCompressedFileSize 的错误返回值（INVALID_FILE_SIZE）。</summary>
    private const uint InvalidFileSize = 0xFFFFFFFF;

    /// <summary>
    /// 用已拿到的 <see cref="FileInfo"/> 判断是否需要额外查询：
    /// 普通文件（非稀疏、非压缩）分配大小≈逻辑大小，直接返回，**不产生额外系统调用**；
    /// 只有稀疏/压缩文件才去问内核真实分配量。扫描热路径必须走这个重载。
    /// </summary>
    public static FileSizeInfo FromFileInfo(FileInfo fi)
    {
        var logical = fi.Length;
        var attributes = fi.Attributes;
        var sparse = attributes.HasFlag(FileAttributes.SparseFile);
        var compressed = attributes.HasFlag(FileAttributes.Compressed);

        if (!sparse && !compressed)
            return new FileSizeInfo(logical, logical, false, false);

        return TryRead(fi.FullName) ?? new FileSizeInfo(logical, logical, sparse, compressed);
    }

    public static FileSizeInfo? TryRead(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            var logical = new FileInfo(path).Length;

            if (!OperatingSystem.IsWindows())
                return new FileSizeInfo(logical, logical, false, false);

            var low = GetCompressedFileSizeW(path, out var high);
            if (low == InvalidFileSize && Marshal.GetLastPInvokeError() != 0)
                return null;

            var allocated = ((long)high << 32) | low;
            return new FileSizeInfo(
                logical,
                allocated,
                attributes.HasFlag(FileAttributes.SparseFile),
                attributes.HasFlag(FileAttributes.Compressed));
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);
}
