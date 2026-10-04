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
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_detection_scripts_probe.cpp'))
$methods = @(
    (Read-Method 'src/Savegame/Base.cpp' 'UfoDetection Base::detect('),
    (Read-Method 'src/Savegame/Craft.cpp' 'UfoDetection Craft::detect(')
)
$template = $template.Replace('// METHODS', ($methods -join [Environment]::NewLine))
$ufoRuleMethods = @(
    (Read-Method 'src/Mod/RuleUfo.cpp' 'int RuleUfo::getRadius()'),
    (Read-Method 'src/Mod/RuleUfo.cpp' 'int RuleUfo::getWeaponRange()'),
    (Read-Method 'src/Mod/RuleUfo.cpp' 'int RuleUfo::getWeaponPower()'),
    (Read-Method 'src/Mod/RuleUfo.cpp' 'int RuleUfo::getWeaponReload()')
)
$template = $template.Replace('// UFO_RULE_METHODS', ($ufoRuleMethods -join [Environment]::NewLine))
$modSettings = @(
    (Read-Method 'src/Mod/Mod.h' 'int getMaxViewDistance()'),
    (Read-Method 'src/Mod/Mod.h' 'int getMaxDarknessToSeeUnits()')
)
$template = $template.Replace('// MOD_SETTINGS', ($modSettings -join [Environment]::NewLine).Replace('int getMaxViewDistance()', 'int Mod::getMaxViewDistance()').Replace('int getMaxDarknessToSeeUnits()', 'int Mod::getMaxDarknessToSeeUnits()'))
$ruleMetadata = @(
    (Read-Method 'src/Mod/RuleItem.cpp' 'int RuleItem::getWeight()'),
    (Read-Method 'src/Mod/RuleItem.cpp' 'BattleType RuleItem::getBattleType()'),
    (Read-Method 'src/Mod/Armor.cpp' 'int Armor::getSize()'),
    (Read-Method 'src/Mod/RuleItem.cpp' 'void getTypeScript(const RuleItem*'),
    (Read-Method 'src/Mod/Armor.cpp' 'void getTypeScript(const Armor*'),
    (Read-Method 'src/Mod/RuleSkill.cpp' 'void getTypeScript(const RuleSkill*'),
    (Read-Method 'src/Mod/RuleCraft.cpp' 'void getTypeScript(const RuleCraft*'),
    (Read-Method 'src/Mod/RuleItem.cpp' 'void getBattleTypeScript(')
)
$template = $template.Replace('// RULE_METADATA', ($ruleMetadata -join [Environment]::NewLine))
$ufoMethods = @(
    (Read-Method 'src/Savegame/Ufo.cpp' 'int Ufo::getDamage()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'int Ufo::getDamagePercentage()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'int Ufo::getShield()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'int Ufo::getShieldPercentage()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'bool Ufo::getDetected()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'bool Ufo::getHyperDetected()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'bool Ufo::isHunterKiller()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'bool Ufo::isHunting()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'bool Ufo::isEscorting()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'int Ufo::getHuntMode()'),
    (Read-Method 'src/Savegame/Ufo.cpp' 'int Ufo::getHuntBehavior()')
)
$template = $template.Replace('// UFO_METHODS', ($ufoMethods -join [Environment]::NewLine))
$template = $template.Replace('// DAMAGE_WRAPPER', (Read-Method 'src/Savegame/Ufo.cpp' 'void getDamageMaxScript('))
$template = $template.Replace('// MOD_EMPTY', (Read-Method 'src/Mod/Mod.h' 'static bool isEmptyRuleName('))
$tagMethods = @(
    (Read-Method 'src/Engine/Script.cpp' 'void ScriptValuesBase::setBase('),
    (Read-Method 'src/Engine/Script.cpp' 'int ScriptValuesBase::getBase(')
)
$template = $template.Replace('// STATS_ADD', (Read-Method 'src/Mod/RuleCraft.h' 'RuleCraftStats& operator+=('))
$template = $template.Replace('// STATS_BIND', ('template<auto Stat, typename TBind>' + [Environment]::NewLine + (Read-Method 'src/Mod/RuleCraft.h' 'static void addGetStatsScript(')))
$template = $template.Replace('// TAG_METHODS', ($tagMethods -join [Environment]::NewLine))
$countryMethods = @(
    (Read-Method 'src/Savegame/Country.h' 'int getCurrentFunding()'),
    (Read-Method 'src/Savegame/Country.h' 'int getCurrentActivityAlien()'),
    (Read-Method 'src/Savegame/Country.h' 'int getCurrentActivityXcom()')
)
$countryRuleMethods = @(
    (Read-Method 'src/Savegame/Country.cpp' 'const RuleCountry* Country::getRules()'),
    (Read-Method 'src/Mod/RuleCountry.cpp' 'int RuleCountry::getFundingCap()')
)
$template = $template.Replace('// COUNTRY_RULE_METHODS', ($countryRuleMethods -join [Environment]::NewLine))
$template = $template.Replace('// COUNTRY_METHODS', (Read-Method 'src/Savegame/Country.cpp' 'bool Country::getPact()'))
foreach ($method in $countryMethods) {
    $signature = [regex]::Replace($method.Substring(0, $method.IndexOf('{')).Trim(), '\s+', ' ')
    $template = $template.Replace($signature + ';', $method)
}
$template = $template.Replace('// COUNTRY_FILTER', (Read-Method 'src/Savegame/SavedGame.cpp' 'bool filterCountryConstScript('))
$countryList = @(
    ('template<typename T, typename V, typename... Args, auto... X, bool (*Func)(T*, V* v, Args...)>' + [Environment]::NewLine + (Read-Method 'src/Engine/ScriptBind.h' 'struct BindListInitImpl<bool (*)(T*, V* v, Args...), Func, X...>') + ';'),
    ('template<typename T, typename V, typename... Args, auto... X, bool (*Func)(T*, V* v, Args...)>' + [Environment]::NewLine + (Read-Method 'src/Engine/ScriptBind.h' 'struct BindListLoopImpl<bool (*)(T*, V* v, Args...), Func, X...>') + ';')
)
$template = $template.Replace('// COUNTRY_LIST', ($countryList -join [Environment]::NewLine))
$template = $template.Replace('// RESEARCH_FIND', (Read-Method 'src/Savegame/SavedGame.cpp' 'bool haveReserchVector(const std::vector<const RuleResearch*> &vec, const RuleResearch *res)'))
$template = $template.Replace('// RESEARCH_METHOD', (Read-Method 'src/Savegame/SavedGame.cpp' 'bool SavedGame::isResearched(const RuleResearch *research, bool considerDebugMode) const'))
$template = $template.Replace('// RESEARCH_WRAPPER', (Read-Method 'src/Savegame/SavedGame.cpp' 'void isResearchedScript('))
$template = $template.Replace('// MOD_METHODS', ('template<typename T>' + [Environment]::NewLine + (Read-Method 'src/Mod/Mod.cpp' 'T *Mod::getRule(') + [Environment]::NewLine + (Read-Method 'src/Mod/Mod.cpp' 'RuleResearch *Mod::getResearch(const std::string &id, bool error) const')))
$lookupMethods = @(
    (Read-Method 'src/Mod/Mod.cpp' 'RuleItem *Mod::getItem('),
    (Read-Method 'src/Mod/Mod.cpp' 'Armor *Mod::getArmor('),
    (Read-Method 'src/Mod/Mod.cpp' 'RuleSoldier *Mod::getSoldier('),
    (Read-Method 'src/Mod/Mod.cpp' 'RuleSkill *Mod::getSkill('),
    (Read-Method 'src/Mod/Mod.cpp' 'void getItemScript('),
    (Read-Method 'src/Mod/Mod.cpp' 'void getArmorScript('),
    (Read-Method 'src/Mod/Mod.cpp' 'void getSoldierScript('),
    (Read-Method 'src/Mod/Mod.cpp' 'void getSkillScript(')
)
$template = $template.Replace('// RULE_LOOKUPS', ($lookupMethods -join [Environment]::NewLine))
$template = $template.Replace('// RESEARCH_LOOKUP', (Read-Method 'src/Mod/Mod.cpp' 'void getRuleResearch('))
$work = Join-Path $repository 'artifacts/reference-strategic-detection-scripts'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_detection_scripts_probe.cpp'
$exe = Join-Path $work 'strategic_detection_scripts_probe.exe'
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
$output = Join-Path $work 'strategic-detection-scripts.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
