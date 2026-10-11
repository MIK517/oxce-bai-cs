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
$arrival = $source.IndexOf('void AlienMission::ufoReachedWaypoint(', [StringComparison]::Ordinal)
$conversionStart = $source.IndexOf('addScore(ufo.getLongitude(), ufo.getLatitude(), game);', $arrival, [StringComparison]::Ordinal)
$conversionEnd = $source.IndexOf('else if (trajectory.getID()', $conversionStart, [StringComparison]::Ordinal)
if ($arrival -lt 0 -or $conversionStart -le $arrival -or $conversionEnd -le $conversionStart) { throw 'Missing UFO site-conversion body.' }
$conversion = $source.Substring($conversionStart, $conversionEnd - $conversionStart).TrimEnd()
if (-not $conversion.EndsWith('}')) { throw 'Unexpected conversion ending.' }
$template = $template.Replace('// CONVERSION_BODY', $conversion.Substring(0, $conversion.Length - 1))
$textureSource = [IO.File]::ReadAllText((Join-Path $reference 'src/Mod/Texture.cpp'))
$textureStart = $textureSource.IndexOf('std::string Texture::getRandomDeployment() const', [StringComparison]::Ordinal)
$textureEnd = $textureSource.IndexOf('// helper overloads', $textureStart, [StringComparison]::Ordinal)
if ($textureStart -lt 0 -or $textureEnd -le $textureStart) { throw 'Missing reference texture selection.' }
$template = $template.Replace('// TEXTURE_SELECTION', $textureSource.Substring($textureStart, $textureEnd - $textureStart))
$baseStart = $source.IndexOf('Base* AlienMission::selectXcomBase(', [StringComparison]::Ordinal)
$baseEnd = $source.IndexOf('/**', $baseStart, [StringComparison]::Ordinal)
if ($baseStart -lt 0 -or $baseEnd -le $baseStart) { throw 'Missing reference base selection.' }
$template = $template.Replace('// BASE_SELECTION', $source.Substring($baseStart, $baseEnd - $baseStart))
$saveSource = [IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/SavedGame.cpp'))
$idStart = $saveSource.IndexOf('int SavedGame::getId(', [StringComparison]::Ordinal)
$idEnd = $saveSource.IndexOf('/**', $idStart, [StringComparison]::Ordinal)
$counterStart = $saveSource.IndexOf('void SavedGame::increaseCustomCounter(', [StringComparison]::Ordinal)
$decreaseStart = $saveSource.IndexOf('void SavedGame::decreaseCustomCounter(', $counterStart, [StringComparison]::Ordinal)
$counterEnd = $saveSource.IndexOf('/**', $decreaseStart, [StringComparison]::Ordinal)
if ($idStart -lt 0 -or $idEnd -le $idStart -or $counterStart -lt 0 -or $counterEnd -le $decreaseStart) { throw 'Missing reference ID/counter methods.' }
$template = $template.Replace('// ID_AND_COUNTER_METHODS', $saveSource.Substring($idStart, $idEnd - $idStart) + $saveSource.Substring($counterStart, $counterEnd - $counterStart))
$ufoSource = [IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/Ufo.cpp'))
$altitudeStart = $ufoSource.IndexOf('void Ufo::setAltitude(', [StringComparison]::Ordinal)
$altitudeEnd = $ufoSource.IndexOf('/**', $altitudeStart, [StringComparison]::Ordinal)
$crashedStart = $ufoSource.IndexOf('bool Ufo::isCrashed() const', [StringComparison]::Ordinal)
$crashedEnd = $ufoSource.IndexOf('/**', $crashedStart, [StringComparison]::Ordinal)
$destroyedStart = $ufoSource.IndexOf('bool Ufo::isDestroyed() const', [StringComparison]::Ordinal)
$destroyedEnd = $ufoSource.IndexOf('/**', $destroyedStart, [StringComparison]::Ordinal)
if ($altitudeStart -lt 0 -or $altitudeEnd -le $altitudeStart -or $crashedEnd -le $crashedStart -or $destroyedEnd -le $destroyedStart) { throw 'Missing reference UFO altitude/damage methods.' }
$template = $template.Replace('// ALTITUDE_AND_DAMAGE_METHODS', $ufoSource.Substring($altitudeStart, $altitudeEnd - $altitudeStart) + $ufoSource.Substring($crashedStart, $crashedEnd - $crashedStart) + $ufoSource.Substring($destroyedStart, $destroyedEnd - $destroyedStart))
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
