using System.Text;
using ManagedNativeWifi;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.WiFi;

public class NativeWiFiScanner : IWiFiScannerService
{
    private bool _isScanning;
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _activeAdapterId;
    private const int ScanIntervalMs = 1000;

    public event EventHandler<AccessPointsDetectedEventArgs>? AccessPointsDetected;
    public event EventHandler<ScanErrorEventArgs>? ScanError;

    public bool IsScanning => _isScanning;

    // --- Interface Implementations (Must be public) ---

    public async Task StartScanningAsync(CancellationToken cancellationToken = default)
    {
        if (_isScanning)
            return;

        _isScanning = true;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await Task.Run(async () => await ScanLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }
        catch (OperationCanceledException) { /* Expected when stopping */ }
        catch (Exception ex)
        {
            OnScanError(new ScanErrorEventArgs { ErrorMessage = "Error during scanning", Exception = ex });
        }
        finally
        {
            _isScanning = false;
        }
    }

    public void StopScanning()
    {
        _cancellationTokenSource?.Cancel();
        _isScanning = false;
    }

    public async Task<List<WiFiAdapter>> GetAvailableAdaptersAsync()
    {
        return await Task.Run(() =>
        {
            var adapters = new List<WiFiAdapter>();
            try
            {
                foreach (var interfaceInfo in NativeWifi.EnumerateInterfaces())
                {
                    adapters.Add(new WiFiAdapter
                    {
                        Id = interfaceInfo.Id.ToString(),
                        Name = interfaceInfo.Description,
                        Description = interfaceInfo.Description
                    });
                }
            }
            catch (Exception ex)
            {
                OnScanError(new ScanErrorEventArgs { ErrorMessage = "Error enumerating adapters", Exception = ex });
            }
            return adapters;
        });
    }

    public void SetActiveAdapter(string adapterId)
    {
        _activeAdapterId = adapterId;
    }
    public async Task<string?> GetConnectedBssidAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                string? connectedInterfaceId = null;

                // First, find which interface is connected
                foreach (var interfaceInfo in NativeWifi.EnumerateInterfaces())
                {
                    if (_activeAdapterId != null && interfaceInfo.Id.ToString() != _activeAdapterId)
                        continue;

                    if (interfaceInfo.State == InterfaceState.Connected)
                    {
                        connectedInterfaceId = interfaceInfo.Id.ToString();
                        break;
                    }
                }

                if (connectedInterfaceId == null)
                    return null;

                // Now check if we have a connection for this interface
                foreach (var connection in NativeWifi.EnumerateInterfaceConnections())
                {
                    if (connection.Id.ToString() == connectedInterfaceId)
                    {
                        // We found the connection - now we need to find the BSSID
                        // Since InterfaceConnectionInfo doesn't expose much, 
                        // we'll find the strongest signal BSS network as a best guess
                        BssNetworkPack strongestBss = default!;
                        int maxSignal = -1;
                        bool foundAny = false;

                        foreach (var bss in NativeWifi.EnumerateBssNetworks())
                        {
                            if (bss.LinkQuality > maxSignal)
                            {
                                maxSignal = bss.LinkQuality;
                                strongestBss = bss;
                                foundAny = true;
                            }
                        }

                        if (foundAny)
                        {
                            return Convert.ToHexString(strongestBss.Bssid.ToBytes())
                                .Insert(2, ":").Insert(5, ":").Insert(8, ":")
                                .Insert(11, ":").Insert(14, ":");
                        }
                    }
                }
            }
            catch { /* Ignore errors when getting connected AP */ }
            return null;
        });
    }

    private async Task ScanLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var accessPoints = await ScanNetworksAsync();
                OnAccessPointsDetected(new AccessPointsDetectedEventArgs { AccessPoints = accessPoints });
                await Task.Delay(ScanIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                OnScanError(new ScanErrorEventArgs { ErrorMessage = "Error in scan loop", Exception = ex });
                await Task.Delay(ScanIntervalMs, cancellationToken);
            }
        }
    }

    private async Task<List<AccessPoint>> ScanNetworksAsync()
    {
        return await Task.Run(async () =>
        {
            var accessPoints = new List<AccessPoint>();

            try
            {
                foreach (var interfaceInfo in NativeWifi.EnumerateInterfaces())
                {
                    if (_activeAdapterId != null && interfaceInfo.Id.ToString() != _activeAdapterId)
                        continue;

                    try
                    {
                        // FIX: ScanNetworksAsync takes TimeSpan as first parameter
                        await NativeWifi.ScanNetworksAsync(TimeSpan.FromSeconds(4));
                    }
                    catch { /* Scan may fail if already in progress */ }

                    // We need AvailableNetworks to get Auth/Cipher info
                    var availableNetworks = NativeWifi.EnumerateAvailableNetworks()
                        .GroupBy(x => x.Ssid.ToString())
                        .ToDictionary(g => g.Key, g => g.First());

                    var bssNetworks = NativeWifi.EnumerateBssNetworks();

                    foreach (var bss in bssNetworks)
                    {
                        var ssid = Encoding.UTF8.GetString(bss.Ssid.ToBytes()).TrimEnd('\0');
                        var bssid = Convert.ToHexString(bss.Bssid.ToBytes()).Insert(2, ":").Insert(5, ":").Insert(8, ":").Insert(11, ":").Insert(14, ":");

                        var (channel, freqMhz) = GetChannelAndFreq(bss.Frequency);

                        var ap = new AccessPoint
                        {
                            Bssid = bssid,
                            Ssid = ssid,
                            Signal = bss.LinkQuality,
                            Channel = channel,
                            FrequencyMhz = freqMhz,
                            RadioType = MapPhyType(bss.PhyType),
                            NetworkType = bss.BssType == BssType.Infrastructure ? NetworkType.Infrastructure : NetworkType.Adhoc,
                            FirstSeen = DateTime.Now,
                            LastSeen = DateTime.Now,
                            IsActive = true
                        };

                        ap.Rssi = -100 + ap.Signal.GetValueOrDefault();
                        
                        // Default to Unknown
                        ap.Authentication = AuthenticationType.Unknown;
                        ap.Encryption = Vistumbler.Core.Models.EncryptionType.Unknown;

                        // Try to find matching Available Network for security info
                        if (availableNetworks.TryGetValue(ssid, out var availableNetwork))
                        {
                            ap.Authentication = MapAuthentication(availableNetwork.AuthenticationAlgorithm);
                            ap.Encryption = MapEncryption(availableNetwork.CipherAlgorithm);
                        }

                        accessPoints.Add(ap);
                    }
                }
            }
            catch (Exception ex)
            {
                OnScanError(new ScanErrorEventArgs { ErrorMessage = "Error scanning networks", Exception = ex });
            }

            return accessPoints;
        });
    }

    // --- Helper and Event Methods ---

    private static (int channel, int freqMhz) GetChannelAndFreq(int frequency)
    {
        // ManagedNativeWifi returns frequency in KHz
        int freqMhz = frequency > 1_000_000 ? frequency / 1000 : frequency;

        // 2.4 GHz band
        if (freqMhz >= 2412 && freqMhz <= 2484)
        {
            int ch = freqMhz == 2484 ? 14 : (freqMhz - 2407) / 5;
            return (ch, freqMhz);
        }

        // 5 GHz band (5150–5925 MHz)  chan = (freq - 5000) / 5
        if (freqMhz >= 5150 && freqMhz < 5925)
            return ((freqMhz - 5000) / 5, freqMhz);

        // 6 GHz band (Wi-Fi 6E / Wi-Fi 7) 5925–7125 MHz  chan = (freq - 5950) / 5
        if (freqMhz >= 5925 && freqMhz <= 7125)
            return ((freqMhz - 5950) / 5, freqMhz);

        return (0, freqMhz);
    }

    // Legacy wrapper kept for any remaining callers
    private int GetChannelFromFrequency(int frequency)
        => GetChannelAndFreq(frequency).channel;
    
    private AuthenticationType MapAuthentication(AuthenticationAlgorithm algo)
    {
        return algo switch
        {
            AuthenticationAlgorithm.Open         => AuthenticationType.Open,
            AuthenticationAlgorithm.Shared       => AuthenticationType.Shared,
            AuthenticationAlgorithm.WPA          => AuthenticationType.WPA,
            AuthenticationAlgorithm.WPA_PSK      => AuthenticationType.WPA_PSK,
            AuthenticationAlgorithm.WPA_NONE     => AuthenticationType.WPA_None,
            AuthenticationAlgorithm.RSNA         => AuthenticationType.WPA2,
            AuthenticationAlgorithm.RSNA_PSK     => AuthenticationType.WPA2_PSK,
            AuthenticationAlgorithm.WPA3_ENT_192 => AuthenticationType.WPA3_Enterprise_192,
            AuthenticationAlgorithm.WPA3_ENT     => AuthenticationType.WPA3_Enterprise,
            AuthenticationAlgorithm.WPA3_SAE     => AuthenticationType.WPA3_PSK,
            AuthenticationAlgorithm.OWE          => AuthenticationType.OWE,
            _ => AuthenticationType.Unknown
        };
    }

    private Vistumbler.Core.Models.EncryptionType MapEncryption(CipherAlgorithm cipher)
    {
        return cipher switch
        {
            CipherAlgorithm.None          => Vistumbler.Core.Models.EncryptionType.None,
            CipherAlgorithm.WEP           => Vistumbler.Core.Models.EncryptionType.WEP,
            CipherAlgorithm.WEP_40        => Vistumbler.Core.Models.EncryptionType.WEP,
            CipherAlgorithm.WEP_104       => Vistumbler.Core.Models.EncryptionType.WEP,
            CipherAlgorithm.TKIP          => Vistumbler.Core.Models.EncryptionType.TKIP,
            CipherAlgorithm.CCMP          => Vistumbler.Core.Models.EncryptionType.CCMP,
            CipherAlgorithm.CCMP_256      => Vistumbler.Core.Models.EncryptionType.CCMP_256,
            CipherAlgorithm.BIP           => Vistumbler.Core.Models.EncryptionType.BIP,
            CipherAlgorithm.GCMP          => Vistumbler.Core.Models.EncryptionType.GCMP,
            CipherAlgorithm.GCMP_256      => Vistumbler.Core.Models.EncryptionType.GCMP_256,
            CipherAlgorithm.BIP_GMAC_128  => Vistumbler.Core.Models.EncryptionType.BIP_GMAC_128,
            CipherAlgorithm.BIP_GMAC_256  => Vistumbler.Core.Models.EncryptionType.BIP_GMAC_256,
            CipherAlgorithm.BIP_CMAC_256  => Vistumbler.Core.Models.EncryptionType.BIP_CMAC_256,
            _ => Vistumbler.Core.Models.EncryptionType.Unknown
        };
    }

    private string MapPhyType(PhyType phy)
    {
        return phy switch
        {
            PhyType.Fhss => "Bluetooth",
            PhyType.Dsss => "802.11b*",
            PhyType.IrBaseband => "Legacy Infrared",
            PhyType.Ofdm => "802.11a",
            PhyType.HrDsss => "802.11b",
            PhyType.Erp => "802.11g",
            PhyType.Ht => "802.11n",
            PhyType.Vht => "802.11ac",
            PhyType.Dmg => "802.11ad",
            PhyType.He => "802.11ax",
            PhyType.Eht => "802.11be",
            _ => "Unknown"
        };
    }

    protected virtual void OnAccessPointsDetected(AccessPointsDetectedEventArgs e)
    {
        AccessPointsDetected?.Invoke(this, e);
    }

    protected virtual void OnScanError(ScanErrorEventArgs e)
    {
        ScanError?.Invoke(this, e);
    }
}