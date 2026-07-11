using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

public class LangService : ILangService, INotifyPropertyChanged
{
    public static LangService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isChinese = true;
    public bool IsChinese
    {
        get => _isChinese;
        set { _isChinese = value; OnPropertyChanged(); OnPropertyChanged("Item"); }
    }

    public string this[string key] => _isChinese ? GetChinese(key) : GetEnglish(key);

    public void Toggle()
    {
        IsChinese = !IsChinese;
    }

    private static string GetChinese(string key) => key switch
    {
        // Sidebar
        "AppTitle" => "清理大师",
        "NavClean" => "磁盘清理",
        "NavLargeFiles" => "大文件查找",
        "NavDuplicates" => "重复文件",
        "NavSoftware" => "软件管理",
        "NavStartup" => "启动项",
        "NavSettings" => "设置",
        "DiskFree" => "可用",

        // Clean page
        "CleanTitle" => "磁盘清理",
        "Scan" => "扫描",
        "Clean" => "清理",
        "Cancel" => "取消",
        "ItemsFound" => "发现项目",
        "CleanableSize" => "可清理大小",
        "Categories" => "分类",
        "Safe" => "安全",
        "Caution" => "谨慎",
        "Dangerous" => "危险",
        "Items" => "项",
        "Ready" => "准备就绪",
        "Scanning" => "正在扫描...",
        "ScanComplete" => "扫描完成",
        "Cleaning" => "正在清理...",
        "CleanComplete" => "清理完成",
        "Cancelled" => "已取消",
        "LastCleanup" => "上次清理结果",
        "Freed" => "释放",

        // Large Files
        "LargeFilesTitle" => "大文件查找",
        "MinSize" => "最小大小",
        "Search" => "搜索",
        "DeleteSelected" => "删除选中",
        "Searching" => "正在搜索...",
        "Found" => "找到",
        "FilesLargerThan" => "个大于",
        "LargeFiles" => "的文件",
        "Deleted" => "已删除",
        "Files" => "个文件",

        // Duplicates
        "DuplicatesTitle" => "重复文件查找",
        "ScanDuplicates" => "扫描重复文件",
        "DeleteDuplicates" => "删除重复项",
        "ScanningFiles" => "正在扫描文件...",
        "DuplicateGroups" => "个重复组",
        "Wasted" => "浪费",
        "Duplicates" => "个副本",
        "Keep" => "保留",
        "Delete" => "删除",

        // Software
        "SoftwareTitle" => "软件管理",
        "ProgramsInstalled" => "个程序已安装",
        "Uninstall" => "卸载",
        "Publisher" => "发布者",
        "Version" => "版本",

        // Startup
        "StartupTitle" => "启动项管理",
        "StartupItems" => "个启动项",
        "Disable" => "禁用",
        "Source" => "来源",

        // Settings
        "SettingsTitle" => "设置",
        "Language" => "语言",
        "Chinese" => "中文",
        "English" => "English",
        "About" => "关于",
        "AppVersion" => "版本",
        "Description" => "安全、透明的 Windows 磁盘清理工具",
        "SwitchLang" => "切换语言",

        _ => key
    };

    private static string GetEnglish(string key) => key switch
    {
        "AppTitle" => "CleanMaster",
        "NavClean" => "Clean",
        "NavLargeFiles" => "Large Files",
        "NavDuplicates" => "Duplicates",
        "NavSoftware" => "Software",
        "NavStartup" => "Startup",
        "NavSettings" => "Settings",
        "DiskFree" => "free",
        "CleanTitle" => "Disk Cleanup",
        "Scan" => "Scan",
        "Clean" => "Clean",
        "Cancel" => "Cancel",
        "ItemsFound" => "Items Found",
        "CleanableSize" => "Cleanable",
        "Categories" => "Categories",
        "Safe" => "Safe",
        "Caution" => "Caution",
        "Dangerous" => "Danger",
        "Items" => "items",
        "Ready" => "Ready",
        "Scanning" => "Scanning...",
        "ScanComplete" => "Scan complete",
        "Cleaning" => "Cleaning...",
        "CleanComplete" => "Clean complete",
        "Cancelled" => "Cancelled",
        "LastCleanup" => "Last Cleanup Result",
        "Freed" => "Freed",
        "LargeFilesTitle" => "Large Files",
        "MinSize" => "Min size",
        "Search" => "Search",
        "DeleteSelected" => "Delete Selected",
        "Searching" => "Searching...",
        "Found" => "Found",
        "FilesLargerThan" => "files larger than",
        "LargeFiles" => "",
        "Deleted" => "Deleted",
        "Files" => "files",
        "DuplicatesTitle" => "Duplicate Files",
        "ScanDuplicates" => "Scan Duplicates",
        "DeleteDuplicates" => "Delete Duplicates",
        "ScanningFiles" => "Scanning files...",
        "DuplicateGroups" => "duplicate groups",
        "Wasted" => "wasted",
        "Duplicates" => "duplicates",
        "Keep" => "Keep",
        "Delete" => "Delete",
        "SoftwareTitle" => "Software Manager",
        "ProgramsInstalled" => "programs installed",
        "Uninstall" => "Uninstall",
        "Publisher" => "Publisher",
        "Version" => "Version",
        "StartupTitle" => "Startup Manager",
        "StartupItems" => "startup items",
        "Disable" => "Disable",
        "Source" => "Source",
        "SettingsTitle" => "Settings",
        "Language" => "Language",
        "Chinese" => "中文",
        "English" => "English",
        "About" => "About",
        "AppVersion" => "Version",
        "Description" => "Safe and transparent Windows disk cleanup tool",
        "SwitchLang" => "Switch Language",
        _ => key
    };

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
