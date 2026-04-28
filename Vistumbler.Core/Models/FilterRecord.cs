namespace Vistumbler.Core.Models;

/// <summary>
/// Represents a saved AP filter (maps to the Filters table).
/// Each field stores a filter expression string. The special value "*" means "no filter on this field".
/// Expressions support: exact match, comma-separated IN list, range (val1-val2),
/// NOT prefix (&lt;&gt;), LIKE wildcard (%) and combinations thereof.
/// </summary>
public class FilterRecord
{
    public int    FiltId   { get; set; }
    public string FiltName { get; set; } = string.Empty;
    public string FiltDesc { get; set; } = string.Empty;

    // Text / enum filter fields  ("*" = any)
    public string Ssid     { get; set; } = "*";
    public string Bssid    { get; set; } = "*";
    public string Auth     { get; set; } = "*";
    public string Encr     { get; set; } = "*";
    public string RadType  { get; set; } = "*";
    public string NetType  { get; set; } = "*";
    public string Btx      { get; set; } = "*";
    public string Otx      { get; set; } = "*";

    // Integer filter fields (stored as text to allow range/list syntax, "*" = any)
    public string Channel  { get; set; } = "*";
    public string Signal   { get; set; } = "*";   // min signal
    public string HighSig  { get; set; } = "*";   // max signal
    public string Rssi     { get; set; } = "*";   // min RSSI
    public string HighRssi { get; set; } = "*";   // max RSSI
    public string Active   { get; set; } = "*";   // 1 = active only, 0 = dead only
}
