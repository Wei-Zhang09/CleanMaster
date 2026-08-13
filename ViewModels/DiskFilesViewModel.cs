using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CleanMaster.Models;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;

namespace CleanMaster.ViewModels;

public class DiskFilesViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IScanService _scanService;
    private readonly ICleanService _cleanService;
    private readonly IFolderScanService _folderScanService;
    private readonly DiskInfoService _diskInfoService;
    private CancellationTokenSource? _cts;

    public ILangService Lang { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    #region Large Files

    public ObservableCollection<LargeFileItem> LargeFiles { get; } = new();

    private ICollectionView? _largeFilesView;
    public ICollectionView LargeFilesView
    {
        get
        {
            if (_largeFilesView == null)
            {
                _largeFilesView = CollectionViewSource.GetDefaultView(LargeFiles);
                _largeFilesView.Filter = FilterLargeFile;
            }
            return _largeFilesView;
        }
    }

    private string _largeFileSearchText = "";
    public string LargeFileSearchText
    {
        get => _largeFileSearchText;
        set { _largeFileSearchText = value; OnPropertyChanged(); LargeFilesView?.Refresh(); }
    }

    private string _selectedFileTypeFilter = "全部";
    public string SelectedFileTypeFilter
    {
        get => _selectedFileTypeFilter;
        set { _selectedFileTypeFilter = value; OnPropertyChanged(); LargeFilesView?.Refresh(); }
    }

    public ObservableCollection<string> FileTypeFilters { get; } = new()
    {
        "全部", "视频", "音频", "图片", "压缩包", "文档", "其他"
    };

    private bool FilterLargeFile(object item)
    {
        if (item is not LargeFileItem file) return false;

        // Search text filter
        if (!string.IsNullOrEmpty(LargeFileSearchText))
        {
            var search = LargeFileSearchText.ToLower();
            if (!file.FileName.ToLower().Contains(search) &&
                !file.FullPath.ToLower().Contains(search) &&
                !file.FileType.ToLower().Contains(search))
                return false;
        }

        // File type filter
        if (SelectedFileTypeFilter != "全部")
        {
            return SelectedFileTypeFilter switch
            {
                "视频" => file.FileType.Contains("视频"),
                "音频" => file.FileType.Contains("音频"),
                "图片" => file.FileType.Contains("图片"),
                "压缩包" => file.FileType.Contains("压缩"),
                "文档" => file.FileType.Contains("文档") || file.FileType.Contains("表格") || file.FileType.Contains("演示"),
                "其他" => !file.FileType.Contains("视频") && !file.FileType.Contains("音频") &&
                          !file.FileType.Contains("图片") && !file.FileType.Contains("压缩") &&
                          !file.FileType.Contains("文档") && !file.FileType.Contains("表格") && !file.FileType.Contains("演示"),
                _ => true
            };
        }

        return true;
    }

    private bool _isFindingLargeFiles;
    public bool IsFindingLargeFiles { get => _isFindingLargeFiles; set { _isFindingLargeFiles = value; OnPropertyChanged(); } }

    private string _largeFileStatus = "";
    public string LargeFileStatus { get => _largeFileStatus; set { _largeFileStatus = value; OnPropertyChanged(); } }

    private long _minFileSizeBytes = 100 * 1024 * 1024;
    public long MinFileSizeBytes { get => _minFileSizeBytes; set { _minFileSizeBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(MinFileSizeText)); OnPropertyChanged(nameof(MinFileSizeInput)); } }

    public string MinFileSizeText => $"{MinFileSizeBytes / 1_048_576} MB";

    public string MinFileSizeInput
    {
        get => $"{MinFileSizeBytes / 1_048_576}";
        set
        {
            if (long.TryParse(value, out var mb) && mb > 0)
                MinFileSizeBytes = mb * 1024 * 1024;
        }
    }

    public ObservableCollection<string> DiskDrives { get; } = new();

    private string _selectedDrive = "C:";
    public string SelectedDrive { get => _selectedDrive; set { _selectedDrive = value; OnPropertyChanged(); } }

    #endregion

    #region Large Folders

    public ObservableCollection<LargeFolderItem> LargeFolders { get; } = new();

    private bool _isScanningFolders;
    public bool IsScanningFolders { get => _isScanningFolders; set { _isScanningFolders = value; OnPropertyChanged(); } }

    private string _folderScanStatus = "";
    public string FolderScanStatus { get => _folderScanStatus; set { _folderScanStatus = value; OnPropertyChanged(); } }

    #endregion

    #region Empty Folders

    public ObservableCollection<EmptyFolderItem> EmptyFolders { get; } = new();

    private bool _isScanningEmpty;
    public bool IsScanningEmpty { get => _isScanningEmpty; set { _isScanningEmpty = value; OnPropertyChanged(); } }

    private string _emptyFolderStatus = "";
    public string EmptyFolderStatus { get => _emptyFolderStatus; set { _emptyFolderStatus = value; OnPropertyChanged(); } }

    #endregion

    #region Commands

    public ICommand FindLargeFilesCommand { get; }
    public ICommand DeleteLargeFilesCommand { get; }
    public ICommand FindLargeFoldersCommand { get; }
    public ICommand FindEmptyFoldersCommand { get; }
    public ICommand DeleteEmptyFoldersCommand { get; }
    public ICommand OpenFolderCommand { get; }

    #endregion

    public DiskFilesViewModel(IScanService scanService, ICleanService cleanService, IFolderScanService folderScanService, DiskInfoService diskInfoService, ILangService langService)
    {
        _scanService = scanService;
        _cleanService = cleanService;
        _folderScanService = folderScanService;
        _diskInfoService = diskInfoService;
        Lang = langService;

        FindLargeFilesCommand = new RelayCommand(async () => await FindLargeFilesAsync());
        DeleteLargeFilesCommand = new RelayCommand(async () => await DeleteLargeFilesAsync());
        FindLargeFoldersCommand = new RelayCommand(async () => await FindLargeFoldersAsync());
        FindEmptyFoldersCommand = new RelayCommand(async () => await FindEmptyFoldersAsync());
        DeleteEmptyFoldersCommand = new RelayCommand(async () => await DeleteEmptyFoldersAsync());
        OpenFolderCommand = new RelayCommand<string>(OpenFolder);

        // 语言切换时刷新本地 Lang 属性, 让 {Binding Lang[...]} 重新求值
        Lang.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        try
        {
            App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(Lang));
            }));
        }
        catch
        {
            OnPropertyChanged(nameof(Lang));
        }
    }

    private static void OpenFolder(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return;

        try
        {
            string? dirPath;
            if (Directory.Exists(fullPath))
            {
                dirPath = fullPath;
            }
            else if (File.Exists(fullPath))
            {
                dirPath = Path.GetDirectoryName(fullPath);
            }
            else
            {
                // Fall back to the longest existing ancestor
                var p = fullPath;
                while (!string.IsNullOrEmpty(p) && !Directory.Exists(p) && !File.Exists(p))
                {
                    p = Path.GetDirectoryName(p);
                }
                dirPath = Directory.Exists(p) ? p : (File.Exists(p) ? Path.GetDirectoryName(p) : null);
            }

            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            {
                System.Windows.MessageBox.Show("路径不存在或无法访问。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Use explorer.exe with the file path to select the file (if it's a file)
            // Otherwise just open the directory.
            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{fullPath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dirPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            CleanMaster.App.LogError("OpenFolder", ex);
            System.Windows.MessageBox.Show($"无法打开目录: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void LoadDiskDrives()
    {
        if (DiskDrives.Count == 0)
        {
            foreach (var disk in _scanService.GetAllDisks())
                DiskDrives.Add(disk.DriveLetter);
            if (DiskDrives.Count > 0) SelectedDrive = DiskDrives[0];
        }
    }

    #region Large Files Methods

    private async Task FindLargeFilesAsync()
    {
        IsFindingLargeFiles = true;
        LargeFiles.Clear();
        LargeFileStatus = Lang["Searching"];

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            var files = await _scanService.FindLargeFilesAsync(SelectedDrive + @"\", MinFileSizeBytes, _cts.Token);
            foreach (var f in files) LargeFiles.Add(f);
            LargeFileStatus = $"{Lang["Found"]} {files.Count} {Lang["FilesLargerThan"]} {MinFileSizeText}";
        }
        catch (OperationCanceledException) { LargeFileStatus = Lang["Cancelled"]; App.Log("Find large files cancelled"); }
        catch (Exception ex) { LargeFileStatus = ex.Message; App.LogError("FindLargeFilesAsync", ex); }
        finally { IsFindingLargeFiles = false; }
    }

    private async Task DeleteLargeFilesAsync()
    {
        var selected = LargeFiles.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        var totalSize = selected.Sum(f => f.SizeBytes);
        var sizeText = totalSize switch
        {
            >= 1_073_741_824 => $"{totalSize / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{totalSize / 1_048_576.0:F1} MB",
            _ => $"{totalSize / 1024.0:F1} KB"
        };

        var confirm = System.Windows.MessageBox.Show(
            $"确定要删除选中的 {selected.Count} 个文件吗？\n\n将释放 {sizeText} 空间\n\n删除后无法恢复！",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _cleanService.CleanLargeFilesAsync(selected, _cts.Token);
            LargeFileStatus = $"{Lang["Deleted"]} {result.FilesDeleted} {Lang["Files"]}, {Lang["Freed"]} {result.FreedText}";
            foreach (var f in selected) LargeFiles.Remove(f);
            _diskInfoService.Refresh("C:");
        }
        catch (Exception ex) { LargeFileStatus = ex.Message; App.LogError("DeleteLargeFilesAsync", ex); }
    }

    #endregion

    #region Large Folders Methods

    private async Task FindLargeFoldersAsync()
    {
        IsScanningFolders = true;
        LargeFolders.Clear();
        FolderScanStatus = "正在扫描文件夹...";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            var folders = await _folderScanService.ScanLargeFoldersAsync(SelectedDrive + @"\", 500 * 1024 * 1024, _cts.Token);
            foreach (var f in folders) LargeFolders.Add(f);
            FolderScanStatus = $"扫描完成，发现 {folders.Count} 个大文件夹";
        }
        catch (OperationCanceledException) { FolderScanStatus = "已取消"; App.Log("Large folder scan cancelled"); }
        catch (Exception ex) { FolderScanStatus = ex.Message; App.LogError("FindLargeFoldersAsync", ex); }
        finally { IsScanningFolders = false; }
    }

    #endregion

    #region Empty Folders Methods

    private async Task FindEmptyFoldersAsync()
    {
        IsScanningEmpty = true;
        EmptyFolders.Clear();
        EmptyFolderStatus = "正在扫描空文件夹...";
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            var folders = await _folderScanService.ScanEmptyFoldersAsync(SelectedDrive + @"\", _cts.Token);
            foreach (var f in folders) EmptyFolders.Add(f);
            EmptyFolderStatus = $"扫描完成，发现 {folders.Count} 个空文件夹";
        }
        catch (OperationCanceledException) { EmptyFolderStatus = "已取消"; App.Log("Empty folder scan cancelled"); }
        catch (Exception ex) { EmptyFolderStatus = ex.Message; App.LogError("FindEmptyFoldersAsync", ex); }
        finally { IsScanningEmpty = false; }
    }

    private async Task DeleteEmptyFoldersAsync()
    {
        var selected = EmptyFolders.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = System.Windows.MessageBox.Show($"确定删除 {selected.Count} 个空文件夹？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        await Task.Run(() =>
        {
            var deleted = _folderScanService.DeleteEmptyFolders(selected);
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var f in selected) EmptyFolders.Remove(f);
                EmptyFolderStatus = $"已删除 {deleted} 个空文件夹";
            }));
        });
    }

    #endregion

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Lang.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
