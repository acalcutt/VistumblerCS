# Vistumbler CS - WiFi Scanner

A modern C# rewrite of the Vistumbler WiFi scanner application, built with .NET 10, WPF, and clean architecture principles.

## Project Structure

```
VistumblerCS/
├── Vistumbler.Core/              # Domain models, interfaces, and business logic
│   ├── Models/                   # Domain entities (AccessPoint, GpsData, SignalHistory)
│   ├── Services/                 # Service interfaces
│   ├── Repositories/             # Repository interfaces
│   └── Enums/                    # Enumerations
├── Vistumbler.Infrastructure/    # Implementation of services and data access
│   ├── WiFi/                     # WiFi scanning implementation
│   ├── Gps/                      # GPS communication and NMEA parsing
│   ├── Data/                     # SQLite database implementation
│   ├── Export/                   # Export functionality (KML, CSV, GPX, NS1, NetXML, Kismet, VS1/VSZ)
│   ├── Import/                   # Import functionality (NS1, NetXML, Kismet, VS1/VSZ, CSV)
│   ├── Sound/                    # Sound alert service
│   └── Settings/                 # INI-based settings persistence
├── Vistumbler.UI/                # WPF user interface
│   ├── ViewModels/               # MVVM ViewModels
│   ├── Views/                    # XAML views
│   ├── Controls/                 # Custom controls (SignalGraph, ChannelGraph)
│   ├── Extensions/               # MapLibre WiFi layer extension methods
│   ├── Converters/               # Value converters
│   └── Resources/                # Images, styles, sounds
└── Vistumbler.Tests/             # Unit and integration tests
```

## Architecture

### Clean Architecture Layers

1. **Core Layer** (Vistumbler.Core)
   - Contains domain models and interfaces
   - No dependencies on other layers
   - Framework-agnostic

2. **Infrastructure Layer** (Vistumbler.Infrastructure)
   - Implements interfaces from Core
   - Contains concrete implementations for WiFi scanning, GPS, and database
   - Depends only on Core

3. **UI Layer** (Vistumbler.UI)
   - WPF application with MVVM pattern
   - Depends on Core and Infrastructure
   - Uses dependency injection

### Key Technologies

- **.NET 10.0**: Modern, cross-platform framework
- **WPF**: Rich desktop UI framework (AnyCPU — x64 and ARM64)
- **SQLite**: Lightweight, file-based database (replaces Access MDB)
- **Dapper**: Micro-ORM for efficient database operations
- **ManagedNativeWifi**: Native WiFi API wrapper
- **CommunityToolkit.Mvvm**: MVVM helpers and commands
- **MapLibreNative.Maui.WPF**: Hardware-accelerated map rendering via P/Invoke (OpenGL)

## Features

### Implemented

- ✅ WiFi network scanning using Native WiFi API
- ✅ GPS integration — serial port (NMEA) and Windows Location API
- ✅ NMEA sentence parsing (GPGGA, GPRMC)
- ✅ SQLite database for storing access points and signal history
- ✅ Real-time UI updates with MVVM pattern
- ✅ Access point listing with sorting and filtering
- ✅ Signal strength tracking
- ✅ Manufacturer lookup by MAC address
- ✅ Custom labels for access points
- ✅ Signal strength graph (line and bar modes)
- ✅ Channel graphs (2.4 GHz, 5 GHz, 6 GHz)
- ✅ Interactive MapLibre map view with live AP overlay
- ✅ GPS location indicator on map
- ✅ WifiDB vector tile layers (daily / weekly / monthly / 0–1 yr / 1–2 yr / 2–3 yr / 3+ yr)
- ✅ AP info popup on map click (SSID, MAC, channel, auth, encryption, manufacturer, timestamps)
- ✅ Export: KML, GPX, CSV, VS1/VSZ, NS1, NetXML, Kismet DB
- ✅ Import: VS1/VSZ, NS1, NetXML, Kismet DB, CSV
- ✅ Auto-save and auto-clear
- ✅ Auto KML generation with optional Google Earth network link
- ✅ Sound alerts (new AP, GPS fix, etc.)
- ✅ WiFiDB locate lookup for selected AP
- ✅ Multi-language support (INI-based language files)
- ✅ Minimal GUI mode

## Getting Started

### Prerequisites

- Visual Studio 2022 or later (or JetBrains Rider)
- .NET 10.0 SDK
- Windows 10/11 (for WiFi API support)
- GPS device (optional, for location tracking)

### Building the Solution

1. Clone the repository
2. Open `VistumblerCS.sln` in Visual Studio
3. Restore NuGet packages:
   ```
   dotnet restore
   ```
4. Build the solution:
   ```
   dotnet build
   ```
5. Run the UI project:
   ```
   dotnet run --project Vistumbler.UI
   ```

### Running Tests

```bash
dotnet test
```

## Database Migration

### From Access MDB to SQLite

The original Vistumbler used Microsoft Access (.mdb) files. This version uses SQLite for better performance and portability.

#### Migration Strategy

1. **Automatic Migration** (planned):
   - Tool to read existing MDB files
   - Convert data to SQLite format
   - Preserve all access points, signal history, and GPS data

2. **Manual Migration**:
   ```csharp
   // Example migration code
   var migrationService = new MdbToSqliteMigration();
   await migrationService.MigrateAsync(
       "path/to/old.mdb",
       "path/to/new.db"
   );
   ```

#### Schema Comparison

**Old (Access MDB):**
- Separate tables: AP, HIST, GPS
- Uses GUID for primary keys
- Complex relationships

**New (SQLite):**
- Normalized schema with foreign keys
- Integer primary keys for better performance
- Simplified relationships
- Indexed for fast queries

## Usage

### Starting a Scan

1. Click **Scan** button or use menu: Extra → Scan APs
2. Access points will appear in the list as they're discovered
3. Click **Stop** to stop scanning

### Using GPS

1. Configure GPS settings: Options → Settings → GPS
2. Select COM port and baud rate
3. Click **GPS** button or use menu: Extra → Use GPS
4. Current position will display in the toolbar

### Viewing Details

- Click an access point in the list to view details
- See signal strength, manufacturer, security settings
- View signal history as a line or bar graph (toggle with Graph 1 / Graph 2 buttons)
- Open channel graphs for 2.4 GHz, 5 GHz, or 6 GHz via the View menu
- Switch to Map view to see scanned APs on a live MapLibre map

### Map View

1. Click the **Map** button in the toolbar to switch to map mode
2. The map loads the WifiDB relief style by default
3. Use the layer buttons (Daily, Weekly, Monthly, 0–1 yr, etc.) to toggle WifiDB vector tile overlays
4. Scanned APs with GPS coordinates appear as colour-coded circles in real time:
   - Green = Open, Orange = WEP, Red = WPA/Secure
5. Click a circle on the WifiDB layers to see AP details in a popup
6. The GPS location indicator tracks your current position when GPS is active

### Exporting Data

- File → Export → Choose format (KML, GPX, CSV, VS1, VSZ, NS1, NetXML, Kismet DB)
- Select destination file
- Choose filtered or all access points

### Importing Data

- File → Import
- Supports VS1, VSZ, NS1, NetXML, Kismet DB, CSV

## Configuration

Settings are stored in:
```
%USERPROFILE%\Documents\Vistumbler\settings.json
```

Database location:
```
%USERPROFILE%\Documents\Vistumbler\vistumbler.db
```

## Performance Improvements

Compared to the original AutoIt version:

- **Faster scanning**: Native C# and async operations
- **Better memory management**: Automatic garbage collection
- **Responsive UI**: MVVM with background workers
- **Efficient database**: SQLite with indexes and prepared statements
- **Modern libraries**: Optimized third-party packages

## Development Roadmap

### Completed
- [x] Core architecture
- [x] WiFi scanning
- [x] GPS integration (serial NMEA + Windows Location API)
- [x] Basic UI
- [x] Database layer (SQLite)
- [x] Signal graphing (line and bar)
- [x] Channel visualization (2.4 GHz, 5 GHz, 6 GHz)
- [x] Export (KML, GPX, CSV, VS1/VSZ, NS1, NetXML, Kismet DB)
- [x] Import (VS1/VSZ, NS1, NetXML, Kismet DB, CSV)
- [x] Auto-save / auto-KML
- [x] Sound alerts
- [x] WiFiDB locate lookup
- [x] Multi-language support
- [x] MapLibre interactive map with live AP overlay and WifiDB vector tile layers

### Planned / In Progress
- [ ] GPS compass widget
- [ ] Google Earth auto-open integration
- [ ] Advanced filtering presets
- [ ] MDB → SQLite migration tool

## Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch
3. Follow the existing code style
4. Add unit tests for new features
5. Submit a pull request

## Code Style

- Use C# naming conventions
- Follow SOLID principles
- Use async/await for I/O operations
- Document public APIs with XML comments
- Write unit tests for business logic

## License

This project maintains compatibility with the original Vistumbler license:
- GPL v2.0 or later

## Original Vistumbler

Original project by Andrew Calcutt:
- Website: http://www.vistumbler.net
- GitHub: https://github.com/acalcutt/Vistumbler

## Acknowledgments

- Andrew Calcutt - Original Vistumbler creator
- ManagedNativeWifi library contributors
- .NET Community Toolkit team

## FAQ

**Q: Why rewrite in C#?**
A: Modern language features, better performance, easier maintenance, richer ecosystem.

**Q: Will it work on Linux/Mac?**
A: The Native WiFi API is Windows-specific, but most code is portable. Cross-platform support could be added with platform-specific adapters.

**Q: Can I import my old .vs1 files?**
A: Yes, import functionality is planned for Phase 2.

**Q: How do I migrate my old database?**
A: A migration tool is planned. For now, you can export to VS1 and re-import.

**Q: Is this compatible with WiFiDB?**
A: Yes — the app can look up a selected AP on WifiDB, and the Map view can display WifiDB vector tile layers for daily, weekly, monthly, and historical data.

## Support

- Report issues on GitHub
- Visit the Vistumbler forums
- Check the Wiki for detailed documentation

## Version History

### 0.1.0 (Current)
- Initial C# implementation
- Core scanning functionality
- GPS support (serial NMEA + Windows Location API)
- SQLite database
- WPF UI with signal graphs, channel graphs, and MapLibre map view
- Full import/export suite (KML, GPX, CSV, VS1/VSZ, NS1, NetXML, Kismet DB)
- WifiDB vector tile map layers
- Sound alerts, auto-save, auto-KML
- Multi-language support
