[CmdletBinding()]
param(
    [string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai',
    [string]$ExpectedCommit = '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15'
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne $ExpectedCommit) { throw 'Unexpected reference revision.' }
$sourcePath = Join-Path $reference 'src\Geoscape\GeoscapeState.cpp'
$source = [IO.File]::ReadAllText($sourcePath)
$start = $source.IndexOf('for (int i = 0; i < timeSpan && !_pause; ++i)', [StringComparison]::Ordinal)
$end = $source.IndexOf('_pause = !_dogfightsToBeStarted.empty()', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -lt $start) { throw 'Reference dispatch loop markers changed.' }
$loop = $source.Substring($start, $end - $start)
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures\reference-probes\savegames\strategic_time_probe.cpp'))
$work = Join-Path $repository 'artifacts\reference-strategic-time'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_time_probe.cpp'
$exe = Join-Path $work 'strategic_time_probe.exe'
[IO.File]::WriteAllText($probe, $template.Replace('// DISPATCH_LOOP', $loop))
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
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
$output = Join-Path $work 'strategic-time.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
Write-Output ('Reference source SHA256: ' + (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash)
