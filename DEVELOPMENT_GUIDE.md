# Vistumbler CS - Development Guide

## Development Environment Setup

### Required Tools

1. **Visual Studio 2022** (Community Edition or higher)
   - Workloads: .NET desktop development, WPF
   - Extensions (recommended):
     - ReSharper or Visual Studio IntelliCode
     - GitHub Copilot (optional)

2. **Alternative IDEs**
   - JetBrains Rider
   - Visual Studio Code with C# extensions

3. **Additional Tools**
   - Git for version control
   - SQLite Browser for database inspection
   - Fiddler or Wireshark for network debugging

### SDK and Runtime

```bash
# Check .NET version
dotnet --version  # Should be 8.0 or higher

# Install .NET 8 SDK if needed
# Download from: https://dotnet.microsoft.com/download/dotnet/8.0
```

## Project Architecture

### Dependency Flow

```
Vistumbler.UI
    ↓ depends on
Vistumbler.Infrastructure
    ↓ depends on
Vistumbler.Core
```

**Core** has no dependencies on other layers.
**Infrastructure** implements interfaces from Core.
**UI** uses both Core and Infrastructure.

### MVVM Pattern

```
View (XAML) ←→ ViewModel ←→ Service ←→ Repository
```

- **View**: Pure XAML, no code-behind logic
- **ViewModel**: Presentation logic, commands, observables
- **Service**: Business logic
- **Repository**: Data access

## Adding New Features

### Example: Adding a New Export Format

1. **Define Interface** (Core layer)
```csharp
// Vistumbler.Core/Services/IExportService.cs
Task ExportToNewFormatAsync(string filePath, List<AccessPoint> accessPoints);
```

2. **Implement Service** (Infrastructure layer)
```csharp
// Vistumbler.Infrastructure/Export/NewFormatExporter.cs
public class NewFormatExporter
{
    public async Task ExportAsync(string filePath, List<AccessPoint> accessPoints)
    {
        // Implementation
    }
}
```

3. **Create ViewModel** (UI layer)
```csharp
// Vistumbler.UI/ViewModels/ExportViewModel.cs
[RelayCommand]
private async Task ExportToNewFormatAsync()
{
    await _exportService.ExportToNewFormatAsync(filePath, AccessPoints);
}
```

4. **Update View** (UI layer)
```xaml
<!-- Vistumbler.UI/Views/MainWindow.xaml -->
<MenuItem Header="Export to New Format" 
          Command="{Binding ExportToNewFormatCommand}"/>
```

5. **Register Service** (UI layer)
```csharp
// Vistumbler.UI/App.xaml.cs
services.AddSingleton<IExportService, ExportService>();
```

### Example: Adding a New Graph

1. **Create ViewModel**
```csharp
public class SignalGraphViewModel : ViewModelBase
{
    private readonly IWiFiScannerService _wifiScanner;
    
    [ObservableProperty]
    private ObservableCollection<DataPoint> _dataPoints = new();
}
```

2. **Create View**
```xaml
<UserControl x:Class="Vistumbler.UI.Views.SignalGraphView">
    <!-- Use LiveCharts or OxyPlot for graphing -->
    <lvc:CartesianChart Series="{Binding Series}"/>
</UserControl>
```

3. **Integrate into Main Window**
```xaml
<TabItem Header="Signal Graph">
    <views:SignalGraphView DataContext="{Binding SignalGraphViewModel}"/>
</TabItem>
```

## Database Schema Changes

### Adding a New Table

1. **Update Schema** (Infrastructure layer)
```csharp
// Vistumbler.Infrastructure/Data/SQLiteDatabaseService.cs
private async Task CreateTablesAsync()
{
    var sql = @"
        CREATE TABLE IF NOT EXISTS NewTable (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ...
        );
    ";
    await _connection.ExecuteAsync(sql);
}
```

2. **Create Model** (Core layer)
```csharp
// Vistumbler.Core/Models/NewModel.cs
public class NewModel
{
    public int Id { get; set; }
    // ... properties
}
```

3. **Add Repository Methods**
```csharp
Task<NewModel> GetNewModelAsync(int id);
Task<int> AddNewModelAsync(NewModel model);
```

### Migration Strategy

For schema changes in production:

1. Check current schema version
2. Apply incremental migrations
3. Update version number

```csharp
public class DatabaseMigration
{
    private const int CurrentVersion = 2;
    
    public async Task MigrateAsync(SQLiteConnection connection)
    {
        var version = await GetSchemaVersionAsync(connection);
        
        if (version < 2)
        {
            await MigrateToVersion2Async(connection);
        }
        
        await SetSchemaVersionAsync(connection, CurrentVersion);
    }
}
```

## Testing

### Unit Tests

```csharp
[Fact]
public async Task WiFiScanner_ShouldDetectAccessPoints()
{
    // Arrange
    var mockScanner = new Mock<IWiFiScannerService>();
    mockScanner.Setup(s => s.StartScanningAsync(It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
    
    // Act
    await mockScanner.Object.StartScanningAsync();
    
    // Assert
    mockScanner.Verify(s => s.StartScanningAsync(It.IsAny<CancellationToken>()), 
                      Times.Once);
}
```

### Integration Tests

```csharp
[Fact]
public async Task Database_ShouldStoreAndRetrieveAccessPoint()
{
    // Arrange
    var dbService = new SQLiteDatabaseService();
    await dbService.InitializeAsync(":memory:");
    
    var ap = new AccessPoint
    {
        Bssid = "00:11:22:33:44:55",
        Ssid = "TestAP"
    };
    
    // Act
    var id = await dbService.UpsertAccessPointAsync(ap);
    var retrieved = await dbService.GetAccessPointByBssidAsync(ap.Bssid);
    
    // Assert
    Assert.NotNull(retrieved);
    Assert.Equal(ap.Ssid, retrieved.Ssid);
}
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific test
dotnet test --filter "FullyQualifiedName~WiFiScannerTests"
```

## Debugging

### WiFi Scanning

Enable debug output:
```csharp
// In NativeWiFiScanner.cs
private void OnScanError(ScanErrorEventArgs e)
{
    Debug.WriteLine($"Scan Error: {e.ErrorMessage}");
    Debug.WriteLine($"Exception: {e.Exception}");
}
```

Check Windows Event Viewer for WLAN events.

### GPS

Test GPS data without hardware:
```csharp
// Create mock GPS service
public class MockGpsService : IGpsService
{
    public async Task StartAsync(GpsConfiguration config, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var gpsData = new GpsData
            {
                Latitude = 40.7128,  // New York
                Longitude = -74.0060,
                Timestamp = DateTime.Now
            };
            
            OnGpsDataReceived(new GpsDataReceivedEventArgs { GpsData = gpsData });
            await Task.Delay(1000, ct);
        }
    }
}
```

### Database

Inspect SQLite database:
```bash
# Using SQLite command line
sqlite3 vistumbler.db
.tables
.schema AccessPoints
SELECT * FROM AccessPoints LIMIT 10;
```

Or use DB Browser for SQLite (GUI tool).

## Performance Optimization

### UI Performance

1. **Virtualization**: DataGrid automatically virtualizes
2. **Batch Updates**: Use `ObservableCollection.Add()` sparingly
3. **Background Work**: Use async/await for I/O

```csharp
// Bad
foreach (var ap in accessPoints)
{
    AccessPoints.Add(ap);  // UI update for each
}

// Good
var newAps = accessPoints.Select(ap => new AccessPointViewModel(ap));
foreach (var ap in newAps)
{
    AccessPoints.Add(ap);
}
```

### Database Performance

1. **Indexes**: Already created on BSSID, ApId, Timestamp
2. **Batch Inserts**: Use transactions

```csharp
using var transaction = await _connection.BeginTransactionAsync();
try
{
    foreach (var ap in accessPoints)
    {
        await _connection.ExecuteAsync(insertSql, ap, transaction);
    }
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

3. **Prepared Statements**: Dapper handles this automatically

### Memory Management

1. **Dispose**: Implement IDisposable for services
2. **WeakReferences**: For cached data
3. **Clear Collections**: Remove old data periodically

## Code Style Guide

### Naming Conventions

```csharp
// Public properties: PascalCase
public string Bssid { get; set; }

// Private fields: _camelCase
private readonly IWiFiScannerService _wifiScanner;

// Methods: PascalCase
public async Task StartScanningAsync()

// Constants: PascalCase
private const int MaxRetries = 3;

// Locals: camelCase
var accessPoint = new AccessPoint();
```

### Comments

```csharp
// XML documentation for public APIs
/// <summary>
/// Scans for WiFi access points
/// </summary>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>List of discovered access points</returns>
public async Task<List<AccessPoint>> ScanAsync(CancellationToken cancellationToken)

// Inline comments for complex logic
// Calculate channel from frequency using 2.4GHz band formula
var channel = (frequency - 2412) / 5 + 1;
```

### Async/Await

```csharp
// Always use Async suffix
public async Task<List<AccessPoint>> GetAccessPointsAsync()

// ConfigureAwait(false) in library code
await Task.Delay(1000).ConfigureAwait(false);

// Use CancellationToken for long-running operations
public async Task ProcessAsync(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(1000, cancellationToken);
    }
}
```

## Common Issues and Solutions

### Issue: Native WiFi API not working

**Solution**: 
- Check if WLAN AutoConfig service is running
- Verify adapter supports Native WiFi
- Run as Administrator if needed

### Issue: GPS not connecting

**Solution**:
- Verify COM port number
- Check baud rate (usually 4800 or 9600)
- Test with Putty or similar terminal

### Issue: Database locked

**Solution**:
```csharp
// Use connection pooling
var connectionString = "Data Source=vistumbler.db;Version=3;Pooling=True;Max Pool Size=100";

// Use transactions properly
using var transaction = connection.BeginTransaction();
// ... work
await transaction.CommitAsync();
```

### Issue: UI freezing

**Solution**:
```csharp
// Run long operations on background thread
await Task.Run(async () =>
{
    // Long running work
    await ProcessDataAsync();
});

// Update UI on dispatcher
Application.Current.Dispatcher.Invoke(() =>
{
    StatusMessage = "Complete";
});
```

## Build and Deployment

### Release Build

```bash
# Build for release
dotnet build -c Release

# Publish self-contained
dotnet publish -c Release -r win-x64 --self-contained true

# Create single file
dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true
```

### Version Numbers

Update in project files:
```xml
<PropertyGroup>
    <Version>1.0.0</Version>
    <FileVersion>1.0.0.0</FileVersion>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
</PropertyGroup>
```

## Contributing Workflow

1. Fork the repository
2. Create feature branch: `git checkout -b feature/my-feature`
3. Make changes and commit: `git commit -am "Add new feature"`
4. Push to branch: `git push origin feature/my-feature`
5. Create Pull Request

### Commit Message Format

```
<type>: <subject>

<body>

<footer>
```

Types: feat, fix, docs, style, refactor, test, chore

Example:
```
feat: Add signal strength graph

- Implement real-time signal graphing
- Add graph settings to options
- Use LiveCharts for visualization

Closes #123
```

## Resources

- [.NET Documentation](https://docs.microsoft.com/dotnet/)
- [WPF Documentation](https://docs.microsoft.com/dotnet/desktop/wpf/)
- [Native WiFi API](https://docs.microsoft.com/windows/win32/nativewifi/native-wifi-portal)
- [SQLite Documentation](https://www.sqlite.org/docs.html)
- [NMEA Protocol](https://www.gpsinformation.org/dale/nmea.htm)
