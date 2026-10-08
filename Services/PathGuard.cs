using System.IO;

namespace CleanMaster.Services;

/// <summary>
/// 删除前最后一道路径守卫：拒绝删除受保护的系统/用户目录、盘符根，
/// 以及符号链接/联接点（避免误删链接目标）。返回拒绝原因供界面展示。
/// </summary>
public static class PathGuard
{
    private static readonly HashSet<string> ProtectedDirs = BuildProtected();

    private static HashSet<string> BuildProtected()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.Desktop
        })
        {
            var p = Environment.GetFolderPath(f);
            if (!string.IsNullOrEmpty(p))
                set.Add(Normalize(p));
        }
        return set;
    }

    private static string Normalize(string p) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));

    /// <summary>
    /// 判断路径是否允许被删除。仅做精确匹配的目录保护，子目录（如 Windows\Temp）不受影响。
    /// </summary>
    public static bool IsDeletable(string path, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(path)) { reason = "路径为空"; return false; }

        string full;
        try { full = Normalize(path); }
        catch (Exception ex) { reason = $"路径无效: {ex.Message}"; return false; }

        // 盘符根
        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root)
            && string.Equals(root.TrimEnd('\\'), full.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            reason = "不允许删除盘符根目录";
            return false;
        }

        // 受保护的系统/用户目录（精确匹配）
        if (ProtectedDirs.Contains(full))
        {
            reason = "受保护的系统/用户目录";
            return false;
        }

        // 符号链接 / 联接点 / 挂载点
        try
        {
            var attr = File.GetAttributes(full);
            if ((attr & FileAttributes.ReparsePoint) != 0)
            {
                reason = "目标是符号链接/联接点";
                return false;
            }
        }
        catch
        {
            // 路径不存在时交由调用方处理（文件不存在会走其它错误分支）
        }

        return true;
    }
}
