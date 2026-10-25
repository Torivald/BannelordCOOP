<#
.SYNOPSIS
  Starts Bannerlord directly (no launcher) with an explicit module list: native modules,
  Bannerlord.Harmony and BannerCoopTW. Other installed mods (e.g. Diplomacy) are not loaded.
.PARAMETER Instances
  Number of game instances to start (20 s apart). Two instances on one PC work (research 0001).
#>
param(
    [int]$Instances = 1,
    # Adds the stock Multiplayer module (S2 experiment: its OnSubModuleLoad initialises GameNetwork and NetworkMain.SetPeers).
    [switch]$WithMultiplayer,
    # Positive control for option B: stock multiplayer client, no mods (Native + Multiplayer only), lobby login through Steam.
    [switch]$StockMultiplayer
)
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$bin = Join-Path $repo 'mb2\bin\Win64_Shipping_Client'
if (-not (Test-Path (Join-Path $bin 'Bannerlord.exe'))) { throw "mb2 junction missing or invalid ($bin)." }

$mp = if ($WithMultiplayer) { 'Multiplayer*' } else { '' }
$modules = "_MODULES_*Bannerlord.Harmony*Native*${mp}SandBoxCore*Sandbox*CustomBattle*StoryMode*BannerCoopTW*_MODULES_"
$args2 = "/singleplayer $modules"
if ($StockMultiplayer) { $args2 = '/multiplayer _MODULES_*Native*Multiplayer*_MODULES_' }
for ($i = 0; $i -lt $Instances; $i++) {
    if ($i -gt 0) { Start-Sleep -Seconds 20 }
    $p = Start-Process -FilePath (Join-Path $bin 'Bannerlord.exe') -ArgumentList $args2 -WorkingDirectory $bin -PassThru
    Write-Host "Started Bannerlord PID $($p.Id) -> log: C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_$($p.Id).txt"
}
Write-Host "Mod log: $([Environment]::GetFolderPath('MyDocuments'))\Mount and Blade II Bannerlord\Logs\BannerCoopTW\"
