using System.Net.Http;
using System.Reflection;
using System.Windows;
using Vistumbler.Core.Services;
using Vistumbler.UI.ViewModels;
using Vistumbler.UI.Views;

namespace Vistumbler.UI.Services;

/// <summary>
/// Checks the release feeds (GitLab, then GitHub) for a newer VistumblerCS and offers to install it. Runs from
/// Help > Check for Updates, and at startup when "Automatically Check For Updates" is on. "Check For Beta Updates"
/// includes pre-releases (e.g. 0.5.0-rc.1).
/// </summary>
public sealed class AppUpdater
{
    public const string ProductName = "VistumblerCS";

    // Also downloads the installer, so no short overall timeout; the feed check has its own
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly SettingsViewModel _settings;
    private readonly UpdateService _service;
    private bool _checking;

    public AppUpdater(SettingsViewModel settings)
    {
        _settings = settings;
        _service = new UpdateService(Http, ProductName, CurrentVersion, "techidiots-llc/VistumblerCS", "acalcutt/VistumblerCS");
    }

    public static string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <param name="interactive">
    /// True from the menu: report "up to date" and errors. False at startup: stay quiet unless there is an update.
    /// </param>
    /// <param name="exitForUpdate">Closes the app, keeping the session, once the installer has started.</param>
    public async Task CheckAsync(bool interactive, Action exitForUpdate)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var installer = WindowsUpdateInstaller.IsInstalledCopy ? WindowsUpdateInstaller.InstallerSuffix : null;
            var result = await _service.CheckAsync(_settings.CheckForBetaUpdates, installer);
            if (result.Update is null)
            {
                if (interactive)
                    MessageBox.Show(Application.Current.MainWindow,
                        $"You're running the latest version of {ProductName} ({result.CurrentVersion}).",
                        "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new UpdateWindow(result, Http) { Owner = Application.Current.MainWindow };
            window.ShowDialog();
            if (window.InstallerStarted) exitForUpdate();
        }
        catch (Exception ex) when (interactive)
        {
            MessageBox.Show(Application.Current.MainWindow, ex.Message, "Check for Updates",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            // Startup check: offline or a feed outage shouldn't interrupt the user
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }
}
