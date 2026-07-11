# Changelog

## master
### ✨ Features and improvements
- _...Add new stuff here..._

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.4.3
### 🐞 Bug fixes
- **Attribution overlay no longer re-expands on every runtime source refresh** — updated the map renderer to MapLibreNative.Maui.WPF 4.2.1, whose WPF control only rewrites and re-expands the attribution overlay when the attribution content actually changes. Previously any periodically-updated runtime source (e.g. a live GeoJSON overlay) made the overlay pop open on every update.

## 0.4.2
### ✨ Features and improvements
- **Updated map renderer to MapLibreNative.Maui.WPF 4.2.0** (from 4.1.3) — 4.2.0 is an Android-focused release (map-open crash, two-finger gestures, rotation/tile-render fixes) with no Windows-facing changes; bumped to stay current on the released package.

### 🐞 Bug fixes

## 0.4.1
### ✨ Features and improvements
- **Updated map renderer to MapLibreNative.Maui.WPF 4.1.3** (from 4.0.0) — picks up the 4.1.x fixes on top of the airspace-free 4.0.0 renderer: `AddLineLayer`/`AddFillLayer`/`AddRasterLayer` wrappers on `MlnMapImage` (4.1.0), the attribution overlay now refreshes when sources are added after the style loads (4.1.1), and the runtime source-layer relayout fix moved to the upstream maplibre-native fix (4.1.3).

### 🐞 Bug fixes

## 0.4.0
### ✨ Features and improvements
- **New airspace-free map renderer (MapLibreNative.Maui.WPF 4.0.0)** — the map moved from the old `MlnMapHost` (HwndHost + floating overlay popups) to `MlnMapImage`, a true in-tree WPF `Image` element whose navigation/GPS controls are ordinary WPF children with correct z-order, clipping, DPI and hit-testing — no more popup realignment/airspace quirks.
- **Offline map caching** — the map now keeps a persistent tile cache, so already-viewed areas keep rendering with no network. A new map-toolbar **Save Map Area** button pre-caches the current view (current zoom + 2 levels) for offline use, and an **Offline** toggle forces MapLibre to serve only cached tiles; caching progress and offline/online state are reported in the status bar. Downloaded tiles share the live map's cache, so they render immediately.
- **Offline area management (Settings → Map)** — the Map settings tab lists every saved offline area with its name, zoom range, cached size and download status, and can Refresh, Delete one, Delete All, or Clear the (non-saved) tile cache to reclaim disk space.
- **Configurable map AP colors (Settings → Map)** — the per-age-bucket circle colors (Open / WEP / Secure, live-active through 10+ years) are now editable from the Map settings tab and applied to the live map without a restart.

### 🐞 Bug fixes

## 0.3.1
### ✨ Features and improvements
- add icon and release signing ([#10](https://github.com/acalcutt/VistumblerCS/pull/10)) (@acalcutt)

## 0.3.0
### ✨ Features and improvements
- Fix single-file publish crash + GPS/WifiDB/Settings feature ([#3](https://github.com/acalcutt/VistumblerCS/pull/3)) (@acalcutt)

## 0.2.0
### ✨ Features and improvements
- Migrate map rendering from MaplibreNative.NET (C++/CLI) to MapLibreNative.Maui.WPF (P/Invoke) + interactive map view ([#1](https://github.com/acalcutt/VistumblerCS/pull/1)) (@acalcutt)

## 0.1.0
### ✨ Features and improvements
- Initial C# WPF port of VistumblerMDB
- Native WiFi scanning via ManagedNativeWifi
- SQLite database for AP persistence
- GPS support (serial / GPSD / file)
- Signal history graphs
- Manufacturer lookup from IEEE OUI database
- Interface menu for per-adapter filtering
- Options menu matching original Vistumbler layout
- View menu with Filters submenu

### 🐞 Bug fixes
- N/A — initial release
