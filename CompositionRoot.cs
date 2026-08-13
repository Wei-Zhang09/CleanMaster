using Microsoft.Extensions.DependencyInjection;
using CleanMaster.Services;
using CleanMaster.Services.Interfaces;
using CleanMaster.ViewModels;

namespace CleanMaster;

public static class CompositionRoot
{
    public static IServiceProvider Configure()
    {
        var services = new ServiceCollection();

        // Singleton services (stateful: events, HttpClient, caches)
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IMachineIdService, MachineIdService>();
        services.AddSingleton<ILangService, LangService>();
        services.AddSingleton<DiskInfoService>();

        // LicenseService depends on SettingsService + MachineIdService
        services.AddSingleton<ILicenseService>(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var machineIdService = sp.GetRequiredService<IMachineIdService>();
            return new LicenseService(settingsService, machineIdService);
        });

        // Other singleton services
        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<ICleanService, CleanService>();
        services.AddSingleton<ISoftwareService, SoftwareService>();
        services.AddSingleton<IFolderScanService, FolderScanService>();
        services.AddSingleton<ISystemCleanupService, SystemCleanupService>();

        // Transient ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<CleanViewModel>();
        services.AddTransient<DiskFilesViewModel>();
        services.AddTransient<SoftwareViewModel>();
        services.AddTransient<StartupViewModel>();
        services.AddTransient<SystemCleanupViewModel>();
        services.AddTransient<SettingsViewModel>(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var licenseService = sp.GetRequiredService<ILicenseService>();
            var scanService = sp.GetRequiredService<IScanService>();
            var langService = sp.GetRequiredService<ILangService>();
            return new SettingsViewModel(settingsService, licenseService, scanService, langService);
        });

        return services.BuildServiceProvider();
    }
}
