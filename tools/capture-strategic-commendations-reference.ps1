[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
function Read-Source([string]$RelativePath) { return [IO.File]::ReadAllText((Join-Path $reference $RelativePath)) }
function Read-Method([string]$Source, [string]$Signature) {
    $start = $Source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method $Signature" }
    $opening = $Source.IndexOf('{', $start)
    $depth = 1; $end = $opening + 1
    while ($depth -gt 0 -and $end -lt $Source.Length) {
        if ($Source[$end] -eq '{') { $depth++ }
        if ($Source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced method $Signature" }
    return $Source.Substring($start, $end - $start)
}
function Read-Between([string]$Source, [string]$From, [string]$To) {
    $start = $Source.IndexOf($From, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing block start $From" }
    $end = $Source.IndexOf($To, $start + $From.Length, [StringComparison]::Ordinal)
    if ($end -lt 0) { throw "Missing block end $To" }
    return $Source.Substring($start, $end - $start)
}

$diary = Read-Source 'src/Savegame/SoldierDiary.cpp'
$statistics = Read-Source 'src/Savegame/MissionStatistics.h'
$kills = Read-Source 'src/Savegame/BattleUnitStatistics.h'
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_commendations_probe.cpp'))
$template = $template.Replace('// MISSION_STATISTICS_METHODS', (@('bool isDarkness(const Mod* mod) const', 'bool isAlienBase() const',
    'bool isBaseDefense() const', 'bool isUfoMission() const') | ForEach-Object { Read-Method $statistics $_ }) -join "`n")
$template = $template.Replace('// BATTLE_UNIT_KILLS_METHODS', (@('bool hostileTurn() const', 'std::string getUnitStatusString() const',
    'std::string getUnitFactionString() const', 'std::string getUnitSideString() const', 'std::string getUnitBodyPartString() const') |
    ForEach-Object { Read-Method $kills $_ }) -join "`n")
$template = $template.Replace('// SOLDIER_COMMENDATIONS_METHODS', (@(
    'SoldierCommendations::SoldierCommendations(const std::string& commendationName, const std::string& noun, const Mod* mod)',
    'const std::string& SoldierCommendations::getType() const', 'const std::string& SoldierCommendations::getNoun() const',
    'int SoldierCommendations::getDecorationLevelInt() const', 'void SoldierCommendations::addDecoration()') |
    ForEach-Object { Read-Method $diary $_ }) -join "`n")
$template = $template.Replace('// SOLDIER_DIARY_METHODS', (@(
    'bool SoldierDiary::manageCommendations(const Mod* mod, SavedGame* save, const Soldier* soldier)',
    'std::map<std::string, int> SoldierDiary::getAlienRankTotal() const', 'std::map<std::string, int> SoldierDiary::getAlienRaceTotal() const',
    'std::map<std::string, int> SoldierDiary::getWeaponTotal() const',
    'std::map<std::string, int> SoldierDiary::getRegionTotal(std::vector<MissionStatistics*> *missionStatistics) const',
    'int SoldierDiary::getKillTotal() const',
    'int SoldierDiary::getMissionTotalFiltered(std::vector<MissionStatistics*>* missionStatistics, const RuleCommendations* rule) const',
    'int SoldierDiary::getWinTotal(std::vector<MissionStatistics*> *missionStatistics) const', 'int SoldierDiary::getStunTotal() const',
    'void SoldierDiary::addMonthlyService()', 'int SoldierDiary::getTrapKillTotal(const Mod* mod) const',
    'int SoldierDiary::getReactionFireKillTotal(const Mod* mod) const',
    'int SoldierDiary::getTerrorMissionTotal(std::vector<MissionStatistics*> *missionStatistics) const',
    'int SoldierDiary::getNightMissionTotal(std::vector<MissionStatistics*> *missionStatistics, const Mod* mod) const',
    'int SoldierDiary::getNightTerrorMissionTotal(std::vector<MissionStatistics*> *missionStatistics, const Mod* mod) const',
    'int SoldierDiary::getBaseDefenseMissionTotal(std::vector<MissionStatistics*> *missionStatistics) const',
    'int SoldierDiary::getAlienBaseAssaultTotal(std::vector<MissionStatistics*> *missionStatistics) const',
    'int SoldierDiary::getImportantMissionTotal(std::vector<MissionStatistics*> *missionStatistics) const',
    'int SoldierDiary::getScoreTotal(std::vector<MissionStatistics*> *missionStatistics) const',
    'int SoldierDiary::getValiantCruxTotal(std::vector<MissionStatistics*> *missionStatistics) const') |
    ForEach-Object { Read-Method $diary $_ }) -join "`n")
if ($template -match '// (MISSION_STATISTICS|BATTLE_UNIT_KILLS|SOLDIER_COMMENDATIONS|SOLDIER_DIARY)_METHODS') { throw 'Unreplaced probe marker.' }

$work = Join-Path $repository 'artifacts/reference-strategic-commendations'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_commendations_probe.cpp'
$exe = Join-Path $work 'strategic_commendations_probe.exe'
[IO.File]::WriteAllText($probe, $template)
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
$devCommand = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find Common7\Tools\VsDevCmd.bat
if (-not $devCommand) { throw 'Visual Studio C++ tools not found.' }
foreach ($value in @($devCommand, $probe, $exe, $work)) {
    if ($value -match '["&|<>^]') { throw 'Unsafe cmd.exe path.' }
}
$compile = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && cd /d "{1}" && cl.exe /nologo /std:c++20 /EHsc /W3 "{2}" /Fe:"{3}"' -f $devCommand, $work, $probe, $exe
& $env:ComSpec /d /c $compile
if ($LASTEXITCODE -ne 0) { throw 'Reference probe compilation failed.' }
$raw = & $exe
if ($LASTEXITCODE -ne 0) { throw 'Reference probe execution failed.' }
$output = Join-Path $work 'strategic-commendations.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
