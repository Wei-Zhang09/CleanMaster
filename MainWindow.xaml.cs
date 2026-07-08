using System.Windows;

namespace CleanMaster;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.LoadDiskInfo();
            }
        };
    }
}
