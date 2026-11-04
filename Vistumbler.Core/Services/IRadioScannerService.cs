using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Scans for Bluetooth devices and cell towers alongside the Wi-Fi scan (VistumblerMAUI has the same interface for
/// Android). Bluetooth needs a Bluetooth radio; cell towers a cellular modem, any number chosen from
/// <see cref="GetCellularModemsAsync"/> as the Wi-Fi adapters are.
/// </summary>
public interface IRadioScannerService
{
    /// <summary>Raised with the networks heard in each Bluetooth batch or cell poll, on a background thread.</summary>
    event EventHandler<IReadOnlyList<RadioNetwork>>? NetworksDetected;

    /// <summary>Whether there's a Bluetooth radio to scan with.</summary>
    Task<bool> IsBluetoothAvailableAsync();

    /// <summary>The cellular modems there are to read cell towers from (none on most PCs).</summary>
    Task<IReadOnlyList<CellularModem>> GetCellularModemsAsync();

    bool IsRunning { get; }

    /// <summary>Starts Bluetooth if asked, and cell towers from each of these modems.</summary>
    void Start(bool bluetooth, IReadOnlyCollection<string> cellularModemIds);
    void Stop();
}

/// <summary>A cellular modem, as the Interface menu lists it.</summary>
public sealed record CellularModem(string Id, string Name);
