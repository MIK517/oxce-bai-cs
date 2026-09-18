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
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures\reference-probes\savegames\strategic_world_probe.cpp'))
foreach ($entry in $methods.GetEnumerator()) {
    $marker = '// ' + $entry.Key
    if ($template.IndexOf($marker, [StringComparison]::Ordinal) -lt 0) { throw "Missing probe marker $marker" }
    $template = $template.Replace($marker, (Read-ReferenceMethod $entry.Value[0] $entry.Value[1]))
}
# Arithmetic the probe reproduces inline is pinned by its exact reference statement.
Assert-ReferenceContains 'src\Mod\UfoTrajectory.h' 'int applySpeedPercentage(size_t wp, int baseSpeed) const { return baseSpeed * _waypoints[wp].speed / 100; }'
Assert-ReferenceContains 'src\Savegame\AlienMission.cpp' '_spawnCountdown = (spawnTimer/2 + RNG::generate(0, spawnTimer)) * 30;'
Assert-ReferenceContains 'src\Savegame\AlienMission.cpp' '_spawnCountdown += 30 * (RNG::generate(0, 400) + 48);'
Assert-ReferenceContains 'src\Savegame\Base.cpp' 'detectionChance = radar_chance * (100 + target->getVisibility()) / 100;'
Assert-ReferenceContains 'src\Savegame\Craft.cpp' 'detectionChance = _stats.radarChance * (100 + target->getVisibility()) / 100;'
$work = Join-Path $repository 'artifacts\reference-strategic-world'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_world_probe.cpp'
$exe = Join-Path $work 'strategic_world_probe.exe'
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
$output = Join-Path $work 'strategic-world.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
