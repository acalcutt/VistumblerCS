namespace Vistumbler.Core.Models;

public enum NetworkType
{
    Unknown,
    Infrastructure,
    Adhoc
}

public enum AuthenticationType
{
    Unknown,
    Open,
    Shared,
    WPA,
    WPA2,
    WPA3,
    WPA_PSK,
    WPA2_PSK,
    WPA3_PSK,
    WPA_Enterprise,
    WPA2_Enterprise,
    WPA3_Enterprise
}

public enum EncryptionType
{
    Unknown,
    None,
    WEP,
    TKIP,
    AES,
    CCMP
}
