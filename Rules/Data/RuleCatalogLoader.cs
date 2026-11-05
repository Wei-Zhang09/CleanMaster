using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CleanMaster.Models;
using Tomlyn;
using Tomlyn.Model;

namespace CleanMaster.Rules.Data;

public sealed class RuleCatalogResult
{
    public IReadOnlyList<RuleDefinition> Rules { get; init; } = Array.Empty<RuleDefinition>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// 规则目录的加载与校验（RC-6）。
///
/// 设计目标：规则是**数据**，不是代码。
/// - 文件是 TOML（能写注释与多行 evidence，非开发者也能审计、能加规则）；
/// - 编译期嵌入程序集，发布时不需要额外散落文件；
/// - 校验失败就是错误（缺字段、枚举非法、占位符拼错、id 重复），不允许"悄悄少一条规则"。
///
/// 未迁移完成的规则仍留在 <see cref="RuleDatabase"/> 的硬编码里；迁移按域分批，
/// 每批用 RuleDefinitionFingerprintTests + CleanMaster.Cli scan --json 前后对比。
/// </summary>
public static class RuleCatalogLoader
{
    public const string ResourcePrefix = "CleanMaster.Rules.Catalog.";

    private static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9._-]*$", RegexOptions.Compiled);

    /// <summary>加载程序集内嵌的全部规则文件。</summary>
    public static RuleCatalogResult LoadEmbedded(Assembly? assembly = null)
    {
        assembly ??= typeof(RuleCatalogLoader).Assembly;

        var definitions = new List<RuleDefinition>();
        var errors = new List<string>();
        var warnings = new List<string>();

        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                        n.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
        {
            return new RuleCatalogResult
            {
                Errors = new[] { $"程序集内没有找到规则文件（前缀 {ResourcePrefix}）。检查 csproj 的 EmbeddedResource 配置。" }
            };
        }

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null)
            {
                errors.Add($"{name}: 资源流打开失败");
                continue;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            var source = ToRelativeSource(name);

            var parsed = ParseDocument(text, source);
            definitions.AddRange(parsed.Rules);
            errors.AddRange(parsed.Errors);
            warnings.AddRange(parsed.Warnings);
        }

        var cross = ValidateCrossFile(definitions);
        errors.AddRange(cross.Errors);
        warnings.AddRange(cross.Warnings);

        return new RuleCatalogResult
        {
            Rules = definitions.OrderBy(r => r.Id, StringComparer.Ordinal).ToList(),
            Errors = errors,
            Warnings = warnings
        };
    }

    /// <summary>解析单个 TOML 文本（<c>[[rule]]</c> 数组）。</summary>
    public static RuleCatalogResult ParseDocument(string text, string source)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var doc = Toml.Parse(text, source);
        if (doc.HasErrors)
        {
            foreach (var diagnostic in doc.Diagnostics)
            {
                var span = diagnostic.Span;
                errors.Add($"{source}:{span.Start.Line + 1}: TOML 语法错误：{diagnostic.Message}");
            }
            return new RuleCatalogResult { Errors = errors };
        }

        TomlTable model;
        try
        {
            model = Toml.ToModel(text);
        }
        catch (Exception ex)
        {
            return new RuleCatalogResult { Errors = new[] { $"{source}: 解析失败：{ex.Message}" } };
        }

        if (!model.TryGetValue("rule", out var rawRules) || rawRules is not TomlTableArray ruleArray)
        {
            return new RuleCatalogResult { Errors = new[] { $"{source}: 缺少 [[rule]] 数组" } };
        }

        var definitions = new List<RuleDefinition>();
        var index = 0;
        foreach (var table in ruleArray)
        {
            index++;
            var where = $"{source} [[rule]] #{index}";
            var definition = ParseRule(table, where, errors, warnings);
            if (definition != null) definitions.Add(definition);
        }

        return new RuleCatalogResult { Rules = definitions, Errors = errors, Warnings = warnings };
    }

    private static RuleDefinition? ParseRule(
        TomlTable table, string where, List<string> errors, List<string> warnings)
    {
        var errorCountBefore = errors.Count;

        var id = RequireString(table, "id", where, errors) ?? "";
        var name = RequireString(table, "name", where, errors) ?? "";
        var category = RequireString(table, "category", where, errors) ?? "";
        var safety = RequireString(table, "safety", where, errors) ?? "";

        if (!string.IsNullOrEmpty(id) && !IdPattern.IsMatch(id))
            errors.Add($"{where}: id \"{id}\" 不合法（要求小写、以字母数字开头、仅含 . _ -）");

        if (!string.IsNullOrEmpty(category) && !Enum.TryParse<CleanCategory>(category, ignoreCase: true, out _))
            errors.Add($"{where}: category \"{category}\" 不是合法分类（可选：{string.Join(", ", Enum.GetNames<CleanCategory>())}）");

        if (!string.IsNullOrEmpty(safety) && !Enum.TryParse<CleanSafety>(safety, ignoreCase: true, out _))
            errors.Add($"{where}: safety \"{safety}\" 不合法（可选：{string.Join(", ", Enum.GetNames<CleanSafety>())}）");

        var path = GetString(table, "path", where, errors) ?? "";
        var paths = GetStringArray(table, "paths", where, errors);
        var resolver = GetString(table, "resolver", where, errors) ?? "";
        var resolverArgs = GetStringMap(table, "resolver_args", where, errors);

        var hasStaticPath = !string.IsNullOrWhiteSpace(path) || paths.Length > 0;

        if (!hasStaticPath && string.IsNullOrWhiteSpace(resolver))
            errors.Add($"{where}: 必须提供 path、paths 或 resolver 三者之一");

        if (!string.IsNullOrWhiteSpace(resolver) && hasStaticPath)
            errors.Add($"{where}: resolver 与 path/paths 不能同时提供");

        if (!string.IsNullOrWhiteSpace(path) && paths.Length > 0)
            errors.Add($"{where}: path 与 paths 不能同时提供");

        if (!string.IsNullOrWhiteSpace(resolver))
        {
            if (!RuleResolvers.IsKnown(resolver))
            {
                errors.Add($"{where}: 未注册的 resolver \"{resolver}\"（已注册：{string.Join(", ", RuleResolvers.Names)}）");
            }
            else
            {
                foreach (var required in RuleResolvers.RequiredArgs(resolver))
                {
                    if (!resolverArgs.ContainsKey(required) || string.IsNullOrWhiteSpace(resolverArgs[required]))
                        errors.Add($"{where}: resolver \"{resolver}\" 缺少必需参数 {required}");
                }

                foreach (var (key, value) in resolverArgs)
                {
                    var unknownArgs = RuleTemplateResolver.FindUnknownPlaceholders(value);
                    if (unknownArgs.Count > 0)
                        errors.Add($"{where}: resolver_args.{key} 含未知占位符 {string.Join(", ", unknownArgs.Select(u => "{" + u + "}"))}");
                }
            }
        }

        foreach (var template in paths.Length > 0 ? paths : new[] { path })
        {
            if (string.IsNullOrWhiteSpace(template)) continue;
            var unknown = RuleTemplateResolver.FindUnknownPlaceholders(template);
            if (unknown.Count > 0)
                errors.Add($"{where}: 路径模板含未知占位符 {string.Join(", ", unknown.Select(u => "{" + u + "}"))}" +
                           $"（支持：{string.Join(", ", RuleTemplateResolver.SupportedPlaceholders.Select(p => "{" + p + "}"))}）");
        }

        var maxDepth = GetInt(table, "max_depth", where, errors) ?? -1;
        if (maxDepth < -1)
            errors.Add($"{where}: max_depth 不能小于 -1（当前 {maxDepth}）");

        var patterns = GetStringArray(table, "patterns", where, errors);
        if (patterns.Any(string.IsNullOrWhiteSpace))
            errors.Add($"{where}: patterns 里存在空字符串");

        var evidence = GetString(table, "evidence", where, errors);
        if (string.IsNullOrWhiteSpace(evidence))
            warnings.Add($"{where}: 缺少 evidence（建议写明「为什么删这些文件是安全的」，审计时最先看它）");

        var verifiedAt = GetString(table, "verified_at", where, errors);
        if (!string.IsNullOrWhiteSpace(verifiedAt) &&
            !DateTime.TryParseExact(verifiedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors.Add($"{where}: verified_at 需为 yyyy-MM-dd（当前 \"{verifiedAt}\"）");

        if (errors.Count > errorCountBefore) return null;

        return new RuleDefinition
        {
            Id = id,
            Name = name,
            Category = category,
            Safety = safety,
            Path = path,
            Paths = paths,
            Software = GetString(table, "software", where, errors),
            FileType = GetString(table, "file_type", where, errors),
            Description = GetString(table, "description", where, errors),
            Patterns = patterns,
            PatternsAreDirectories = GetBool(table, "patterns_are_directories", where, errors) ?? true,
            MaxDepth = maxDepth,
            Enabled = GetBool(table, "enabled", where, errors) ?? true,
            Evidence = evidence,
            VerifiedAt = verifiedAt,
            References = GetStringArray(table, "references", where, errors),
            Resolver = resolver,
            ResolverArgs = resolverArgs,
            Source = where
        };
    }

    /// <summary>跨文件校验：id 与名称都必须在整个目录内唯一（外置后要能唯一定位一条规则）。</summary>
    public static RuleCatalogResult ValidateCrossFile(IReadOnlyList<RuleDefinition> definitions)
    {
        var errors = new List<string>();

        foreach (var group in definitions.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"id 重复：\"{group.Key}\"（{string.Join("、", group.Select(g => g.Source))}）");

        foreach (var group in definitions.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"规则名重复：\"{group.Key}\"（{string.Join("、", group.Select(g => g.Source))}）");

        return new RuleCatalogResult { Errors = errors };
    }

    /// <summary>把数据定义转成运行期规则。路径在扫描时按当前环境展开。</summary>
    public static CleanupRule ToCleanupRule(RuleDefinition definition)
    {
        var templates = definition.EffectivePaths.ToArray();

        // resolver 规则：路径在扫描时由注册的解析器算出；参数里的占位符同样展开
        if (definition.UsesResolver)
        {
            var resolverName = definition.Resolver;
            var resolverArgs = definition.ResolverArgs.ToDictionary(
                kv => kv.Key,
                kv => RuleTemplateResolver.Resolve(kv.Value),
                StringComparer.OrdinalIgnoreCase);

            return new CleanupRule
            {
                Id = definition.Id,
                Name = definition.Name,
                PathTemplate = "resolver:" + resolverName,
                Category = Enum.Parse<CleanCategory>(definition.Category, ignoreCase: true),
                Safety = Enum.Parse<CleanSafety>(definition.Safety, ignoreCase: true),
                Description = definition.Description ?? "",
                SoftwareName = string.IsNullOrWhiteSpace(definition.Software) ? null : definition.Software,
                FileType = string.IsNullOrWhiteSpace(definition.FileType) ? null : definition.FileType,
                FilePatterns = definition.Patterns.Length > 0 ? definition.Patterns : null,
                PatternsAreDirectories = definition.PatternsAreDirectories,
                MaxDepth = definition.MaxDepth,
                IsEnabled = definition.Enabled,
                PathFactoryMulti = () => RuleResolvers.Resolve(resolverName, resolverArgs)
            };
        }

        return new CleanupRule
        {
            Id = definition.Id,
            Name = definition.Name,
            PathTemplate = templates.Length == 1 ? templates[0] : string.Join(";", templates),
            Category = Enum.Parse<CleanCategory>(definition.Category, ignoreCase: true),
            Safety = Enum.Parse<CleanSafety>(definition.Safety, ignoreCase: true),
            Description = definition.Description ?? "",
            SoftwareName = string.IsNullOrWhiteSpace(definition.Software) ? null : definition.Software,
            FileType = string.IsNullOrWhiteSpace(definition.FileType) ? null : definition.FileType,
            FilePatterns = definition.Patterns.Length > 0 ? definition.Patterns : null,
            PatternsAreDirectories = definition.PatternsAreDirectories,
            MaxDepth = definition.MaxDepth,
            IsEnabled = definition.Enabled,
            PathFactoryMulti = () => templates
                .Select(RuleTemplateResolver.Resolve)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray()
        };
    }

    // ---------------- 取值辅助（类型不符直接报错，不做隐式转换） ----------------

    private static string? GetString(TomlTable table, string key, string where, List<string> errors)
    {
        if (!table.TryGetValue(key, out var raw) || raw == null) return null;
        if (raw is string s) return s;
        errors.Add($"{where}: 字段 {key} 期望字符串，实际 {raw.GetType().Name}");
        return null;
    }

    private static string? RequireString(TomlTable table, string key, string where, List<string> errors)
    {
        var value = GetString(table, key, where, errors);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (value == null) errors.Add($"{where}: 缺少必填字段 {key}");
            else errors.Add($"{where}: 字段 {key} 不能为空");
        }
        return value;
    }

    private static string[] GetStringArray(TomlTable table, string key, string where, List<string> errors)
    {
        if (!table.TryGetValue(key, out var raw) || raw == null) return Array.Empty<string>();

        if (raw is TomlArray array)
        {
            var result = new List<string>();
            foreach (var item in array)
            {
                if (item is string s) result.Add(s);
                else errors.Add($"{where}: {key} 里存在非字符串元素（{item?.GetType().Name ?? "null"}）");
            }
            return result.ToArray();
        }

        errors.Add($"{where}: 字段 {key} 期望字符串数组，实际 {raw.GetType().Name}");
        return Array.Empty<string>();
    }

    private static IReadOnlyDictionary<string, string> GetStringMap(
        TomlTable table, string key, string where, List<string> errors)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!table.TryGetValue(key, out var raw) || raw == null) return result;

        if (raw is not TomlTable nested)
        {
            errors.Add($"{where}: 字段 {key} 期望表（键值对），实际 {raw.GetType().Name}");
            return result;
        }

        foreach (var (k, v) in nested)
        {
            if (v is string text) result[k] = text;
            else errors.Add($"{where}: {key}.{k} 期望字符串，实际 {v?.GetType().Name ?? "null"}");
        }
        return result;
    }

    private static bool? GetBool(TomlTable table, string key, string where, List<string> errors)
    {
        if (!table.TryGetValue(key, out var raw) || raw == null) return null;
        if (raw is bool b) return b;
        errors.Add($"{where}: 字段 {key} 期望布尔值，实际 {raw.GetType().Name}");
        return null;
    }

    private static int? GetInt(TomlTable table, string key, string where, List<string> errors)
    {
        if (!table.TryGetValue(key, out var raw) || raw == null) return null;
        if (raw is long l) return (int)l;
        errors.Add($"{where}: 字段 {key} 期望整数，实际 {raw.GetType().Name}");
        return null;
    }

    private static string ToRelativeSource(string resourceName)
    {
        // 内嵌资源名：CleanMaster.Rules.Catalog.system.temp.toml → Rules/Catalog/system/temp.toml
        var tail = resourceName.Substring(ResourcePrefix.Length);
        if (!tail.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)) return resourceName;

        var body = tail[..^".toml".Length];
        return "Rules/Catalog/" + body.Replace('.', '/') + ".toml";
    }
}
