using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Radio;

/// <summary>Cell towers from a cellular modem: none yet.</summary>
public sealed partial class WindowsRadioScannerService
{
    public Task<IReadOnlyList<CellularModem>> GetCellularModemsAsync() => Task.FromResult<IReadOnlyList<CellularModem>>(Array.Empty<CellularModem>());

    private void StartCells(IReadOnlyCollection<string> modemIds) { }
    private void StopCells() { }
}
