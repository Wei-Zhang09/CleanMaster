using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CleanMaster.Models;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class CleanViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IScanService _scanService;
    private readonly ICleanService _cleanService;
    private readonly DiskInfoService _diskInfoService;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ILangService Lang { get; }

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
        ? $"{Lang["Freed"]} {_lastCleanResult.FreedText} ({_lastCleanResult.FilesDeleted} {Lang["Files"]})"
        : "";

    private bool _isCleanProgressVisible;
    public bool IsCleanProgressVisible { get => _isCleanProgressVisible; set { _isCleanProgressVisible = value; OnPropertyChanged(); } }

    private string _cleanProgressText = "";
    public string CleanProgressText { get => _cleanProgressText; set { _cleanProgressText = value; OnPropertyChanged(); } }

    private string _cleanCurrentFile = "";
    public string CleanCurrentFile { get => _cleanCurrentFile; set { _cleanCurrentFile = value; OnPropertyChanged(); } }

    private string _cleanCurrentPath = "";
    public string CleanCurrentPath { get => _cleanCurrentPath; set { _cleanCurrentPath = value; OnPropertyChanged(); } }

    private double _cleanProgressPercent;
    public double CleanProgressPercent { get => _cleanProgressPercent; set { _cleanProgressPercent = value; OnPropertyChanged(); } }

    #endregion

    #region Commands

    public ICommand ScanCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ToggleExpandCommand { get; }

    #endregion

    public CleanViewModel(IScanService scanService, ICleanService cleanService, DiskInfoService diskInfoService, ILangService langService)
    {
        _scanService = scanService;
        _cleanService = cleanService;
        _diskInfoService = diskInfoService;
        Lang = langService;

        ScanCommand = new RelayCommand(async () => await StartScanAsync());
        CleanCommand = new RelayCommand(async () => await StartCleanAsync());
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
        ToggleExpandCommand = new RelayCommand<ScanCategoryResult>(ToggleExpand);

        StatusText = Lang["Ready"];

        _scanService.ProgressChanged += OnScanProgressChanged;
        _scanService.CategoryScanned += OnCategoryScanned;
        _scanService.AccessDenied += OnAccessDenied;
        _cleanService.ProgressChanged += OnCleanProgressChanged;
        _cleanService.ProgressUpdated += OnCleanProgressUpdated;

        // 订阅全局语言变更: 切换语言后刷新本地 Lang 属性 + 派生文本 (如 CleanResultText)
        Lang.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        try
        {
            App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(Lang));
                OnPropertyChanged(nameof(CleanResultText));
                OnPropertyChanged(nameof(StatusText));
            }));
        }
        catch
        {
            OnPropertyChanged(nameof(Lang));
            OnPropertyChanged(nameof(CleanResultText));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void OnScanProgressChanged(ScanProgress p)
    {
        App.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            StatusText = p.CurrentTask;
            ProgressPercent = p.ProgressPercent;
        }));
    }

    private void OnCategoryScanned(ScanCategoryResult cat)
    {
        App.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            ScanResults.Add(cat);
            TotalCleanableSize += cat.TotalSize;
            TotalItemCount += cat.ItemCount;
        }));
    }

    private void OnAccessDenied(string message)
    {
        App.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            // Surface a non-blocking hint to the user (only once per scan to avoid spam)
            if (!_accessDeniedShown)
            {
                _accessDeniedShown = true;
                StatusText = "部分目录因权限不足被跳过，建议以管理员身份运行";
            }
            App.Log($"AccessDenied: {message}");
        }));
    }

    private void OnCleanProgressChanged(string msg)
    {
        App.Current.Dispatcher.BeginInvoke(new Action(() => StatusText = msg));
    }

    private void OnCleanProgressUpdated(CleanProgress p)
    {
        App.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            CleanProgressText = $"{p.Current} / {p.Total}";
            CleanCurrentFile = p.CurrentFile;
            CleanCurrentPath = p.CurrentPath;
            CleanProgressPercent = p.Percent;
        }));
    }

    private bool _accessDeniedShown;

    #region Scan/Clean

    private async Task StartScanAsync()
    {
        IsScanning = true;
        ScanResults.Clear();
        TotalCleanableSize = 0;
        TotalItemCount = 0;
        ProgressPercent = 0;
        _accessDeniedShown = false;
        StatusText = Lang["Scanning"];

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            await _scanService.ScanAllAsync(_cts.Token);
            StatusText = $"{Lang["ScanComplete"]}. {TotalItemCount} {Lang["Items"]} ({TotalCleanableText})";
        }
        catch (OperationCanceledException) { StatusText = Lang["Cancelled"]; App.Log("Scan cancelled by user"); }
        catch (Exception ex) { StatusText = ex.Message; App.LogError("StartScanAsync", ex); }
        finally { IsScanning = false; _diskInfoService.Refresh("C:"); }
    }

    private async Task StartCleanAsync()
    {
        var toClean = ScanResults.Where(c => c.IsSelected && c.Items.Any(i => i.IsSelected)).ToList();
        if (toClean.Count == 0) return;

        // Preview confirmation
        var totalSize = toClean.Sum(c => c.TotalSize);
        var totalItems = toClean.Sum(c => c.Items.Count(i => i.IsSelected));

        // 检测是否选中了危险项 — 危险项需要更强的警告
        var dangerousItems = toClean
            .SelectMany(c => c.Items.Where(i => i.IsSelected && i.IsDangerous))
            .ToList();

        var previewMsg = "即将清理以下内容：\n\n";
        foreach (var cat in toClean.Take(10))
        {
            previewMsg += $"• {cat.DisplayName}: {cat.ItemCount} 项 ({cat.TotalSizeText})\n";
        }
        if (toClean.Count > 10)
            previewMsg += $"... 等 {toClean.Count} 个分类\n";

        previewMsg += $"\n总计: {totalItems} 项，约 {FormatSize(totalSize)}";

        if (dangerousItems.Count > 0)
        {
            previewMsg += "\n\n⚠️ 警告：您选中了危险项，删除可能影响系统功能：\n";
            foreach (var item in dangerousItems.Take(5))
                previewMsg += $"  - {item.Name}\n";
            previewMsg += "\n请确认您了解后果后再继续！";
        }

        previewMsg += "\n\n是否继续清理？";

        var confirm = System.Windows.MessageBox.Show(
            previewMsg,
            dangerousItems.Count > 0 ? "危险操作确认" : "清理确认",
            MessageBoxButton.YesNo,
            dangerousItems.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsCleaning = true;
        IsCleanProgressVisible = true;
        CleanProgressPercent = 0;
        CleanProgressText = "";
        CleanCurrentFile = "";
        CleanCurrentPath = "";
        StatusText = Lang["Cleaning"];
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            LastCleanResult = await _cleanService.CleanAsync(toClean, _cts.Token);
            StatusText = $"{Lang["CleanComplete"]}. {CleanResultText}";
            ScanResults.Clear();
            TotalCleanableSize = 0;
            TotalItemCount = 0;
        }
        catch (OperationCanceledException) { StatusText = Lang["Cancelled"]; App.Log("Clean cancelled by user"); }
        catch (Exception ex) { StatusText = ex.Message; App.LogError("StartCleanAsync", ex); }
        finally { IsCleaning = false; IsCleanProgressVisible = false; _diskInfoService.Refresh("C:"); }
    }

    #endregion

    private void ToggleExpand(ScanCategoryResult? category)
    {
        if (category == null) return;
        category.IsExpanded = !category.IsExpanded;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        _ => $"{bytes / 1024.0:F1} KB"
    };

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _scanService.ProgressChanged -= OnScanProgressChanged;
            _scanService.CategoryScanned -= OnCategoryScanned;
            _scanService.AccessDenied -= OnAccessDenied;
            _cleanService.ProgressChanged -= OnCleanProgressChanged;
            _cleanService.ProgressUpdated -= OnCleanProgressUpdated;
            Lang.LanguageChanged -= OnLanguageChanged;
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}
