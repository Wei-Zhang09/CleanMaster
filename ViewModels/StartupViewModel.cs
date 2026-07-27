using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class StartupViewModel : INotifyPropertyChanged
{
    private readonly ISoftwareService _softwareService;
    private bool _isLoading;
    private string _statusText = "";

    public LangService Lang { get; } = LangService.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StartupItem> StartupItems { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public RelayCommand<StartupItem> ToggleStartupCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public StartupViewModel(ISoftwareService softwareService)
    {
        _softwareService = softwareService;
        ToggleStartupCommand = new RelayCommand<StartupItem>(ToggleStartupItem);
        RefreshCommand = new RelayCommand(async () => await LoadStartupItemsAsync(forceRefresh: true));

        // 订阅语言变更, 刷新本地 Lang 属性 + 状态文本
        Lang.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        try
        {
            App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(Lang));
                OnPropertyChanged(nameof(StatusText));
            }));
        }
        catch
        {
            OnPropertyChanged(nameof(Lang));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>
    /// Used by MainViewModel.OnNavigatedTo — fire-and-forget load with cache.
    /// Subsequent navigations skip reloading unless forceRefresh is requested.
    /// </summary>
    public async void LoadStartupItems()
    {
        await LoadStartupItemsAsync(forceRefresh: false);
    }

    public async Task LoadStartupItemsAsync(bool forceRefresh)
    {
        if (IsLoading) return;
        if (!forceRefresh && StartupItems.Count > 0) return;

        IsLoading = true;
        StatusText = Lang["StartupLoading"];
        var list = new List<StartupItem>();

        try
        {
            // 1) 后台线程: 仅读取注册表/启动文件夹, 不抽取图标 — 这步很快
            list = await Task.Run(() => _softwareService.GetStartupItems());

            // 2) UI 线程: 清空并立即填充列表, 图标先留空字符串, 让列表先显示出来
            StartupItems.Clear();
            foreach (var item in list)
            {
                StartupItems.Add(item);
            }

            // 3) 状态文本更新: 显示找到多少项
            StatusText = string.Format(Lang["StartupItemsLoaded"], list.Count);

            // 4) 后台线程: 异步逐个解析图标路径, 然后回 UI 线程更新对应项的 IconPath。
            //    INotifyPropertyChanged 会让 Image binding 自动重新评估, 不需要替换整个 item。
            await Task.Run(() =>
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    var iconPath = _softwareService.GetStartupItemIconPath(item);
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        var captured = item;
                        App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            captured.IconPath = iconPath;
                        }));
                    }
                }
            });
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("LoadStartupItems", ex);
            StatusText = Lang["StartupLoadFailed"];
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ToggleStartupItem(StartupItem? item)
    {
        if (item == null) return;

        try
        {
            bool success;
            if (item.IsEnabled)
            {
                success = _softwareService.DisableStartupItem(item);
                if (success) item.IsEnabled = false;
            }
            else
            {
                success = _softwareService.EnableStartupItem(item);
                if (success) item.IsEnabled = true;
            }

            if (!success)
            {
                MessageBox.Show("切换启动项失败，可能需要管理员权限。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                // Force UI refresh by replacing the item reference (ObservableCollection doesn't
                // observe property changes on the items themselves)
                var idx = StartupItems.IndexOf(item);
                if (idx >= 0)
                {
                    StartupItems[idx] = item;
                }
            }
        }
        catch (Exception ex) { CleanMaster.App.LogError("ToggleStartupItem", ex); }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
