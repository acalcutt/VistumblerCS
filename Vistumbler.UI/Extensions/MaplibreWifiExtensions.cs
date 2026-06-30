using MapLibreNative.Maui.WPF;

namespace Vistumbler.UI.Extensions;

/// <summary>
/// Extension methods for MlnMapHost that provide WiFi/cell-tower vector and GeoJSON
/// layer handling. Color, radius and z-order match WifiDB's map.php and
/// VistumblerMAUI's MapViewModel so all three clients render history layers identically.
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

    // MapLibre GL "case" expression: sectype==2→WEP, sectype==3→secure, default→open.
    private static object[] SeCtypeColorExpr(BucketStyle s) =>
    [
        "case",
        new object[] { "==", new object[] { "get", "sectype" }, 2 }, s.Wep,
        new object[] { "==", new object[] { "get", "sectype" }, 3 }, s.Secure,
        s.Open,
    ];

    // MapLibre GL zoom-interpolated radius function.
    // base=1.5 is the interpolation curve (fixed); the per-bucket size difference
    // lives in the stop VALUES, not the base.
    private static Dictionary<string, object?> RadiusExpr(double baseRadius) => new()
    {
        ["base"]  = 1.5,
        ["stops"] = new object[] {
            new object[] { 1,  baseRadius * 0.5 },
            new object[] { 4,  baseRadius },
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
                ["circle-color"]   = SeCtypeColorExpr(style),
                ["circle-radius"]  = RadiusExpr(style.Radius),
                ["circle-opacity"] = 1.0,
                ["circle-blur"]    = 0.5,
            });
    }

    // ── Cell tower layers ─────────────────────────────────────────────────────

    /// <summary>
    /// Add a vector tile source from a TileJSON URL with a single graduated-purple
    /// circle layer for cell towers. Cell tiles use <c>type</c> (LTE/GSM/etc.) instead
    /// of <c>sectype</c>, so only one color (no security-type split) is used per bucket.
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
