using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Vistumbler.UI.Services;

/// <summary>
/// Resolves the tile-source URL for each WifiDB history bucket.
///
/// WifiDB's <c>api/tilejson.php?bucket=</c> already does the work: for a bucket
/// that has been archived it answers 302 to the swarm's per-category alias,
/// <c>https://data.wifidb.net/latest/{category}/tiles.json</c>, with the archive's
/// torrent and magnet in the fragment. So that endpoint stays the source URL — it
/// resolves the current build server-side, and it still serves tiles itself on an
/// install with no swarm configured, neither of which an app can do for itself.
///
/// What the app keeps is a way to draw when that endpoint cannot be reached:
/// <see cref="FallbackUrlFor"/> returns the same alias, built in, with a magnet in
/// its fragment. <see cref="ProbeAsync"/> decides between them once per run.
///
/// Do not "shortcut" this by asking for the archive alias first. It looks like a free
/// win -- one request rather than two, and it draws while wifidb.net is down -- and it
/// was tried; the reason it is wrong is that the endpoint is the only piece that can
/// steer. It resolves which build is current, it can move the archives to another host
/// or a mirror without an app release, and on an install with no swarm it serves the
/// tiles itself. An address compiled into the app can do none of that, which is exactly
/// why it belongs in the fallback and not in front.
///
/// Nor is going direct needed for peer-to-peer tile loading later. A torrent-aware
/// source takes the archive's infohash and magnet from the `torrent` block in the
/// TileJSON body, which is present whichever URL fetched it; the handles in the
/// fragment here are the fallback for when that document cannot be had at all.
///
/// The fallback magnets are BEP 46 mutable ones — a single public key with the
/// category as the salt, so <c>wifidb-daily</c> resolves to whatever the newest
/// daily archive is rather than to one build of it. That is what makes a built-in
/// table sane: it names categories, not builds, and so does not go stale when the
/// archives are rebuilt. Do not replace them with the per-build magnets from
/// feed.xml, which carry only an infohash and are correct for one day.
/// </summary>
public static class WifiDbTileSources
{
    /// <summary>Origin serving the published archives, used by the fallback URLs.</summary>
    public const string DefaultDataRoot = "https://data.wifidb.net";

    private static string _dataRoot = DefaultDataRoot;

    /// <summary>
    /// Origin the fallback URLs point at. Settable so a mirror can be pointed at on
    /// startup; never stored blank.
    /// </summary>
    public static string DataRoot
    {
        get => _dataRoot;
        set => _dataRoot = string.IsNullOrWhiteSpace(value)
            ? DefaultDataRoot
            : value.Trim().TrimEnd('/');
    }
    private static string _apiBaseUrl = "https://wifidb.net/api";

    /// <summary>
    /// WifiDB's API root, e.g. "https://wifidb.net/api". Pushed in from the site
    /// setting rather than read from it, since this class is static.
    /// </summary>
    public static string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set => _apiBaseUrl = string.IsNullOrWhiteSpace(value)
            ? "https://wifidb.net/api"
            : value.Trim().TrimEnd('/');
    }

    /// <summary>
    /// Feed category for a bucket name. The mapping is mechanical and holds for all
    /// twenty buckets: "daily" -> "wifidb-daily", "cell_0to1year" -> "wifidb-cell-0to1year".
    /// It is also the salt of that category's mutable magnet.
    /// </summary>
    public static string CategoryFor(string bucket) => "wifidb-" + bucket.Replace('_', '-');

    /// <summary>
    /// Source URL for a bucket: WifiDB's TileJSON endpoint, or the built-in swarm
    /// alias once <see cref="ProbeAsync"/> has found that endpoint unreachable.
    /// </summary>
    public static string TileJsonUrlFor(string bucket) =>
        _apiReachable == false
            ? FallbackUrlFor(bucket)
            : $"{ApiBaseUrl.TrimEnd('/')}/tilejson.php?bucket={bucket}";

    /// <summary>
    /// The bucket's archive addressed directly, for when WifiDB cannot be reached.
    /// </summary>
    /// <remarks>
    /// Carries the same handles in its fragment that the endpoint would have
    /// redirected with: the .torrent for anything wanting the metadata up front,
    /// the magnet for anything that would rather resolve it from the swarm. A
    /// fragment is never sent in an HTTP request, so MapLibre fetches the TileJSON
    /// and ignores the rest. Do not "simplify" these to bare tiles.json URLs.
    /// </remarks>
    public static string FallbackUrlFor(string bucket)
    {
        string category = CategoryFor(bucket);
        string alias = $"{DataRoot.TrimEnd('/')}/latest/{category}/tiles.json";

        if (!Magnets.TryGetValue(category, out string? magnet))
            return alias;

        string torrent = string.Empty;
        if (InfoHashOf(magnet) is { } infoHash)
        {
            // A swarm addresses its archives by infohash, so this is composed rather
            // than stored -- which also keeps it on whichever mirror DataRoot names.
            string url = $"{DataRoot.TrimEnd('/')}/archives/{infoHash}/archive.torrent";
            torrent = $"torrent={Uri.EscapeDataString(url)}&";
        }

        return $"{alias}#{torrent}magnet={Uri.EscapeDataString(magnet)}";
    }

    // -- Reachability ---------------------------------------------------------

    private static bool? _apiReachable;

    /// <summary>
    /// True once WifiDB's endpoint is known reachable, false once it is known not
    /// to be, null before <see cref="ProbeAsync"/> has answered.
    /// </summary>
    public static bool? ApiReachable => _apiReachable;

    /// <summary>
    /// Asks the endpoint for one bucket to find out whether it can be used at all.
    /// </summary>
    /// <remarks>
    /// One request decides for every bucket, because they all come from the same
    /// endpoint. Until it answers, <see cref="TileJsonUrlFor"/> assumes the endpoint
    /// works: it is the better source when it is up, and being wrong for the first
    /// moment of a run costs one failed tile request. Called on startup, not per
    /// layer -- a layer is added synchronously and cannot wait for this.
    /// </remarks>
    public static async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync(
                TileJsonUrlFor("daily"), HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            _apiReachable = response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            // Offline, DNS failure, timeout, TLS refusal -- all the same answer.
            _apiReachable = false;
        }

        return _apiReachable.Value;
    }

    // -- Built-in fallback ----------------------------------------------------

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
    /// Mutable magnet per category, from WifiDB's own endpoint on 2026-08-17.
    /// </summary>
    /// <remarks>
    /// Every one shares a public key and differs only in its salt, which is the
    /// category name. That is what keeps them current: the infohash in each is
    /// whatever build was newest when this was written, but a client that resolves
    /// the key and salt through the DHT gets the newest build at the time it asks.
    ///
    /// Stored whole, trackers and all, rather than composed from a shared list.
    /// Every archive announces its own trackers; that these twenty agree is a
    /// property of how they were published, not of the format.
    /// </remarks>
    private static readonly Dictionary<string, string> Magnets = new()
    {
        ["wifidb-daily"] =
            "magnet:?xt=urn:btih:a4c4c571115588b21ad402bb17ac41ecb1e59fff&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-daily&s=wifidb-daily&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-weekly"] =
            "magnet:?xt=urn:btih:821b365ae6631a5035fbab0ee222e37d105d4633&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-weekly&s=wifidb-weekly&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-monthly"] =
            "magnet:?xt=urn:btih:215aee52aaa60ca4966cf0ff37ea0d1aa7e3d152&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-monthly&s=wifidb-monthly&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-0to1year"] =
            "magnet:?xt=urn:btih:62210be3d7f78748755b05188ff1ba07c100cd8d&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-0to1year&s=wifidb-0to1year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-1to2year"] =
            "magnet:?xt=urn:btih:480d0342de47ad7820a5bca0d6c1a5fb3fb943fa&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-1to2year&s=wifidb-1to2year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-2to3year"] =
            "magnet:?xt=urn:btih:a46d62748171025119e11beb9abaf9143913efd7&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-2to3year&s=wifidb-2to3year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-3to5year"] =
            "magnet:?xt=urn:btih:7fa1105c4b65cbc114d0b7c815cdb39316dd42ba&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-3to5year&s=wifidb-3to5year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-5to10year"] =
            "magnet:?xt=urn:btih:32057a069772d6b17315ef7e7ac9a6696f6ee096&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-5to10year&s=wifidb-5to10year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-10yrplus"] =
            "magnet:?xt=urn:btih:03e5a0fd12e2ba65952fe7c2adbf70a88d7fdfb9&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-10yrplus&s=wifidb-10yrplus&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-heatmap"] =
            "magnet:?xt=urn:btih:b66d0b97993af0466422058326f85381cb6fd594&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-heatmap&s=wifidb-heatmap&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-daily"] =
            "magnet:?xt=urn:btih:a42c457646d9491e7e3429864d3ffe3eed111c13&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-daily&s=wifidb-cell-daily&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-weekly"] =
            "magnet:?xt=urn:btih:74936804c587d236ea3b4c2b9c57dead5f81fce8&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-weekly&s=wifidb-cell-weekly&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-monthly"] =
            "magnet:?xt=urn:btih:22b546f8626c427f0f7fb4e1dee7ec27b4188bde&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-monthly&s=wifidb-cell-monthly&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-0to1year"] =
            "magnet:?xt=urn:btih:02569bf9c25a9ab3e046d5a8af159833c4c89a44&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-0to1year&s=wifidb-cell-0to1year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-1to2year"] =
            "magnet:?xt=urn:btih:222e7d123ed31d58d64587e47818ae35e756cc3a&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-1to2year&s=wifidb-cell-1to2year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-2to3year"] =
            "magnet:?xt=urn:btih:433264e121ca11cdfaa9d21a421c07676dc22962&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-2to3year&s=wifidb-cell-2to3year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-3to5year"] =
            "magnet:?xt=urn:btih:4c26ef63b4ac784be97bfc21551ad1bd662e5087&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-3to5year&s=wifidb-cell-3to5year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-5to10year"] =
            "magnet:?xt=urn:btih:f9b9e180b504afdeea92282d705e2cb60e816b9d&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-5to10year&s=wifidb-cell-5to10year&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-10yrplus"] =
            "magnet:?xt=urn:btih:764f49ebe71e51db8d31de99fd1249f122753fc7&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-10yrplus&s=wifidb-cell-10yrplus&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
        ["wifidb-cell-heatmap"] =
            "magnet:?xt=urn:btih:2987e5413b8919ea228be166094f3e23bf392d8c&xs=urn:btpk:7c35153f97d42995023abd68788586557130c2b8b78261aa8230db2a4320c535&dn=wifidb-cell-heatmap&s=wifidb-cell-heatmap&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce&tr=udp%3A%2F%2Ftracker.torrent.eu.org%3A451%2Fannounce&tr=udp%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=http%3A%2F%2Ftracker.datacenterlight.ch%3A6969%2Fannounce&tr=udp%3A%2F%2Ftracker-udp.gbitt.info%3A80%2Fannounce&tr=https%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Ftracker.gbitt.info%2Fannounce&tr=http%3A%2F%2Fretracker.local%2Fannounce&tr=wss%3A%2F%2Ftracker.openwebtorrent.com&tr=wss%3A%2F%2Ftracker.webtorrent.dev",
    };
}
