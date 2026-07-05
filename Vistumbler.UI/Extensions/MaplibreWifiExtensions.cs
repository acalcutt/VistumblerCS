using MapLibreNative.Maui.WPF;

namespace Vistumbler.UI.Extensions;

/// <summary>
/// Extension methods for MlnMapImage that provide WiFi/cell-tower vector and GeoJSON
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
    // Colors graduate from bright (the live scan's active APs) to dark/muted (oldest
    // history bucket); radius shrinks with age. The live scan is drawn as a single
    // "live_aps" layer whose active APs use the brightest colors and whose dead APs
    // (seen this session but not in the current scan cycle) use a dimmer variant, so
    // they read as "still here but not currently active" and clearly outrank every
    // WifiDB history bucket below them. Colors are seeded here with sensible defaults
    // but are overwritten at runtime from the Map settings tab via ApplyColors().
    // Cell buckets use a single graduated purple (no sectype split — cells use `type`
    // for LTE/GSM/CDMA etc., not an open/WEP/secure split), so Wep/Secure just repeat
    // Open for those entries and are not user-configurable.
    private record BucketStyle(string Open, string Wep, string Secure, double Radius);

    private static readonly BucketStyle DefaultStyle = new("#1aff66", "#ffad33", "#ff1a1a", 3.0);

    // Live-scan layer styles (both share one radius — dead APs are dimmer, not smaller).
    private const double LiveRadius = 3.0;
    private static BucketStyle _liveActive = new("#1aff66", "#ffad33", "#ff1a1a", LiveRadius);
    private static BucketStyle _liveDead   = new("#14c750", "#c78528", "#c71414", LiveRadius);

    private static readonly Dictionary<string, BucketStyle> BucketStyles = new()
    {
        // History buckets start clearly dimmer than the live layer so recent history
        // is no longer mistaken for the current scan, then darken further with age.
        ["daily"]          = new("#12a642", "#a66d20", "#a61111", 3.0),
        ["weekly"]         = new("#109a3d", "#9a641e", "#9a1010", 3.0),
        ["monthly"]        = new("#0e8d38", "#8d5c1b", "#8d0f0f", 3.0),
        ["0to1year"]       = new("#0d8033", "#805319", "#800e0e", 3.0),
        ["1to2year"]       = new("#0b732e", "#734b16", "#730c0c", 2.75),
        ["2to3year"]       = new("#0a6629", "#664213", "#660b0b", 2.5),
        ["3to5year"]       = new("#085924", "#593a11", "#590a0a", 2.25),
        ["5to10year"]      = new("#07401a", "#40290c", "#400707", 2.0),
        ["10yrplus"]       = new("#052e13", "#2e1e09", "#2e0505", 1.5),
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
    // Public so callers re-applying layers after a style reload can add them in this order.
    public static readonly string[] BucketOrder =
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

    /// <summary>
    /// Clears the internal active-layer z-order tracking. Call this right before re-adding
    /// all layers after a style reload so BelowLayerFor() rebuilds the stack from scratch
    /// (the old style's layers no longer exist, so their tracked entries are stale).
    /// </summary>
    public static void ResetActiveLayerTracking() => _activeLayersByBucket.Clear();

    /// <summary>
    /// Overwrite the per-bucket circle colors from the Map settings tab. Keys are the
    /// live-scan pseudo-buckets "live_active"/"live_dead" plus the wifi history bucket
    /// names (daily, weekly, … 10yrplus). Each value is an (Open, Wep, Secure) triple of
    /// "#RRGGBB" strings. Radii and cell colors are left untouched; unknown keys are
    /// ignored. Callers must remove and re-add the affected circle layers afterward —
    /// circle-color is baked into a layer's paint when it is added.
    /// </summary>
    public static void ApplyColors(IReadOnlyDictionary<string, (string Open, string Wep, string Secure)> colors)
    {
        foreach (var (key, c) in colors)
        {
            if (key == "live_active")
                _liveActive = _liveActive with { Open = c.Open, Wep = c.Wep, Secure = c.Secure };
            else if (key == "live_dead")
                _liveDead = _liveDead with { Open = c.Open, Wep = c.Wep, Secure = c.Secure };
            else if (BucketStyles.TryGetValue(key, out var s))
                BucketStyles[key] = s with { Open = c.Open, Wep = c.Wep, Secure = c.Secure };
        }
    }

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

    // Live-scan color function. The GeoJSON feature carries a single "styidx" property
    // that folds active/dead and sectype into one 1..6 value (active: 1/2/3 open/wep/secure,
    // dead: 4/5/6), so this stays the same proven property+stops form as SeCtypeStopsExpr.
    // styidx is always an exact integer, so the implicit interpolation between stops never
    // blends the active and dead colors in practice.
    private static Dictionary<string, object?> LiveColorExpr() => new()
    {
        ["property"] = "styidx",
        ["stops"]    = new object[] {
            new object[] { 1, _liveActive.Open },
            new object[] { 2, _liveActive.Wep },
            new object[] { 3, _liveActive.Secure },
            new object[] { 4, _liveDead.Open },
            new object[] { 5, _liveDead.Wep },
            new object[] { 6, _liveDead.Secure },
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
    public static void SetWifiGeoJsonLayer(this MlnMapImage map, string sourceId, string geoJsonUrl)
    {
        map.AddGeoJsonSourceUrl(sourceId, geoJsonUrl);
        AddWifiCircleLayer(map, sourceId, sourceId, sourceLayer: null, belowLayerId: null);
    }

    /// <summary>
    /// Update an existing GeoJSON source with inline GeoJSON string data.
    /// Use this for live/frequently-updated data to avoid HTTP round-trips.
    /// If the source does not exist yet, it is created (with its circle layer).
    /// </summary>
    public static void SetWifiGeoJsonLayerData(this MlnMapImage map, string sourceId, string geoJson)
    {
        map.AddGeoJsonSource(sourceId, geoJson);
        AddWifiCircleLayer(map, sourceId, sourceId, sourceLayer: null, belowLayerId: null);
    }

    /// <summary>
    /// Add or update the live-scan GeoJSON source and its circle layer. Unlike the
    /// generic wifi layer, this colors each point from its "styidx" property so the
    /// current scan's active APs render brightest and this session's dead APs render
    /// dimmer (see <see cref="LiveColorExpr"/>). This is the top layer in the z-order;
    /// all WifiDB history buckets insert below it.
    /// </summary>
    public static void SetLiveApGeoJsonLayer(this MlnMapImage map, string sourceId, string geoJson)
    {
        map.AddGeoJsonSource(sourceId, geoJson);
        map.AddCircleLayer(
            layerName:    sourceId,
            sourceName:   sourceId,
            belowLayerId: null,
            sourceLayer:  null,
            properties: new Dictionary<string, object?>
            {
                ["circle-color"]   = LiveColorExpr(),
                ["circle-radius"]  = RadiusExpr(LiveRadius),
                ["circle-opacity"] = 1.0,
                ["circle-blur"]    = 0.5,
            });
    }

    /// <summary>Remove a GeoJSON wifi layer (circle layer + source).</summary>
    public static void RemoveWifiGeoJsonLayer(this MlnMapImage map, string sourceId)
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
    public static void SetWifiVectorLayer(this MlnMapImage map, string sourceId, string tileJsonUrl, string bucket)
    {
        map.AddVectorSourceUrl(sourceId, tileJsonUrl);
        AddWifiCircleLayer(map, sourceId, sourceId, bucket, BelowLayerFor(bucket));
        _activeLayersByBucket[bucket] = sourceId;
    }

    /// <summary>Remove a wifi vector layer (circle layer + source) for the given bucket.</summary>
    public static void RemoveWifiVectorLayer(this MlnMapImage map, string sourceId, string bucket)
    {
        map.RemoveLayer(sourceId);
        map.RemoveSource(sourceId);
        _activeLayersByBucket.Remove(bucket);
    }

    private static void AddWifiCircleLayer(MlnMapImage map, string layerName, string sourceName,
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
    public static void SetCellVectorLayer(this MlnMapImage map, string sourceId, string tileJsonUrl, string bucket)
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
    public static void RemoveCellVectorLayer(this MlnMapImage map, string sourceId, string bucket)
    {
        map.RemoveLayer(sourceId);
        map.RemoveSource(sourceId);
        _activeLayersByBucket.Remove(bucket);
    }
}
