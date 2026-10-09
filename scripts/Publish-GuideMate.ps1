param([string]$OutputDirectory, [string]$Version)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$publishDirectory = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts\GuideMate-win-x64' }
$publishedExe = Join-Path $publishDirectory 'GuideMate.exe'
if (Get-Process GuideMate -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $publishedExe }) {
    throw 'Please exit GuideMate normally before publishing so it can save settings. User data will not be modified.'
}
$publishArguments = @(
    '-p:PublishSingleFile=false'
    '-p:UseAppHost=true'
    '-p:DebugType=None'
    '-p:PublishDocumentationFiles=false'
)
if ($Version) { $publishArguments += ('-p:Version=' + $Version.TrimStart('v')) }
& $sdkCommand publish (Join-Path $projectRoot 'src\GuideMate.App\GuideMate.App.csproj') -c Release -r win-x64 --self-contained false @publishArguments -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'GuideMate publish failed.' }
$hostDirectory = Join-Path $publishDirectory 'chrome-host'
& $sdkCommand publish (Join-Path $projectRoot 'src\GuideMate.ChromeHost\GuideMate.ChromeHost.csproj') -c Release -r win-x64 --self-contained false -p:PublishTrimmed=false @publishArguments -o $hostDirectory
if ($LASTEXITCODE -ne 0) { throw 'GuideMate Chrome host publish failed.' }
$updaterDirectory = Join-Path $publishDirectory 'updater'
& $sdkCommand publish (Join-Path $projectRoot 'src\GuideMate.Updater\GuideMate.Updater.csproj') -c Release -r win-x64 --self-contained false @publishArguments -o $updaterDirectory
if ($LASTEXITCODE -ne 0) { throw 'GuideMate updater publish failed.' }
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
foreach ($name in @('USER_GUIDE.md', 'CHROME_SYNC.md', 'GENSHIN.md', 'ENDFIELD.md', 'IMPLEMENTATION.md', 'TESTING.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "docs\$name") -Destination $documentDirectory -Force
}
$assemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishDirectory 'GuideMate.dll')).ProductVersion.Split('+')[0]
$programFolders = @('assets', 'chrome-host', 'chrome-extension', 'licenses', 'scripts', 'docs', 'updater', 'runtimes', 'zh-Hans', 'zh-Hant', 'en')
$programDocuments = @('Start-GuideMate.cmd', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CONTRIBUTING.md', 'SECURITY.md')
$files = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('\', '/')
    $parts = $relative.Split('/')
    if ($parts | Where-Object { $_ -in @('settings.json', 'active-profile.json', 'WebView2', 'vision', 'genshin-preview') -or $_ -like '*.WebView2' }) { return }
    if ($parts.Length -gt 1) {
        if ($parts[0] -in $programFolders) { $relative }
    } elseif ($relative -in $programDocuments -or $_.Extension -eq '.dll' -or $relative -eq 'GuideMate.exe' -or ($relative -like 'GuideMate*.json' -and $relative -ne 'update-manifest.json')) { $relative }
}
$manifest = @{ Version = $assemblyVersion; Files = @($files | Sort-Object) } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $publishDirectory 'update-manifest.json'), $manifest, [Text.UTF8Encoding]::new($false))
