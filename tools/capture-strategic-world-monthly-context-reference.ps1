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
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_world_monthly_context_probe.cpp'))
$methods = @(
    (Read-Method 'src/Savegame/SavedGame.cpp' 'int SavedGame::getCurrentScore('),
    (Read-Method 'src/Savegame/SavedGame.cpp' 'int SavedGame::getCountryFunding()'),
    (Read-Method 'src/Savegame/SavedGame.cpp' 'int SavedGame::getBaseMaintenance()')
)
$template = $template.Replace('// METHODS', ($methods -join [Environment]::NewLine))
$modHeader = [IO.File]::ReadAllText((Join-Path $reference 'src/Mod/Mod.h'))
$bonus = [regex]::Match($modHeader, 'int getPerformanceBonus\(int score\) const \{ return \(int\)\(score \* _performanceBonusFactor\); \}')
if (-not $bonus.Success) { throw 'Missing reference performance bonus method.' }
$template = $template.Replace('// BONUS_METHOD', $bonus.Value)
$geoscape = [IO.File]::ReadAllText((Join-Path $reference 'src/Geoscape/GeoscapeState.cpp'))
$method = $geoscape.IndexOf('void GeoscapeState::determineAlienMissions(', [StringComparison]::Ordinal)
if ($method -lt 0) { throw 'Missing reference mission scheduler.' }
$start = $geoscape.IndexOf('int currentScore = save->getCurrentScore(month);', $method, [StringComparison]::Ordinal)
$end = $geoscape.IndexOf('std::vector<RuleMissionScript*> availableMissions;', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -lt 0) { throw 'Missing reference scheduling context.' }
$template = $template.Replace('// CONTEXT', $geoscape.Substring($start, $end - $start))
$work = Join-Path $repository 'artifacts/reference-strategic-world-monthly-context'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_world_monthly_context_probe.cpp'
$exe = Join-Path $work 'strategic_world_monthly_context_probe.exe'
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
$output = Join-Path $work 'strategic-world-monthly-context.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
