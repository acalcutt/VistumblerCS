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
    WPA3_PSK,           // SAE
    WPA_Enterprise,
    WPA2_Enterprise,
    WPA3_Enterprise,
    WPA3_Enterprise_192,
    WPA_None,
    OWE,
    IHV
}

public enum EncryptionType
{
    Unknown,
    None,
    WEP,                // Generic WEP, WEP40, WEP104
    TKIP,
    AES,                // CCMP
    CCMP,
    GCMP,
    GCMP_256,
    CCMP_256,
    BIP,
    BIP_GMAC_128,
    BIP_GMAC_256,
    BIP_CMAC_256,
    IHV
}
