[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
$source = [IO.File]::ReadAllText((Join-Path $reference 'src/Geoscape/GeoscapeState.cpp'))
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_alien_base_activity_probe.cpp'))
$start = $source.IndexOf('// handle regional and country points for alien bases', [StringComparison]::Ordinal)
$end = $source.IndexOf('// Handle resupply of alien bases.', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Missing base activity block.' }
$template = $template.Replace('// BASE_ACTIVITY', $source.Substring($start,$end-$start))
$start = $source.IndexOf('// Handle alien base detection (by xcom base facilities).', [StringComparison]::Ordinal)
$end = $source.IndexOf('// clear the daily dogfight experience cache', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Missing facility detection block.' }
$template = $template.Replace('// FACILITY_DETECTION', $source.Substring($start,$end-$start))
function StatementBlock([string]$Text,[string]$Match) {
 $start=$Text.IndexOf($Match,[StringComparison]::Ordinal)
 if($start -lt 0){throw "Missing statement $Match"}
 $open=$Text.IndexOf('{',$start);$depth=1;$end=$open+1
 while($depth -gt 0 -and $end -lt $Text.Length){if($Text[$end] -eq '{'){$depth++};if($Text[$end] -eq '}'){$depth--};$end++}
 if($depth -ne 0){throw 'Unbalanced source block.'}
 return $Text.Substring($start,$end-$start)
}
$template=$template.Replace('// CRAFT_DISCOVERY',(StatementBlock $source 'if (xcraft->getDestination() == 0 && xcraft->getCraftStats().sightRange > 0)'))
$math=[IO.File]::ReadAllText((Join-Path $reference 'src/fmath.h'))
$template=$template.Replace('// XCOM_DISTANCE',(StatementBlock $math 'inline int XcomDistance(double nautical)'))
$weighted=[IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/WeightedOptions.cpp'))
$template=$template.Replace('// WEIGHT_SET',(StatementBlock $weighted 'void WeightedOptions::set('))
$template=$template.Replace('// WEIGHT_LOAD',(StatementBlock $weighted 'void WeightedOptions::load('))
$work=Join-Path $repository 'artifacts/reference-strategic-alien-base-activity'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe=Join-Path $work 'strategic_alien_base_activity_probe.cpp'
$exe=Join-Path $work 'strategic_alien_base_activity_probe.exe'
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
$output=Join-Path $work 'strategic-alien-base-activity.actual.json'
[IO.File]::WriteAllText($output,($raw -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
Write-Output $output
