using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Vistumbler.UI.Views;

public enum SessionPickerAction { NewSession, Resume, Exit }

public record SessionPickerResult(SessionPickerAction Action, string? SelectedPath = null);

/// <summary>
/// Shown on startup when existing session databases are found.
/// Lets the user resume a previous session, start a new one, or delete old files.
/// </summary>
public partial class SessionPickerWindow : Window
{
    public SessionPickerResult Result { get; private set; } = new(SessionPickerAction.NewSession);

    public SessionPickerWindow(IReadOnlyList<string> dbPaths)
    {
        InitializeComponent();

        foreach (var path in dbPaths)
        {
            var info = new FileInfo(path);
            SessionList.Items.Add(new SessionEntry(
                info.Name,
                info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                FormatSize(info.Length),
                path));
        }

        if (SessionList.Items.Count > 0)
            SessionList.SelectedIndex = 0;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024         => $"{bytes} B",
        < 1024 * 1024  => $"{bytes / 1024} KB",
        _              => $"{bytes / (1024 * 1024):F1} MB",
    };

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is SessionEntry entry)
        {
            Result = new SessionPickerResult(SessionPickerAction.Resume, entry.FullPath);
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("Please select a session to resume.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void NewSession_Click(object sender, RoutedEventArgs e)
    {
        Result = new SessionPickerResult(SessionPickerAction.NewSession);
        DialogResult = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Result = new SessionPickerResult(SessionPickerAction.Exit);
        DialogResult = false;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionEntry entry) return;

        var confirm = MessageBox.Show(
            $"Delete session file '{entry.FileName}'?\nThis cannot be undone.",
            "Delete Session",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            File.Delete(entry.FullPath);
            SessionList.Items.Remove(entry);
            if (SessionList.Items.Count > 0)
                SessionList.SelectedIndex = 0;
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Could not delete file: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SessionList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SessionList.SelectedItem is SessionEntry)
            Resume_Click(sender, e);
    }

    // Simple data record for the ListView rows.
    private record SessionEntry(string FileName, string Modified, string Size, string FullPath);
}
