using System.Text;
using ManagedNativeWifi;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.WiFi;

/// <summary>
/// WiFi scanner using Native WiFi API
/// </summary>
public class NativeWiFiScanner : IWiFiScannerService
{
    private bool _isScanning;
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _activeAdapterId;
    private const int ScanIntervalMs = 1000;

    public event EventHandler<AccessPointsDetectedEventArgs>? AccessPointsDetected;
    public event EventHandler<ScanErrorEventArgs>? ScanError;

    public bool IsScanning => _isScanning;

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
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            OnScanError(new ScanErrorEventArgs 
            { 
                ErrorMessage = "Error during scanning", 
                Exception = ex 
            });
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
                OnScanError(new ScanErrorEventArgs 
                { 
                    ErrorMessage = "Error enumerating adapters", 
                    Exception = ex 
                });
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
                foreach (var interfaceInfo in NativeWifi.EnumerateInterfaces())
                {
                    if (_activeAdapterId != null && interfaceInfo.Id.ToString() != _activeAdapterId)
                        continue;

                    var connection = NativeWifi.GetConnectionAttributes(interfaceInfo.Id);
                    if (connection != null)
                    {
                        return BitConverter.ToString(connection.wlanAssociationAttributes.dot11Bssid)
                            .Replace("-", ":");
                    }
                }
            }
            catch
            {
                // Ignore errors when getting connected AP
            }

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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnScanError(new ScanErrorEventArgs 
                { 
                    ErrorMessage = "Error in scan loop", 
                    Exception = ex 
                });
                
                // Continue scanning despite errors
                await Task.Delay(ScanIntervalMs, cancellationToken);
            }
        }
    }

    private Task<List<AccessPoint>> ScanNetworksAsync()
    {
        return Task.Run(() =>
        {
            var accessPoints = new List<AccessPoint>();

            try
            {
                foreach (var interfaceInfo in NativeWifi.EnumerateInterfaces())
                {
                    if (_activeAdapterId != null && interfaceInfo.Id.ToString() != _activeAdapterId)
                        continue;

                    // Trigger scan
                    NativeWifi.Scan(interfaceInfo.Id);

                    // Get BSS list
                    var bssEntries = NativeWifi.EnumerateBssNetworks(interfaceInfo.Id);

                    foreach (var bss in bssEntries)
                    {
                        var ap = new AccessPoint
                        {
                            Bssid = BitConverter.ToString(bss.Bssid).Replace("-", ":"),
                            Ssid = Encoding.UTF8.GetString(bss.Ssid),
                            Signal = bss.LinkQuality,
                            Rssi = bss.Rssi,
                            Channel = GetChannelFromFrequency(bss.ChCenterFrequency),
                            RadioType = GetRadioTypeName(bss.PhyType),
                            NetworkType = GetNetworkType(bss.BssType),
                            LastSeen = DateTime.Now,
                            IsActive = true
                        };

                        // Parse security information
                        ParseSecurityInfo(bss, ap);

                        accessPoints.Add(ap);
                    }
                }
            }
            catch (Exception ex)
            {
                OnScanError(new ScanErrorEventArgs 
                { 
                    ErrorMessage = "Error scanning networks", 
                    Exception = ex 
                });
            }

            return accessPoints;
        });
    }

    private void ParseSecurityInfo(BssNetworkPack bss, AccessPoint ap)
    {
        // Simplified security parsing - would need more detailed implementation
        // based on the BSS network data
        if (bss.BssType == BssType.Infrastructure)
        {
            ap.NetworkType = NetworkType.Infrastructure;
        }
        else if (bss.BssType == BssType.Independent)
        {
            ap.NetworkType = NetworkType.Adhoc;
        }

        // Default values - would need proper parsing from BSS data
        ap.Authentication = AuthenticationType.Unknown;
        ap.Encryption = EncryptionType.Unknown;
    }

    private int GetChannelFromFrequency(uint frequency)
    {
        // 2.4 GHz band
        if (frequency >= 2412 && frequency <= 2484)
        {
            if (frequency == 2484)
                return 14;
            return (int)((frequency - 2412) / 5) + 1;
        }

        // 5 GHz band
        if (frequency >= 5170 && frequency <= 5825)
        {
            return (int)((frequency - 5170) / 5) + 34;
        }

        return 0;
    }

    private string GetRadioTypeName(PhyType phyType)
    {
        return phyType switch
        {
            PhyType.B => "802.11b",
            PhyType.G => "802.11g",
            PhyType.N => "802.11n",
            PhyType.A => "802.11a",
            PhyType.AC => "802.11ac",
            PhyType.AX => "802.11ax",
            _ => "Unknown"
        };
    }

    private NetworkType GetNetworkType(BssType bssType)
    {
        return bssType switch
        {
            BssType.Infrastructure => NetworkType.Infrastructure,
            BssType.Independent => NetworkType.Adhoc,
            _ => NetworkType.Unknown
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
