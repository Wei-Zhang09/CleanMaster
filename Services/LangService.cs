using System.ComponentModel;
using System.Runtime.CompilerServices;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

public class LangService : ILangService, INotifyPropertyChanged
{
    public static LangService Instance { get; } = new();

    /// <summary>
    /// Raised when the active language changes. ViewModels subscribe to this and
    /// fire a PropertyChanged for their <c>Lang</c> property so XAML bindings
    /// like <c>{Binding Lang[SomeKey]}</c> re-evaluate across the whole app.
    /// </summary>
    public event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isChinese = true;
    public bool IsChinese
    {
        get => _isChinese;
        set
        {
            if (_isChinese == value) return;
            _isChinese = value;
            // Fire PropertyChanged for the indexer so {Binding Lang[Key]} re-reads.
            // Using "" as the property name signals the indexer to WPF.
            OnPropertyChanged("Item");
            OnPropertyChanged(nameof(IsChinese));
            LanguageChanged?.Invoke();
        }
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
        "NavLargeFolders" => "大文件夹扫描",
        "NavDiskAnalysis" => "磁盘分析",
        "NavSystemCleanup" => "系统清理",
        "NavGuide" => "使用指南",
        "NavFollowAuthor" => "关注作者",
        "NavDonate" => "打赏作者",
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

        // Large Files / Large Folders
        "LargeFilesTitle" => "大文件查找",
        "LargeFoldersTitle" => "大文件夹扫描",
        "LargeFoldersHint" => "扫描占用空间较大的文件夹，帮助您找到空间占用的来源。",
        "MinSize" => "最小大小",
        "Search" => "搜索",
        "DeleteSelected" => "删除选中",
        "Searching" => "正在搜索...",
        "Found" => "找到",
        "FilesLargerThan" => "个大于",
        "LargeFiles" => "的文件",
        "Deleted" => "已删除",
        "Files" => "个文件",
        "OpenFolder" => "前往目录",

        // Disk Analysis
        "DiskAnalysisTitle" => "磁盘分析",
        "DiskAnalysisHint" => "分析磁盘空间占用情况，帮助您了解各类文件的空间占用比例。",
        "DiskAnalysisSelectDrive" => "选择磁盘:",
        "DiskAnalysisStart" => "开始分析",
        "DiskAnalysisAnalyzing" => "正在分析 {0} 磁盘空间...",
        "DiskAnalysisDone" => "{0} 分析完成",
        "DiskAnalysisFailed" => "分析失败: {0}",
        "DiskCategoryWindows" => "Windows 系统",
        "DiskCategoryProgramFiles" => "Program Files",
        "DiskCategoryProgramFilesX86" => "Program Files (x86)",
        "DiskCategoryUsers" => "用户数据",
        "DiskCategoryOther" => "其他文件",
        "DiskCategoryOtherInaccessible" => "0 B（含无法访问的目录）",

        // System Cleanup
        "SystemCleanupTitle" => "系统清理",
        "SystemCleanupHint" => "使用 Windows 内置工具进行深度清理，完全安全可靠。",
        "SystemCleanupProgressTitle" => "执行进度",
        "SystemCleanupDismTitle" => "Windows 组件清理",
        "SystemCleanupDismDesc" => "清理旧版本的 Windows 更新组件，通常可释放 1-5GB 空间。",
        "SystemCleanupDismMeta" => "安全等级：安全 | 耗时：3-10分钟",
        "SystemCleanupDismBtn" => "执行清理",
        "SystemCleanupSfcTitle" => "系统文件修复",
        "SystemCleanupSfcDesc" => "扫描并修复损坏的系统文件，解决系统异常问题。",
        "SystemCleanupSfcMeta" => "安全等级：安全 | 耗时：5-15分钟",
        "SystemCleanupSfcBtn" => "开始扫描",
        "SystemCleanupDnsTitle" => "DNS 缓存清理",
        "SystemCleanupDnsDesc" => "清理 DNS 解析缓存，解决网页无法访问的问题。",
        "SystemCleanupDnsMeta" => "安全等级：安全 | 耗时：即时",
        "SystemCleanupDnsBtn" => "清理",
        "SystemCleanupAdminNote" => "注意：系统清理功能需要管理员权限",

        // Guide
        "GuideTitle" => "使用指南",
        "GuideSafetyTitle" => "安全等级说明",
        "GuideSafetySafeDesc" => "删除不影响系统和软件正常运行，数据可自动重建",
        "GuideSafetyCautionDesc" => "删除前请确认内容，可能包含有用数据",
        "GuideSafetyDangerousDesc" => "删除可能导致系统异常，请勿轻易删除",
        "GuideCleanTitle" => "磁盘清理",
        "GuideCleanDesc" => "扫描并清理系统中的垃圾文件，包括临时文件、缓存、日志等。",
        "GuideCleanScope" => "扫描范围：",
        "GuideCleanScopeRecycleBin" => "- 回收站（安全）",
        "GuideCleanScopeTemp" => "- Windows 临时文件（安全）",
        "GuideCleanScopeUpdate" => "- Windows 更新缓存（安全）",
        "GuideCleanScopeBrowser" => "- 浏览器缓存：Chrome、Edge、Firefox、Brave、Opera 等（安全）",
        "GuideCleanScopeDev" => "- 开发工具缓存：VS Code、Docker、JetBrains、npm、pip 等（安全）",
        "GuideCleanScopeApp" => "- 应用缓存：QQ、微信、腾讯视频、爱奇艺、哔哩哔哩、抖音等（安全）",
        "GuideCleanScopeLog" => "- 日志文件、崩溃转储（安全）",
        "GuideCleanTip" => "提示：点击分类右侧的箭头可展开查看详细文件列表",
        "GuideLargeFilesTitle" => "大文件查找",
        "GuideLargeFilesDesc" => "扫描磁盘中占用空间较大的文件，支持设置最小文件大小。",
        "GuideLargeFilesNote" => "每个文件都会显示类型和安全提示，帮助您判断是否可以删除。",
        "GuideSoftwareTitle" => "软件管理",
        "GuideSoftwareDesc" => "查看已安装的软件并卸载，自动清理残留文件和注册表。",
        "GuideSoftwareNote" => "卸载后会自动扫描残留文件夹和注册表项，推荐一并清理。",
        "GuideStartupTitle" => "启动项管理",
        "GuideStartupDesc" => "管理开机自启动的程序，禁用不需要的启动项可加快开机速度。",
        "GuideStartupNote" => "支持从注册表和启动文件夹两个位置管理启动项。",
        "GuideLargeFoldersTitle" => "大文件夹扫描",
        "GuideLargeFoldersDesc" => "扫描占用空间较大的文件夹，帮助您找到空间占用的来源。",
        "GuideLargeFoldersNote" => "适合在大文件查找无法找到足够空间时使用，许多小文件聚集也会占用大量空间。",
        "GuideDiskAnalysisTitle" => "磁盘分析",
        "GuideDiskAnalysisDesc" => "分析磁盘空间占用情况，帮助您了解各类文件的空间占用比例。",
        "GuideDiskAnalysisScope" => "分析内容包括：",
        "GuideDiskAnalysisScopeWindows" => "- Windows 系统文件",
        "GuideDiskAnalysisScopeProgramFiles" => "- Program Files 程序文件",
        "GuideDiskAnalysisScopeUsers" => "- 用户数据文件",
        "GuideDiskAnalysisScopeOther" => "- 其他文件",
        "GuideSystemCleanupTitle" => "系统清理",
        "GuideSystemCleanupDesc" => "使用 Windows 内置工具进行深度清理，完全安全可靠。",
        "GuideSystemCleanupDism" => "- Windows 组件清理：删除旧版本更新文件（安全，1-5GB）",
        "GuideSystemCleanupSfc" => "- 系统文件修复：扫描修复损坏的系统文件（安全）",
        "GuideSystemCleanupDns" => "- DNS 缓存清理：重置域名解析缓存（安全）",

        // Follow Author
        "FollowAuthorTitle" => "关注作者",
        "FollowAuthorHeading" => "关注作者",
        "FollowAuthorDesc" => "把你们的想法私信作者，有机会让作者实现你的想法哦！另外，项目的最新动态也通过这两个平台实时更新，期待你的关注！",
        "FollowAuthorDouyin" => "抖音",
        "FollowAuthorBilibili" => "哔哩哔哩",
        "FollowAuthorWechat" => "公众号",
        "FollowAuthorNote" => "注：如遇到无法解决的问题，请通过以上两个平台联系作者，作者看到第一时间回复！",

        // Donate
        "DonateTitle" => "打赏作者",
        "DonateHeading" => "开源不易，感谢支持",
        "DonateDesc" => "如果这款软件对您有帮助，欢迎请我喝杯咖啡。您的支持是我持续更新的动力！",
        "DonateWechatPay" => "微信支付",

        // Empty folders
        "EmptyFoldersTitle" => "空文件夹清理",
        "EmptyFoldersHint" => "扫描并清理空文件夹，保持文件系统整洁。空文件夹不占用空间，但会影响查找效率。",

        // Registry Cleaner
        "RegistryCleanerTitle" => "注册表清理",
        "RegistryCleanerHint" => "扫描并清理无效的注册表项，提升系统稳定性。清理前会自动备份。",

        // Privacy Cleaner
        "PrivacyCleanerTitle" => "隐私清理",
        "PrivacyCleanerHint" => "清理浏览记录、最近文档、搜索历史等隐私数据，保护个人隐私。",

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
        "ProgramsLoaded" => "共 {0} 个软件",
        "Uninstall" => "卸载",
        "Publisher" => "发布者",
        "Version" => "版本",
        "SoftwareLoading" => "正在扫描已安装软件...",
        "SoftwareLoadingHint" => "首次加载可能需要几秒钟",
        "SoftwareLoadFailed" => "扫描失败",

        // Startup
        "StartupTitle" => "启动项管理",
        "StartupItems" => "个启动项",
        "Disable" => "禁用",
        "Enable" => "启用",
        "Source" => "来源",
        "StartupToggleEnable" => "启用",
        "StartupToggleDisable" => "禁用",
        "StartupLoading" => "正在扫描启动项...",
        "StartupLoadingHint" => "首次加载可能需要几秒钟",
        "StartupItemsLoaded" => "已加载 {0} 个启动项",
        "StartupLoadFailed" => "加载失败，请稍后重试",
        "Refresh" => "刷新",

        // Settings
        "SettingsTitle" => "设置",
        "Language" => "语言",
        "Chinese" => "中文",
        "English" => "English",
        "About" => "关于",
        "AppVersion" => "版本",
        "Description" => "安全、透明的 Windows 磁盘清理工具",
        "SwitchLang" => "切换语言",
        "OfficialWebsite" => "官方网站",
        "WebsiteTip" => "如官方网站无法访问，请通过抖音或B站联系作者获取最新地址。",
        "WechatOfficial" => "公众号",
        "WechatOfficialName" => "AWe-software",
        "WechatOfficialTip" => "关注公众号获取最新软件动态、使用技巧和问题解答。在微信搜索「AWe-software」即可找到我们。",
        "WechatOfficialGuide" => "长按识别下方二维码，或在微信中搜索「AWe-software」关注我们",
        "PathNotExist" => "路径不存在或无法访问。",
        "OpenFolderError" => "无法打开目录: {0}",

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
        "NavLargeFolders" => "Large Folders",
        "NavDiskAnalysis" => "Disk Analysis",
        "NavSystemCleanup" => "System Cleanup",
        "NavGuide" => "User Guide",
        "NavFollowAuthor" => "Follow Author",
        "NavDonate" => "Donate",
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
        // Large Files / Large Folders
        "LargeFilesTitle" => "Large Files",
        "LargeFoldersTitle" => "Large Folders",
        "LargeFoldersHint" => "Scan folders that take up significant space to find the source of disk usage.",
        "MinSize" => "Min size",
        "Search" => "Search",
        "DeleteSelected" => "Delete Selected",
        "Searching" => "Searching...",
        "Found" => "Found",
        "FilesLargerThan" => "files larger than",
        "LargeFiles" => "",
        "Deleted" => "Deleted",
        "Files" => "files",
        "OpenFolder" => "Open Folder",

        // Disk Analysis
        "DiskAnalysisTitle" => "Disk Analysis",
        "DiskAnalysisHint" => "Analyze disk space usage to understand the proportion of various file types.",
        "DiskAnalysisSelectDrive" => "Drive:",
        "DiskAnalysisStart" => "Start Analysis",
        "DiskAnalysisAnalyzing" => "Analyzing {0} disk space...",
        "DiskAnalysisDone" => "{0} analysis complete",
        "DiskAnalysisFailed" => "Analysis failed: {0}",
        "DiskCategoryWindows" => "Windows System",
        "DiskCategoryProgramFiles" => "Program Files",
        "DiskCategoryProgramFilesX86" => "Program Files (x86)",
        "DiskCategoryUsers" => "User Data",
        "DiskCategoryOther" => "Other Files",
        "DiskCategoryOtherInaccessible" => "0 B (includes inaccessible folders)",

        // System Cleanup
        "SystemCleanupTitle" => "System Cleanup",
        "SystemCleanupHint" => "Deep cleanup using built-in Windows tools — completely safe and reliable.",
        "SystemCleanupProgressTitle" => "Progress",
        "SystemCleanupDismTitle" => "Windows Component Cleanup",
        "SystemCleanupDismDesc" => "Clean up old Windows update components; typically frees 1-5 GB.",
        "SystemCleanupDismMeta" => "Safety: Safe | Duration: 3-10 minutes",
        "SystemCleanupDismBtn" => "Run Cleanup",
        "SystemCleanupSfcTitle" => "System File Repair",
        "SystemCleanupSfcDesc" => "Scan and repair corrupted system files to resolve system issues.",
        "SystemCleanupSfcMeta" => "Safety: Safe | Duration: 5-15 minutes",
        "SystemCleanupSfcBtn" => "Start Scan",
        "SystemCleanupDnsTitle" => "DNS Cache Cleanup",
        "SystemCleanupDnsDesc" => "Clear DNS resolution cache to resolve inaccessible websites.",
        "SystemCleanupDnsMeta" => "Safety: Safe | Duration: Instant",
        "SystemCleanupDnsBtn" => "Clean",
        "SystemCleanupAdminNote" => "Note: System cleanup requires administrator privileges.",

        // Guide
        "GuideTitle" => "User Guide",
        "GuideSafetyTitle" => "Safety Levels",
        "GuideSafetySafeDesc" => "Deletion does not affect system/software operation; data is auto-rebuilt.",
        "GuideSafetyCautionDesc" => "Verify contents before deletion; may contain useful data.",
        "GuideSafetyDangerousDesc" => "Deletion may cause system issues; do not delete lightly.",
        "GuideCleanTitle" => "Disk Cleanup",
        "GuideCleanDesc" => "Scan and clean junk files including temporary files, caches, and logs.",
        "GuideCleanScope" => "Scan scope:",
        "GuideCleanScopeRecycleBin" => "- Recycle Bin (Safe)",
        "GuideCleanScopeTemp" => "- Windows Temp Files (Safe)",
        "GuideCleanScopeUpdate" => "- Windows Update Cache (Safe)",
        "GuideCleanScopeBrowser" => "- Browser Cache: Chrome, Edge, Firefox, Brave, Opera, etc. (Safe)",
        "GuideCleanScopeDev" => "- Dev Tool Cache: VS Code, Docker, JetBrains, npm, pip, etc. (Safe)",
        "GuideCleanScopeApp" => "- App Cache: QQ, WeChat, Tencent Video, iQIYI, Bilibili, Douyin, etc. (Safe)",
        "GuideCleanScopeLog" => "- Log files, crash dumps (Safe)",
        "GuideCleanTip" => "Tip: Click the arrow on the right of a category to expand the file list.",
        "GuideLargeFilesTitle" => "Large Files",
        "GuideLargeFilesDesc" => "Scan large files on disk; minimum file size is configurable.",
        "GuideLargeFilesNote" => "Each file shows its type and safety hint to help you decide whether to delete.",
        "GuideSoftwareTitle" => "Software Manager",
        "GuideSoftwareDesc" => "View installed software and uninstall it; residue files and registry are auto-cleaned.",
        "GuideSoftwareNote" => "After uninstall, residual folders and registry entries are scanned; recommend cleaning them too.",
        "GuideStartupTitle" => "Startup Manager",
        "GuideStartupDesc" => "Manage auto-start programs; disabling unused items speeds up boot.",
        "GuideStartupNote" => "Supports managing startup items from both registry and startup folder.",
        "GuideLargeFoldersTitle" => "Large Folders",
        "GuideLargeFoldersDesc" => "Scan folders that take up significant space to find the source of disk usage.",
        "GuideLargeFoldersNote" => "Useful when Large Files doesn't free enough space; many small files together also take a lot.",
        "GuideDiskAnalysisTitle" => "Disk Analysis",
        "GuideDiskAnalysisDesc" => "Analyze disk space usage to understand the proportion of various file types.",
        "GuideDiskAnalysisScope" => "Analysis includes:",
        "GuideDiskAnalysisScopeWindows" => "- Windows system files",
        "GuideDiskAnalysisScopeProgramFiles" => "- Program Files",
        "GuideDiskAnalysisScopeUsers" => "- User data files",
        "GuideDiskAnalysisScopeOther" => "- Other files",
        "GuideSystemCleanupTitle" => "System Cleanup",
        "GuideSystemCleanupDesc" => "Deep cleanup using built-in Windows tools — completely safe and reliable.",
        "GuideSystemCleanupDism" => "- Windows Component Cleanup: delete old update files (Safe, 1-5 GB)",
        "GuideSystemCleanupSfc" => "- System File Repair: scan and repair corrupted system files (Safe)",
        "GuideSystemCleanupDns" => "- DNS Cache Cleanup: reset DNS resolution cache (Safe)",

        // Follow Author
        "FollowAuthorTitle" => "Follow Author",
        "FollowAuthorHeading" => "Follow the Author",
        "FollowAuthorDesc" => "Send your ideas via DM — the author may bring them to life! The latest project updates are posted on these platforms. Looking forward to your follow!",
        "FollowAuthorDouyin" => "Douyin",
        "FollowAuthorBilibili" => "Bilibili",
        "FollowAuthorWechat" => "WeChat OA",
        "FollowAuthorNote" => "Note: If you encounter any issue, contact the author via the platforms above; the author will reply as soon as seen!",

        // Donate
        "DonateTitle" => "Donate",
        "DonateHeading" => "Open source is hard — thank you for your support",
        "DonateDesc" => "If this software helps you, please buy me a coffee. Your support keeps me updating!",
        "DonateWechatPay" => "WeChat Pay",

        // Empty folders
        "EmptyFoldersTitle" => "Empty Folder Cleanup",
        "EmptyFoldersHint" => "Scan and clean empty folders to keep the filesystem tidy. Empty folders don't consume space but slow down searches.",

        // Registry Cleaner
        "RegistryCleanerTitle" => "Registry Cleaner",
        "RegistryCleanerHint" => "Scan and clean invalid registry entries to improve system stability. Auto-backup before cleaning.",

        // Privacy Cleaner
        "PrivacyCleanerTitle" => "Privacy Cleaner",
        "PrivacyCleanerHint" => "Clean browsing history, recent documents, search history, and other private data to protect your privacy.",
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
        "ProgramsLoaded" => "{0} programs in total",
        "Uninstall" => "Uninstall",
        "Publisher" => "Publisher",
        "Version" => "Version",
        "SoftwareLoading" => "Scanning installed software...",
        "SoftwareLoadingHint" => "First load may take a few seconds",
        "SoftwareLoadFailed" => "Scan failed",
        "StartupTitle" => "Startup Manager",
        "StartupItems" => "startup items",
        "Disable" => "Disable",
        "Enable" => "Enable",
        "Source" => "Source",
        "StartupToggleEnable" => "Enable",
        "StartupToggleDisable" => "Disable",
        "StartupLoading" => "Scanning startup items...",
        "StartupLoadingHint" => "First load may take a few seconds",
        "StartupItemsLoaded" => "Loaded {0} startup items",
        "StartupLoadFailed" => "Load failed, please retry later",
        "Refresh" => "Refresh",
        "PathNotExist" => "Path does not exist or is inaccessible.",
        "OpenFolderError" => "Cannot open folder: {0}",
        "SettingsTitle" => "Settings",
        "Language" => "Language",
        "Chinese" => "中文",
        "English" => "English",
        "About" => "About",
        "AppVersion" => "Version",
        "Description" => "Safe and transparent Windows disk cleanup tool",
        "SwitchLang" => "Switch Language",
        "OfficialWebsite" => "Official Website",
        "WebsiteTip" => "If the official website is unreachable, contact the author via Douyin or Bilibili for the latest address.",
        "WechatOfficial" => "WeChat Official Account",
        "WechatOfficialName" => "AWe-software",
        "WechatOfficialTip" => "Follow our WeChat official account for the latest updates, tips, and support. Search 'AWe-software' in WeChat to find us.",
        "WechatOfficialGuide" => "Long-press the QR code below or search 'AWe-software' in WeChat to follow us",
        _ => key
    };

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
