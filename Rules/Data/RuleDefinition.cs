namespace CleanMaster.Rules.Data;

/// <summary>
/// 一条清理规则的**数据定义**（来自 TOML 文件），与运行期的 <see cref="CleanupRule"/> 分离。
///
/// 为什么要分开：规则要能被非开发者审计（改一行 TOML 就能加一条缓存规则、写清依据），
/// 而运行期规则带 lambda 路径工厂，不适合做数据。数据层只描述"扫哪里、怎么匹配、风险多高"。
/// </summary>
public sealed class RuleDefinition
{
    /// <summary>稳定标识（小写、点分，如 <c>system.temp.user</c>）。外置后用于定位与对比。</summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Category { get; init; } = "";

    public string Safety { get; init; } = "";

    /// <summary>单路径模板；与 <see cref="Paths"/> 二选一。</summary>
    public string Path { get; init; } = "";

    /// <summary>多路径模板（如多个浏览器 profile 目录）。</summary>
    public string[] Paths { get; init; } = Array.Empty<string>();

    public string? Software { get; init; }

    public string? FileType { get; init; }

    public string? Description { get; init; }

    public string[] Patterns { get; init; } = Array.Empty<string>();

    public bool PatternsAreDirectories { get; init; } = true;

    public int MaxDepth { get; init; } = -1;

    public bool Enabled { get; init; } = true;

    /// <summary>为什么这条规则安全（清理依据）。审计时最先看这个字段。</summary>
    public string? Evidence { get; init; }

    /// <summary>证据的采集日期（yyyy-MM-dd），表示"有人在这天核对过"。</summary>
    public string? VerifiedAt { get; init; }

    public string[] References { get; init; } = Array.Empty<string>();

    /// <summary>来源文件（相对路径），用于错误定位。</summary>
    public string Source { get; init; } = "";

    public IEnumerable<string> EffectivePaths =>
        Paths.Length > 0 ? Paths : string.IsNullOrEmpty(Path) ? Array.Empty<string>() : new[] { Path };
}
