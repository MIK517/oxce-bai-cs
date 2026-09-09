[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
function Read-ReferenceMethod([string]$RelativePath, [string]$Signature) {
    $source = [IO.File]::ReadAllText((Join-Path $reference $RelativePath))
    $start = $source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method $Signature" }
    $opening = $source.IndexOf('{', $start)
    $depth = 1
    $end = $opening + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced method $Signature" }
    return $source.Substring($start, $end - $start)
}
function Assert-ReferenceContains([string]$RelativePath, [string]$Text) {
    $source = [IO.File]::ReadAllText((Join-Path $reference $RelativePath))
    if (-not $source.Contains($Text, [StringComparison]::Ordinal)) { throw "Missing reference fragment in $RelativePath" }
}
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures\reference-probes\savegames\strategic_readiness_probe.cpp'))
$template = $template.Replace('// UNIT_STATS_COMBINE', (Read-ReferenceMethod 'src\Mod\Unit.h' 'static UnitStats combine('))
$template = $template.Replace('// WEAPON_REARM', (Read-ReferenceMethod 'src\Savegame\CraftWeapon.cpp' 'int CraftWeapon::rearm('))
$template = $template.Replace('// HEAL_WOUND', (Read-ReferenceMethod 'src\Savegame\Soldier.cpp' 'void Soldier::healWound('))
$template = $template.Replace('// REPLENISH_MANA', (Read-ReferenceMethod 'src\Savegame\Soldier.cpp' 'void Soldier::replenishMana('))
$template = $template.Replace('// REPLENISH_HEALTH', (Read-ReferenceMethod 'src\Savegame\Soldier.cpp' 'void Soldier::replenishHealth('))
$template = $template.Replace('// REPLENISH_STATS', (Read-ReferenceMethod 'src\Savegame\Soldier.cpp' 'void Soldier::replenishStats('))
$template = $template.Replace('// IS_FULLY_TRAINED', (Read-ReferenceMethod 'src\Savegame\Soldier.cpp' 'bool Soldier::isFullyTrained() const'))
Assert-ReferenceContains 'src\Basescape\PlaceFacilityState.cpp' 'refundValueTemp = checkFacilityTemp->getRules()->getRefundValue();'
Assert-ReferenceContains 'src\Basescape\PlaceFacilityState.cpp' '_game->getSavedGame()->getFunds() < (_rule->getBuildCost() - refundValueTemp)'
$work = Join-Path $repository 'artifacts\reference-strategic-readiness'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_readiness_probe.cpp'
$exe = Join-Path $work 'strategic_readiness_probe.exe'
[IO.File]::WriteAllText($probe, $template)
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$devCommand = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find Common7\Tools\VsDevCmd.bat
if (-not $devCommand) { throw 'Visual Studio C++ tools not found.' }
foreach ($value in @($devCommand, $probe, $exe, $work)) {
    if ($value -match '["&|<>^]') { throw 'Unsafe cmd.exe path.' }
}
$compile = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && cd /d "{1}" && cl.exe /nologo /std:c++20 /EHsc "{2}" /Fe:"{3}"' -f $devCommand, $work, $probe, $exe
& $env:ComSpec /d /c $compile
if ($LASTEXITCODE -ne 0) { throw 'Reference probe compilation failed.' }
$raw = & $exe
if ($LASTEXITCODE -ne 0) { throw 'Reference probe execution failed.' }
$output = Join-Path $work 'strategic-readiness.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
