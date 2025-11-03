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

                    var bssNetworks = NativeWifi.EnumerateBssNetworks();

                    foreach (var bss in bssNetworks)
                    {
                        var ap = new AccessPoint
                        {
                            Bssid = Convert.ToHexString(bss.Bssid.ToBytes()).Insert(2, ":").Insert(5, ":").Insert(8, ":").Insert(11, ":").Insert(14, ":"),
                            Ssid = Encoding.UTF8.GetString(bss.Ssid.ToBytes()).TrimEnd('\0'),
                            Signal = bss.LinkQuality,
                            Channel = GetChannelFromFrequency(bss.Frequency),
                            // FIX: Use Band property
                            RadioType = bss.Band.ToString(),
                            NetworkType = bss.BssType == BssType.Infrastructure ? NetworkType.Infrastructure : NetworkType.Adhoc,
                            LastSeen = DateTime.Now,
                            IsActive = true
                        };

                        ap.Rssi = -100 + ap.Signal.GetValueOrDefault();
                        ap.Authentication = AuthenticationType.Unknown;
                        ap.Encryption = Core.Models.EncryptionType.Unknown;

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

    private int GetChannelFromFrequency(int frequency)
    {
        if (frequency >= 2412 && frequency <= 2484)
        {
            if (frequency == 2484)
                return 14;
            return (frequency - 2412) / 5 + 1;
        }

        if (frequency >= 5170 && frequency <= 5825)
        {
            return (frequency - 5170) / 5 + 34;
        }

        return 0;
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