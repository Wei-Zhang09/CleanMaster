namespace CleanMaster.Rules;

/// <summary>
/// 缓存路径模式。路径基于「环境变量占位 + 相对子路径」，
/// 而不是硬编码盘符——因为不同用户/机器的安装位置差异很大。
///
/// 支持的占位符（在 <see cref="AppCacheMap.ResolvePath"/> 中展开）：
///   {LOCAL}   = %LOCALAPPDATA%
///   {ROAMING} = %APPDATA%
///   {USER}    = 用户主目录（%USERPROFILE%）
///   {INSTALL} = 注册表 InstallLocation（该应用的实际安装目录）
/// </summary>
public class CachePathPattern
{
    /// <summary>含占位符的路径模板，如 "{ROAMING}\\Clash Verge\\cache"。</summary>
    public string Template { get; set; } = "";

    /// <summary>是否只清理该路径下的文件（而非整个目录）。</summary>
    public bool IsFile { get; set; }
}

/// <summary>
/// 「注册表 DisplayName → 缓存路径」的映射条目。
/// 通过 DisplayName 关键词命中已装应用，动态生成该应用的缓存规则。
/// </summary>
public class AppCacheMapEntry
{
    /// <summary>用于匹配注册表 DisplayName 的关键词（不区分大小写，包含匹配）。</summary>
    public string DisplayNameKeyword { get; set; } = "";

    /// <summary>该应用命中的缓存路径（可为空，表示仅用启发式指纹发现兜底）。</summary>
    public List<CachePathPattern> Patterns { get; set; } = new();

    /// <summary>是否 Caution（默认 Safe）。需用户确认的缓存标 Caution。</summary>
    public bool Caution { get; set; }
}

/// <summary>
/// 缓存路径映射表（路线 C 核心）。
/// 从注册表枚举已装应用，命中本表的 DisplayName 关键词后，
/// 用该应用的 InstallLocation + 环境变量解析出真实缓存路径。
/// </summary>
public static class AppCacheMap
{
    /// <summary>
    /// 映射表。关键词按优先级排列，越具体的越靠前。
    /// 每个条目对应一个「有缓存价值」的软件。
    /// </summary>
    public static readonly List<AppCacheMapEntry> Entries = new()
    {
        // ── 浏览器（用 {LOCAL}/{ROAMING}，缓存目录固定）──
        new() { DisplayNameKeyword = "Microsoft Edge", Patterns = {
            new() { Template = @"{LOCAL}\Microsoft\Edge\User Data" },
        }},
        new() { DisplayNameKeyword = "Google Chrome", Patterns = {
            new() { Template = @"{LOCAL}\Google\Chrome\User Data" },
        }},
        new() { DisplayNameKeyword = "Firefox", Patterns = {
            new() { Template = @"{LOCAL}\Mozilla\Firefox\Profiles" },
        }},
        new() { DisplayNameKeyword = "Brave", Patterns = {
            new() { Template = @"{LOCAL}\BraveSoftware\Brave-Browser\User Data" },
        }},
        new() { DisplayNameKeyword = "Opera", Patterns = {
            new() { Template = @"{LOCAL}\Opera Software" },
        }},
        new() { DisplayNameKeyword = "Vivaldi", Patterns = {
            new() { Template = @"{LOCAL}\Vivaldi\User Data" },
        }},

        // ── 开发工具（IDE/编辑器缓存常在 {ROAMING} 或 {LOCAL}）──
        new() { DisplayNameKeyword = "Visual Studio Code", Patterns = {
            new() { Template = @"{ROAMING}\Code" },
        }},
        new() { DisplayNameKeyword = "IntelliJ IDEA", Patterns = {
            new() { Template = @"{LOCAL}\JetBrains" },
        }},
        new() { DisplayNameKeyword = "PyCharm", Patterns = {
            new() { Template = @"{LOCAL}\JetBrains" },
        }},
        new() { DisplayNameKeyword = "DataGrip", Patterns = {
            new() { Template = @"{LOCAL}\JetBrains" },
        }},
        new() { DisplayNameKeyword = "Cursor", Patterns = {
            new() { Template = @"{ROAMING}\Cursor" },
        }},
        new() { DisplayNameKeyword = "Obsidian", Patterns = {
            new() { Template = @"{ROAMING}\obsidian" },
        }},
        new() { DisplayNameKeyword = "Typora", Patterns = {
            new() { Template = @"{ROAMING}\Typora" },
        }},
        new() { DisplayNameKeyword = "draw.io", Patterns = {
            new() { Template = @"{ROAMING}\draw.io" },
        }},

        // ── 国产应用 ──
        // 注意：微信/QQ 缓存路径复杂（多账号、多子目录），继续走硬编码 RuleDatabase，
        // 不纳入映射表（简单模板无法表达其多路径逻辑）。
        new() { DisplayNameKeyword = "企业微信", Patterns = {
            new() { Template = @"{ROAMING}\Tencent\WXWork" },
        }},
        new() { DisplayNameKeyword = "钉钉", Patterns = {
            new() { Template = @"{LOCAL}\DingTalk" },
        }},
        new() { DisplayNameKeyword = "飞书", Patterns = {
            new() { Template = @"{LOCAL}\Feishu" },
        }},
        new() { DisplayNameKeyword = "WPS", Patterns = {
            new() { Template = @"{ROAMING}\kingsoft\wps" },
        }},
        new() { DisplayNameKeyword = "百度网盘", Patterns = {
            new() { Template = @"{LOCAL}\BaiduNetdisk" },
        }},

        // ── 网络工具 / 加速器（缓存大）──
        new() { DisplayNameKeyword = "Clash Verge", Patterns = {
            new() { Template = @"{ROAMING}\io.github.clash-verge-rev.clash-verge-rev" },
        }},
        new() { DisplayNameKeyword = "Watt Toolkit", Patterns = {
            new() { Template = @"{ROAMING}\Steam++" },
        }},
        new() { DisplayNameKeyword = "ToDesk", Patterns = {
            new() { Template = @"{ROAMING}\ToDesk" },
        }},
        new() { DisplayNameKeyword = "UU远程", Patterns = {
            new() { Template = @"{ROAMING}\UU" },
        }},

        // ── API 调试 / AI 工具 ──
        new() { DisplayNameKeyword = "Apifox", Patterns = {
            new() { Template = @"{ROAMING}\Apifox" },
        }},
        new() { DisplayNameKeyword = "Codex++", Patterns = {
            new() { Template = @"{ROAMING}\Codex" },
        }},
        new() { DisplayNameKeyword = "Ollama", Patterns = {
            new() { Template = @"{USER}\.ollama\models" },
        }, Caution = true },

        // ── 游戏 / 图形（shader cache 大）──
        new() { DisplayNameKeyword = "NVIDIA App", Patterns = {
            new() { Template = @"{LOCAL}\NVIDIA" },
        }},
        new() { DisplayNameKeyword = "Steam", Patterns = {
            new() { Template = @"{INSTALL}\steamapps\shadercache" },
        }},
        new() { DisplayNameKeyword = "英雄联盟", Patterns = {
            new() { Template = @"{LOCAL}\League of Legends" },
        }},
        new() { DisplayNameKeyword = "WeGame", Patterns = {
            new() { Template = @"{ROAMING}\Tencent\WeGame" },
        }},

        // ── 数据库（缓存/日志）──
        new() { DisplayNameKeyword = "MySQL", Patterns = {
            new() { Template = @"{INSTALL}\data" },
        }, Caution = true },
        new() { DisplayNameKeyword = "PostgreSQL", Patterns = {
            new() { Template = @"{INSTALL}\data" },
        }, Caution = true },
    };

    /// <summary>展开路径模板中的占位符。</summary>
    public static string ResolvePath(string template, string installLocation)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return template
            .Replace("{LOCAL}", local, StringComparison.OrdinalIgnoreCase)
            .Replace("{ROAMING}", roaming, StringComparison.OrdinalIgnoreCase)
            .Replace("{USER}", user, StringComparison.OrdinalIgnoreCase)
            .Replace("{INSTALL}", installLocation.Trim('"').TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }
}
