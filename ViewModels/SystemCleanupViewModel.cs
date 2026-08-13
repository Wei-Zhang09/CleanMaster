using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class SystemCleanupViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISystemCleanupService _systemCleanupService;
    private readonly DiskInfoService _diskInfoService;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ILangService Lang { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isRunningCleanup;
    public bool IsRunningCleanup
    {
        get => _isRunningCleanup;
        set { _isRunningCleanup = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsProgressVisible)); }
    }

    private string _cleanupStatus = "";
    public string CleanupStatus { get => _cleanupStatus; set { _cleanupStatus = value; OnPropertyChanged(); } }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
    }

    public string ProgressText => $"{ProgressPercent:F0}%";

    public bool IsProgressVisible => IsRunningCleanup || ProgressPercent > 0;

    public RelayCommand RunDismCleanupCommand { get; }
    public RelayCommand RunSfcScanCommand { get; }
    public RelayCommand FlushDnsCommand { get; }

    public SystemCleanupViewModel(ISystemCleanupService systemCleanupService, DiskInfoService diskInfoService, ILangService langService)
    {
        _systemCleanupService = systemCleanupService;
        _diskInfoService = diskInfoService;
        Lang = langService;

        _systemCleanupService.ProgressChanged += OnProgressChanged;

        RunDismCleanupCommand = new RelayCommand(async () => await RunDismCleanupAsync());
        RunSfcScanCommand = new RelayCommand(async () => await RunSfcScanAsync());
        FlushDnsCommand = new RelayCommand(async () => await FlushDnsAsync());
    }

    private void OnProgressChanged(string msg)
    {
        App.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            CleanupStatus = msg;
            var percent = ParsePercent(msg);
            if (percent >= 0)
                ProgressPercent = percent;
        }));
    }

    private static double ParsePercent(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return -1;

        // Match patterns like "100%", "100 %", "进度 50%" etc.
        var match = Regex.Match(msg, @"(\d+)\s*%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var percent))
            return percent;

        return -1;
    }

    private async Task RunDismCleanupAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Windows 组件清理将删除旧版本的系统更新文件。\n\n此操作安全但可能需要几分钟时间。\n\n是否继续？",
            "系统组件清理", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsRunningCleanup = true;
        ProgressPercent = 0;
        CleanupStatus = "正在准备 Windows 组件清理...";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _systemCleanupService.RunDismCleanupAsync(_cts.Token);
            ProgressPercent = 100;
            CleanupStatus = result.Success ? $"清理完成: {result.Message}" : $"清理失败: {result.Message}";
            if (result.Success)
                System.Windows.MessageBox.Show($"{result.Message}\n\n释放空间: {result.FreedText}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { CleanupStatus = "操作已取消"; }
        catch (Exception ex) { CleanupStatus = $"错误: {ex.Message}"; CleanMaster.App.LogError("RunDismCleanup", ex); }
        finally { IsRunningCleanup = false; _diskInfoService.Refresh("C:"); }
    }

    private async Task RunSfcScanAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "系统文件扫描将检查并修复损坏的系统文件。\n\n此操作安全但可能需要较长时间。\n\n是否继续？",
            "系统文件修复", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsRunningCleanup = true;
        ProgressPercent = 0;
        CleanupStatus = "正在准备系统文件扫描...";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _systemCleanupService.RunSfcScanAsync(_cts.Token);
            ProgressPercent = 100;
            CleanupStatus = result.Success ? $"扫描完成: {result.Message}" : $"扫描失败: {result.Message}";
            System.Windows.MessageBox.Show(
                result.Success ? result.Message : $"扫描失败: {result.Message}",
                result.Success ? "扫描完成" : "扫描失败",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (OperationCanceledException) { CleanupStatus = "操作已取消"; }
        catch (Exception ex) { CleanupStatus = $"错误: {ex.Message}"; CleanMaster.App.LogError("RunSfcScan", ex); }
        finally { IsRunningCleanup = false; }
    }

    private async Task FlushDnsAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "清理 DNS 缓存将重置域名解析记录。\n\n此操作安全，不影响其他设置。\n\n是否继续？",
            "DNS 缓存清理", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsRunningCleanup = true;
        ProgressPercent = 0;
        CleanupStatus = "正在清理 DNS 缓存...";
        try
        {
            var result = await _systemCleanupService.FlushDnsCacheAsync();
            ProgressPercent = 100;
            CleanupStatus = result.Success ? "DNS 缓存已清理" : $"清理失败: {result.Message}";
            System.Windows.MessageBox.Show(
                result.Success ? "DNS 缓存已清理成功。" : $"清理失败: {result.Message}",
                result.Success ? "清理完成" : "清理失败",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { CleanupStatus = $"错误: {ex.Message}"; CleanMaster.App.LogError("FlushDns", ex); }
        finally { IsRunningCleanup = false; }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _systemCleanupService.ProgressChanged -= OnProgressChanged; }
        catch { }
        GC.SuppressFinalize(this);
    }
}
