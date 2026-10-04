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
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_detection_damage_probe.cpp'))
$header = [IO.File]::ReadAllText((Join-Path $reference 'src/Mod/RuleDamageType.h'))
$headerStart = $header.IndexOf('enum ItemDamageType', [StringComparison]::Ordinal)
$headerEnd = $header.IndexOf('/// Default constructor.', $headerStart, [StringComparison]::Ordinal)
$template = $template.Replace('// TYPES', $header.Substring($headerStart, $headerEnd - $headerStart))
$mod = [IO.File]::ReadAllText((Join-Path $reference 'src/Mod/Mod.cpp'))
$modStart = $mod.IndexOf('_damageTypes.resize(DAMAGE_TYPES)', [StringComparison]::Ordinal)
$modEnd = $mod.IndexOf('_converter = new RuleConverter()', $modStart, [StringComparison]::Ordinal)
$template = $template.Replace('// BUILTINS', $mod.Substring($modStart, $modEnd - $modStart))
$methods = @(
 (Read-Method 'src/Mod/RuleDamageType.cpp' 'RuleDamageType::RuleDamageType()'),
 (Read-Method 'src/Mod/RuleDamageType.cpp' 'bool RuleDamageType::isDirect()'),
 (Read-Method 'src/Mod/RuleItem.cpp' 'const RuleDamageType *RuleItem::getDamageType()'),
 (Read-Method 'src/Mod/RuleItem.cpp' 'const RuleDamageType *RuleItem::getMeleeType()'),
 (Read-Method 'src/Mod/RuleItem.cpp' 'void isSingleTargetScript('),
 (Read-Method 'src/Mod/RuleItem.cpp' 'void getResistTypeScript('),
 (Read-Method 'src/Mod/RuleItem.cpp' 'void getAoeScript('),
 (Read-Method 'src/Mod/RuleItem.cpp' 'void getRandomTypeScript('),
 (Read-Method 'src/Mod/RuleItem.cpp' 'void getArmorEffectivenessScript('),
 (Read-Method 'src/Mod/RuleItem.cpp' 'void getArmorIgnoreScript('),
 ('template<float RuleDamageType::* Ptr>' + [Environment]::NewLine + (Read-Method 'src/Mod/RuleItem.cpp' 'void getDamageToScript('))
)
$template = $template.Replace('// METHODS', ($methods -join [Environment]::NewLine))
$work = Join-Path $repository 'artifacts/reference-strategic-detection-damage'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_detection_damage_probe.cpp'
$exe = Join-Path $work 'strategic_detection_damage_probe.exe'
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
$output = Join-Path $work 'strategic-detection-damage.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
