[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
$source = [IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/AlienMission.cpp'))
$method = $source.IndexOf('MissionSite *AlienMission::spawnMissionSite(', [StringComparison]::Ordinal)
$logging = $source.IndexOf('if (Options::oxceGeoscapeDebugLogMaxEntries', $method, [StringComparison]::Ordinal)
$selection = $source.IndexOf('RuleRegion* regionRules = mod.getRegion(_region, true);', [StringComparison]::Ordinal)
$counter = $source.IndexOf('++_nextUfoCounter;', $selection, [StringComparison]::Ordinal)
if ($method -lt 0 -or $logging -le $method -or $selection -lt 0 -or $counter -le $selection) { throw 'Missing reference site-spawn fragments.' }
# Exclude only the debug logging tail; the return and null-result behavior remain explicit.
$spawn = $source.Substring($method, $logging - $method) + "return missionSite;`n}`nreturn 0;`n}`n"
# Remove the enclosing think branch's final brace; direct() owns that scope.
$direct = $source.Substring($selection, $counter - $selection).TrimEnd()
if (-not $direct.EndsWith('}')) { throw 'Unexpected direct selection ending.' }
$direct = $direct.Substring(0, $direct.Length - 1)
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_site_spawn_probe.cpp'))
$template = $template.Replace('// SPAWN_METHOD', $spawn).Replace('// DIRECT_SELECTION', $direct)
$work = Join-Path $repository 'artifacts/reference-strategic-site-spawn'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_site_spawn_probe.cpp'
$exe = Join-Path $work 'strategic_site_spawn_probe.exe'
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
$output = Join-Path $work 'strategic-site-spawn.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
