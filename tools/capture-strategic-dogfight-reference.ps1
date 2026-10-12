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

$dogfight = Read-Source 'src/Geoscape/DogfightState.cpp'
$craft = Read-Source 'src/Savegame/Craft.cpp'
$craftWeapon = Read-Source 'src/Savegame/CraftWeapon.cpp'
$ufo = Read-Source 'src/Savegame/Ufo.cpp'
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_dogfight_probe.cpp'))

$template = $template.Replace('// PROJECTILE_HEADER', (Without-Includes (Read-Source 'src/Savegame/CraftWeaponProjectile.h')))
$template = $template.Replace('// PROJECTILE_SOURCE', (Without-Includes (Read-Source 'src/Savegame/CraftWeaponProjectile.cpp')))
$template = $template.Replace('// CRAFT_METHODS', (@(
    'int Craft::getPilotAccuracyBonus(const std::vector<Soldier*> &pilots, const Mod *mod) const',
    'int Craft::getPilotDodgeBonus(const std::vector<Soldier*> &pilots, const Mod *mod) const',
    'int Craft::getPilotApproachSpeedModifier(const std::vector<Soldier*> &pilots, const Mod *mod) const',
    'void Craft::setDamage(int damage)', 'void Craft::setShield(int shield)', 'int Craft::getDamagePercentage() const',
    'bool Craft::isDestroyed() const', 'int Craft::getDamageMax() const', 'int Craft::getShieldCapacity() const'
) | ForEach-Object { Read-Method $craft $_ }) -join "`n")
$damageType = Read-Source 'src/Mod/RuleDamageType.cpp'
$template = $template.Replace('// DAMAGE_TYPE_METHODS', (@('int RuleDamageType::getRandomDamage(int power) const',
    'int RuleDamageType::getRandomDamage(int power, FuncRef<int(int, int)> randFunc) const') | ForEach-Object { Read-Method $damageType $_ }) -join "`n")
$template = $template.Replace('// CRAFT_WEAPON_METHODS', (@('bool CraftWeapon::setAmmo(int ammo)', 'CraftWeaponProjectile* CraftWeapon::fire() const') |
    ForEach-Object { Read-Method $craftWeapon $_ }) -join "`n")
$template = $template.Replace('// UFO_METHODS', (@('void Ufo::setDamage(int damage, const Mod *mod)', 'void Ufo::setShield(int shield)',
    'bool Ufo::isCrashed() const', 'bool Ufo::isDestroyed() const') | ForEach-Object { Read-Method $ufo $_ }) -join "`n")

$constructor = Read-Method $dogfight 'DogfightState::DogfightState(GeoscapeState *state, Craft *craft, Ufo *ufo, bool ufoIsAttacking) :'
$initializers = Read-Between $constructor '_state(state)' "`n{"
$template = $template.Replace('// CONSTRUCTOR_INITIALIZERS', $initializers)
$template = $template.Replace('// CONSTRUCTOR_OPTIONS', (Read-Between $constructor '_craft->setInDogfight(true);' '// Create objects'))
$template = $template.Replace('// CONSTRUCTOR_MODE', (Read-Between $constructor '_mode = (_ufoIsAttacking' '_craftDamageAnimTimer'))
$aggressive = $constructor.IndexOf('btnAggressivePress(0);', [StringComparison]::Ordinal)
$aggressiveStart = $constructor.LastIndexOf('if (', $aggressive, [StringComparison]::Ordinal)
$aggressiveEnd = $constructor.IndexOf('}', $aggressive, [StringComparison]::Ordinal) + 1
if ($aggressive -lt 0 -or $aggressiveStart -lt 0) { throw 'Missing initial aggressive mode.' }
$template = $template.Replace('// CONSTRUCTOR_AGGRESSIVE', $constructor.Substring($aggressiveStart, $aggressiveEnd - $aggressiveStart))
$template = $template.Replace('// CONSTRUCTOR_COUNTDOWNS', (Read-Between $constructor "// don't set these variables" '// Set UFO size'))
$template = $template.Replace('// CONSTRUCTOR_UFO_SIZE', (Read-Between $constructor '// Set UFO size' '// Get crafts height'))
$shield = Read-Between $constructor '// Set this as the interception handling UFO shield recharge' "`n}"
$template = $template.Replace('// CONSTRUCTOR_SHIELD_HANDLE', $shield)
$template = $template.Replace('// DOGFIGHT_METHODS', (@(
    'void DogfightState::think()', 'void DogfightState::endDogfight()', 'void DogfightState::animate()', 'void DogfightState::update()',
    'void DogfightState::fireWeapon(int i)', 'void DogfightState::ufoFireWeapon()', 'void DogfightState::minimumDistance()',
    'void DogfightState::maximumDistance()', 'void DogfightState::aggressiveDistance()',
    'void DogfightState::setStatus(const std::string &status)', 'void DogfightState::btnStandoffPress(Action *)',
    'void DogfightState::btnCautiousPress(Action *)', 'void DogfightState::btnStandardPress(Action *)',
    'void DogfightState::btnAggressivePress(Action *)', 'void DogfightState::btnDisengagePress(Action *)'
) | ForEach-Object { Read-Method $dogfight $_ }) -join "`n")
if ($template -match '// [A-Z_]+\r?\n' -and $template -match '// (PROJECTILE|CRAFT|UFO|CONSTRUCTOR|DOGFIGHT|DAMAGE)_[A-Z_]+') { throw 'Unreplaced probe marker.' }

$work = Join-Path $repository 'artifacts/reference-strategic-dogfight'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_dogfight_probe.cpp'
$exe = Join-Path $work 'strategic_dogfight_probe.exe'
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
$output = Join-Path $work 'strategic-dogfight.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
