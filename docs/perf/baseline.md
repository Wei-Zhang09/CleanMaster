# 性能基线（BASE-1）

- 采集时间：2026-11-05 16:17–16:19
- 机器：Windows 11 家庭版 中文版 / i7-12700H（14C/20T）/ 15.6 GB / NVMe WD_BLACK SN770 1TB
- 运行时：.NET 9.0.3，Release 构建
- 数据文件：`docs/perf/baseline.jsonl`（扫描）、`docs/perf/coldstart.json`（冷启动）

## 方法

```powershell
# 扫描：Release + 预热一次后取第二次（热态）
$env:CLEANMASTER_BENCH=1; $env:CLEANMASTER_BENCH_OUT="docs/perf/baseline.jsonl"
dotnet test tests\CleanMaster.Tests.csproj -c Release --filter Category=Bench

# 冷启动：3 次，取中位数
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\bench-coldstart.ps1 -Runs 3
```

- 扫描用例由 `tests/Benchmarks/ScanBenchmarks.cs` 提供，未设置 `CLEANMASTER_BENCH=1` 时全部跳过，不影响日常 `dotnet test`。
- 冷启动用 `dotnet <dll>` 启动（apphost 清单是 `requireAdministrator`，跑 exe 会弹 UAC 无法无人值守），因此**不含 UAC 授权等待**；测的是运行时加载 + WPF + DI + 主窗口创建。
- 峰值工作集是**测试宿主进程**的整体峰值（含 xunit），不是纯净的应用内存，用于同机前后对比，不用于跨产品比较。

## 结果

| 场景 | 样本 | 文件数 | 数据量 | 耗时 | 分配内存 | 峰值工作集 |
|---|---|---|---|---|---|---|
| 大文件夹扫描（FolderScanService） | 合成树 200 目录×100 文件×4KB | 20,000 | 81.9 MB | **1,029 ms** | 5 MB | 74 MB |
| 全盘大文件扫描（ScanService.FindLargeFilesAsync） | 真实目录 `%LOCALAPPDATA%` | 441,350 | 50.1 GB | **57,133 ms** | 485 MB | 91 MB |
| 重复文件扫描（DuplicateService，三阶段哈希） | 合成树 3,000 文件×64KB，约 1/3 重复 | 3,000 | 196.6 MB | **1,171 ms** | 263 MB | 91 MB |
| 冷启动（dotnet host，不含 UAC） | 3 次 | — | — | **中位 1,175 ms**（1,115 / 1,175 / 2,084） | — | — |
| 全量规则扫描（真实规则库，STO-1 接线后补测） | 9 分类 / 160 项 | 160 | 逻辑 17.59 GB / 实际 17.53 GB | **6,810 ms** | — | — |

> 最后一行同时是 STO-1 的验收证据：逻辑 17,594,813,769 字节 vs 实际 17,526,882,317 字节，差 68 MB，来自压缩/稀疏文件（WER、缩略图缓存等）。修复前"可回收"会按逻辑大小高估这 68 MB。

### 派生指标

- 全盘扫描吞吐：441,350 文件 / 57.1 s ≈ **7,725 文件/秒**；50.1 GB / 57.1 s ≈ 877 MB/s（元数据与数据混合读取）。
- 全盘扫描分配：485 MB / 441,350 ≈ **1.15 KB/文件**（`FileInfo`、路径字符串、结果 List）。
- 大文件夹扫描吞吐：20,000 / 1.029 s ≈ **19,400 文件/秒**。
- 冷启动构成（`%APPDATA%\CleanMaster\logs\startup.log` 毫秒时间戳）：App 构造 → DI 容器 9 ms → MainWindow 创建 298 ms。即 **约 300 ms 是 WPF 窗口，其余约 800 ms 是 hostfxr/CoreCLR/WPF 程序集加载**。

## 结论（供 PF-1 使用）

1. **同一台机器上，两条遍历路径的吞吐差 2.5 倍**（19.4k/s vs 7.7k/s）。`FindLargeFilesAsync` 每文件 `new FileInfo(...)` 再取 `Length`，而 `FolderScanService` 的枚举更轻。先对齐这两条路径，收益比换语言大。
2. **分配量是内存压力的主因**（全盘 485 MB 分配、重复扫描 263 MB）。改为结构体式枚举（`FileSystemEnumerable` + `ref struct`）可以显著压低，属于 PF-1 的低风险项。
3. **冷启动 1.1 s 中约 800 ms 是运行时加载**，这是换 Rust 唯一能结构性消除的部分；但这 800 ms 换不来"兼容性"，只换来启动体感与体积。
4. 后续任何优化都要复跑本页命令，把新数字追加到同目录，与本次对照，不允许只凭感觉宣称"变快了"。

## 已知局限

- `%LOCALAPPDATA%` 首次扫描可能命中冷缓存，耗时偏高；跨次对比时需注意。
- 合成树的目录深度、文件大小分布与真实用户盘不同，只用于回归对比，不代表真实用户耗时。
- 冷启动不含 UAC，也不含安装目录在机械盘/网络盘的情况。
