using Vistumbler.Core.Models;

namespace Vistumbler.Core.Extensions;

public static class EnumExtensions
{
    public static string ToLegacyString(this AuthenticationType auth)
    {
        return auth switch
        {
            AuthenticationType.Open => "Open",
            AuthenticationType.Shared => "Shared Key",
            AuthenticationType.WPA => "WPA-Enterprise",
            AuthenticationType.WPA_PSK => "WPA-Personal",
            AuthenticationType.WPA2 => "WPA2-Enterprise",
            AuthenticationType.WPA2_PSK => "WPA2-Personal",
            AuthenticationType.WPA3 => "WPA3-Enterprise",
            AuthenticationType.WPA3_PSK => "WPA3-Personal",
            AuthenticationType.WPA3_Enterprise => "WPA3-Enterprise",
            // AuthenticationType.WPA3_Enterprise_192 => "WPA3-Enterprise-192", // If added to enum
            _ => auth.ToString()
        };
    }

    public static string ToLegacyString(this EncryptionType enc)
    {
        return enc switch
        {
            EncryptionType.None => "None",
            EncryptionType.WEP => "WEP",
            EncryptionType.TKIP => "TKIP",
            EncryptionType.CCMP => "CCMP",
            EncryptionType.AES => "CCMP", // Map AES to CCMP as per Vistumbler logic
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
