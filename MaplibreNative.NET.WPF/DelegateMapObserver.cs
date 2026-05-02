using System;
using MaplibreNative;

namespace MaplibreNative.WPF;

/// <summary>
/// Simple MapObserver that forwards events to a delegate, useful for logging.
/// An optional <paramref name="onStyleLoaded"/> callback is invoked whenever
/// the style finishes loading (including after style URL changes).
/// </summary>
public class DelegateMapObserver : MapObserver
{
    private readonly Action<string, string> _log;
    private readonly Action? _onStyleLoaded;

    public DelegateMapObserver(Action<string, string> log, Action? onStyleLoaded = null)
    {
        _log            = log;
        _onStyleLoaded  = onStyleLoaded;
    }

    protected override void onDidFailLoadingMap(MapLoadError type, string description)
        => _log("Fail",    $"[{type}] {description}");

    protected override void onDidFinishLoadingMap()
        => _log("Finish",  "Map loaded");

    protected override void onWillStartLoadingMap()
        => _log("Will",    "WillStartLoading");

    protected override void onDidFinishLoadingStyle()
    {
        _log("Style", "DidFinishLoadingStyle");
        _onStyleLoaded?.Invoke();
    }

    protected override void onDidFinishRenderingFrame(RenderFrameStatus status)
        => _log("Frame",   $"mode={status.mode} needsRepaint={status.needsRepaint}");
}
