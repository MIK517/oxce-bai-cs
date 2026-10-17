[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
function Read-Source([string]$RelativePath) { return [IO.File]::ReadAllText((Join-Path $reference $RelativePath)) }
function Read-Method([string]$Source, [string]$Signature) {
    $start = $Source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method $Signature" }
    $opening = $Source.IndexOf('{', $start)
    $depth = 1; $end = $opening + 1
    while ($depth -gt 0 -and $end -lt $Source.Length) {
        if ($Source[$end] -eq '{') { $depth++ }
        if ($Source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced method $Signature" }
    return $Source.Substring($start, $end - $start)
}
function Read-Between([string]$Source, [string]$From, [string]$To) {
    $start = $Source.IndexOf($From, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing block start $From" }
    $end = $Source.IndexOf($To, $start + $From.Length, [StringComparison]::Ordinal)
    if ($end -lt 0) { throw "Missing block end $To" }
    return $Source.Substring($start, $end - $start)
}
function Without-Includes([string]$Source) {
    return (($Source -split "`n") | Where-Object { $_ -notmatch '^\s*#(include|pragma)' }) -join "`n"
}

$globe = Read-Source 'src/Geoscape/Globe.cpp'
$time = Read-Source 'src/Savegame/GameTime.cpp'
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_landing_shade_probe.cpp'))
$template = $template.Replace('// CORD_HEADER', (Without-Includes (Read-Source 'src/Geoscape/Cord.h')))
$template = $template.Replace('// GAMETIME_DAYLIGHT', (Read-Method $time 'double GameTime::getDaylight() const'))
$template = $template.Replace('// GLOBE_STATIC_DATA', (Read-Between $globe 'struct GlobeStaticData' 'struct Ocean'))
$template = $template.Replace('// CREATE_SHADOW_VALUE', (Read-Method $globe 'static inline Uint8 getShadowValue(const Cord& earth, const Cord& sun, const Sint16& noise)'))
$template = $template.Replace('// GLOBE_SUN_DIRECTION', (Read-Method $globe 'Cord Globe::getSunDirection(double lon, double lat) const'))
$shadeMethod = Read-Method $globe 'void Globe::getPolygonTextureAndShade(double lon, double lat, int *texture, int *shade) const'
$template = $template.Replace('// GLOBE_SHADE', (Read-Between $shadeMethod 'int worldshades[32]' 'Polygon *t'))
if ($template -match '// (CORD|GAMETIME|GLOBE|CREATE)_[A-Z_]+') { throw 'Unreplaced probe marker.' }

$work = Join-Path $repository 'artifacts/reference-strategic-landing-shade'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_landing_shade_probe.cpp'
$exe = Join-Path $work 'strategic_landing_shade_probe.exe'
[IO.File]::WriteAllText($probe, $template)
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
$devCommand = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find Common7\Tools\VsDevCmd.bat
if (-not $devCommand) { throw 'Visual Studio C++ tools not found.' }
foreach ($value in @($devCommand, $probe, $exe, $work)) {
    if ($value -match '["&|<>^]') { throw 'Unsafe cmd.exe path.' }
}
$compile = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && cd /d "{1}" && cl.exe /nologo /std:c++20 /EHsc /W3 "{2}" /Fe:"{3}"' -f $devCommand, $work, $probe, $exe
& $env:ComSpec /d /c $compile
if ($LASTEXITCODE -ne 0) { throw 'Reference probe compilation failed.' }
$raw = & $exe
if ($LASTEXITCODE -ne 0) { throw 'Reference probe execution failed.' }
$output = Join-Path $work 'strategic-landing-shade.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
