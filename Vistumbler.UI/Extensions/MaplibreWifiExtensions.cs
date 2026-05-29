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
        string filterJson)
    {
        // Use reflection to check if layer exists and set filter
        // This is a workaround since the public API doesn't expose layer checking
        try
        {
            // Try to add the circle layer - AddCircleLayer will skip if it already exists
            map.AddCircleLayer(
                layerName: layerName,
                sourceName: sourceName,
                belowLayerId: null,
                sourceLayer: null,
                properties: new Dictionary<string, object?>
                {
                    ["circle-color"] = color,
                    ["circle-radius"] = 5.0,
                    ["circle-opacity"] = 0.85
                });

            // Now we need to set the filter on the layer
            // We'll use reflection to access the internal _style and get the layer
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
