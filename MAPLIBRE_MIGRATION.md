# VistumblerCS MapLibre Migration Summary

## Overview
Successfully migrated VistumblerCS from MaplibreNative.NET-ac (C++/CLI) to maplibre-maui-ac (P/Invoke) packages.

## Changes Made

### 1. NuGet Configuration
- Created `NuGet.config` in the VistumblerCS root directory
- Added local package source pointing to `C:\Users\Andrew\Downloads` where the maplibre-maui-ac packages are located

### 2. Project File Updates

#### Upgraded to .NET 9
All projects upgraded from .NET 8 to .NET 9 to match the maplibre-maui-ac package requirements:
- `Vistumbler.UI`: `net9.0-windows10.0.19041.0`
- `Vistumbler.Core`: `net9.0-windows`
- `Vistumbler.Infrastructure`: `net9.0-windows10.0.19041.0`
- `Vistumbler.Tests`: `net9.0-windows10.0.19041.0`

#### Changed Platform Target
- **From**: `<PlatformTarget>x64</PlatformTarget>` (x64 only)
- **To**: `<PlatformTarget>AnyCPU</PlatformTarget>` (supports x64 and ARM64)

This enables VistumblerCS to run on Windows ARM64 devices (e.g., Snapdragon laptops).

#### Package References
In `Vistumbler.UI.csproj`:
- **Removed**: 
  - ProjectReference to `MaplibreNative.NET.WPF`
  - Direct reference to `MaplibreNative.NET.dll`
  - Content items for `MaplibreNative.NET.dll` and `Ijwhost.dll`
- **Added**:
  - PackageReference: `Maui.MapLibre.WPF` version `2.0.2`

### 3. XAML Updates

In `MainWindow.xaml`:
- Changed namespace from `xmlns:mlcontrols="clr-namespace:MaplibreNative.WPF;assembly=MaplibreNative.NET.WPF"`
- To: `xmlns:mlwpf="clr-namespace:Maui.MapLibre.WPF;assembly=Maui.MapLibre.WPF"`
- Changed control from `<mlcontrols:MaplibreMapHost>` to `<mlwpf:MlnMapHost>`

### 4. Code Updates

#### Created Extension Methods
New file: `Vistumbler.UI\Extensions\MaplibreWifiExtensions.cs`

Provides backward-compatible extension methods for the new `MlnMapHost` control:
- `SetWifiGeoJsonLayer(string sourceId, string geoJsonUrl)` - Add/update GeoJSON from URL
- `SetWifiGeoJsonLayerData(string sourceId, string geoJson)` - Add/update GeoJSON from inline data
- `RemoveWifiGeoJsonLayer(string sourceId)` - Remove WiFi layer set

These methods automatically create three circle layers (open/WEP/secure) with color-coded styling:
- **Open** (sectype=1): Green (#00802b)
- **WEP** (sectype=2): Orange (#cc7a00)
- **Secure** (sectype=3): Red (#b30000)

#### Updated MainWindow.xaml.cs
- Added `using Vistumbler.UI.Extensions;` to access extension methods
- No other code changes needed - all existing calls to MapHost methods work as-is

## API Compatibility

### Direct Mapping (No Changes Needed)
The following methods have identical signatures in both libraries:
- `CenterOn(double latitude, double longitude, double zoom)`
- `ZoomIn()`
- `ZoomOut()`
- `ResetNorth()`
- `UpdateLocationIndicator(double lat, double lon, float bearing, float accuracyMeters)`
- `ClearLocationIndicator()`

### Extension Methods (WiFi-Specific)
These custom methods were provided via extension methods:
- `SetWifiGeoJsonLayer(string sourceId, string geoJsonUrl)`
- `SetWifiGeoJsonLayerData(string sourceId, string geoJson)`
- `RemoveWifiGeoJsonLayer(string sourceId)`

## Build Results

✅ **Build Successful**
- All projects compile without errors
- Only pre-existing warnings remain (unrelated to migration)
- NuGet packages restore correctly from local source

## Testing Recommendations

1. **Map Rendering**: Verify the map loads and displays the WifiDB tile style
2. **Navigation**: Test zoom, pan, and north reset controls
3. **WiFi Layers**: Test adding/removing WifiDB GeoJSON layers (Daily, Weekly, Monthly, etc.)
4. **Live APs**: Verify live access point visualization during scanning
5. **GPS Indicator**: Test GPS location indicator (blue dot with bearing arrow)

## Benefits of New Library

1. **No C++/CLI dependency**: Pure P/Invoke implementation
2. **Better performance**: Direct native bindings without IJW overhead
3. **Unified codebase**: Shares core with MAUI implementation
4. **Active development**: Part of the actively maintained maplibre-maui-ac project
5. **Multi-architecture support**: Now supports both x64 and ARM64 (changed PlatformTarget to AnyCPU)

## Architecture Support

VistumblerCS now supports both **x64** and **ARM64** Windows platforms:

- **Development builds**: Use AnyCPU (runs on both architectures)
- **Production publishing**: Create architecture-specific builds
  - `dotnet publish -r win-x64 -c Release` for x64
  - `dotnet publish -r win-arm64 -c Release` for ARM64

The NuGet package system automatically includes the correct native binaries:
- `mln-cabi.dll` (MapLibre Native) - win-x64 or win-arm64
- `SQLite.Interop.dll` (System.Data.SQLite) - win-x64 or win-arm64

All other dependencies (ManagedNativeWifi, System.IO.Ports, etc.) are either pure managed code or support both architectures.

## Package Dependencies

The migration uses these NuGet packages from the local source:
- `Maui.MapLibre.WPF.2.0.2.nupkg` (main WPF control)
- `Maui.MapLibre.Native.2.0.2.nupkg` (P/Invoke bindings, pulled automatically)
- `Maui.MapLibre.Native.Vulkan.2.0.2.nupkg` (Vulkan renderer, pulled automatically)
- `Maui.Maplibre.Handlers.2.0.2.nupkg` (not used by WPF path)

## Notes

- The old `MaplibreNative.NET.WPF` folder remains in the repository but is no longer referenced
- The `lib\x64\` folder with the old DLLs can be removed if desired
- Runtime DLLs are now provided by NuGet packages in the `runtimes\win-x64\native\` folder
