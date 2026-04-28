namespace Vistumbler.Core.Services;

/// <summary>
/// Loads and saves application settings to/from a vistumbler_settings.ini file
/// mirroring the original AutoIt Vistumbler persistence format.
/// </summary>
public interface ISettingsService
{
    /// <summary>Full path to the settings file being used.</summary>
    string SettingsFilePath { get; }

    /// <summary>Read a value from the INI file. Returns <paramref name="defaultValue"/> if the key is absent.</summary>
    string Read(string section, string key, string defaultValue = "");

    /// <summary>Write a value to the INI file.</summary>
    void Write(string section, string key, string value);

    /// <summary>Flush any pending writes to disk.</summary>
    void Save();
}
