using System.ComponentModel;
using System.IO;
using CleanMaster.Models;

namespace CleanMaster.Services;

public class DiskInfoService : INotifyPropertyChanged
{
    private DiskInfo? _diskInfo;
    public DiskInfo? DiskInfo
    {
        get => _diskInfo;
        set { _diskInfo = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiskInfo))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh(string drive = "C:")
    {
        var di = new DriveInfo(drive);
        DiskInfo = new DiskInfo
        {
            DriveLetter = drive,
            TotalBytes = di.TotalSize,
            UsedBytes = di.TotalSize - di.AvailableFreeSpace,
            FreeBytes = di.AvailableFreeSpace
        };
    }
}
