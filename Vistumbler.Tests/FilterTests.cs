using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>The original Vistumbler's filter syntax, and a saved filter applied to an AP.</summary>
public class FilterTests
{
    [Theory]
    [InlineData("*", "anything", false, true)]
    [InlineData("", "anything", false, true)]
    [InlineData("6", "6", true, true)]
    [InlineData("6", "11", true, false)]
    [InlineData("1,6,11", "11", true, true)]
    [InlineData("1,6,11", "3", true, false)]
    [InlineData("<>6", "6", true, false)]
    [InlineData("<>6,<>11", "1", true, true)]
    [InlineData("<>6,<>11", "11", true, false)]
    [InlineData("1-6", "4", true, true)]
    [InlineData("1-6", "7", true, false)]
    [InlineData("-80--60", "-70", true, true)]
    [InlineData("-80--60", "-50", true, false)]
    [InlineData("<>1-6", "4", true, false)]
    [InlineData("linksys%", "Linksys-Guest", false, true)]
    [InlineData("%guest%", "My Guest Net", false, true)]
    [InlineData("%guest%", "Home", false, false)]
    [InlineData("WPA2-Personal", "wpa2-personal", false, true)]   // a dash in a value, not a range
    [InlineData("a\\,b", "a,b", false, true)]
    [InlineData("100\\%", "100%", false, true)]
    [InlineData("6", "", true, false)]
    public void TheOriginalsSyntax(string expression, string value, bool numeric, bool matches)
        => Assert.Equal(matches, FilterExpression.Matches(expression, value, numeric));

    private static AccessPoint Ap() => new()
    {
        Ssid = "Linksys-Guest", Bssid = "00:11:22:33:44:55", Channel = 6,
        Authentication = AuthenticationType.WPA2_PSK, Encryption = EncryptionType.CCMP,
        NetworkType = NetworkType.Infrastructure, RadioType = "802.11n",
        Signal = 70, HighestSignal = 90, Rssi = -65, HighestRssi = -55, IsActive = true,
    };

    [Fact]
    public void AnEmptyFilterLetsEveryApThrough()
    {
        var filter = new FilterRecord();
        Assert.True(filter.IsEmpty);
        Assert.True(filter.Matches(Ap()));
    }

    [Fact]
    public void AnApHasToPassEveryField()
    {
        Assert.True(new FilterRecord { Ssid = "linksys%", Channel = "1,6,11", Auth = "WPA2-Personal", Active = "1" }.Matches(Ap()));
        Assert.False(new FilterRecord { Ssid = "linksys%", Channel = "1,11" }.Matches(Ap()));
        Assert.True(new FilterRecord { HighRssi = "-60-0" }.Matches(Ap()));
        Assert.False(new FilterRecord { Rssi = "-60-0" }.Matches(Ap()));    // the current RSSI is -65
        Assert.False(new FilterRecord { Active = "0" }.Matches(Ap()));
        Assert.True(new FilterRecord { NetType = "<>Ad Hoc", Encr = "CCMP,GCMP" }.Matches(Ap()));
    }
}
