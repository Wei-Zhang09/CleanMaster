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
            _mockDiskInfoService.Object);
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
    public void TotalCleanableText_WhenZero_ShowsKB()
    {
        Assert.Contains("KB", _viewModel.TotalCleanableText);
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
}

public class SoftwareViewModelTests
{
    private readonly Mock<ISoftwareService> _mockSoftwareService;
    private readonly SoftwareViewModel _viewModel;

    public SoftwareViewModelTests()
    {
        _mockSoftwareService = new Mock<ISoftwareService>();
        _viewModel = new SoftwareViewModel(_mockSoftwareService.Object);
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
    private readonly Mock<ILicenseService> _mockLicenseService;
    private readonly Mock<IScanService> _mockScanService;
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        _mockSettingsService = new Mock<ISettingsService>();
        _mockLicenseService = new Mock<ILicenseService>();
        _mockScanService = new Mock<IScanService>();

        _mockSettingsService.Setup(s => s.Get()).Returns(new AppSettings());
        _mockLicenseService.Setup(l => l.CheckActivationAsync())
            .ReturnsAsync((false, "Not activated"));
        _mockScanService.Setup(s => s.GetAllDisks())
            .Returns(new List<DiskInfo> { new() { DriveLetter = "C:" } });

        _viewModel = new SettingsViewModel(
            _mockSettingsService.Object,
            _mockLicenseService.Object,
            _mockScanService.Object);
    }

    [Fact]
    public void InitialState_HasLanguageSetting()
    {
        Assert.NotNull(_viewModel.Lang);
    }

    [Fact]
    public void IsChinese_CanBeToggled()
    {
        var initial = _viewModel.IsChinese;
        _viewModel.IsChinese = !initial;

        Assert.NotEqual(initial, _viewModel.IsChinese);
    }

    [Fact]
    public void WebsiteUrl_CanBeSet()
    {
        _viewModel.WebsiteUrl = "https://example.com";

        Assert.Equal("https://example.com", _viewModel.WebsiteUrl);
    }

    [Fact]
    public void PropertyChanged_IsRaised_WhenIsActivatedChanges()
    {
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsActivated))
                propertyChangedRaised = true;
        };

        _viewModel.IsActivated = true;

        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void LicenseStatusColor_WhenActivated_ReturnsGreen()
    {
        _viewModel.IsActivated = true;

        Assert.Equal("#10B981", _viewModel.LicenseStatusColor);
    }

    [Fact]
    public void LicenseStatusColor_WhenNotActivated_ReturnsYellow()
    {
        _viewModel.IsActivated = false;

        Assert.Equal("#F59E0B", _viewModel.LicenseStatusColor);
    }
}
