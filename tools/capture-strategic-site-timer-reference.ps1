[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
$source = [IO.File]::ReadAllText((Join-Path $reference 'src/Geoscape/GeoscapeState.cpp'))
$method = $source.IndexOf('bool GeoscapeState::processMissionSite(', [StringComparison]::Ordinal)
$start = $source.IndexOf('bool removeSite = site->getSecondsRemaining() < 30 * 60;', $method, [StringComparison]::Ordinal)
$end = $source.IndexOf('if (removeSite)', $start, [StringComparison]::Ordinal)
$score = $source.IndexOf('int score = removeSite ?', $end, [StringComparison]::Ordinal)
$scoreEnd = $source.IndexOf(';', $score) + 1
if ($method -lt 0 -or $start -lt 0 -or $end -lt 0 -or $score -lt 0 -or $scoreEnd -le $score) { throw 'Missing reference site-timer statements.' }
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_site_timer_probe.cpp'))
$template = $template.Replace('// TIMER_AND_FOLLOWERS', $source.Substring($start, $end - $start))
$template = $template.Replace('// SCORE', $source.Substring($score, $scoreEnd - $score))
$saveSource = [IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/SavedGame.cpp'))
$counterStart = $saveSource.IndexOf('void SavedGame::increaseCustomCounter(', [StringComparison]::Ordinal)
$decreaseStart = $saveSource.IndexOf('void SavedGame::decreaseCustomCounter(', $counterStart, [StringComparison]::Ordinal)
$counterEnd = $saveSource.IndexOf('/**', $decreaseStart, [StringComparison]::Ordinal)
if ($counterStart -lt 0 -or $decreaseStart -lt 0 -or $counterEnd -le $decreaseStart) { throw 'Missing reference counter methods.' }
$template = $template.Replace('// COUNTER_METHODS', $saveSource.Substring($counterStart, $counterEnd - $counterStart))
$work = Join-Path $repository 'artifacts/reference-strategic-site-timer'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_site_timer_probe.cpp'
$exe = Join-Path $work 'strategic_site_timer_probe.exe'
[IO.File]::WriteAllText($probe, $template)
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
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
$output = Join-Path $work 'strategic-site-timer.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
