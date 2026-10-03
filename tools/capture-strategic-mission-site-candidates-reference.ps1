[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
$geoscape=[IO.File]::ReadAllText((Join-Path $reference 'src/Geoscape/GeoscapeState.cpp'))
$template=[IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_mission_site_candidates_probe.cpp'))
function StatementBlock([string]$Text,[string]$Match) {
 $start=$Text.IndexOf($Match,[StringComparison]::Ordinal)
 if($start -lt 0){throw "Missing statement $Match"}
 $open=$Text.IndexOf('{',$start);$depth=1;$end=$open+1
 while($depth -gt 0 -and $end -lt $Text.Length){if($Text[$end] -eq '{'){$depth++};if($Text[$end] -eq '}'){$depth--};$end++}
 if($depth -ne 0){throw 'Unbalanced source block.'}
 return $Text.Substring($start,$end-$start)
}
$template=$template.Replace('// SITE_COMMAND',(StatementBlock $geoscape 'if (command->getSiteType())'))
$siteStart=$geoscape.IndexOf('if (command->getSiteType())',[StringComparison]::Ordinal)
$summaryEnd=$geoscape.IndexOf('End of the summary',$siteStart,[StringComparison]::Ordinal)
$siteTail=$geoscape.Substring($summaryEnd)
$template=$template.Replace('// CANDIDATES',(StatementBlock $siteTail 'for (auto regionNameIt = regions.begin();'))
$work=Join-Path $repository 'artifacts/reference-strategic-mission-site-candidates'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe=Join-Path $work 'strategic_mission_site_candidates_probe.cpp'
$exe=Join-Path $work 'strategic_mission_site_candidates_probe.exe'
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
$output=Join-Path $work 'strategic-mission-site-candidates.actual.json'
[IO.File]::WriteAllText($output,($raw -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
Write-Output $output
