# Reads the app version from the project file and checks whether a GitLab release already exists for it.
# Writes release.env (a dotenv report) that the release build and publish jobs read:
#   RELEASE_VERSION, RELEASE_TAG, RELEASE_PRERELEASE, RELEASE_PUBLISHED
param(
    [Parameter(Mandatory)][string]$Project,   # .csproj holding the version
    [Parameter(Mandatory)][string]$Element    # version element, e.g. Version or ApplicationDisplayVersion
)

$ErrorActionPreference = 'Stop'

$m = [regex]::Match((Get-Content $Project -Raw), "<$Element>([^<]+)</$Element>")
if (-not $m.Success) { throw "<$Element> not found in $Project" }
$version = $m.Groups[1].Value.Trim()
$tag = "v$version"

$published = $false
try {
    Invoke-RestMethod -Uri "$env:CI_API_V4_URL/projects/$env:CI_PROJECT_ID/releases/$([uri]::EscapeDataString($tag))" `
        -Headers @{ 'JOB-TOKEN' = $env:CI_JOB_TOKEN } | Out-Null
    $published = $true
}
catch {
    if ([int]$_.Exception.Response.StatusCode -ne 404) { throw }
}

@(
    "RELEASE_VERSION=$version"
    "RELEASE_TAG=$tag"
    "RELEASE_PRERELEASE=$(([string]($version -match '-')).ToLower())"
    "RELEASE_PUBLISHED=$(([string]$published).ToLower())"
) | Set-Content release.env -Encoding ascii

if ($published) { Write-Host "Release $tag already exists; the release jobs will skip." }
else { Write-Host "Release $tag does not exist yet; building it." }
