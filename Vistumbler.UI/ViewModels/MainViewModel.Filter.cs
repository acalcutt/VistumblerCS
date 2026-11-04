using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Models;

namespace Vistumbler.UI.ViewModels;

/// <summary>
/// The filter chosen in the Filters menu, as the original Vistumbler applies it: the AP list and the treeview show only
/// the APs it lets through, and the active count counts only those. The list re-checks an AP whenever a field a filter
/// can test changes (live filtering), so one that drops below a signal limit or goes dead leaves the list by itself.
/// </summary>
public partial class MainViewModel
{
    private FilterRecord? _activeFilter;
    private ListCollectionView? _apView;

    /// <summary>"Filter: name (shown of all)" while a filter is in use, otherwise empty.</summary>
    [ObservableProperty] private string _filterStatusText = string.Empty;

    // The AP fields a filter can test (FilterRecord.Matches)
    private static readonly string[] FilteredProperties =
    {
        nameof(AccessPointViewModel.Ssid), nameof(AccessPointViewModel.Bssid), nameof(AccessPointViewModel.Channel),
        nameof(AccessPointViewModel.Authentication), nameof(AccessPointViewModel.Encryption),
        nameof(AccessPointViewModel.RadioType), nameof(AccessPointViewModel.NetworkType),
        nameof(AccessPointViewModel.BasicTransferRates), nameof(AccessPointViewModel.OtherTransferRates),
        nameof(AccessPointViewModel.Signal), nameof(AccessPointViewModel.HighestSignal),
        nameof(AccessPointViewModel.Rssi), nameof(AccessPointViewModel.HighestRssi), nameof(AccessPointViewModel.IsActive),
    };

    private void InitializeFilter()
    {
        _apView = (ListCollectionView)CollectionViewSource.GetDefaultView(AccessPoints);
        _apView.IsLiveFiltering = true;
        foreach (var p in FilteredProperties) _apView.LiveFilteringProperties.Add(p);

        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.ActiveFilterId)) ApplyActiveFilter();
        };
        AvailableFilters.CollectionChanged += (_, _) => ApplyActiveFilter();
        AccessPoints.CollectionChanged += (_, _) => UpdateFilterStatus();
    }

    /// <summary>Whether the filter in use (if any) lets this AP through.</summary>
    public bool PassesFilter(AccessPointViewModel ap) => _activeFilter is null || _activeFilter.Matches(ap.ToModel());

    /// <summary>The filter in use, or null when there's none (or it lets every AP through).</summary>
    public FilterRecord? ActiveFilter => _activeFilter;

    /// <summary>Takes up the filter the Filters menu has ticked, and redraws the list and the treeview by it.</summary>
    private void ApplyActiveFilter()
    {
        var filter = AvailableFilters.FirstOrDefault(f => f.FiltId == _settings.ActiveFilterId);
        _activeFilter = filter is { IsEmpty: false } ? filter : null;
        OnPropertyChanged(nameof(ActiveFilter));
        if (_apView is not null)
            _apView.Filter = _activeFilter is null ? null : o => o is AccessPointViewModel ap && PassesFilter(ap);

        ClearTreeview();
        foreach (var ap in AccessPoints) AddApToTreeview(ap);
        ActiveApCount = CountActiveAps();
        UpdateFilterStatus();
    }

    /// <summary>The active APs the list shows.</summary>
    private int CountActiveAps() => AccessPoints.Count(ap => ap.IsActive && PassesFilter(ap));

    private void UpdateFilterStatus()
    {
        if (_activeFilter is null)
        {
            FilterStatusText = string.Empty;
            return;
        }
        int shown = AccessPoints.Count(PassesFilter);
        FilterStatusText = $"Filter: {_activeFilter.FiltName} ({shown} of {AccessPoints.Count})";
    }
}
