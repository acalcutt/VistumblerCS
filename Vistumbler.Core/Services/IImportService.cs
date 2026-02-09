using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Service for importing access point data from various formats
/// </summary>
public interface IImportService
{
    /// <summary>
    /// Import from VS1 format (Vistumbler native text)
    /// </summary>
    Task<List<AccessPoint>> ImportFromVs1Async(string filePath);

    /// <summary>
    /// Import from VSZ format (Vistumbler compressed)
    /// </summary>
    Task<List<AccessPoint>> ImportFromVszAsync(string filePath);

    /// <summary>
    /// Import from NS1 format (NetStumbler binary)
    /// </summary>
    Task<List<AccessPoint>> ImportFromNs1Async(string filePath);

    /// <summary>
    /// Import from NetXML format (Kismet Legacy XML)
    /// </summary>
    Task<List<AccessPoint>> ImportFromNetXmlAsync(string filePath);

    /// <summary>
    /// Import from KismetDB format (Kismet SQLite)
    /// </summary>
    Task<List<AccessPoint>> ImportFromKismetDbAsync(string filePath);

    /// <summary>
    /// Import from CSV format (Supports Vistumbler Detailed and Wigle)
    /// </summary>
    Task<List<AccessPoint>> ImportFromCsvAsync(string filePath);
}
