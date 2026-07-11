using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CleanMaster.Models;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class CleanViewModel : INotifyPropertyChanged
{
    private readonly IScanService _scanService;
    private readonly ICleanService _cleanService;
    private readonly DiskInfoService _diskInfoService;
    private CancellationTokenSource? _cts;

    public LangService Lang { get; } = LangService.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    #region Properties

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
        ? $"{LangService.Instance["Freed"]} {_lastCleanResult.FreedText} ({_lastCleanResult.FilesDeleted} {LangService.Instance["Files"]})"
        : "";

    private bool _isCleanProgressVisible;
    public bool IsCleanProgressVisible { get => _isCleanProgressVisible; set { _isCleanProgressVisible = value; OnPropertyChanged(); } }

    private string _cleanProgressText = "";
    public string CleanProgressText { get => _cleanProgressText; set { _cleanProgressText = value; OnPropertyChanged(); } }

    private string _cleanCurrentFile = "";
    public string CleanCurrentFile { get => _cleanCurrentFile; set { _cleanCurrentFile = value; OnPropertyChanged(); } }

    private double _cleanProgressPercent;
    public double CleanProgressPercent { get => _cleanProgressPercent; set { _cleanProgressPercent = value; OnPropertyChanged(); } }

    #endregion

    #region Commands

    public ICommand ScanCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand CancelCommand { get; }

    #endregion

    public CleanViewModel(IScanService scanService, ICleanService cleanService, DiskInfoService diskInfoService)
    {
        _scanService = scanService;
        _cleanService = cleanService;
        _diskInfoService = diskInfoService;

        ScanCommand = new RelayCommand(async () => await StartScanAsync());
        CleanCommand = new RelayCommand(async () => await StartCleanAsync());
        CancelCommand = new RelayCommand(() => _cts?.Cancel());

        StatusText = LangService.Instance["Ready"];

        _scanService.ProgressChanged += p => { StatusText = p.CurrentTask; ProgressPercent = p.ProgressPercent; };
        _scanService.CategoryScanned += cat =>
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() => { ScanResults.Add(cat); TotalCleanableSize += cat.TotalSize; TotalItemCount += cat.ItemCount; }));
        };
        _cleanService.ProgressChanged += msg => { App.Current.Dispatcher.BeginInvoke(new Action(() => StatusText = msg)); };
        _cleanService.ProgressUpdated += p =>
        {
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                CleanProgressText = $"{p.Current} / {p.Total}";
                CleanCurrentFile = p.CurrentFile;
                CleanProgressPercent = p.Percent;
            }));
        };
    }

    #region Scan/Clean

    private async Task StartScanAsync()
    {
        IsScanning = true;
        ScanResults.Clear();
        TotalCleanableSize = 0;
        TotalItemCount = 0;
        ProgressPercent = 0;
        StatusText = LangService.Instance["Scanning"];

        _cts = new CancellationTokenSource();
        try
        {
            await _scanService.ScanAllAsync(_cts.Token);
            StatusText = $"{LangService.Instance["ScanComplete"]}. {TotalItemCount} {LangService.Instance["Items"]} ({TotalCleanableText})";
        }
        catch (OperationCanceledException) { StatusText = LangService.Instance["Cancelled"]; App.Log("Scan cancelled by user"); }
        catch (Exception ex) { StatusText = ex.Message; App.LogError("StartScanAsync", ex); }
        finally { IsScanning = false; _diskInfoService.Refresh("C:"); }
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
        StatusText = LangService.Instance["Cleaning"];
        _cts = new CancellationTokenSource();
        try
        {
            LastCleanResult = await _cleanService.CleanAsync(toClean, _cts.Token);
            StatusText = $"{LangService.Instance["CleanComplete"]}. {CleanResultText}";
            ScanResults.Clear();
            TotalCleanableSize = 0;
            TotalItemCount = 0;
        }
        catch (OperationCanceledException) { StatusText = LangService.Instance["Cancelled"]; App.Log("Clean cancelled by user"); }
        catch (Exception ex) { StatusText = ex.Message; App.LogError("StartCleanAsync", ex); }
        finally { IsCleaning = false; IsCleanProgressVisible = false; _diskInfoService.Refresh("C:"); }
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
