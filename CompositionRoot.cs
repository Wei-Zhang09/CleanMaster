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

        // 日志：复用 App 的静态 logger 实例，避免双重写入同一文件。
        services.AddSingleton<IAppLogger>(_ => App.Logger);

        // Singleton services (stateful: events, HttpClient, caches)
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILangService, LangService>();
        services.AddSingleton<DiskInfoService>();

        // Other singleton services
        services.AddSingleton<IScanService, ScanService>();
        services.AddSingleton<ICleanService, CleanService>();
        services.AddSingleton<ISoftwareService, SoftwareService>();
        services.AddSingleton<IFolderScanService, FolderScanService>();
        services.AddSingleton<ISystemCleanupService, SystemCleanupService>();
        services.AddSingleton<DuplicateService>();

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
            var scanService = sp.GetRequiredService<IScanService>();
            var langService = sp.GetRequiredService<ILangService>();
            return new SettingsViewModel(settingsService, scanService, langService);
        });

        return services.BuildServiceProvider();
    }
}
