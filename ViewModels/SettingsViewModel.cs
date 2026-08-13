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
    private readonly ILicenseService _licenseService;
    private readonly IScanService _scanService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ILangService Lang { get; }

    public bool IsChinese
    {
        get => Lang.IsChinese;
        set { Lang.IsChinese = value; OnPropertyChanged(); OnPropertyChanged(nameof(Lang)); }
    }

    private string _websiteUrl = "";
    public string WebsiteUrl { get => _websiteUrl; set { _websiteUrl = value; OnPropertyChanged(); } }

    private bool _isActivated;
    public bool IsActivated
    {
        get => _isActivated;
        set { _isActivated = value; OnPropertyChanged(); OnPropertyChanged(nameof(LicenseStatusText)); OnPropertyChanged(nameof(LicenseStatusColor)); }
    }

    private string _licenseStatusText = "";
    public string LicenseStatusText { get => _licenseStatusText; set { _licenseStatusText = value; OnPropertyChanged(); } }

    public string LicenseStatusColor => IsActivated ? "#10B981" : "#F59E0B";

    private string _activatedProduct = "";
    public string ActivatedProduct { get => _activatedProduct; set { _activatedProduct = value; OnPropertyChanged(); } }

    #region Disk Space Analysis

    public ObservableCollection<DiskSpaceCategory> DiskSpaceCategories { get; } = new();

    private bool _isAnalyzing;
    public bool IsAnalyzing { get => _isAnalyzing; set { _isAnalyzing = value; OnPropertyChanged(); } }

    private string _analysisStatus = "";
    public string AnalysisStatus { get => _analysisStatus; set { _analysisStatus = value; OnPropertyChanged(); } }

    private string _analyzedDrive = "C:";
    public string AnalyzedDrive { get => _analyzedDrive; set { _analyzedDrive = value; OnPropertyChanged(); } }

    private long _totalUsedBytes;
    public long TotalUsedBytes { get => _totalUsedBytes; set { _totalUsedBytes = value; OnPropertyChanged(); } }

    public ObservableCollection<string> AvailableDrives { get; } = new();

    public RelayCommand AnalyzeDiskCommand { get; }

    #endregion

    public RelayCommand ToggleLangCommand { get; }
    public RelayCommand ShowActivationCommand { get; }
    public RelayCommand OpenWebsiteCommand { get; }
    public RelayCommand SaveWebsiteUrlCommand { get; }

    public SettingsViewModel(ISettingsService settingsService, ILicenseService licenseService, IScanService scanService, ILangService langService)
    {
        _settingsService = settingsService;
        _licenseService = licenseService;
        _scanService = scanService;
        Lang = langService;
        WebsiteUrl = _settingsService.Get().WebsiteUrl;

        // 反向订阅 LangService.LanguageChanged: 即使语言被其它地方切换,
        // SettingsView 也能同步 RadioButton 状态以及本地 Lang 属性。
        Lang.LanguageChanged += OnLanguageChanged;

        ToggleLangCommand = new RelayCommand(() => { IsChinese = !IsChinese; OnPropertyChanged(nameof(Lang)); });
        ShowActivationCommand = new RelayCommand(ShowActivationDialog);
        OpenWebsiteCommand = new RelayCommand(OpenWebsite);
        SaveWebsiteUrlCommand = new RelayCommand(SaveWebsiteUrl);
        AnalyzeDiskCommand = new RelayCommand(async () => await AnalyzeDiskSpaceAsync());

        // Load available drives
        foreach (var disk in _scanService.GetAllDisks())
            AvailableDrives.Add(disk.DriveLetter);
        if (AvailableDrives.Count > 0)
            AnalyzedDrive = AvailableDrives[0];

#if DEBUG
        IsActivated = true;
        LicenseStatusText = "DEBUG - 已激活";
#else
        _ = Task.Run(async () =>
        {
            try { await CheckLicenseAsync(); }
            catch (Exception ex) { CleanMaster.App.LogError("CheckLicense", ex); }
        });
        LicenseStatusText = "检查激活状态...";
#endif
    }

    public async Task CheckLicenseAsync()
    {
        LicenseStatusText = "检查激活状态...";
        var (isValid, message) = await _licenseService.CheckActivationAsync();
        IsActivated = isValid;
        LicenseStatusText = isValid ? "已激活" : "未激活";

        if (isValid)
        {
            var info = _licenseService.LoadLocal();
            ActivatedProduct = info?.SoftwareName ?? "";
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
                    cat.SizeText = FormatSize(cat.SizeBytes);
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

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        _ => $"{bytes / 1024.0:F1} KB"
    };

    private void ShowActivationDialog()
    {
        if (IsActivated)
        {
            System.Windows.MessageBox.Show("您已激活本软件，无需再激活。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new Views.ActivationDialog();
            dialog.ShowDialog();

            if (dialog.ActivationSuccessful)
            {
                _ = CheckLicenseAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                        App.LogError("Activation-CheckLicense", t.Exception);
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"打开激活窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenWebsite()
    {
        try
        {
            var url = _settingsService.Get().WebsiteUrl;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { CleanMaster.App.LogError("OpenWebsite", ex); }
    }

    private void SaveWebsiteUrl()
    {
        try
        {
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
