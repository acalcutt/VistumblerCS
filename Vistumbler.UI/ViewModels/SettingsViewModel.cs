using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Enums;
using Vistumbler.Core.Services;

namespace Vistumbler.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDatabaseService _db;

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
    [ObservableProperty] private bool   _gpsTypeKernel32       = true;
    [ObservableProperty] private bool   _gpsTypeNetcomm        = false;
    [ObservableProperty] private bool   _gpsTypeCommMg         = false;
    [ObservableProperty] private int    _comPortNumber         = 4;
    [ObservableProperty] private string _baudRate              = "4800";
    [ObservableProperty] private string _stopBit               = "1";
    [ObservableProperty] private string _parity                = "None";
    [ObservableProperty] private string _dataBit               = "8";
    [ObservableProperty] private bool   _gpsLogEnabled         = false;
    [ObservableProperty] private bool   _gpsLogDeleteOnExit    = true;
    [ObservableProperty] private string _gpsLogLocation        = string.Empty;
    [ObservableProperty] private string _gpsFormat             = "ddmm.mmmm";
    [ObservableProperty] private bool   _gpsDisconnectOnTimeout = true;
    [ObservableProperty] private bool   _gpsResetOnNoData       = true;

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
    public SettingsViewModel(IDatabaseService db)
    {
        _db = db;
        var defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Vistumbler") + Path.DirectorySeparatorChar;
        SaveDir             = defaultDir;
        SaveDirAuto         = defaultDir;
        SaveDirAutoRecovery = defaultDir;
        SaveDirKml          = defaultDir;
        GpsLogLocation      = Path.Combine(defaultDir, "gps_nmea_log.txt");
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
