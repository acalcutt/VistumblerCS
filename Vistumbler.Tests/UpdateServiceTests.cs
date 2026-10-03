using System.Net;
using Vistumbler.Core.Services;
using Xunit;

namespace Vistumbler.Tests;

public class UpdateServiceTests
{
    private static SemanticVersion V(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var v), $"'{text}' should parse");
        return v;
    }

    [Theory]
    [InlineData("0.4.5", "0.4.6")]
    [InlineData("0.4.9", "0.5.0")]
    [InlineData("0.9.0", "1.0.0")]
    [InlineData("1.0.0-rc.1", "1.0.0")]          // stable outranks its pre-releases
    [InlineData("1.0.0-alpha.1", "1.0.0-beta.1")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10")]    // numeric identifiers compare as numbers
    [InlineData("1.0.0-rc", "1.0.0-rc.1")]       // fewer identifiers rank lower
    [InlineData("1.0.0-1", "1.0.0-alpha")]       // numeric ranks below alphanumeric
    [InlineData("0.3.9-pre.1", "0.3.9")]
    public void Orders_versions_by_semver_precedence(string lower, string higher)
    {
        Assert.True(V(lower).CompareTo(V(higher)) < 0);
        Assert.True(V(higher).CompareTo(V(lower)) > 0);
    }

    [Theory]
    [InlineData("v0.4.5", "0.4.5")]
    [InlineData("0.5", "0.5.0")]
    [InlineData("1.2.3+build.7", "1.2.3")]
    [InlineData("v1.0.0-rc.1", "1.0.0-rc.1")]
    public void Parses_tags(string tag, string expected) => Assert.Equal(expected, V(tag).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-rc..1")]
    [InlineData("forks-list-20261002")]
    public void Rejects_invalid_versions(string tag) => Assert.False(SemanticVersion.TryParse(tag, out _));

    private static List<ReleaseInfo> Releases(params string[] versions) =>
        versions.Select(v => new ReleaseInfo(V(v), "v" + v, "", "", [])).ToList();

    [Fact]
    public void Offers_the_newest_stable_release()
    {
        var update = UpdateService.SelectUpdate(Releases("0.4.4", "0.4.6", "0.5.0-rc.1", "0.4.5"), V("0.4.5"), includePrereleases: false);
        Assert.Equal("0.4.6", update?.Version.ToString());
    }

    [Fact]
    public void Offers_prereleases_only_when_included()
    {
        var releases = Releases("0.4.5", "0.5.0-rc.1");
        Assert.Null(UpdateService.SelectUpdate(releases, V("0.4.5"), includePrereleases: false));
        Assert.Equal("0.5.0-rc.1", UpdateService.SelectUpdate(releases, V("0.4.5"), includePrereleases: true)?.Version.ToString());
    }

    [Fact]
    public void Running_a_prerelease_includes_newer_prereleases_and_the_final_release()
    {
        Assert.Equal("0.5.0-rc.2",
            UpdateService.SelectUpdate(Releases("0.5.0-rc.1", "0.5.0-rc.2"), V("0.5.0-rc.1"), false)?.Version.ToString());
        Assert.Equal("0.5.0",
            UpdateService.SelectUpdate(Releases("0.5.0-rc.2", "0.5.0"), V("0.5.0-rc.1"), false)?.Version.ToString());
    }

    [Fact]
    public void Up_to_date_returns_null() =>
        Assert.Null(UpdateService.SelectUpdate(Releases("0.4.4", "0.4.5"), V("0.4.5"), includePrereleases: true));

    private const string GitLabJson = """
        [
          { "tag_name": "v0.5.0", "description": "## Changes\n- New", "upcoming_release": false,
            "_links": { "self": "https://gitlab.techidiots.net/techidiots-llc/VistumblerCS/-/releases/v0.5.0" },
            "assets": { "links": [
              { "name": "VistumblerCS-v0.5.0-win-x64-setup.exe",
                "url": "https://gitlab.techidiots.net/api/v4/projects/1/packages/generic/vistumblercs/0.5.0/VistumblerCS-v0.5.0-win-x64-setup.exe",
                "direct_asset_url": "https://gitlab.techidiots.net/techidiots-llc/VistumblerCS/-/releases/v0.5.0/downloads/VistumblerCS-v0.5.0-win-x64-setup.exe" },
              { "name": "VistumblerCS-v0.5.0-win-arm64-setup.exe",
                "direct_asset_url": "https://gitlab.techidiots.net/techidiots-llc/VistumblerCS/-/releases/v0.5.0/downloads/VistumblerCS-v0.5.0-win-arm64-setup.exe" } ] } },
          { "tag_name": "v0.6.0", "upcoming_release": true, "assets": { "links": [] } },
          { "tag_name": "not-a-version", "assets": { "links": [] } }
        ]
        """;

    [Fact]
    public void Parses_gitlab_releases()
    {
        var r = Assert.Single(UpdateService.ParseGitLabReleases(GitLabJson));   // upcoming and unparseable tags skipped
        Assert.Equal("0.5.0", r.Version.ToString());
        Assert.Equal("## Changes\n- New", r.Notes);
        Assert.EndsWith("/-/releases/v0.5.0", r.PageUrl);
        Assert.Equal(
            "https://gitlab.techidiots.net/techidiots-llc/VistumblerCS/-/releases/v0.5.0/downloads/VistumblerCS-v0.5.0-win-arm64-setup.exe",
            r.FindAsset("-win-arm64-setup.exe")?.Url);
        Assert.Null(r.FindAsset("-android.apk"));
    }

    private const string GitHubJson = """
        [
          { "tag_name": "v0.5.0", "body": "Notes", "draft": false, "prerelease": false,
            "html_url": "https://github.com/acalcutt/VistumblerCS/releases/tag/v0.5.0",
            "assets": [ { "name": "VistumblerCS-v0.5.0-win-x64-setup.exe",
                          "browser_download_url": "https://github.com/acalcutt/VistumblerCS/releases/download/v0.5.0/VistumblerCS-v0.5.0-win-x64-setup.exe" } ] },
          { "tag_name": "v0.6.0", "draft": true, "assets": [] }
        ]
        """;

    [Fact]
    public void Parses_github_releases()
    {
        var r = Assert.Single(UpdateService.ParseGitHubReleases(GitHubJson));   // drafts skipped
        Assert.Equal("0.5.0", r.Version.ToString());
        Assert.Equal("Notes", r.Notes);
        Assert.Equal("https://github.com/acalcutt/VistumblerCS/releases/tag/v0.5.0", r.PageUrl);
        Assert.NotNull(r.FindAsset("-win-x64-setup.exe"));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.Host);
            return Task.FromResult(respond(request));
        }
    }

    private static UpdateService Service(FakeHandler handler, string current = "0.4.5") =>
        new(new HttpClient(handler), "VistumblerCS", current, "techidiots-llc/VistumblerCS", "acalcutt/VistumblerCS");

    [Fact]
    public async Task Uses_gitlab_first()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(GitLabJson) });
        var result = await Service(handler).CheckAsync(includePrereleases: false);

        Assert.Equal("GitLab", result.Source);
        Assert.Equal("0.5.0", result.Update?.Version.ToString());
        Assert.Equal(["gitlab.techidiots.net"], handler.Requests);
    }

    [Fact]
    public async Task Falls_back_to_github_when_gitlab_fails()
    {
        var handler = new FakeHandler(req => req.RequestUri!.Host == "gitlab.techidiots.net"
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(GitHubJson) });
        var result = await Service(handler).CheckAsync(includePrereleases: false);

        Assert.Equal("GitHub", result.Source);
        Assert.Equal("0.5.0", result.Update?.Version.ToString());
        Assert.Equal(["gitlab.techidiots.net", "api.github.com"], handler.Requests);
    }

    [Fact]
    public async Task Takes_the_installer_from_github_when_the_gitlab_release_lacks_it()
    {
        // GitLab's v0.5.0 has x64/arm64 setups but no APK; GitHub's v0.5.0 has the x64 setup only
        var gitHubWithApk = GitHubJson.Replace("\"assets\": [ {",
            "\"assets\": [ { \"name\": \"VistumblerCS-v0.5.0-android.apk\", \"browser_download_url\": \"https://github.com/x.apk\" }, {");
        var handler = new FakeHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.Host == "gitlab.techidiots.net" ? GitLabJson : gitHubWithApk),
        });

        var x64 = await Service(handler).CheckAsync(false, "-win-x64-setup.exe");
        Assert.Equal("GitLab", x64.Source);                                 // GitLab has it: no second request

        var apk = await Service(handler).CheckAsync(false, "-android.apk");
        Assert.Equal("GitHub", apk.Source);
        Assert.Equal("https://github.com/x.apk", apk.Update?.FindAsset("-android.apk")?.Url);

        var missing = await Service(handler).CheckAsync(false, "-win-riscv64-setup.exe");
        Assert.Equal("GitLab", missing.Source);                             // nowhere: keep the primary feed's release
        Assert.Equal("0.5.0", missing.Update?.Version.ToString());
    }

    [Fact]
    public async Task Throws_when_every_feed_fails()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<UpdateCheckException>(() => Service(handler).CheckAsync(includePrereleases: false));
    }

    [Fact]
    public async Task Reports_up_to_date()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(GitLabJson) });
        var result = await Service(handler, current: "0.5.0").CheckAsync(includePrereleases: true);
        Assert.False(result.IsUpdateAvailable);
    }
}
