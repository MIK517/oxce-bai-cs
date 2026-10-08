[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
$header = [IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/ResearchDiary.h'))
$save = [IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/SavedGame.cpp'))
if (-not $save.Contains('ResearchDiaryEntry* entry = new ResearchDiaryEntry(research);')) { throw 'Missing outer research filter.' }
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_research_diary_probe.cpp'))
function StructBlock([string]$Name) {
    $start = $header.IndexOf("struct $Name", [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing $Name." }
    $open = $header.IndexOf('{', $start); $depth = 1; $end = $open + 1
    while ($depth -gt 0 -and $end -lt $header.Length) {
        if ($header[$end] -eq '{') { $depth++ }; if ($header[$end] -eq '}') { $depth-- }; $end++
    }
    if ($depth -ne 0) { throw 'Unbalanced reference block.' }
    return $header.Substring($start, $end - $start) + ';'
}
$enumStart = $header.IndexOf('enum DiscoverySourceType', [StringComparison]::Ordinal)
if ($enumStart -lt 0) { throw 'Missing source enum.' }
$enumEnd = $header.IndexOf(';', $enumStart) + 1
$template = $template.Replace('// DISCOVERY_SOURCE', $header.Substring($enumStart, $enumEnd - $enumStart) + "`n" + (StructBlock 'DiscoverySource'))
$template = $template.Replace('// DIARY_ENTRY', (StructBlock 'ResearchDiaryEntry'))
$work = Join-Path $repository 'artifacts/reference-strategic-research-diary'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_research_diary_probe.cpp'
$exe = Join-Path $work 'strategic_research_diary_probe.exe'
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
$output = Join-Path $work 'strategic-research-diary.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
