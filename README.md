# Vistumbler CS

> **Experimental preview.** Vistumbler CS is still being developed: expect missing features, bugs and changes between versions. For everyday scanning, the stable release is [Vistumbler](https://gitlab.techidiots.net/techidiots-llc/Vistumbler).

Vistumbler CS is a rewrite of the [Vistumbler](https://gitlab.techidiots.net/techidiots-llc/Vistumbler) WiFi scanner in C#, built on .NET 10 and WPF. It keeps Vistumbler's workflow (scan, record where access points were seen with GPS, export) and adds an interactive map, a SQLite database and native ARM64 support.

Website: [vistumbler.net/cs](https://www.vistumbler.net/cs/) · Forum: [forum.techidiots.net](https://forum.techidiots.net/forum/) · Changelog: [CHANGELOG.md](CHANGELOG.md)

## Download

Get the latest version from the [releases page](https://gitlab.techidiots.net/techidiots-llc/VistumblerCS/-/releases) (also mirrored to [GitHub](https://github.com/acalcutt/VistumblerCS/releases)). Each release has, for both **x64** and **ARM64** PCs:

| File | What it is |
| :--- | :--- |
| `VistumblerCS-vX.Y.Z-win-x64-setup.exe` | Installer, with a Start Menu shortcut and uninstaller (recommended) |
| `VistumblerCS-vX.Y.Z-win-x64.zip` | Portable copy: unzip and run `Vistumbler.UI.exe` |

Both are self-contained, so there's no separate .NET to install. Pick `arm64` on Windows on ARM devices.

### Requirements

* Windows 10 or 11, x64 or ARM64
* A WiFi adapter. A GPS receiver is optional.

## Features

* WiFi scanning with the Windows Native WiFi API
* GPS from serial (NMEA) receivers or the Windows Location API, with GPS details and compass windows
* Interactive MapLibre map with live access points, your GPS position, WiFiDB history and cell tower layers, 3D terrain and offline map downloads
* Signal strength graphs (line and bar) and channel graphs for 2.4, 5 and 6 GHz
* Sorting, tree view, filters, custom labels and manufacturer lookup
* Import: VS1/VSZ, NS1, NetXML, Kismet DB and CSV, one file or a whole folder
* Export: KML, GPX, CSV, VS1/VSZ, NS1, NetXML and Kismet DB
* Auto-save and clear, Auto KML with a Google Earth network link, and sound alerts
* Upload to [WiFiDB](https://wifidb.net/wifidb/) and look up access points there
* Translations through Vistumbler-style language files

## Using Vistumbler CS

* **Scanning:** press **Scan** on the toolbar (or **Options → Scan APs**). Access points appear as they're found. Select one to see its details and signal graph.
* **GPS:** choose your receiver under **Settings → GPS Settings**, then turn on GPS from the toolbar.
* **Map:** switch to the map from the toolbar. Scanned access points with a GPS position appear live, colour-coded by security. Use the layer buttons to show WiFiDB history.
* **Import / export:** **File → Import** and **File → Export**.
* **Sessions:** each run records into a session database. On startup you can resume a previous session or start a new one. **File → Exit (Save DB)** keeps the session, while **File → Exit** deletes it.
* **Updates:** **Help → Check for Updates**. Vistumbler CS also checks at startup when "Automatically Check For Updates" is on, and "Include Pre-release Updates" offers pre-releases too. An installed copy downloads and runs the new installer, then restarts with your session kept. A portable copy opens the download page.

### Where your data is kept

| What | Where |
| :--- | :--- |
| Settings | `%AppData%\VistumblerCS\vistumbler_settings.ini` |
| Session databases (SQLite) | `Documents\Vistumbler\sessions\` |

### Coming from Vistumbler

Vistumbler CS uses SQLite instead of Vistumbler's Access (`.mdb`) databases, and can't open `.mdb` files directly yet. To bring your scans across, export them from Vistumbler as **VS1** or **VSZ**, then import that file with **File → Import**. A direct MDB-to-SQLite migration (`MdbToSqliteMigration` in `Vistumbler.Infrastructure`) exists but isn't available in the app yet.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows 10 or 11. Visual Studio 2022 or later, or Rider, is optional.

```bash
dotnet build VistumblerCS.sln
dotnet run --project Vistumbler.UI
dotnet test
```

### Project structure

```
VistumblerCS.sln
├── Vistumbler.Core/             Domain models, enums and service interfaces (no UI or platform code)
├── Vistumbler.Infrastructure/   Implementations: WiFi scanning, GPS, SQLite data, import, export, settings, sound
├── Vistumbler.UI/               WPF app (MVVM): views, view models, controls (graphs), map extensions, services
├── Vistumbler.Tests/            xUnit tests
└── build/                       Windows installer (NSIS) and code-signing scripts used by CI
```

The UI uses MVVM with [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) and dependency injection. Maps are drawn by [MapLibreNative.Maui.WPF](https://www.nuget.org/packages/MapLibreNative.Maui.WPF). Further notes are in [DEVELOPMENT_GUIDE.md](DEVELOPMENT_GUIDE.md) and [QUICK_START.md](QUICK_START.md).

### Releases

GitLab CI (`.gitlab-ci.yml`) builds every merge request and push to `main`. When `main` has a version with no release yet, CI publishes one: x64 and ARM64 zips and installers, signed when a signing certificate is configured, with release notes taken from `CHANGELOG.md`. To make a release, run a pipeline on `main` with `BUMP_VERSION` set (for example `patch`, or `prerelease` for an `-rc` build). This opens a merge request that bumps the version and changelog.

## Contributing and support

* **Bugs and feature requests:** open an [issue](https://gitlab.techidiots.net/techidiots-llc/VistumblerCS/-/issues), or post on the [forum](https://forum.techidiots.net/forum/).
* **Code:** merge requests are welcome. Please follow the existing code style, add tests for new logic, and describe your change in the `master` section of [CHANGELOG.md](CHANGELOG.md).

## Related projects

* [Vistumbler](https://gitlab.techidiots.net/techidiots-llc/Vistumbler): the original, stable AutoIt version
* [Vistumbler MAUI](https://gitlab.techidiots.net/techidiots-llc/VistumblerMAUI): an experimental cross-platform version for Windows and Android

## License

GPL v2.0 or later, like the original Vistumbler.

Created by Andrew Calcutt, [TechIdiots LLC](https://www.techidiots.net).
