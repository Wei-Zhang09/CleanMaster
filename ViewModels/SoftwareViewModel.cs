using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class SoftwareViewModel : INotifyPropertyChanged
{
    private readonly ISoftwareService _softwareService;

    public LangService Lang { get; } = LangService.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<InstalledSoftware> InstalledSoftware { get; } = new();

    private InstalledSoftware? _selectedSoftware;
    public InstalledSoftware? SelectedSoftware { get => _selectedSoftware; set { _selectedSoftware = value; OnPropertyChanged(); } }

    private bool _isLoadingSoftware;
    public bool IsLoadingSoftware { get => _isLoadingSoftware; set { _isLoadingSoftware = value; OnPropertyChanged(); } }

    private string _softwareStatus = "";
    public string SoftwareStatus { get => _softwareStatus; set { _softwareStatus = value; OnPropertyChanged(); } }

    public RelayCommand<InstalledSoftware> UninstallCommand { get; }

    public SoftwareViewModel(ISoftwareService softwareService)
    {
        _softwareService = softwareService;
        UninstallCommand = new RelayCommand<InstalledSoftware>(async (s) => await UninstallSoftwareAsync(s));
    }

    public async Task LoadInstalledSoftwareAsync()
    {
        if (InstalledSoftware.Count > 0 || IsLoadingSoftware) return;
        IsLoadingSoftware = true;
        SoftwareStatus = "正在扫描已安装软件...";

        try
        {
            var list = await Task.Run(() => _softwareService.GetInstalledSoftware());
            foreach (var s in list) InstalledSoftware.Add(s);
            SoftwareStatus = $"共 {list.Count} 个软件";
        }
        catch (Exception ex)
        {
            SoftwareStatus = "扫描失败";
            CleanMaster.App.LogError("LoadInstalledSoftware", ex);
        }
        finally
        {
            IsLoadingSoftware = false;
        }
    }

    private async Task UninstallSoftwareAsync(InstalledSoftware? software)
    {
        if (software == null) return;

        var confirmMsg = $"确定要卸载以下软件吗？\n\n" +
            $"名称：{software.Name}\n" +
            $"版本：{software.Version}\n" +
            $"发布者：{software.Publisher}\n" +
            $"大小：{software.SizeText}\n" +
            $"路径：{software.InstallLocation}\n\n" +
            $"提示：卸载后将自动清理残留文件和注册表。";

        var confirm = System.Windows.MessageBox.Show(confirmMsg, "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _softwareService.UninstallSoftware(software);
            await Task.Delay(3000);

            var leftovers = _softwareService.ScanLeftovers(software);

            if (leftovers.LeftoverFolders.Count > 0 || leftovers.LeftoverRegistryKeys.Count > 0)
            {
                var leftoverMsg = $"卸载程序已启动。\n\n发现残留文件和注册表项：\n";
                foreach (var folder in leftovers.LeftoverFolders.Take(3))
                    leftoverMsg += $"  文件：{folder}\n";
                foreach (var reg in leftovers.LeftoverRegistryKeys.Take(3))
                    leftoverMsg += $"  注册表：{reg}\n";

                leftoverMsg += $"\n残留大小：{leftovers.LeftoverSizeText}";
                leftoverMsg += $"\n\n是否自动清理这些残留？（推荐清理）";

                var cleanupConfirm = System.Windows.MessageBox.Show(leftoverMsg, "清理残留", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (cleanupConfirm == MessageBoxResult.Yes)
                {
                    _softwareService.CleanupLeftovers(leftovers);
                    System.Windows.MessageBox.Show($"已清理 {leftovers.LeftoverSizeText} 残留文件和注册表", "清理完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("卸载完成，未发现残留文件。", "卸载完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            InstalledSoftware.Clear();
            SoftwareStatus = "";
            await LoadInstalledSoftwareAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"卸载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
