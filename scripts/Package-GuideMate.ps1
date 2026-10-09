param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v?[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $projectRoot ('artifacts\release-stage-' + [guid]::NewGuid().ToString('N'))
$output = Join-Path $projectRoot 'artifacts\releases'
try {
    & (Join-Path $PSScriptRoot 'Publish-GuideMate.ps1') -OutputDirectory $stage -Version $Version
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $archive = Join-Path $output ("GuideMate-$Version-win-x64.zip")
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
    Write-Host ("Package: " + $archive)
} finally {
    $artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if (!$resolvedStage.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedStage) -notmatch '^release-stage-[0-9a-f]{32}$') {
        throw 'Refusing to clean a directory outside the generated release stage.'
    }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}
