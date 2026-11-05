using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CleanMaster.Services;

/// <summary>
/// 等待一个进程的"整棵进程树"退出。
///
/// 为什么需要它：大量卸载器（NSIS/Inno 自定义脚本、厂商启动器）会先 fork 子进程再立刻退出，
/// 只等父进程会让我们在卸载还在进行时就开始扫残留 —— 扫出一批其实正在被删除的"假残留"，
/// 用户一旦点了清理，就会和卸载器抢同一个文件。
///
/// 实现用 Toolhelp32 快照而不是 WMI：不引入 System.Management（本项目启动期刻意不加载 WMI），
/// 也不依赖进程句柄权限（卸载器常常是另一个用户/提权上下文）。
/// </summary>
public static class ProcessTree
{
    private const uint Th32csSnapProcess = 0x00000002;

    /// <summary>
    /// 轮询直到 rootPid 的所有后代进程都退出。
    /// 返回 true = 全部退出；false = 超时仍有存活（调用方应如实告知用户）。
    /// </summary>
    public static async Task<bool> WaitForDescendantsAsync(
        int rootPid,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (rootPid <= 0) return true;

        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (GetLiveDescendants(rootPid).Count == 0) return true;

            if (DateTime.UtcNow >= deadline) return false;

            await Task.Delay(200, ct);
        }
    }

    /// <summary>返回仍存活的、父进程链指向 rootPid 的进程 ID（不含 rootPid 自己）。</summary>
    public static List<int> GetLiveDescendants(int rootPid)
    {
        var childrenByParent = SnapshotParentMap();
        if (childrenByParent.Count == 0) return new List<int>();

        var result = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(rootPid);
        var visited = new HashSet<int> { rootPid };

        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!childrenByParent.TryGetValue(parent, out var children)) continue;

            foreach (var child in children)
            {
                if (!visited.Add(child)) continue; // 防环（理论上不该出现）
                result.Add(child);
                queue.Enqueue(child);
            }
        }

        return result;
    }

    /// <summary>进程 ID → 其子进程 ID 列表。快照里出现的都是当前存活进程。</summary>
    private static Dictionary<int, List<int>> SnapshotParentMap()
    {
        var map = new Dictionary<int, List<int>>();
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return map;

        try
        {
            var entry = new ProcessEntry32W { dwSize = (uint)Marshal.SizeOf<ProcessEntry32W>() };
            if (!Process32FirstW(snapshot, ref entry)) return map;

            do
            {
                var pid = (int)entry.th32ProcessID;
                var parent = (int)entry.th32ParentProcessID;
                if (pid <= 0 || parent <= 0 || pid == parent) continue;

                if (!map.TryGetValue(parent, out var list))
                {
                    list = new List<int>();
                    map[parent] = list;
                }
                list.Add(pid);

                entry.dwSize = (uint)Marshal.SizeOf<ProcessEntry32W>();
            }
            while (Process32NextW(snapshot, ref entry));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SnapshotParentMap: {ex.Message}");
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return map;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref ProcessEntry32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
