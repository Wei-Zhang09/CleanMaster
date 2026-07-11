using System.ComponentModel;

namespace CleanMaster.Services.Interfaces;

public interface ILangService : INotifyPropertyChanged
{
    string this[string key] { get; }
    bool IsChinese { get; set; }
    void Toggle();
}
