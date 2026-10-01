[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_alien_base_cleanup_probe.cpp'))
function ReadBlock([string]$Path, [string]$Signature) {
 $source = [IO.File]::ReadAllText((Join-Path $reference $Path))
 $start = $source.IndexOf($Signature, [StringComparison]::Ordinal)
 if ($start -lt 0) { throw "Missing method $Signature" }
 $open = $source.IndexOf('{', $start); $depth = 1; $end = $open + 1
 while ($depth -gt 0 -and $end -lt $source.Length) { if ($source[$end] -eq '{') { $depth++ }; if ($source[$end] -eq '}') { $depth-- }; $end++ }
 if ($depth -ne 0) { throw 'Unbalanced reference block.' }
 return $source.Substring($start, $end - $start)
}
$template = $template.Replace('// TARGET_DESTRUCTOR', (ReadBlock 'src/Savegame/Target.cpp' 'Target::~Target()'))
$template = $template.Replace('// CANCEL_PACT', (ReadBlock 'src/Savegame/Country.cpp' 'void Country::setCancelPact()'))
$template = $template.Replace('// CLEAR_LINKS', (ReadBlock 'src/Savegame/SavedGame.cpp' 'void SavedGame::clearLinksForAlienBase('))
$source = [IO.File]::ReadAllText((Join-Path $reference 'src/Geoscape/GeoscapeState.cpp'))
$start = $source.IndexOf('// check and self-destruct alien bases if necessary')
$end = $source.IndexOf('// handle regional and country points for alien bases', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Missing base self-destruction block.' }
$template = $template.Replace('// SELF_DESTRUCT', $source.Substring($start, $end - $start))
$work = Join-Path $repository 'artifacts/reference-strategic-alien-base-cleanup'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_alien_base_cleanup_probe.cpp'
$exe = Join-Path $work 'strategic_alien_base_cleanup_probe.exe'
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
$output = Join-Path $work 'strategic-alien-base-cleanup.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
