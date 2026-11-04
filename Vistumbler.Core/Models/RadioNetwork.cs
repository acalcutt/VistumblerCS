namespace Vistumbler.Core.Models;

/// <summary>
/// The kinds of non-Wi-Fi network, named as WiGLE's CSV Type column names them (and as VS1 4.1 and WifiDB do).
/// </summary>
public enum RadioNetworkType
{
    BT,     // Bluetooth classic
    BLE,    // Bluetooth Low Energy
    GSM,
    CDMA,
    WCDMA,
    LTE,
    NR,     // 5G
}

/// <summary>
/// A cell tower or Bluetooth device, kept apart from the Wi-Fi access points as WiGLE, VistumblerMAUI and WifiDB keep
/// them. Both are scanned when the Interface menu turns them on, and come in with a VS1 4.1 file or a WiGLE CSV and go out
/// again with the same formats. The fields are WiGLE's:
/// <list type="bullet">
/// <item>Cells: <see cref="Key"/> is "mccmnc_lac_cid" (TAC for LTE and NR; "sid_nid_bid" for CDMA), <see cref="Name"/>
/// the operator, <see cref="Capabilities"/> "LTE;mccmnc", <see cref="Channel"/> the ARFCN, UARFCN, EARFCN or NRARFCN.</item>
/// <item>Bluetooth: <see cref="Key"/> is the device's MAC, <see cref="Name"/> its name, <see cref="Capabilities"/> its
/// device class, <see cref="Frequency"/> the device class code, <see cref="MfgrId"/> a BLE advertisement's manufacturer
/// id.</item>
/// </list>
/// </summary>
public class RadioNetwork
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public RadioNetworkType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Capabilities { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public int? Channel { get; set; }
    public int Frequency { get; set; }
    public int? MfgrId { get; set; }
    public int Rssi { get; set; }
    public int HighestRssi { get; set; } = int.MinValue;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    /// <summary>Where it was strongest.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Its readings, when loaded (an import, an export).</summary>
    public List<RadioReading> History { get; set; } = new();

    public bool IsBluetooth => Type is RadioNetworkType.BT or RadioNetworkType.BLE;
}

/// <summary>One reading of a <see cref="RadioNetwork"/>, with the GPS fix it was taken at.</summary>
public class RadioReading
{
    public int Id { get; set; }
    public int NetworkId { get; set; }
    public int? GpsId { get; set; }
    public int Rssi { get; set; }
    public DateTime Timestamp { get; set; }
    public GpsData? GpsData { get; set; }
}
