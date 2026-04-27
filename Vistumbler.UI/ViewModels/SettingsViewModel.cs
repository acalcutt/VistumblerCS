using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using Vistumbler.Core.Enums;

namespace Vistumbler.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _comPort = "COM4";

    [ObservableProperty]
    private int _baudRate = 4800;

    [ObservableProperty]
    private int _refreshInterval = 1000;

    [ObservableProperty]
    private bool _autoScan = false;

    [ObservableProperty]
    private bool _playSound = true;

    // ── Sound – new AP behaviour ─────────────────────────────────────────────
    // Mirrors Vistumbler INI [Sound] section and the Sound settings tab

    [ObservableProperty]
    private SoundPerApMode _soundPerApMode = SoundPerApMode.OncePerLoop;

    // ── Sound – Speak Signal ─────────────────────────────────────────────────

    [ObservableProperty]
    private bool _speakSignal = false;

    [ObservableProperty]
    private SpeakSoundType _speakType = SpeakSoundType.Sapi;

    [ObservableProperty]
    private int _speakSignalIntervalMs = 2000;

    [ObservableProperty]
    private bool _speakSignalSayPercent = true;

    // ── Sound – MIDI ─────────────────────────────────────────────────────────
    // Mirrors Vistumbler INI [MIDI] section

    [ObservableProperty]
    private bool _playMidiForActiveAps = false;

    [ObservableProperty]
    private int _midiInstrument = 56;   // 56 = Trumpet (matches Vistumbler default)

    [ObservableProperty]
    private int _midiPlayTimeMs = 500;

    [ObservableProperty]
    private int _timeBeforeMarkingDead = 2;

    [ObservableProperty]
    private string _saveDirectory = string.Empty;

    public SettingsViewModel()
    {
        SaveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Vistumbler");
    }
}
