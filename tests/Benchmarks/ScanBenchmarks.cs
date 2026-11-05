using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CleanMaster.Services;

namespace CleanMaster.Tests.Benchmarks;

/// <summary>
/// 性能基线（BASE-1）。只在设置 CLEANMASTER_BENCH=1 时运行，普通 dotnet test 不受影响。
///
/// 目的：给"扫描性能优化 / 是否值得换语言"提供可复跑的数字，而不是感觉。
/// 输出：CLEANMASTER_BENCH_OUT 指定的 JSON 文件（未设置则写到 %TEMP%\cm-bench-*.json），
/// 并在控制台打印 Markdown 表格。
///
/// 跑法：
///   CLEANMASTER_BENCH=1 CLEANMASTER_BENCH_OUT=docs/perf/baseline.json \
///     dotnet test tests/CleanMaster.Tests.csproj -c Release --filter Category=Bench
/// </summary>
[Trait("Category", "Bench")]
public class ScanBenchmarks : IDisposable
{
    private static bool Enabled => Environment.GetEnvironmentVariable("CLEANMASTER_BENCH") == "1";

    private readonly string _root;
    private readonly List<Result> _results = new();

    private sealed record Result(
        string Name,
        string Workload,
        int Files,
        long Bytes,
        long ElapsedMs,
        long AllocatedMb,
        long PeakWorkingSetMb);

    public ScanBenchmarks()
    {
        _root = Path.Combine(Path.GetTempPath(), "cm-bench-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        if (_results.Count == 0) return;

        // xunit 每个 [Fact] 一个实例，所以按 JSONL 追加：不依赖测试执行顺序，也不怕并发。
        var outPath = Environment.GetEnvironmentVariable("CLEANMASTER_BENCH_OUT")
                      ?? Path.Combine(Path.GetTempPath(), "cm-bench.jsonl");
        // 相对路径按仓库根解析：测试进程的当前目录是 tests/bin/<cfg>/<tfm>，直接用会把结果写进 bin。
        var full = Path.IsPathRooted(outPath)
            ? outPath
            : Path.Combine(FindRepoRoot(), outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        foreach (var r in _results)
        {
            File.AppendAllText(full, JsonSerializer.Serialize(r) + Environment.NewLine, Encoding.UTF8);
            Console.WriteLine($"BENCH | {r.Name} | {r.Workload} | files={r.Files} | " +
                              $"bytes={r.Bytes} | {r.ElapsedMs} ms | alloc={r.AllocatedMb} MB | peakWS={r.PeakWorkingSetMb} MB");
        }
        Console.WriteLine($"BENCH_JSONL={full}");
    }

    private static Result Measure(string name, string workload, int files, long bytes, Action action)
    {
        // 预热一次：排除 JIT / 元数据加载对基线的影响，记录第二次（热态）耗时。
        action();

        var proc = Process.GetCurrentProcess();
        var allocBefore = GC.GetTotalAllocatedBytes(precise: true);

        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();

        proc.Refresh();
        return new Result(
            name,
            workload,
            files,
            bytes,
            sw.ElapsedMilliseconds,
            (GC.GetTotalAllocatedBytes(precise: true) - allocBefore) / 1024 / 1024,
            proc.PeakWorkingSet64 / 1024 / 1024);
    }

    /// <summary>造一棵受控目录树：dirs 个目录 × filesPerDir 个文件 × sizeBytes 字节。</summary>
    private string CreateTree(int dirs, int filesPerDir, int sizeBytes, int duplicateEvery = 0)
    {
        var payload = new byte[sizeBytes];
        var distinct = 0;
        for (var d = 0; d < dirs; d++)
        {
            var dir = Path.Combine(_root, $"d{d:D4}");
            Directory.CreateDirectory(dir);
            for (var f = 0; f < filesPerDir; f++)
            {
                // duplicateEvery > 0 时，每 N 个文件复用同一份内容，制造重复候选
                if (duplicateEvery > 0 && distinct % duplicateEvery != 0)
                {
                    File.WriteAllBytes(Path.Combine(dir, $"f{f:D4}.bin"), payload);
                }
                else
                {
                    new Random(distinct++).NextBytes(payload);
                    File.WriteAllBytes(Path.Combine(dir, $"f{f:D4}.bin"), payload);
                }
            }
        }
        return _root;
    }

    [Fact]
    public async Task Bench_FolderScan_Synthetic20k()
    {
        if (!Enabled) return;

        const int dirs = 200, perDir = 100, size = 4096;
        CreateTree(dirs, perDir, size);

        var service = new FolderScanService();
        _results.Add(Measure("大文件夹扫描", "合成树 200 目录×100 文件×4KB", dirs * perDir, (long)dirs * perDir * size,
            () => service.ScanLargeFoldersAsync(_root, minSizeBytes: 0).GetAwaiter().GetResult()));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Bench_LargeFiles_RealUserLocalAppData()
    {
        if (!Enabled) return;

        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (!Directory.Exists(target)) return;

        var service = new ScanService();
        var (files, bytes) = await CountTreeAsync(target);

        _results.Add(Measure("全盘大文件扫描", $"真实目录 {target}", files, bytes,
            () => service.FindLargeFilesAsync(target, minSizeBytes: 50 * 1024 * 1024).GetAwaiter().GetResult()));
    }

    [Fact]
    public async Task Bench_ScanAll_RealRules()
    {
        if (!Enabled) return;

        // 真实规则库全量扫描：验证扫描链路端到端可用，并给出"一次完整清理扫描"的耗时基线。
        var service = new ScanService();
        var sw = Stopwatch.StartNew();
        var results = await service.ScanAllAsync();
        sw.Stop();

        var items = results.Sum(r => r.ItemCount);
        var logical = results.Sum(r => r.TotalSize);
        var reclaimable = results.Sum(r => r.ReclaimableSize);
        Console.WriteLine($"BENCH | 全量规则扫描 | {results.Count} 分类 | items={items} | " +
                          $"logical={logical} | reclaimable={reclaimable} | {sw.ElapsedMilliseconds} ms");
        Assert.NotNull(results);
    }

    [Fact]
    public async Task Bench_DuplicateScan_Synthetic3k()
    {
        if (!Enabled) return;

        const int dirs = 30, perDir = 100, size = 64 * 1024;
        CreateTree(dirs, perDir, size, duplicateEvery: 3);

        var service = new DuplicateService();
        _results.Add(Measure("重复文件扫描", "合成树 3,000 文件×64KB，约 1/3 重复", dirs * perDir, (long)dirs * perDir * size,
            () => service.FindDuplicatesAsync(new[] { _root }, minFileSize: 1024).GetAwaiter().GetResult()));
        await Task.CompletedTask;
    }

    /// <summary>从测试输出目录向上找仓库根（含 CleanMaster.sln 的那一层）。</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CleanMaster.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private static async Task<(int Files, long Bytes)> CountTreeAsync(string path)
    {
        var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        return await Task.Run(() =>
        {
            var files = 0;
            long bytes = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", opts))
                {
                    files++;
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return (files, bytes);
        });
    }
}
