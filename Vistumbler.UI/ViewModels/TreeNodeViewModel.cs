using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vistumbler.UI.ViewModels;

/// <summary>
/// A node in the NetStumbler-style filter treeview.
///
/// Three node roles:
///   Root  – one of the five category headers (Authentication, Channel, …)
///           GroupKey = string.Empty, AccessPoint = null
///   Group – a unique value within a category (e.g. "006", "WPA2-PSK")
///           GroupKey = the value, AccessPoint = null
///   Leaf  – a single AP under a group
///           GroupKey = same as parent group, AccessPoint = the AP VM
/// </summary>
public partial class TreeNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isExpanded = false;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The grouping key this node belongs to (empty for root nodes).</summary>
    public string GroupKey { get; init; } = string.Empty;

    /// <summary>Non-null only for leaf (AP) nodes.</summary>
    public AccessPointViewModel? AccessPoint { get; init; }

    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();
}
