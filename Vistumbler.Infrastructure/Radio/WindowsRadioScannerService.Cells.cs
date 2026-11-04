using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using Windows.Devices.Enumeration;
using Windows.Networking.NetworkOperators;

namespace Vistumbler.Infrastructure.Radio;

/// <summary>
/// Cell towers from cellular modems, through Windows' mobile broadband API: the serving and neighbouring cells each
/// modem reports, polled every few seconds. A cell is listed only with its full id (a neighbour reported by its
/// physical id alone isn't), keyed as WiGLE keys it.
/// </summary>
public sealed partial class WindowsRadioScannerService
{
    private static readonly TimeSpan CellPollInterval = TimeSpan.FromSeconds(5);

    private readonly List<MobileBroadbandModem> _modems = new();
    private Timer? _cellTimer;
    private int _cellPolling;

    public async Task<IReadOnlyList<CellularModem>> GetCellularModemsAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(MobileBroadbandModem.GetDeviceSelector());
            return devices.Select(d => new CellularModem(d.Id, d.Name)).ToList();
        }
        catch
        {
            return Array.Empty<CellularModem>();   // no mobile broadband service, or no access to it
        }
    }

    private void StartCells(IReadOnlyCollection<string> modemIds)
    {
        foreach (var id in modemIds)
        {
            try
            {
                if (MobileBroadbandModem.FromId(id) is { } modem) _modems.Add(modem);
            }
            catch
            {
                // unplugged since the menu listed it
            }
        }
        if (_modems.Count > 0)
            _cellTimer = new Timer(_ => _ = PollCellsAsync(), null, TimeSpan.Zero, CellPollInterval);
    }

    private void StopCells()
    {
        _cellTimer?.Dispose();
        _cellTimer = null;
        _modems.Clear();
    }

    private async Task PollCellsAsync()
    {
        // A slow modem doesn't get a second poll stacked on the first
        if (Interlocked.Exchange(ref _cellPolling, 1) == 1) return;
        try
        {
            foreach (var modem in _modems.ToList())
            {
                if (!_running) return;
                try
                {
                    var network = modem.CurrentNetwork;
                    if (network is null) continue;
                    var info = await network.GetCellsInfoAsync();
                    if (info is null) continue;
                    foreach (var n in ReadCells(info, network.RegisteredProviderId, network.RegisteredProviderName))
                        Heard(n);
                }
                catch
                {
                    // the modem's off, or doesn't report cells
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _cellPolling, 0);
        }
    }

    /// <summary>The cells the modem reports, in WiGLE's form (see <see cref="RadioNetwork"/>).</summary>
    internal static IEnumerable<RadioNetwork> ReadCells(MobileBroadbandCellsInfo info, string? registeredId, string? registeredName)
    {
        string Operator(string? providerId) => providerId is not null && providerId == registeredId ? registeredName ?? "" : "";

        foreach (var c in info.ServingCellsGsm.Concat(info.NeighboringCellsGsm))
            if (c.CellId is { } cid && c.LocationAreaCode is { } lac && c.ReceivedSignalStrengthInDBm is { } rssi)
                yield return Cell(RadioNetworkType.GSM, $"{c.ProviderId}_{lac}_{cid}", c.ProviderId, Operator(c.ProviderId), c.ChannelNumber, rssi);

        foreach (var c in info.ServingCellsUmts.Concat(info.NeighboringCellsUmts))
            if (c.CellId is { } cid && c.LocationAreaCode is { } lac && c.ReceivedSignalCodePowerInDBm is { } rscp)
                yield return Cell(RadioNetworkType.WCDMA, $"{c.ProviderId}_{lac}_{cid}", c.ProviderId, Operator(c.ProviderId), c.ChannelNumber, rscp);

        foreach (var c in info.ServingCellsLte.Concat(info.NeighboringCellsLte))
            if (c.CellId is { } cid && c.TrackingAreaCode is { } tac && c.ReferenceSignalReceivedPowerInDBm is { } rsrp)
                yield return Cell(RadioNetworkType.LTE, $"{c.ProviderId}_{tac}_{cid}", c.ProviderId, Operator(c.ProviderId), c.ChannelNumber, rsrp);

        foreach (var c in info.ServingCellsCdma.Concat(info.NeighboringCellsCdma))
            if (c.SystemId is { } sid && c.NetworkId is { } nid && c.BaseStationId is { } bid && c.PilotSignalStrengthInDB is { } pilot)
                yield return Cell(RadioNetworkType.CDMA, $"{sid}_{nid}_{bid}", $"{sid}", "", null, pilot);
    }

    private static RadioNetwork Cell(RadioNetworkType type, string key, string? providerId, string name, int? channel, double signal)
    {
        int rssi = (int)Math.Round(signal);
        return new RadioNetwork
        {
            Key = key,
            Type = type,
            Name = name,
            Capabilities = $"{type};{providerId}",
            Channel = channel,
            Rssi = rssi,
            HighestRssi = rssi,
        };
    }
}
