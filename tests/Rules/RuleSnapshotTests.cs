using CleanMaster.Models;
using CleanMaster.Rules;

namespace CleanMaster.Tests.Rules;

/// <summary>
/// 规则快照测试：锁定当前规则库的完整规则集（名称 + 分类 + 安全等级），
/// 作为「路线 C 扫描规则重构」的安全网。
///
/// 路线 C 的目标是把 1614 行硬编码规则库重构为「注册表枚举 + 映射表 + 启发式指纹发现」。
/// 重构后，本快照必须逐条通过——任何一条规则名称、分类、安全等级的丢失或改变都会被抓住，
/// 保证重构后覆盖不减、语义不变。
///
/// 若要**有意**新增/删除/调整规则，先改这里的快照，再改实现。
/// </summary>
public class RuleSnapshotTests
{
    /// <summary>
    /// 当前 124 条规则的完整快照：名称 | 分类 | 安全等级。
    /// 由 Route C 重构前的规则库生成，作为迁移安全的基线。
    /// </summary>
    private static readonly (string Name, CleanCategory Category, CleanSafety Safety)[] Snapshot =
    {
        // ── Recycle Bin ──
        ("Recycle Bin", CleanCategory.RecycleBin, CleanSafety.Safe),

        // ── Temp / Prefetch ──
        ("User Temp", CleanCategory.TempFiles, CleanSafety.Safe),
        ("Windows Temp", CleanCategory.TempFiles, CleanSafety.Safe),
        ("Prefetch", CleanCategory.TempFiles, CleanSafety.Safe),
        ("LocalService Temp", CleanCategory.TempFiles, CleanSafety.Safe),
        ("NetworkService Temp", CleanCategory.TempFiles, CleanSafety.Safe),

        // ── Windows Update ──
        ("WU Download Cache", CleanCategory.WindowsUpdate, CleanSafety.Safe),
        ("WU DataStore", CleanCategory.WindowsUpdate, CleanSafety.Caution),
        ("Delivery Optimization", CleanCategory.WindowsUpdate, CleanSafety.Safe),
        ("Windows.old", CleanCategory.WindowsUpdate, CleanSafety.Caution),

        // ── Windows Logs ──
        ("Windows Logs (top-level)", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("CBS Logs", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("Windows Logs Compressed", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("DISM Logs", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("DPX Logs", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("Setup Logs (Panther)", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("SysReset Logs", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("Windows LogFiles", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("Windows Debug", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("Security Logs", CleanCategory.WindowsLogs, CleanSafety.Safe),
        ("Windows Defender History", CleanCategory.WindowsLogs, CleanSafety.Safe),

        // ── Browser Cache ──
        ("Edge Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Edge Code Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Edge GPUCache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Chrome Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Chrome Code Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Chrome GPUCache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Brave Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Brave GPUCache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Opera Cache", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Vivaldi Cache (all profiles)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("Firefox Cache2", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("INetCache (IE/Edge legacy)", CleanCategory.BrowserCache, CleanSafety.Safe),
        ("WebCache", CleanCategory.BrowserCache, CleanSafety.Caution),

        // ── Dev Tool Cache ──
        ("Gradle Caches", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("Gradle Daemon Logs", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("NuGet Packages", CleanCategory.DevToolCache, CleanSafety.Caution),
        ("NuGet HTTP Cache", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("npm Cache", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("pip Cache", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("Maven Repository", CleanCategory.DevToolCache, CleanSafety.Caution),
        ("JetBrains Caches", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("VS Code Cache", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("VS Code CachedData", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("VS Code GPUCache", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("VS Code logs", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("VS Code workspaceStorage", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("VS Code Extension Cache (all extensions)", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("Cargo Cache (Rust)", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("Conda pkgs", CleanCategory.DevToolCache, CleanSafety.Caution),
        ("Yarn Cache", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("pnpm store", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("Bun install cache", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("Go mod cache", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("Android build-cache", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("Docker Desktop logs", CleanCategory.DevToolCache, CleanSafety.Safe),
        ("Docker Desktop cache", CleanCategory.DevToolCache, CleanSafety.Caution),
        ("Postman Cache", CleanCategory.DevToolCache, CleanSafety.Safe),

        // ── App Cache ──
        ("Lingma Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Lingma Index", CleanCategory.AppCache, CleanSafety.Safe),
        ("Lingma Logs", CleanCategory.AppCache, CleanSafety.Safe),
        ("Codex Temp", CleanCategory.AppCache, CleanSafety.Safe),
        ("MarsCode Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Qoder shared_client", CleanCategory.AppCache, CleanSafety.Safe),
        ("WeChat 4.0 Logs (xwechat)", CleanCategory.AppCache, CleanSafety.Safe),
        ("WeChat 4.0 Radium Cache (xwechat)", CleanCategory.AppCache, CleanSafety.Caution),
        ("WeChat 4.0 Plugin Cache (xwechat)", CleanCategory.AppCache, CleanSafety.Caution),
        ("WeChat 4.0 Network Cache (xwechat)", CleanCategory.AppCache, CleanSafety.Safe),
        ("WeChat Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("WeChat Files Cache", CleanCategory.AppCache, CleanSafety.Caution),
        ("QQ Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("QQNT Cache", CleanCategory.AppCache, CleanSafety.Caution),
        ("WeCom (企业微信) Cache", CleanCategory.AppCache, CleanSafety.Caution),
        ("Tencent Video Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("WeGame Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("WPS Cache", CleanCategory.AppCache, CleanSafety.Caution),
        ("iQiyi (爱奇艺) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Youku (优酷) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Bilibili Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("NetEase Music (网易云音乐) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("QQ Music Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Kuwo (酷我) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Kugou (酷狗) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Douyin (抖音) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("AliyunPan (阿里云盘) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Baidu Netdisk (百度网盘) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("115 网盘 Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("迅雷 cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("DingTalk (钉钉) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Feishu (飞书) Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Notion Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Slack Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Discord Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("缩略图缓存", CleanCategory.AppCache, CleanSafety.Safe),
        ("IconCache.db", CleanCategory.AppCache, CleanSafety.Safe),
        ("最近文档记录", CleanCategory.AppCache, CleanSafety.Safe),
        ("DirectX 着色器缓存", CleanCategory.AppCache, CleanSafety.Safe),

        // ── Installer Cache ──
        ("System Package Cache", CleanCategory.InstallerCache, CleanSafety.Caution),
        ("User Package Cache", CleanCategory.InstallerCache, CleanSafety.Caution),
        ("Windows Installer Patch Cache", CleanCategory.InstallerCache, CleanSafety.Caution),
        ("Downloaded Program Files", CleanCategory.InstallerCache, CleanSafety.Safe),

        // ── Crash Dumps ──
        ("Crash Dumps", CleanCategory.CrashDumps, CleanSafety.Safe),
        ("Windows Error Reporting (root)", CleanCategory.CrashDumps, CleanSafety.Safe),
        ("WER ReportArchive", CleanCategory.CrashDumps, CleanSafety.Safe),
        ("WER ReportQueue", CleanCategory.CrashDumps, CleanSafety.Safe),
        ("LiveKernelReports", CleanCategory.CrashDumps, CleanSafety.Safe),
        ("MEMORY.DMP", CleanCategory.CrashDumps, CleanSafety.Safe),
        ("Minidump", CleanCategory.CrashDumps, CleanSafety.Safe),

        // ── Windows Cache (system) ──
        ("程序兼容性缓存 (AppCompat)", CleanCategory.AppCache, CleanSafety.Dangerous),
        ("Windows Search Index", CleanCategory.AppCache, CleanSafety.Caution),
        ("Font Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Edge WebView2 Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("UWP/Store 应用 LocalCache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Windows 通知缓存", CleanCategory.AppCache, CleanSafety.Safe),

        // ── AI Tool Cache ──
        ("Huggingface Model Cache", CleanCategory.AppCache, CleanSafety.Caution),
        ("PyTorch Model Cache", CleanCategory.AppCache, CleanSafety.Caution),
        ("Ollama Models", CleanCategory.AppCache, CleanSafety.Caution),
        ("Claude Code Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Gemini CLI Cache", CleanCategory.AppCache, CleanSafety.Safe),
        ("Continue.dev Cache", CleanCategory.AppCache, CleanSafety.Safe),

        // ── Game Cache（条件性：仅当对应游戏平台已安装时出现，不纳入精确快照）──
        // Steam ShaderCache / Steam httpcache / Steam logs / Epic VaultCache / Epic Logs /
        // Battle.net Cache / HoYo Cache —— 见 ConditionalGameRulesArePresentWhenInstalled 测试。

        // ── Electron generic ──
        ("Electron App Cache (auto-discovered)", CleanCategory.AppCache, CleanSafety.Safe),
    };

    [Fact]
    public void GetAllRules_MatchesSnapshot_Exactly()
    {
        var rules = RuleDatabase.GetAllRules()
            .Where(r => !IsConditionalGameRule(r.Name))
            .ToList();

        var actual = rules
            .Select(r => (r.Name, r.Category, r.Safety))
            .OrderBy(r => r.Name)
            .ToArray();
        var expected = Snapshot
            .OrderBy(r => r.Name)
            .ToArray();

        // 数量一致
        Assert.Equal(expected.Length, actual.Length);

        // 逐条比对（名称 + 分类 + 安全等级）
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Name, actual[i].Name);
            Assert.Equal(expected[i].Category, actual[i].Category);
            Assert.Equal(expected[i].Safety, actual[i].Safety);
        }
    }

    [Fact]
    public void GetAllRules_NoDuplicateRuleNames()
    {
        var rules = RuleDatabase.GetAllRules();
        var names = rules.Select(r => r.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    /// <summary>条件性游戏规则：是否出现取决于本机是否安装对应游戏平台。</summary>
    private static bool IsConditionalGameRule(string name) =>
        name is "Steam ShaderCache" or "Steam httpcache" or "Steam logs"
              or "Epic VaultCache" or "Epic Logs"
              or "Battle.net Cache" or "HoYo Cache";
}
