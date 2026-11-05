using System.IO;
using System.Text.RegularExpressions;

namespace CleanMaster.Rules.Data;

/// <summary>
/// 路径模板占位符展开。规则文件里写 <c>{LOCAL}\Temp</c>，运行期按当前环境展开。
///
/// 支持的白名单刻意很小：每个占位符都必须有明确、稳定、与用户无关的语义。
/// 不认识 <c>{XXX}</c> 直接报错，而不是当成字面量 —— 否则一条拼错的规则会静默扫不到任何东西，
/// 表现为"这个缓存项永远不出现"，极难排查。
/// </summary>
public static class RuleTemplateResolver
{
    private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    private static readonly Dictionary<string, Func<string>> Resolvers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["LOCAL"] = () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["ROAMING"] = () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["USER"] = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["SYSTEMDRIVE"] = () => Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))?.TrimEnd('\\') ?? "C:",
            ["WINDIR"] = () => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ["PROGRAMFILES"] = () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ["PROGRAMFILESX86"] = () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            ["PROGRAMDATA"] = () => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["DOCUMENTS"] = () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["RECENT"] = () => Environment.GetFolderPath(Environment.SpecialFolder.Recent),
            ["TEMP"] = () => Path.GetTempPath().TrimEnd('\\')
        };

    public static IReadOnlyCollection<string> SupportedPlaceholders => Resolvers.Keys;

    /// <summary>返回模板里出现但不在白名单内的占位符（去重、原样大小写）。</summary>
    public static IReadOnlyList<string> FindUnknownPlaceholders(string template)
    {
        if (string.IsNullOrEmpty(template)) return Array.Empty<string>();

        return PlaceholderRegex.Matches(template)
            .Select(m => m.Groups[1].Value)
            .Where(name => !Resolvers.ContainsKey(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>展开模板。含未知占位符时返回空串（调用方应已在校验期拦截）。</summary>
    public static string Resolve(string template)
    {
        if (string.IsNullOrEmpty(template)) return "";
        if (FindUnknownPlaceholders(template).Count > 0) return "";

        return PlaceholderRegex.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            return Resolvers.TryGetValue(name, out var resolver) ? resolver() : match.Value;
        });
    }
}
