using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vistumbler.Core.Services;
using Vistumbler.Infrastructure.Data;
using Vistumbler.Infrastructure.Export;
using Vistumbler.Infrastructure.Gps;
using Vistumbler.Infrastructure.Import;
using Vistumbler.Infrastructure.Settings;
using Vistumbler.Infrastructure.Sound;
using Vistumbler.Infrastructure.WiFi;
using Vistumbler.UI.ViewModels;
using Vistumbler.UI.Views;

namespace Vistumbler.UI;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<ISettingsService, IniSettingsService>();

                // Register services
                services.AddSingleton<IWiFiScannerService, NativeWiFiScanner>();
                services.AddSingleton<SerialGpsService>();
                services.AddSingleton<WindowsLocationGpsService>();
                services.AddSingleton<GpsServiceRouter>();
                services.AddSingleton<IGpsService>(sp => sp.GetRequiredService<GpsServiceRouter>());
                services.AddSingleton<IDatabaseService, SQLiteDatabaseService>();
                services.AddSingleton<IExportService, ExportService>();
                services.AddSingleton<IImportService, ImportService>();
                services.AddSingleton<ISoundService, SoundService>();

                // Register ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddTransient<ImportViewModel>();
                services.AddTransient<GpsDetailsViewModel>();

                // Register Views
                services.AddSingleton<MainWindow>();
                services.AddTransient<ImportWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        await _host.StartAsync();

        // Load persisted settings before showing any window
        var settings = _host.Services.GetRequiredService<SettingsViewModel>();
        settings.LoadSettings();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (_host)
        {
            await _host.StopAsync();
        }

        base.OnExit(e);
    }
}
