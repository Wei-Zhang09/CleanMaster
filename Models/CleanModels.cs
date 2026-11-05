using System.ComponentModel;
using System.Runtime.CompilerServices;
using CleanMaster.Services;

namespace CleanMaster.Models;

public static class FileExtensionConstants
{
    public static readonly HashSet<string> SafeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".log", ".bak", ".old", ".cache", ".temp",
        ".dmp", ".mdmp", ".etl", ".evtx",
        ".zip", ".rar", ".7z", ".tar", ".gz",
        ".iso", ".img",
        ".mp4", ".avi", ".mkv", ".mov", ".wmv",
        ".mp3", ".wav", ".flac",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
    };

    public static readonly HashSet<string> CautionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vmdk", ".vhd", ".vhdx", ".qcow2",
        ".pst", ".ost", ".db", ".sqlite",
        // 二进制文件 / AI 模型权重（用途不明或重要，删除前需确认）
        ".bin", ".dat", ".safetensors", ".ckpt", ".pt", ".pth", ".onnx",
        // 分卷压缩包片段（删除任一片会破坏整个压缩包）
        ".001", ".002", ".003", ".004", ".005", ".006", ".007", ".008", ".009"
    };

    public static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys"
    };
}

public enum CleanSafety
{
    Safe,       // cache/temp/log - always safe
    Caution,    // may contain useful data - ask user
    Dangerous   // could break things - warn strongly
}

public enum CleanCategory
{
    Unknown = 0,
    RecycleBin,
    TempFiles,
    WindowsUpdate,
    WindowsLogs,
    BrowserCache,
    DevToolCache,
    AppCache,
    InstallerCache,
    CrashDumps,
    DesktopInstallers,
    LargeFiles,
    DuplicateFiles
}

public class CleanableItem : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }

    /// <summary>
    /// 实际占用的磁盘空间（稀疏/压缩文件可能远小于 <see cref="SizeBytes"/>）。
    /// null 表示未采集，此时一律按逻辑大小处理。
    /// 列表展示仍用逻辑大小；"可回收 / 已释放"这类汇总口径用 <see cref="ReclaimableBytes"/>。
    /// </summary>
    public long? AllocatedBytes { get; set; }

    /// <summary>删除该项实际能腾出的空间：优先用实际占用，未采集时退化为逻辑大小。</summary>
    public long ReclaimableBytes => AllocatedBytes ?? SizeBytes;

    public CleanSafety Safety { get; set; }
    public CleanCategory Category { get; set; }
    public string Description { get; set; } = "";
    public string SoftwareName { get; set; } = "";
    public string FileType { get; set; } = "";

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 是否为危险项。用于 UI 显示醒目警告, 以及决定是否需要二次确认对话框。
    /// </summary>
    public bool IsDangerous => Safety == CleanSafety.Dangerous;

    public bool IsDirectory { get; set; }
    public DateTime LastModified { get; set; }

    public string SizeText => ByteSizeFormatter.Format(SizeBytes);

    public string SafetyText => Safety switch
    {
        CleanSafety.Safe => "安全",
        CleanSafety.Caution => "谨慎",
        CleanSafety.Dangerous => "危险",
        _ => "未知"
    };

    public string SoftwareInfo => !string.IsNullOrEmpty(SoftwareName)
        ? $"[{SoftwareName}] {FileType}"
        : FileType;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ScanCategoryResult : INotifyPropertyChanged
{
    public CleanCategory Category { get; set; }
    public string DisplayName { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<CleanableItem> Items { get; set; } = new();
    public long TotalSize => Items.Sum(i => i.SizeBytes);

    /// <summary>该分类删除后实际能腾出的空间（按实际占用累加）。</summary>
    public long ReclaimableSize => Items.Sum(i => i.ReclaimableBytes);

    public int ItemCount => Items.Count;

    /// <summary>
    /// 该分类是否包含危险项 (用于 UI 显示警告 + 决定 IsSelected 默认值)。
    /// </summary>
    public bool HasDangerousItems => Items.Any(i => i.IsDangerous);

    /// <summary>
    /// 该分类是否包含 Caution 项 (用于 UI 显示中等警告)。
    /// </summary>
    public bool HasCautionItems => Items.Any(i => i.Safety == CleanSafety.Caution);

    private bool? _isSelected = true;
    /// <summary>
    /// 分类勾选状态（三态）：true=全选，false=全不选，null=部分选中。
    /// 联动条目的逻辑由 CleanViewModel 统一处理，避免模型层反向依赖 ViewModel。
    /// </summary>
    public bool? IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    private bool _isExpanded = false;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
    }

    public string TotalSizeText => ByteSizeFormatter.Format(ReclaimableSize);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ScanProgress
{
    public string CurrentTask { get; set; } = "";
    public int CategoriesScanned { get; set; }
    public int TotalCategories { get; set; }
    public double ProgressPercent => TotalCategories > 0 ? (double)CategoriesScanned / TotalCategories * 100 : 0;
}

public class CleanResult
{
    public int FilesDeleted { get; set; }
    public int FoldersDeleted { get; set; }
    public long BytesFreed { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<CleanableItem> DeletedItems { get; set; } = new();

    /// <summary>本次清理实际删除的文件/目录路径（用于 UI 精确移除已删除项）。</summary>
    public List<string> DeletedPaths { get; set; } = new();

    /// <summary>是否报告了任何错误或警告</summary>
    public bool HasIssues => Errors.Count > 0 || Warnings.Count > 0;

    public string FreedText => ByteSizeFormatter.Format(BytesFreed);
}

public class DiskInfo
{
    public string DriveLetter { get; set; } = "";
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long FreeBytes { get; set; }
    public double UsedPercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;

    public string TotalText => $"{TotalBytes / 1_073_741_824.0:F1} GB";
    public string UsedText => $"{UsedBytes / 1_073_741_824.0:F1} GB";
    public string FreeText => $"{FreeBytes / 1_073_741_824.0:F1} GB";
}

public class LargeFileItem : INotifyPropertyChanged
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = "";
    public string SafetyHint { get; set; } = "";
    public string FileType { get; set; } = "";
    public string FileDesc { get; set; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string SizeText => ByteSizeFormatter.Format(SizeBytes);

    public string SafetyText => SafetyHint switch
    {
        "safe" => "可安全删除",
        "caution" => "请确认后删除",
        "danger" => "谨慎删除",
        _ => "请确认"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
