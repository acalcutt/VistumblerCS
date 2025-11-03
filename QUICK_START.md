# VistumblerCS - Quick Start Guide

## Getting Started in 5 Minutes

### Prerequisites
1. Install Visual Studio 2022 (Community Edition is free)
   - Download: https://visualstudio.microsoft.com/downloads/
   - Select ".NET desktop development" workload during installation

2. Install .NET 8 SDK
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Or it's included with Visual Studio 2022

### Opening the Project

1. **Open Visual Studio 2022**
2. **Click**: File → Open → Project/Solution
3. **Navigate to**: VistumblerCS folder
4. **Select**: VistumblerCS.sln
5. **Click**: Open

### Building the Project

**Option 1: Using Visual Studio**
- Press `F6` or click Build → Build Solution
- Wait for NuGet packages to restore (first time only)

**Option 2: Using Command Line**
```bash
cd VistumblerCS
dotnet restore
dotnet build
```

### Running the Application

**Option 1: Debug Mode (for development)**
- Press `F5` or click Debug → Start Debugging
- Application will launch with debugger attached

**Option 2: Without Debugger**
- Press `Ctrl+F5` or click Debug → Start Without Debugging
- Faster startup, no debugging overhead

**Option 3: Command Line**
```bash
cd Vistumbler.UI
dotnet run
```

## First Use

### 1. Start WiFi Scanning
- Click the **"Scan"** button in the toolbar
- Or use menu: Extra → Scan APs
- Access points will appear in the list as discovered

### 2. Connect GPS (Optional)
- Connect your GPS receiver to a COM port
- Click **"GPS"** button in the toolbar
- Or use menu: Extra → Use GPS
- Current location will show in the toolbar

### 3. View Access Point Details
- Click any access point in the list
- Details will appear in the right panel
- Signal strength, encryption, manufacturer, etc.

## Common First-Time Issues

### Issue: Build Errors
**Solution**: 
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

### Issue: "ManagedNativeWifi" not found
**Solution**: 
- Right-click solution → Restore NuGet Packages
- Or: `dotnet restore`

### Issue: WiFi not scanning
**Solution**:
- Check if WLAN AutoConfig service is running
- Open Services (services.msc)
- Find "WLAN AutoConfig"
- Ensure it's running

### Issue: GPS not connecting
**Solution**:
- Check COM port number in Device Manager
- Try different baud rates (4800, 9600)
- Verify GPS is powered on

## Project Structure Overview

```
VistumblerCS/
├── VistumblerCS.sln              ← Open this in Visual Studio
├── README.md                     ← Full documentation
├── DEVELOPMENT_GUIDE.md          ← For developers
├── PROJECT_SUMMARY.md            ← Complete overview
│
├── Vistumbler.Core/              ← Domain models & interfaces
├── Vistumbler.Infrastructure/    ← Implementations
├── Vistumbler.UI/                ← WPF User Interface ⭐ START HERE
└── Vistumbler.Tests/             ← Unit tests
```

## Making Your First Change

### Example: Change the Window Title

1. **Open**: Vistumbler.UI/Views/MainWindow.xaml
2. **Find**: `Title="Vistumbler CS - WiFi Scanner"`
3. **Change to**: `Title="My WiFi Scanner"`
4. **Press**: F5 to run
5. **See**: New title in window

### Example: Change Scan Button Color

1. **Open**: Vistumbler.UI/Views/MainWindow.xaml
2. **Find**: `<Button Content="Scan"`
3. **Add**: `Background="LightGreen"`
4. **Run**: Press F5

## Key Files to Know

### For UI Changes
- `Vistumbler.UI/Views/MainWindow.xaml` - Main window layout
- `Vistumbler.UI/ViewModels/MainViewModel.cs` - Main logic
- `Vistumbler.UI/App.xaml` - App-level resources and styles

### For WiFi Scanning
- `Vistumbler.Infrastructure/WiFi/NativeWiFiScanner.cs`
- `Vistumbler.Core/Services/IWiFiScannerService.cs`

### For GPS
- `Vistumbler.Infrastructure/Gps/SerialGpsService.cs`
- `Vistumbler.Core/Services/IGpsService.cs`

### For Database
- `Vistumbler.Infrastructure/Data/SQLiteDatabaseService.cs`
- `Vistumbler.Core/Services/IDatabaseService.cs`

## Useful Keyboard Shortcuts in Visual Studio

| Shortcut | Action |
|----------|--------|
| F5 | Start debugging |
| Ctrl+F5 | Run without debugging |
| F6 | Build solution |
| Ctrl+Shift+B | Build solution |
| F9 | Toggle breakpoint |
| F10 | Step over |
| F11 | Step into |
| Ctrl+K, Ctrl+D | Format document |
| Ctrl+. | Quick actions |
| F12 | Go to definition |

## Testing the Application

### Run Unit Tests
```bash
cd Vistumbler.Tests
dotnet test
```

Or in Visual Studio:
- Test → Run All Tests
- Or press `Ctrl+R, A`

## Next Steps

1. **Read**: README.md for full feature list
2. **Explore**: DEVELOPMENT_GUIDE.md for advanced topics
3. **Review**: PROJECT_SUMMARY.md for architecture details
4. **Contribute**: See DEVELOPMENT_GUIDE.md for contribution guidelines

## Getting Help

- **Documentation**: See README.md and DEVELOPMENT_GUIDE.md
- **Code Comments**: Most files have detailed XML documentation
- **IntelliSense**: Hover over any method/class for documentation
- **Original Vistumbler**: http://www.vistumbler.net

## Quick Reference - Command Line

```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Build in Release mode
dotnet build -c Release

# Run application
cd Vistumbler.UI
dotnet run

# Run tests
dotnet test

# Clean build artifacts
dotnet clean

# Create release package
dotnet publish -c Release -r win-x64
```

## Database Location

The SQLite database is created at:
```
%USERPROFILE%\Documents\Vistumbler\vistumbler.db
```

You can inspect it with:
- DB Browser for SQLite: https://sqlitebrowser.org/
- Or any SQLite viewer

## Project Status

✅ **Working**: WiFi scanning, GPS, Database, UI, Export
⏳ **In Progress**: Graphing, Advanced filtering, Settings dialog
📋 **Planned**: WiFiDB integration, Multi-language, Themes

## Support

This is an alpha release. If you encounter issues:
1. Check this guide
2. Check README.md
3. Review code comments
4. Open GitHub issue

## Success Checklist

- [ ] Visual Studio 2022 installed
- [ ] .NET 8 SDK installed
- [ ] Solution opens without errors
- [ ] Solution builds successfully
- [ ] Application runs
- [ ] WiFi scan works
- [ ] Access points appear in list
- [ ] GPS connects (if you have hardware)

If all items are checked, you're ready to develop!

## Tips for Success

1. **Start Small**: Make small changes and test frequently
2. **Use Breakpoints**: Debug issues by stepping through code
3. **Read Comments**: Code is heavily documented
4. **Follow Patterns**: Look at existing code for examples
5. **Ask Questions**: Code is designed to be understandable

## Additional Resources

- **.NET Documentation**: https://docs.microsoft.com/dotnet/
- **WPF Tutorial**: https://docs.microsoft.com/dotnet/desktop/wpf/
- **C# Guide**: https://docs.microsoft.com/dotnet/csharp/
- **MVVM Pattern**: https://docs.microsoft.com/archive/msdn-magazine/2009/february/patterns-wpf-apps-with-the-model-view-viewmodel-design-pattern

---

**Welcome to VistumblerCS!** 🎉

You're now ready to explore, modify, and enhance the WiFi scanner. Happy coding!
