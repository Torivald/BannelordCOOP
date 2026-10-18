<#
.SYNOPSIS
  Builds the solution; Bannerlord.BuildResources copies the module into mb2\Modules\BannerCoopTW.
  Run setup-junction.ps1 once first.
#>
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$module = Join-Path $repo 'mb2\Modules\BannerCoopTW'

if (-not (Test-Path (Join-Path $repo 'mb2\bin'))) {
    throw "mb2 junction missing. Run tools\dev\setup-junction.ps1 -GamePath <game folder> first."
}

dotnet build (Join-Path $repo 'BannerCoopTW.sln') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

foreach ($f in @('SubModule.xml', 'bin\Win64_Shipping_Client\BannerCoopTW.dll', 'bin\Win64_Shipping_Client\BannerCoopTW.Core.dll')) {
    if (-not (Test-Path (Join-Path $module $f))) { throw "Deploy incomplete: $f missing in $module" }
}
if (Select-String -Path (Join-Path $module 'SubModule.xml') -Pattern '\$\w+\$' -Quiet) {
    throw 'SubModule.xml still contains unreplaced $placeholders$.'
}
Write-Host "Deployed to $module"
