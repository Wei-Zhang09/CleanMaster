using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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

        var confirm = CleanMaster.Views.AppDialog.Show(confirmMsg, "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsUninstalling = true;
        SoftwareStatus = $"正在卸载 {software.Name}...";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var attempt = await _softwareService.UninstallSoftwareAsync(software, cts.Token);

            if (attempt.Outcome == UninstallOutcome.NotStarted)
            {
                CleanMaster.Views.AppDialog.Show($"卸载程序无法启动：{attempt.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                SoftwareStatus = "卸载失败";
                return;
            }

            if (attempt.Outcome == UninstallOutcome.Cancelled)
            {
                CleanMaster.Views.AppDialog.Show(
                    "卸载已被取消或等待超时。\n\n软件可能处于「部分卸载」状态，建议重新卸载一次。",
                    "卸载已取消", MessageBoxButton.OK, MessageBoxImage.Warning);
                SoftwareStatus = "卸载已取消";
                return;
            }

            // 卸载器还在跑子进程时不要扫残留：会把"正在被删除"的文件当成残留，用户一清理就和卸载器抢文件。
            if (attempt.DescendantsStillRunning)
            {
                CleanMaster.Views.AppDialog.Show(
                    "卸载程序已经退出，但它启动的后台进程仍在运行。\n\n请等一两分钟后再刷新软件列表确认；" +
                    "如果软件仍然存在，重启电脑后重新卸载一次。",
                    "卸载仍在进行", MessageBoxButton.OK, MessageBoxImage.Warning);
                SoftwareStatus = "卸载仍在进行";
                await RefreshSoftwareListAsync();
                return;
            }

            switch (attempt.Outcome)
            {
                case UninstallOutcome.VerifiedRemoved:
                    await HandleVerifiedRemovalAsync(software);
                    break;

                case UninstallOutcome.RestartRequired:
                    CleanMaster.Views.AppDialog.Show(
                        $"「{software.Name}」需要重启电脑才能完成卸载。\n\n" +
                        "重启后请回到软件管理页刷新，确认它是否已经消失。",
                        "需要重启", MessageBoxButton.OK, MessageBoxImage.Information);
                    SoftwareStatus = "等待重启完成卸载";
                    break;

                case UninstallOutcome.StillRegistered:
                    // 不扫残留、不清残留：软件还登记在系统里，清文件等于破坏已装软件。
                    var openSettings = CleanMaster.Views.AppDialog.Show(
                        $"卸载程序已经结束，但系统里仍然登记着「{software.Name}」。\n\n" +
                        "常见原因：\n" +
                        "  • 卸载被取消，或弹窗被直接关掉\n" +
                        "  • 卸载器被权限/UAC 拦截\n" +
                        "  • 卸载器只删了文件，没清注册信息\n\n" +
                        "是否打开 Windows 设置里的「应用」列表，用系统自带卸载再试一次？",
                        "卸载未完成", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (openSettings == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            CleanMaster.App.LogError("OpenAppsSettings", ex);
                        }
                    }
                    SoftwareStatus = "卸载未完成";
                    break;

                default: // Unverified
                    CleanMaster.Views.AppDialog.Show(
                        $"卸载程序已经结束，但无法确认「{software.Name}」是否真的被移除（缺少注册表登记路径）。\n\n" +
                        "请刷新列表确认；如果软件还在，重启后再卸载一次。",
                        "卸载结果未知", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SoftwareStatus = "卸载结果未知";
                    break;
            }

            // 刷新列表会把状态栏改成"已加载 N 个软件"，卸载结论会被冲掉。
            // 先把结论存下来，刷新完再写回，用户才看得到刚才到底卸没卸掉。
            var outcomeStatus = SoftwareStatus;

            InstalledSoftware.Clear();
            SoftwareStatus = "";
            await LoadInstalledSoftwareAsync();

            if (!string.IsNullOrEmpty(outcomeStatus)) SoftwareStatus = outcomeStatus;
        }
        catch (Exception ex)
        {
            CleanMaster.Views.AppDialog.Show($"卸载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SoftwareStatus = "卸载失败";
        }
        finally
        {
            IsUninstalling = false;
        }
    }

    /// <summary>确认卸载后：扫残留 → 询问清理 → 刷新列表。</summary>
    private async Task HandleVerifiedRemovalAsync(InstalledSoftware software)
    {
        // 给文件句柄一点释放时间（卸载器刚退出时仍可能有句柄未关闭）
        await Task.Delay(800);

        var leftovers = _softwareService.ScanLeftovers(software);

        if (leftovers.LeftoverFolders.Count == 0 && leftovers.LeftoverRegistryKeys.Count == 0)
        {
            CleanMaster.Views.AppDialog.Show($"「{software.Name}」已卸载完成，未发现残留文件。",
                "卸载完成", MessageBoxButton.OK, MessageBoxImage.Information);
            SoftwareStatus = "卸载完成";
            return;
        }

        var leftoverMsg = $"「{software.Name}」已卸载完成。\n\n发现残留文件和注册表项：\n";
        foreach (var folder in leftovers.LeftoverFolders.Take(5))
            leftoverMsg += $"  文件：{folder}\n";
        if (leftovers.LeftoverFolders.Count > 5)
            leftoverMsg += $"  ...等 {leftovers.LeftoverFolders.Count} 个目录\n";
        foreach (var reg in leftovers.LeftoverRegistryKeys.Take(3))
            leftoverMsg += $"  注册表：{reg}\n";

        leftoverMsg += $"\n残留大小：{leftovers.LeftoverSizeText}";
        leftoverMsg += "\n\n是否自动清理这些残留？（推荐清理）";

        var cleanupConfirm = CleanMaster.Views.AppDialog.Show(leftoverMsg, "清理残留", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (cleanupConfirm == MessageBoxResult.Yes)
        {
            _softwareService.CleanupLeftovers(leftovers);
            CleanMaster.Views.AppDialog.Show($"已清理 {leftovers.LeftoverSizeText} 残留文件和注册表",
                "清理完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        SoftwareStatus = "卸载完成";
    }

    /// <summary>卸载流程中途需要刷新列表时复用（清空后重载）。</summary>
    private async Task RefreshSoftwareListAsync()
    {
        InstalledSoftware.Clear();
        SoftwareStatus = "";
        await LoadInstalledSoftwareAsync();
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        Lang.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
