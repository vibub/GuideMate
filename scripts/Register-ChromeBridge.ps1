param([switch]$Unregister)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$registryPath = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.guidemate.bilibili'
if ($Unregister) {
    if (Test-Path -LiteralPath $registryPath) { Remove-Item -LiteralPath $registryPath }
    Write-Host 'GuideMate Chrome bridge registration removed. No cookies or browser data were changed.'
    exit
}
$runtime = if (Test-Path -LiteralPath (Join-Path $root 'GuideMate.exe')) { $root } else { Join-Path $root 'artifacts\GuideMate-win-x64' }
$hostExe = Join-Path $runtime 'chrome-host\GuideMate.ChromeHost.exe'
if (!(Test-Path -LiteralPath $hostExe)) { throw 'Publish GuideMate before registering the Chrome bridge.' }
$manifestPath = Join-Path (Split-Path $hostExe) 'com.guidemate.bilibili.json'
$manifest = [ordered]@{
    name = 'com.guidemate.bilibili'
    description = 'GuideMate Bilibili session bridge'
    path = $hostExe
    type = 'stdio'
    allowed_origins = @('chrome-extension://emeedledfchopaemhkhjfpbppffbjhid/')
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
New-Item -Path $registryPath -Force | Out-Null
Set-Item -LiteralPath $registryPath -Value $manifestPath
Write-Host 'GuideMate Bilibili native bridge registered for the current Windows user.'
Write-Host ('Chrome extension folder: ' + (Join-Path $runtime 'chrome-extension'))
