using Vistumbler.Core.Models;
using Vistumbler.Infrastructure.Export;
using Vistumbler.Infrastructure.Export.M8b;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>WiGLE's Magic 8 Ball: SipHash-keyed MACs and the 1 km MGRS squares they were seen in.</summary>
public class M8bTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("m8b").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void SipHashMatchesThePublishedTestVector()
    {
        // the reference implementation's vector: key 00..0f, message 00..0e
        var key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var data = Enumerable.Range(0, 15).Select(i => (byte)i).ToArray();
        Assert.Equal(0xa129ca6149be45e5UL, SipHash24.Hash(key, data));
    }

    [Fact]
    public void MgrsGivesTheKnownSquare()
    {
        // the Washington Monument, 18S UJ 23487 06483
        Assert.Equal("18SUJ2306", MgrsSquare.FromLatLon(38.8895, -77.0353)!.Value.Text);
    }

    [Fact]
    public async Task TheExportReadsBackAndLocates()
    {
        var aps = new List<AccessPoint>
        {
            new() { Bssid = "00:11:22:33:44:55", Latitude = 38.8895, Longitude = -77.0353 },
            new() { Bssid = "66:77:88:99:AA:BB", Latitude = 38.8896, Longitude = -77.0354 },
            new() { Bssid = "CC:DD:EE:FF:00:11" },   // no position
        };
        var file = Path.Combine(_dir, "out.m8b");
        Assert.Equal(2, await new ExportService().ExportToM8bAsync(file, aps));

        var m8b = M8bFile.Read(file);
        Assert.Equal(2, m8b.RecordCount);
        Assert.Equal(new[] { "18SUJ2306" }, m8b.SquaresFor("00-11-22-33-44-55"));
        var best = M8bFile.Locate(new[] { m8b }, new[] { "00:11:22:33:44:55", "66:77:88:99:aa:bb" })[0];
        Assert.Equal(("18SUJ2306", 2), (best.Square, best.Count));
        Assert.InRange(best.Latitude, 38.88, 38.90);
    }
}
