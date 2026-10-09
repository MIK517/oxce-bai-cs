[CmdletBinding()]
param([string]$ReferenceRoot = 'D:\Development\Projects\CPP\oxce-bai')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$commit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne '4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15') { throw 'Unexpected reference revision.' }
function Read-ReferenceMethod([string]$RelativePath, [string]$Signature) {
    $source = [IO.File]::ReadAllText((Join-Path $reference $RelativePath))
    $start = $source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method $Signature" }
    $opening = $source.IndexOf('{', $start)
    $depth = 1
    $end = $opening + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced method $Signature" }
    return $source.Substring($start, $end - $start)
}
$template = [IO.File]::ReadAllText((Join-Path $repository 'fixtures/reference-probes/savegames/strategic_hunting_probe.cpp'))
$researchGetters = (Read-ReferenceMethod 'src/Mod/RuleResearch.cpp' 'int RuleResearch::getCost() const').Replace('_cost', 'cost') + [Environment]::NewLine +
    (Read-ReferenceMethod 'src/Mod/RuleResearch.cpp' 'int RuleResearch::getPoints() const').Replace('_points', 'points') + [Environment]::NewLine +
    (Read-ReferenceMethod 'src/Mod/RuleResearch.h' 'const RuleResearch* getLookup() const').Replace('getLookup()', 'RuleResearch::getLookup()').Replace('_lookup', 'lookup') + [Environment]::NewLine +
    (Read-ReferenceMethod 'src/Mod/RuleResearch.h' 'const RuleItem* getNeededItem() const').Replace('getNeededItem()', 'RuleResearch::getNeededItem()').Replace('_neededItem', 'needed')
$template = $template.Replace('// RESEARCH_RULE_GETTERS', $researchGetters)
$template = $template.Replace('// PRICE_TIME_POINTER',
    (Read-ReferenceMethod 'src/Savegame/SavedGame.cpp' 'GameTime *SavedGame::getTime() const').Replace('_time', 'clock') + [Environment]::NewLine +
    (Read-ReferenceMethod 'src/Savegame/SavedGame.cpp' 'void getTimeScript(const SavedGame* sg, const GameTime*& r)'))
$refuelMethods = ''
foreach ($signature in @('void Craft::setFuel(int fuel)', 'std::string Craft::refuel()', 'void Craft::setDestination(Target *dest)', 'void Craft::repair()', 'const RuleItem* Craft::rearm()', 'int Craft::getFuelConsumption(int speed, int escortSpeed) const', 'void Craft::consumeFuel(int escortSpeed)', 'void Craft::returnToBase()', 'void Craft::checkup()', 'bool Craft::think()', 'void Craft::setBase(Base *base, bool move)', 'void Craft::reuseItem(const RuleItem* item)')) {
    $refuelMethods += (Read-ReferenceMethod 'src/Savegame/Craft.cpp' $signature) + [Environment]::NewLine
}
$template = $template.Replace('// REFUEL_METHODS', $refuelMethods)
$template = $template.Replace('// TRANSFER_METHOD', (Read-ReferenceMethod 'src/Savegame/Transfer.cpp' 'void Transfer::advance(Base *base)') + [Environment]::NewLine +
    (Read-ReferenceMethod 'src/Savegame/Transfer.cpp' 'int Transfer::getHours() const'))
$researchSpawn = Read-ReferenceMethod 'src/Savegame/SavedGame.cpp' 'void SavedGame::handlePrimaryResearchSideEffects('
$spawnStart = $researchSpawn.IndexOf('RuleItem* spawnedItem =', [StringComparison]::Ordinal)
$spawnEnd = $researchSpawn.IndexOf('// 3l. handle spawned events', $spawnStart, [StringComparison]::Ordinal)
if ($spawnStart -lt 0 -or $spawnEnd -le $spawnStart) { throw 'Missing research item transfer fragment.' }
$dailyResearch = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::time1Day()'
$corpseStart = $dailyResearch.IndexOf('if (Options::retainCorpses && research->needItem()', [StringComparison]::Ordinal)
$corpseEnd = $dailyResearch.IndexOf('// 3bb.', $corpseStart, [StringComparison]::Ordinal)
if ($corpseStart -lt 0 -or $corpseEnd -le $corpseStart) { throw 'Missing daily research corpse fragment.' }
$template = $template.Replace('// RESEARCH_DAILY_CORPSE', $dailyResearch.Substring($corpseStart, $corpseEnd - $corpseStart))
$template = $template.Replace('// ARMOR_CORPSE', (Read-ReferenceMethod 'src/Mod/Armor.cpp' 'const RuleItem* Armor::getCorpseGeoscape() const'))
$template = $template.Replace('// CORPSE_RECOVERY_FLAGS', (Read-ReferenceMethod 'src/Mod/RuleItem.cpp' 'bool RuleItem::isRecoverable() const') + [Environment]::NewLine + (Read-ReferenceMethod 'src/Mod/RuleItem.cpp' 'bool RuleItem::isCorpseRecoverable() const'))
$primaryStart = $dailyResearch.IndexOf('topicsToCheck.push_back(research);', [StringComparison]::Ordinal)
$primaryEnd = $dailyResearch.IndexOf('if (!topicsToCheck.empty())', $primaryStart, [StringComparison]::Ordinal)
if ($primaryStart -lt 0 -or $primaryEnd -le $primaryStart) { throw 'Missing primary topic collection fragment.' }
# Exclude the closing daily finished-project loop; the collaborator wraps one completion.
$primary = $dailyResearch.Substring($primaryStart, $primaryEnd - $primaryStart).TrimEnd()
$primary = $primary.Substring(0, $primary.LastIndexOf('}')).TrimEnd()
$template = $template.Replace('// RESEARCH_PRIMARY_TOPICS', $primary)
$discoveryMethods = ''
foreach ($signature in @('void SavedGame::addFinishedResearch(', 'const RuleResearch* SavedGame::selectGetOneFree(', 'bool SavedGame::hasUndiscoveredGetOneFree(', 'bool SavedGame::hasUndiscoveredProtectedUnlock(')) {
    $discoveryMethods += (Read-ReferenceMethod 'src/Savegame/SavedGame.cpp' $signature) + [Environment]::NewLine
}
$template = $template.Replace('// RESEARCH_DISCOVERY_METHODS', $discoveryMethods)
$template = $template.Replace('// RESEARCH_SPAWN', $researchSpawn.Substring($spawnStart, $spawnEnd - $spawnStart))
$counterStart = $researchSpawn.IndexOf('for (auto& inc : myResearchRule->getIncreaseCounter())', [StringComparison]::Ordinal)
$counterEnd = $researchSpawn.LastIndexOf("`n`t}", [StringComparison]::Ordinal)
if ($counterStart -lt 0 -or $counterEnd -le $counterStart) { throw 'Missing primary research counter fragment.' }
$template = $template.Replace('// RESEARCH_PRIMARY_COUNTERS', $researchSpawn.Substring($counterStart, $counterEnd - $counterStart))
$template = $template.Replace('// RESEARCH_COUNTER_METHODS', (Read-ReferenceMethod 'src/Savegame/SavedGame.cpp' 'void SavedGame::increaseCustomCounter(') + [Environment]::NewLine + (Read-ReferenceMethod 'src/Savegame/SavedGame.cpp' 'void SavedGame::decreaseCustomCounter('))
$researchReturn = Read-ReferenceMethod 'src/Savegame/Base.cpp' 'void Base::removeResearch(ResearchProject * project)'
$returnStart = $researchReturn.IndexOf('if (ruleResearch->isHoldingNeededItem())', [StringComparison]::Ordinal)
$returnEnd = $researchReturn.IndexOf('Collections::deleteIf', $returnStart, [StringComparison]::Ordinal)
if ($returnStart -lt 0 -or $returnEnd -le $returnStart) { throw 'Missing held research item return fragment.' }
$template = $template.Replace('// RESEARCH_REFUND', $researchReturn.Substring($returnStart, $returnEnd - $returnStart))
$template = $template.Replace('// PRODUCTION_AMOUNT', (Read-ReferenceMethod 'src/Savegame/Production.cpp' 'int Production::getAmountProduced() const'))
$production = Read-ReferenceMethod 'src/Savegame/Production.cpp' 'productionProgress_e Production::step('
$progressStart = $production.IndexOf('int done = getAmountProduced();', [StringComparison]::Ordinal)
$progressEnd = $production.IndexOf('int count = 0;', $progressStart, [StringComparison]::Ordinal)
if ($progressStart -lt 0 -or $progressEnd -le $progressStart) { throw 'Missing production completion-count fragment.' }
$deliveryStart = $production.IndexOf('int transferTimeItems =', [StringComparison]::Ordinal)
$craftStart = $production.IndexOf('auto ruleCraft = _rules->getProducedCraft();', [StringComparison]::Ordinal)
$craftEnd = $production.IndexOf('else', $craftStart, [StringComparison]::Ordinal)
# The first else belongs to transfer-vs-immediate delivery. The next is item production.
$craftEnd = $production.IndexOf('else', $craftEnd + 4, [StringComparison]::Ordinal)
if ($craftStart -lt 0 -or $craftEnd -le $craftStart) { throw 'Missing craft production fragment.' }
$template = $template.Replace('// PRODUCTION_CRAFT_DELIVERY', $production.Substring($craftStart, $craftEnd - $craftStart))
$deliveryEnd = $production.IndexOf('// Random manufacture', $deliveryStart, [StringComparison]::Ordinal)
if ($deliveryStart -lt 0 -or $deliveryEnd -le $deliveryStart) { throw 'Missing fixed production delivery fragment.' }
$delivery = $production.Substring($deliveryStart, $deliveryEnd - $deliveryStart).TrimEnd()
$delivery = $delivery.Substring(0, $delivery.LastIndexOf('}')).TrimEnd()
$template = $template.Replace('// PRODUCTION_DELIVERY', $delivery)
$randomStart = $production.IndexOf('if (!_rules->getRandomProducedItems().empty())', $deliveryEnd, [StringComparison]::Ordinal)
$randomEnd = $production.IndexOf('// Spawn persons', $randomStart, [StringComparison]::Ordinal)
if ($randomStart -lt 0 -or $randomEnd -le $randomStart) { throw 'Missing random production delivery fragment.' }
$template = $template.Replace('// PRODUCTION_RANDOM_DELIVERY', $production.Substring($randomStart, $randomEnd - $randomStart))
$personStart = $production.IndexOf('const std::string &spawnedPersonType =', $randomEnd, [StringComparison]::Ordinal)
$personEnd = $production.IndexOf('// Generate an event', $personStart, [StringComparison]::Ordinal)
if ($personStart -lt 0 -or $personEnd -le $personStart) { throw 'Missing staff production transfer fragment.' }
$template = $template.Replace('// PRODUCTION_PERSONNEL', $production.Substring($personStart, $personEnd - $personStart))
$template = $template.Replace('// PRODUCTION_SOLDIER_GENERATION', (Read-ReferenceMethod 'src/Mod/Mod.cpp' 'Soldier *Mod::genSoldier(').Replace('Soldier *Mod::genSoldier(', 'Soldier *ResearchMod::genSoldier('))
$template = $template.Replace('// PRODUCTION_QUARTERS', (Read-ReferenceMethod 'src/Savegame/Base.cpp' 'int Base::getUsedQuarters() const'))
$scoreStart = $production.IndexOf('if (_rules->getPoints() != 0)', [StringComparison]::Ordinal)
$scoreEnd = $production.IndexOf('count++;', $scoreStart, [StringComparison]::Ordinal)
if ($scoreStart -lt 0 -or $scoreEnd -le $scoreStart) { throw 'Missing production score fragment.' }
$template = $template.Replace('// PRODUCTION_SCORE', $production.Substring($scoreStart, $scoreEnd - $scoreStart))
$template = $template.Replace('// RESEARCH_SCORE', (Read-ReferenceMethod 'src/Savegame/SavedGame.cpp' 'void SavedGame::addResearchScore('))
$template = $template.Replace('// PRODUCTION_CRAFT_COUNT', (Read-ReferenceMethod 'src/Savegame/Base.cpp' 'int Base::getCraftCountForProduction('))
$unload = Read-ReferenceMethod 'src/Savegame/Craft.cpp' 'void Craft::unload()'
$unloadStart = $unload.IndexOf('for (const auto& pair : *_items->getContents())', [StringComparison]::Ordinal)
$unloadEnd = $unload.IndexOf('// Remove vehicles', $unloadStart, [StringComparison]::Ordinal)
if ($unloadStart -lt 0 -or $unloadEnd -le $unloadStart) { throw 'Missing craft inventory unload fragment.' }
$template = $template.Replace('// PRODUCTION_CRAFT_UNLOAD_ITEMS', $unload.Substring($unloadStart, $unloadEnd - $unloadStart))
$template = $template.Replace('// TARGET_DESTRUCTOR', (Read-ReferenceMethod 'src/Savegame/Target.cpp' 'Target::~Target()'))
$productionHeader = [IO.File]::ReadAllText((Join-Path $reference 'src/Savegame/Production.h'))
$productionEnum = [regex]::Match($productionHeader, 'enum productionProgress_e[^;]+;').Value
if (-not $productionEnum) { throw 'Missing production progress enum.' }
$template = $template.Replace('// PRODUCTION_ENUM', $productionEnum)
$staffStart = $production.IndexOf('if (_isFallback)', [StringComparison]::Ordinal)
$staffEnd = $production.IndexOf('int done = getAmountProduced();', $staffStart, [StringComparison]::Ordinal)
if ($staffStart -lt 0 -or $staffEnd -le $staffStart) { throw 'Missing fallback production staffing fragment.' }
$template = $template.Replace('// PRODUCTION_STAFFING', $production.Substring($staffStart, $staffEnd - $staffStart))
$baseMethods = ''
foreach ($signature in @('int Base::getUsedWorkshops() const', 'int Base::getFreeWorkshops() const', 'void Base::removeProduction(')) {
    $baseMethods += (Read-ReferenceMethod 'src/Savegame/Base.cpp' $signature) + [Environment]::NewLine
}
$template = $template.Replace('// PRODUCTION_BASE_METHODS', $baseMethods)
$hourProduction = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::time1Hour()'
$callerStart = $hourProduction.IndexOf('std::map<Production*, productionProgress_e> toRemove;', [StringComparison]::Ordinal)
$callerEnd = $hourProduction.IndexOf('if (Options::storageLimitsEnforced)', $callerStart, [StringComparison]::Ordinal)
if ($callerStart -lt 0 -or $callerEnd -le $callerStart) { throw 'Missing hourly production removal caller.' }
$template = $template.Replace('// PRODUCTION_HOURLY_CALLER', $hourProduction.Substring($callerStart, $callerEnd - $callerStart))
$continuationStart = $production.IndexOf('count++;', [StringComparison]::Ordinal)
if ($continuationStart -lt 0) { throw 'Missing production continuation fragment.' }
$continuation = $production.Substring($continuationStart).TrimEnd()
$continuation = $continuation.Substring(0, $continuation.LastIndexOf('}')).TrimEnd()
$template = $template.Replace('// PRODUCTION_CONTINUATION', $continuation)
$productionInputMethods = ''
foreach ($signature in @('bool Production::haveEnoughMoneyForOneMoreUnit(', 'bool Production::haveEnoughLivingSpaceForOneMoreUnit(', 'bool Production::haveEnoughMaterialsForOneMoreUnit(', 'void Production::startItem(')) {
    $productionInputMethods += (Read-ReferenceMethod 'src/Savegame/Production.cpp' $signature) + [Environment]::NewLine
}
$template = $template.Replace('// PRODUCTION_INPUT_METHODS', $productionInputMethods)
$template = $template.Replace('// PRODUCTION_MONEY_RULE', (Read-ReferenceMethod 'src/Mod/RuleManufacture.cpp' 'bool RuleManufacture::haveEnoughMoneyForOneMoreUnit('))
$template = $template.Replace('// PRODUCTION_SALE_PRICE', (Read-ReferenceMethod 'src/Mod/RuleItem.cpp' 'int RuleItem::getSellCostAdjusted('))
$template = $template.Replace('// PRODUCTION_ACCOUNTING', (Read-ReferenceMethod 'src/Savegame/SavedGame.cpp' 'void SavedGame::setFunds('))
$template = $template.Replace('// PRODUCTION_COUNT', $production.Substring($progressStart, $progressEnd - $progressStart) + 'return produced; } return 0;')
$template = $template.Replace('// WEAPON_REARM', (Read-ReferenceMethod 'src/Savegame/CraftWeapon.cpp' 'int CraftWeapon::rearm(const int available, const int clipSize)'))
$template = $template.Replace('// FACILITY_REARM', (Read-ReferenceMethod 'src/Savegame/BaseFacility.cpp' 'const RuleItem* BaseFacility::rearm()'))
$halfHour = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::time30Minutes()'
$autoStart = $halfHour.IndexOf('if (xcraft->getStatus() == "STR_READY" && xcraft->getRules()->canAutoPatrol())', [StringComparison]::Ordinal)
if ($autoStart -lt 0) { throw 'Missing auto-patrol fragment.' }
$autoOpen = $halfHour.IndexOf('{', $autoStart)
$autoDepth = 1
$autoEnd = $autoOpen + 1
while ($autoDepth -gt 0 -and $autoEnd -lt $halfHour.Length) {
    if ($halfHour[$autoEnd] -eq '{') { $autoDepth++ }
    if ($halfHour[$autoEnd] -eq '}') { $autoDepth-- }
    $autoEnd++
}
if ($autoDepth -ne 0) { throw 'Unbalanced auto-patrol fragment.' }
$template = $template.Replace('// AUTO_PATROL', $halfHour.Substring($autoStart, $autoEnd - $autoStart))
$tenMinutes = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::time10Minutes()'
$fuelStart = $tenMinutes.IndexOf('int escortSpeed = 0;', [StringComparison]::Ordinal)
$fuelEnd = $tenMinutes.IndexOf('if (xcraft->getDestination() == 0', $fuelStart, [StringComparison]::Ordinal)
if ($fuelStart -lt 0 -or $fuelEnd -le $fuelStart) { throw 'Missing ordered craft fuel fragment.' }
$template = $template.Replace('// FUEL_CALLER', $tenMinutes.Substring($fuelStart, $fuelEnd - $fuelStart))
$template = $template.Replace('// IGNORED_METHOD', (Read-ReferenceMethod 'src/Savegame/Craft.cpp' 'bool Craft::isIgnoredByHK() const'))
$template = $template.Replace('// ATTRACTION', (Read-ReferenceMethod 'src/Savegame/Craft.cpp' 'int Craft::getHunterKillerAttraction(int huntMode) const'))
$constructor = Read-ReferenceMethod 'src/Savegame/Ufo.cpp' 'Ufo::Ufo(const RuleUfo *rules, int uniqueId, int hunterKillerPercentage, int huntMode, int huntBehavior)'
$flagStart = $constructor.IndexOf('if (hunterKillerPercentage > 0)', [StringComparison]::Ordinal)
if ($flagStart -lt 0) { throw 'Missing constructor hunter flags.' }
$template = $template.Replace('// CONSTRUCTOR_FLAGS', $constructor.Substring($flagStart, $constructor.LastIndexOf('}') - $flagStart))
$template = $template.Replace('// RADAR', (Read-ReferenceMethod 'src/Savegame/Ufo.cpp' 'bool Ufo::insideRadarRange(Target *target) const'))
$method = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::ufoHuntingAndEscorting()'
$start = $method.IndexOf('int newAttraction = INT_MAX;', [StringComparison]::Ordinal)
$end = $method.IndexOf('if (newTarget)', $start, [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Missing hunter selection fragment.' }
$template = $template.Replace('// SELECTION', $method.Substring($start, $end - $start))
$escortStart = $method.IndexOf('if (ufo->isEscort() && !ufo->isHunting() && !ufo->isEscorting())', [StringComparison]::Ordinal)
if ($escortStart -lt 0) { throw 'Missing retarget escort fragment.' }
$escortOpening = $method.IndexOf('{', $escortStart)
$depth = 1
$escortEnd = $escortOpening + 1
while ($depth -gt 0 -and $escortEnd -lt $method.Length) {
    if ($method[$escortEnd] -eq '{') { $depth++ }
    if ($method[$escortEnd] -eq '}') { $depth-- }
    $escortEnd++
}
if ($depth -ne 0) { throw 'Unbalanced retarget escort fragment.' }
$template = $template.Replace('// RETARGET_ESCORT', $method.Substring($escortStart, $escortEnd - $escortStart))

$retarget = ''
foreach ($signature in @('void Ufo::backupOriginalDestination()', 'void Ufo::setTargetedXcomCraft(', 'void Ufo::setEscortedUfo(', 'void Ufo::resetOriginalDestination(bool', 'void Ufo::setDestination(')) {
    $retarget += (Read-ReferenceMethod 'src/Savegame/Ufo.cpp' $signature) + [Environment]::NewLine
}
$template = $template.Replace('// RETARGET_METHODS', $retarget)
$spawn = Read-ReferenceMethod 'src/Savegame/AlienMission.cpp' 'Ufo *AlienMission::spawnUfo('
$escortStart = $spawn.LastIndexOf('if (ufo->isHunterKiller() && wave.escort)', [StringComparison]::Ordinal)
$escortEnd = $spawn.IndexOf('logUfo(ufo, game, this);', $escortStart, [StringComparison]::Ordinal)
if ($escortStart -lt 0 -or $escortEnd -le $escortStart) { throw 'Missing ordinary spawn escort fragment.' }
$template = $template.Replace('// SPAWN_ESCORT', $spawn.Substring($escortStart, $escortEnd - $escortStart))
$fiveSeconds = Read-ReferenceMethod 'src/Geoscape/GeoscapeState.cpp' 'void GeoscapeState::time5Seconds()'
$conditionStart = $fiveSeconds.IndexOf('if ((_timeSpeed', [StringComparison]::Ordinal)
$conditionEnd = $fiveSeconds.IndexOf('{', $conditionStart, [StringComparison]::Ordinal)
if ($conditionStart -lt 0 -or $conditionEnd -le $conditionStart) { throw 'Missing fast retarget condition.' }
$condition = $fiveSeconds.Substring($conditionStart + 3, $conditionEnd - $conditionStart - 3).Trim()
$template = $template.Replace('// FAST_CONDITION', $condition)
$work = Join-Path $repository 'artifacts/reference-strategic-hunting'
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $work 'strategic_hunting_probe.cpp'
$exe = Join-Path $work 'strategic_hunting_probe.exe'
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
$output = Join-Path $work 'strategic-hunting.actual.json'
[IO.File]::WriteAllText($output, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $output
