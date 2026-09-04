using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vistumbler.UI.Services;

/// <summary>
/// Finds the raster-dem source a style intends terrain to be draped over, so the map's
/// terrain button can appear only when terrain is actually possible.
///
/// The map renderer's terrain control adds no sources of its own — it toggles terrain
/// against a DEM that must already be in the style — so it has to be pointed at the
/// right one. The WifiDB relief styles declare five raster-dem sources, and taking the
/// first would drape the map over GEBCO bathymetry (ocean depths) instead of land
/// elevation. <see cref="FindAsync"/> resolves which to use; null means the style
/// carries none, and <see cref="Fallbacks"/> is then what the app adds itself.
///
/// The style JSON is fetched separately from the map's own load, because the renderer
/// does not surface a loaded style's sources back to the app. Cached per URL, so a
/// style costs one request per run.
/// </summary>
public static class TerrainSources
{
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Source id used when the app supplies the DEM itself. Prefixed so it
    /// cannot collide with a source a style already declares.</summary>
    public const string FallbackSourceId = "vistumbler-terrain-dem";

    /// <summary>
    /// A DEM the app can add to a style that has none: either a TileJSON
    /// <paramref name="TileJsonUrl"/>, or explicit <c>{z}/{x}/{y}</c> templates plus the
    /// <paramref name="Encoding"/> they use — terrarium DEMs decode differently from the
    /// default mapbox encoding, and a terrarium source added without it renders garbage
    /// elevation rather than failing.
    /// </summary>
    /// <param name="TileSize">
    /// The size the source selects tiles with. Getting this wrong is not cosmetic: it is
    /// what the terrain mesh covers at, so a DEM declared 256 while serving 512px tiles
    /// loads one zoom deeper than the mesh and every per-tile lookup misses, leaving the
    /// map flat. Each entry below is declared at the size its tiles actually are.
    /// </param>
    public sealed record Dem(
        string Name,
        string? TileJsonUrl,
        string[]? TileUrlTemplates = null,
        string? Encoding           = null,
        string? Attribution        = null,
        int TileSize               = 512,
        int MaxZoom                = 15);

    /// <summary>
    /// DEMs the app can fall back on, in preference order. Only consulted for styles that
    /// declare no raster-dem of their own — a style's own DEM is always better, since it
    /// is the one its hillshade and relief layers already draw from.
    /// </summary>
    public static readonly IReadOnlyList<Dem> Fallbacks = new[]
    {
        // WifiDB's own terrain, the same source its relief styles drape over: global to
        // z16, mapbox-encoded, 512px tiles (its TileJSON declares no tileSize, so the
        // value here is what sets it).
        new Dem("WifiDB Terrain",
                TileJsonUrl:  "https://swarm.wifidb.net/latest/terrain_sparse/tiles.json",
                MaxZoom:      16),

        // Mapterhorn — high quality. Its TileJSON declares both encoding (terrarium,
        // despite the 512px tiles) and tileSize, and the renderer honours a TileJSON's
        // own values, so neither is repeated here.
        new Dem("Mapterhorn",
                TileJsonUrl:  "https://tiles.mapterhorn.com/tilejson.json"),

        // AWS Open Data terrain-tiles (Mapzen) — no TileJSON, terrarium-encoded, 256px.
        new Dem("AWS Terrarium (Mapzen)",
                TileJsonUrl:      null,
                TileUrlTemplates: new[] { "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png" },
                Encoding:         "terrarium",
                Attribution:      "<a href=\"https://registry.opendata.aws/terrain-tiles/\">Open Data</a>",
                TileSize:         256),
    };

    /// <summary>The DEM used for styles that carry none.</summary>
    public static Dem DefaultFallback => Fallbacks[0];

    /// <summary>
    /// The DEM source id terrain should use for <paramref name="styleUrl"/>, or
    /// <see langword="null"/> if the style declares none (or could not be read — an
    /// unreachable style is treated as "no terrain" rather than surfacing an error,
    /// since the map itself will have failed to load anyway).
    /// </summary>
    public static async Task<string?> FindAsync(string styleUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(styleUrl)) return null;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Cache.TryGetValue(styleUrl, out var cached)) return cached;

            string? source = null;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var json = await http.GetStringAsync(styleUrl, ct).ConfigureAwait(false);
                source = FindInStyleJson(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TerrainSources] {styleUrl} lookup failed: {ex.Message}");
            }

            Cache[styleUrl] = source;
            Debug.WriteLine($"[TerrainSources] {styleUrl} -> {source ?? "(no raster-dem)"}");
            return source;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Picks the DEM out of a style document. Split out from the fetch so it can be
    /// reasoned about (and tested) without a network.
    /// </summary>
    internal static string? FindInStyleJson(string styleJson)
    {
        using var doc = JsonDocument.Parse(styleJson);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // 1. The style already says which source terrain belongs to. Nothing to guess.
        if (root.TryGetProperty("terrain", out var terrain) &&
            terrain.ValueKind == JsonValueKind.Object &&
            terrain.TryGetProperty("source", out var declared) &&
            declared.ValueKind == JsonValueKind.String &&
            declared.GetString() is { Length: > 0 } declaredId &&
            IsRasterDem(sources, declaredId))
        {
            return declaredId;
        }

        var dems = new List<string>();
        foreach (var source in sources.EnumerateObject())
        {
            if (source.Value.ValueKind == JsonValueKind.Object &&
                source.Value.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                type.GetString() == "raster-dem")
            {
                dems.Add(source.Name);
            }
        }

        if (dems.Count == 0) return null;

        // 2. A DEM that names itself terrain. The WifiDB styles follow this: alongside
        //    hillshade_source, color_relief_source and the bathymetry pair, they carry a
        //    terrain_source that no layer draws — it exists purely to be draped over.
        var named = dems.FirstOrDefault(id => id.Contains("terrain", StringComparison.OrdinalIgnoreCase));
        if (named is not null) return named;

        // 3. Otherwise the only/first DEM, the sane guess for a style that carries one
        //    without naming it.
        return dems[0];
    }

    private static bool IsRasterDem(JsonElement sources, string id) =>
        sources.TryGetProperty(id, out var source) &&
        source.ValueKind == JsonValueKind.Object &&
        source.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String &&
        type.GetString() == "raster-dem";
}
