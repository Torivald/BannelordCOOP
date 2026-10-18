<#
.SYNOPSIS
  Starts Bannerlord directly (no launcher) with an explicit module list: native modules,
  Bannerlord.Harmony and BannerCoopTW. Other installed mods (e.g. Diplomacy) are not loaded.
.PARAMETER Instances
  Number of game instances to start (20 s apart). Two instances on one PC work (research 0001).
#>
param(
    [int]$Instances = 1
)
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$bin = Join-Path $repo 'mb2\bin\Win64_Shipping_Client'
if (-not (Test-Path (Join-Path $bin 'Bannerlord.exe'))) { throw "mb2 junction missing or invalid ($bin)." }

$modules = '_MODULES_*Bannerlord.Harmony*Native*SandBoxCore*Sandbox*CustomBattle*StoryMode*BannerCoopTW*_MODULES_'
for ($i = 0; $i -lt $Instances; $i++) {
    if ($i -gt 0) { Start-Sleep -Seconds 20 }
    $p = Start-Process -FilePath (Join-Path $bin 'Bannerlord.exe') -ArgumentList "/singleplayer $modules" -WorkingDirectory $bin -PassThru
    Write-Host "Started Bannerlord PID $($p.Id) -> log: C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_$($p.Id).txt"
}
Write-Host "Mod log: $([Environment]::GetFolderPath('MyDocuments'))\Mount and Blade II Bannerlord\Logs\BannerCoopTW\"
