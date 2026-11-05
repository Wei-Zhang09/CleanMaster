using System.IO;

namespace CleanMaster.Rules.Data;

/// <summary>
/// 命名解析器注册表：把"运行时才知道扫哪里"的规则逻辑留在代码里，数据文件只引用名字 + 参数。
///
/// 为什么不把这类规则也写成纯数据：浏览器缓存要枚举 User Data 下的所有 profile 目录，
/// 本质是一段程序。硬把它塞进数据格式就得发明一门脚本语言 —— 那比现在更难审计。
/// 折中方案：数据层负责"有哪些规则、风险多高、依据是什么"，代码层只负责"路径怎么算出来"。
///
/// 约定：
/// - 解析器名字一经发布不要改名（规则文件引用它，改名等于悄悄废掉规则）；
/// - 每个解析器声明自己必需的参数名，缺参数在**校验期**报错，而不是运行期静默返回空。
/// </summary>
public static class RuleResolvers
{
    public delegate IEnumerable<string> Resolver(IReadOnlyDictionary<string, string> args);

    private sealed record Entry(Resolver Resolve, string[] RequiredArgs);

    private static readonly Dictionary<string, Entry> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chromium-profile-subdir"] = new Entry(
            ChromiumProfileSubdir,
            new[] { "user_data", "sub" }),

        ["firefox-profile-subdir"] = new Entry(
            FirefoxProfileSubdir,
            new[] { "profiles_root", "sub" }),

        ["children-subdir"] = new Entry(
            ChildrenSubdir,
            new[] { "parent", "sub" }),

        ["children-subdirs"] = new Entry(
            ChildrenSubdirs,
            new[] { "parent", "subs" }),

        ["parent-subdirs"] = new Entry(
            ParentSubdirs,
            new[] { "parent", "subs" })
    };

    public static IReadOnlyCollection<string> Names => Registry.Keys;

    public static bool IsKnown(string name)
        => !string.IsNullOrWhiteSpace(name) && Registry.ContainsKey(name);

    public static IReadOnlyList<string> RequiredArgs(string name)
        => Registry.TryGetValue(name, out var entry) ? entry.RequiredArgs : Array.Empty<string>();

    /// <summary>执行解析器。未知名字或异常一律返回空集合（校验期已拦截，这里只做防御）。</summary>
    public static IEnumerable<string> Resolve(string name, IReadOnlyDictionary<string, string> args)
    {
        if (!Registry.TryGetValue(name, out var entry)) return Array.Empty<string>();

        try
        {
            return entry.Resolve(args).ToList();
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError($"RuleResolvers.{name}", ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 枚举 Chromium 系浏览器每个 profile 下的子目录。
    /// 跳过 Snapshots / Crashpad / SmartScreen / GrShaderCache 等非 profile 目录（与旧实现一致）。
    /// </summary>
    private static IEnumerable<string> ChromiumProfileSubdir(IReadOnlyDictionary<string, string> args)
    {
        var root = args.GetValueOrDefault("user_data", "");
        var sub = args.GetValueOrDefault("sub", "");

        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(sub) || !Directory.Exists(root))
            yield break;

        string[] profiles;
        try { profiles = Directory.GetDirectories(root); }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("RuleResolvers.ChromiumProfileSubdir", ex);
            yield break;
        }

        foreach (var profile in profiles)
        {
            var profileName = Path.GetFileName(profile);
            if (IsNonProfileDirectory(profileName)) continue;

            var candidate = Path.Combine(profile, sub);
            if (Directory.Exists(candidate)) yield return candidate;
        }
    }

    private static IEnumerable<string> FirefoxProfileSubdir(IReadOnlyDictionary<string, string> args)
    {
        var root = args.GetValueOrDefault("profiles_root", "");
        var sub = args.GetValueOrDefault("sub", "");

        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(sub) || !Directory.Exists(root))
            yield break;

        string[] profiles;
        try { profiles = Directory.GetDirectories(root); }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("RuleResolvers.FirefoxProfileSubdir", ex);
            yield break;
        }

        foreach (var profile in profiles)
        {
            var candidate = Path.Combine(profile, sub);
            if (Directory.Exists(candidate)) yield return candidate;
        }
    }

    /// <summary>parent 下每个子目录的 sub 孙目录（如 VS Code 各扩展的 cache）。</summary>
    private static IEnumerable<string> ChildrenSubdir(IReadOnlyDictionary<string, string> args)
    {
        var parent = args.GetValueOrDefault("parent", "");
        var sub = args.GetValueOrDefault("sub", "");

        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(sub) || !Directory.Exists(parent))
            yield break;

        string[] children;
        try { children = Directory.GetDirectories(parent); }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("RuleResolvers.ChildrenSubdir", ex);
            yield break;
        }

        foreach (var child in children)
        {
            var candidate = Path.Combine(child, sub);
            if (Directory.Exists(candidate)) yield return candidate;
        }
    }

    /// <summary>parent 下每个子目录的多个孙目录（如 JetBrains 各版本 IDE 的 caches/index/log/tmp）。</summary>
    private static IEnumerable<string> ChildrenSubdirs(IReadOnlyDictionary<string, string> args)
    {
        var parent = args.GetValueOrDefault("parent", "");
        var subs = SplitSubs(args.GetValueOrDefault("subs", ""));

        if (string.IsNullOrEmpty(parent) || subs.Length == 0 || !Directory.Exists(parent))
            yield break;

        string[] children;
        try { children = Directory.GetDirectories(parent); }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("RuleResolvers.ChildrenSubdirs", ex);
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var sub in subs)
            {
                var candidate = Path.Combine(child, sub);
                if (Directory.Exists(candidate)) yield return candidate;
            }
        }
    }

    /// <summary>单个 parent 下的多个直接子目录（如 Docker 的 cache/tmp）。</summary>
    private static IEnumerable<string> ParentSubdirs(IReadOnlyDictionary<string, string> args)
    {
        var parent = args.GetValueOrDefault("parent", "");
        var subs = SplitSubs(args.GetValueOrDefault("subs", ""));

        if (string.IsNullOrEmpty(parent) || subs.Length == 0 || !Directory.Exists(parent))
            yield break;

        foreach (var sub in subs)
        {
            var candidate = Path.Combine(parent, sub);
            if (Directory.Exists(candidate)) yield return candidate;
        }
    }

    private static string[] SplitSubs(string value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsNonProfileDirectory(string name)
        => name.Equals("Snapshots", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Crashpad", StringComparison.OrdinalIgnoreCase)
           || name.Equals("SmartScreen", StringComparison.OrdinalIgnoreCase)
           || name.Equals("GrShaderCache", StringComparison.OrdinalIgnoreCase);
}
