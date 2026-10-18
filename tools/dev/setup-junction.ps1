<#
.SYNOPSIS
  Creates the git-ignored `mb2` junction at the repo root pointing to the Bannerlord game folder.
  Directory.Build.props uses it as GameFolder, so builds deploy to mb2\Modules\BannerCoopTW.
.EXAMPLE
  .\tools\dev\setup-junction.ps1 -GamePath "D:\Steam\steamapps\common\Mount & Blade II Bannerlord"
#>
param(
    [Parameter(Mandatory = $true)][string]$GamePath
)
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$link = Join-Path $repo 'mb2'

if (-not (Test-Path (Join-Path $GamePath 'bin\Win64_Shipping_Client\Bannerlord.exe'))) {
    throw "Bannerlord.exe not found under '$GamePath\bin\Win64_Shipping_Client'."
}

if (Test-Path $link) {
    $item = Get-Item $link -Force
    if ($item.LinkType -ne 'Junction') { throw "'$link' exists and is not a junction; remove it manually." }
    $current = @($item.Target)[0]
    if ($current -eq (Resolve-Path $GamePath).Path) { Write-Host "mb2 already points to $current"; return }
    Write-Host "Re-pointing mb2 (was $current)"
    $item.Delete()
}

New-Item -ItemType Junction -Path $link -Target (Resolve-Path $GamePath).Path | Out-Null
Write-Host "mb2 -> $((Get-Item $link -Force).Target)"
