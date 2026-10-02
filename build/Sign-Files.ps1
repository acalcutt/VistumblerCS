# Code-signs files with the certificate in the SIG_PFX_B64 / SIG_PFX_PASS CI/CD variables.
# Skips with a warning when SIG_PFX_B64 is not available (e.g. on an unprotected branch).
param(
    [Parameter(Mandatory)][string[]]$Path
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:SIG_PFX_B64)) {
    Write-Warning 'SIG_PFX_B64 is not set; skipping code signing.'
    return
}

$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
if (-not $signtool) { throw 'signtool.exe not found. Install the Windows SDK signing tools.' }
Write-Host "Using $($signtool.FullName)"

$pfxPath = Join-Path ([IO.Path]::GetTempPath()) ("sign-{0}.pfx" -f [guid]::NewGuid())
try {
    [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($env:SIG_PFX_B64))
    foreach ($file in $Path) {
        Write-Host "Signing $file"
        # The timestamp server occasionally fails transiently, so retry a few times
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            & $signtool.FullName sign /f $pfxPath /p $env:SIG_PFX_PASS /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $file
            if ($LASTEXITCODE -eq 0) { break }
            if ($attempt -eq 3) { throw "signtool failed for $file (exit $LASTEXITCODE)" }
            Write-Warning "signtool attempt $attempt failed; retrying in 10 seconds"
            Start-Sleep -Seconds 10
        }
    }
}
finally {
    Remove-Item $pfxPath -Force -ErrorAction SilentlyContinue
}
