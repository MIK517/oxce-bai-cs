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
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_hunting_probe.cpp'))
$refuelMethods = ''
foreach ($signature in @('void Craft::setFuel(int fuel)', 'std::string Craft::refuel()', 'void Craft::setDestination(Target *dest)', 'void Craft::repair()', 'const RuleItem* Craft::rearm()', 'int Craft::getFuelConsumption(int speed, int escortSpeed) const', 'void Craft::consumeFuel(int escortSpeed)', 'void Craft::returnToBase()')) {
    $refuelMethods += (Read-ReferenceMethod 'src/Savegame/Craft.cpp' $signature) + [Environment]::NewLine
}
$template = $template.Replace('// REFUEL_METHODS', $refuelMethods)
$halfHour = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::time30Minutes()'
$autoStart = $halfHour.IndexOf('if (xcraft->getStatus() == "STR_READY" && xcraft->getRules()->canAutoPatrol())', [StringComparison]::Ordinal)
if ($autoStart -lt 0) { throw 'Missing auto-patrol fragment.' }
$autoOpen = $halfHour.IndexOf('{', $autoStart)
$autoDepth = 1
$autoEnd = $autoOpen + 1
while ($autoDepth -gt 0 -and $autoEnd -lt $halfHour.Length) {
    if ($halfHour[$autoEnd] -eq '{') { $autoDepth++ }
    if ($halfHour[$autoEnd] -eq '}') { $autoDepth-- }
    $autoEnd++
}
if ($autoDepth -ne 0) { throw 'Unbalanced auto-patrol fragment.' }
$template = $template.Replace('// AUTO_PATROL', $halfHour.Substring($autoStart, $autoEnd - $autoStart))
$tenMinutes = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::time10Minutes()'
$fuelStart = $tenMinutes.IndexOf('int escortSpeed = 0;', [StringComparison]::Ordinal)
$fuelEnd = $tenMinutes.IndexOf('if (xcraft->getDestination() == 0', $fuelStart, [StringComparison]::Ordinal)
if ($fuelStart -lt 0 -or $fuelEnd -le $fuelStart) { throw 'Missing ordered craft fuel fragment.' }
$template = $template.Replace('// FUEL_CALLER', $tenMinutes.Substring($fuelStart, $fuelEnd - $fuelStart))
$template = $template.Replace('// IGNORED_METHOD', (Read-ReferenceMethod 'src/Savegame/Craft.cpp' 'bool Craft::isIgnoredByHK() const'))
$template = $template.Replace('// ATTRACTION', (Read-ReferenceMethod 'src/Savegame/Craft.cpp' 'int Craft::getHunterKillerAttraction(int huntMode) const'))
$constructor = Read-ReferenceMethod 'src/Savegame/Ufo.cpp' 'Ufo::Ufo(const RuleUfo *rules, int uniqueId, int hunterKillerPercentage, int huntMode, int huntBehavior)'
$flagStart = $constructor.IndexOf('if (hunterKillerPercentage > 0)', [StringComparison]::Ordinal)
if ($flagStart -lt 0) { throw 'Missing constructor hunter flags.' }
$template = $template.Replace('// CONSTRUCTOR_FLAGS', $constructor.Substring($flagStart, $constructor.LastIndexOf('}') - $flagStart))
$template = $template.Replace('// RADAR', (Read-ReferenceMethod 'src/Savegame/Ufo.cpp' 'bool Ufo::insideRadarRange(Target *target) const'))
$method = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::ufoHuntingAndEscorting()'
$start = $method.IndexOf('int newAttraction = INT_MAX;', [StringComparison]::Ordinal)
$end = $method.IndexOf('if (newTarget)', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Missing hunter selection fragment.' }
$template = $template.Replace('// SELECTION', $method.Substring($start, $end - $start))
$escortStart = $method.IndexOf('if (ufo->isEscort() && !ufo->isHunting() && !ufo->isEscorting())', [StringComparison]::Ordinal)
if ($escortStart -lt 0) { throw 'Missing retarget escort fragment.' }
$escortOpening = $method.IndexOf('{', $escortStart)
$depth = 1
$escortEnd = $escortOpening + 1
while ($depth -gt 0 -and $escortEnd -lt $method.Length) {
    if ($method[$escortEnd] -eq '{') { $depth++ }
    if ($method[$escortEnd] -eq '}') { $depth-- }
    $escortEnd++
}
if ($depth -ne 0) { throw 'Unbalanced retarget escort fragment.' }
$template = $template.Replace('// RETARGET_ESCORT', $method.Substring($escortStart, $escortEnd - $escortStart))

$retarget = ''
foreach ($signature in @('void Ufo::backupOriginalDestination()', 'void Ufo::setTargetedXcomCraft(', 'void Ufo::setEscortedUfo(', 'void Ufo::resetOriginalDestination(bool', 'void Ufo::setDestination(')) {
    $retarget += (Read-ReferenceMethod 'src/Savegame/Ufo.cpp' $signature) + [Environment]::NewLine
}
$template = $template.Replace('// RETARGET_METHODS', $retarget)
$spawn = Read-ReferenceMethod 'src/Savegame/AlienMission.cpp' 'Ufo *AlienMission::spawnUfo('
$escortStart = $spawn.LastIndexOf('if (ufo->isHunterKiller() && wave.escort)', [StringComparison]::Ordinal)
$escortEnd = $spawn.IndexOf('logUfo(ufo, game, this);', $escortStart, [StringComparison]::Ordinal)
if ($escortStart -lt 0 -or $escortEnd -le $escortStart) { throw 'Missing ordinary spawn escort fragment.' }
$template = $template.Replace('// SPAWN_ESCORT', $spawn.Substring($escortStart, $escortEnd - $escortStart))
$fiveSeconds = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::time5Seconds()'
$conditionStart = $fiveSeconds.IndexOf('if ((_timeSpeed', [StringComparison]::Ordinal)
$conditionEnd = $fiveSeconds.IndexOf('{', $conditionStart, [StringComparison]::Ordinal)
if ($conditionStart -lt 0 -or $conditionEnd -le $conditionStart) { throw 'Missing fast retarget condition.' }
$condition = $fiveSeconds.Substring($conditionStart + 3, $conditionEnd - $conditionStart - 3).Trim()
$template = $template.Replace('// FAST_CONDITION', $condition)
$work = Join-Path $repository 'artifacts/reference-strategic-hunting'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_hunting_probe.cpp'
$exe = Join-Path $work 'strategic_hunting_probe.exe'
[IO.File]::WriteAllText($probe, $template)
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
$devCommand = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find Common7\Tools\VsDevCmd.bat
if (-not $devCommand) { throw 'Visual Studio C++ tools not found.' }
foreach ($value in @($devCommand, $probe, $exe, $work)) { if ($value -match '["&|<>^]') { throw 'Unsafe cmd.exe path.' } }
$compile = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && cd /d "{1}" && cl.exe /nologo /std:c++20 /EHsc "{2}" /Fe:"{3}"' -f $devCommand, $work, $probe, $exe
& $env:ComSpec /d /c $compile
if ($LASTEXITCODE -ne 0) { throw 'Reference probe compilation failed.' }
$raw = & $exe
if ($LASTEXITCODE -ne 0) { throw 'Reference probe execution failed.' }
$output = Join-Path $work 'strategic-hunting.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
