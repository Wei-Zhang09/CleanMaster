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
    private readonly DuplicateService _duplicateService;
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

    public string TotalCleanableText => ByteSizeFormatter.Format(TotalCleanableSize);

    /// <summary>
    /// 当前勾选（分类勾选且项勾选）的文件/目录总大小。勾选变化时实时刷新，
    /// 让用户清楚这次清理能释放多少空间。
    /// </summary>
    public long SelectedCleanableSize =>
        ScanResults.Where(c => c.IsSelected)
                   .Sum(c => c.Items.Where(i => i.IsSelected).Sum(i => i.SizeBytes));

    public string SelectedCleanableText => ByteSizeFormatter.Format(SelectedCleanableSize);

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

    public CleanViewModel(IScanService scanService, ICleanService cleanService, DiskInfoService diskInfoService, DuplicateService duplicateService, ILangService langService)
    {
        _scanService = scanService;
        _cleanService = cleanService;
        _diskInfoService = diskInfoService;
        _duplicateService = duplicateService;
        Lang = langService;

        ScanCommand = new RelayCommand(async () => await StartScanAsync());
        CleanCommand = new RelayCommand(async () => await StartCleanAsync());
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
        ToggleExpandCommand = new RelayCommand<ScanCategoryResult>(ToggleExpand);

        StatusText = Lang["Ready"];

        _scanService.ProgressChanged += OnScanProgressChanged;
        _scanService.CategoryScanned += OnCategoryScanned;
        _scanService.AccessDenied += OnAccessDenied;

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

            // 订阅分类及其所有项的勾选变化，实时刷新"已选中大小"。
            cat.PropertyChanged += OnCategoryPropertyChanged;
            foreach (var item in cat.Items)
                item.PropertyChanged += OnItemPropertyChanged;
            OnSelectedSizeChanged();
        }));
    }

    private void OnCategoryPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanCategoryResult.IsSelected))
            OnSelectedSizeChanged();
    }

    private void OnItemPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanableItem.IsSelected))
            OnSelectedSizeChanged();
    }

    private void OnSelectedSizeChanged()
    {
        OnPropertyChanged(nameof(SelectedCleanableSize));
        OnPropertyChanged(nameof(SelectedCleanableText));
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

    private void OnCleanProgressUpdated(CleanProgress p)
    {
        CleanProgressText = $"{p.Current} / {p.Total}";
        CleanCurrentFile = p.CurrentFile;
        CleanCurrentPath = p.CurrentPath;
        CleanProgressPercent = p.Percent;
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

            // 扫描重复文件（作为磁盘清理的补充分类，标注"重复文件·请确认"）
            await ScanDuplicatesAsync(_cts.Token);

            StatusText = $"{Lang["ScanComplete"]}. {TotalItemCount} {Lang["Items"]} ({TotalCleanableText})";
        }
        catch (OperationCanceledException) { StatusText = Lang["Cancelled"]; App.Log("Scan cancelled by user"); }
        catch (Exception ex) { StatusText = ex.Message; App.LogError("StartScanAsync", ex); }
        finally { IsScanning = false; _diskInfoService.Refresh(); }
    }

    /// <summary>
    /// 扫描用户目录下的重复文件，结果作为"重复文件"分类追加到扫描结果中。
    /// 每个重复项标注为 Caution（请确认后删除），并说明与哪个保留副本重复。
    /// </summary>
    private async Task ScanDuplicatesAsync(CancellationToken ct)
    {
        try
        {
            StatusText = "正在扫描重复文件...";
            var groups = await _duplicateService.FindDuplicatesAsync(ct: ct);
            if (groups.Count == 0) return;

            var category = new ScanCategoryResult
            {
                Category = CleanCategory.DuplicateFiles,
                DisplayName = "重复文件",
                Icon = "\uE8C8", // Segoe MDL2 复制图标
                IsSelected = false // 重复文件默认不勾选，用户逐组确认后再清理
            };

            int groupIndex = 0;
            foreach (var group in groups)
            {
                groupIndex++;
                var keptFile = group.Files.FirstOrDefault(f => f.IsKept);
                foreach (var file in group.Files.Where(f => !f.IsKept))
                {
                    category.Items.Add(new CleanableItem
                    {
                        Name = file.FileName,
                        FullPath = file.FullPath,
                        SizeBytes = file.SizeBytes,
                        Safety = CleanSafety.Caution,
                        Category = CleanCategory.DuplicateFiles,
                        Description = $"重复组 #{groupIndex}：内容与保留副本「{keptFile?.FileName ?? "?"}」完全相同，删除后释放 {file.SizeText}",
                        SoftwareName = $"重复组 #{groupIndex}",
                        FileType = "重复文件",
                        LastModified = file.LastModified,
                        IsDirectory = false,
                        IsSelected = false // 重复文件默认不勾选，用户逐组确认后再清理
                    });
                }
            }

            if (category.Items.Count > 0)
            {
                ScanResults.Add(category);
                TotalCleanableSize += category.TotalSize;
                TotalItemCount += category.ItemCount;
            }
        }
        catch (OperationCanceledException) { /* 用户取消扫描，静默处理 */ }
        catch (Exception ex) { App.LogError("ScanDuplicatesAsync", ex); }
    }

    private async Task StartCleanAsync()
    {
        var toClean = ScanResults.Where(c => c.IsSelected && c.Items.Any(i => i.IsSelected)).ToList();
        if (toClean.Count == 0) return;

        // Preview confirmation
        var totalSize = toClean.Sum(c => c.TotalSize);
        var totalItems = toClean.Sum(c => c.Items.Count(i => i.IsSelected));

        // 检测选中项的风险等级：危险 + 谨慎项都需要醒目警告（目标用户是不太懂电脑的人）
        var dangerousItems = toClean
            .SelectMany(c => c.Items.Where(i => i.IsSelected && i.IsDangerous))
            .ToList();
        var cautionItems = toClean
            .SelectMany(c => c.Items.Where(i => i.IsSelected && i.Safety == CleanSafety.Caution))
            .ToList();

        var previewMsg = "即将清理以下内容：\n\n";
        foreach (var cat in toClean.Take(10))
        {
            previewMsg += $"• {cat.DisplayName}: {cat.ItemCount} 项 ({cat.TotalSizeText})\n";
        }
        if (toClean.Count > 10)
            previewMsg += $"... 等 {toClean.Count} 个分类\n";

        previewMsg += $"\n总计: {totalItems} 项，约 {ByteSizeFormatter.Format(totalSize)}";

        // 谨慎项提醒（黄色，针对小白用户说明"可能包含有用数据"）
        if (cautionItems.Count > 0)
        {
            previewMsg += $"\n\n⚠️ 注意：其中包含 {cautionItems.Count} 个「谨慎」项（可能有用的数据），如：\n";
            foreach (var item in cautionItems.Take(5))
                previewMsg += $"  - {item.Name}\n";
            previewMsg += "\n请确认这些内容确实不需要了。";
        }

        // 危险项强警告（红色，可能影响系统/软件）
        if (dangerousItems.Count > 0)
        {
            previewMsg += "\n\n🚫 严重警告：您选中了危险项，删除可能影响系统或软件功能：\n";
            foreach (var item in dangerousItems.Take(5))
                previewMsg += $"  - {item.Name}\n";
            previewMsg += "\n请务必确认您了解后果！";
        }

        // 永久删除的后果明说（小白用户最容易忽略这点）
        previewMsg += "\n\n删除后将无法恢复（不进入回收站）。是否继续清理？";

        var hasRisk = dangerousItems.Count > 0 || cautionItems.Count > 0;
        var confirm = CleanMaster.Views.AppDialog.Show(
            previewMsg,
            dangerousItems.Count > 0 ? "危险操作确认" : (cautionItems.Count > 0 ? "请确认后清理" : "清理确认"),
            MessageBoxButton.YesNo,
            dangerousItems.Count > 0 ? MessageBoxImage.Warning : (hasRisk ? MessageBoxImage.Warning : MessageBoxImage.Question));
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

        // Progress<T> 在 UI 线程创建，回调自动回到 UI 线程，无需 Dispatcher。
        var progress = new Progress<CleanProgress>(OnCleanProgressUpdated);

        try
        {
            LastCleanResult = await _cleanService.CleanAsync(toClean, progress, _cts.Token);
            StatusText = $"{Lang["CleanComplete"]}. {CleanResultText}";

            // 失败项/警告可见：至少给出数量提示（详见日志）
            if (LastCleanResult != null && LastCleanResult.HasIssues)
            {
                var issueParts = new List<string>();
                if (LastCleanResult.Errors.Count > 0)
                    issueParts.Add($"{LastCleanResult.Errors.Count} 项失败");
                if (LastCleanResult.Warnings.Count > 0)
                    issueParts.Add($"{LastCleanResult.Warnings.Count} 项警告");
                StatusText += $"（{string.Join("，", issueParts)}，详见日志）";
            }

            ScanResults.Clear();
            TotalCleanableSize = 0;
            TotalItemCount = 0;
        }
        catch (OperationCanceledException) { StatusText = Lang["Cancelled"]; App.Log("Clean cancelled by user"); }
        catch (Exception ex) { StatusText = ex.Message; App.LogError("StartCleanAsync", ex); }
        finally { IsCleaning = false; IsCleanProgressVisible = false; _diskInfoService.Refresh(); }
    }

    #endregion

    private void ToggleExpand(ScanCategoryResult? category)
    {
        if (category == null) return;
        category.IsExpanded = !category.IsExpanded;
    }

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
            Lang.LanguageChanged -= OnLanguageChanged;
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}
