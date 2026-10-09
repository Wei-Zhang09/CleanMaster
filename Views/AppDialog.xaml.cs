using System.Windows;
using System.Windows.Media;

namespace CleanMaster.Views;

/// <summary>
/// 与软件主题风格统一的自定义弹窗，替代系统原生 MessageBox。
/// 静态 <see cref="Show"/> 方法的签名兼容 MessageBox，返回值同为 <see cref="MessageBoxResult"/>。
/// </summary>
public partial class AppDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.Cancel;
    private MessageBoxButton _button = MessageBoxButton.OK;

    public AppDialog()
    {
        InitializeComponent();
    }

    public static MessageBoxResult Show(string message, string title = "CleanMaster",
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var dialog = new AppDialog();
        dialog.Configure(message, title, button, icon);
        dialog.ShowDialog();
        return dialog._result;
    }

    private void Configure(string message, string title, MessageBoxButton button, MessageBoxImage icon)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        ConfigureIcon(icon);
        ConfigureButtons(button);
    }

    private void ConfigureIcon(MessageBoxImage icon)
    {
        // Segoe MDL2 图标 + 主题色
        switch (icon)
        {
            case MessageBoxImage.Warning:
                IconText.Text = "\uE7BA"; // Warning
                IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF9C3"));
                IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CA8A04"));
                break;
            case MessageBoxImage.Error:
                IconText.Text = "\uEA39"; // ErrorBadge
                IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
                IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                break;
            case MessageBoxImage.Question:
                IconText.Text = "\uE9CE"; // Help
                IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F3FF"));
                IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E90FF"));
                break;
            case MessageBoxImage.Information:
            default:
                IconText.Text = "\uE946"; // Info
                IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
                IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                break;
        }
    }

    private void ConfigureButtons(MessageBoxButton button)
    {
        _button = button;
        switch (button)
        {
            case MessageBoxButton.YesNo:
                YesButton.Content = "是";
                NoButton.Content = "否";
                NoButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
                break;
            case MessageBoxButton.YesNoCancel:
                YesButton.Content = "是";
                NoButton.Content = "否";
                CancelButton.Content = "取消";
                NoButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                break;
            case MessageBoxButton.OKCancel:
                YesButton.Content = "确定";
                CancelButton.Content = "取消";
                CancelButton.Visibility = Visibility.Visible;
                NoButton.Visibility = Visibility.Collapsed;
                break;
            case MessageBoxButton.OK:
            default:
                YesButton.Content = "确定";
                NoButton.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        // OK 按钮返回 OK；Yes 按钮返回 Yes（保持 MessageBox 语义）
        _result = _button == MessageBoxButton.OK || _button == MessageBoxButton.OKCancel
            ? MessageBoxResult.OK
            : MessageBoxResult.Yes;
        DialogResult = true;
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.No;
        DialogResult = false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Cancel;
        DialogResult = false;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _result = MessageBoxResult.Cancel;
        DialogResult = false;
    }
}
