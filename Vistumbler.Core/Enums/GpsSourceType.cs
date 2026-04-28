namespace Vistumbler.Core.Enums;

/// <summary>Selects which GPS back-end the application uses.</summary>
public enum GpsSourceType
{
    /// <summary>A serial / COM-port connected GPS device (NMEA sentences).</summary>
    Serial,

    /// <summary>Windows Location API – uses GPS hardware, Wi-Fi positioning, or cell-tower
    /// location depending on what the platform provides.</summary>
    WindowsLocation,
}
