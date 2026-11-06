using System.Threading;
using System.Windows;

namespace CleanMaster.Views;

/// <summary>
/// 更新下载/安装进度窗。
/// 用非阻塞 Show() + 主窗口禁用实现"模态观感"：下载是 async 的，
/// ShowDialog() 会阻塞调用线程导致进度回调进不来（界面停在 0%）。
/// </summary>
public partial class UpdateProgressDialog : Window
{
    private readonly CancellationTokenSource _cts = new();
    private bool _finished;

    public UpdateProgressDialog(string? version = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(version))
            TitleText.Text = $"正在更新到 v{version}";
    }

    /// <summary>传给下载的取消令牌：用户点「取消」时触发。</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>下载已开始、安装器已拉起后置位，此时不再关闭窗口/恢复主窗口。</summary>
    public bool HandedOffToInstaller { get; private set; }

    public void SetStatus(string text) => StatusText.Text = text;

    public void SetPercent(double percent)
    {
        var value = Math.Clamp(double.IsNaN(percent) ? 0 : percent, 0, 100);
        Progress.Value = value;
        PercentText.Text = $"{value:F0}%";
    }

    /// <summary>下载完成、安装器即将启动：把进度条钉在 100%，提示即将重启。</summary>
    public void SetInstalling()
    {
        _finished = true;
        Progress.Value = 100;
        PercentText.Text = "100%";
        StatusText.Text = "下载完成，正在安装...";
        HintText.Text = "程序将自动关闭并重启，请稍候。";
        CancelButton.IsEnabled = false;
    }

    /// <summary>取消后（或失败后）的终态文案。</summary>
    public void SetFinished(string message)
    {
        _finished = true;
        StatusText.Text = message;
        CancelButton.IsEnabled = false;
    }

    public void MarkHandedOff() => HandedOffToInstaller = true;

    /// <summary>关闭窗口并恢复主窗口可用（安装器接管时不要调用）。</summary>
    public void CloseAndRestore()
    {
        if (Owner != null) Owner.IsEnabled = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_finished) return;

        _cts.Cancel();
        StatusText.Text = "正在取消...";
        CancelButton.IsEnabled = false;
    }

    // 下载途中用户直接点窗口的关闭（Alt+F4）等同于取消，避免进程被关掉但下载还在跑
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_finished && !HandedOffToInstaller)
        {
            _cts.Cancel();
        }
        base.OnClosing(e);
    }
}
