# Builds a Windows installer (NSIS, build\installer.nsi) for a published, self-contained app folder.
# Used by both the GitLab pipeline and the GitHub release workflow.
#
# Example:
#   ./build/Build-Installer.ps1 -SourceDir publish/x64 -AppName VistumblerCS -AppExe Vistumbler.UI.exe `
#       -Version 0.4.6 -Arch x64 -OutFile release-files/VistumblerCS-v0.4.6-win-x64-setup.exe
param(
    [Parameter(Mandatory)][string]$SourceDir,   # published app folder to install
    [Parameter(Mandatory)][string]$AppName,     # product name: install folder, shortcuts, Apps & Features
    [Parameter(Mandatory)][string]$AppExe,      # main executable, relative to SourceDir
    [Parameter(Mandatory)][string]$Version,     # e.g. 0.4.6 or 0.5.0-rc.1
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Arch,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$Icon,                              # optional .ico for the installer
    [string]$Publisher = 'TechIdiots LLC'
)

$ErrorActionPreference = 'Stop'

$source = (Resolve-Path $SourceDir).Path.TrimEnd('\', '/')
if (-not (Test-Path (Join-Path $source $AppExe))) { throw "$AppExe not found in $source" }

$makensis = (Get-Command makensis.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $makensis) {
    $makensis = (Get-Item 'C:\Program Files (x86)\NSIS\makensis.exe', 'C:\Program Files\NSIS\makensis.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1).FullName
}
if (-not $makensis) { throw 'makensis.exe not found. Install NSIS (https://nsis.sourceforge.io).' }

# File properties need a four-part number: 0.5.0-rc.1 -> 0.5.0.0
$parts = @(($Version -split '-')[0] -split '\.' | ForEach-Object { [int]$_ })
while ($parts.Count -lt 4) { $parts += 0 }
$numeric = ($parts[0..3] -join '.')

# Generate the install and uninstall lists from the published folder, so the uninstaller removes exactly
# what was installed. In install-time paths NSIS treats $ as a variable prefix, so a literal $ is written as
# $$; File's source path is read at build time and taken literally.
function Get-NsisText([string]$Text) { $Text.Replace('$', '$$') }
$dirs = @(Get-ChildItem -LiteralPath $source -Recurse -Directory | Sort-Object FullName)
$install = @('!macro InstallFiles')
$uninstall = @('!macro UninstallFiles')
foreach ($dir in @($source) + $dirs.FullName) {
    $rel = $dir.Substring($source.Length).TrimStart('\')
    $target = if ($rel) { "`$INSTDIR\$(Get-NsisText $rel)" } else { '$INSTDIR' }
    $files = @(Get-ChildItem -LiteralPath $dir -File | Sort-Object Name)
    if (-not $files) { continue }
    $install += "  SetOutPath `"$target`""
    foreach ($file in $files) {
        $install += "  File `"$($file.FullName)`""
        $uninstall += "  Delete `"$target\$(Get-NsisText $file.Name)`""
    }
}
# Remove now-empty folders, deepest first; RMDir without /r leaves any folder that still has other files
foreach ($dir in $dirs | Sort-Object { $_.FullName.Length } -Descending) {
    $uninstall += "  RMDir `"`$INSTDIR\$(Get-NsisText $dir.FullName.Substring($source.Length).TrimStart('\'))`""
}
$install += '!macroend'
$uninstall += '!macroend'
$fileList = Join-Path ([IO.Path]::GetTempPath()) ("installer-files-{0}.nsh" -f [guid]::NewGuid())
[IO.File]::WriteAllLines($fileList, [string[]]($install + $uninstall), [Text.UTF8Encoding]::new($true))

# Resolve against the PowerShell location, not the process directory (they differ under the GitLab runner)
$out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

$defines = @(
    "/DAPP_NAME=$AppName"
    "/DAPP_EXE=$AppExe"
    "/DVERSION=$Version"
    "/DVERSION_NUMERIC=$numeric"
    "/DARCH=$Arch"
    "/DPUBLISHER=$Publisher"
    "/DFILE_LIST=$fileList"
    "/DOUTFILE=$out"
)
if ($Icon) { $defines += "/DAPP_ICON=$((Resolve-Path $Icon).Path)" }

Write-Host "Building $AppName $Version ($Arch) installer from $source ($(@(Get-ChildItem -LiteralPath $source -Recurse -File).Count) files)"
try {
    & $makensis /V2 @defines (Join-Path $PSScriptRoot 'installer.nsi')
    if ($LASTEXITCODE -ne 0) { throw "makensis failed (exit $LASTEXITCODE)" }
}
finally {
    Remove-Item $fileList -Force -ErrorAction SilentlyContinue
}
Write-Host "Installer: $out"
