using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CleanMaster.Models;
using CleanMaster.Services;


namespace CleanMaster.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly DiskInfoService _diskInfoService;
    public LangService Lang { get; } = LangService.Instance;

    // Child ViewModels
    public CleanViewModel Clean { get; }
    public DiskFilesViewModel DiskFiles { get; }
    public SoftwareViewModel Software { get; }
    public StartupViewModel Startup { get; }
    public SystemCleanupViewModel SystemCleanup { get; }
    public SettingsViewModel Settings { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

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
        SettingsViewModel settingsViewModel)
    {
        _diskInfoService = diskInfoService;

        Clean = cleanViewModel;
        DiskFiles = diskFilesViewModel;
        Software = softwareViewModel;
        Startup = startupViewModel;
        SystemCleanup = systemCleanupViewModel;
        Settings = settingsViewModel;

        _diskInfoService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DiskInfoService.DiskInfo))
                DiskInfo = _diskInfoService.DiskInfo;
        };
    }

    public void LoadDiskInfo()
    {
        try { _diskInfoService.Refresh("C:"); } catch (Exception ex) { CleanMaster.App.LogError("LoadDiskInfo", ex); }
    }

    private async void OnNavigatedTo(string view)
    {
        switch (view)
        {
            case "LargeFiles":
            case "LargeFolders":
            case "EmptyFolders":
                DiskFiles.LoadDiskDrives();
                break;
            case "Software":
                await Software.LoadInstalledSoftwareAsync();
                break;
            case "Startup":
                Startup.LoadStartupItems();
                break;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
