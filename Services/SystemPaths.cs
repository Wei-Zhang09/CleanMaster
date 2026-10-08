using System.IO;

namespace CleanMaster.Services;

/// <summary>
/// 统一的系统路径常量，消除散落在各处的 "C:" 硬编码。
/// </summary>
public static class SystemPaths
{
    /// <summary>
    /// 系统盘根目录（如 "C:\"）。取自 Windows 目录所在盘，避免假设一定是 C 盘。
    /// </summary>
    public static readonly string SystemDrive = Path.GetPathRoot(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
}
