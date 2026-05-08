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
    private readonly Action? _onMapLoaded;

    public DelegateMapObserver(Action<string, string> log, Action? onStyleLoaded = null, Action? onMapLoaded = null)
    {
        _log           = log;
        _onStyleLoaded = onStyleLoaded;
        _onMapLoaded   = onMapLoaded;
    }

    protected override void onDidFailLoadingMap(MapLoadError type, string description)
        => _log("Fail",    $"[{type}] {description}");

    protected override void onDidFinishLoadingMap()
    {
        _log("Finish", "Map loaded");
        _onMapLoaded?.Invoke();
    }

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
