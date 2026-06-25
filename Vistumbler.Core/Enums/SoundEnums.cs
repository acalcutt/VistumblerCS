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

/// <summary>
/// Signal graph display mode.
/// Mirrors Vistumbler's $Graph variable: 0=hidden, 1=line (Graph1), 2=bar (Graph2).
/// </summary>
public enum GraphMode
{
    Hidden = 0,
    Line   = 1,   // Graph1 – line chart, last 50 points
    Bar    = 2,   // Graph2 – bar chart, one bar per pixel of width
    Map    = 3    // Map – MapLibre interactive map
}
