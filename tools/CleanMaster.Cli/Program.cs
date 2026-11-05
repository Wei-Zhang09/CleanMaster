using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CleanMaster.Models;
using CleanMaster.Services;

namespace CleanMaster.Cli;

/// <summary>
/// 清理引擎命令行入口。与 GUI 共用同一套 service，不重复实现扫描/清理逻辑。
///
/// 设计约束：
/// - **只读**：本 CLI 只做扫描与预览（dry-run），没有任何删除命令。删除必须走 GUI 的确认流程。
/// - **不需要管理员**：独立于主程序的 requireAdministrator 清单，可在 CI 与普通命令行直接跑。
/// - **机器可读**：--json 输出稳定结构，供 e2e 断言与规则迁移前后对比。
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitUsage = 2;

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        if (args.Length == 0 || Is(args[0], "help", "-h", "--help"))
        {
            PrintHelp();
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        var command = args[0].ToLowerInvariant();
        var options = Options.Parse(args.Skip(1).ToArray());

        try
        {
            return command switch
            {
                "version" or "--version" => PrintVersion(options),
                "scan" => Scan(options),
                "largefiles" => LargeFiles(options),
                "duplicates" => Duplicates(options),
                "startup" => Startup(options),
                "apps" => Apps(options),
                _ => Unknown(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            if (options.Verbose) Console.Error.WriteLine(ex);
            return ExitError;
        }
    }

    // ---------------- 命令 ----------------

    private static int PrintVersion(Options options)
    {
        var version = Version();
        if (options.Json)
            WriteJson(new { tool = "cleanmaster-cli", version });
        else
            Console.WriteLine($"CleanMaster CLI {version}");
        return ExitOk;
    }

    private static int Scan(Options options)
    {
        var service = new ScanService();
        var sw = Stopwatch.StartNew();
        var categories = service.ScanAllAsync().GetAwaiter().GetResult();
        sw.Stop();

        var items = categories.SelectMany(c => c.Items.Select(i => new
        {
            category = c.Category.ToString(),
            categoryName = c.DisplayName,
            name = i.Name,
            path = i.FullPath,
            logicalBytes = i.SizeBytes,
            reclaimableBytes = i.ReclaimableBytes,
            safety = i.Safety.ToString(),
            isDirectory = i.IsDirectory
        })).ToList();

        var payload = new
        {
            tool = "cleanmaster-cli",
            command = "scan",
            version = Version(),
            generatedAt = DateTimeOffset.Now,
            elapsedMs = sw.ElapsedMilliseconds,
            totals = new
            {
                categories = categories.Count,
                items = items.Count,
                logicalBytes = categories.Sum(c => c.TotalSize),
                reclaimableBytes = categories.Sum(c => c.ReclaimableSize)
            },
            items
        };

        if (options.Json)
        {
            WriteJson(payload);
            return ExitOk;
        }

        Console.WriteLine($"扫描完成：{categories.Count} 个分类 / {items.Count} 项，耗时 {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"逻辑大小 {ByteSizeFormatter.Format(payload.totals.logicalBytes)}，" +
                          $"可回收 {ByteSizeFormatter.Format(payload.totals.reclaimableBytes)}");
        Console.WriteLine();
        foreach (var group in categories.OrderByDescending(c => c.ReclaimableSize))
        {
            Console.WriteLine($"[{group.DisplayName}] {group.ItemCount} 项  " +
                              $"{ByteSizeFormatter.Format(group.ReclaimableSize)}");
            foreach (var item in group.Items.OrderByDescending(i => i.ReclaimableBytes).Take(10))
                Console.WriteLine($"    {ByteSizeFormatter.Format(item.ReclaimableBytes),10}  {item.Name}");
        }
        return ExitOk;
    }

    private static int LargeFiles(Options options)
    {
        var path = options.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var minSize = options.MinSizeBytes ?? 100L * 1024 * 1024;

        var service = new ScanService();
        var sw = Stopwatch.StartNew();
        var files = service.FindLargeFilesAsync(path, minSize).GetAwaiter().GetResult();
        sw.Stop();

        if (options.Json)
        {
            WriteJson(new
            {
                tool = "cleanmaster-cli",
                command = "largefiles",
                version = Version(),
                generatedAt = DateTimeOffset.Now,
                path,
                minSizeBytes = minSize,
                elapsedMs = sw.ElapsedMilliseconds,
                totals = new
                {
                    files = files.Count,
                    bytes = files.Sum(f => f.SizeBytes)
                },
                files = files.Select(f => new
                {
                    name = f.FileName,
                    path = f.FullPath,
                    sizeBytes = f.SizeBytes,
                    lastModified = f.LastModified,
                    extension = f.Extension,
                    safety = f.SafetyHint,
                    fileType = f.FileType
                })
            });
            return ExitOk;
        }

        Console.WriteLine($"{path} 下 ≥{ByteSizeFormatter.Format(minSize)} 的文件：{files.Count} 个，" +
                          $"合计 {ByteSizeFormatter.Format(files.Sum(f => f.SizeBytes))}，耗时 {sw.ElapsedMilliseconds} ms");
        foreach (var f in files.Take(options.Limit ?? 20))
            Console.WriteLine($"    {ByteSizeFormatter.Format(f.SizeBytes),10}  {f.FullPath}");
        return ExitOk;
    }

    private static int Duplicates(Options options)
    {
        var path = options.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var minSize = options.MinSizeBytes ?? 1024L * 1024;

        var service = new DuplicateService();
        var sw = Stopwatch.StartNew();
        var groups = service.FindDuplicatesAsync(new[] { path }, minSize).GetAwaiter().GetResult();
        sw.Stop();

        if (options.Json)
        {
            WriteJson(new
            {
                tool = "cleanmaster-cli",
                command = "duplicates",
                version = Version(),
                generatedAt = DateTimeOffset.Now,
                path,
                minSizeBytes = minSize,
                elapsedMs = sw.ElapsedMilliseconds,
                linkedPathsSkipped = service.LinkedPathsSkipped,
                totals = new
                {
                    groups = groups.Count,
                    wastedBytes = groups.Sum(g => g.WastedSpace)
                },
                groups = groups.Select(g => new
                {
                    hash = g.Hash,
                    fileSizeBytes = g.FileSize,
                    wastedBytes = g.WastedSpace,
                    files = g.Files.Select(f => new { name = f.FileName, path = f.FullPath, sizeBytes = f.SizeBytes })
                })
            });
            return ExitOk;
        }

        Console.WriteLine($"{path} 下 ≥{ByteSizeFormatter.Format(minSize)} 的重复文件：{groups.Count} 组，" +
                          $"可回收 {ByteSizeFormatter.Format(groups.Sum(g => g.WastedSpace))}，耗时 {sw.ElapsedMilliseconds} ms");
        if (service.LinkedPathsSkipped > 0)
            Console.WriteLine($"（已跳过 {service.LinkedPathsSkipped} 条指向同一物理文件的硬链接/junction 路径）");
        foreach (var g in groups.Take(options.Limit ?? 20))
            Console.WriteLine($"    {ByteSizeFormatter.Format(g.WastedSpace),10}  {g.Files.Count} 份  {g.Files[0].FullPath}");
        return ExitOk;
    }

    private static int Startup(Options options)
    {
        var service = new SoftwareService();
        var items = service.GetStartupItems()
            .Select(i => new
            {
                name = i.Name,
                command = i.Command,
                source = i.Source,
                enabled = i.IsEnabled,
                legacyDisabled = i.IsLegacyDisabled,
                signature = service.GetStartupItemSignature(i)
            })
            .ToList();

        if (options.Json)
        {
            WriteJson(new
            {
                tool = "cleanmaster-cli",
                command = "startup",
                version = Version(),
                generatedAt = DateTimeOffset.Now,
                totals = new
                {
                    items = items.Count,
                    enabled = items.Count(i => i.enabled)
                },
                items
            });
            return ExitOk;
        }

        Console.WriteLine($"启动项 {items.Count} 个（启用 {items.Count(i => i.enabled)}）：");
        foreach (var i in items)
        {
            var state = i.enabled ? "启用" : "禁用";
            var sig = i.signature switch { "Signed" => "有签名", "Unsigned" => "无签名", _ => "签名未知" };
            Console.WriteLine($"    [{state}] [{i.source}] [{sig}] {i.name}");
        }
        return ExitOk;
    }

    private static int Apps(Options options)
    {
        var service = new SoftwareService();
        var apps = service.GetInstalledSoftware();

        if (options.Json)
        {
            WriteJson(new
            {
                tool = "cleanmaster-cli",
                command = "apps",
                version = Version(),
                generatedAt = DateTimeOffset.Now,
                totals = new { apps = apps.Count },
                apps = apps.Select(a => new
                {
                    name = a.Name,
                    publisher = a.Publisher,
                    version = a.Version,
                    installLocation = a.InstallLocation,
                    scope = a.RegistryHive,
                    estimatedSizeBytes = a.EstimatedSize
                })
            });
            return ExitOk;
        }

        Console.WriteLine($"已安装软件 {apps.Count} 个：");
        foreach (var a in apps.Take(options.Limit ?? 50))
            Console.WriteLine($"    [{a.RegistryHive}] {a.Name}  {a.Version}  {a.SizeText}");
        return ExitOk;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令：{command}");
        PrintHelp();
        return ExitUsage;
    }

    // ---------------- 辅助 ----------------

    private static string Version()
        // 版本号单一来源：读主程序集（CleanMaster.csproj 的 <Version>），
        // 不要读 CLI 自己的程序集版本，否则会和产品版本脱节。
        => typeof(ScanService).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private static bool Is(string value, params string[] candidates)
        => candidates.Any(c => string.Equals(value, c, StringComparison.OrdinalIgnoreCase));

    private static void WriteJson(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            // 中文不转义成 \uXXXX，便于人直接看与 grep
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Console.WriteLine(json);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            CleanMaster CLI —— 清理引擎命令行（只读，不做任何删除）

            用法：
              cleanmaster-cli <命令> [选项]

            命令：
              scan                      扫描可清理项（等同 GUI 的磁盘清理预览）
              largefiles [路径]         查找大文件（默认用户目录，≥100MB）
              duplicates [路径]         查找重复文件（默认用户目录，≥1MB）
              startup                   列出启动项及其启用状态与签名
              apps                      列出已安装软件
              version                   显示版本

            选项：
              --json                    输出机器可读 JSON
              --path <路径>             指定扫描路径（largefiles / duplicates）
              --min-size <字节>         最小文件大小
              --limit <N>               控制台最多显示 N 条
              --verbose                 出错时打印堆栈

            退出码：0 成功 / 1 出错 / 2 用法错误

            说明：本工具不提供删除命令。删除必须走 GUI 的确认流程。
            """);
    }

    private sealed class Options
    {
        public bool Json { get; private init; }
        public bool Verbose { get; private init; }
        public string? Path { get; private init; }
        public long? MinSizeBytes { get; private init; }
        public int? Limit { get; private init; }

        public static Options Parse(string[] args)
        {
            var json = false;
            var verbose = false;
            string? path = null;
            long? minSize = null;
            int? limit = null;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg.ToLowerInvariant())
                {
                    case "--json":
                        json = true;
                        break;
                    case "--verbose":
                        verbose = true;
                        break;
                    case "--path":
                        if (i + 1 < args.Length) path = args[++i];
                        break;
                    case "--min-size":
                        if (i + 1 < args.Length && long.TryParse(args[++i], out var size)) minSize = size;
                        break;
                    case "--limit":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var n)) limit = n;
                        break;
                    default:
                        // 位置参数：第一个非选项参数当作路径
                        if (!arg.StartsWith("-", StringComparison.Ordinal) && path == null) path = arg;
                        break;
                }
            }

            return new Options { Json = json, Verbose = verbose, Path = path, MinSizeBytes = minSize, Limit = limit };
        }
    }
}
