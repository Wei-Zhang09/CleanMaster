using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly ILicenseService _licenseService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LangService Lang { get; } = LangService.Instance;

    public bool IsChinese
    {
        get => Lang.IsChinese;
        set { Lang.IsChinese = value; OnPropertyChanged(); OnPropertyChanged(nameof(Lang)); }
    }

    private string _websiteUrl = "";
    public string WebsiteUrl { get => _websiteUrl; set { _websiteUrl = value; OnPropertyChanged(); } }

    private bool _isActivated;
    public bool IsActivated
    {
        get => _isActivated;
        set { _isActivated = value; OnPropertyChanged(); OnPropertyChanged(nameof(LicenseStatusText)); OnPropertyChanged(nameof(LicenseStatusColor)); }
    }

    private string _licenseStatusText = "";
    public string LicenseStatusText { get => _licenseStatusText; set { _licenseStatusText = value; OnPropertyChanged(); } }

    public string LicenseStatusColor => IsActivated ? "#10B981" : "#F59E0B";

    private string _activatedProduct = "";
    public string ActivatedProduct { get => _activatedProduct; set { _activatedProduct = value; OnPropertyChanged(); } }

    public RelayCommand ToggleLangCommand { get; }
    public RelayCommand ShowActivationCommand { get; }
    public RelayCommand OpenWebsiteCommand { get; }
    public RelayCommand SaveWebsiteUrlCommand { get; }

    public SettingsViewModel(ISettingsService settingsService, ILicenseService licenseService)
    {
        _settingsService = settingsService;
        _licenseService = licenseService;
        WebsiteUrl = _settingsService.Get().WebsiteUrl;

        ToggleLangCommand = new RelayCommand(() => { IsChinese = !IsChinese; OnPropertyChanged(nameof(Lang)); });
        ShowActivationCommand = new RelayCommand(ShowActivationDialog);
        OpenWebsiteCommand = new RelayCommand(OpenWebsite);
        SaveWebsiteUrlCommand = new RelayCommand(SaveWebsiteUrl);

#if DEBUG
        IsActivated = true;
        LicenseStatusText = "DEBUG - 已激活";
#else
        _ = Task.Run(async () =>
        {
            try { await CheckLicenseAsync(); }
            catch (Exception ex) { CleanMaster.App.LogError("CheckLicense", ex); }
        });
        LicenseStatusText = "检查激活状态...";
#endif
    }

    public async Task CheckLicenseAsync()
    {
        LicenseStatusText = "检查激活状态...";
        var (isValid, message) = await _licenseService.CheckActivationAsync();
        IsActivated = isValid;
        LicenseStatusText = isValid ? "已激活" : "未激活";

        if (isValid)
        {
            var info = _licenseService.LoadLocal();
            ActivatedProduct = info?.SoftwareName ?? "";
        }
    }

    private void ShowActivationDialog()
    {
        if (IsActivated)
        {
            System.Windows.MessageBox.Show("您已激活本软件，无需再激活。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new Views.ActivationDialog();
            dialog.ShowDialog();

            if (dialog.ActivationSuccessful)
            {
                _ = CheckLicenseAsync();
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"打开激活窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenWebsite()
    {
        try
        {
            var url = _settingsService.Get().WebsiteUrl;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { CleanMaster.App.LogError("OpenWebsite", ex); }
    }

    private void SaveWebsiteUrl()
    {
        try
        {
            var settings = _settingsService.Get();
            settings.WebsiteUrl = WebsiteUrl;
            _settingsService.Save(settings);
            System.Windows.MessageBox.Show("网站地址已保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { CleanMaster.App.LogError("SaveWebsiteUrl", ex); }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
