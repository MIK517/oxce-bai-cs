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

$saved = Read-Source 'src/Savegame/SavedGame.cpp'
$region = Read-Source 'src/Savegame/Region.cpp'
$country = Read-Source 'src/Savegame/Country.cpp'
$mod = Read-Source 'src/Mod/Mod.cpp'
$mission = Read-Source 'src/Mod/RuleAlienMission.cpp'
$report = Read-Source 'src/Geoscape/MonthlyReportState.cpp'
$geoscape = Read-Source 'src/Geoscape/GeoscapeState.cpp'
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_monthly_report_probe.cpp'))
$template = $template.Replace('// SAVEDGAME_METHODS', (@('void SavedGame::addMonth()', 'void SavedGame::monthlyFunding()',
    'int SavedGame::getCountryFunding() const', 'int SavedGame::getBaseMaintenance() const',
    'int SavedGame::getDifficultyCoefficient() const', 'int64_t SavedGame::getFunds() const',
    'void SavedGame::setFunds(int64_t funds)') | ForEach-Object { Read-Method $saved $_ }) -join "`n")
$template = $template.Replace('// REGION_METHODS', (Read-Method $region 'void Region::newMonth()'))
$template = $template.Replace('// COUNTRY_METHODS', (@('Country::Satisfaction Country::getSatisfaction() const',
    'void Country::addActivityAlien(int activity)',
    'void Country::newMonth(int xcomTotal, int alienTotal, int pactScore, int averageFunding, const SavedGame* save)') |
    ForEach-Object { Read-Method $country $_ }) -join "`n")
$template = $template.Replace('// MISSION_METHODS', (@(
    (Read-Method $mission 'int RuleAlienMission::getWeight(const size_t monthsPassed) const'),
    (Read-Method $mod 'const RuleAlienMission *Mod::getRandomMission(MissionObjective objective, size_t monthsPassed) const')) -join "`n"))
# The constructor from calculateChanges() on, including its closing brace; the widget construction before it is skipped.
$template = $template.Replace('// REPORT_CONSTRUCTOR_TAIL', (Read-Between $report "calculateChanges();`n`n`tint month" 'MonthlyReportState::~MonthlyReportState()'))
$template = $template.Replace('// REPORT_METHODS', (@('void MonthlyReportState::btnOkClick(Action*)', 'void MonthlyReportState::calculateChanges()',
    'std::string MonthlyReportState::countryList(const std::vector<std::string>& countries, const std::string& singular, const std::string& plural)') |
    ForEach-Object { Read-Method $report $_ }) -join "`n")
$template = $template.Replace('// TIME1MONTH', (Read-Method $geoscape 'void GeoscapeState::time1Month()'))
if ($template -match '// (SAVEDGAME|REGION|COUNTRY|MISSION|REPORT|TIME1MONTH)[A-Z_]*') { throw 'Unreplaced probe marker.' }

$work = Join-Path $repository 'artifacts/reference-strategic-monthly-report'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_monthly_report_probe.cpp'
$exe = Join-Path $work 'strategic_monthly_report_probe.exe'
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
$output = Join-Path $work 'strategic-monthly-report.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
