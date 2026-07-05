using CommunityToolkit.Mvvm.ComponentModel;

namespace Vistumbler.UI.ViewModels;

/// <summary>
/// One row in the Map settings color table: a "bucket" (the live scan's active/dead
/// pseudo-buckets, or a WifiDB history age bucket) together with the circle colors used
/// for its Open, WEP and Secure access points. Each color is a 6-char hex string without
/// a leading '#', matching the Misc-tab color fields and <c>HexColorToBrushConverter</c>.
/// </summary>
public partial class MapBucketColorRow : ObservableObject
{
    /// <summary>Renderer bucket key (e.g. "live_active", "daily", "10yrplus").</summary>
    public string BucketKey { get; }

    /// <summary>Human-friendly row label shown in the settings table.</summary>
    public string DisplayName { get; }

    [ObservableProperty] private string _openHex;
    [ObservableProperty] private string _wepHex;
    [ObservableProperty] private string _secureHex;

    public MapBucketColorRow(string bucketKey, string displayName,
        string openHex, string wepHex, string secureHex)
    {
        BucketKey   = bucketKey;
        DisplayName = displayName;
        _openHex    = openHex;
        _wepHex     = wepHex;
        _secureHex  = secureHex;
    }
}
