using System.Windows;

namespace Vistumbler.UI.Views;

public partial class WifiDbUploadWindow : Window
{
    public string UserName    => UserBox.Text.Trim();
    public string OtherUsers  => OtherUsersBox.Text.Trim();
    public string ApiKey      => ApiKeyBox.Text.Trim();
    public string UploadTitle => TitleBox.Text.Trim();
    public string Notes       => NotesBox.Text.Trim();

    /// <summary>"VS1" or "CSV".</summary>
    public string FileType => CsvRadio.IsChecked == true ? "CSV" : "VS1";

    public WifiDbUploadWindow(string user, string apiKey, string defaultTitle)
    {
        InitializeComponent();
        UserBox.Text   = user;
        ApiKeyBox.Text = apiKey;
        TitleBox.Text  = defaultTitle;
    }

    private void Upload_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
