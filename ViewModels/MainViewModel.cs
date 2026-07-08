using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CleanMaster.Models;
using CleanMaster.Services;

namespace CleanMaster.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ScanService _scanService = new();
    private readonly CleanService _cleanService = new();
    private readonly SoftwareService _softwareService = new();
    private readonly LicenseService _licenseService = new();
    private readonly FolderScanService _folderScanService = new();
    private readonly SystemCleanupService _systemCleanupService = new();
    public LangService Lang { get; } = LangService.Instance;
    private CancellationTokenSource? _cts;

    public event PropertyChangedEventHandler? PropertyChanged;

    #region License
    private bool _isActivated;
    public bool IsActivated { get => _isActivated; set { _isActivated = value; OnPropertyChanged(); OnPropertyChanged(nameof(LicenseStatusText)); OnPropertyChanged(nameof(LicenseStatusColor)); } }
    private string _licenseStatusText = "";
    public string LicenseStatusText { get => _licenseStatusText; set { _licenseStatusText = value; OnPropertyChanged(); } }
    public string LicenseStatusColor => IsActivated ? "#10B981" : "#F59E0B";
    private string _activatedProduct = "";
    public string ActivatedProduct { get => _activatedProduct; set { _activatedProduct = value; OnPropertyChanged(); } }
    #endregion

    #region Disk Info
    private DiskInfo? _diskInfo;
    public DiskInfo? DiskInfo
    {
        get => _diskInfo;
        set { _diskInfo = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiskUsageText)); OnPropertyChanged(nameof(DiskFreeText)); OnPropertyChanged(nameof(DiskUsedPercent)); }
    }
    public string DiskUsageText => _diskInfo != null ? $"{_diskInfo.UsedText} / {_diskInfo.TotalText}" : "--";
    public string DiskFreeText => _diskInfo != null ? $"{_diskInfo.FreeText}" : "--";
    public double DiskUsedPercent => _diskInfo?.UsedPercent ?? 0;
    #endregion

    #region Navigation
    private string _currentView = "Clean";
    public string CurrentView
    {
        get => _currentView;
        set
        {
            _currentView = value;
            OnPropertyChanged();
            OnNavigatedTo(value);
        }
    }
    #endregion

    #region Scan/Clean
    private bool _isScanning;
    public bool IsScanning { get => _isScanning; set { _isScanning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanScan)); OnPropertyChanged(nameof(CanClean)); } }
    private bool _isCleaning;
    public bool IsCleaning { get => _isCleaning; set { _isCleaning = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanClean)); } }
    public bool CanScan => !IsScanning && !IsCleaning;
    public bool CanClean => !IsScanning && !IsCleaning && ScanResults.Count > 0;

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    private double _progressPercent;
    public double ProgressPercent { get => _progressPercent; set { _progressPercent = value; OnPropertyChanged(); } }

    public ObservableCollection<ScanCategoryResult> ScanResults { get; } = new();
    private long _totalCleanableSize;
    public long TotalCleanableSize { get => _totalCleanableSize; set { _totalCleanableSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalCleanableText)); } }
    public string TotalCleanableText => FormatSize(TotalCleanableSize);
    private int _totalItemCount;
    public int TotalItemCount { get => _totalItemCount; set { _totalItemCount = value; OnPropertyChanged(); } }
    private CleanResult? _lastCleanResult;
    public CleanResult? LastCleanResult { get => _lastCleanResult; set { _lastCleanResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(CleanResultText)); } }
    public string CleanResultText => _lastCleanResult != null
        ? $"{Lang["Freed"]} {_lastCleanResult.FreedText} ({_lastCleanResult.FilesDeleted} {Lang["Files"]})" : "";
    #endregion

    #region Large Files
    public ObservableCollection<LargeFileItem> LargeFiles { get; } = new();
    private bool _isFindingLargeFiles;
    public bool IsFindingLargeFiles { get => _isFindingLargeFiles; set { _isFindingLargeFiles = value; OnPropertyChanged(); } }
    private string _largeFileStatus = "";
    public string LargeFileStatus { get => _largeFileStatus; set { _largeFileStatus = value; OnPropertyChanged(); } }
    private long _minFileSizeBytes = 100 * 1024 * 1024;
    public long MinFileSizeBytes { get => _minFileSizeBytes; set { _minFileSizeBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(MinFileSizeText)); OnPropertyChanged(nameof(MinFileSizeInput)); } }
    public string MinFileSizeText => $"{MinFileSizeBytes / 1_048_576} MB";
    public string MinFileSizeInput
    {
      get => $"{MinFileSizeBytes / 1_048_576}";
      set
      {
        if (long.TryParse(value, out var mb) && mb > 0)
          MinFileSizeBytes = mb * 1024 * 1024;
      }
    }
    public ObservableCollection<string> DiskDrives { get; } = new();
    private string _selectedDrive = "C:";
    public string SelectedDrive { get => _selectedDrive; set { _selectedDrive = value; OnPropertyChanged(); } }
    #endregion

    #region Large Folders
    public ObservableCollection<LargeFolderItem> LargeFolders { get; } = new();
    private bool _isScanningFolders;
    public bool IsScanningFolders { get => _isScanningFolders; set { _isScanningFolders = value; OnPropertyChanged(); } }
    private string _folderScanStatus = "";
    public string FolderScanStatus { get => _folderScanStatus; set { _folderScanStatus = value; OnPropertyChanged(); } }
    #endregion

    #region Empty Folders
    public ObservableCollection<EmptyFolderItem> EmptyFolders { get; } = new();
    private bool _isScanningEmpty;
    public bool IsScanningEmpty { get => _isScanningEmpty; set { _isScanningEmpty = value; OnPropertyChanged(); } }
    private string _emptyFolderStatus = "";
    public string EmptyFolderStatus { get => _emptyFolderStatus; set { _emptyFolderStatus = value; OnPropertyChanged(); } }
    #endregion

    #region System Cleanup
    private bool _isRunningCleanup;
    public bool IsRunningCleanup { get => _isRunningCleanup; set { _isRunningCleanup = value; OnPropertyChanged(); } }
    private string _cleanupStatus = "";
    public string CleanupStatus { get => _cleanupStatus; set { _cleanupStatus = value; OnPropertyChanged(); } }
    #endregion

    #region Software
    public ObservableCollection<InstalledSoftware> InstalledSoftware { get; } = new();
    private InstalledSoftware? _selectedSoftware;
    public InstalledSoftware? SelectedSoftware { get => _selectedSoftware; set { _selectedSoftware = value; OnPropertyChanged(); } }
    #endregion

    #region Startup
    public ObservableCollection<StartupItem> StartupItems { get; } = new();
    #endregion

    #region Settings
    public bool IsChinese { get => Lang.IsChinese; set { Lang.IsChinese = value; OnPropertyChanged(); OnPropertyChanged(nameof(Lang)); } }
    #endregion

    #region Commands
    public ICommand ScanCommand => new RelayCommand(async () => await StartScanAsync());
    public ICommand CleanCommand => new RelayCommand(async () => await StartCleanAsync());
    public ICommand CancelCommand => new RelayCommand(() => _cts?.Cancel());
    public ICommand NavigateCommand => new RelayCommand<string>(view => CurrentView = view ?? "Clean");
    public ICommand FindLargeFilesCommand => new RelayCommand(async () => await FindLargeFilesAsync());
    public ICommand DeleteLargeFilesCommand => new RelayCommand(async () => await DeleteLargeFilesAsync());
    public ICommand FindLargeFoldersCommand => new RelayCommand(async () => await FindLargeFoldersAsync());
    public ICommand FindEmptyFoldersCommand => new RelayCommand(async () => await FindEmptyFoldersAsync());
    public ICommand DeleteEmptyFoldersCommand => new RelayCommand(async () => await DeleteEmptyFoldersAsync());
    public ICommand RunDismCleanupCommand => new RelayCommand(async () => await RunDismCleanupAsync());
    public ICommand RunSfcScanCommand => new RelayCommand(async () => await RunSfcScanAsync());
    public ICommand FlushDnsCommand => new RelayCommand(async () => await FlushDnsAsync());
    public ICommand UninstallCommand => new RelayCommand<InstalledSoftware>(UninstallSoftware);
    public ICommand DisableStartupCommand => new RelayCommand<StartupItem>(DisableStartupItem);
    public ICommand ToggleLangCommand => new RelayCommand(() => { IsChinese = !IsChinese; OnPropertyChanged(nameof(Lang)); });
    public ICommand ShowActivationCommand => new RelayCommand(ShowActivationDialog);
    public ICommand OpenWebsiteCommand => new RelayCommand(OpenWebsite);
    public ICommand SaveWebsiteUrlCommand => new RelayCommand(SaveWebsiteUrl);
    #endregion

    private string _websiteUrl = "";
    public string WebsiteUrl { get => _websiteUrl; set { _websiteUrl = value; OnPropertyChanged(); } }

    private void OpenWebsite()
    {
        try
        {
            var url = SettingsService.Get().WebsiteUrl;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void SaveWebsiteUrl()
    {
        try
        {
            var settings = SettingsService.Get();
            settings.WebsiteUrl = WebsiteUrl;
            SettingsService.Save(settings);
            System.Windows.MessageBox.Show("网站地址已保存", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch { }
    }

    #region Clean Progress
    private bool _isCleanProgressVisible;
    public bool IsCleanProgressVisible { get => _isCleanProgressVisible; set { _isCleanProgressVisible = value; OnPropertyChanged(); } }
    private string _cleanProgressText = "";
    public string CleanProgressText { get => _cleanProgressText; set { _cleanProgressText = value; OnPropertyChanged(); } }
    private string _cleanCurrentFile = "";
    public string CleanCurrentFile { get => _cleanCurrentFile; set { _cleanCurrentFile = value; OnPropertyChanged(); } }
    private double _cleanProgressPercent;
    public double CleanProgressPercent { get => _cleanProgressPercent; set { _cleanProgressPercent = value; OnPropertyChanged(); } }
    #endregion

    public MainViewModel()
    {
        StatusText = Lang["Ready"];
        _scanService.ProgressChanged += p => { StatusText = p.CurrentTask; ProgressPercent = p.ProgressPercent; };
        _scanService.CategoryScanned += cat =>
        {
            App.Current.Dispatcher.Invoke(() => { ScanResults.Add(cat); TotalCleanableSize += cat.TotalSize; TotalItemCount += cat.ItemCount; });
        };
        _cleanService.ProgressChanged += msg => { App.Current.Dispatcher.Invoke(() => StatusText = msg); };
        _cleanService.ProgressUpdated += p =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                CleanProgressText = $"{p.Current} / {p.Total}";
                CleanCurrentFile = p.CurrentFile;
                CleanProgressPercent = p.Percent;
            });
        };

        // Load settings
        WebsiteUrl = SettingsService.Get().WebsiteUrl;

        // Sync settings from server
        _ = SyncSettingsAsync();

        // License check disabled (code preserved for future use)
        // _ = CheckLicenseAsync();
        IsActivated = true;
        LicenseStatusText = "";
    }

    private async Task SyncSettingsAsync()
    {
        try
        {
            await SettingsService.SyncFromServerAsync();
            WebsiteUrl = SettingsService.Get().WebsiteUrl;
        }
        catch { }
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

    private void ShowActivationDialog()
    {
        if (IsActivated)
        {
            MessageBox.Show("您已激活本软件，无需再激活。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show($"打开激活窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void LoadDiskInfo()
    {
        try { DiskInfo = _scanService.GetDiskInfo("C:"); } catch { }
    }

    private void OnNavigatedTo(string view)
    {
        // Load disk drives for all disk-related views
        if (view is "LargeFiles" or "LargeFolders" or "EmptyFolders")
            LoadDiskDrives();

        switch (view)
        {
            case "LargeFiles": LoadLargeFiles(); break;
            case "Software": LoadInstalledSoftware(); break;
            case "Startup": LoadStartupItems(); break;
        }
    }

    private void LoadDiskDrives()
    {
        if (DiskDrives.Count == 0)
        {
            foreach (var disk in _scanService.GetAllDisks())
                DiskDrives.Add(disk.DriveLetter);
            if (DiskDrives.Count > 0) SelectedDrive = DiskDrives[0];
        }
    }

    private void LoadLargeFiles()
    {
        if (LargeFiles.Count > 0 || IsFindingLargeFiles) return;
        LargeFileStatus = Lang["Ready"];
    }

    private bool _isLoadingSoftware;
    public bool IsLoadingSoftware { get => _isLoadingSoftware; set { _isLoadingSoftware = value; OnPropertyChanged(); } }
    private string _softwareStatus = "";
    public string SoftwareStatus { get => _softwareStatus; set { _softwareStatus = value; OnPropertyChanged(); } }

    private async void LoadInstalledSoftware()
    {
        if (InstalledSoftware.Count > 0 || IsLoadingSoftware) return;
        IsLoadingSoftware = true;
        SoftwareStatus = "正在扫描已安装软件...";

        try
        {
            var list = await Task.Run(() => _softwareService.GetInstalledSoftware());
            foreach (var s in list) InstalledSoftware.Add(s);
            SoftwareStatus = $"共 {list.Count} 个软件";
        }
        catch
        {
            SoftwareStatus = "扫描失败";
        }
        finally
        {
            IsLoadingSoftware = false;
        }
    }

    private async void LoadStartupItems()
    {
        if (StartupItems.Count > 0) return;
        try
        {
            var list = await Task.Run(() => _softwareService.GetStartupItems());
            foreach (var item in list) StartupItems.Add(item);
        }
        catch { }
    }

    #region Scan/Clean
    private async Task StartScanAsync()
    {
        IsScanning = true;
        ScanResults.Clear();
        TotalCleanableSize = 0;
        TotalItemCount = 0;
        ProgressPercent = 0;
        StatusText = Lang["Scanning"];
        CurrentView = "Clean";

        _cts = new CancellationTokenSource();
        try
        {
            await _scanService.ScanAllAsync(_cts.Token);
            StatusText = $"{Lang["ScanComplete"]}. {TotalItemCount} {Lang["Items"]} ({TotalCleanableText})";
        }
        catch (OperationCanceledException) { StatusText = Lang["Cancelled"]; }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { IsScanning = false; LoadDiskInfo(); }
    }

    private async Task StartCleanAsync()
    {
        var toClean = ScanResults.Where(c => c.IsSelected && c.Items.Any(i => i.IsSelected)).ToList();
        if (toClean.Count == 0) return;

        IsCleaning = true;
        IsCleanProgressVisible = true;
        CleanProgressPercent = 0;
        CleanProgressText = "";
        CleanCurrentFile = "";
        StatusText = Lang["Cleaning"];
        _cts = new CancellationTokenSource();
        try
        {
            LastCleanResult = await _cleanService.CleanAsync(toClean, _cts.Token);
            StatusText = $"{Lang["CleanComplete"]}. {CleanResultText}";
            ScanResults.Clear();
            TotalCleanableSize = 0;
            TotalItemCount = 0;
        }
        catch (OperationCanceledException) { StatusText = Lang["Cancelled"]; }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { IsCleaning = false; IsCleanProgressVisible = false; LoadDiskInfo(); }
    }
    #endregion

    #region Large Files
    private async Task FindLargeFilesAsync()
    {
        IsFindingLargeFiles = true;
        LargeFiles.Clear();
        LargeFileStatus = Lang["Searching"];

        _cts = new CancellationTokenSource();
        try
        {
            var files = await _scanService.FindLargeFilesAsync(SelectedDrive + @"\", MinFileSizeBytes, _cts.Token);
            foreach (var f in files) LargeFiles.Add(f);
            LargeFileStatus = $"{Lang["Found"]} {files.Count} {Lang["FilesLargerThan"]} {MinFileSizeText}";
        }
        catch (OperationCanceledException) { LargeFileStatus = Lang["Cancelled"]; }
        catch (Exception ex) { LargeFileStatus = ex.Message; }
        finally { IsFindingLargeFiles = false; }
    }

    private async Task DeleteLargeFilesAsync()
    {
        var selected = LargeFiles.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        var totalSize = selected.Sum(f => f.SizeBytes);
        var sizeText = totalSize switch
        {
            >= 1_073_741_824 => $"{totalSize / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{totalSize / 1_048_576.0:F1} MB",
            _ => $"{totalSize / 1024.0:F1} KB"
        };

        var confirm = MessageBox.Show(
            $"确定要删除选中的 {selected.Count} 个文件吗？\n\n将释放 {sizeText} 空间\n\n删除后无法恢复！",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        _cts = new CancellationTokenSource();
        try
        {
            var result = await _cleanService.CleanLargeFilesAsync(selected, _cts.Token);
            LargeFileStatus = $"{Lang["Deleted"]} {result.FilesDeleted} {Lang["Files"]}, {Lang["Freed"]} {result.FreedText}";
            foreach (var f in selected) LargeFiles.Remove(f);
            LoadDiskInfo();
        }
        catch (Exception ex) { LargeFileStatus = ex.Message; }
    }
    #endregion

    #region Software
    private async void UninstallSoftware(InstalledSoftware? software)
    {
        if (software == null) return;

        var confirmMsg = $"确定要卸载以下软件吗？\n\n" +
            $"名称：{software.Name}\n" +
            $"版本：{software.Version}\n" +
            $"发布者：{software.Publisher}\n" +
            $"大小：{software.SizeText}\n" +
            $"路径：{software.InstallLocation}\n\n" +
            $"提示：卸载后将自动清理残留文件和注册表。";

        var confirm = MessageBox.Show(confirmMsg, "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _softwareService.UninstallSoftware(software);
            await Task.Delay(3000);

            var leftovers = _softwareService.ScanLeftovers(software);

            if (leftovers.LeftoverFolders.Count > 0 || leftovers.LeftoverRegistryKeys.Count > 0)
            {
                var leftoverMsg = $"卸载程序已启动。\n\n发现残留文件和注册表项：\n";
                foreach (var folder in leftovers.LeftoverFolders.Take(3))
                    leftoverMsg += $"  文件：{folder}\n";
                foreach (var reg in leftovers.LeftoverRegistryKeys.Take(3))
                    leftoverMsg += $"  注册表：{reg}\n";

                leftoverMsg += $"\n残留大小：{leftovers.LeftoverSizeText}";
                leftoverMsg += $"\n\n是否自动清理这些残留？（推荐清理）";

                var cleanupConfirm = MessageBox.Show(leftoverMsg, "清理残留", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (cleanupConfirm == MessageBoxResult.Yes)
                {
                    _softwareService.CleanupLeftovers(leftovers);
                    MessageBox.Show($"已清理 {leftovers.LeftoverSizeText} 残留文件和注册表", "清理完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("卸载完成，未发现残留文件。", "卸载完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            InstalledSoftware.Clear();
            SoftwareStatus = "";
            LoadInstalledSoftware();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"卸载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    #endregion

    #region Startup
    private void DisableStartupItem(StartupItem? item)
    {
        if (item == null) return;
        if (_softwareService.DisableStartupItem(item))
            StartupItems.Remove(item);
    }
    #endregion

    #region Large Folders
    private async Task FindLargeFoldersAsync()
    {
        IsScanningFolders = true;
        LargeFolders.Clear();
        FolderScanStatus = "正在扫描文件夹...";
        _cts = new CancellationTokenSource();
        try
        {
            var folders = await _folderScanService.ScanLargeFoldersAsync(SelectedDrive + @"\", 500 * 1024 * 1024, _cts.Token);
            foreach (var f in folders) LargeFolders.Add(f);
            FolderScanStatus = $"扫描完成，发现 {folders.Count} 个大文件夹";
        }
        catch (OperationCanceledException) { FolderScanStatus = "已取消"; }
        catch (Exception ex) { FolderScanStatus = ex.Message; }
        finally { IsScanningFolders = false; }
    }
    #endregion

    #region Empty Folders
    private async Task FindEmptyFoldersAsync()
    {
        IsScanningEmpty = true;
        EmptyFolders.Clear();
        EmptyFolderStatus = "正在扫描空文件夹...";
        _cts = new CancellationTokenSource();
        try
        {
            var folders = await _folderScanService.ScanEmptyFoldersAsync(SelectedDrive + @"\", _cts.Token);
            foreach (var f in folders) EmptyFolders.Add(f);
            EmptyFolderStatus = $"扫描完成，发现 {folders.Count} 个空文件夹";
        }
        catch (OperationCanceledException) { EmptyFolderStatus = "已取消"; }
        catch (Exception ex) { EmptyFolderStatus = ex.Message; }
        finally { IsScanningEmpty = false; }
    }

    private async Task DeleteEmptyFoldersAsync()
    {
        var selected = EmptyFolders.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show($"确定删除 {selected.Count} 个空文件夹？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        await Task.Run(() =>
        {
            var deleted = _folderScanService.DeleteEmptyFolders(selected);
            App.Current.Dispatcher.Invoke(() =>
            {
                foreach (var f in selected) EmptyFolders.Remove(f);
                EmptyFolderStatus = $"已删除 {deleted} 个空文件夹";
            });
        });
    }
    #endregion

    #region System Cleanup
    private async Task RunDismCleanupAsync()
    {
        var confirm = MessageBox.Show(
            "Windows 组件清理将删除旧版本的系统更新文件。\n\n此操作安全但可能需要几分钟时间。\n\n是否继续？",
            "系统组件清理", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsRunningCleanup = true;
        CleanupStatus = "正在执行 Windows 组件清理，这可能需要几分钟...";
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _systemCleanupService.RunDismCleanupAsync(_cts.Token);
            CleanupStatus = result.Success ? $"清理完成: {result.Message}" : $"清理失败: {result.Message}";
            if (result.Success) MessageBox.Show("系统组件清理完成！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { CleanupStatus = ex.Message; }
        finally { IsRunningCleanup = false; LoadDiskInfo(); }
    }

    private async Task RunSfcScanAsync()
    {
        var confirm = MessageBox.Show(
            "系统文件扫描将检查并修复损坏的系统文件。\n\n此操作安全但可能需要较长时间。\n\n是否继续？",
            "系统文件修复", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsRunningCleanup = true;
        CleanupStatus = "正在扫描系统文件，这可能需要较长时间...";
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _systemCleanupService.RunSfcScanAsync(_cts.Token);
            CleanupStatus = result.Success ? $"扫描完成: {result.Message}" : $"扫描失败: {result.Message}";
        }
        catch (Exception ex) { CleanupStatus = ex.Message; }
        finally { IsRunningCleanup = false; }
    }

    private async Task FlushDnsAsync()
    {
        var confirm = MessageBox.Show(
            "清理 DNS 缓存将重置域名解析记录。\n\n此操作安全，不影响其他设置。\n\n是否继续？",
            "DNS 缓存清理", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsRunningCleanup = true;
        CleanupStatus = "正在清理 DNS 缓存...";
        try
        {
            var result = await _systemCleanupService.FlushDnsCacheAsync();
            CleanupStatus = result.Success ? "DNS 缓存已清理" : $"清理失败: {result.Message}";
        }
        catch (Exception ex) { CleanupStatus = ex.Message; }
        finally { IsRunningCleanup = false; }
    }
    #endregion

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        _ => $"{bytes / 1024.0:F1} KB"
    };

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    public RelayCommand(Action execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    public RelayCommand(Action<T?> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute((T?)parameter);
}
