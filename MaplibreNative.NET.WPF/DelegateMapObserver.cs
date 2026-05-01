using System;
using MaplibreNative;

namespace MaplibreNative.WPF;

/// <summary>
/// Simple MapObserver that forwards events to a delegate, useful for logging.
/// </summary>
public class DelegateMapObserver : MapObserver
{
    private readonly Action<string, string> _log;

    public DelegateMapObserver(Action<string, string> log) { _log = log; }

    protected override void onDidFailLoadingMap(MapLoadError type, string description)
        => _log("Fail",    $"[{type}] {description}");

    protected override void onDidFinishLoadingMap()
        => _log("Finish",  "Map loaded");

    protected override void onWillStartLoadingMap()
        => _log("Will",    "WillStartLoading");

    protected override void onDidFinishLoadingStyle()
        => _log("Style",   "DidFinishLoadingStyle");

    protected override void onDidFinishRenderingFrame(RenderFrameStatus status)
        => _log("Frame",   $"mode={status.mode} needsRepaint={status.needsRepaint}");
}
