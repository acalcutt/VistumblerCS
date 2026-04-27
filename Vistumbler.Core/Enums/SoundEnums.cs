namespace Vistumbler.Core.Enums;

/// <summary>
/// Controls when the new-AP sound fires during a scan loop.
/// Mirrors Vistumbler's Sound/SoundPerAP and Sound/NewSoundSigBased INI values.
/// </summary>
public enum SoundPerApMode
{
    OncePerLoop   = 0,
    OncePerAp     = 1,
    OncePerApSigBased = 2   // volume proportional to signal strength
}

/// <summary>
/// Backend used to speak the signal value aloud.
/// Mirrors Vistumbler's MIDI/SpeakType INI value (1/2/3).
/// </summary>
public enum SpeakSoundType
{
    VistumblerSounds = 1,   // individual WAV word files  (zero.wav, one.wav …)
    Sapi             = 2,   // Microsoft SAPI text-to-speech
    Midi             = 3    // MIDI note whose pitch represents signal strength
}
