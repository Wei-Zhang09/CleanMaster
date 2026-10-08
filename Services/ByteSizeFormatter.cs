namespace CleanMaster.Services;

/// <summary>
/// 统一的字节数格式化工具。消除散落在各 Model/ViewModel 里十余处口径不一的
/// switch 实现（有的缺 B 档、有的 F2 有的 F1）。
/// 口径：GB 保留 2 位小数，MB/KB 保留 1 位，小于 1KB 显示字节数。
/// </summary>
public static class ByteSizeFormatter
{
    public static string Format(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };
}
