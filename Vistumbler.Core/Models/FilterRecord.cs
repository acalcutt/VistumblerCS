using Vistumbler.Core.Extensions;
using Vistumbler.Core.Services;

namespace Vistumbler.Core.Models;

/// <summary>
/// Represents a saved AP filter (maps to the Filters table), the original Vistumbler's Filters: each field holds an
/// expression in its syntax (<see cref="FilterExpression"/>) for the AP column of the same name, "*" meaning any.
/// An AP is shown when it passes every field (<see cref="Matches"/>).
/// </summary>
public class FilterRecord
{
    public int    FiltId   { get; set; }
    public string FiltName { get; set; } = string.Empty;
    public string FiltDesc { get; set; } = string.Empty;

    // Text fields
    public string Ssid     { get; set; } = "*";
    public string Bssid    { get; set; } = "*";
    public string Auth     { get; set; } = "*";   // e.g. "WPA2-Personal", as the list shows it
    public string Encr     { get; set; } = "*";   // e.g. "CCMP"
    public string RadType  { get; set; } = "*";
    public string NetType  { get; set; } = "*";   // "Infrastructure" or "Ad Hoc"
    public string Btx      { get; set; } = "*";
    public string Otx      { get; set; } = "*";

    // Number fields (lists and ranges: "1,6,11", "-80--60")
    public string Channel  { get; set; } = "*";
    public string Signal   { get; set; } = "*";   // the current signal, %
    public string HighSig  { get; set; } = "*";   // the highest signal, %
    public string Rssi     { get; set; } = "*";   // the current RSSI, dBm
    public string HighRssi { get; set; } = "*";   // the highest RSSI, dBm
    public string Active   { get; set; } = "*";   // 1 = active, 0 = dead

    /// <summary>Whether an AP passes every field, as the original's filter query lets it through.</summary>
    public bool Matches(AccessPoint ap)
        => FilterExpression.Matches(Ssid, ap.Ssid, numeric: false)
        && FilterExpression.Matches(Bssid, ap.Bssid, numeric: false)
        && FilterExpression.Matches(Channel, ap.Channel.ToString(), numeric: true)
        && FilterExpression.Matches(Auth, ap.Authentication.ToLegacyString(), numeric: false)
        && FilterExpression.Matches(Encr, ap.Encryption.ToLegacyString(), numeric: false)
        && FilterExpression.Matches(RadType, ap.RadioType, numeric: false)
        && FilterExpression.Matches(NetType, ap.NetworkType.ToLegacyString(), numeric: false)
        && FilterExpression.Matches(Btx, ap.BasicTransferRates, numeric: false)
        && FilterExpression.Matches(Otx, ap.OtherTransferRates, numeric: false)
        && FilterExpression.Matches(Signal, (ap.Signal ?? 0).ToString(), numeric: true)
        && FilterExpression.Matches(HighSig, (ap.HighestSignal ?? 0).ToString(), numeric: true)
        && FilterExpression.Matches(Rssi, ap.Rssi?.ToString() ?? "", numeric: true)
        && FilterExpression.Matches(HighRssi, ap.HighestRssi?.ToString() ?? "", numeric: true)
        && FilterExpression.Matches(Active, ap.IsActive ? "1" : "0", numeric: true);

    /// <summary>Whether it lets every AP through (all its fields "*").</summary>
    public bool IsEmpty => new[] { Ssid, Bssid, Channel, Auth, Encr, RadType, NetType, Btx, Otx, Signal, HighSig, Rssi, HighRssi, Active }
        .All(FilterExpression.IsAny);
}
