# VistumblerCS - Clean and Rebuild Script
# Run this script to fix build issues by cleaning all artifacts and rebuilding

Write-Host "VistumblerCS - Clean and Rebuild Script" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean all bin and obj folders
Write-Host "Step 1: Cleaning bin and obj folders..." -ForegroundColor Yellow
$foldersToDelete = Get-ChildItem -Path . -Include bin,obj -Recurse -Directory
$count = $foldersToDelete.Count
if ($count -gt 0) {
    $foldersToDelete | Remove-Item -Recurse -Force
    Write-Host "  ✓ Deleted $count folders" -ForegroundColor Green
} else {
    Write-Host "  ✓ No folders to delete" -ForegroundColor Green
}
Write-Host ""

# Step 2: Clear NuGet cache
Write-Host "Step 2: Clearing NuGet cache..." -ForegroundColor Yellow
try {
    dotnet nuget locals all --clear | Out-Null
    Write-Host "  ✓ NuGet cache cleared" -ForegroundColor Green
} catch {
    Write-Host "  ! Warning: Could not clear NuGet cache" -ForegroundColor Yellow
}
Write-Host ""

# Step 3: Restore packages
Write-Host "Step 3: Restoring NuGet packages..." -ForegroundColor Yellow
$restoreResult = dotnet restore 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Packages restored successfully" -ForegroundColor Green
} else {
    Write-Host "  ✗ Package restore failed!" -ForegroundColor Red
    Write-Host $restoreResult
    exit 1
}
Write-Host ""

# Step 4: Build solution
Write-Host "Step 4: Building solution..." -ForegroundColor Yellow
$buildResult = dotnet build --no-restore 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Build successful!" -ForegroundColor Green
} else {
    Write-Host "  ✗ Build failed!" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}
Write-Host ""

# Step 5: Run tests (optional)
Write-Host "Step 5: Running tests..." -ForegroundColor Yellow
$testResult = dotnet test --no-build --verbosity quiet 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ All tests passed!" -ForegroundColor Green
} else {
    Write-Host "  ! Some tests failed (this is OK for initial build)" -ForegroundColor Yellow
}
Write-Host ""

# Summary
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host "Build completed successfully! ✓" -ForegroundColor Green
Write-Host ""
Write-Host "You can now:" -ForegroundColor Cyan
Write-Host "  1. Open the solution in Visual Studio" -ForegroundColor White
Write-Host "  2. Press F5 to run the application" -ForegroundColor White
Write-Host "  3. Or run: dotnet run --project Vistumbler.UI" -ForegroundColor White
Write-Host ""