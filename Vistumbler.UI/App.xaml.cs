using System.IO;
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
                services.AddTransient<ImportFolderViewModel>();
                services.AddTransient<GpsDetailsViewModel>();

                // Register Views
                services.AddSingleton<MainWindow>();
                services.AddTransient<ImportWindow>();
                services.AddTransient<ImportFolderWindow>();
            })
            .Build();
    }

    /// <summary>Where an unhandled exception gets written, beside the sessions.</summary>
    private static string CrashLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Vistumbler", "crash.log");

    /// <summary>
    /// Record an unhandled exception before the process goes.
    /// </summary>
    /// <remarks>
    /// Nothing was catching these, so a crash left no trace at all: WPF tears the
    /// process down, and Windows Error Reporting only archives a report for a native
    /// fault, not for a managed exception escaping the dispatcher. The result was
    /// crashes with nothing to go on -- no stack, no message, no file. This does not
    /// change what happens (the app still terminates), it just leaves the stack behind.
    ///
    /// Appends rather than overwrites: an intermittent crash is much easier to place
    /// when the previous ones are still there to compare against.
    /// </remarks>
    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var text =
                $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {source} ====={Environment.NewLine}" +
                (ex?.ToString() ?? "(no exception object)") + Environment.NewLine + Environment.NewLine;
            File.AppendAllText(CrashLogPath, text);
        }
        catch
        {
            // Logging a crash must never cause one.
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Before anything else, so a failure during startup is recorded too.
        DispatcherUnhandledException += (_, args) => LogCrash("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("UnobservedTaskException", args.Exception);
            args.SetObserved();   // already logged; do not escalate a background fault
        };

        await _host.StartAsync();

        // Load persisted settings before showing any window
        var settings = _host.Services.GetRequiredService<SettingsViewModel>();
        settings.LoadSettings();

        // ── Session selection ────────────────────────────────────────────────
        // Use OnExplicitShutdown so that closing the picker window (the only
        // window at this point) does not trigger application shutdown.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mainVm = _host.Services.GetRequiredService<MainViewModel>();
        Directory.CreateDirectory(MainViewModel.SessionsFolder);

        bool forceNew = System.Array.Exists(e.Args,
            a => string.Equals(a, "--new-session", StringComparison.OrdinalIgnoreCase));

        var existing = forceNew ? [] : MainViewModel.FindExistingSessions();
        string dbPath;

        if (existing.Count > 0)
        {
            var picker = new Views.SessionPickerWindow(existing);
            picker.ShowDialog();

            if (picker.Result.Action == Views.SessionPickerAction.Exit)
            {
                Shutdown();
                return;
            }

            dbPath = picker.Result.Action == Views.SessionPickerAction.Resume
                     && picker.Result.SelectedPath is not null
                ? picker.Result.SelectedPath
                : MainViewModel.NewSessionPath();
        }
        else
        {
            dbPath = MainViewModel.NewSessionPath();
        }

        // Initialize the database (sets window title) before the window is shown.
        await mainVm.InitializeWithPathAsync(dbPath);

        // Wire up session-file cleanup on normal window close.
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();

        // Restore normal shutdown behaviour, then show the main window.
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Close the DB and clean up the session file on every exit path
        // (X button, File > Exit, File > Exit (Save DB), etc.).
        //
        // OnExit must be synchronous here: WPF does NOT await an `async void`
        // OnExit, so the process would terminate at the first real await
        // (inside CloseAsync) before the file delete ran. We block on the
        // cleanup via Task.Run so the continuations resume on the thread pool
        // (avoiding a UI-thread deadlock) while OnExit waits for completion.
        var mainVm = _host.Services.GetRequiredService<MainViewModel>();
        Task.Run(() => mainVm.CloseSessionAsync()).GetAwaiter().GetResult();

        using (_host)
        {
            Task.Run(() => _host.StopAsync()).GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }
}
