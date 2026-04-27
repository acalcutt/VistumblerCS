using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Sound;

/// <summary>
/// C# equivalent of Vistumbler's say.exe (say.au3).
///
/// say.exe was launched as a separate process so audio never blocked the main
/// scan loop. Here each public method fires a Task.Run() background thread
/// instead, achieving the same isolation without an out-of-process helper.
///
/// Supported modes (matching say.au3 /t flag):
///   Type 1 – WAV word files   : SpeakSignalWithWavFiles()
///   Type 2 – Microsoft SAPI   : SpeakWithSapi()
///   Type 3 – Single MIDI note : PlayMidiForSignal()
///   Type 4 – MIDI per AP list : PlayMidiForActiveAps()
///   Type 5 – Sig-based volume : PlaySigBasedSound()
///
/// MIDI is implemented via direct WinMM P/Invoke (winmm.dll), which is the
/// same DLL the MIDIFunctions.au3 UDF wraps.
/// </summary>
public sealed class SoundService : ISoundService, IDisposable
{
    // ── Configuration ────────────────────────────────────────────────────────

    public string SoundDirectory { get; set; }
    public string NewApSoundFile { get; set; } = "new_ap.wav";

    // ── State ────────────────────────────────────────────────────────────────

    private volatile bool _isMidiPlaying;
    private bool _disposed;

    // ── Constructor ──────────────────────────────────────────────────────────

    public SoundService()
    {
        SoundDirectory = Path.Combine(AppContext.BaseDirectory, "Sounds");
    }

    // ── WinMM P/Invoke – WAV ─────────────────────────────────────────────────

    /// <summary>
    /// PlaySound from winmm.dll.  SND_SYNC blocks the calling thread until
    /// playback finishes; we always call from Task.Run so this is safe.
    /// </summary>
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string? pszSound, nint hmod, uint fdwSound);

    private const uint SND_SYNC      = 0x00000000;
    private const uint SND_NODEFAULT = 0x00000002;
    private const uint SND_FILENAME  = 0x00020000;

    // ── WinMM P/Invoke – MIDI ────────────────────────────────────────────────

    [DllImport("winmm.dll")]
    private static extern uint midiOutOpen(
        out nint lphMidiOut,
        uint     uDeviceID,
        nint     dwCallback,
        nint     dwCallbackInstance,
        uint     dwFlags);

    [DllImport("winmm.dll")]
    private static extern uint midiOutClose(nint hMidiOut);

    [DllImport("winmm.dll")]
    private static extern uint midiOutShortMsg(nint hMidiOut, uint dwMsg);

    // MIDI_MAPPER = (UINT)-1 – routes to the Windows default MIDI output device
    private const uint MidiMapper = uint.MaxValue;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void PlayNewApSound()
    {
        var path = Path.Combine(SoundDirectory, NewApSoundFile);
        _ = Task.Run(() => PlayWavSync(path));
    }

    /// <inheritdoc/>
    public void PlaySigBasedSound(int signal)
    {
        // Mirror say.au3 _SigBasedSound(): play new-AP WAV at system volume.
        // (True per-sound volume adjustment would require waveOut API calls;
        //  the original AutoIt also just called SoundSetWaveVolume which sets
        //  the global wave volume - a heavy-handed approach best avoided.)
        var path = Path.Combine(SoundDirectory, NewApSoundFile);
        _ = Task.Run(() => PlayWavSync(path));
    }

    /// <inheritdoc/>
    public void SpeakSignalWithWavFiles(int signal, bool sayPercent)
    {
        _ = Task.Run(() => SpeakNumberWithWavFiles(signal, sayPercent));
    }

    /// <inheritdoc/>
    public void SpeakWithSapi(string text)
    {
        // Mirror say.au3 _TalkOBJ(): create a fresh SAPI voice object, speak, discard.
        _ = Task.Run(() =>
        {
            try
            {
                using var synth = new SpeechSynthesizer();
                synth.SetOutputToDefaultAudioDevice();
                synth.Speak(text);
            }
            catch { /* audio errors are non-fatal */ }
        });
    }

    /// <inheritdoc/>
    public void PlayMidiForSignal(int signal, int instrument, int playTimeMs)
    {
        _ = Task.Run(() => PlayMidiNote(instrument, signal, playTimeMs));
    }

    /// <inheritdoc/>
    public void PlayMidiForActiveAps(IEnumerable<int> signals, int instrument, int playTimeMs)
    {
        // Mirror Vistumbler's _PlayMidiForActiveAPs(): skip if still playing
        // (the original checked ProcessExists($MidiProcess) == 0).
        if (_isMidiPlaying) return;

        var list = signals.ToList();
        if (list.Count == 0) return;

        _ = Task.Run(() =>
        {
            _isMidiPlaying = true;
            try
            {
                foreach (var sig in list)
                    PlayMidiNote(instrument, sig, playTimeMs);
            }
            finally
            {
                _isMidiPlaying = false;
            }
        });
    }

    // ── Private – WAV helpers ────────────────────────────────────────────────

    private void PlayWavSync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
        PlaySound(filePath, nint.Zero, SND_FILENAME | SND_SYNC | SND_NODEFAULT);
    }

    /// <summary>
    /// Speaks a number 0-100 by sequentially playing individual WAV word files.
    /// Mirrors say.au3 <c>_SpeakSignal()</c>.
    /// </summary>
    private void SpeakNumberWithWavFiles(int signal, bool sayPercent)
    {
        if (signal < 0 || signal > 100) return;

        if (signal == 100)
        {
            PlayWavSync(Wav("one"));
            PlayWavSync(Wav("hundred"));
            if (sayPercent) PlayWavSync(Wav("percent"));
            return;
        }

        int tens = signal / 10;
        int ones = signal % 10;
        bool onesPlayed = false;

        if (tens == 1) // teens 10-19
        {
            PlayWavSync(Wav(signal switch
            {
                10 => "ten",
                11 => "eleven",
                12 => "twelve",
                13 => "thirteen",
                14 => "fourteen",
                15 => "fifteen",
                16 => "sixteen",
                17 => "seventeen",
                18 => "eightteen",   // matches original typo in say.au3
                19 => "nineteen",
                _  => string.Empty
            }));
            onesPlayed = true;
        }
        else if (tens >= 2)
        {
            PlayWavSync(Wav(tens switch
            {
                2 => "twenty",
                3 => "thirty",
                4 => "fourty",      // matches original typo in say.au3
                5 => "fifty",
                6 => "sixty",
                7 => "seventy",
                8 => "eighty",
                9 => "ninety",
                _ => string.Empty
            }));
        }

        if (!onesPlayed)
        {
            PlayWavSync(Wav((ones == 0 && tens == 0) ? "zero" : ones switch
            {
                1 => "one",
                2 => "two",
                3 => "three",
                4 => "four",
                5 => "five",
                6 => "six",
                7 => "seven",
                8 => "eight",
                9 => "nine",
                _ => string.Empty
            }));
        }

        if (sayPercent) PlayWavSync(Wav("percent"));
    }

    private string Wav(string name) =>
        string.IsNullOrEmpty(name) ? string.Empty : Path.Combine(SoundDirectory, name + ".wav");

    // ── Private – MIDI helpers ───────────────────────────────────────────────

    /// <summary>
    /// Maps a Vistumbler signal percentage (0-100) to a MIDI note number.
    ///
    /// Mirrors the signal→pitch table in say.au3 <c>_PlayMidi()</c>:
    ///   1-9   → A0  (21)
    ///   10-14 → A#0 (22)
    ///   15    → B0  (23)
    ///   16-100 → note = signal + 8  (C1=24 … C8=108)
    /// </summary>
    private static int SignalToMidiNote(int signal)
    {
        if (signal <= 0)   return -1;
        if (signal <= 9)   return 21;          // A0
        if (signal <= 14)  return 22;          // A#0
        if (signal == 15)  return 23;          // B0
        if (signal <= 100) return signal + 8;  // C1 (24) … C8 (108)
        return -1;
    }

    /// <summary>
    /// Opens the MIDI mapper, sets the instrument, plays a note for
    /// <paramref name="playTimeMs"/> ms, then closes the device.
    /// Mirrors the NoteOn/Sleep/NoteOff sequence in say.au3 <c>_PlayMidi()</c>.
    ///
    /// MIDI short-message layout (little-endian DWORD):
    ///   Byte 0 : status  (0x90 = Note On ch1, 0x80 = Note Off ch1, 0xC0 = Program Change ch1)
    ///   Byte 1 : data 1  (note number or instrument number)
    ///   Byte 2 : data 2  (velocity)
    /// </summary>
    private static void PlayMidiNote(int instrument, int signal, int playTimeMs)
    {
        int note = SignalToMidiNote(signal);
        if (note < 0) return;

        if (midiOutOpen(out nint handle, MidiMapper, nint.Zero, nint.Zero, 0) != 0)
            return;

        try
        {
            // Program Change – select instrument on channel 1
            midiOutShortMsg(handle, (uint)(0xC0 | ((instrument & 0x7F) << 8)));

            // Note On – channel 1, velocity 127 (maximum)
            midiOutShortMsg(handle, (uint)(0x90 | ((note & 0x7F) << 8) | (127 << 16)));

            Thread.Sleep(Math.Max(1, playTimeMs));

            // Note Off – channel 1
            midiOutShortMsg(handle, (uint)(0x80 | ((note & 0x7F) << 8) | (127 << 16)));
        }
        finally
        {
            midiOutClose(handle);
        }
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
