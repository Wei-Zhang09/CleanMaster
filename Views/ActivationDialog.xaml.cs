using System.Windows;
using CleanMaster.Services;

namespace CleanMaster.Views;

public partial class ActivationDialog : Window
{
    private readonly LicenseService _licenseService = new();
    private bool _isActivating;

    public bool ActivationSuccessful { get; private set; }

    public ActivationDialog()
    {
        InitializeComponent();
    }

    private void KeyCodeBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Placeholder.Visibility = string.IsNullOrEmpty(KeyCodeBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        var keyCode = KeyCodeBox.Text.Trim();
        if (string.IsNullOrEmpty(keyCode))
        {
            ShowStatus("请输入激活密钥", false);
            return;
        }

        if (_isActivating) return;
        _isActivating = true;
        ActivateButton.IsEnabled = false;
        ActivateButton.Content = "验证中...";
        ShowStatus("正在连接服务器验证...", true);

        try
        {
            var (success, message, info) = await _licenseService.VerifyKeyAsync(keyCode);

            if (success)
            {
                ShowStatus($"激活成功！{info?.SoftwareName ?? ""}", true);
                ActivationSuccessful = true;
                await Task.Delay(1500);
                DialogResult = true;
                Close();
            }
            else
            {
                ShowStatus(message, false);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"错误: {ex.Message}", false);
        }
        finally
        {
            _isActivating = false;
            ActivateButton.IsEnabled = true;
            ActivateButton.Content = "激活";
        }
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OpenWebsite_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://awe-software-production.up.railway.app/store",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void ShowStatus(string message, bool isSuccess)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusText.Text = message;
        StatusBorder.Background = isSuccess
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 16, 185, 129))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 239, 68, 68));
        StatusText.Foreground = isSuccess
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
    }
}
