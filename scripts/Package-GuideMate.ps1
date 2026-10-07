param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v?[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $projectRoot ('artifacts\release-stage-' + [guid]::NewGuid().ToString('N'))
$output = Join-Path $projectRoot 'artifacts\releases'
& (Join-Path $PSScriptRoot 'Publish-GuideMate.ps1') -OutputDirectory $stage
New-Item -ItemType Directory -Path $output -Force | Out-Null
$archive = Join-Path $output ("GuideMate-$Version-win-x64.zip")
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archive + '.sha256', "$checksum  $([IO.Path]::GetFileName($archive))" + [Environment]::NewLine, [Text.Encoding]::ASCII)
Write-Host ("Package: " + $archive)
Write-Host ("SHA256: " + $checksum)
