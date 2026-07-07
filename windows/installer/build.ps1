<#
.SYNOPSIS
  Publishes FluidVoice (ARM64 primary, x64 optional) and builds installers with Inno Setup.
.EXAMPLE
  pwsh windows/installer/build.ps1                 # arm64 only
  pwsh windows/installer/build.ps1 -Arches arm64,x64
#>
param(
    [string[]]$Arches = @("arm64"),
    [string]$Version = "1.6.2",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # repo root
$proj = Join-Path $root "windows\src\FluidVoice.App\FluidVoice.App.csproj"
$iss = Join-Path $root "windows\installer\FluidVoice.iss"
$dist = Join-Path $root "windows\dist"
New-Item -ItemType Directory -Force $dist | Out-Null

# locate dotnet + ISCC
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { $dotnet = Join-Path $env:USERPROFILE "dotnet\dotnet.exe" }
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe (Inno Setup) not found. Install from https://jrsoftware.org/isdl.php" }

foreach ($arch in $Arches) {
    $rid = "win-$arch"
    $pub = Join-Path $root "windows\publish\$arch"
    Write-Host "==> Publishing $rid ..." -ForegroundColor Cyan
    & $dotnet publish $proj -c $Configuration -r $rid --self-contained true `
        -p:PublishSingleFile=false -o $pub
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }

    Write-Host "==> Building installer for $arch ..." -ForegroundColor Cyan
    & $iscc "/DArch=$arch" "/DSourceDir=$pub" "/DAppVersion=$Version" $iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed for $arch" }

    # also produce a portable zip
    $zip = Join-Path $dist "FluidVoice-portable-$Version-$arch.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path "$pub\*" -DestinationPath $zip
    Write-Host "==> Portable zip: $zip" -ForegroundColor Green
}

Write-Host "`nArtifacts in $dist :" -ForegroundColor Green
Get-ChildItem $dist | Format-Table Name, @{N="MB";E={[math]::Round($_.Length/1MB,1)}}
