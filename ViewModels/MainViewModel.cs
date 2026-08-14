using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CleanMaster.Models;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;


namespace CleanMaster.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DiskInfoService _diskInfoService;
    public ILangService Lang { get; }

    // Child ViewModels
    public CleanViewModel Clean { get; }
    public DiskFilesViewModel DiskFiles { get; }
    public SoftwareViewModel Software { get; }
    public StartupViewModel Startup { get; }
    public SystemCleanupViewModel SystemCleanup { get; }
    public SettingsViewModel Settings { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool _disposed;

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
    private string _previousView = "";
    private bool _isNavigating;

    public string CurrentView
    {
        get => _currentView;
        set
        {
            if (_currentView == value) return; // 避免重复导航
            _previousView = _currentView;
            _currentView = value;
            OnPropertyChanged();
            OnNavigatedTo(value);
        }
    }
    #endregion

    #region Commands
    public ICommand NavigateCommand => new RelayCommand<string>(view => CurrentView = view ?? "Clean");
    #endregion

    public MainViewModel(
        DiskInfoService diskInfoService,
        CleanViewModel cleanViewModel,
        DiskFilesViewModel diskFilesViewModel,
        SoftwareViewModel softwareViewModel,
        StartupViewModel startupViewModel,
        SystemCleanupViewModel systemCleanupViewModel,
        SettingsViewModel settingsViewModel,
        ILangService langService)
    {
        _diskInfoService = diskInfoService;

        Clean = cleanViewModel;
        DiskFiles = diskFilesViewModel;
        Software = softwareViewModel;
        Startup = startupViewModel;
        SystemCleanup = systemCleanupViewModel;
        Settings = settingsViewModel;
        Lang = langService;

        _diskInfoService.PropertyChanged += OnDiskInfoChanged;

        // When the language changes, re-fire PropertyChanged for our Lang property so
        // every {Binding Lang[Key]} across the main view re-evaluates immediately.
        Lang.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        // Trigger WPF to re-read all {Binding Lang[...]} expressions on this VM.
        try
        {
            App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(Lang));
            }));
        }
        catch
        {
            OnPropertyChanged(nameof(Lang));
        }
    }

    public void LoadDiskInfo()
    {
        try { _diskInfoService.Refresh("C:"); } catch (Exception ex) { CleanMaster.App.LogError("LoadDiskInfo", ex); }
    }

    private void OnNavigatedTo(string view)
    {
        // 防止用户连续点击导航时触发多次重负载加载。
        // 导航本身（CurrentView 切换、Visibility 切换）非常轻量、仍然即时响应；
        // 这里只对真正会触发 IO/扫描的副作用做去重入。
        // 注意: 不能 await — 那会阻塞 CurrentView setter, 导致页面切换卡顿、
        // 加载提示也来不及渲染。改为 fire-and-forget, 让 ViewModel 自己管理
        // IsLoading 状态和 UI 提示。
        if (_isNavigating) return;
        _isNavigating = true;
        try
        {
            switch (view)
            {
                case "LargeFiles":
                case "LargeFolders":
                    DiskFiles.LoadDiskDrives();
                    break;
                case "Software":
                    _ = Software.LoadInstalledSoftwareAsync().ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                            App.LogError("Nav-Software", t.Exception);
                    }, TaskContinuationOptions.OnlyOnFaulted);
                    break;
                case "Startup":
                    _ = Startup.LoadStartupItemsAsync(forceRefresh: false).ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                            App.LogError("Nav-Startup", t.Exception);
                    }, TaskContinuationOptions.OnlyOnFaulted);
                    break;
            }
        }
        finally
        {
            _isNavigating = false;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _diskInfoService.PropertyChanged -= OnDiskInfoChanged;
            Lang.LanguageChanged -= OnLanguageChanged;
            (Clean as IDisposable)?.Dispose();
            (SystemCleanup as IDisposable)?.Dispose();
            (Software as IDisposable)?.Dispose();
            (DiskFiles as IDisposable)?.Dispose();
            (Startup as IDisposable)?.Dispose();
            (Settings as IDisposable)?.Dispose();
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    private void OnDiskInfoChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiskInfoService.DiskInfo))
            DiskInfo = _diskInfoService.DiskInfo;
    }
}
