# HANDOVER — CleanMaster 本轮改造（2026-11-05）

## 目标

起因是两个问题：① 参考 MangoDisk（Rust + Tauri 的跨平台清理工具）看有什么可借鉴；② 是否该改用 Rust 解决兼容性与性能。

结论是**不换语言**，改为在现有 WPF/.NET 9 代码上补齐能力，并把清理规则外置成数据。本轮把这套判断全部落地。

## 关键结论（带证据，别凭印象推翻）

1. **兼容性不是语言问题**。我们历史上那次 `0xC0000602` 启动失败来自「.NET 9 单文件自解压」的加载链，`16c6e63` 改多文件铺装后已消除。.NET 9 官方支持 Win10 1607+；而 Tauri 要 WebView2（Win7/8.1 停在 v109），兼容面只会更窄。详见 `docs/architecture/ADR-001-deprecate-win10-fork.md`。
2. **换 Rust 的性能收益是常数因子**，真正的瓶颈是 syscall 用错：`Directory.GetFiles`（只给名字）+ 每文件 `new FileInfo`（一次 stat）。改成 `DirectoryInfo.EnumerateFiles`（目录扫描自带元数据）后，全盘大文件扫描 **57.1s → 5.5s（10.3×）**、大文件夹扫描 **1.03s → 0.074s（13.9×）**，且用 Python `os.scandir` 独立统计交叉核对确认没有漏扫。
3. **可回收空间必须按实际占用（allocated）算**。真实规则扫描里逻辑 17.59 GB vs 实际 17.53 GB，差 68 MB 全是压缩/稀疏文件造成的虚报。
4. **卸载不能只看退出码**。UAC 被拒、卸载器闪退、用户关弹窗都可能是 0；现在等整棵进程树 + 回读注册表登记项，六态结果。
5. **启动项禁用要写 `StartupApproved`**（任务管理器同一份状态），不是给注册表值名加 `Disabled_` 前缀——后者会在任务管理器里显示成 `Disabled_Disabled_Disabled_...` 这种怪条目（本机注册表里真有一条这样的历史遗留）。
6. **规则要能被非开发者审计**。124 条规则已数据化 119 条（TOML + evidence + 构建期校验），剩 5 条是"规则工厂"（RecycleBin 特例、游戏/Electron 动态发现），数据模型表达不了。

## 当前状态

- 分支 `main`，工作区干净，24 个提交（`16c6e63..248b757`），65 文件 +6658/−1599
- `dotnet build` 0 错误 0 警告；`dotnet test` **450/450 通过**
- 版本号 **3.2.1**；v3.2.0 已发布到 GitHub Releases，v3.2.1 补了图标与公众号信息（阿伟同学学AI）
- 规则：124 条 = 119 条 TOML（`Rules/Catalog/` 13 个文件）+ 5 条代码；5 个命名解析器、11 个路径占位符
- 新增 `tools/CleanMaster.Cli`：只读命令行（scan/largefiles/duplicates/startup/apps），免提权、`--json`、8 个 e2e 用例
- 看板（`hermes kanban --board cleanmaster`）除 STARTUP-2 外全部 done

### 性能基线（同机：i7-12700H / NVMe / .NET 9.0.3 Release）

| 场景 | 改造前 | 现在 |
|---|---|---|
| 全盘大文件扫描（441k 文件 / 50 GB） | 57,133 ms | 5,524 ms |
| 大文件夹扫描（20k 文件） | 1,029 ms | 132 ms（ST-1 手动递归后；PF-1 时 74 ms） |
| 重复文件扫描（3k 文件 / 64KB） | 1,171 ms | 1,094 ms（哈希为主，未优化） |
| 冷启动（dotnet host，不含 UAC） | 1,175 ms | 未改（其中约 800 ms 是运行时加载） |

复跑方式见 `docs/perf/baseline.md`。

## 待办

1. **STARTUP-2 高级启动项**（看板唯一未完成项）：服务/计划任务/打包应用启动项，折叠在「高级（不建议修改）」+ 二次确认 + 不提供一键全禁。**建议先出交互方案再写代码**（高风险区域）。
2. ~~发版~~：已完成。3.2.0 已由 CI 发布；3.2.1（新图标 + 公众号改为「阿伟同学学AI」）已本地打包 `installer/CleanMaster-Setup-v3.2.1.exe` 并推送 tag。
3. 可选：把 `CleanMaster.Cli` 纳入安装包（当前只在源码树里，供排查与回归）。
4. 可选：`CleanService` 的"已释放空间"对硬链接仍按 allocated 计（删硬链接实际不释放），未处理——缓存规则里硬链接罕见，属已知小偏差。

## 待用户决策

- STARTUP-2 的交互细节（哪些项进「高级」、禁用前的提示文案、是否允许批量）。
- 是否现在发 3.2.0。
- CLI 是否随安装包发布。

## 本轮的坑（下次别再踩）

- **批量手术迁移规则**：块结束判定要按括号深度归零（`Description = "..." },` 这种同行闭合会让"一个块"吞掉后面所有规则）；不要原地删除，重建整个 list 区域；写源码前断言"删除的名字集合 == 生成的名字集合"。详见 `wpf-dotnet-desktop-pitfalls` 技能。
- **`X509Certificate.CreateFromSignedFile` 会撒谎**：签名正常的 `notepad.exe` 被判成无签名（它只读内嵌签名，系统文件多是 catalog 签名）。要用 WinVerifyTrust + catalog 回退。
- **纯中文规则名生成空 slug** → id 撞成 `app.`，规则被校验器丢弃（快照从 121 掉到 119）。id 必须 ASCII，中文名要显式给 id。
- **手工写的迁移条目要靠快照兜底**：曾把 UWP LocalCache 的安全等级从 Safe 写成 Caution，被 `RuleSnapshotTests` 抓出。
