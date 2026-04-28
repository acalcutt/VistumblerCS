using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Enums;
using Vistumbler.Core.Services;

namespace Vistumbler.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDatabaseService  _db;
    private readonly ISettingsService  _ini;

    // ── Tab navigation ────────────────────────────────────────────────────
    [ObservableProperty]
    private int _selectedTabIndex = 0;

    // ── Misc tab ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool _autoCheckForUpdates = true;
    [ObservableProperty] private bool _checkForBetaUpdates = false;
    [ObservableProperty] private int  _refreshLoopTimeMs   = 1000;
    [ObservableProperty] private int  _maxSignalDbm        = -30;
    [ObservableProperty] private int  _disassociationSignalDbm = -85;
    [ObservableProperty] private int  _timeBeforeMarkingDeadS  = 5;
    [ObservableProperty] private bool _autoRefreshNetworks      = true;
    [ObservableProperty] private int  _autoRefreshNetworksTimeS = 1;
    // Colors: 6-char hex (no '#')
    [ObservableProperty] private string _backgroundColorHex     = "99B4A1";
    [ObservableProperty] private string _controlColorHex        = "D7E4C2";
    [ObservableProperty] private string _fontColorHex           = "000000";
    [ObservableProperty] private string _buttonActiveColorHex   = "E1F2D0";
    [ObservableProperty] private string _buttonInactiveColorHex = "F2D0D0";
    [ObservableProperty] private double _guiTextSize = 8.5;

    // ── Save tab ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _saveDir             = string.Empty;
    [ObservableProperty] private string _saveDirAuto         = string.Empty;
    [ObservableProperty] private string _saveDirAutoRecovery = string.Empty;
    [ObservableProperty] private string _saveDirKml          = string.Empty;
    [ObservableProperty] private bool   _autoSaveAndClear             = false;
    [ObservableProperty] private bool   _autoSaveAndClearOnAps        = true;
    [ObservableProperty] private bool   _autoSaveAndClearOnTime       = false;
    [ObservableProperty] private int    _autoSaveAndClearAps          = 1000;
    [ObservableProperty] private int    _autoSaveAndClearTimeMinutes  = 60;
    [ObservableProperty] private bool   _autoSaveAndClearPlaySound    = true;
    [ObservableProperty] private bool   _autoRecovery                 = true;
    [ObservableProperty] private bool   _autoRecoveryDeleteOnExit     = true;
    [ObservableProperty] private int    _autoRecoveryEveryMinutes     = 5;

    // ── GPS tab ───────────────────────────────────────────────────────────
    [ObservableProperty] private GpsSourceType _gpsSource          = GpsSourceType.Serial;
    [ObservableProperty] private int    _comPortNumber             = 4;
    [ObservableProperty] private string _baudRate                  = "4800";
    [ObservableProperty] private string _stopBit                   = "1";
    [ObservableProperty] private string _parity                    = "None";
    [ObservableProperty] private string _dataBit                   = "8";
    [ObservableProperty] private bool   _gpsLogEnabled             = false;
    [ObservableProperty] private bool   _gpsLogDeleteOnExit        = true;
    [ObservableProperty] private string _gpsLogLocation            = string.Empty;
    [ObservableProperty] private string _gpsFormat                 = "ddmm.mmmm";
    [ObservableProperty] private bool   _gpsDisconnectOnTimeout    = true;
    [ObservableProperty] private bool   _gpsResetOnNoData          = true;

    /// <summary>Bound to the Serial radio button via EnumBoolConverter.</summary>
    public bool GpsSourceIsSerial
    {
        get => GpsSource == GpsSourceType.Serial;
        set { if (value) GpsSource = GpsSourceType.Serial; }
    }

    /// <summary>Bound to the Windows Location API radio button via EnumBoolConverter.</summary>
    public bool GpsSourceIsWindowsLocation
    {
        get => GpsSource == GpsSourceType.WindowsLocation;
        set { if (value) GpsSource = GpsSourceType.WindowsLocation; }
    }

    partial void OnGpsSourceChanged(GpsSourceType value)
    {
        OnPropertyChanged(nameof(GpsSourceIsSerial));
        OnPropertyChanged(nameof(GpsSourceIsWindowsLocation));
        OnPropertyChanged(nameof(ComSettingsVisible));
    }

    /// <summary>Hides/shows the COM Settings group based on the selected source.</summary>
    public System.Windows.Visibility ComSettingsVisible =>
        GpsSource == GpsSourceType.Serial
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public static IReadOnlyList<string> BaudRateOptions    { get; } = new[] { "1200","2400","4800","9600","19200","38400","57600","115200" };
    public static IReadOnlyList<string> StopBitOptions     { get; } = new[] { "1","1.5","2" };
    public static IReadOnlyList<string> ParityOptions      { get; } = new[] { "None","Odd","Even","Mark","Space" };
    public static IReadOnlyList<string> DataBitOptions     { get; } = new[] { "7","8" };
    public static IReadOnlyList<string> GpsFormatOptions   { get; } = new[] { "dd.dddddd","ddmm.mmmm","dd mm ss.s","dd mm.mmmm" };
    public static IReadOnlyList<int>    ComPortOptions     { get; } = Enumerable.Range(1, 32).ToList();

    // ── Language tab ─────────────────────────────────────────────────────
    [ObservableProperty] private string _language            = "English";
    [ObservableProperty] private string _languageAuthor      = "Andrew Calcutt";
    [ObservableProperty] private string _languageDate        = "09/06/2020";
    [ObservableProperty] private string _languageCode        = "en_US";
    [ObservableProperty] private string _languageDescription = "English SearchWords. English Text. Default Language.";
    [ObservableProperty] private string _languageImportPath  = string.Empty;
    public static IReadOnlyList<string> LanguageOptions { get; } = new[] { "English" };

    // ── Manufacturers tab ────────────────────────────────────────────────
    public ObservableCollection<ManufacturerEntryViewModel> Manufacturers { get; } = new();
    [ObservableProperty] private string _newMacAddress   = string.Empty;
    [ObservableProperty] private string _newManufacturer = string.Empty;
    [ObservableProperty] private ManufacturerEntryViewModel? _selectedManufacturer;

    // ── Labels tab ───────────────────────────────────────────────────────
    public ObservableCollection<LabelEntryViewModel> Labels { get; } = new();
    [ObservableProperty] private string _newLabelMacAddress = string.Empty;
    [ObservableProperty] private string _newLabel           = string.Empty;
    [ObservableProperty] private LabelEntryViewModel? _selectedLabel;

    // ── Columns tab ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _showLineNumber    = true;  [ObservableProperty] private int _lineNumberWidth    = 60;
    [ObservableProperty] private bool _showActive        = true;  [ObservableProperty] private int _activeWidth        = 60;
    [ObservableProperty] private bool _showSsid          = true;  [ObservableProperty] private int _ssidWidth          = 150;
    [ObservableProperty] private bool _showMacAddress    = true;  [ObservableProperty] private int _macAddressWidth    = 110;
    [ObservableProperty] private bool _showSignal        = true;  [ObservableProperty] private int _signalWidth        = 75;
    [ObservableProperty] private bool _showHighSignal    = true;  [ObservableProperty] private int _highSignalWidth    = 75;
    [ObservableProperty] private bool _showRssi          = true;  [ObservableProperty] private int _rssiWidth          = 75;
    [ObservableProperty] private bool _showHighRssi      = true;  [ObservableProperty] private int _highRssiWidth      = 75;
    [ObservableProperty] private bool _showAuthentication = true; [ObservableProperty] private int _authenticationWidth = 105;
    [ObservableProperty] private bool _showEncryption    = true;  [ObservableProperty] private int _encryptionWidth    = 105;
    [ObservableProperty] private bool _showRadioType     = true;  [ObservableProperty] private int _radioTypeWidth     = 85;
    [ObservableProperty] private bool _showNetworkType   = true;  [ObservableProperty] private int _networkTypeWidth   = 100;
    [ObservableProperty] private bool _showChannel       = true;  [ObservableProperty] private int _channelWidth       = 70;
    [ObservableProperty] private bool _showFrequency     = true;  [ObservableProperty] private int _frequencyWidth     = 80;
    [ObservableProperty] private bool _showManufacturer  = true;  [ObservableProperty] private int _manufacturerWidth  = 110;
    [ObservableProperty] private bool _showLabel         = true;  [ObservableProperty] private int _labelWidth         = 110;
    [ObservableProperty] private bool _showLatitude      = true;  [ObservableProperty] private int _latitudeWidth      = 85;
    [ObservableProperty] private bool _showLongitude     = true;  [ObservableProperty] private int _longitudeWidth     = 85;
    [ObservableProperty] private bool _showLatitudeDdmmss  = true; [ObservableProperty] private int _latitudeDdmmssWidth  = 115;
    [ObservableProperty] private bool _showLongitudeDdmmss = true; [ObservableProperty] private int _longitudeDdmmssWidth = 115;
    [ObservableProperty] private bool _showLatitudeDdmmmm  = true; [ObservableProperty] private int _latitudeDdmmmmWidth  = 140;
    [ObservableProperty] private bool _showLongitudeDdmmmm = true; [ObservableProperty] private int _longitudeDdmmmmWidth = 140;
    [ObservableProperty] private bool _showBasicTransferRates = true; [ObservableProperty] private int _basicTransferRatesWidth = 130;
    [ObservableProperty] private bool _showOtherTransferRates = true; [ObservableProperty] private int _otherTransferRatesWidth = 130;
    [ObservableProperty] private bool _showFirstActive  = true;   [ObservableProperty] private int _firstActiveWidth  = 130;
    [ObservableProperty] private bool _showLastActive   = true;   [ObservableProperty] private int _lastActiveWidth   = 130;

    // ── Auto tab ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _autoKml               = false;
    [ObservableProperty] private bool   _autoOpenKmlNetLink    = true;
    [ObservableProperty] private string _googleEarthExe        = @"C:/Program Files (x86)/Google/Google Earth/client/googleearth.exe";
    [ObservableProperty] private int    _kmlActiveRefreshTimeS = 1;
    [ObservableProperty] private int    _kmlDeadRefreshTimeS   = 30;
    [ObservableProperty] private int    _kmlGpsRefreshTimeS    = 1;
    [ObservableProperty] private int    _kmlTrackRefreshTimeS  = 10;
    [ObservableProperty] private bool   _kmlFlyTo              = true;
    [ObservableProperty] private int    _kmlAltitudeM          = 4000;
    [ObservableProperty] private string _kmlAltMode            = "clampToGround";
    [ObservableProperty] private int    _kmlRangeM             = 4000;
    [ObservableProperty] private int    _kmlHeading            = 0;
    [ObservableProperty] private int    _kmlTilt               = 0;
    [ObservableProperty] private bool   _autoSort              = false;
    [ObservableProperty] private string _sortBy                = "SSID";
    [ObservableProperty] private string _sortDirection         = "Ascending";
    [ObservableProperty] private int    _sortEverySeconds      = 60;
    public static IReadOnlyList<string> AltModeOptions      { get; } = new[] { "clampToGround","relativeToGround","absolute" };
    public static IReadOnlyList<string> SortByOptions       { get; } = new[] { "SSID","Mac Address","Signal","High Signal","RSSI","Channel","Authentication","Encryption","Radio Type","Network Type","Manufacturer","Label","Latitude","Longitude","First Active","Last Active" };
    public static IReadOnlyList<string> SortDirectionOptions { get; } = new[] { "Ascending","Descending" };

    // ── Sound tab ────────────────────────────────────────────────────────
    [ObservableProperty] private bool _playSound = true;
    [ObservableProperty] private SoundPerApMode _soundPerApMode  = SoundPerApMode.OncePerLoop;
    [ObservableProperty] private bool _speakSignal               = false;
    [ObservableProperty] private SpeakSoundType _speakType       = SpeakSoundType.Sapi;
    [ObservableProperty] private int  _speakSignalIntervalMs     = 2000;
    [ObservableProperty] private bool _speakSignalSayPercent     = true;
    [ObservableProperty] private bool _playMidiForActiveAps      = false;
    [ObservableProperty] private int  _midiInstrument            = 56;
    [ObservableProperty] private int  _midiPlayTimeMs            = 500;

    public static IReadOnlyList<string> MidiInstrumentOptions { get; } = new[]
    {
        "0 - Acoustic Grand Piano",  "1 - Bright Acoustic Piano", "2 - Electric Grand Piano",   "3 - Honky-Tonk Piano",
        "4 - Electric Piano 1",      "5 - Electric Piano 2",      "6 - Harpsichord",             "7 - Clavinet",
        "8 - Celesta",               "9 - Glockenspiel",          "10 - Music Box",              "11 - Vibraphone",
        "12 - Marimba",              "13 - Xylophone",            "14 - Tubular Bells",          "15 - Dulcimer",
        "16 - Drawbar Organ",        "17 - Percussive Organ",     "18 - Rock Organ",             "19 - Church Organ",
        "20 - Reed Organ",           "21 - Accordion",            "22 - Harmonica",              "23 - Tango Accordion",
        "24 - Acoustic Guitar (Nylon)","25 - Acoustic Guitar (Steel)","26 - Electric Guitar (Jazz)","27 - Electric Guitar (Clean)",
        "28 - Electric Guitar (Muted)","29 - Overdriven Guitar",  "30 - Distortion Guitar",      "31 - Guitar Harmonics",
        "32 - Acoustic Bass",        "33 - Electric Bass (Finger)","34 - Electric Bass (Pick)", "35 - Fretless Bass",
        "36 - Slap Bass 1",          "37 - Slap Bass 2",          "38 - Synth Bass 1",           "39 - Synth Bass 2",
        "40 - Violin",               "41 - Viola",                "42 - Cello",                  "43 - Contrabass",
        "44 - Tremolo Strings",      "45 - Pizzicato Strings",    "46 - Orchestral Harp",        "47 - Timpani",
        "48 - String Ensemble 1",    "49 - String Ensemble 2",    "50 - Synth Strings 1",        "51 - Synth Strings 2",
        "52 - Choir Aahs",           "53 - Voice Oohs",           "54 - Synth Choir",            "55 - Orchestra Hit",
        "56 - Trumpet",              "57 - Trombone",             "58 - Tuba",                   "59 - Muted Trumpet",
        "60 - French Horn",          "61 - Brass Section",        "62 - Synth Brass 1",          "63 - Synth Brass 2",
        "64 - Soprano Sax",          "65 - Alto Sax",             "66 - Tenor Sax",              "67 - Baritone Sax",
        "68 - Oboe",                 "69 - English Horn",         "70 - Bassoon",                "71 - Clarinet",
        "72 - Piccolo",              "73 - Flute",                "74 - Recorder",               "75 - Pan Flute",
        "76 - Blown Bottle",         "77 - Shakuhachi",           "78 - Whistle",                "79 - Ocarina",
        "80 - Lead 1 (Square)",      "81 - Lead 2 (Sawtooth)",    "82 - Lead 3 (Calliope)",      "83 - Lead 4 (Chiff)",
        "84 - Lead 5 (Charang)",     "85 - Lead 6 (Voice)",       "86 - Lead 7 (Fifths)",        "87 - Lead 8 (Bass+Lead)",
        "88 - Pad 1 (New Age)",      "89 - Pad 2 (Warm)",         "90 - Pad 3 (Polysynth)",      "91 - Pad 4 (Choir)",
        "92 - Pad 5 (Bowed)",        "93 - Pad 6 (Metallic)",     "94 - Pad 7 (Halo)",           "95 - Pad 8 (Sweep)",
        "96 - FX 1 (Rain)",          "97 - FX 2 (Soundtrack)",    "98 - FX 3 (Crystal)",         "99 - FX 4 (Atmosphere)",
        "100 - FX 5 (Brightness)",   "101 - FX 6 (Goblins)",      "102 - FX 7 (Echoes)",         "103 - FX 8 (Sci-Fi)",
        "104 - Sitar",               "105 - Banjo",               "106 - Shamisen",              "107 - Koto",
        "108 - Kalimba",             "109 - Bagpipe",             "110 - Fiddle",                "111 - Shanai",
        "112 - Tinkle Bell",         "113 - Agogo",               "114 - Steel Drums",           "115 - Woodblock",
        "116 - Taiko Drum",          "117 - Melodic Tom",         "118 - Synth Drum",            "119 - Reverse Cymbal",
        "120 - Guitar Fret Noise",   "121 - Breath Noise",        "122 - Seashore",              "123 - Bird Tweet",
        "124 - Telephone Ring",      "125 - Helicopter",          "126 - Applause",              "127 - Gunshot"
    };

    // ── WifiDB tab ────────────────────────────────────────────────────────
    [ObservableProperty] private string _wifiDbUser          = string.Empty;
    [ObservableProperty] private string _wifiDbApiKey        = string.Empty;
    [ObservableProperty] private string _wifiDbGraphUrl      = "https://api.wifidb.net/wifi/";
    [ObservableProperty] private string _wifiDbUrl           = "https://wifidb.net/";
    [ObservableProperty] private string _wifiDbApiUrl        = "https://api.wifidb.net/";
    [ObservableProperty] private bool   _useWifiDbGpsLocate  = false;
    [ObservableProperty] private int    _wifiDbGpsLocateRefreshTimeS = 5;
    [ObservableProperty] private bool   _enableAutoUpApsToWifiDb     = false;
    [ObservableProperty] private int    _autoUpApsToWifiDbTimeS      = 60;

    // ── Cameras tab ───────────────────────────────────────────────────────
    public ObservableCollection<CameraEntryViewModel> Cameras { get; } = new();
    [ObservableProperty] private string _newCameraName  = string.Empty;
    [ObservableProperty] private string _newCameraUrl   = string.Empty;
    [ObservableProperty] private CameraEntryViewModel? _selectedCamera;
    [ObservableProperty] private bool   _enableCameraTrigger        = false;
    [ObservableProperty] private string _cameraTriggerScript        = string.Empty;
    [ObservableProperty] private int    _cameraTriggerRefreshTimeMs = 10000;

    // ── Constructor ───────────────────────────────────────────────────────
    public SettingsViewModel(IDatabaseService db, ISettingsService ini)
    {
        _db  = db;
        _ini = ini;
        var defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Vistumbler") + Path.DirectorySeparatorChar;
        SaveDir             = defaultDir;
        SaveDirAuto         = defaultDir;
        SaveDirAutoRecovery = defaultDir;
        SaveDirKml          = defaultDir;
        GpsLogLocation      = Path.Combine(defaultDir, "gps_nmea_log.txt");
    }

    // ── INI persistence ───────────────────────────────────────────────────

    /// <summary>Populate all properties from the INI file (call on startup).</summary>
    public void LoadSettings()
    {
        string V(string section, string key, string def) => _ini.Read(section, key, def);
        bool   B(string section, string key, bool   def) => V(section, key, def ? "1" : "0") == "1";
        int    I(string section, string key, int    def) => int.TryParse(V(section, key, def.ToString()), out var r) ? r : def;
        double D(string section, string key, double def) => double.TryParse(V(section, key, def.ToString()), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : def;

        var defaultDir = SaveDir; // already set to default in ctor

        // Misc
        AutoCheckForUpdates        = B("Vistumbler",  "AutoCheckForUpdates",       true);
        CheckForBetaUpdates        = B("Vistumbler",  "CheckForBetaUpdates",       false);
        RefreshLoopTimeMs          = I("Vistumbler",  "Sleeptime",                 1000);
        MaxSignalDbm               = I("Vistumbler",  "dBmMaxSignal",              -30);
        DisassociationSignalDbm    = I("Vistumbler",  "dBmDissociationSignal",     -85);
        TimeBeforeMarkingDeadS     = I("Vistumbler",  "TimeBeforeMarkedDead",      5);
        AutoRefreshNetworks        = B("Vistumbler",  "AutoRefreshNetworks",       true);
        AutoRefreshNetworksTimeS   = I("Vistumbler",  "AutoRefreshTime",           1);
        BackgroundColorHex         = V("Vistumbler",  "BackgroundColor",           "0x99B4A1").TrimStart('0', 'x').TrimStart('X');
        ControlColorHex            = V("Vistumbler",  "ControlBackgroundColor",    "0xD7E4C2").TrimStart('0', 'x').TrimStart('X');
        FontColorHex               = V("Vistumbler",  "TextColor",                 "0x000000").TrimStart('0', 'x').TrimStart('X');
        ButtonActiveColorHex       = V("Vistumbler",  "ButtonActiveColor",         "0xE1F2D0").TrimStart('0', 'x').TrimStart('X');
        ButtonInactiveColorHex     = V("Vistumbler",  "ButtonInactiveColor",       "0xF2D0D0").TrimStart('0', 'x').TrimStart('X');
        GuiTextSize                = D("Vistumbler",  "TextSize",                  8.5);

        // Save
        SaveDir             = V("Vistumbler",      "SaveDir",             defaultDir);
        SaveDirAuto         = V("Vistumbler",      "SaveDirAuto",         defaultDir);
        SaveDirAutoRecovery = V("Vistumbler",      "SaveDirAutoRecovery", defaultDir);
        SaveDirKml          = V("Vistumbler",      "SaveDirKml",          defaultDir);
        AutoSaveAndClear          = B("AutoSaveAndClear", "AutoSaveAndClear",          false);
        AutoSaveAndClearOnAps     = B("AutoSaveAndClear", "AutoSaveAndClearOnAPs",     true);
        AutoSaveAndClearOnTime    = B("AutoSaveAndClear", "AutoSaveAndClearOnTime",    false);
        AutoSaveAndClearAps       = I("AutoSaveAndClear", "AutoSaveAndClearAPs",       1000);
        AutoSaveAndClearTimeMinutes = I("AutoSaveAndClear","AutoSaveAndClearTime",     60);
        AutoSaveAndClearPlaySound = B("AutoSaveAndClear", "AutoSaveAndClearPlaySound", true);
        AutoRecovery              = B("AutoRecovery",     "AutoRecovery",              true);
        AutoRecoveryDeleteOnExit  = B("AutoRecovery",     "AutoRecoveryDel",           true);
        AutoRecoveryEveryMinutes  = I("AutoRecovery",     "AutoRecoveryTime",          5);

        // GPS
        var gpsTypeRaw = V("GpsSettings", "GpsSource", "Serial");
        GpsSource      = Enum.TryParse<GpsSourceType>(gpsTypeRaw, out var gs) ? gs : GpsSourceType.Serial;
        ComPortNumber  = I("GpsSettings", "ComPort",             4);
        BaudRate       = V("GpsSettings", "Baud",                "4800");
        Parity         = V("GpsSettings", "Parity",              "None");
        DataBit        = V("GpsSettings", "DataBit",             "8");
        StopBit        = V("GpsSettings", "StopBit",             "1");
        GpsLogEnabled       = B("GpsSettings", "GpsLogEnabled",       false);
        GpsLogDeleteOnExit  = B("GpsSettings", "GpsLogDeleteOnExit",  true);
        GpsLogLocation      = V("AutoKML",     "GpsLogLocation",       Path.Combine(defaultDir, "gps_nmea_log.txt"));
        GpsDisconnectOnTimeout = B("GpsSettings", "GpsDisconnect",     true);
        GpsResetOnNoData       = B("GpsSettings", "GpsReset",          true);

        var fmtOptions = new[] { "dd.dddddd", "ddmm.mmmm", "dd mm ss.s", "dd mm.mmmm" };
        var fmtIdxRaw  = V("GpsSettings", "GPSformat", "1");
        GpsFormat = int.TryParse(fmtIdxRaw, out var fi) && fi >= 0 && fi < fmtOptions.Length
            ? fmtOptions[fi]
            : fmtIdxRaw.Contains(' ') || fmtIdxRaw.Contains('.') ? fmtIdxRaw : "ddmm.mmmm";

        // Language
        Language = V("Vistumbler", "Language", "English");

        // Auto/KML
        AutoKml                = B("AutoKML",  "AutoKML",         false);
        AutoOpenKmlNetLink     = B("AutoKML",  "OpenKmlNetLink",  true);
        GoogleEarthExe         = V("AutoKML",  "GoogleEarthExe",  GoogleEarthExe);
        KmlActiveRefreshTimeS  = I("AutoKML",  "AutoKmlActiveTime", 1);
        KmlDeadRefreshTimeS    = I("AutoKML",  "AutoKmlDeadTime",   30);
        KmlGpsRefreshTimeS     = I("AutoKML",  "AutoKmlGpsTime",    1);
        KmlTrackRefreshTimeS   = I("AutoKML",  "AutoKmlTrackTime",  10);
        KmlFlyTo               = B("AutoKML",  "KmlFlyTo",         true);
        KmlAltitudeM           = I("AutoKML",  "AutoKML_Alt",      4000);
        KmlAltMode             = V("AutoKML",  "AutoKML_AltMode",  "clampToGround");
        KmlRangeM              = I("AutoKML",  "AutoKML_Range",    4000);
        KmlHeading             = I("AutoKML",  "AutoKML_Heading",  0);
        KmlTilt                = I("AutoKML",  "AutoKML_Tilt",     0);
        AutoSort               = B("AutoSort", "AutoSort",         false);
        SortBy                 = V("AutoSort", "SortCombo",        "SSID");
        SortEverySeconds       = I("AutoSort", "AutoSortTime",     60);
        var sortDirRaw = V("AutoSort", "AscDecDefault", "0");
        SortDirection = sortDirRaw == "1" ? "Descending" : "Ascending";

        // Sound
        PlaySound               = B("Sound", "PlaySoundOnNewAP",   true);
        SoundPerApMode = V("Sound", "SoundPerAP", "0") == "1"
            ? SoundPerApMode.OncePerAp : SoundPerApMode.OncePerLoop;
        SpeakSignal             = B("MIDI", "SpeakSignal",         false);
        SpeakSignalIntervalMs   = I("MIDI", "SpeakSigTime",        2000);
        SpeakSignalSayPercent   = B("MIDI", "SpeakSigSayPecent",   true);
        MidiInstrument          = I("MIDI", "Midi_Instument",      56);
        MidiPlayTimeMs          = I("MIDI", "Midi_PlayTime",       500);
        PlayMidiForActiveAps    = B("MIDI", "Midi_PlayForActiveAps", false);
        var speakTypeRaw = V("MIDI", "SpeakType", "2");
        SpeakType = speakTypeRaw == "1" ? SpeakSoundType.VistumblerSounds : SpeakSoundType.Sapi;

        // WifiDB
        WifiDbUser                    = V("WifiDbWifiTools", "WifiDb_User",               "");
        WifiDbApiKey                  = V("WifiDbWifiTools", "WifiDb_ApiKey",              "");
        WifiDbGraphUrl                = V("WifiDbWifiTools", "WifiDb_GRAPH_URL",           WifiDbGraphUrl);
        WifiDbUrl                     = V("WifiDbWifiTools", "WiFiDB_URL",                 WifiDbUrl);
        WifiDbApiUrl                  = V("WifiDbWifiTools", "WifiDB_API_URL",             WifiDbApiUrl);
        UseWifiDbGpsLocate            = B("WifiDbWifiTools", "UseWiFiDbGpsLocate",         false);
        WifiDbGpsLocateRefreshTimeS   = I("WifiDbWifiTools", "WiFiDbLocateRefreshTime",   5);
        EnableAutoUpApsToWifiDb       = B("WifiDbWifiTools", "AutoUpApsToWifiDB",         false);
        AutoUpApsToWifiDbTimeS        = I("WifiDbWifiTools", "AutoUpApsToWifiDBTime",     60);

        // Camera
        EnableCameraTrigger        = B("Cam", "CamTrigger",      false);
        CameraTriggerScript        = V("Cam", "CamTriggerScript", "");
        CameraTriggerRefreshTimeMs = I("Cam", "CamTriggerTime",  10000);

        // Columns – show/hide (0 = hidden in original means shown in original's column order; -1 = hidden)
        // The original stores the column position; -1 means hidden. We map >=0 → visible.
        ShowLineNumber     = I("Columns", "Column_Line",               0)  >= 0;
        ShowActive         = I("Columns", "Column_Active",             1)  >= 0;
        ShowSsid           = I("Columns", "Column_SSID",               3)  >= 0;
        ShowMacAddress     = I("Columns", "Column_BSSID",              2)  >= 0;
        ShowSignal         = I("Columns", "Column_Signal",             4)  >= 0;
        ShowHighSignal     = I("Columns", "Column_HighSignal",         5)  >= 0;
        ShowRssi           = I("Columns", "Column_RSSI",               6)  >= 0;
        ShowHighRssi       = I("Columns", "Column_HighRSSI",           7)  >= 0;
        ShowAuthentication = I("Columns", "Column_Authentication",     9)  >= 0;
        ShowEncryption     = I("Columns", "Column_Encryption",        10)  >= 0;
        ShowRadioType      = I("Columns", "Column_RadioType",         16)  >= 0;
        ShowNetworkType    = I("Columns", "Column_NetworkType",       11)  >= 0;
        ShowChannel        = I("Columns", "Column_Channel",            8)  >= 0;
        ShowFrequency      = I("Columns", "Column_Frequency",          12)  >= 0;
        ShowManufacturer   = I("Columns", "Column_Manufacturer",      14)  >= 0;
        ShowLabel          = I("Columns", "Column_Label",             15)  >= 0;
        ShowLatitude       = I("Columns", "Column_Latitude",          12)  >= 0;
        ShowLongitude      = I("Columns", "Column_Longitude",         13)  >= 0;
        ShowLatitudeDdmmss  = I("Columns", "Column_LatitudeDMS",      17)  >= 0;
        ShowLongitudeDdmmss = I("Columns", "Column_LongitudeDMS",     18)  >= 0;
        ShowLatitudeDdmmmm  = I("Columns", "Column_LatitudeDMM",      19)  >= 0;
        ShowLongitudeDdmmmm = I("Columns", "Column_LongitudeDMM",     20)  >= 0;
        ShowBasicTransferRates = I("Columns", "Column_BasicTransferRates", 21) >= 0;
        ShowOtherTransferRates = I("Columns", "Column_OtherTransferRates", 22) >= 0;
        ShowFirstActive    = I("Columns", "Column_FirstActive",       23)  >= 0;
        ShowLastActive     = I("Columns", "Column_LastActive",        24)  >= 0;

        // Column widths
        LineNumberWidth       = I("Column_Width", "Column_Line",               60);
        ActiveWidth           = I("Column_Width", "Column_Active",             60);
        SsidWidth             = I("Column_Width", "Column_SSID",              150);
        MacAddressWidth       = I("Column_Width", "Column_BSSID",             110);
        SignalWidth           = I("Column_Width", "Column_Signal",             75);
        HighSignalWidth       = I("Column_Width", "Column_HighSignal",         75);
        RssiWidth             = I("Column_Width", "Column_RSSI",               75);
        HighRssiWidth         = I("Column_Width", "Column_HighRSSI",           75);
        AuthenticationWidth   = I("Column_Width", "Column_Authentication",    105);
        EncryptionWidth       = I("Column_Width", "Column_Encryption",        105);
        RadioTypeWidth        = I("Column_Width", "Column_RadioType",          85);
        NetworkTypeWidth      = I("Column_Width", "Column_NetworkType",       100);
        ChannelWidth          = I("Column_Width", "Column_Channel",            70);
        FrequencyWidth        = I("Column_Width", "Column_Frequency",          80);
        ManufacturerWidth     = I("Column_Width", "Column_Manufacturer",      110);
        LabelWidth            = I("Column_Width", "Column_Label",             110);
        LatitudeWidth         = I("Column_Width", "Column_Latitude",           85);
        LongitudeWidth        = I("Column_Width", "Column_Longitude",          85);
        LatitudeDdmmssWidth   = I("Column_Width", "Column_LatitudeDMS",       115);
        LongitudeDdmmssWidth  = I("Column_Width", "Column_LongitudeDMS",      115);
        LatitudeDdmmmmWidth   = I("Column_Width", "Column_LatitudeDMM",       140);
        LongitudeDdmmmmWidth  = I("Column_Width", "Column_LongitudeDMM",      140);
        BasicTransferRatesWidth = I("Column_Width", "Column_BasicTransferRates", 130);
        OtherTransferRatesWidth = I("Column_Width", "Column_OtherTransferRates", 130);
        FirstActiveWidth      = I("Column_Width", "Column_FirstActive",       130);
        LastActiveWidth       = I("Column_Width", "Column_LastActive",        130);
    }

    /// <summary>Write all current property values back to the INI file and flush.</summary>
    public void SaveSettings()
    {
        void W(string s, string k, string v)  => _ini.Write(s, k, v);
        void WB(string s, string k, bool v)   => _ini.Write(s, k, v ? "1" : "0");
        void WI(string s, string k, int v)    => _ini.Write(s, k, v.ToString());
        void WD(string s, string k, double v) => _ini.Write(s, k, v.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Misc
        WB("Vistumbler", "AutoCheckForUpdates",    AutoCheckForUpdates);
        WB("Vistumbler", "CheckForBetaUpdates",    CheckForBetaUpdates);
        WI("Vistumbler", "Sleeptime",               RefreshLoopTimeMs);
        WI("Vistumbler", "dBmMaxSignal",            MaxSignalDbm);
        WI("Vistumbler", "dBmDissociationSignal",   DisassociationSignalDbm);
        WI("Vistumbler", "TimeBeforeMarkedDead",    TimeBeforeMarkingDeadS);
        WB("Vistumbler", "AutoRefreshNetworks",     AutoRefreshNetworks);
        WI("Vistumbler", "AutoRefreshTime",         AutoRefreshNetworksTimeS);
        W ("Vistumbler", "BackgroundColor",         "0x" + BackgroundColorHex);
        W ("Vistumbler", "ControlBackgroundColor",  "0x" + ControlColorHex);
        W ("Vistumbler", "TextColor",               "0x" + FontColorHex);
        W ("Vistumbler", "ButtonActiveColor",       "0x" + ButtonActiveColorHex);
        W ("Vistumbler", "ButtonInactiveColor",     "0x" + ButtonInactiveColorHex);
        WD("Vistumbler", "TextSize",                GuiTextSize);
        W ("Vistumbler", "Language",                Language);

        // Save
        W ("Vistumbler",       "SaveDir",              SaveDir);
        W ("Vistumbler",       "SaveDirAuto",           SaveDirAuto);
        W ("Vistumbler",       "SaveDirAutoRecovery",   SaveDirAutoRecovery);
        W ("Vistumbler",       "SaveDirKml",            SaveDirKml);
        WB("AutoSaveAndClear", "AutoSaveAndClear",      AutoSaveAndClear);
        WB("AutoSaveAndClear", "AutoSaveAndClearOnAPs", AutoSaveAndClearOnAps);
        WB("AutoSaveAndClear", "AutoSaveAndClearOnTime",AutoSaveAndClearOnTime);
        WI("AutoSaveAndClear", "AutoSaveAndClearAPs",   AutoSaveAndClearAps);
        WI("AutoSaveAndClear", "AutoSaveAndClearTime",  AutoSaveAndClearTimeMinutes);
        WB("AutoSaveAndClear", "AutoSaveAndClearPlaySound", AutoSaveAndClearPlaySound);
        WB("AutoRecovery",     "AutoRecovery",          AutoRecovery);
        WB("AutoRecovery",     "AutoRecoveryDel",       AutoRecoveryDeleteOnExit);
        WI("AutoRecovery",     "AutoRecoveryTime",      AutoRecoveryEveryMinutes);

        // GPS
        W ("GpsSettings", "GpsSource",         GpsSource.ToString());
        WI("GpsSettings", "ComPort",            ComPortNumber);
        W ("GpsSettings", "Baud",               BaudRate);
        W ("GpsSettings", "Parity",             Parity);
        W ("GpsSettings", "DataBit",            DataBit);
        W ("GpsSettings", "StopBit",            StopBit);
        WB("GpsSettings", "GpsLogEnabled",      GpsLogEnabled);
        WB("GpsSettings", "GpsLogDeleteOnExit", GpsLogDeleteOnExit);
        W ("AutoKML",     "GpsLogLocation",     GpsLogLocation);
        WB("GpsSettings", "GpsDisconnect",      GpsDisconnectOnTimeout);
        WB("GpsSettings", "GpsReset",           GpsResetOnNoData);
        // Store GPS format as both the string value and its legacy index
        W ("GpsSettings", "GPSformat",          GpsFormat);

        // Auto/KML
        WB("AutoKML",  "AutoKML",         AutoKml);
        WB("AutoKML",  "OpenKmlNetLink",  AutoOpenKmlNetLink);
        W ("AutoKML",  "GoogleEarthExe",  GoogleEarthExe);
        WI("AutoKML",  "AutoKmlActiveTime", KmlActiveRefreshTimeS);
        WI("AutoKML",  "AutoKmlDeadTime",   KmlDeadRefreshTimeS);
        WI("AutoKML",  "AutoKmlGpsTime",    KmlGpsRefreshTimeS);
        WI("AutoKML",  "AutoKmlTrackTime",  KmlTrackRefreshTimeS);
        WB("AutoKML",  "KmlFlyTo",         KmlFlyTo);
        WI("AutoKML",  "AutoKML_Alt",      KmlAltitudeM);
        W ("AutoKML",  "AutoKML_AltMode",  KmlAltMode);
        WI("AutoKML",  "AutoKML_Range",    KmlRangeM);
        WI("AutoKML",  "AutoKML_Heading",  KmlHeading);
        WI("AutoKML",  "AutoKML_Tilt",     KmlTilt);
        WB("AutoSort", "AutoSort",         AutoSort);
        W ("AutoSort", "SortCombo",        SortBy);
        WI("AutoSort", "AutoSortTime",     SortEverySeconds);
        W ("AutoSort", "AscDecDefault",    SortDirection == "Descending" ? "1" : "0");

        // Sound
        WB("Sound", "PlaySoundOnNewAP",       PlaySound);
        W ("Sound", "SoundPerAP",             SoundPerApMode == SoundPerApMode.OncePerAp ? "1" : "0");
        WB("MIDI",  "SpeakSignal",            SpeakSignal);
        WI("MIDI",  "SpeakSigTime",           SpeakSignalIntervalMs);
        WB("MIDI",  "SpeakSigSayPecent",      SpeakSignalSayPercent);
        WI("MIDI",  "Midi_Instument",         MidiInstrument);
        WI("MIDI",  "Midi_PlayTime",          MidiPlayTimeMs);
        WB("MIDI",  "Midi_PlayForActiveAps",  PlayMidiForActiveAps);
        W ("MIDI",  "SpeakType",              SpeakType == SpeakSoundType.VistumblerSounds ? "1" : SpeakType == SpeakSoundType.Midi ? "3" : "2");

        // WifiDB
        W ("WifiDbWifiTools", "WifiDb_User",                WifiDbUser);
        W ("WifiDbWifiTools", "WifiDb_ApiKey",              WifiDbApiKey);
        W ("WifiDbWifiTools", "WifiDb_GRAPH_URL",           WifiDbGraphUrl);
        W ("WifiDbWifiTools", "WiFiDB_URL",                 WifiDbUrl);
        W ("WifiDbWifiTools", "WifiDB_API_URL",             WifiDbApiUrl);
        WB("WifiDbWifiTools", "UseWiFiDbGpsLocate",         UseWifiDbGpsLocate);
        WI("WifiDbWifiTools", "WiFiDbLocateRefreshTime",    WifiDbGpsLocateRefreshTimeS);
        WB("WifiDbWifiTools", "AutoUpApsToWifiDB",          EnableAutoUpApsToWifiDb);
        WI("WifiDbWifiTools", "AutoUpApsToWifiDBTime",      AutoUpApsToWifiDbTimeS);

        // Camera
        WB("Cam", "CamTrigger",       EnableCameraTrigger);
        W ("Cam", "CamTriggerScript", CameraTriggerScript);
        WI("Cam", "CamTriggerTime",   CameraTriggerRefreshTimeMs);

        // Columns – store position (0-based index when shown, -1 when hidden)
        WI("Columns", "Column_Line",               ShowLineNumber     ? 0  : -1);
        WI("Columns", "Column_Active",             ShowActive         ? 1  : -1);
        WI("Columns", "Column_SSID",               ShowSsid           ? 3  : -1);
        WI("Columns", "Column_BSSID",              ShowMacAddress     ? 2  : -1);
        WI("Columns", "Column_Signal",             ShowSignal         ? 4  : -1);
        WI("Columns", "Column_HighSignal",         ShowHighSignal     ? 5  : -1);
        WI("Columns", "Column_RSSI",               ShowRssi           ? 6  : -1);
        WI("Columns", "Column_HighRSSI",           ShowHighRssi       ? 7  : -1);
        WI("Columns", "Column_Authentication",     ShowAuthentication ? 9  : -1);
        WI("Columns", "Column_Encryption",         ShowEncryption     ? 10 : -1);
        WI("Columns", "Column_RadioType",          ShowRadioType      ? 16 : -1);
        WI("Columns", "Column_NetworkType",        ShowNetworkType    ? 11 : -1);
        WI("Columns", "Column_Channel",            ShowChannel        ? 8  : -1);
        WI("Columns", "Column_Frequency",          ShowFrequency      ? 12 : -1);
        WI("Columns", "Column_Manufacturer",       ShowManufacturer   ? 14 : -1);
        WI("Columns", "Column_Label",              ShowLabel          ? 15 : -1);
        WI("Columns", "Column_Latitude",           ShowLatitude       ? 12 : -1);
        WI("Columns", "Column_Longitude",          ShowLongitude      ? 13 : -1);
        WI("Columns", "Column_LatitudeDMS",        ShowLatitudeDdmmss  ? 17 : -1);
        WI("Columns", "Column_LongitudeDMS",       ShowLongitudeDdmmss ? 18 : -1);
        WI("Columns", "Column_LatitudeDMM",        ShowLatitudeDdmmmm  ? 19 : -1);
        WI("Columns", "Column_LongitudeDMM",       ShowLongitudeDdmmmm ? 20 : -1);
        WI("Columns", "Column_BasicTransferRates", ShowBasicTransferRates ? 21 : -1);
        WI("Columns", "Column_OtherTransferRates", ShowOtherTransferRates ? 22 : -1);
        WI("Columns", "Column_FirstActive",        ShowFirstActive    ? 23 : -1);
        WI("Columns", "Column_LastActive",         ShowLastActive     ? 24 : -1);

        // Column widths
        WI("Column_Width", "Column_Line",               LineNumberWidth);
        WI("Column_Width", "Column_Active",             ActiveWidth);
        WI("Column_Width", "Column_SSID",               SsidWidth);
        WI("Column_Width", "Column_BSSID",              MacAddressWidth);
        WI("Column_Width", "Column_Signal",             SignalWidth);
        WI("Column_Width", "Column_HighSignal",         HighSignalWidth);
        WI("Column_Width", "Column_RSSI",               RssiWidth);
        WI("Column_Width", "Column_HighRSSI",           HighRssiWidth);
        WI("Column_Width", "Column_Authentication",     AuthenticationWidth);
        WI("Column_Width", "Column_Encryption",         EncryptionWidth);
        WI("Column_Width", "Column_RadioType",          RadioTypeWidth);
        WI("Column_Width", "Column_NetworkType",        NetworkTypeWidth);
        WI("Column_Width", "Column_Channel",            ChannelWidth);
        WI("Column_Width", "Column_Frequency",          FrequencyWidth);
        WI("Column_Width", "Column_Manufacturer",       ManufacturerWidth);
        WI("Column_Width", "Column_Label",              LabelWidth);
        WI("Column_Width", "Column_Latitude",           LatitudeWidth);
        WI("Column_Width", "Column_Longitude",          LongitudeWidth);
        WI("Column_Width", "Column_LatitudeDMS",        LatitudeDdmmssWidth);
        WI("Column_Width", "Column_LongitudeDMS",       LongitudeDdmmssWidth);
        WI("Column_Width", "Column_LatitudeDMM",        LatitudeDdmmmmWidth);
        WI("Column_Width", "Column_LongitudeDMM",       LongitudeDdmmmmWidth);
        WI("Column_Width", "Column_BasicTransferRates", BasicTransferRatesWidth);
        WI("Column_Width", "Column_OtherTransferRates", OtherTransferRatesWidth);
        WI("Column_Width", "Column_FirstActive",        FirstActiveWidth);
        WI("Column_Width", "Column_LastActive",         LastActiveWidth);

        _ini.Save();
    }

    // ── Manufacturer commands ─────────────────────────────────────────────
    public async Task LoadManufacturersAsync()
    {
        var all = await _db.GetAllManufacturersAsync();
        Manufacturers.Clear();
        foreach (var (mac, mfr) in all)
            Manufacturers.Add(new ManufacturerEntryViewModel(mac, mfr));
    }

    [RelayCommand]
    private async Task AddNewManufacturerAsync()
    {
        var mac = NewMacAddress.Trim().ToUpper();
        var mfr = NewManufacturer.Trim();
        if (string.IsNullOrEmpty(mac) || string.IsNullOrEmpty(mfr)) return;
        await _db.UpsertManufacturerAsync(mac, mfr);
        var existing = Manufacturers.FirstOrDefault(m => m.MacPrefix == mac);
        if (existing != null) existing.Manufacturer = mfr;
        else Manufacturers.Add(new ManufacturerEntryViewModel(mac, mfr));
        NewMacAddress   = string.Empty;
        NewManufacturer = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveManufacturerAsync()
    {
        if (SelectedManufacturer is not { } sel) return;
        await _db.DeleteManufacturerAsync(sel.MacPrefix);
        Manufacturers.Remove(sel);
    }

    [RelayCommand]
    private void EditManufacturer()
    {
        if (SelectedManufacturer is not { } sel) return;
        NewMacAddress   = sel.MacPrefix;
        NewManufacturer = sel.Manufacturer;
    }

    // ── Label commands ────────────────────────────────────────────────────
    public async Task LoadLabelsAsync()
    {
        var all = await _db.GetAllLabelsAsync();
        Labels.Clear();
        foreach (var (bssid, lbl) in all)
            Labels.Add(new LabelEntryViewModel(bssid, lbl));
    }

    [RelayCommand]
    private async Task AddNewLabelAsync()
    {
        var bssid = NewLabelMacAddress.Trim().ToUpper();
        var lbl   = NewLabel.Trim();
        if (string.IsNullOrEmpty(bssid) || string.IsNullOrEmpty(lbl)) return;
        await _db.UpsertLabelAsync(bssid, lbl);
        var existing = Labels.FirstOrDefault(l => l.Bssid == bssid);
        if (existing != null) existing.Label = lbl;
        else Labels.Add(new LabelEntryViewModel(bssid, lbl));
        NewLabelMacAddress = string.Empty;
        NewLabel           = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveLabelAsync()
    {
        if (SelectedLabel is not { } sel) return;
        await _db.DeleteLabelAsync(sel.Bssid);
        Labels.Remove(sel);
    }

    [RelayCommand]
    private void EditLabel()
    {
        if (SelectedLabel is not { } sel) return;
        NewLabelMacAddress = sel.Bssid;
        NewLabel           = sel.Label;
    }

    // ── Camera commands ───────────────────────────────────────────────────
    [RelayCommand]
    private void AddNewCamera()
    {
        var name = NewCameraName.Trim();
        var url  = NewCameraUrl.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var existing = Cameras.FirstOrDefault(c => c.Name == name);
        if (existing != null) existing.Url = url;
        else Cameras.Add(new CameraEntryViewModel(name, url));
        NewCameraName = string.Empty;
        NewCameraUrl  = string.Empty;
    }

    [RelayCommand]
    private void RemoveCamera()
    {
        if (SelectedCamera is not { } sel) return;
        Cameras.Remove(sel);
    }

    [RelayCommand]
    private void EditCamera()
    {
        if (SelectedCamera is not { } sel) return;
        NewCameraName = sel.Name;
        NewCameraUrl  = sel.Url;
    }
}
