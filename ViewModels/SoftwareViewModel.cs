using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class SoftwareViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISoftwareService _softwareService;

    public ILangService Lang { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<InstalledSoftware> InstalledSoftware { get; } = new();

    private ICollectionView? _softwareView;
    public ICollectionView SoftwareView
    {
        get
        {
            if (_softwareView == null)
            {
                _softwareView = CollectionViewSource.GetDefaultView(InstalledSoftware);
                _softwareView.Filter = FilterSoftware;
            }
            return _softwareView;
        }
    }

    private string _softwareSearchText = "";
    public string SoftwareSearchText
    {
        get => _softwareSearchText;
        set { _softwareSearchText = value; OnPropertyChanged(); SoftwareView?.Refresh(); }
    }

    private string _selectedSizeFilter = "全部";
    public string SelectedSizeFilter
    {
        get => _selectedSizeFilter;
        set { _selectedSizeFilter = value; OnPropertyChanged(); SoftwareView?.Refresh(); }
    }

    public ObservableCollection<string> SizeFilters { get; } = new()
    {
        "全部", "> 1 GB", "> 100 MB", "> 10 MB", "< 10 MB"
    };

    private bool FilterSoftware(object item)
    {
        if (item is not InstalledSoftware software) return false;

        if (!string.IsNullOrEmpty(SoftwareSearchText))
        {
            var search = SoftwareSearchText.ToLower();
            if (!software.Name.ToLower().Contains(search) &&
                !software.Publisher.ToLower().Contains(search) &&
                !software.Version.ToLower().Contains(search))
                return false;
        }

        if (SelectedSizeFilter != "全部")
        {
            return SelectedSizeFilter switch
            {
                "> 1 GB" => software.EstimatedSize >= 1_073_741_824,
                "> 100 MB" => software.EstimatedSize >= 104_857_600,
                "> 10 MB" => software.EstimatedSize >= 10_485_760,
                "< 10 MB" => software.EstimatedSize < 10_485_760,
                _ => true
            };
        }

        return true;
    }

    private InstalledSoftware? _selectedSoftware;
    public InstalledSoftware? SelectedSoftware { get => _selectedSoftware; set { _selectedSoftware = value; OnPropertyChanged(); } }

    private bool _isLoadingSoftware;
    public bool IsLoadingSoftware { get => _isLoadingSoftware; set { _isLoadingSoftware = value; OnPropertyChanged(); } }

    private bool _isUninstalling;
    public bool IsUninstalling { get => _isUninstalling; set { _isUninstalling = value; OnPropertyChanged(); } }

    private string _softwareStatus = "";
    public string SoftwareStatus { get => _softwareStatus; set { _softwareStatus = value; OnPropertyChanged(); } }

    public RelayCommand<InstalledSoftware> UninstallCommand { get; }

    public SoftwareViewModel(ISoftwareService softwareService, ILangService langService)
    {
        _softwareService = softwareService;
        Lang = langService;
        UninstallCommand = new RelayCommand<InstalledSoftware>(async (s) => await UninstallSoftwareAsync(s));

        // 订阅语言变更, 让 {Binding Lang[Key]} 在中英切换时立即刷新
        Lang.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        try
        {
            App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(Lang));
                OnPropertyChanged(nameof(SoftwareStatus));
            }));
        }
        catch
        {
            OnPropertyChanged(nameof(Lang));
            OnPropertyChanged(nameof(SoftwareStatus));
        }
    }

    public async Task LoadInstalledSoftwareAsync()
    {
        if (InstalledSoftware.Count > 0 || IsLoadingSoftware) return;
        IsLoadingSoftware = true;
        SoftwareStatus = Lang["SoftwareLoading"];

        try
        {
            var list = await Task.Run(() => _softwareService.GetInstalledSoftware());
            foreach (var s in list) InstalledSoftware.Add(s);
            SoftwareStatus = string.Format(Lang["ProgramsLoaded"], list.Count);
        }
        catch (Exception ex)
        {
            SoftwareStatus = Lang["SoftwareLoadFailed"];
            CleanMaster.App.LogError("LoadInstalledSoftware", ex);
        }
        finally
        {
            IsLoadingSoftware = false;
        }
    }

    /// <summary>
    /// Uninstall flow:
    /// 1. Show confirmation.
    /// 2. Launch uninstaller and wait for it to exit (or user-cancelled wait).
    /// 3. After exit, scan leftovers.
    /// 4. If leftovers found, ask user to clean them.
    /// </summary>
    private async Task UninstallSoftwareAsync(InstalledSoftware? software)
    {
        if (software == null || IsUninstalling) return;

        var confirmMsg = $"确定要卸载以下软件吗？\n\n" +
            $"名称：{software.Name}\n" +
            $"版本：{software.Version}\n" +
            $"发布者：{software.Publisher}\n" +
            $"大小：{software.SizeText}\n" +
            $"路径：{software.InstallLocation}\n\n" +
            $"提示：卸载完成后将自动扫描残留文件和注册表。";

        var confirm = System.Windows.MessageBox.Show(confirmMsg, "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsUninstalling = true;
        SoftwareStatus = $"正在卸载 {software.Name}...";

        try
        {
            // Use process-exit-aware overload instead of fixed 3s delay.
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var (started, exitCode, message) = await _softwareService.UninstallSoftwareAsync(software, cts.Token);

            if (!started)
            {
                System.Windows.MessageBox.Show($"卸载程序无法启动：{message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                SoftwareStatus = "卸载失败";
                return;
            }

            // Uninstaller exited (or wait cancelled). Brief delay to let file handles release.
            await Task.Delay(1500);

            var leftovers = _softwareService.ScanLeftovers(software);

            if (leftovers.LeftoverFolders.Count > 0 || leftovers.LeftoverRegistryKeys.Count > 0)
            {
                var leftoverMsg = $"卸载程序已结束。\n\n发现残留文件和注册表项：\n";
                foreach (var folder in leftovers.LeftoverFolders.Take(5))
                    leftoverMsg += $"  文件：{folder}\n";
                if (leftovers.LeftoverFolders.Count > 5)
                    leftoverMsg += $"  ...等 {leftovers.LeftoverFolders.Count} 个目录\n";
                foreach (var reg in leftovers.LeftoverRegistryKeys.Take(3))
                    leftoverMsg += $"  注册表：{reg}\n";

                leftoverMsg += $"\n残留大小：{leftovers.LeftoverSizeText}";
                leftoverMsg += "\n\n是否自动清理这些残留？（推荐清理）";

                var cleanupConfirm = System.Windows.MessageBox.Show(leftoverMsg, "清理残留", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (cleanupConfirm == MessageBoxResult.Yes)
                {
                    _softwareService.CleanupLeftovers(leftovers);
                    System.Windows.MessageBox.Show($"已清理 {leftovers.LeftoverSizeText} 残留文件和注册表", "清理完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("卸载完成，未发现残留文件。", "卸载完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            InstalledSoftware.Clear();
            SoftwareStatus = "";
            await LoadInstalledSoftwareAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"卸载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SoftwareStatus = "卸载失败";
        }
        finally
        {
            IsUninstalling = false;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        Lang.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
