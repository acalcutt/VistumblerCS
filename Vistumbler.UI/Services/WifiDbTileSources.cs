using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Vistumbler.UI.Services;

/// <summary>
/// Resolves the tile-source URL for each WifiDB history bucket.
///
/// WifiDB history is no longer served by the mvtd daemon behind
/// <c>api/tilejson.php?bucket=</c>. Each bucket is now a PMTiles archive published
/// to data.wifidb.net and reachable at a stable per-category alias:
/// <c>https://data.wifidb.net/latest/{category}/tiles.json</c>.
///
/// The URL carries a fragment naming the same archive's torrent and magnet:
/// <c>#torrent={url}&amp;magnet={magnet}</c>. Nothing in the app reads that today —
/// MapLibre fetches the TileJSON and a fragment is never sent in an HTTP request,
/// so it costs nothing — but it means the handles are already in place when
/// pmtiles-torrent support arrives. Do not "simplify" these to bare tiles.json URLs.
///
/// Two sources of truth, in that order:
/// <list type="number">
/// <item>the built-in <see cref="Defaults"/> table, so the map works on first launch
/// and offline with no network round-trip;</item>
/// <item>the feed at <see cref="DefaultFeedUrl"/>, refreshed in the background, so a
/// renamed or added bucket needs no app release.</item>
/// </list>
/// The feed is advisory. A refresh that fails leaves the built-in list working — it
/// must never blank the overlays.
///
/// This is deliberately a copy of VistumblerMAUI's service of the same name rather
/// than shared code: the two apps have separate Vistumbler.Core projects, and the
/// only thing that differs here is where the cache is written.
/// </summary>
public static class WifiDbTileSources
{
    /// <summary>Origin serving the PMTiles buckets and the feed.</summary>
    public const string DefaultDataRoot = "https://data.wifidb.net";

    /// <summary>Feed listing every published bucket archive.</summary>
    public const string DefaultFeedUrl = DefaultDataRoot + "/feed.xml";

    /// <summary>How long a fetched feed is trusted before <see cref="RefreshIfStaleAsync"/> refetches.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    private static string _dataRoot = DefaultDataRoot;

    /// <summary>
    /// Data origin, e.g. "https://data.wifidb.net". Settable so a mirror can be
    /// pointed at on startup; never stored blank.
    /// </summary>
    public static string DataRoot
    {
        get => _dataRoot;
        set => _dataRoot = string.IsNullOrWhiteSpace(value)
            ? DefaultDataRoot
            : value.Trim().TrimEnd('/');
    }

    /// <summary>Feed URL, derived from <see cref="DataRoot"/> so pointing at a mirror moves both.</summary>
    public static string FeedUrl => DataRoot.TrimEnd('/') + "/feed.xml";

    /// <summary>
    /// Feed category for a bucket name. The mapping is mechanical and holds for all
    /// twenty buckets: "daily" → "wifidb-daily", "cell_0to1year" → "wifidb-cell-0to1year".
    /// </summary>
    public static string CategoryFor(string bucket) => "wifidb-" + bucket.Replace('_', '-');

    /// <summary>
    /// Full tile-source URL for a bucket — the stable <c>latest</c> alias plus the
    /// torrent/magnet fragment. Falls back to the bare alias for a bucket we have no
    /// archive details for, which still renders: only the fragment is lost.
    /// </summary>
    public static string TileJsonUrlFor(string bucket)
    {
        string category = CategoryFor(bucket);

        if (Cache.TryGetValue(category, out string? cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        if (Defaults.TryGetValue(category, out string? magnet))
            return BuildUrl(category, magnet);

        return $"{DataRoot.TrimEnd('/')}/latest/{category}/tiles.json";
    }

    // ── Feed refresh ──────────────────────────────────────────────────────────

    /// <summary>
    /// Refetches the feed if the last successful fetch is older than
    /// <see cref="RefreshInterval"/>. Safe to call on every startup.
    /// </summary>
    public static Task<int> RefreshIfStaleAsync(CancellationToken ct = default)
    {
        _ = Cache;   // forces the on-disk cache (and its timestamp) to load

        if (_fetchedUtc is { } last && DateTimeOffset.UtcNow - last < RefreshInterval)
            return Task.FromResult(0);

        return RefreshAsync(ct);
    }

    /// <summary>
    /// Fetches the feed and replaces the cached per-category URLs.
    /// </summary>
    /// <returns>
    /// The number of categories learned, or 0 if the feed could not be fetched or
    /// parsed. A failure leaves whatever was cached — and failing that, the built-in
    /// defaults — untouched, so the overlays keep working offline.
    /// </returns>
    public static async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            string xml = await http.GetStringAsync(FeedUrl, ct).ConfigureAwait(false);
            var found = ParseFeed(xml);

            // An empty parse is indistinguishable from a served error page. Keep what
            // we have rather than blanking every overlay on a bad response.
            if (found.Count == 0) return 0;

            _cache = found;
            _fetchedUtc = DateTimeOffset.UtcNow;
            Save(found, _fetchedUtc.Value);
            return found.Count;
        }
        catch (Exception)
        {
            // Offline, DNS failure, 500, malformed XML — all the same answer: keep
            // serving the last known good list.
            return 0;
        }
    }

    /// <summary>
    /// Turns a swarm feed into category → tile-source URL.
    /// </summary>
    /// <remarks>
    /// A feed lists every archive ever published, so a category appears once per
    /// rebuild; the newest <c>pubDate</c> wins.
    /// </remarks>
    internal static Dictionary<string, string> ParseFeed(string xml)
    {
        XNamespace pm = "https://github.com/TechIdiots-LLC/pmtiles-swarm/ns/1.0";
        var newest = new Dictionary<string, (DateTimeOffset When, string Url)>();

        foreach (var item in XDocument.Parse(xml).Descendants("item"))
        {
            string? category = (string?)item.Element("category");
            string? magnet   = (string?)item.Element(pm + "magnet");
            string? torrent  = (string?)item.Element("enclosure")?.Attribute("url");
            if (string.IsNullOrWhiteSpace(category) ||
                string.IsNullOrWhiteSpace(magnet) ||
                string.IsNullOrWhiteSpace(torrent))
            {
                continue;
            }

            // An item with no parsable date sorts oldest, so it is used only when it
            // is the sole item for its category.
            _ = DateTimeOffset.TryParse((string?)item.Element("pubDate"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when);

            if (newest.TryGetValue(category, out var have) && have.When >= when) continue;

            string url = $"{Alias(category)}#torrent={Uri.EscapeDataString(torrent)}" +
                         $"&magnet={Uri.EscapeDataString(StripWebSeeds(magnet))}";
            newest[category] = (when, url);
        }

        return newest.ToDictionary(e => e.Key, e => e.Value.Url);
    }

    /// <summary>
    /// Drops <c>ws=</c> web-seed parameters from a magnet.
    /// </summary>
    /// <remarks>
    /// The feed's magnets carry a web seed pointing back at wifidb.net. The URLs the
    /// apps and styles share deliberately omit it: a web seed makes every client fall
    /// back to one origin over HTTP, which is the load the swarm exists to remove.
    /// </remarks>
    private static string StripWebSeeds(string magnet)
    {
        int split = magnet.IndexOf('?');
        if (split < 0) return magnet;

        var kept = magnet.Substring(split + 1)
            .Split('&')
            .Where(p => !p.StartsWith("ws=", StringComparison.OrdinalIgnoreCase));

        return magnet.Substring(0, split) + "?" + string.Join("&", kept);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    private sealed class CacheFile
    {
        [JsonPropertyName("fetchedUtc")] public DateTimeOffset FetchedUtc { get; set; }
        [JsonPropertyName("sources")]    public Dictionary<string, string> Sources { get; set; } = new();
    }

    /// <summary>
    /// Where the last known good feed is kept, beside the app's other user files.
    /// </summary>
    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Vistumbler", "wifidb-tile-sources.json");

    private static Dictionary<string, string>? _cache;
    private static DateTimeOffset? _fetchedUtc;

    private static Dictionary<string, string> Cache
    {
        get
        {
            if (_cache is not null) return _cache;

            try
            {
                if (File.Exists(CachePath) &&
                    JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath)) is { } file)
                {
                    _cache = file.Sources;
                    _fetchedUtc = file.FetchedUtc;
                }
            }
            catch (Exception)
            {
                // Missing, unreadable or corrupt — the defaults cover every bucket.
            }

            return _cache ??= new Dictionary<string, string>();
        }
    }

    private static void Save(Dictionary<string, string> sources, DateTimeOffset fetchedUtc)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(
                new CacheFile { FetchedUtc = fetchedUtc, Sources = sources },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // A cache that cannot be written costs one feed fetch next start.
        }
    }

    /// <summary>Forgets the cached feed, so the next lookup uses <see cref="Defaults"/>.</summary>
    public static void ClearCache()
    {
        _cache = new Dictionary<string, string>();
        _fetchedUtc = null;
        try { File.Delete(CachePath); } catch (Exception) { }
    }

    // ── Built-in defaults ─────────────────────────────────────────────────────

    private static string Alias(string category) =>
        $"{DataRoot.TrimEnd('/')}/latest/{category}/tiles.json";

    /// <summary>
    /// Pairs a TileJSON alias with the torrent and magnet for the same archive.
    /// </summary>
    /// <remarks>
    /// Both handles are included so a client that cannot reach the origin has
    /// somewhere to fall back to: the .torrent for anything that wants the metadata
    /// up front, the magnet for anything that would rather resolve it from the swarm.
    /// The torrent URL is not stored — a swarm addresses its archives by infohash, so
    /// it is composed from the one inside the magnet and the current
    /// <see cref="DataRoot"/>, which keeps it pointing at the mirror in use.
    /// </remarks>
    private static string BuildUrl(string category, string magnet)
    {
        string torrent = string.Empty;
        if (InfoHashOf(magnet) is { } infoHash)
        {
            string url = $"{DataRoot.TrimEnd('/')}/archives/{infoHash}/archive.torrent";
            torrent = $"torrent={Uri.EscapeDataString(url)}&";
        }

        return $"{Alias(category)}#{torrent}magnet={Uri.EscapeDataString(magnet)}";
    }

    /// <summary>Reads the btih infohash out of a magnet, or null if it carries none.</summary>
    private static string? InfoHashOf(string magnet)
    {
        const string marker = "xt=urn:btih:";

        int at = magnet.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        int start = at + marker.Length;
        int end = magnet.IndexOf('&', start);
        return end < 0 ? magnet.Substring(start) : magnet.Substring(start, end - start);
    }

    /// <summary>
    /// Magnet for the newest archive in each category as of 2026-08-17.
    /// </summary>
    /// <remarks>
    /// These exist so a fresh install renders before it has ever reached the network,
    /// and they go stale by design — each rebuild publishes a new infohash. That is
    /// harmless: the TileJSON alias they resolve to is stable and always serves the
    /// current archive, and <see cref="RefreshAsync"/> replaces the magnet on first
    /// run. Regenerate them when convenient, not on a schedule.
    ///
    /// Each magnet is stored whole, trackers and all, rather than composed from a
    /// shared list. Every archive announces its own trackers; that they currently
    /// agree is a property of how these twenty were published, not of the format.
    /// Web seeds are stripped — see <see cref="StripWebSeeds"/>.
    /// </remarks>
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["wifidb-daily"] =
            "magnet:?xt=urn:btih:a4c4c571115588b21ad402bb17ac41ecb1e59fff&dn=daily-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-weekly"] =
            "magnet:?xt=urn:btih:821b365ae6631a5035fbab0ee222e37d105d4633&dn=weekly-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-monthly"] =
            "magnet:?xt=urn:btih:215aee52aaa60ca4966cf0ff37ea0d1aa7e3d152&dn=monthly-20260817.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-0to1year"] =
            "magnet:?xt=urn:btih:62210be3d7f78748755b05188ff1ba07c100cd8d&dn=0to1year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-1to2year"] =
            "magnet:?xt=urn:btih:480d0342de47ad7820a5bca0d6c1a5fb3fb943fa&dn=1to2year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-2to3year"] =
            "magnet:?xt=urn:btih:a46d62748171025119e11beb9abaf9143913efd7&dn=2to3year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-3to5year"] =
            "magnet:?xt=urn:btih:7fa1105c4b65cbc114d0b7c815cdb39316dd42ba&dn=3to5year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-5to10year"] =
            "magnet:?xt=urn:btih:32057a069772d6b17315ef7e7ac9a6696f6ee096&dn=5to10year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-10yrplus"] =
            "magnet:?xt=urn:btih:03e5a0fd12e2ba65952fe7c2adbf70a88d7fdfb9&dn=10yrplus-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-heatmap"] =
            "magnet:?xt=urn:btih:b66d0b97993af0466422058326f85381cb6fd594&dn=heatmap-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-daily"] =
            "magnet:?xt=urn:btih:a42c457646d9491e7e3429864d3ffe3eed111c13&dn=cell_daily-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-weekly"] =
            "magnet:?xt=urn:btih:74936804c587d236ea3b4c2b9c57dead5f81fce8&dn=cell_weekly-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-monthly"] =
            "magnet:?xt=urn:btih:22b546f8626c427f0f7fb4e1dee7ec27b4188bde&dn=cell_monthly-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-0to1year"] =
            "magnet:?xt=urn:btih:02569bf9c25a9ab3e046d5a8af159833c4c89a44&dn=cell_0to1year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-1to2year"] =
            "magnet:?xt=urn:btih:222e7d123ed31d58d64587e47818ae35e756cc3a&dn=cell_1to2year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-2to3year"] =
            "magnet:?xt=urn:btih:433264e121ca11cdfaa9d21a421c07676dc22962&dn=cell_2to3year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-3to5year"] =
            "magnet:?xt=urn:btih:4c26ef63b4ac784be97bfc21551ad1bd662e5087&dn=cell_3to5year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-5to10year"] =
            "magnet:?xt=urn:btih:f9b9e180b504afdeea92282d705e2cb60e816b9d&dn=cell_5to10year-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-10yrplus"] =
            "magnet:?xt=urn:btih:764f49ebe71e51db8d31de99fd1249f122753fc7&dn=cell_10yrplus-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-heatmap"] =
            "magnet:?xt=urn:btih:2987e5413b8919ea228be166094f3e23bf392d8c&dn=cell_heatmap-20260815.pmtiles&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
    };
}
