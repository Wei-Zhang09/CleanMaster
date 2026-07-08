namespace CleanMaster.Models;

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

public class CleanableItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public CleanSafety Safety { get; set; }
    public CleanCategory Category { get; set; }
    public string Description { get; set; } = "";
    public bool IsSelected { get; set; } = true;
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
        CleanSafety.Safe => "Safe",
        CleanSafety.Caution => "Caution",
        CleanSafety.Dangerous => "Dangerous",
        _ => "Unknown"
    };
}

public class ScanCategoryResult
{
    public CleanCategory Category { get; set; }
    public string DisplayName { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<CleanableItem> Items { get; set; } = new();
    public long TotalSize => Items.Sum(i => i.SizeBytes);
    public int ItemCount => Items.Count;
    public bool IsSelected { get; set; } = true;

    public string TotalSizeText => TotalSize switch
    {
        >= 1_073_741_824 => $"{TotalSize / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{TotalSize / 1_048_576.0:F1} MB",
        _ => $"{TotalSize / 1024.0:F1} KB"
    };
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
    public List<CleanableItem> DeletedItems { get; set; } = new();

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

public class LargeFileItem
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = "";
    public bool IsSelected { get; set; }
    public string SafetyHint { get; set; } = "";
    public string FileType { get; set; } = "";
    public string FileDesc { get; set; } = "";

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
}
