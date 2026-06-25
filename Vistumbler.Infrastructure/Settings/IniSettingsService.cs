using System.Text;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Settings;

/// <summary>
/// INI-based settings persistence that mirrors the original AutoIt Vistumbler
/// %AppData%\VistumblerCS\vistumbler_settings.ini format.  All values are stored in memory and flushed
/// to disk on <see cref="Save"/>.
/// </summary>
public class IniSettingsService : ISettingsService
{
    // section -> key -> value  (case-insensitive keys, case-preserving values)
    private readonly Dictionary<string, Dictionary<string, string>> _data =
        new(StringComparer.OrdinalIgnoreCase);

    public string SettingsFilePath { get; }

    public IniSettingsService()
    {
        // Settings stored in %AppData%\VistumblerCS\vistumbler_settings.ini
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir     = Path.Combine(appData, "VistumblerCS");
        Directory.CreateDirectory(dir);
        SettingsFilePath = Path.Combine(dir, "vistumbler_settings.ini");

        if (File.Exists(SettingsFilePath))
            ParseFile(SettingsFilePath);
    }

    // ── ISettingsService ─────────────────────────────────────────────────

    public string Read(string section, string key, string defaultValue = "")
    {
        if (_data.TryGetValue(section, out var sec) && sec.TryGetValue(key, out var val))
            return val;
        return defaultValue;
    }

    public void Write(string section, string key, string value)
    {
        if (!_data.TryGetValue(section, out var sec))
        {
            sec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _data[section] = sec;
        }
        sec[key] = value;
    }

    public void Save()
    {
        var sb = new StringBuilder();
        foreach (var (section, keys) in _data)
        {
            sb.AppendLine($"[{section}]");
            foreach (var (key, value) in keys)
                sb.AppendLine($"{key}={value}");
            sb.AppendLine();
        }
        File.WriteAllText(SettingsFilePath, sb.ToString(), Encoding.UTF8);
    }

    public void Reload()
    {
        _data.Clear();
        if (File.Exists(SettingsFilePath))
            ParseFile(SettingsFilePath);
    }

    // ── Parsing ──────────────────────────────────────────────────────────

    private void ParseFile(string path)
    {
        string? currentSection = null;
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!_data.ContainsKey(currentSection))
                    _data[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else if (currentSection != null)
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                {
                    var key = line[..eq].Trim();
                    var val = line[(eq + 1)..].Trim();
                    _data[currentSection][key] = val;
                }
            }
        }
    }
}
