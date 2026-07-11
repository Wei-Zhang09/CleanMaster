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

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly ILicenseService _licenseService;
    private readonly IScanService _scanService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LangService Lang { get; } = LangService.Instance;

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

    public SettingsViewModel(ISettingsService settingsService, ILicenseService licenseService, IScanService scanService)
    {
        _settingsService = settingsService;
        _licenseService = licenseService;
        _scanService = scanService;
        WebsiteUrl = _settingsService.Get().WebsiteUrl;

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

                // Analyze different categories
                var categories = new List<DiskSpaceCategory>();

                // Windows folder
                var windowsSize = GetDirectorySize($@"{AnalyzedDrive}\Windows");
                categories.Add(new DiskSpaceCategory
                {
                    Name = "Windows 系统",
                    SizeBytes = windowsSize,
                    Color = "#3B82F6"
                });

                // Program Files
                var programFilesSize = GetDirectorySize($@"{AnalyzedDrive}\Program Files");
                categories.Add(new DiskSpaceCategory
                {
                    Name = "Program Files",
                    SizeBytes = programFilesSize,
                    Color = "#10B981"
                });

                // Program Files (x86)
                var programFilesX86Size = GetDirectorySize($@"{AnalyzedDrive}\Program Files (x86)");
                categories.Add(new DiskSpaceCategory
                {
                    Name = "Program Files (x86)",
                    SizeBytes = programFilesX86Size,
                    Color = "#F59E0B"
                });

                // Users folder
                var usersSize = GetDirectorySize($@"{AnalyzedDrive}\Users");
                categories.Add(new DiskSpaceCategory
                {
                    Name = "用户数据",
                    SizeBytes = usersSize,
                    Color = "#EF4444"
                });

                // Other
                var knownSize = windowsSize + programFilesSize + programFilesX86Size + usersSize;
                var otherSize = Math.Max(0, diskInfo.UsedBytes - knownSize);
                categories.Add(new DiskSpaceCategory
                {
                    Name = "其他文件",
                    SizeBytes = otherSize,
                    Color = "#8B5CF6"
                });

                // Calculate percentages
                foreach (var cat in categories)
                {
                    cat.Percentage = diskInfo.UsedBytes > 0 ? (double)cat.SizeBytes / diskInfo.UsedBytes * 100 : 0;
                    cat.SizeText = FormatSize(cat.SizeBytes);
                }

                // Sort by size descending
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
                _ = CheckLicenseAsync();
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

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
