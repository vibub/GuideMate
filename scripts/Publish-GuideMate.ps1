param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$publishDirectory = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts\GuideMate-win-x64' }
$publishedExe = Join-Path $publishDirectory 'GuideMate.exe'
if (Get-Process GuideMate -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $publishedExe }) {
    throw 'Please exit GuideMate normally before publishing so it can save settings. User data will not be modified.'
}
$singleFileArguments = @(
    '-p:PublishSingleFile=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-p:EnableCompressionInSingleFile=false'
    '-p:DebugType=None'
    '-p:PublishDocumentationFiles=false'
)
& $sdkCommand publish (Join-Path $projectRoot 'src\GuideMate.App\GuideMate.App.csproj') -c Release -r win-x64 --self-contained true @singleFileArguments -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'GuideMate publish failed.' }
$hostDirectory = Join-Path $publishDirectory 'chrome-host'
& $sdkCommand publish (Join-Path $projectRoot 'src\GuideMate.ChromeHost\GuideMate.ChromeHost.csproj') -c Release -r win-x64 --self-contained true -p:PublishTrimmed=true @singleFileArguments -o $hostDirectory
if ($LASTEXITCODE -ne 0) { throw 'GuideMate Chrome host publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'chrome-extension') -Destination $publishDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $publishDirectory -Recurse -Force
$scriptDirectory = Join-Path $publishDirectory 'scripts'
New-Item -ItemType Directory -Path $scriptDirectory -Force | Out-Null
foreach ($name in @('Register-ChromeBridge.ps1', 'Start-GuideMate.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $scriptDirectory -Force
}
foreach ($name in @('Start-GuideMate.cmd', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CONTRIBUTING.md', 'SECURITY.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $publishDirectory -Force
}
$documentDirectory = Join-Path $publishDirectory 'docs'
New-Item -ItemType Directory -Path $documentDirectory -Force | Out-Null
foreach ($name in @('CHROME_SYNC.md', 'GENSHIN.md', 'ENDFIELD.md', 'IMPLEMENTATION.md', 'TESTING.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "docs\$name") -Destination $documentDirectory -Force
}
