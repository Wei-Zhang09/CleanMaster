using System.Diagnostics;
using System.IO;

namespace CleanMaster.Services;

public static class FileSystemUtils
{
    /// <summary>
    /// 计算目录总大小。
    /// 改进点:
    /// 1. 跳过 ReparsePoint (junction/symlink/mount point), 防止跨盘循环扫描
    /// 2. 并行遍历子目录, 大目录(如 JetBrains caches)扫描速度提升 3-5 倍
    /// 3. 当 maxDepth=-1 时全递归, 否则只下钻到指定深度
    /// </summary>
    public static long GetDirectorySize(string path, int maxDepth = -1)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;

        // 当前层的 reparse point 检查: 如果 path 本身就是 reparse point, 不进入
        if (IsReparsePoint(path)) return 0;

        try
        {
            return GetDirectorySizeCore(path, maxDepth, depth: 0);
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("GetDirectorySize", ex);
            return 0;
        }
    }

    private static long GetDirectorySizeCore(string path, int maxDepth, int depth)
    {
        long size = 0;

        // 1) 累加当前目录下的文件大小 (不递归)
        EnumerationOptions fileOpts = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", fileOpts))
            {
                try { size += new FileInfo(file).Length; }
                catch (Exception ex) { Debug.WriteLine($"GetDirectorySize-File: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"GetDirectorySize-EnumFiles: {ex.Message}"); }

        // 2) 是否继续下钻
        if (maxDepth >= 0 && depth >= maxDepth) return size;

        // 3) 枚举子目录, 跳过 reparse points, 并行累加
        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(path, "*",
                new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetDirectorySize-EnumDirs: {ex.Message}");
            return size;
        }

        // 4) 并行处理子目录。粒度: 每个子目录作为一个工作单元。
        //    对于有大量小文件的目录 (JetBrains caches 上百万文件),
        //    多线程能显著降低扫描时间。线程数限制为 ProcessorCount,
        //    避免在线程切换上浪费 CPU。
        if (subDirs.Length == 0) return size;
        if (subDirs.Length == 1)
        {
            // 单子目录场景不需要并行开销
            if (!IsReparsePoint(subDirs[0]))
                size += GetDirectorySizeCore(subDirs[0], maxDepth, depth + 1);
            return size;
        }

        // 多子目录: 并行
        try
        {
            object lockObj = new();
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8)
            };
            Parallel.ForEach(subDirs, options, (sub, state) =>
            {
                if (IsReparsePoint(sub)) return;
                var subSize = GetDirectorySizeCore(sub, maxDepth, depth + 1);
                if (subSize > 0)
                {
                    lock (lockObj) { size += subSize; }
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetDirectorySize-Parallel: {ex.Message}");
        }

        return size;
    }

    /// <summary>
    /// 检测路径是否为 reparse point (junction / symlink / mount point)。
    /// 跳过这些可以避免跨盘循环扫描、跟随到网络驱动器等异常情况。
    /// </summary>
    private static bool IsReparsePoint(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            // 异常时保守返回 true, 跳过该目录 (避免误入循环)
            return true;
        }
    }
}
