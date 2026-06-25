using MapLibreNative.Maui.WPF;

namespace Vistumbler.UI.Extensions;

/// <summary>
/// Extension methods for MlnMapHost that provide WiFi-specific GeoJSON layer handling,
/// maintaining compatibility with the old MaplibreNative.NET.WPF MaplibreMapHost API.
/// </summary>
public static class MaplibreWifiExtensions
{
    /// <summary>
    /// Add or update a GeoJSON source that fetches from a URL, with circle
    /// layers for open (green), WEP (orange) and secure (red) access points.
    /// </summary>
    public static void SetWifiGeoJsonLayer(this MlnMapHost map, string sourceId, string geoJsonUrl)
    {
        // Add or update the GeoJSON source from URL
        map.AddGeoJsonSourceUrl(sourceId, geoJsonUrl);
        
        // Add the three security-type circle layers if they don't exist
        AddWifiCircleLayers(map, sourceId);
    }

    /// <summary>
    /// Update an existing GeoJSON source with inline GeoJSON string data.
    /// Use this for live/frequently-updated data to avoid HTTP round-trips.
    /// If the source does not exist yet, it is created (with circle layers).
    /// </summary>
    public static void SetWifiGeoJsonLayerData(this MlnMapHost map, string sourceId, string geoJson)
    {
        // Add or update the GeoJSON source with inline data
        map.AddGeoJsonSource(sourceId, geoJson);
        
        // Add the three security-type circle layers if they don't exist
        AddWifiCircleLayers(map, sourceId);
    }

    /// <summary>Remove a GeoJSON wifi layer set (3 security-type circle layers + source).</summary>
    public static void RemoveWifiGeoJsonLayer(this MlnMapHost map, string sourceId)
    {
        map.RemoveLayer(sourceId + "_open");
        map.RemoveLayer(sourceId + "_wep");
        map.RemoveLayer(sourceId + "_secure");
        map.RemoveSource(sourceId);
    }

    /// <summary>
    /// Add a vector tile source from a TileJSON URL with the three security-type
    /// circle layers. The <paramref name="sourceLayer"/> must match the layer name
    /// inside the MVT tiles (tilejson.php uses the bucket name, e.g. "weekly").
    /// </summary>
    public static void SetWifiVectorLayer(this MlnMapHost map, string sourceId, string tileJsonUrl, string sourceLayer)
    {
        map.AddVectorSourceUrl(sourceId, tileJsonUrl);
        AddWifiCircleLayersVector(map, sourceId, sourceLayer);
    }

    /// <summary>Remove a vector wifi layer set (3 circle layers + source).</summary>
    public static void RemoveWifiVectorLayer(this MlnMapHost map, string sourceId)
    {
        map.RemoveLayer(sourceId + "_open");
        map.RemoveLayer(sourceId + "_wep");
        map.RemoveLayer(sourceId + "_secure");
        map.RemoveSource(sourceId);
    }

    // Colors graduate from dark/muted (oldest) to bright/saturated (newest),
    // matching the wifidb.net maplibre-gl-js style.
    private static readonly Dictionary<string, (string Open, string Wep, string Secure)> BucketColors = new()
    {
        ["legacy"]   = ("#00802b", "#cc7a00", "#b30000"),
        ["2to3year"] = ("#00b33c", "#e68a00", "#cc0000"),
        ["1to2year"] = ("#00e64d", "#ff9900", "#e60000"),
        ["0to1year"] = ("#1aff66", "#ffad33", "#ff1a1a"),
        ["monthly"]  = ("#1aff66", "#ffad33", "#ff1a1a"),
        ["weekly"]   = ("#1aff66", "#ffad33", "#ff1a1a"),
        ["daily"]    = ("#1aff66", "#ffad33", "#ff1a1a"),
    };

    private static void AddWifiCircleLayersVector(MlnMapHost map, string sourceId, string sourceLayer)
    {
        var colors = BucketColors.TryGetValue(sourceLayer, out var c) ? c : (Open: "#1aff66", Wep: "#ffad33", Secure: "#ff1a1a");

        AddWifiCircleLayer(
            map,
            layerName:   sourceId + "_open",
            sourceName:  sourceId,
            color:       colors.Open,
            sourceLayer: sourceLayer,
            filterJson:  "[\"any\",[\"==\",[\"get\",\"sectype\"],1],[\"==\",[\"get\",\"sectype\"],\"1\"]]");

        AddWifiCircleLayer(
            map,
            layerName:   sourceId + "_wep",
            sourceName:  sourceId,
            color:       colors.Wep,
            sourceLayer: sourceLayer,
            filterJson:  "[\"any\",[\"==\",[\"get\",\"sectype\"],2],[\"==\",[\"get\",\"sectype\"],\"2\"]]");

        AddWifiCircleLayer(
            map,
            layerName:   sourceId + "_secure",
            sourceName:  sourceId,
            color:       colors.Secure,
            sourceLayer: sourceLayer,
            filterJson:  "[\"any\",[\"==\",[\"get\",\"sectype\"],3],[\"==\",[\"get\",\"sectype\"],\"3\"]]");
    }

    private static void AddWifiCircleLayers(MlnMapHost map, string sourceId)
    {
        // Each layer gets a filter so only features matching that security type are drawn.
        // Filters accept both int and string sectype values (mirrors the Android app pattern).
        // sectype: 1=Open(green), 2=WEP(orange), 3=Secure/WPA*(red)
        
        AddWifiCircleLayer(
            map,
            layerName: sourceId + "_open",
            sourceName: sourceId,
            color: "#00802b",
            filterJson: "[\"any\",[\"==\",[\"get\",\"sectype\"],1],[\"==\",[\"get\",\"sectype\"],\"1\"]]");

        AddWifiCircleLayer(
            map,
            layerName: sourceId + "_wep",
            sourceName: sourceId,
            color: "#cc7a00",
            filterJson: "[\"any\",[\"==\",[\"get\",\"sectype\"],2],[\"==\",[\"get\",\"sectype\"],\"2\"]]");

        AddWifiCircleLayer(
            map,
            layerName: sourceId + "_secure",
            sourceName: sourceId,
            color: "#b30000",
            filterJson: "[\"any\",[\"==\",[\"get\",\"sectype\"],3],[\"==\",[\"get\",\"sectype\"],\"3\"]]");
    }

    private static void AddWifiCircleLayer(
        MlnMapHost map,
        string layerName,
        string sourceName,
        string color,
        string filterJson,
        string? sourceLayer = null)
    {
        try
        {
            map.AddCircleLayer(
                layerName: layerName,
                sourceName: sourceName,
                belowLayerId: null,
                sourceLayer: sourceLayer,
                properties: new Dictionary<string, object?>
                {
                    ["circle-color"]   = color,
                    ["circle-radius"]  = 2.0,
                    ["circle-opacity"] = 1.0,
                    ["circle-blur"]    = 0.5
                });

            // Set the filter via reflection (public API doesn't expose layer.SetFilter).
            var mapType = map.GetType();
            var styleField = mapType.GetField("_style", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (styleField?.GetValue(map) is object style)
            {
                var styleType = style.GetType();
                var getLayerMethod = styleType.GetMethod("GetLayer", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
                if (getLayerMethod?.Invoke(style, new object[] { layerName }) is object layer)
                {
                    var layerType = layer.GetType();
                    var setFilterMethod = layerType.GetMethod("SetFilter",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    
                    setFilterMethod?.Invoke(layer, new object[] { filterJson });
                }
            }
        }
        catch
        {
            // Silently ignore if reflection fails - layer may already exist with correct filter
        }
    }
}
