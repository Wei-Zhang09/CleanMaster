using System.Diagnostics;
using System.IO;
using CleanMaster.Models;
using CleanMaster.Rules;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.Services;

public class ScanService : IScanService
{
    public event Action<ScanProgress>? ProgressChanged;
    public event Action<ScanCategoryResult>? CategoryScanned;

    public async Task<List<ScanCategoryResult>> ScanAllAsync(CancellationToken ct = default)
    {
        var results = new List<ScanCategoryResult>();
        var rules = RuleDatabase.GetAllRules();
        var grouped = rules.GroupBy(r => r.Category).ToList();

        var progress = new ScanProgress { TotalCategories = grouped.Count };

        foreach (var group in grouped)
        {
            ct.ThrowIfCancellationRequested();

            progress.CurrentTask = GetCategoryName(group.Key);
            ProgressChanged?.Invoke(progress);

            var result = await Task.Run(() => ScanCategory(group.Key, group.ToList()), ct);

            if (result.Items.Count > 0)
            {
                results.Add(result);
                CategoryScanned?.Invoke(result);
            }

            progress.CategoriesScanned++;
            ProgressChanged?.Invoke(progress);
        }

        return results.OrderByDescending(r => r.TotalSize).ToList();
    }

    private ScanCategoryResult ScanCategory(CleanCategory category, List<CleanupRule> rules)
    {
        var result = new ScanCategoryResult
        {
            Category = category,
            DisplayName = GetCategoryName(category),
            Icon = GetCategoryIcon(category)
        };

        foreach (var rule in rules)
        {
            try
            {
                var path = rule.GetResolvedPath();
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path) && !File.Exists(path))
                    continue;

                // 从规则名称中提取软件信息
                var softwareName = ExtractSoftwareName(rule.Name);
                var fileType = ExtractFileType(rule.Name, rule.Description);

                if (Directory.Exists(path))
                {
                    ScanDirectory(path, rule, result, softwareName, fileType);
                }
                else if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    result.Items.Add(new CleanableItem
                    {
                        Name = fi.Name,
                        FullPath = fi.FullName,
                        SizeBytes = fi.Length,
                        Safety = rule.Safety,
                        Category = category,
                        Description = rule.Description,
                        SoftwareName = softwareName,
                        FileType = fileType,
                        LastModified = fi.LastWriteTime,
                        IsDirectory = false
                    });
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("ScanCategory", ex); }
        }

        return result;
    }

    private void ScanDirectory(string path, CleanupRule rule, ScanCategoryResult result, string softwareName = "", string fileType = "")
    {
        try
        {
            var dirInfo = new DirectoryInfo(path);

            if (rule.FilePatterns != null && rule.FilePatterns.Length > 0)
            {
                foreach (var pattern in rule.FilePatterns)
                {
                    var subPath = Path.Combine(path, pattern);
                    if (Directory.Exists(subPath))
                    {
                        var size = GetDirectorySize(subPath);
                        if (size > 0)
                        {
                            result.Items.Add(new CleanableItem
                            {
                                Name = $"{rule.Name} - {pattern}",
                                FullPath = subPath,
                                SizeBytes = size,
                                Safety = rule.Safety,
                                Category = rule.Category,
                                Description = rule.Description,
                                SoftwareName = softwareName,
                                FileType = fileType,
                                LastModified = Directory.GetLastWriteTime(subPath),
                                IsDirectory = true
                            });
                        }
                    }
                }
            }
            else
            {
                var size = GetDirectorySize(path);
                if (size > 0)
                {
                    result.Items.Add(new CleanableItem
                    {
                        Name = rule.Name,
                        FullPath = path,
                        SizeBytes = size,
                        Safety = rule.Safety,
                        Category = rule.Category,
                        Description = rule.Description,
                        SoftwareName = softwareName,
                        FileType = fileType,
                        LastModified = Directory.GetLastWriteTime(path),
                        IsDirectory = true
                    });
                }
            }
        }
        catch (Exception ex) { CleanMaster.App.LogError("ScanDirectory", ex); }
    }

    public static long GetDirectorySize(string path, int maxDepth = -1)
        => FileSystemUtils.GetDirectorySize(path, maxDepth);

    private static string ExtractSoftwareName(string ruleName)
    {
        // 从规则名称中提取软件名称
        // 例如 "Chrome Cache" -> "Chrome"
        //      "WeChat Cache" -> "微信"
        //      "QQ Temp" -> "QQ"

        var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Chrome", "Chrome" },
            { "Edge", "Edge" },
            { "Firefox", "Firefox" },
            { "Brave", "Brave" },
            { "Opera", "Opera" },
            { "Vivaldi", "Vivaldi" },
            { "WeChat", "微信" },
            { "QQ", "QQ" },
            { "Tencent Video", "腾讯视频" },
            { "iQiyi", "爱奇艺" },
            { "Youku", "优酷" },
            { "Bilibili", "哔哩哔哩" },
            { "NetEase Music", "网易云音乐" },
            { "QQ Music", "QQ音乐" },
            { "Kuwo", "酷我音乐" },
            { "Kugou", "酷狗音乐" },
            { "Douyin", "抖音" },
            { "Taobao", "淘宝" },
            { "JD", "京东" },
            { "Meituan", "美团" },
            { "Eleme", "饿了么" },
            { "DingTalk", "钉钉" },
            { "Feishu", "飞书" },
            { "AliyunPan", "阿里云盘" },
            { "Baidu Netdisk", "百度网盘" },
            { "WPS", "WPS" },
            { "WeGame", "WeGame" },
            { "VS Code", "VS Code" },
            { "Docker", "Docker" },
            { "Postman", "Postman" },
            { "Notion", "Notion" },
            { "Slack", "Slack" },
            { "Discord", "Discord" },
            { "Gradle", "Gradle" },
            { "NuGet", "NuGet" },
            { "npm", "npm" },
            { "pip", "pip" },
            { "Maven", "Maven" },
            { "JetBrains", "JetBrains" }
        };

        foreach (var kvp in nameMap)
        {
            if (ruleName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return "";
    }

    private static string ExtractFileType(string ruleName, string description)
    {
        // 提取文件类型
        if (ruleName.Contains("Cache", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("cache", StringComparison.OrdinalIgnoreCase))
            return "缓存";

        if (ruleName.Contains("Temp", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("temp", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("temporary", StringComparison.OrdinalIgnoreCase))
            return "临时文件";

        if (ruleName.Contains("Log", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("log", StringComparison.OrdinalIgnoreCase))
            return "日志";

        if (ruleName.Contains("Crash", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("dump", StringComparison.OrdinalIgnoreCase))
            return "崩溃转储";

        if (ruleName.Contains("Prefetch", StringComparison.OrdinalIgnoreCase))
            return "预读取";

        return "缓存";
    }

    public DiskInfo GetDiskInfo(string drive = "C:")
    {
        var di = new DriveInfo(drive);
        return new DiskInfo
        {
            DriveLetter = drive,
            TotalBytes = di.TotalSize,
            UsedBytes = di.TotalSize - di.AvailableFreeSpace,
            FreeBytes = di.AvailableFreeSpace
        };
    }

    public List<DiskInfo> GetAllDisks()
    {
        var disks = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    disks.Add(new DiskInfo
                    {
                        DriveLetter = drive.Name.Replace(@"\", ""),
                        TotalBytes = drive.TotalSize,
                        UsedBytes = drive.TotalSize - drive.AvailableFreeSpace,
                        FreeBytes = drive.AvailableFreeSpace
                    });
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("GetAllDisks", ex); }
        }
        return disks;
    }

    public async Task<List<LargeFileItem>> FindLargeFilesAsync(
        string drive = @"C:\",
        long minSizeBytes = 100 * 1024 * 1024,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var files = new List<LargeFileItem>();

            // Directories to skip (system/important)
            var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\Windows",
                @"C:\$Recycle.Bin",
                @"C:\System Volume Information",
                @"C:\ProgramData\Microsoft",
                @"C:\ProgramData\Package Cache"
            };

            var safeExts = FileExtensionConstants.SafeExtensions;
            var cautionExts = FileExtensionConstants.CautionExtensions;

            try
            {
                foreach (var file in Directory.EnumerateFiles(drive, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.System
                }))
                {
                    ct.ThrowIfCancellationRequested();

                    // Skip if in excluded directory
                    var isInSkipDir = skipDirs.Any(d => file.StartsWith(d, StringComparison.OrdinalIgnoreCase));
                    if (isInSkipDir) continue;

                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length >= minSizeBytes)
                        {
                            var ext = fi.Extension.ToLower();
                            var (safety, fileType, fileDesc) = GetFileInfo(ext, fi.FullName);

                            files.Add(new LargeFileItem
                            {
                                FileName = fi.Name,
                                FullPath = fi.FullName,
                                SizeBytes = fi.Length,
                                LastModified = fi.LastWriteTime,
                                Extension = fi.Extension,
                                SafetyHint = safety,
                                FileType = fileType,
                                FileDesc = fileDesc
                            });
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"FindLargeFilesAsync: {ex.Message}"); }
                }
            }
            catch (Exception ex) { CleanMaster.App.LogError("FindLargeFilesAsync", ex); }
            return files.OrderByDescending(f => f.SizeBytes).Take(500).ToList();
        }, ct);
    }

    private static (string safety, string type, string desc) GetFileInfo(string ext, string fullPath)
    {
        var lowerPath = fullPath.ToLower();

        string safety = "unknown";
        if (FileExtensionConstants.SafeExtensions.Contains(ext)) safety = "safe";
        else if (FileExtensionConstants.CautionExtensions.Contains(ext)) safety = "caution";
        else if (FileExtensionConstants.DangerousExtensions.Contains(ext)) safety = "danger";

        // Type and description
        string type, desc;

        if (lowerPath.Contains("\\temp\\"))
        {
            type = "临时文件";
            desc = "应用程序产生的临时数据，删除不影响正常使用";
            safety = "safe";
        }
        else if (lowerPath.Contains("\\cache\\"))
        {
            type = "缓存文件";
            desc = "程序缓存数据，删除后会自动重建";
            safety = "safe";
        }
        else if (lowerPath.Contains("\\downloads\\"))
        {
            type = "下载文件";
            desc = "浏览器或应用下载的文件，请确认不需要后再删除";
        }
        else if (lowerPath.Contains("\\desktop\\"))
        {
            type = "桌面文件";
            desc = "桌面上的文件，请确认内容后再决定是否删除";
            safety = "caution";
        }
        else
        {
            (type, desc) = ext switch
            {
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => ("视频文件", "视频播放文件，如不需要可安全删除"),
                ".mp3" or ".wav" or ".flac" => ("音频文件", "音频播放文件，如不需要可安全删除"),
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => ("图片文件", "图片文件，请确认不需要后再删除"),
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => ("压缩包", "压缩文件，已解压后可安全删除"),
                ".iso" or ".img" => ("磁盘镜像", "光盘或磁盘镜像，已使用后可安全删除"),
                ".exe" or ".msi" => ("安装程序", "软件安装包，安装完成后可安全删除"),
                ".dll" or ".sys" or ".drv" => ("系统文件", "Windows系统文件，删除可能导致系统异常，请勿删除"),
                ".log" or ".etl" or ".evtx" => ("日志文件", "程序运行日志，删除不影响功能"),
                ".tmp" or ".temp" or ".cache" => ("临时文件", "临时数据，可安全删除"),
                ".dmp" or ".mdmp" => ("崩溃转储", "程序崩溃时生成的报告文件，可安全删除"),
                ".pst" or ".ost" => ("邮件数据", "Outlook邮件数据文件，删除可能丢失邮件，请谨慎"),
                ".db" or ".sqlite" or ".mdb" => ("数据库文件", "程序数据库，删除可能导致数据丢失，请谨慎"),
                ".bak" or ".old" => ("备份文件", "程序自动创建的备份，通常可安全删除"),
                ".doc" or ".docx" => ("Word文档", "Word文档文件，请确认不需要后再删除"),
                ".xls" or ".xlsx" => ("Excel表格", "Excel表格文件，请确认不需要后再删除"),
                ".ppt" or ".pptx" => ("PPT演示", "演示文稿文件，请确认不需要后再删除"),
                ".pdf" => ("PDF文档", "PDF文档文件，请确认不需要后再删除"),
                ".txt" or ".md" => ("文本文件", "纯文本文件，请确认内容后再删除"),
                ".py" or ".js" or ".ts" or ".java" or ".cpp" or ".cs" => ("代码文件", "源代码文件，请确认不需要后再删除"),
                _ => ("文件", "请确认文件用途后再决定是否删除")
            };
        }

        return (safety, type, desc);
    }

    public static string GetCategoryName(CleanCategory cat) => cat switch
    {
        CleanCategory.RecycleBin => "Recycle Bin",
        CleanCategory.TempFiles => "Temporary Files",
        CleanCategory.WindowsUpdate => "Windows Update",
        CleanCategory.WindowsLogs => "System Logs",
        CleanCategory.BrowserCache => "Browser Cache",
        CleanCategory.DevToolCache => "Dev Tool Cache",
        CleanCategory.AppCache => "App Cache",
        CleanCategory.InstallerCache => "Installer Cache",
        CleanCategory.CrashDumps => "Crash Dumps",
        CleanCategory.DesktopInstallers => "Desktop Installers",
        CleanCategory.LargeFiles => "Large Files",
        CleanCategory.DuplicateFiles => "Duplicate Files",
        _ => "Unknown"
    };

    public static string GetCategoryIcon(CleanCategory cat) => cat switch
    {
        CleanCategory.RecycleBin => "\uE74D",
        CleanCategory.TempFiles => "\uE7C3",
        CleanCategory.WindowsUpdate => "\uE777",
        CleanCategory.WindowsLogs => "\uE7BA",
        CleanCategory.BrowserCache => "\uE774",
        CleanCategory.DevToolCache => "\uE730",
        CleanCategory.AppCache => "\uE71D",
        CleanCategory.InstallerCache => "\uE7B8",
        CleanCategory.CrashDumps => "\uE730",
        CleanCategory.DesktopInstallers => "\uE7F4",
        CleanCategory.LargeFiles => "\uE7C3",
        CleanCategory.DuplicateFiles => "\uE8C8",
        _ => "\uE7C3"
    };
}
