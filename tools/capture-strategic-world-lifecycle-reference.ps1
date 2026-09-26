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
    # Windows PowerShell 5.1 has no String.Contains(String, StringComparison) overload.
    if ($source.IndexOf($Text, [StringComparison]::Ordinal) -lt 0) { throw "Missing reference fragment in $RelativePath" }
}
$methods = [ordered]@{
    'FMATH_ARE_SAME'                       = @('src\fmath.h', 'inline bool AreSame(double l, double r)')
    'FMATH_DEG2RAD'                        = @('src\fmath.h', 'inline double Deg2Rad(double deg)')
    'FMATH_NAUTICAL'                       = @('src\fmath.h', 'inline double Nautical(double x)')
    'FMATH_XCOM_DISTANCE'                  = @('src\fmath.h', 'inline int XcomDistance(double nautical)')
    'TARGET_SET_LONGITUDE'                 = @('src\Savegame\Target.cpp', 'void Target::setLongitude(double lon)')
    'TARGET_SET_LATITUDE'                  = @('src\Savegame\Target.cpp', 'void Target::setLatitude(double lat)')
    'TARGET_GET_DISTANCE'                  = @('src\Savegame\Target.cpp', 'double Target::getDistance(double lon, double lat) const')
    'MOVING_TARGET_CALCULATE_RADIAN_SPEED' = @('src\Savegame\MovingTarget.cpp', 'double MovingTarget::calculateRadianSpeed(int speed)')
    'MOVING_TARGET_SET_SPEED'              = @('src\Savegame\MovingTarget.cpp', 'void MovingTarget::setSpeed(int speed)')
    'MOVING_TARGET_CALCULATE_SPEED'        = @('src\Savegame\MovingTarget.cpp', 'void MovingTarget::calculateSpeed()')
    'MOVING_TARGET_CALCULATE_MEET_POINT'   = @('src\Savegame\MovingTarget.cpp', 'void MovingTarget::calculateMeetPoint()')
    'MOVING_TARGET_REACHED_DESTINATION'    = @('src\Savegame\MovingTarget.cpp', 'bool MovingTarget::reachedDestination() const')
    'MOVING_TARGET_MOVE'                   = @('src\Savegame\MovingTarget.cpp', 'void MovingTarget::move()')
    'RULE_REGION_INSIDE_REGION'            = @('src\Mod\RuleRegion.cpp', 'bool RuleRegion::insideRegion(double lon, double lat, bool ignoreTechnicalRegion) const')
    'RULE_REGION_GET_RANDOM_POINT'         = @('src\Mod\RuleRegion.cpp', 'std::pair<double, double> RuleRegion::getRandomPoint(size_t zone, int area) const')
    'UFO_CALCULATE_SPEED'                  = @('src\Savegame\Ufo.cpp', 'void Ufo::calculateSpeed()')
    'UFO_GET_VISIBILITY'                   = @('src\Savegame\Ufo.cpp', 'int Ufo::getVisibility() const')
    'UFO_GET_ALTITUDE_INT'                 = @('src\Savegame\Ufo.cpp', 'int Ufo::getAltitudeInt() const')
    'BASE_GET_DETECTION_CHANCE'            = @('src\Savegame\Base.cpp', 'size_t Base::getDetectionChance() const')
    'CRAFT_RECALC_SPEED_MAX_RADIAN'        = @('src\Savegame\Craft.cpp', 'void Craft::recalcSpeedMaxRadian()')
    'CRAFT_GET_FUEL_CONSUMPTION'           = @('src\Savegame\Craft.cpp', 'int Craft::getFuelConsumption(int speed, int escortSpeed) const')
    'CRAFT_GET_FUEL_LIMIT'                 = @('src\Savegame\Craft.cpp', 'int Craft::getFuelLimit(Base *base) const')
    'CRAFT_GET_BASE_RANGE'                 = @('src\Savegame\Craft.cpp', 'double Craft::getBaseRange() const')
    'WEIGHTED_OPTIONS_CHOOSE'              = @('src\Savegame\WeightedOptions.cpp', 'std::string WeightedOptions::choose() const')
    'WEIGHTED_OPTIONS_SET'                 = @('src\Savegame\WeightedOptions.cpp', 'void WeightedOptions::set(const std::string &id, size_t weight)')
    'ALIEN_STRATEGY_ADD_MISSION_RUN'       = @('src\Savegame\AlienStrategy.cpp', 'void AlienStrategy::addMissionRun(const std::string &varName, int increment)')
    'ALIEN_STRATEGY_ADD_MISSION_LOCATION'  = @('src\Savegame\AlienStrategy.cpp', 'void AlienStrategy::addMissionLocation(const std::string &varName, const std::string &regionName, int zoneNumber, int maximum)')
    'ALIEN_STRATEGY_VALID_MISSION_LOCATION'= @('src\Savegame\AlienStrategy.cpp', 'bool AlienStrategy::validMissionLocation(const std::string &varName, const std::string &regionName, int zoneNumber)')
}

# Geometry collaborators reuse the independently verified arithmetic probe.
$geometry = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_world_probe.cpp'))
$geometry = $geometry.Substring(0, $geometry.IndexOf('struct RuleUfoStats'))
foreach ($entry in $methods.GetEnumerator()) {
    $marker = '// ' + $entry.Key
    if ($geometry.Contains($marker)) { $geometry = $geometry.Replace($marker, (Read-ReferenceMethod $entry.Value[0] $entry.Value[1])) }
}
$geometry = $geometry.Replace('void calculateSpeed();', 'virtual void calculateSpeed();')
$geometry = $geometry.Replace('int intValue = 0;', 'int calls = 0; int intValue = 0;')
$geometry = $geometry.Replace('return std::min(max, min + intValue);', 'assert(min <= max); ++calls; return std::min(max, min + intValue);')
$geometry = $geometry.Replace('return min + fraction * (max - min);', 'assert(min <= max); ++calls; return min + fraction * (max - min);')
$geometry = $geometry.Replace('std::vector<MissionZone> _missionZones;', 'std::vector<MissionZone> _missionZones; const std::vector<MissionZone>& getMissionZones() const { return _missionZones; } std::string getType() const { return "REGION"; }')
$collaborators = 'namespace RNG { bool percent(int chance) { return chance > 0; } size_t generate(int minimum, size_t maximum) { assert(minimum >= 0 && (size_t)minimum <= maximum); ++calls; return minimum; } }' + "`n#define LOG_WARNING 0`n#define Log(level) std::cerr`n" + 'using Exception = std::runtime_error; namespace OpenXcom {'
$geometry = $geometry.Replace('namespace OpenXcom {', $collaborators)
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_world_lifecycle_probe.cpp'))
$template = $template.Replace('// GEOMETRY', $geometry)
$direct = [ordered]@{
 'UFO_THINK' = @('src/Savegame/Ufo.cpp', 'void Ufo::think()')
 'UFO_ALTITUDE' = @('src/Savegame/Ufo.cpp', 'void Ufo::setAltitude(const std::string &altitude)')
 'UFO_CRASHED' = @('src/Savegame/Ufo.cpp', 'bool Ufo::isCrashed() const')
 'UFO_DESTROYED' = @('src/Savegame/Ufo.cpp', 'bool Ufo::isDestroyed() const')
 'UFO_SPEED' = @('src/Savegame/Ufo.cpp', 'void Ufo::calculateSpeed()')
 'MISSION_OVER' = @('src/Savegame/AlienMission.cpp', 'bool AlienMission::isOver() const')
 'MISSION_LIFT' = @('src/Savegame/AlienMission.cpp', 'void AlienMission::ufoLifting(Ufo &ufo, SavedGame &game)')
 'MISSION_WAYPOINT' = @('src/Savegame/AlienMission.cpp', 'std::pair<double, double> AlienMission::getWaypoint(')
 'MISSION_LAND' = @('src/Savegame/AlienMission.cpp', 'std::pair<double, double> AlienMission::getLandPoint(')
}
foreach ($entry in $direct.GetEnumerator()) { $template = $template.Replace('// ' + $entry.Key, (Read-ReferenceMethod $entry.Value[0] $entry.Value[1])) }
function Replace-UnsupportedBody([string]$Source, [string]$Condition) {
    $start = $Source.IndexOf($Condition, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing excluded branch: $Condition" }
    $open = $Source.IndexOf('{', $start)
    $depth = 1; $end = $open + 1
    while ($depth -gt 0) { if ($Source[$end] -eq '{') {$depth++}; if ($Source[$end] -eq '}') {$depth--}; $end++ }
    return $Source.Substring(0,$open) + '{ throw std::runtime_error("Unsupported probe branch"); }' + $Source.Substring($end)
}
$arrival = Read-ReferenceMethod 'src/Savegame/AlienMission.cpp' 'void AlienMission::ufoReachedWaypoint('
$arrival = Replace-UnsupportedBody $arrival 'if (Options::aggressiveRetaliation'
$arrival = $arrival.Replace('Options::aggressiveRetaliation', 'false')
$arrival = Replace-UnsupportedBody $arrival 'if (_missionSiteZoneArea != -1 && wave.objective'
$arrival = Replace-UnsupportedBody $arrival 'else if (trajectory.getID() == UfoTrajectory::RETALIATION_ASSAULT_RUN)'
$arrival = Replace-UnsupportedBody $arrival 'if (_rule.getObjective() == OBJECTIVE_BASE && wave.objectiveOnTheLandingSite'
$template = $template.Replace('// MISSION_ARRIVAL', $arrival)
$spawn = Read-ReferenceMethod 'src/Savegame/AlienMission.cpp' 'Ufo *AlienMission::spawnUfo('
$start = $spawn.LastIndexOf('Ufo *ufo = new Ufo(ufoRule, game.getId("STR_UFO_UNIQUE"), hunterKillerPercentage, huntMode, huntBehavior);')
$end = $spawn.IndexOf('// Only hunter-killers can escort', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Ordinary spawn fragment missing.' }
$spawn = 'Ufo* AlienMission::spawnUfo(SavedGame& game,const Mod& mod,const Globe& globe,const MissionWave& wave,const UfoTrajectory& trajectory) { const RuleUfo* ufoRule=&mod.ufo; int hunterKillerPercentage=0,huntMode=0,huntBehavior=0;' + $spawn.Substring($start,$end-$start) + 'return ufo;}'
$template = $template.Replace('// MISSION_SPAWN', $spawn)
$think = Read-ReferenceMethod 'src/Savegame/AlienMission.cpp' 'void AlienMission::think('
$prefixEnd = $think.IndexOf('else if ((mod.getDeployment')
$countersStart = $think.IndexOf('++_nextUfoCounter;')
$countersEnd = $think.IndexOf('if (_rule.getObjective() == OBJECTIVE_INFILTRATION')
$timerStart = $think.LastIndexOf('if (_nextWave != _rule.getWaveCount())')
if ($prefixEnd -lt 0 -or $countersStart -lt 0 -or $countersEnd -lt 0 -or $timerStart -lt 0) {throw 'Mission countdown fragments missing.'}
$think = $think.Substring(0,$prefixEnd) + $think.Substring($countersStart,$countersEnd-$countersStart) + $think.Substring($timerStart)
$template = $template.Replace('// MISSION_THINK', $think)
Assert-ReferenceContains 'src/Geoscape/GeoscapeState.cpp' 'if (ufo->getStatus() == Ufo::DESTROYED)'
Assert-ReferenceContains 'src/Geoscape/GeoscapeState.cpp' 'mission->ufoLifting(*ufo, *_game->getSavedGame());'
Assert-ReferenceContains 'src/Savegame/Ufo.cpp' '_mission->decreaseLiveUfos();'
Assert-ReferenceContains 'src/Savegame/AlienMission.h' 'void decreaseLiveUfos() { --_liveUfos; }'
$work = Join-Path $repository 'artifacts/reference-strategic-world-lifecycle'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_world_lifecycle_probe.cpp'
$exe = Join-Path $work 'strategic_world_lifecycle_probe.exe'
[IO.File]::WriteAllText($probe, $template)
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
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
$output = Join-Path $work 'strategic-world-lifecycle.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
