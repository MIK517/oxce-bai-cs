[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
function Read-Method([string]$RelativePath, [string]$Signature) {
    $source = [IO.File]::ReadAllText((Join-Path $reference $RelativePath))
    $start = $source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method $Signature" }
    $opening = $source.IndexOf('{', $start)
    $depth = 1; $end = $opening + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced method $Signature" }
    return $source.Substring($start, $end - $start)
}
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_event_scripts_probe.cpp'))
$methods = @(
    (Read-Method 'src/Savegame/WeightedOptions.cpp' 'std::string WeightedOptions::choose()'),
    (Read-Method 'src/Savegame/WeightedOptions.cpp' 'void WeightedOptions::set('),
    (Read-Method 'src/Savegame/WeightedOptions.cpp' 'std::vector<std::string> WeightedOptions::getNames()'),
    (Read-Method 'src/Mod/RuleEventScript.cpp' 'std::string RuleEventScript::generate('),
    (Read-Method 'src/Savegame/SavedGame.cpp' 'bool SavedGame::wasEventGenerated(')
)
$template = $template.Replace('// METHODS', ($methods -join [Environment]::NewLine))
$template = $template.Replace('// EVENT_LOOP', (Read-Method 'src/Geoscape/GeoscapeState.cpp' 'for (auto& eventCommand : relevantEventScripts)'))
$work = Join-Path $repository 'artifacts/reference-strategic-event-scripts'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_event_scripts_probe.cpp'
$exe = Join-Path $work 'strategic_event_scripts_probe.exe'
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
$output = Join-Path $work 'strategic-event-scripts.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
