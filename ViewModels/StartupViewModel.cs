using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class StartupViewModel : INotifyPropertyChanged
{
    private readonly ISoftwareService _softwareService;

    public LangService Lang { get; } = LangService.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StartupItem> StartupItems { get; } = new();

    public RelayCommand<StartupItem> DisableStartupCommand { get; }

    public StartupViewModel(ISoftwareService softwareService)
    {
        _softwareService = softwareService;
        DisableStartupCommand = new RelayCommand<StartupItem>(DisableStartupItem);
    }

    public async void LoadStartupItems()
    {
        if (StartupItems.Count > 0) return;
        try
        {
            var list = await Task.Run(() => _softwareService.GetStartupItems());
            foreach (var item in list) StartupItems.Add(item);
        }
        catch (Exception ex) { CleanMaster.App.LogError("LoadStartupItems", ex); }
    }

    private void DisableStartupItem(StartupItem? item)
    {
        if (item == null) return;
        if (_softwareService.DisableStartupItem(item))
            StartupItems.Remove(item);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
