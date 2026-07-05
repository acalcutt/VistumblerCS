namespace Vistumbler.UI.ViewModels;

/// <summary>
/// One row in the Map settings "Offline Map Areas" list: a saved offline map
/// region (created from the map toolbar's "Save Map Area" button) with its
/// display name (from the region's metadata), zoom range, cached size, and
/// download status.
/// </summary>
public sealed record OfflineRegionRow(
    long Id,
    string Name,
    string ZoomRange,
    string SizeText,
    string StatusText);
