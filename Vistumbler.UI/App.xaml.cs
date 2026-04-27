using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vistumbler.Core.Services;
using Vistumbler.Infrastructure.Data;
using Vistumbler.Infrastructure.Export;
using Vistumbler.Infrastructure.Gps;
using Vistumbler.Infrastructure.Import;
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
                // Register services
                services.AddSingleton<IWiFiScannerService, NativeWiFiScanner>();
                services.AddSingleton<IGpsService, SerialGpsService>();
                services.AddSingleton<IDatabaseService, SQLiteDatabaseService>();
                services.AddSingleton<IExportService, ExportService>();
                services.AddSingleton<IImportService, ImportService>();
                services.AddSingleton<ISoundService, SoundService>();

                // Register ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
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
