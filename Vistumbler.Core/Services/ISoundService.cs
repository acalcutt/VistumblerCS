namespace Vistumbler.Core.Services;

/// <summary>
/// Replaces the say.exe external process used by the original Vistumbler.
/// All playback methods return immediately; audio runs on background threads
/// so the scan loop is never blocked.
/// </summary>
public interface ISoundService
{
    /// <summary>Directory that contains the Vistumbler WAV word files (zero.wav, one.wav …).</summary>
    string SoundDirectory { get; set; }

    /// <summary>File name (not full path) of the "new AP" WAV file, e.g. "new_ap.wav".</summary>
    string NewApSoundFile { get; set; }

    // ── New-AP sound ─────────────────────────────────────────────────────────

    /// <summary>Plays the new-AP WAV file on a background thread.</summary>
    void PlayNewApSound();

    /// <summary>
    /// Plays the new-AP WAV file at a volume proportional to signal strength.
    /// Mirrors say.au3 <c>_SigBasedSound()</c> type 5.
    /// </summary>
    void PlaySigBasedSound(int signal);

    // ── Signal speaking ──────────────────────────────────────────────────────

    /// <summary>
    /// Speaks a signal value 0-100 by playing sequential word WAV files.
    /// Mirrors say.au3 <c>_SpeakSignal()</c> type 1.
    /// </summary>
    void SpeakSignalWithWavFiles(int signal, bool sayPercent);

    /// <summary>
    /// Speaks arbitrary text via Microsoft SAPI TTS on a background thread.
    /// Mirrors say.au3 <c>_TalkOBJ()</c> type 2.
    /// </summary>
    void SpeakWithSapi(string text);

    // ── MIDI ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays a single MIDI note whose pitch corresponds to the given signal (0-100).
    /// Mirrors say.au3 <c>_PlayMidi()</c> type 3.
    /// </summary>
    void PlayMidiForSignal(int signal, int instrument, int playTimeMs);

    /// <summary>
    /// Plays a MIDI note for each signal in the list (one per active AP).
    /// Skips silently if a previous call is still playing.
    /// Mirrors say.au3 type 4 and Vistumbler's <c>_PlayMidiForActiveAPs()</c>.
    /// </summary>
    void PlayMidiForActiveAps(IEnumerable<int> signals, int instrument, int playTimeMs);
}
