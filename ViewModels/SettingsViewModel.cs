using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class DiskSpaceCategory
{
    public string Name { get; set; } = "";
    public string SizeText { get; set; } = "";
    public long SizeBytes { get; set; }
    public double Percentage { get; set; }
    public string Color { get; set; } = "";
}

public class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IScanService _scanService;
    private readonly IUpdateService _updateService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ILangService Lang { get; }

    public bool IsChinese
    {
        get => Lang.IsChinese;
        set { Lang.IsChinese = value; OnPropertyChanged(); OnPropertyChanged(nameof(Lang)); }
    }

    /// <summary>
    /// 应用版本号。单一来源：csproj 的 <Version>，installer.iss 通过 /DAppVersion 传入。
    /// </summary>
    public string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private string _websiteUrl = "";
    public string WebsiteUrl { get => _websiteUrl; set { _websiteUrl = value; OnPropertyChanged(); } }

    #region Update

    private bool _isCheckingUpdate;
    public bool IsCheckingUpdate { get => _isCheckingUpdate; set { _isCheckingUpdate = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCheckUpdate)); } }

    public bool CanCheckUpdate => !IsCheckingUpdate;

    private string _updateStatusText = "";
    public string UpdateStatusText { get => _updateStatusText; set { _updateStatusText = value; OnPropertyChanged(); } }

    private UpdateInfo? _pendingUpdate;
    public UpdateInfo? PendingUpdate { get => _pendingUpdate; set { _pendingUpdate = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUpdate)); } }

    public bool HasUpdate => _pendingUpdate != null;

    public RelayCommand CheckUpdateCommand { get; }

    #endregion

    #region Disk Space Analysis

    public ObservableCollection<DiskSpaceCategory> DiskSpaceCategories { get; } = new();

    private bool _isAnalyzing;
    public bool IsAnalyzing { get => _isAnalyzing; set { _isAnalyzing = value; OnPropertyChanged(); } }

    private string _analysisStatus = "";
    public string AnalysisStatus { get => _analysisStatus; set { _analysisStatus = value; OnPropertyChanged(); } }

    private string _analyzedDrive = SystemPaths.SystemDrive;
    public string AnalyzedDrive { get => _analyzedDrive; set { _analyzedDrive = value; OnPropertyChanged(); } }

    private long _totalUsedBytes;
    public long TotalUsedBytes { get => _totalUsedBytes; set { _totalUsedBytes = value; OnPropertyChanged(); } }

    public ObservableCollection<string> AvailableDrives { get; } = new();

    public RelayCommand AnalyzeDiskCommand { get; }

    #endregion

    public RelayCommand ToggleLangCommand { get; }
    public RelayCommand OpenWebsiteCommand { get; }
    public RelayCommand SaveWebsiteUrlCommand { get; }

    public SettingsViewModel(ISettingsService settingsService, IScanService scanService, ILangService langService, IUpdateService updateService)
    {
        _settingsService = settingsService;
        _scanService = scanService;
        _updateService = updateService;
        Lang = langService;
        WebsiteUrl = _settingsService.Get().WebsiteUrl;

        // 反向订阅 LangService.LanguageChanged: 即使语言被其它地方切换,
        // SettingsView 也能同步 RadioButton 状态以及本地 Lang 属性。
        Lang.LanguageChanged += OnLanguageChanged;

        ToggleLangCommand = new RelayCommand(() => { IsChinese = !IsChinese; OnPropertyChanged(nameof(Lang)); });
        OpenWebsiteCommand = new RelayCommand(OpenWebsite);
        SaveWebsiteUrlCommand = new RelayCommand(SaveWebsiteUrl);
        AnalyzeDiskCommand = new RelayCommand(async () => await AnalyzeDiskSpaceAsync());
        CheckUpdateCommand = new RelayCommand(async () => await CheckForUpdateAsync(silent: false));

        // Load available drives
        foreach (var disk in _scanService.GetAllDisks())
            AvailableDrives.Add(disk.DriveLetter);
        if (AvailableDrives.Count > 0)
            AnalyzedDrive = AvailableDrives[0];

        // 启动后静默检查一次更新
        _ = Task.Run(async () => await CheckForUpdateAsync(silent: true));
    }

    /// <summary>
    /// 检查更新。silent=true 时仅在有更新时提示，无更新不打扰；silent=false 时始终反馈结果。
    /// </summary>
    private async Task CheckForUpdateAsync(bool silent)
    {
        if (IsCheckingUpdate) return;
        IsCheckingUpdate = true;
        if (!silent) UpdateStatusText = "正在检查更新...";

        try
        {
            var update = await _updateService.CheckForUpdateAsync();
            if (update != null)
            {
                PendingUpdate = update;
                UpdateStatusText = $"发现新版本 v{update.Version}";
                // 弹窗提示，让用户决定是否立即更新
                var msg = $"发现新版本 v{update.Version}。\n\n{update.Notes}\n\n是否立即下载并安装更新？";
                var result = System.Windows.MessageBox.Show(msg, "软件更新",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await DownloadAndInstallAsync(update);
                }
            }
            else
            {
                PendingUpdate = null;
                if (!silent) UpdateStatusText = "已是最新版本";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = "检查更新失败";
            App.LogError("CheckForUpdateAsync", ex);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private async Task DownloadAndInstallAsync(UpdateInfo update)
    {
        UpdateStatusText = $"正在下载 v{update.Version}...";
        try
        {
            var progress = new Progress<double>(p => UpdateStatusText = $"正在下载 v{update.Version}... {p:F0}%");
            var installerPath = await _updateService.DownloadAsync(update, progress);
            UpdateStatusText = "下载完成，正在安装...";
            _updateService.InstallAndRestart(installerPath);
        }
        catch (Exception ex)
        {
            UpdateStatusText = "更新失败";
            App.LogError("DownloadAndInstallAsync", ex);
            System.Windows.MessageBox.Show($"更新失败：{ex.Message}", "软件更新",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task AnalyzeDiskSpaceAsync()
    {
        IsAnalyzing = true;
        DiskSpaceCategories.Clear();
        AnalysisStatus = $"正在分析 {AnalyzedDrive} 磁盘空间...";

        try
        {
            await Task.Run(() =>
            {
                var diskInfo = _scanService.GetDiskInfo(AnalyzedDrive);
                TotalUsedBytes = diskInfo.UsedBytes;

                var categories = new List<DiskSpaceCategory>();
                var driveRoot = AnalyzedDrive.TrimEnd('\\') + "\\";

                // ── 动态扫描所选盘符的顶层目录 (不再硬编码 Windows/Program Files/Users) ──
                var dirSizes = new List<(string Name, long Size)>();

                try
                {
                    foreach (var dir in Directory.GetDirectories(driveRoot))
                    {
                        try
                        {
                            var name = Path.GetFileName(dir.TrimEnd('\\'));
                            if (string.IsNullOrEmpty(name)) continue;
                            var size = GetDirectorySize(dir);
                            dirSizes.Add((name, size));
                        }
                        catch { /* 跳过无法访问的目录 */ }
                    }
                }
                catch (Exception ex) { CleanMaster.App.LogError("AnalyzeDiskSpace-enum", ex); }

                // 按大小降序，取前 12 个最大的目录作为分类
                var topDirs = dirSizes.OrderByDescending(d => d.Size).Take(12).ToList();

                var palette = new[]
                {
                    "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6",
                    "#EC4899", "#14B8A6", "#F97316", "#6366F1", "#84CC16",
                    "#0EA5E9", "#A855F7"
                };

                for (int i = 0; i < topDirs.Count; i++)
                {
                    categories.Add(new DiskSpaceCategory
                    {
                        Name = topDirs[i].Name,
                        SizeBytes = topDirs[i].Size,
                        Color = palette[i % palette.Length]
                    });
                }

                // 根目录下的散落文件 + 未计入的小目录 → "其他文件"
                long rootFilesSize = 0;
                try
                {
                    foreach (var file in Directory.GetFiles(driveRoot))
                    {
                        try { rootFilesSize += new FileInfo(file).Length; } catch { }
                    }
                }
                catch { }

                var accounted = topDirs.Sum(d => d.Size) + rootFilesSize;
                var otherSize = Math.Max(0, diskInfo.UsedBytes - accounted);
                categories.Add(new DiskSpaceCategory
                {
                    Name = "其他文件",
                    SizeBytes = otherSize,
                    Color = "#94A3B8"
                });

                // 移除零大小分类
                categories.RemoveAll(c => c.SizeBytes <= 0);

                // 计算百分比
                foreach (var cat in categories)
                {
                    cat.Percentage = diskInfo.UsedBytes > 0 ? (double)cat.SizeBytes / diskInfo.UsedBytes * 100 : 0;
                    cat.SizeText = ByteSizeFormatter.Format(cat.SizeBytes);
                }

                // 按大小降序
                categories.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var cat in categories) DiskSpaceCategories.Add(cat);
                }));
            });

            AnalysisStatus = $"{AnalyzedDrive} 分析完成";
        }
        catch (Exception ex)
        {
            AnalysisStatus = $"分析失败: {ex.Message}";
            CleanMaster.App.LogError("AnalyzeDiskSpace", ex);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true
                }))
                {
                    try { size += new FileInfo(file).Length; } catch { }
                }
            }
        }
        catch { }
        return size;
    }

    private void OpenWebsite()
    {
        try
        {
            var url = _settingsService.Get().WebsiteUrl;
            // 打开前校验协议，只允许 http/https（深度防御）
            if (!UrlGuard.TryGetWebUri(url, out var uri))
            {
                System.Windows.MessageBox.Show("网站地址无效，仅支持 http/https 链接。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { CleanMaster.App.LogError("OpenWebsite", ex); }
    }

    private void SaveWebsiteUrl()
    {
        try
        {
            // 保存前校验协议，仅允许 http/https
            if (!UrlGuard.TryGetWebUri(WebsiteUrl, out _))
            {
                System.Windows.MessageBox.Show("网站地址无效，仅支持 http/https 链接。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = _settingsService.Get();
            settings.WebsiteUrl = WebsiteUrl;
            _settingsService.Save(settings);
            System.Windows.MessageBox.Show("网站地址已保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { CleanMaster.App.LogError("SaveWebsiteUrl", ex); }
    }

    private void OnLanguageChanged()
    {
        // LangService 已经切换了 IsChinese, 通知 WPF 让 RadioButton 和 Lang[] 绑定刷新。
        try
        {
            App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(IsChinese));
                OnPropertyChanged(nameof(Lang));
            }));
        }
        catch
        {
            // App 当前可能未启动 (单元测试场景): 同步触发属性变更
            OnPropertyChanged(nameof(IsChinese));
            OnPropertyChanged(nameof(Lang));
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Lang.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
