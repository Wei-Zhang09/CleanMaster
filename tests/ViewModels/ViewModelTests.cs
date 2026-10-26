using Moq;
using CleanMaster.Models;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;
using CleanMaster.ViewModels;

namespace CleanMaster.Tests.ViewModels;

public class CleanViewModelTests
{
    private readonly Mock<IScanService> _mockScanService;
    private readonly Mock<ICleanService> _mockCleanService;
    private readonly Mock<DiskInfoService> _mockDiskInfoService;
    private readonly CleanViewModel _viewModel;

    public CleanViewModelTests()
    {
        _mockScanService = new Mock<IScanService>();
        _mockCleanService = new Mock<ICleanService>();
        _mockDiskInfoService = new Mock<DiskInfoService>();
        _viewModel = new CleanViewModel(
            _mockScanService.Object,
            _mockCleanService.Object,
            _mockDiskInfoService.Object,
            new DuplicateService(),
            new LangService());
    }

    [Fact]
    public void InitialState_IsNotScanning()
    {
        Assert.False(_viewModel.IsScanning);
        Assert.True(_viewModel.CanScan);
    }

    [Fact]
    public void InitialState_IsNotCleaning()
    {
        Assert.False(_viewModel.IsCleaning);
    }

    [Fact]
    public void InitialState_HasEmptyScanResults()
    {
        Assert.Empty(_viewModel.ScanResults);
    }

    [Fact]
    public void InitialState_StatusTextIsReady()
    {
        Assert.False(string.IsNullOrEmpty(_viewModel.StatusText));
    }

    [Fact]
    public void CanScan_WhenScanning_ReturnsFalse()
    {
        _viewModel.IsScanning = true;
        Assert.False(_viewModel.CanScan);
    }

    [Fact]
    public void CanClean_WhenScanning_ReturnsFalse()
    {
        _viewModel.IsScanning = true;
        Assert.False(_viewModel.CanClean);
    }

    [Fact]
    public void CanClean_WhenNoResults_ReturnsFalse()
    {
        Assert.False(_viewModel.CanClean);
    }

    [Fact]
    public void TotalCleanableText_WhenZero_ShowsBytes()
    {
        Assert.Equal("0 B", _viewModel.TotalCleanableText);
    }

    [Fact]
    public void PropertyChanged_IsRaised_WhenIsScanningChanges()
    {
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CleanViewModel.IsScanning))
                propertyChangedRaised = true;
        };

        _viewModel.IsScanning = true;

        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void PropertyChanged_IsRaised_WhenStatusTextChanges()
    {
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CleanViewModel.StatusText))
                propertyChangedRaised = true;
        };

        _viewModel.StatusText = "Test";

        Assert.True(propertyChangedRaised);
    }

    /// <summary>构造一个分类并通过 CategoryScanned 事件送入 ViewModel，使其完成勾选联动订阅。</summary>
    private ScanCategoryResult RaiseCategory(int itemCount)
    {
        var cat = new ScanCategoryResult
        {
            Category = CleanCategory.TempFiles,
            DisplayName = "临时文件",
            IsSelected = true
        };

        for (int i = 0; i < itemCount; i++)
        {
            cat.Items.Add(new CleanableItem
            {
                Name = $"f{i}.tmp",
                FullPath = $@"C:\temp\f{i}.tmp",
                SizeBytes = 1024,
                Safety = CleanSafety.Safe,
                Category = CleanCategory.TempFiles
            });
        }

        _mockScanService.Raise(m => m.CategoryScanned += null, cat);
        return cat;
    }

    [Fact]
    public void CategoryUncheck_PropagatesToAllItems()
    {
        var cat = RaiseCategory(3);
        Assert.All(cat.Items, i => Assert.True(i.IsSelected));

        cat.IsSelected = false;

        Assert.All(cat.Items, i => Assert.False(i.IsSelected));
        Assert.Equal(0, _viewModel.SelectedCleanableSize);
    }

    [Fact]
    public void CategoryCheck_SelectsAllItems()
    {
        var cat = RaiseCategory(3);
        cat.IsSelected = false;

        cat.IsSelected = true;

        Assert.All(cat.Items, i => Assert.True(i.IsSelected));
        Assert.Equal(3 * 1024, _viewModel.SelectedCleanableSize);
    }

    [Fact]
    public void ItemPartialSelection_MakesCategoryIndeterminate()
    {
        var cat = RaiseCategory(3);

        cat.Items[0].IsSelected = false;

        Assert.Null(cat.IsSelected);   // 部分选中 → 三态 null
        Assert.Equal(2, cat.Items.Count(i => i.IsSelected));
        Assert.Equal(2 * 1024, _viewModel.SelectedCleanableSize);
    }

    [Fact]
    public void ItemAllUnselected_MakesCategoryFalse()
    {
        var cat = RaiseCategory(2);

        foreach (var item in cat.Items) item.IsSelected = false;

        Assert.False(cat.IsSelected);
    }

    [Fact]
    public void ItemAllSelected_MakesCategoryTrue()
    {
        var cat = RaiseCategory(2);
        cat.IsSelected = false;

        foreach (var item in cat.Items) item.IsSelected = true;

        Assert.True(cat.IsSelected);
    }
}

public class SoftwareViewModelTests
{
    private readonly Mock<ISoftwareService> _mockSoftwareService;
    private readonly SoftwareViewModel _viewModel;

    public SoftwareViewModelTests()
    {
        _mockSoftwareService = new Mock<ISoftwareService>();
        _viewModel = new SoftwareViewModel(_mockSoftwareService.Object, new LangService());
    }

    [Fact]
    public void InitialState_IsNotLoading()
    {
        Assert.False(_viewModel.IsLoadingSoftware);
    }

    [Fact]
    public void InitialState_HasEmptySoftwareList()
    {
        Assert.Empty(_viewModel.InstalledSoftware);
    }

    [Fact]
    public void InstalledSoftware_CanAddItems()
    {
        var software = new InstalledSoftware { Name = "Test Software" };
        _viewModel.InstalledSoftware.Add(software);

        Assert.Single(_viewModel.InstalledSoftware);
        Assert.Equal("Test Software", _viewModel.InstalledSoftware[0].Name);
    }

    [Fact]
    public void SelectedSoftware_CanBeSet()
    {
        var software = new InstalledSoftware { Name = "Test" };
        _viewModel.SelectedSoftware = software;

        Assert.Equal(software, _viewModel.SelectedSoftware);
    }

    [Fact]
    public void PropertyChanged_IsRaised_WhenIsLoadingSoftwareChanges()
    {
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SoftwareViewModel.IsLoadingSoftware))
                propertyChangedRaised = true;
        };

        _viewModel.IsLoadingSoftware = true;

        Assert.True(propertyChangedRaised);
    }
}

public class SettingsViewModelTests
{
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly Mock<IScanService> _mockScanService;
    private readonly Mock<IUpdateService> _mockUpdateService;
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        _mockSettingsService = new Mock<ISettingsService>();
        _mockScanService = new Mock<IScanService>();
        _mockUpdateService = new Mock<IUpdateService>();

        _mockSettingsService.Setup(s => s.Get()).Returns(new AppSettings());
        _mockScanService.Setup(s => s.GetAllDisks())
            .Returns(new List<DiskInfo> { new() { DriveLetter = "C:" } });
        _mockUpdateService.Setup(u => u.CheckForUpdateAsync())
            .ReturnsAsync((UpdateInfo?)null);

        _viewModel = new SettingsViewModel(
            _mockSettingsService.Object,
            _mockScanService.Object,
            new LangService(),
            _mockUpdateService.Object);
    }

    [Fact]
    public void InitialState_HasLanguageSetting()
    {
        Assert.NotNull(_viewModel.Lang);
    }

    [Fact]
    public void WebsiteUrl_CanBeSet()
    {
        _viewModel.WebsiteUrl = "https://example.com";

        Assert.Equal("https://example.com", _viewModel.WebsiteUrl);
    }
}
