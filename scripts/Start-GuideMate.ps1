param([switch]$Build)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
# A release ZIP has the executable at its root; source checkouts use artifacts.
$releaseExe = Join-Path $projectRoot 'GuideMate.exe'
$publishedExe = Join-Path $projectRoot 'artifacts\GuideMate-win-x64\GuideMate.exe'
$readyExe = if (Test-Path -LiteralPath $releaseExe) { $releaseExe } else { $publishedExe }
if ((Test-Path -LiteralPath $readyExe) -and -not $Build) {
    Start-Process -FilePath $readyExe -WorkingDirectory (Split-Path $readyExe) -WindowStyle Hidden
    exit
}
if (Test-Path -LiteralPath $releaseExe) {
    throw 'A release package contains no source project. Build from a source checkout instead.'
}
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
& $sdkCommand build (Join-Path $projectRoot 'src\GuideMate.App\GuideMate.App.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'GuideMate build failed. Install .NET 10 SDK (Windows x64).' }
$builtExe = Join-Path $projectRoot 'src\GuideMate.App\bin\Release\net10.0-windows10.0.17763.0\GuideMate.exe'
Start-Process -FilePath $builtExe -WorkingDirectory (Split-Path $builtExe) -WindowStyle Hidden
