using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Vistumbler.Core.Services;
using Vistumbler.UI.Services;

namespace Vistumbler.UI.Views;

/// <summary>
/// Shows a newer release and its notes. An installed copy downloads the matching setup.exe and runs it in update
/// mode; a portable copy is sent to the release page.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private readonly HttpClient _http;
    private readonly ReleaseAsset? _installer;
    private CancellationTokenSource? _download;

    /// <summary>True once the installer is running; the caller then exits the app.</summary>
    public bool InstallerStarted { get; private set; }

    public UpdateWindow(UpdateCheckResult result, HttpClient http)
    {
        InitializeComponent();
        _release = result.Update!;
        _http = http;

        HeadingText.Text = $"{AppUpdater.ProductName} {_release.Version} is available";
        VersionText.Text = $"You have version {result.CurrentVersion}." +
                           (_release.Version.IsPrerelease ? " This update is a pre-release." : "");
        NotesBox.Text = string.IsNullOrWhiteSpace(_release.Notes)
            ? "No release notes were published for this version."
            : _release.Notes.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        if (WindowsUpdateInstaller.IsInstalledCopy)
            _installer = _release.FindAsset(WindowsUpdateInstaller.InstallerSuffix);

        if (_installer is not null)
        {
            PrimaryButton.Content = "Install and Restart";
            InfoText.Text = $"{AppUpdater.ProductName} will close while the update installs, then start again. " +
                            "Your current session is kept, so you can resume it.";
        }
        else
        {
            PrimaryButton.Content = "Open Download Page";
            ReleasePageButton.Visibility = Visibility.Collapsed;
            InfoText.Text = WindowsUpdateInstaller.IsInstalledCopy
                ? "This release has no installer for your PC yet. Download it from the release page."
                : "This copy wasn't installed with the setup program, so download the new version from the release page.";
        }
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_installer is null)
        {
            OpenUrl(_release.PageUrl);
            Close();
            return;
        }

        PrimaryButton.IsEnabled = false;
        ReleasePageButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        StatusText.Text = $"Downloading {_installer.Name}...";
        _download = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => DownloadProgress.Value = p);
            var path = await WindowsUpdateInstaller.DownloadAsync(_http, _installer, AppUpdater.ProductName, progress, _download.Token);

            StatusText.Text = "Checking the installer's signature...";
            WindowsUpdateInstaller.VerifyPublisher(path);

            StatusText.Text = "Starting the installer...";
            if (!WindowsUpdateInstaller.Launch(path))
            {
                StatusText.Text = "The update was cancelled.";
                PrimaryButton.IsEnabled = true;
                ReleasePageButton.IsEnabled = true;
                return;
            }
            InstallerStarted = true;
            Close();
        }
        catch (OperationCanceledException) when (_download.IsCancellationRequested)
        {
            // Later was pressed during the download; the window is closing
        }
        catch (Exception ex)
        {
            StatusText.Text = $"The update failed: {ex.Message}";
            PrimaryButton.IsEnabled = true;
            ReleasePageButton.IsEnabled = true;
        }
        finally
        {
            _download?.Dispose();
            _download = null;
        }
    }

    private void ReleasePage_Click(object sender, RoutedEventArgs e) => OpenUrl(_release.PageUrl);

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e) => _download?.Cancel();

    private static void OpenUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
