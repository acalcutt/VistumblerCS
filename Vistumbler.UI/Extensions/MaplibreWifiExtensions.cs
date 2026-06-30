using MapLibreNative.Maui.WPF;

namespace Vistumbler.UI.Extensions;

/// <summary>
/// Extension methods for MlnMapHost that provide WiFi/cell-tower vector and GeoJSON
/// layer handling. Color and radius graduation matches WifiDB's map.php and
/// VistumblerMAUI's MapViewModel; z-order keeps newest tiers on top.
///
/// circle-color uses the property-function form (<c>{"property":"sectype",
/// "stops":[[1,open],[2,wep],[3,sec]]}</c>). Earlier investigation suspected this
/// maplibre-native build couldn't render data-driven (per-feature) paint properties
/// at all — that was wrong. A minimal repro in maplibre-maui-ac's WPF sample app
/// (see its investigate/runtime-data-driven-circle-color branch) proved property+
/// stops, case and match expressions all render correctly via this exact runtime
/// AddCircleLayer API. The real bug was server-side: WifiDB's out/tiles/.htaccess
/// used "Header always set Content-Encoding gzip", which Apache also applies to
/// its own 404 HTML error page for any tile with no data (the common case —
/// most of the world has none). This client correctly gzip-decodes the
/// Content-Encoding it's told, gets HTML instead, fails ("incorrect header
/// check"), and silently drops that tile's geometry — indistinguishable from a
/// rendering bug regardless of how circle-color is set. Fixed upstream in WifiDB
/// by dropping "always" so the header only applies to genuine 2xx/3xx responses.
/// </summary>
public static class MaplibreWifiExtensions
{
    // ── Per-bucket style ──────────────────────────────────────────────────────
    // Colors graduate from bright (newest) to dark/muted (oldest); radius shrinks
    // the same way. Cell buckets use a single graduated purple (no sectype split —
    // cells use `type` for LTE/GSM/CDMA etc., not an open/WEP/secure split), so
    // Wep/Secure just repeat Open for those entries.
    private record BucketStyle(string Open, string Wep, string Secure, double Radius);

    private static readonly BucketStyle DefaultStyle = new("#1aff66", "#ffad33", "#ff1a1a", 3.0);

    private static readonly Dictionary<string, BucketStyle> BucketStyles = new()
    {
        ["daily"]          = new("#1aff66", "#ffad33", "#ff1a1a", 3.0),
        ["weekly"]         = new("#1aff66", "#ffad33", "#ff1a1a", 3.0),
        ["monthly"]        = new("#1aff66", "#ffad33", "#ff1a1a", 3.0),
        ["0to1year"]       = new("#1aff66", "#ffad33", "#ff1a1a", 3.0),
        ["1to2year"]       = new("#00e64d", "#ff9900", "#e60000", 2.75),
        ["2to3year"]       = new("#00b33c", "#e68a00", "#cc0000", 2.5),
        ["3to5year"]       = new("#009933", "#d98000", "#c00000", 2.25),
        ["5to10year"]      = new("#00802b", "#cc7a00", "#b30000", 2.0),
        ["10yrplus"]       = new("#005c1f", "#996000", "#800000", 1.5),
        ["cell_daily"]     = new("#b296e3", "#b296e3", "#b296e3", 3.0),
        ["cell_weekly"]    = new("#9d78d8", "#9d78d8", "#9d78d8", 3.0),
        ["cell_monthly"]   = new("#885fcd", "#885fcd", "#885fcd", 3.0),
        ["cell_0to1year"]  = new("#885fcd", "#885fcd", "#885fcd", 3.0),
        ["cell_1to2year"]  = new("#7a4dc0", "#7a4dc0", "#7a4dc0", 2.75),
        ["cell_2to3year"]  = new("#6f40b3", "#6f40b3", "#6f40b3", 2.5),
        ["cell_3to5year"]  = new("#5e3599", "#5e3599", "#5e3599", 2.25),
        ["cell_5to10year"] = new("#4d2b80", "#4d2b80", "#4d2b80", 2.0),
        ["cell_10yrplus"]  = new("#3d2266", "#3d2266", "#3d2266", 1.5),
    };

    // Canonical z-order, newest → oldest, wifi tiers then cell tiers.
    // "live_aps" (the local Vistumbler scan layer) is always the top anchor.
    private static readonly string[] BucketOrder =
    [
        "daily", "weekly", "monthly", "0to1year", "1to2year",
        "2to3year", "3to5year", "5to10year", "10yrplus",
        "cell_daily", "cell_weekly", "cell_monthly", "cell_0to1year",
        "cell_1to2year", "cell_2to3year", "cell_3to5year",
        "cell_5to10year", "cell_10yrplus",
    ];

    // bucket → currently-added layer name, so BelowLayerFor() can find the
    // nearest newer bucket already present in the style regardless of toggle order.
    private static readonly Dictionary<string, string> _activeLayersByBucket = new();

    /// Returns the layerId to insert below for the given bucket: the nearest
    /// newer bucket already present in the style, or "live_aps" as the top anchor.
    private static string BelowLayerFor(string bucket)
    {
        int idx = Array.IndexOf(BucketOrder, bucket);
        if (idx <= 0) return "live_aps";
        for (int i = idx - 1; i >= 0; i--)
        {
            if (_activeLayersByBucket.TryGetValue(BucketOrder[i], out var layerName))
                return layerName;
        }
        return "live_aps";
    }

    // Property function (no "type" — defaults to Exponential, the form confirmed
    // by dependencies/maplibre-native's own passing render tests for circle-color).
    // sectype is always exactly 1/2/3 (never fractional), so the implicit
    // interpolation between stops never actually blends colors in practice.
    private static Dictionary<string, object?> SeCtypeStopsExpr(BucketStyle s) => new()
    {
        ["property"] = "sectype",
        ["stops"]    = new object[] {
            new object[] { 1, s.Open },
            new object[] { 2, s.Wep },
            new object[] { 3, s.Secure },
        },
    };

    // MapLibre GL zoom-interpolated radius function.
    // Holds at baseRadius from the lowest zoom up through z12 (never shrinks below
    // the per-bucket size, unlike a curve that scales down at low zoom — at z1 a
    // 1.5px dot blurred by circle-blur=0.5 is effectively invisible on a desktop-size
    // viewport showing a whole wardriving area), then grows up to 20px by z20 so
    // dots stay easy to tap/click at street level.
    private static Dictionary<string, object?> RadiusExpr(double baseRadius) => new()
    {
        ["base"]  = 1.5,
        ["stops"] = new object[] {
            new object[] { 1,  baseRadius },
            new object[] { 12, baseRadius },
            new object[] { 20, 20.0 },
        },
    };

    // ── GeoJSON-backed live layer ────────────────────────────────────────────

    /// <summary>
    /// Add or update a GeoJSON source that fetches from a URL, with a single
    /// sectype-coloured circle layer (open/WEP/secure).
    /// </summary>
    public static void SetWifiGeoJsonLayer(this MlnMapHost map, string sourceId, string geoJsonUrl)
    {
        map.AddGeoJsonSourceUrl(sourceId, geoJsonUrl);
        AddWifiCircleLayer(map, sourceId, sourceId, sourceLayer: null, belowLayerId: null);
    }

    /// <summary>
    /// Update an existing GeoJSON source with inline GeoJSON string data.
    /// Use this for live/frequently-updated data to avoid HTTP round-trips.
    /// If the source does not exist yet, it is created (with its circle layer).
    /// </summary>
    public static void SetWifiGeoJsonLayerData(this MlnMapHost map, string sourceId, string geoJson)
    {
        map.AddGeoJsonSource(sourceId, geoJson);
        AddWifiCircleLayer(map, sourceId, sourceId, sourceLayer: null, belowLayerId: null);
    }

    /// <summary>Remove a GeoJSON wifi layer (circle layer + source).</summary>
    public static void RemoveWifiGeoJsonLayer(this MlnMapHost map, string sourceId)
    {
        map.RemoveLayer(sourceId);
        map.RemoveSource(sourceId);
    }

    // ── Vector tile layers (history buckets) ─────────────────────────────────

    /// <summary>
    /// Add a vector tile source from a TileJSON URL with a single sectype-coloured
    /// circle layer. <paramref name="bucket"/> must match the layer name inside the
    /// MVT tiles (tilejson.php uses the bucket name, e.g. "weekly") and selects the
    /// per-bucket color/radius and the layer's position in the z-order stack.
    /// </summary>
    public static void SetWifiVectorLayer(this MlnMapHost map, string sourceId, string tileJsonUrl, string bucket)
    {
        map.AddVectorSourceUrl(sourceId, tileJsonUrl);
        AddWifiCircleLayer(map, sourceId, sourceId, bucket, BelowLayerFor(bucket));
        _activeLayersByBucket[bucket] = sourceId;
    }

    /// <summary>Remove a wifi vector layer (circle layer + source) for the given bucket.</summary>
    public static void RemoveWifiVectorLayer(this MlnMapHost map, string sourceId, string bucket)
    {
        map.RemoveLayer(sourceId);
        map.RemoveSource(sourceId);
        _activeLayersByBucket.Remove(bucket);
    }

    private static void AddWifiCircleLayer(MlnMapHost map, string layerName, string sourceName,
        string? sourceLayer, string? belowLayerId)
    {
        var style = sourceLayer != null && BucketStyles.TryGetValue(sourceLayer, out var s) ? s : DefaultStyle;
        map.AddCircleLayer(
            layerName:    layerName,
            sourceName:   sourceName,
            belowLayerId: belowLayerId,
            sourceLayer:  sourceLayer,
            properties: new Dictionary<string, object?>
            {
                ["circle-color"]   = SeCtypeStopsExpr(style),
                ["circle-radius"]  = RadiusExpr(style.Radius),
                ["circle-opacity"] = 1.0,
                ["circle-blur"]    = 0.5,
            });
    }

    // ── Cell tower layers ─────────────────────────────────────────────────────

    /// <summary>
    /// Add a vector tile source from a TileJSON URL with a single graduated-purple
    /// circle layer for cell towers. Cell tiles use <c>type</c> (LTE/GSM/etc.) instead
    /// of <c>sectype</c>, so only one literal color (no security-type split) is used
    /// per bucket.
    /// </summary>
    public static void SetCellVectorLayer(this MlnMapHost map, string sourceId, string tileJsonUrl, string bucket)
    {
        map.AddVectorSourceUrl(sourceId, tileJsonUrl);
        var style = BucketStyles.TryGetValue(bucket, out var s) ? s : BucketStyles["cell_monthly"];
        map.AddCircleLayer(
            layerName:    sourceId,
            sourceName:   sourceId,
            belowLayerId: BelowLayerFor(bucket),
            sourceLayer:  bucket,
            properties: new Dictionary<string, object?>
            {
                ["circle-color"]   = style.Open,   // cells: Open/Wep/Secure are all equal
                ["circle-radius"]  = RadiusExpr(style.Radius),
                ["circle-opacity"] = 1.0,
                ["circle-blur"]    = 0.5,
            });
        _activeLayersByBucket[bucket] = sourceId;
    }

    /// <summary>Remove a cell vector layer (circle layer + source) for the given bucket.</summary>
    public static void RemoveCellVectorLayer(this MlnMapHost map, string sourceId, string bucket)
    {
        map.RemoveLayer(sourceId);
        map.RemoveSource(sourceId);
        _activeLayersByBucket.Remove(bucket);
    }
}
