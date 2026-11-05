# ADR-001：废弃 CleanMaster-Win10 独立分支

- 状态：已接受（2026-11-05）
- 相关：`D:\HermesWorkSpace\CleanMaster-Win10\docs\architecture\ADR-001-runtime-and-helper.md`（原决策，现被本 ADR 取代）

## 背景

原 CleanMaster 曾用 .NET 9 自包含**单文件**发布，并在部分 Windows 10 x64 21H2 机器上启动即 `0xC0000602`（`STATUS_FAIL_FAST_EXCEPTION`），且应用日志未生成。为绕开该问题，建立了独立的 `CleanMaster-Win10` 项目：.NET Framework 4.8 WPF + 拆分 `CleanMaster.Helper.exe` 提权，共 5,929 行代码，功能只覆盖磁盘清理/大文件/重复文件/磁盘分析/设置，软件管理、启动项、系统清理全部暂缓。

## 事实核对

1. `0xC0000602` 是启动阶段的 fail-fast，属于**宿主/加载链**故障，不是"Win10 版本不支持 .NET 9"。.NET 9 官方支持矩阵为 Windows 10 1607 及以上（`dotnet/core` `release-notes/9.0/supported-os.md`）。
2. 原 Win10 版 ADR 自己给出的修法是「传统文件夹发布，不使用单文件自解压」「主程序启动阶段不加载 WMI、Registry、System.Drawing、网络和扫描服务」。
3. 主分支提交 `16c6e63 build: 去掉单文件发布，改为多文件铺装` 已经落地了第 2 条的核心部分。

即：该分支存在的技术前提已经消失，而它继续存在要付两份成本——功能永久落后（缺软件管理/启动项/系统清理/在线更新）、规则库与安全修复要双份维护。

## 决策

废弃 `CleanMaster-Win10` 独立分支，整目录归档，不再维护、不再发版、不再合入规则修复。主分支为唯一产品线。

## 理由

- 兼容性门槛由**依赖的运行时/系统组件**决定，不因换语言而改变。放弃单文件自解压后，.NET 9 自包含多文件铺装与原生程序在 Win10 上的启动链风险已同级。
- 双线维护的实际代价是"每条清理规则、每个删除安全修复都要写两遍，且 Win10 线没有测试覆盖"，这是**误删风险**的直接来源，比启动兼容风险更高。
- 若将来确实出现某类机器仍无法启动，正确做法是"主分支 + 启动诊断/降级开关"，而不是再分叉一个项目。

## 需要从该分支抢救的设计（保留文档，不保留代码）

1. **主程序 asInvoker + 独立提权 Helper**：主界面不依赖 UAC 成功即可显示，这一点主分支应当对齐。
2. **两阶段清理（dry-run 预览 → 用户确认 → 提权执行）**：与主分支现有流程合并时以此为准。
3. **启动期不加载 WMI/Registry/网络/扫描服务**：降低启动链长度。
4. **脱敏诊断包**（不含机器码、密钥、个人文件名）：主分支尚无等价能力。

## 后果

- 正面：单一规则库、单一安全修复路径、单一发布流水线；测试与 CI 只覆盖一套代码。
- 负面：不再有 .NET Framework 4.8 兜底路径；若未来需要支持 Win10 早期版本或极简镜像，需要重新评估（届时应作为主分支的发布变体，而非独立项目）。
- 归档位置：`D:\HermesWorkSpace\_archive\CleanMaster-Win10-deprecated-20261105`（含 tar 备份）。

## 未决事项

- ~~`app.manifest` / `csproj` 中 `SupportedOSPlatformVersion` 为 `7.0`，与 .NET 9 实际支持范围（Win10 1607+）不符~~ —— 已核查，**决定不改**：`net9.0-windows` 的 `TargetPlatformVersion` 固定为 7.0，直接抬高 `SupportedOSPlatformVersion` 会报 `NETSDK1135`；改用 `net9.0-windows10.0.19041.0` 会激活 CsWinRT 投影生成器，产出需要 `/unsafe` 的代码（CS0227），等于为一个纯元数据问题给安全敏感的程序引入 unsafe 与一整套 WinRT 投影。对外支持范围已由 README 与 `app.manifest` 的 `supportedOS` 声明为 Win10/11，够用。
- 是否把"启动失败自诊断"（启动早期落盘日志 + 最小化加载）纳入主分支。
