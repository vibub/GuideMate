# Console regressions only; no production app or browser profile is opened.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $projectRoot
try {
    & $sdkCommand build src/GuideMate.App/GuideMate.App.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
    & $sdkCommand build src/GuideMate.ChromeHost/GuideMate.ChromeHost.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Chrome host build failed.' }
    & $sdkCommand run --project tests/GuideMate.Specs/GuideMate.Specs.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core regression checks failed.' }
    node tests/chrome.spec.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Chrome extension checks failed.' }
    node tests/bridge-subtitles.spec.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Subtitle bridge checks failed.' }
    node tests/bridge-danmaku.spec.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Danmaku bridge checks failed.' }
    & $sdkCommand run --project tests/GuideMate.Vision.Specs/GuideMate.Vision.Specs.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Vision regression checks failed.' }
} finally {
    Pop-Location
}
