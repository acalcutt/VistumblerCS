using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.UI.Views;

/// <summary>
/// Filters → Add / Remove Filters: the saved filters on the left, the chosen one's fields on the right. Edits are
/// made on a copy and kept with Save Filter; the main window reloads the Filters menu when this closes.
/// </summary>
public partial class FiltersWindow : Window
{
    private readonly IDatabaseService _db;
    private FilterRecord? _editing;   // a copy of the chosen filter, what the fields are bound to

    public FiltersWindow(IDatabaseService db)
    {
        InitializeComponent();
        _db = db;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync(int selectId = 0)
    {
        var filters = await _db.GetAllFiltersAsync();
        FilterList.ItemsSource = filters;
        FilterList.SelectedItem = filters.FirstOrDefault(f => f.FiltId == selectId);
        if (FilterList.SelectedItem is null) Edit(null);
    }

    private void FilterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => Edit(FilterList.SelectedItem as FilterRecord);

    private void Edit(FilterRecord? filter)
    {
        _editing = filter is null ? null : JsonSerializer.Deserialize<FilterRecord>(JsonSerializer.Serialize(filter));
        Editor.DataContext = _editing;
        Editor.IsEnabled = _editing is not null;
        DeleteButton.IsEnabled = filter is not null;
        SavedText.Text = string.Empty;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        int id = await _db.UpsertFilterAsync(new FilterRecord { FiltName = "New filter" });
        await ReloadAsync(id);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (FilterList.SelectedItem is not FilterRecord filter) return;
        if (MessageBox.Show(this, $"Delete the filter \"{filter.FiltName}\"?", "Filters",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _db.DeleteFilterAsync(filter.FiltId);
        await ReloadAsync();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        // An empty field is "any", as "*"
        foreach (var p in typeof(FilterRecord).GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanWrite))
        {
            if (p.Name is nameof(FilterRecord.FiltName) or nameof(FilterRecord.FiltDesc)) continue;
            if (string.IsNullOrWhiteSpace((string?)p.GetValue(_editing))) p.SetValue(_editing, "*");
        }
        if (string.IsNullOrWhiteSpace(_editing.FiltName)) _editing.FiltName = "Filter " + _editing.FiltId;

        int id = await _db.UpsertFilterAsync(_editing);
        await ReloadAsync(id);
        SavedText.Text = "Saved";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
