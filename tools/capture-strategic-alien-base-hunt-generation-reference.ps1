[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
$geoscape=[IO.File]::ReadAllText((Join-Path $reference 'src/Geoscape/GeoscapeState.cpp'))
$mission=[IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/AlienMission.cpp'))
$rule=[IO.File]::ReadAllText((Join-Path $reference 'src/Mod/RuleAlienMission.cpp'))
$template=[IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_alien_base_hunt_generation_probe.cpp'))
function StatementBlock([string]$Text,[string]$Match) {
 $start=$Text.IndexOf($Match,[StringComparison]::Ordinal)
 if($start -lt 0){throw "Missing statement $Match"}
 $open=$Text.IndexOf('{',$start);$depth=1;$end=$open+1
 while($depth -gt 0 -and $end -lt $Text.Length){if($Text[$end] -eq '{'){$depth++};if($Text[$end] -eq '}'){$depth--};$end++}
 if($depth -ne 0){throw 'Unbalanced source block.'}
 return $Text.Substring($start,$end-$start)
}
$weighted=[IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/WeightedOptions.cpp'))
$craft=[IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/Craft.cpp'))
$deployment=[IO.File]::ReadAllText((Join-Path $reference 'src/Mod/AlienDeployment.cpp'))
$template=$template.Replace('// CHOOSE',(StatementBlock $weighted 'std::string WeightedOptions::choose() const'))
$template=$template.Replace('// SET',(StatementBlock $weighted 'void WeightedOptions::set('))
$template=$template.Replace('// DESTROYED',(StatementBlock $craft 'bool Craft::isDestroyed() const'))
$template=$template.Replace('// IGNORED',(StatementBlock $craft 'bool Craft::isIgnoredByHK() const'))
$template=$template.Replace('// HUNT_MISSION',(StatementBlock $deployment 'std::string AlienDeployment::generateHuntMission('))
$template=$template.Replace('// ACTIVE',(StatementBlock $geoscape 'const std::vector<Craft*>* GeoscapeState::updateActiveCrafts()'))
$template=$template.Replace('// HUNTING',(StatementBlock $geoscape 'void GeoscapeState::baseHunting()'))
$start=$mission.IndexOf('_nextWave = 0;', $mission.IndexOf('void AlienMission::start('))
$end=$mission.IndexOf('// Earth-based alien operations', $start)
if($start -lt 0 -or $end -le $start){throw 'Missing start timer.'}
$template=$template.Replace('// START_TIMER',$mission.Substring($start,$end-$start))
$start=$mission.IndexOf('if (_nextWave >= _rule.getWaveCount())', $mission.IndexOf('void AlienMission::think('))
$end=$mission.IndexOf('const MissionWave &wave',$start)
if($start -lt 0 -or $end -le $start){throw 'Missing think countdown.'}
$template=$template.Replace('// THINK_COUNTDOWN',$mission.Substring($start,$end-$start))
$think=StatementBlock $mission 'void AlienMission::think('
$counterStart=$think.IndexOf('++_nextUfoCounter;')
$counterEnd=$think.IndexOf('if (_rule.getObjective() == OBJECTIVE_INFILTRATION')
$timerStart=$think.LastIndexOf('if (_nextWave != _rule.getWaveCount())')
if($counterStart -lt 0 -or $counterEnd -le $counterStart -or $timerStart -lt 0){throw 'Missing no-object counters.'}
$template=$template.Replace('// THINK_COUNTERS',$think.Substring($counterStart,$counterEnd-$counterStart))
$template=$template.Replace('// THINK_NEXT_TIMER',$think.Substring($timerStart).TrimEnd().TrimEnd('}'))
$work=Join-Path $repository 'artifacts/reference-strategic-alien-base-hunt-generation'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe=Join-Path $work 'strategic_alien_base_hunt_generation_probe.cpp'
$exe=Join-Path $work 'strategic_alien_base_hunt_generation_probe.exe'
[IO.File]::WriteAllText($probe,$template)
$vswhere=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
$devCommand=& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find Common7\Tools\VsDevCmd.bat
if(-not $devCommand){throw 'Visual Studio C++ tools not found.'}
foreach($value in @($devCommand,$probe,$exe,$work)){if($value -match '["&|<>^]'){throw 'Unsafe cmd.exe path.'}}
$compile='call "{0}" -no_logo -arch=x64 -host_arch=x64 && cd /d "{1}" && cl.exe /nologo /std:c++20 /EHsc "{2}" /Fe:"{3}"' -f $devCommand,$work,$probe,$exe
& $env:ComSpec /d /c $compile
if($LASTEXITCODE -ne 0){throw 'Reference probe compilation failed.'}
$raw=& $exe
if($LASTEXITCODE -ne 0){throw 'Reference probe execution failed.'}
$output=Join-Path $work 'strategic-alien-base-hunt-generation.actual.json'
[IO.File]::WriteAllText($output,($raw -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
Write-Output $output
