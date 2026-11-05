using System.Runtime.InteropServices;
using CleanMaster.Services;
using Microsoft.Win32.SafeHandles;

namespace CleanMaster.Tests.Services;

public class AllocatedSizeTests : IDisposable
{
    private readonly string _dir;

    public AllocatedSizeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"CleanMasterAlloc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void TryRead_NormalFile_AllocatedAtLeastLogical()
    {
        var path = Path.Combine(_dir, "normal.bin");
        File.WriteAllBytes(path, new byte[64 * 1024]);

        var info = AllocatedSizeReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal(64 * 1024, info!.Value.LogicalBytes);
        // 分配大小按簇向上取整：>= 逻辑大小，但不会离谱地大
        Assert.InRange(info.Value.AllocatedBytes, 64 * 1024, 64 * 1024 + 1024 * 1024);
        Assert.False(info.Value.IsSparse);
    }

    [Fact]
    public void TryRead_SparseFile_AllocatedFarBelowLogical()
    {
        var path = Path.Combine(_dir, "sparse.bin");
        const long logicalLength = 32L * 1024 * 1024; // 32 MB 逻辑大小

        if (!TryCreateSparseFile(path, logicalLength))
            return; // 非 NTFS/无权限环境跳过

        var info = AllocatedSizeReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal(logicalLength, info!.Value.LogicalBytes);
        Assert.True(info.Value.IsSparse, "应识别为稀疏文件");
        // 实际只写了 4KB，占用必须远小于逻辑大小
        Assert.True(info.Value.AllocatedBytes < logicalLength / 4,
            $"稀疏文件实际占用应远小于逻辑大小，实际 {info.Value.AllocatedBytes}");
        Assert.True(info.Value.SavedByAllocation > 0);
    }

    [Fact]
    public void SumAllocated_MixedFiles_ReportsBothTotals()
    {
        var normal = Path.Combine(_dir, "n.bin");
        var sparse = Path.Combine(_dir, "s.bin");
        File.WriteAllBytes(normal, new byte[16 * 1024]);
        if (!TryCreateSparseFile(sparse, 8L * 1024 * 1024)) return;

        var (allocated, logical, fallback) = AllocatedSizeReader.SumAllocated(new[] { normal, sparse });

        Assert.Equal(0, fallback);
        Assert.Equal(16 * 1024 + 8L * 1024 * 1024, logical);
        Assert.True(allocated < logical, "稀疏文件应让实际占用小于逻辑大小");
    }

    [Fact]
    public void TryRead_MissingFile_ReturnsNull()
    {
        Assert.Null(AllocatedSizeReader.TryRead(Path.Combine(_dir, "nope.bin")));
    }

    [Fact]
    public void GetDirectorySizes_SparseInside_ReportsLogicalAndAllocatedSeparately()
    {
        var normal = Path.Combine(_dir, "n.bin");
        var sparse = Path.Combine(_dir, "s.bin");
        File.WriteAllBytes(normal, new byte[16 * 1024]);
        if (!TryCreateSparseFile(sparse, 16L * 1024 * 1024)) return;

        var (logical, allocated) = FileSystemUtils.GetDirectorySizes(_dir);

        Assert.True(logical >= 16L * 1024 * 1024, $"逻辑大小应含稀疏文件的 16MB，实际 {logical}");
        Assert.True(allocated < logical / 2, $"实际占用应远小于逻辑大小，实际 {allocated}");
    }

    [Fact]
    public void ReclaimableBytes_FallsBackToLogicalWhenAllocatedUnknown()
    {
        var item = new CleanMaster.Models.CleanableItem { SizeBytes = 1024 };
        Assert.Equal(1024, item.ReclaimableBytes);

        item.AllocatedBytes = 256;
        Assert.Equal(256, item.ReclaimableBytes);
    }

    [Fact]
    public void CategoryTotalSizeText_UsesAllocatedButItemSizeStaysLogical()
    {
        var result = new CleanMaster.Models.ScanCategoryResult
        {
            Items =
            {
                new CleanMaster.Models.CleanableItem { SizeBytes = 4096, AllocatedBytes = 512 },
                new CleanMaster.Models.CleanableItem { SizeBytes = 1024 }
            }
        };

        Assert.Equal(5120, result.TotalSize);        // 逻辑合计，供列表/排序
        Assert.Equal(1536, result.ReclaimableSize);  // 实际可回收
        Assert.Equal(ByteSizeFormatter.Format(1536), result.TotalSizeText);
    }

    /// <summary>建一个稀疏文件：标记 sparse → 设长度 → 只在开头写 4KB，其余不落盘。</summary>
    private static bool TryCreateSparseFile(string path, long logicalLength)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            if (!DeviceIoControl(handle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                return false;

            using var stream = new FileStream(handle, FileAccess.ReadWrite);
            stream.SetLength(logicalLength);
            stream.Position = 0;
            stream.Write(new byte[4096], 0, 4096);
            stream.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private const uint FsctlSetSparse = 0x000900C4;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
