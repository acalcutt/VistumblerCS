@echo off
REM VistumblerCS - Clean and Rebuild Script (Batch version)
echo.
echo VistumblerCS - Clean and Rebuild Script
echo =======================================
echo.

REM Step 1: Clean build artifacts
echo Step 1: Cleaning build artifacts...
dotnet clean
echo   Done!
echo.

REM Step 2: Restore packages
echo Step 2: Restoring NuGet packages...
dotnet restore
if errorlevel 1 (
    echo   ERROR: Package restore failed!
    pause
    exit /b 1
)
echo   Done!
echo.

REM Step 3: Build solution
echo Step 3: Building solution...
dotnet build
if errorlevel 1 (
    echo   ERROR: Build failed!
    pause
    exit /b 1
)
echo   Done!
echo.

REM Summary
echo =======================================
echo Build completed successfully!
echo.
echo You can now:
echo   1. Open VistumblerCS.sln in Visual Studio
echo   2. Press F5 to run the application
echo   3. Or run: dotnet run --project Vistumbler.UI
echo.
pause
