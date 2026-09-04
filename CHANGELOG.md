# Changelog

## master
### ✨ Features and improvements
- **3D terrain button on the map** — the renderer's terrain control, shown only when terrain is actually available. It drapes the basemap over elevation and is a plain toggle: on, then off again.
  Which DEM it uses is decided per style, because getting it wrong is worse than not offering the button. A style that declares its own raster-dem is draped over that one — it is the source the style's hillshade and relief layers already draw from, so the relief matches what is drawn and nothing extra is fetched. The WifiDB styles each carry several DEMs, and the choice is not arbitrary: picking the first would drape the map over GEBCO bathymetry (ocean depths) rather than land, so a source the style names as terrain wins, then a `terrain`-named one, then whatever single DEM is there.
  A style with no DEM at all still gets the button — the app adds one itself, defaulting to WifiDB's own terrain tiles, the same elevation the relief styles use. [Mapterhorn](https://mapterhorn.com/data-access/) and the [AWS Open Data terrain-tiles](https://registry.opendata.aws/terrain-tiles/) are built in beside it, so the button works on a custom style too.
- **History layers read from the published archives directly** — WifiDB's map now addresses its pmtiles-swarm archives itself rather than going through `tilejson.php`, and the app follows. It was already reaching the same archives, since that endpoint answers 302 to them, so what changes is that it asks for them straight away: one request instead of two, and the history layers still draw when wifidb.net is down but the archives are up. The layer inside each archive is named for its bucket — the same name the endpoint reported — so every source-layer and colour is untouched.
  Each URL now carries the archive's `.torrent` and magnet in its fragment, which is the part a redirect could never give the app: a fragment is not sent in a request, so the HTTP stack that follows the redirect never surfaces it. Nothing reads them yet and no torrent is fetched — they are there so peer-to-peer tile loading can be switched on later without every URL changing again. The `.torrent` handle points at WifiDB's own per-bucket endpoint rather than being built from the magnet's infohash, which named one build and went stale as the archives were rebuilt.
  `cell_networks` has no published archive and keeps the endpoint, as does everything if the archives cannot be reached at all. The **WifiDB Data URL** setting still chooses where they are fetched from.
- **History overlays keep working when wifidb.net cannot be reached** — the age-tier and cell layers still ask WifiDB for each bucket's TileJSON, which now points them at that bucket's published PMTiles archive on data.wifidb.net. If WifiDB itself is unreachable, the app now goes to those archives directly instead of drawing nothing, using a built-in address per bucket. Each fallback address carries the bucket's `.torrent` and magnet in its fragment — unused for now, so the handles are already in place when peer-to-peer tile loading arrives. The magnets name a bucket rather than one nightly build, so they stay valid as the archives are rebuilt.
- **Map renderer updated to MapLibreNative.Maui.WPF 5.0.0-pre.2** (from 4.4.0). The renderer's low-level binding types were renamed `Mbgl*` → `Mln*`, so the two places this app reaches past the map control — the offline manager and the online/offline toggle — now use `MlnOfflineManager` and `MlnNetwork`. Nothing about the map control's own API changed, and no behaviour here changes with it. The update also brings 3D terrain with a runtime API and an on-map toggle, a selectable terrain load mode, a Vulkan rendering path alongside OpenGL, tile-template raster/vector sources that can declare their own attribution, and a fix for the attribution banner wrapping well short of the map's width. The terrain control is used by the entry below; the rest is not, yet.
- **New "WifiDB Data URL" setting (Settings → WifiDB → WifiDB URLs)** — the origin those archives are fetched from, separate from the site and API URLs because it is a different host. Defaults to `https://data.wifidb.net`; point it at a mirror to move the fallback.

### 🐞 Bug fixes
- _...Add new stuff here..._

## 0.4.4
### ✨ Features and improvements
- **Reworked GPS map buttons: separate tracking and bearing controls** — updated the map renderer to MapLibreNative.Maui.WPF 4.4.0 (from 4.2.1). The top GPS button now cycles the tracking mode Off ○ → Show ⊙ → Follow ◎, and the bottom button (previously a plain reset-to-north) cycles the camera bearing mode Free ↺ → North-up N → GPS bearing ➤. Dragging the map while in Follow drops back to Show (one click re-enters Follow), and manually rotating the map drops the bearing mode back to Free. The location dot now always points in the direction of travel.
- Also picks up 4.2.2–4.3.0: a manually opened attribution banner stays open longer (10 s) instead of collapsing almost immediately, and the renderer gains an opt-in `UiScale` property for honouring the OS font-scale setting.

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
