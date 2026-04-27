using Vistumbler.Core.Models;

namespace Vistumbler.Core.Extensions;

public static class EnumExtensions
{
    public static string ToLegacyString(this AuthenticationType auth)
    {
        return auth switch
        {
            AuthenticationType.Open               => "Open",
            AuthenticationType.Shared             => "Shared Key",
            AuthenticationType.WPA                => "WPA-Enterprise",
            AuthenticationType.WPA_PSK            => "WPA-Personal",
            AuthenticationType.WPA2               => "WPA2-Enterprise",
            AuthenticationType.WPA2_PSK           => "WPA2-Personal",
            AuthenticationType.WPA3               => "WPA3-Enterprise",
            AuthenticationType.WPA3_PSK           => "WPA3-Personal",
            AuthenticationType.WPA3_Enterprise    => "WPA3-Enterprise",
            AuthenticationType.WPA3_Enterprise_192 => "WPA3-Enterprise-192",
            AuthenticationType.OWE                => "OWE",
            _ => auth.ToString()
        };
    }

    public static string ToLegacyString(this EncryptionType enc)
    {
        return enc switch
        {
            EncryptionType.None         => "None",
            EncryptionType.WEP         => "WEP",
            EncryptionType.TKIP        => "TKIP",
            EncryptionType.CCMP        => "CCMP",
            EncryptionType.AES         => "CCMP",
            EncryptionType.CCMP_256    => "CCMP-256",
            EncryptionType.GCMP        => "GCMP",
            EncryptionType.GCMP_256    => "GCMP-256",
            EncryptionType.BIP         => "BIP",
            EncryptionType.BIP_GMAC_128 => "BIP-GMAC-128",
            EncryptionType.BIP_GMAC_256 => "BIP-GMAC-256",
            EncryptionType.BIP_CMAC_256 => "BIP-CMAC-256",
            _ => enc.ToString()
        };
    }

    public static string ToLegacyString(this NetworkType type)
    {
        return type switch
        {
            NetworkType.Infrastructure => "Infrastructure",
            NetworkType.Adhoc => "Ad Hoc",
            _ => type.ToString()
        };
    }
}
