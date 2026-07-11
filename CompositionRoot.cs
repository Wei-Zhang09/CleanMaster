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
        services.AddSingleton<ILangService>(_ => LangService.Instance);
        services.AddSingleton<DiskInfoService>();

        // LicenseService depends on SettingsService
        services.AddSingleton<ILicenseService>(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            return new LicenseService(settingsService);
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
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
