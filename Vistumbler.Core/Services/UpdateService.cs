using System.Net.Http.Headers;
using System.Text.Json;

namespace Vistumbler.Core.Services;

/// <summary>A file attached to a release, e.g. VistumblerCS-v0.5.0-win-x64-setup.exe.</summary>
public sealed record ReleaseAsset(string Name, string Url);

/// <summary>A published release, as read from the GitLab or GitHub releases API.</summary>
public sealed record ReleaseInfo(SemanticVersion Version, string Tag, string Notes, string PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The first asset whose name ends with <paramref name="suffix"/>, e.g. "-win-x64-setup.exe".</summary>
    public ReleaseAsset? FindAsset(string suffix) =>
        Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Result of an update check: the newest release that is newer than the running version, if any.</summary>
public sealed record UpdateCheckResult(SemanticVersion CurrentVersion, ReleaseInfo? Update, string Source)
{
    public bool IsUpdateAvailable => Update is not null;
}

/// <summary>Thrown when no release feed could be reached or parsed.</summary>
public sealed class UpdateCheckException(string message, Exception? inner) : Exception(message, inner);

/// <summary>
/// Checks the project's release feeds for a newer version. Feeds are tried in order, GitLab first and GitHub as the
/// fallback, like the original Vistumbler updater. Releases are versioned vMAJOR.MINOR.PATCH[-PRERELEASE]; a version
/// with a pre-release suffix (0.5.0-rc.1) is only offered when pre-releases are included or the running version is
/// itself a pre-release.
/// </summary>
public sealed class UpdateService
{
    public const string GitLabHost = "https://gitlab.techidiots.net";

    private static readonly TimeSpan FeedTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly string _currentVersion;
    private readonly string _userAgent;
    private readonly IReadOnlyList<(string Name, string Url, Func<string, IReadOnlyList<ReleaseInfo>> Parse)> _feeds;

    /// <param name="productName">Sent as the User-Agent product, which the GitHub API requires.</param>
    /// <param name="gitLabProject">Project path on <see cref="GitLabHost"/>, e.g. "techidiots-llc/VistumblerCS".</param>
    /// <param name="gitHubRepo">Repository on github.com, e.g. "acalcutt/VistumblerCS".</param>
    public UpdateService(HttpClient http, string productName, string currentVersion, string gitLabProject, string gitHubRepo)
    {
        _http = http;
        _currentVersion = currentVersion;
        _userAgent = $"{productName}/{currentVersion}";
        _feeds =
        [
            ("GitLab", $"{GitLabHost}/api/v4/projects/{Uri.EscapeDataString(gitLabProject)}/releases?per_page=30", ParseGitLabReleases),
            ("GitHub", $"https://api.github.com/repos/{gitHubRepo}/releases?per_page=30", ParseGitHubReleases),
        ];
    }

    /// <summary>
    /// Returns the newest release newer than the running version from the first feed that answers.
    /// Throws <see cref="UpdateCheckException"/> if every feed fails.
    /// </summary>
    /// <param name="requiredAssetSuffix">
    /// The file this platform installs from, e.g. "-win-x64-setup.exe". If the first feed's release lacks it (say a
    /// release job failed on one server), the same version is taken from the next feed that has it.
    /// </param>
    public async Task<UpdateCheckResult> CheckAsync(bool includePrereleases, string? requiredAssetSuffix = null,
        CancellationToken ct = default)
    {
        if (!SemanticVersion.TryParse(_currentVersion, out var current))
            throw new UpdateCheckException($"The running version '{_currentVersion}' isn't a valid version number.", null);

        UpdateCheckResult? first = null;
        var errors = new List<Exception>();
        foreach (var feed in _feeds)
        {
            try
            {
                var json = await GetAsync(feed.Url, ct);
                var update = SelectUpdate(feed.Parse(json), current, includePrereleases);
                var result = new UpdateCheckResult(current, update, feed.Name);
                if (first is null)
                {
                    first = result;
                    if (update is null || requiredAssetSuffix is null || update.FindAsset(requiredAssetSuffix) is not null)
                        return result;
                }
                else if (update is not null && update.Version.Equals(first.Update!.Version) &&
                         update.FindAsset(requiredAssetSuffix!) is not null)
                {
                    return result;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested &&
                                       ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                errors.Add(new Exception($"{feed.Name}: {ex.Message}", ex));
            }
        }
        if (first is not null) return first;   // no feed had the installer; the release page is still offered
        throw new UpdateCheckException(
            "Couldn't check for updates: " + string.Join("; ", errors.Select(e => e.Message)), new AggregateException(errors));
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(FeedTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(_userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }

    /// <summary>The newest release newer than <paramref name="current"/>, or null when up to date.</summary>
    public static ReleaseInfo? SelectUpdate(IEnumerable<ReleaseInfo> releases, SemanticVersion current, bool includePrereleases)
    {
        bool allowPrerelease = includePrereleases || current.IsPrerelease;
        return releases
            .Where(r => allowPrerelease || !r.Version.IsPrerelease)
            .Where(r => r.Version.CompareTo(current) > 0)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();
    }

    /// <summary>Parses GET /api/v4/projects/:id/releases. Releases with unparseable tags are skipped.</summary>
    public static IReadOnlyList<ReleaseInfo> ParseGitLabReleases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("upcoming_release", out var upcoming) && upcoming.ValueKind == JsonValueKind.True) continue;
            var tag = GetString(r, "tag_name");
            if (!SemanticVersion.TryParse(tag, out var version)) continue;

            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var a) && a.TryGetProperty("links", out var links))
            {
                foreach (var link in links.EnumerateArray())
                {
                    var url = GetString(link, "direct_asset_url");
                    if (url.Length == 0) url = GetString(link, "url");
                    assets.Add(new ReleaseAsset(GetString(link, "name"), url));
                }
            }
            var page = r.TryGetProperty("_links", out var l) ? GetString(l, "self") : "";
            releases.Add(new ReleaseInfo(version, tag, GetString(r, "description"), page, assets));
        }
        return releases;
    }

    /// <summary>Parses GET /repos/:owner/:repo/releases. Drafts and unparseable tags are skipped.</summary>
    public static IReadOnlyList<ReleaseInfo> ParseGitHubReleases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            var tag = GetString(r, "tag_name");
            if (!SemanticVersion.TryParse(tag, out var version)) continue;

            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var a))
            {
                foreach (var asset in a.EnumerateArray())
                    assets.Add(new ReleaseAsset(GetString(asset, "name"), GetString(asset, "browser_download_url")));
            }
            releases.Add(new ReleaseInfo(version, tag, GetString(r, "body"), GetString(r, "html_url"), assets));
        }
        return releases;
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}

/// <summary>
/// A MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD] version, ordered by Semantic Versioning 2.0 precedence. A leading "v" is
/// accepted and a missing patch is treated as 0, so tags like v0.5 parse too.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    /// <summary>Dot-separated pre-release identifiers, e.g. "rc.1"; empty for a stable release.</summary>
    public string Prerelease { get; }
    public bool IsPrerelease => Prerelease.Length > 0;

    private SemanticVersion(int major, int minor, int patch, string prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];

        int plus = s.IndexOf('+');                 // build metadata doesn't affect precedence
        if (plus >= 0) s = s[..plus];
        int dash = s.IndexOf('-');
        var core = dash >= 0 ? s[..dash] : s;
        var pre = dash >= 0 ? s[(dash + 1)..] : "";
        if (dash >= 0 && (pre.Length == 0 || pre.Split('.').Any(p => p.Length == 0))) return false;

        var parts = core.Split('.');
        if (parts.Length is < 2 or > 3) return false;
        var numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out numbers[i])) return false;
        }
        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // A stable release ranks above any pre-release of the same version: 1.0.0-rc.1 < 1.0.0
        if (!IsPrerelease || !other.IsPrerelease) return other.IsPrerelease.CompareTo(IsPrerelease);

        var mine = Prerelease.Split('.');
        var theirs = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            bool myNumeric = int.TryParse(mine[i], System.Globalization.NumberStyles.None, null, out int myNumber);
            bool theirNumeric = int.TryParse(theirs[i], System.Globalization.NumberStyles.None, null, out int theirNumber);
            c = (myNumeric, theirNumeric) switch
            {
                (true, true) => myNumber.CompareTo(theirNumber),
                (true, false) => -1,               // numeric identifiers rank below alphanumeric ones
                (false, true) => 1,
                _ => string.CompareOrdinal(mine[i], theirs[i]),
            };
            if (c != 0) return c;
        }
        return mine.Length.CompareTo(theirs.Length);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);
    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";
}
