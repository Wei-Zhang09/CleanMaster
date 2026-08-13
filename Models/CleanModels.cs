using System.ComponentModel;
using System.Runtime.CompilerServices;

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
        ".pst", ".ost", ".db", ".sqlite"
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

    /// <summary>
    /// 是否需要二次确认 (Caution + Dangerous 都需要)。
    /// </summary>
    public bool RequiresConfirmation => Safety != CleanSafety.Safe;

    public bool IsDirectory { get; set; }
    public DateTime LastModified { get; set; }

    public string SizeText => SizeBytes switch
    {
        >= 1_073_741_824 => $"{SizeBytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{SizeBytes / 1_048_576.0:F1} MB",
        >= 1024 => $"{SizeBytes / 1024.0:F1} KB",
        _ => $"{SizeBytes} B"
    };

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
    public int ItemCount => Items.Count;

    /// <summary>
    /// 该分类是否包含危险项 (用于 UI 显示警告 + 决定 IsSelected 默认值)。
    /// </summary>
    public bool HasDangerousItems => Items.Any(i => i.IsDangerous);

    /// <summary>
    /// 该分类是否包含 Caution 项 (用于 UI 显示中等警告)。
    /// </summary>
    public bool HasCautionItems => Items.Any(i => i.Safety == CleanSafety.Caution);

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    private bool _isExpanded = false;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandButtonText)); } }
    }

    public string TotalSizeText => TotalSize switch
    {
        >= 1_073_741_824 => $"{TotalSize / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{TotalSize / 1_048_576.0:F1} MB",
        _ => $"{TotalSize / 1024.0:F1} KB"
    };

    public string ExpandButtonText => IsExpanded ? "收起" : "展开";

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

    /// <summary>是否报告了任何错误或警告</summary>
    public bool HasIssues => Errors.Count > 0 || Warnings.Count > 0;

    public string FreedText => BytesFreed switch
    {
        >= 1_073_741_824 => $"{BytesFreed / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{BytesFreed / 1_048_576.0:F1} MB",
        _ => $"{BytesFreed / 1024.0:F1} KB"
    };
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

    public string SizeText => SizeBytes switch
    {
        >= 1_073_741_824 => $"{SizeBytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{SizeBytes / 1_048_576.0:F1} MB",
        _ => $"{SizeBytes / 1024.0:F1} KB"
    };

    public string SafetyColor => SafetyHint switch
    {
        "safe" => "#10B981",
        "caution" => "#F59E0B",
        "danger" => "#EF4444",
        _ => "#64748B"
    };

    public string SafetyText => SafetyHint switch
    {
        "safe" => "可安全删除",
        "caution" => "请确认后删除",
        "danger" => "谨慎删除",
        _ => ""
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
