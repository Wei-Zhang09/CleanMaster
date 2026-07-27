using System.ComponentModel;

namespace CleanMaster.Services.Interfaces;

public interface ILangService : INotifyPropertyChanged
{
    string this[string key] { get; }
    bool IsChinese { get; set; }
    void Toggle();

    /// <summary>
    /// Raised whenever the active language changes. Subscribers should
    /// re-fire PropertyChanged for their <c>Lang</c> property so XAML bindings
    /// like <c>{Binding Lang[Key]}</c> re-evaluate across the whole app.
    /// </summary>
    event Action? LanguageChanged;
}
