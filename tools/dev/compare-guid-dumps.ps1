<#
.SYNOPSIS
  Compares two guid-dump-<pid>.tsv files (S1 spike): per type, counts, identical (stringId -> guid),
  same stringId with different guid ("order/subId mismatch"), and stringIds present on one side only.
.EXAMPLE
  .\compare-guid-dumps.ps1 guid-dump-100.tsv guid-dump-200.tsv -Examples 5
#>
param(
    [Parameter(Mandatory)][string]$A,
    [Parameter(Mandatory)][string]$B,
    [int]$Examples = 5
)
$ErrorActionPreference = 'Stop'

function Read-Dump([string]$path) {
    $map = @{}
    foreach ($line in [System.IO.File]::ReadLines($path) | Select-Object -Skip 1) {
        $f = $line -split "`t"
        if ($f.Count -lt 5) { continue }
        $type = $f[0]
        if (-not $map.ContainsKey($type)) { $map[$type] = @{} }
        $map[$type][$f[2]] = [pscustomobject]@{ Index = [int]$f[1]; Guid = [uint32]$f[3] }
    }
    $map
}

$da = Read-Dump $A
$db = Read-Dump $B
$totalBad = 0
foreach ($type in (@($da.Keys) + @($db.Keys) | Sort-Object -Unique)) {
    $ma = $da[$type]; $mb = $db[$type]
    if (-not $ma) { $ma = @{} }
    if (-not $mb) { $mb = @{} }
    $same = 0; $diff = @(); $onlyA = @(); $onlyB = @()
    foreach ($id in $ma.Keys) {
        if (-not $mb.ContainsKey($id)) { $onlyA += $id }
        elseif ($ma[$id].Guid -eq $mb[$id].Guid) { $same++ }
        else { $diff += $id }
    }
    foreach ($id in $mb.Keys) { if (-not $ma.ContainsKey($id)) { $onlyB += $id } }
    $bad = $diff.Count + $onlyA.Count + $onlyB.Count
    $totalBad += $bad
    '{0,-16} A={1,6} B={2,6} identical={3,6} guidDiffers={4,5} onlyA={5,5} onlyB={6,5}' -f $type, $ma.Count, $mb.Count, $same, $diff.Count, $onlyA.Count, $onlyB.Count
    foreach ($id in $diff | Select-Object -First $Examples) {
        '    guidDiffers  {0}: A={1} (idx {2})  B={3} (idx {4})' -f $id, $ma[$id].Guid, $ma[$id].Index, $mb[$id].Guid, $mb[$id].Index
    }
    foreach ($id in $onlyA | Select-Object -First $Examples) { "    onlyA  $id" }
    foreach ($id in $onlyB | Select-Object -First $Examples) { "    onlyB  $id" }
}
if ($totalBad -eq 0) { 'RESULT: parity (all types identical)'; exit 0 } else { "RESULT: $totalBad mismatching entries"; exit 1 }
