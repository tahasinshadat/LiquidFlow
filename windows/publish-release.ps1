<#
.SYNOPSIS
  Publishes a LiquidFlow release to GitHub (tahasinshadat/LiquidFlow): tags the commit,
  creates the release, and uploads every installer in windows\dist for that version.
  The in-app updater reads these releases, so publishing here ships the update to
  every install watching GitHub.

.PREREQS
  - GitHub CLI (winget install GitHub.cli), then one-time: gh auth login
  - Installers built: powershell windows\installer\build.ps1 -Arches arm64 -Version <ver>

.EXAMPLE
  powershell windows\publish-release.ps1 -Version 1.9.0
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Repo = "tahasinshadat/LiquidFlow"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot   # repo root
$dist = Join-Path $root "windows\dist"

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    $candidate = "$env:ProgramFiles\GitHub CLI\gh.exe"
    if (Test-Path $candidate) { $gh = @{ Source = $candidate } } else {
        throw "GitHub CLI not found. Install with: winget install GitHub.cli  — then run: gh auth login"
    }
}
$ghExe = $gh.Source

& $ghExe auth status 2>$null
if ($LASTEXITCODE -ne 0) { throw "Not signed in. Run once: gh auth login  (choose GitHub.com, SSH, browser login)" }

$assets = Get-ChildItem $dist -File | Where-Object { $_.Name -match [regex]::Escape($Version) }
if (-not $assets) { throw "No installers matching $Version in $dist — build first: windows\installer\build.ps1 -Version $Version" }
Write-Host "Assets:" ($assets.Name -join ", ")

$tag = "v$Version"
Set-Location $root
if (-not (git tag -l $tag)) {
    git tag $tag
    git push personal $tag
}

$notes = @"
LiquidFlow $Version

Installers below (Windows on ARM: -arm64). The app auto-updates from these releases.

LiquidFlow is a GPLv3 port based on altic-dev/FluidVoice; VoiceBox integration points
at the MIT-licensed github.com/jamiepine/voicebox.
"@

& $ghExe release create $tag ($assets | ForEach-Object { $_.FullName }) `
    --repo $Repo --title "LiquidFlow $Version" --notes $notes
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
Write-Host "Published: https://github.com/$Repo/releases/tag/$tag"
