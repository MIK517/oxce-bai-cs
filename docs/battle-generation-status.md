# Battle generation status

Phase 7 branch 1 (`claude/battle-generation`), from `main` at `2b0c81fb`. Plan:
[Phase 7 branch 1](phase-7-plan.md#branch-1--battle-generation); decisions: [ADR 0038](decisions/0038-battle-aggregate.md).

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-18) |
| S1 battle aggregate | Done (2026-10-18) |
| S2 runtime battle terrain | Done (2026-10-19) |
| S3 map generation | Done (2026-10-24) |
| S4 X-COM deployment | Done (2026-10-24); the base defense deployment is S6 part 2 |
| S5 alien and civilian deployment | Done (2026-10-23); crashed-UFO and map-junk explosions refuse until branch 5 |
| S6 base defense and other entry points | Done (2026-10-24) |
| S7 acceptance and branch audit | Done (2026-10-24); the branch is complete |

## S0 — Planning (2026-10-18)

The [Phase 7 plan](phase-7-plan.md) splits the phase into eight branches and records the audit's Phase 7 checklist
(M4), the failure model (D1.3) and the live-state convention (D2) in [ADR 0038](decisions/0038-battle-aggregate.md).
The inherited refusals are assigned to owning branches in the plan's
[inherited refusals](phase-7-plan.md#inherited-refusals) section.

## S1 — Battle aggregate (2026-10-18)

Reference: `ConfirmLandingState::btnYesClick`, `GeoscapeState` (base defense), `ConfirmCydoniaState::btnYesClick`,
`BattlescapeGenerator::run` (deployment choice, turn limit, chrono trigger, cheat turn), `SavedBattleGame`
(constructor: turn 0, player side), `AlienDeployment::load` and `getDimensions`, `BattleUnit::MAX_SOLDIER_ID`,
`UnitFaction`.

- `Oxce.Gameplay/Battles`: `BattleState` (mission type, deployment, map size, turn, side, limits, revision, units),
  `BattleUnit` (stable ID, faction, controller, position, per-faction spotting), `BattleController`, `BattleViewer`.
  `IBattleQuery.QueryBattle(viewer)` returns the viewer's faction's units and the units it has spotted.
- `StartBattle` replaces the Phase 6 seam `TakeDeploymentRequest`. It picks the deployment as `run` does (the alien
  custom mission, else the UFO's deployment for a UFO target, else the mission type's), refuses before any change
  when the content has none (the reference throws in `Mod::getDeployment`), generates the battle and only then
  consumes the request (`ICampaignDeploymentHandOver`).
- While a battle exists, the `battle` preflight (order 121) stops time and a command gate refuses every other
  command with `battle.in-progress`; saving is refused with the named restriction `battle.save-unsupported`,
  which branch 2 retires.
- `RuntimeAlienDeploymentRule.Battle` projects the battle properties read so far: size, `turnLimit`,
  `chronoTrigger`, `cheatTurn` and the shade overrides, with the reference constructor's defaults.
- The landing hold now reads "A confirmed landing waits for its battle to start." (a modal hold: the player starts
  the battle). The Phase 6 refusal inventory stays the Phase 6 closure record and is not regenerated.
- The battle capability registry (ADR 0038) arrives with the first battle command; S1 has none.

Tests: `BattleStartTests` (hand-over, holds, save refusal, missing deployment, no request, linked defaults),
`BattleStateTests` (viewer filtering, spotting revisions, tile index, bounds). `StrategicLandingTests` no longer
takes the request through the retired seam. Unit and compatibility suites pass.

## S2 — Runtime battle terrain (2026-10-19)

Reference: `MapDataSet::loadData`, `MapData` (setters, `setBlockValue`, `setStopLOS`, `setHEBlock`),
`MCDPatch::load` and `modifyData`, `MapBlock::load`, `BattlescapeGenerator::loadMAP` (header and size checks) and
`loadRMP` (file), `FileMap::getIStream`.

- `RuntimeRuleCatalog.Battle` (`RuntimeBattleRules`) links the battlescape-only families as one group: terrains
  (`RuntimeTerrain`, `RuntimeMapBlock`, in rule order for `getTerrainList`) and MCD patches. The typed records are
  projected into runtime records because their collection types differ after a compiled-cache round trip, which the
  fresh/cache content comparison caught. Map scripts are projected in S3, map block items in S5.
- `IBattleTerrainFiles` is the host's port for terrain files. `VirtualBattleTerrainFiles` reads the installation's
  virtual file catalog (mod overrides included); the SDL app attaches it to new and loaded campaigns as
  `CampaignState.BattleTerrainFiles`, which is not state and is not saved. The content build already requires each
  terrain data set's MCD, PCK and TAB files to exist (resource resolution), so a missing data set fails at load.
- `BattleTerrainLoader` loads and keeps data sets and map blocks on demand and returns a reason for malformed
  content instead of throwing: a missing file (`getIStream` throws), a patch index past the data set
  (`MapDataSet::getObject` throws), more than 12 LOFTS in a patch (the reference writes past the array), a tile type
  outside the four parts after patching on an object that replaces another (narrowed in [S7](#s7-part-1--headless-acceptance-on-the-corpus-2026-10-24)),
  a MAP whose header differs from the block's size. The header is compared as
  signed chars, so a block side above 127 never matches, as in the reference.
- Intentional difference: a truncated MAP file is invalid content (the codec's check); the reference leaves the
  missing tiles empty.
- `McdTerrainRecord.BlockSmokeValue` exposes the raw byte: `setBlockValue` blocks smoke only when it is exactly 1.

Tests: `BattleTerrainLoaderTests` (field mapping and block values, patch order and LOFTS, caching, data set order,
trailing partial records, every malformed case, MAP and RMP checks). `StrategicReadinessTestContent.Load` can now
write extra installation files.

## S3a — Terrain map generation (2026-10-19)

Reference: `BattlescapeGenerator::run` (terrain, depth, shade and map script choice), `setDepth`, `generateMap`
(script loop, labels, conditionals, execution chances, executions, the level-0 floor fill), `init`, `selectPosition`,
`addBlock`, `removeBlocks`, `clearModule`, `pickTerrain`, `loadExtraTerrain`, `loadMAP`, `MapScript::load`, `init`,
`getGroupNumber`, `getBlockNumber`, `getNextBlock`, `RuleTerrain::getRandomMapBlock`, `getMapData`,
`getRandomMapScript`, `AlienDeployment::getRandomMapScript`, `Texture::load`, `getRandomTerrain`, `TerrainCriteria`,
`SavedBattleGame::initMap`, `Tile::setMapData`, `Tile::setDiscovered`, `MapBlock::MapBlock`.

- `BattleMap` holds the tiles as structure-of-arrays (map data ID and set per part, discovery bits), the loaded data
  sets and the per-block terrain names, block names and reinforcement marks. `BattleState` owns it, with the terrain,
  map script, depth and global shade.
- `StartBattle` needs `CampaignState.BattleTerrainFiles` (`battle.no-terrain-files` otherwise) and keeps one
  `BattleTerrainLoader` per attached file port. Generation runs the deployment's terrain choice (deployment terrains,
  else the mission texture's weighted criteria at the target, else the mod's first terrain), depth, shade overrides,
  map script choice (the deployment's own script wins when it exists; a named but missing script refuses), the globe
  terrain, then the map script. A failure throws `MapGenerationException`, which refuses with `battle.generation-failed`
  and puts the random state back (ADR 0038, amended).
- Commands: `addBlock`, `fillArea`, `checkBlock`, `removeBlock` and `resize` with labels, conditionals, execution
  chances (the roll is drawn even at 100), executions sharing one success flag (`removeBlock` overwrites it, the
  others accumulate), `randomTerrain`/`terrain` with `globeTerrain` and `baseTerrain`. The reference's quirks are kept:
  `addBlock`'s occupancy check skips the block's last row; `removeBlock` with block indices removes any placed block
  once a listed index is valid; `checkBlock` sees covered cells as the dummy block in group 0.
- `loadMAP`: the header height replaces the block's, too tall a block or stack refuses with the reference's messages,
  a block on the ground stacks on the highest existing floor, parts map across the terrain's data sets with the
  past-the-end fallback to set 0 object 0, an extra terrain clears the object part first, revealed floors are discovered.
- Runtime content: `RuntimeBattleRules.MapScripts` (`RuntimeMapScript`; "blocks" clears the groups as `MapScript::load`
  does), `RuntimeDeploymentBattle` terrains, map scripts, depth and custom UFO, `RuntimeGlobeTexture.Terrains` and
  `BaseTerrains` (`RuntimeTerrainCriterion.Pick`).
- Corrections found on the way: a deployment's own `script` defaulted to `DEFAULT` in the typed catalog, which would have
  overridden every terrain's script; the reference leaves it empty. An alien-base landing recorded mission texture 0
  for the reference's null texture; it is now -1.
- Not yet: base defense maps refuse until S6; map block items are S5.
- Intentional differences: the level-0 floor fill uses set 0 object 1 (the reference uses BLANKS object 1 by pointer,
  which is set 0 whenever BLANKS is loaded first); a rectangle outside the block grid is treated as occupied (the reference
  reads outside its arrays).

Tests: `BattleStartTests` (a generated map with block names, tile parts, scorched floors; resize, checkBlock, removeBlock
and conditionals; an unfilled map refused with the random state put back; missing deployment and terrain files; linked
defaults and map script projection), `RuntimeTerrainCriterionTests`, `BattleStateTests`.

## S3c — Routes (2026-10-19)

Reference: `BattlescapeGenerator::loadNodes`, `loadRMP`, `attachNodeLinks`, `Node::Node`.

- `BattleMap.Nodes` (`BattleNode`): ID, position, segment, type, rank, flags, reserved, priority and links; culled
  nodes stay as dummies so IDs line up. `initMap` (a resize) clears them.
- `loadNodes` gives every block cell the segment number current when it is visited and loads each placed block's
  RMP with that segment (the codec's offsets, culling and link termination); z counts down from the block's MAP height.
- `attachNodeLinks` runs both passes as the reference does: edge links joined through the neighbouring segment, then
  by block position with vertical links between nodes of one block on adjacent levels within three tiles. The upward
  test compares the level with the map height rather than the top level, as in the reference.

Tests: `BattleStartTests` checks segments, IDs across blocks, a culled node and its terminated link, and the east-west
join between two blocks.

## S3b, part 1 — addLine (2026-10-19)

Reference: `BattlescapeGenerator::addLine`, `MapScript::MapScript` (group defaults).

- `addLine` picks a free 10x10 position in the rectangles and needs every existing block along the line to be in the
  other road's group (up to 22 tries, then the command fails). Empty cells get a random block of the line's group;
  cells of the other road become a random crossing block in place (cleared and reloaded; the block names keep the
  road's, as in the reference). `both` draws the vertical line first and the horizontal one only if it succeeded.
  No block of the needed group is the reference's "addLine failed" exception, a generation refusal.
- Correction: the typed map-script defaults were vertical 1 and crossing 3; `MapScript`'s constructor uses
  `MT_NSROAD` (3) and `MT_CROSSING` (4). The horizontal default (`MT_EWROAD`, 2) was right.

Tests: `BattleStartTests.AddLineInBothDirectionsCrossesTheRoadsAndKeepsTheReplacedBlocksName`.

## S3b, part 2 — Craft and UFO maps (2026-10-19)

Reference: `RuleCraft::load` and `RuleUfo::load` (`battlescapeTerrainData`), `RuleTerrain::load`,
`BattlescapeGenerator::generateMap` (the addCraft and addUFO cases, `craftGroups`, the UFO and craft map and route loads
after the script, the craft's discovered tiles), `addCraft`, `loadMAP` (`_craftZ`, `_ufoZ`), `loadNodes` (the landing
zone), `Node::CRAFTSEGMENT` and `UFOSEGMENT`, `RuleStartingCondition::getCraftReplacement`, `RuleCraft::isMapVisible`.

- `battlescapeTerrainData` is read for crafts and UFOs (`InlineTerrainRule`): each node is a new terrain named by its
  `name`, loaded through the terrain loader, not merged with an earlier definition. `RuntimeCraftRule` and
  `RuntimeUfoRule` carry it as `BattlescapeTerrain`; the craft also gets `MapVisible` and `Groups`, and
  `RuntimeStartingCondition` its `CraftTransformations`.
- The generator gets the landing craft, the target UFO (UFO targets only), the deployment's `customUfo` and the
  starting condition (the deployment's, else the mission texture's). `addCraft` swaps the craft's rules by the command's
  `craftName` and then by the starting condition's transformation of the current craft, picks a random group-0 block
  of its terrain (any size up to 999), places it on a free position and fills its landing zone from the command's
  selection (group 1 by default); an unplaced craft refuses unless the command has a label. `addUFO` uses the command's
  UFO, else the battle's, else the custom UFO; a UFO terrain found earlier stays in use, as in the reference; an
  unplaced UFO is skipped when `canBeSkipped` or labelled. Commands with `craftGroups` run only for a deployed craft
  in one of them.
- After the script: nodes (the landing zone under a grounded craft or UFO loads no routes), then the UFO data sets and
  maps with their routes (segment 2000), then the craft's (segment 1000), each stacked on the highest floor below it
  and clearing the object part; the craft's area and border are discovered as the craft's `mapVisible` says. The block
  names there are the craft or UFO type and its map block.
- Craft skins followed in S3, part 5 (below).

Tests: `BattleStartTests.TheCraftAndTheUfoLandOnLandingZonesAndTheirMapsAndRoutesLoadAfterTheScript` and the inline
terrain links in `DeploymentBattlePropertiesAreLinkedWithTheReferenceDefaults`.

## S3b, part 3 — digTunnel (2026-10-19)

Reference: `BattlescapeGenerator::drillModules`, `addBlock` (drill edges), `MapScript::load` (`tunnelData`),
`TunnelData`, `MCDReplacement`, `MapData::getTUCost`.

- `addBlock` marks drill edges as the reference does (south edge vertical, east edge horizontal, the far corner both).
- `digTunnel` opens each marked edge between two occupied cells on the tunnel level across the first rectangle's span
  (3, 3, 3, 3 without one): walls and objects go, objects on the far side go only when they cost no walking time, the
  floor replacement is laid and the wall and corner replacements close the sides. The command always succeeds.
- `RuntimeMapScriptCommand.Tunnel` (`RuntimeTunnelData`) parses `tunnelData` (level 0, replacements with set and entry
  -1 by default); a malformed node, a missing tunnelData, a replacement past the terrain's data sets or a side tile
  outside the map refuse generation (the reference throws or crashes).
- Intentional difference: replacements are recorded at the terrain's loaded data set offset. The reference stores the
  terrain-relative set index while pointing at the right object, so its saved IDs only agree for the deployment terrain.

Tests: `BattleStartTests.DigTunnelOpensTheMarkedEdgeBetweenTwoBlocks`.

## S3b, part 4 — Vertical levels (2026-10-20)

Reference: `BattlescapeGenerator::generateMap` (levels per execution), `populateVerticalLevels`, `loadVerticalLevels`,
`addCraft` (the first level's landing zone), `addLine` (placed positions), `removeBlocks` and `loadNodes` (level
segments), `init`, `MapScript::initVerticalLevel`, `VerticalLevel::load`.

- `RuntimeMapScriptCommand.VerticalLevels` (`RuntimeVerticalLevel`) parses `verticalLevels` as `VerticalLevel::load`
  does: entries without a type are skipped, an unknown type is middle, "blocks" clears the groups, size defaults
  (1, 1, -1) with a decoration's height 0, `maxRepeats` -1. A malformed node refuses generation when its command runs.
- Each execution repopulates the working levels (ground first, group 1 by default for a craft or UFO; then the craft
  level used once, the line level, and every middle, empty and decoration level; then the ceiling) and starts the
  command's selection from the first level, or from the line level for `addLine`. `addBlock`, `fillArea`, `addLine`,
  `addCraft` and `addUFO` record their placed positions and stack levels on each: the ground or line level already
  placed, the ceiling reserved first if it fits, then fillers in turn while they fit (20 misses at most). An `addLine`
  whose first level is not its line re-places the ground and loads the line block as a filler. A craft level raises
  the craft or UFO. Each loaded level is a route segment after the ground segments; `removeBlock` and `resize` drop them.
- `MapScriptSelection.InitVerticalLevel` replaces the command's own selection, as `initVerticalLevel` does, for the rest
  of the command's executions. In the reference the change also outlives the battle, because map scripts are shared
  mod data; the port keeps it to one generation (intentional difference).
- A block's height is its MAP header's once any battle has read the block, else its rule's (`MapBlock::getSizeZ` after
  `setSizeZ`). The loader that keeps read blocks lives with the campaign; the reference's blocks live with the mod.
- Deterministic choices where the reference misbehaves: no levels left for the command (it dereferences an empty list)
  and a first level without a block or size refuse; no ceiling is reserved when the ground was the only level (it reads
  past the list); a filler position past the list after the ceiling is reserved wraps to the first filler; levels that
  never add height stop after 10,000 rounds with a refusal (the reference loops forever).

Tests: `BattleStartTests.VerticalLevelsStackTheGroundAFillerAndTheReservedCeiling` (stacked floors, level segments and
the vertical links between them).

## S4a — X-COM roster and placement (2026-10-20)

Reference: `BattlescapeGenerator::deployXCOM` (unit order), `addXCOMVehicle`, `addXCOMUnit` (craft start tiles, the
craft rule's deployment, spawn nodes), `canPlaceXCOMUnit`, `setCustomCraftInventoryTile`, `placeUnitNearFriend`,
`loadMAP` (map block inventory tiles), `SavedBattleGame::getSpawnNode`, `setUnitPosition`, `Tile::getTUCost`,
`hasNoFloor`, the tile cache's terrain level, `RuleItem::getVehicleUnit`, `Mod::EXTENDED_HWP_LOAD_ORDER`.

- After the map, the craft's vehicles and soldiers become player units controlled by player slot 0, in the reference's
  order: 2x2 vehicles (all vehicles without `extendedHwpLoadOrder`), soldiers in 2x2 armor, the remaining 1x1
  vehicles, then 1x1 soldiers, soldiers in base order. Soldiers keep their IDs; vehicles are numbered from
  `BattleUnit::MAX_SOLDIER_ID`. The first unit placed is the selected unit.
- Placement: with a craft map, the craft rule's `deployment` points when it has them, else the first start-point tile in
  tile order that takes the unit; without one, a random X-COM spawn node (rank 1) of the highest priority that suits the
  unit, with a random facing. `setUnitPosition`'s checks apply (inside the map, no other unit, passable, a floor unless
  flying, no low big wall, raised tiles lift the unit). The first start tile checked, the craft rule's inventory tile or
  a random map block inventory tile becomes `BattleState.CraftInventoryTile`. A unit that finds no place stays out; no
  unit at all refuses ("no xcom units could be placed on the map").
- `BattleMap` keeps which unit stands on each tile; `BattleUnit` gains its type, soldier ID, armor, size, movement type
  and facing. Runtime content: the craft's `deployment` and inventory tile, the armor's movement type,
  `extendedHwpLoadOrder`, the deployment's `enviroEffects`, the enviro effects' armor transformations and the starting
  condition's armor and equipment restriction flags.
- Named S4b refusal: starting-condition vehicle and item restrictions (they send vehicles and items home), which arrive
  with the craft's items. Large units, `placeUnitNearFriend` and armor changes followed in S4b parts 1 and 2 (below).
- Left for S4b, all done by part 7: the save-side custom deployments (`customRuleCraftDeployments`, a craft's custom soldier and vehicle
  deployment), which the strategic save does not read yet; the craft's items and their equipment layouts; unit
  statistics.

Tests: `BattleStartTests` (the craft's soldier on the first start tile and inventory tile; a soldier on an X-COM spawn
node without a craft map); the route expectations follow the X-COM spawn node added to the test block.

## S4b, part 1 — Large units and placement next to a friend (2026-10-20)

Reference: `SavedBattleGame::setUnitPosition` (the direction checks for large units), `placeUnitNearPosition`,
`BattlescapeGenerator::placeUnitNearFriend`, `Pathfinding::isBlockedDirection` and `isBlocked` (wall and big wall parts),
`Pathfinding::getMovementType`.

- `BattlePathing.IsBlockedDirection` ports the wall checks of `isBlockedDirection` for all eight directions: west and north
  walls with impassable wall parts, big walls in the object part and the neighbours' facing big walls, the map edge. The
  floor part (units in the way, falling), missiles and open UFO doors belong to pathfinding in branch 4; no generation
  path asks for them. `strictBlockedChecking` is off, its default, until the port has battle options.
- A unit larger than one tile is placed only where no wall lies between its tiles (east, south-east, south).
- Without spawn nodes, a unit is placed next to a random placed friend at least as large (ten rounds of up to 100 draws):
  on the friend's tile, then the eight neighbours not walled off from it, then one level up for a flyer over open air.
  A neighbour whose halfway tile is outside the map is skipped (the reference would read a missing tile).

Tests: `BattlePathingTests`; `BattleStartTests.VehiclesComeFirstAndALargeVehicleTakesFourStartTiles`.

## S4b, part 2 — Battle armor (2026-10-20)

Reference: `BattlescapeGenerator::deployXCOM` (the armor pass), `RuleEnviroEffects::getArmorTransformation`,
`RuleStartingCondition::getArmorReplacement`, `WeightedOptions::choose`, `Mod::getRule` (an empty name is no rule).

- Each soldier's battle armor: the environment's transformation (the deployment's `enviroEffects`, else the terrain's)
  when it is no larger, else for an armor the starting condition does not allow, a weighted pick from its
  `defaultArmor` for the soldier type ("noChange" keeps it), when it is no larger. The pick draws in base order, before
  the soldiers are placed, as the reference's pass does.
- Intentional difference: the reference sets the geoscape soldier's armor for the battle and restores it in the
  debriefing; the port gives the battle unit the armor (`BattleUnit.ArmorId`, with `OriginalArmorId`) and leaves the
  soldier unchanged. Branch 2 writes the reference's form into battle saves.
- Runtime content: the starting condition's allowed and forbidden armors and default armor.

Tests: `BattleStartTests.TheEnvironmentOrTheStartingConditionChangesTheSoldiersBattleArmor`.

## S4b, part 3 — Battle items and the craft's cargo (2026-10-20)

Reference: `SavedBattleGame::createItemForTile`, `getCurrentItemId`, `BattleItem::BattleItem`, `Mod::getInventoryGround`,
`BattlescapeGenerator::deployXCOM` (the craft's items and their X-COM property).

- `BattleItem`: ID in creation order, item type, tile or carrying unit, inventory slot and position, ammo quantity (the
  clip size to start), fuse timer (-1), X-COM property. `BattleState.Items` and `NextItemId`.
- After the X-COM units, every item the craft carries becomes battle items on the ground of the craft inventory tile
  (none on the map when there is no inventory tile), marked as X-COM property. The mod must have an `STR_GROUND`
  inventory (the reference throws without one).
- Intentional difference: the reference walks the craft's item map in `RuleItem` pointer order, an allocation detail;
  the port uses the item rules' order, which that follows in practice.
- Not yet (the rest of S4b): vehicle weapons and ammunition (`addXCOMVehicle`'s `createItemForUnit`, inventory fitting),
  which come before the craft's items and so shift their IDs once added; medikit quantities; equipment layouts
  (`placeItemByLayout`, the soldiers' saved layouts), `loadWeapons` and `autoEquip`; unit statistics.

Tests: `BattleStartTests.TheCraftsItemsLieOnTheCraftInventoryTileAsXcomProperty`.

## S4b, part 4 — Unit statistics and built-in equipment (2026-10-21)

Reference: `BattleUnit::BattleUnit` (soldier and unit-rule constructors: statistics, time units, energy, health, mana,
morale), `Soldier::prepareStatsWithBonuses`, `getBonuses`, `UnitStats::obeyFixedMinimum`, `SavedBattleGame::initUnit`,
`addFixedItems`, `createItemForUnit`, `createItemForUnitSpecialBuiltin`, `BattleUnit::setSpecialWeapon`, `addItem`,
`fitItemToInventory`, `getItem`, `getRightHandWeapon`, `getCarriedWeight`, `Inventory::overlapItems`,
`RuleInventory::fitItemInSlot`, `getCost`, `BattleItem::BattleItem`, `setAmmoPreMission`, `setAmmoForSlot`,
`haveAnyAmmo`, `getAmmoForAction`, `getTotalWeight`, `RuleItem::getVehicleClipAmmo`, `getSlotForAmmo`,
`canBePlacedIntoInventorySection`, `BattlescapeGenerator::addXCOMVehicle`, `addXCOMUnit`.

- Runtime content: `RuntimeBattleRules.Equipment` (`RuntimeBattleEquipment`): inventory sections in `Mod::getInvsList`
  order with their slots and move costs; the item fields the strategic projection lacks (fixed weapon, default slot and
  position, inventory size, supported sections, turret type, psi and mana requirements, rechargeable ammo, medikit
  quantities, categories after replacement, the actions' ammo slots); armor built-in, special and self-destruct items,
  two main weapons, inventory and weight; unit statistics, built-in and weighted weapon sets, melee and psi weapons,
  living weapon and race; soldier special weapons; category inventory orders; weapon sets.
- `BattleUnitStats` (twelve 16-bit statistics, wrapping sums, the fixed minimum). A soldier's statistics are its current
  statistics plus its soldier bonuses plus the battle armor's; health and mana subtract what is missing, and a wounded
  soldier starts at morale 75 with the wound recovery off its health. A vehicle has its unit rule's statistics plus its
  armor's.
- `UnitEquipment`: each placed unit gets its special built-in items (owned, in no section; psi items skipped without
  psi skill; a firearm or melee special weapon without a clip size refuses as the reference throws), then the armor's
  built-in weapons, the unit's built-in set for item level 0, a weighted weapon set, and a living weapon, ammunition
  after the rest. `addItem` is ported whole: the weight and duplicate checks for standard player units, fixed weapons
  in their default slot or a hand, psi and mana requirements, C1 hands, loading ammunition into a hand weapon, C2
  psi-amps for hostiles, D1 default slot, D2 category order, the cheapest storage section and the C3 fallback.
- Vehicles: after placement, the fixed weapon (turret type from it), then its clip ammunition, loaded into the weapon
  with the vehicle's ammo. The deployment request now records each vehicle's ammo (`VehicleAmmo`, saved as
  `vehicleAmmo` in the port-only request node and checked against the vehicle count on load).
- Items get IDs in the reference's order: an item that fits nowhere still uses its ID. Medikits start with their heal,
  pain killer and stimulant quantities; weapons fill the ammo slots that take no ammunition with themselves.
- Refusals where the reference crashes or fails to load: a missing hand inventory, default slot or category section; a
  missing move cost; a missing item, armor or weapon set; a living-weapon race shorter than four characters; a vehicle's
  fixed ammo slot outside its ammo slots; the C3 fallback leaving an item in a section after reporting it unplaced
  (the reference keeps a freed item); a vehicle with clip ammunition and no recorded ammo (since S6 part 2 a base defense loads its stored vehicles
  with `getVehicleClipSize`, and since S7 part 2 an older request takes the craft's ammo, so this is only a guard).
- Not ported here: the CreateUnit, CreateItem and ApplySoldierBonuses script hooks (tactical script bindings, branch 8);
  `getBonuses`' listOrder insertion quirk, which can keep two bonuses with the same list order (the port keeps each
  bonus once).
- Equipment layouts, `loadWeapons` and `autoEquip` follow in part 5.

Tests: `BattleStartTests.AVehicleCarriesItsSpecialItemItsFixedWeaponAndTheWeaponsLoadedAmmo`,
`ASoldiersArmorBuiltInItemsFillTheHandsTheWeaponAndTheCheapestSection`.

## S4b, part 5 — Equipment layouts and the craft's cargo distribution (2026-10-21)

Reference: `Soldier::load` (equipment layout), `EquipmentLayoutItem::load`, `BattlescapeGenerator::deployXCOM` (after the
craft's items), `placeItemByLayout`, `reloadFixedWeaponsByLayout`, `loadWeapons`, `autoEquip`, `BattleUnit::addItem`
(allowInfinite, testMode), `BattleItem::moveToOwner`, `haveAllAmmo`.

- Part 5a: `SoldierPersonalState.EquipmentLayout` (`SoldierLayoutItem`: item, section, position, the four ammo slots,
  fuse timer, fixed) is read from the soldier's `equipmentLayout`; an entry naming a missing item, section or ammunition
  is dropped as `Soldier::load` logs and skips it. The save keeps writing the soldier's own node (layouts are not
  edited in the port yet). Campaign options gain `disableAutoEquip`, `oxceAlternateCraftEquipmentManagement`,
  `preprimeGrenades` and `includePrimeStateInSavedLayout`.
- Part 5b: `XcomCargo` after the craft's items: every tile item becomes X-COM property and goes by layout (ammunition
  waits unless the alternate craft equipment management option is on), fixed weapons load their layout ammo, unfixed
  ground weapons load in load order, extra ammunition follows the layouts, then `autoEquip` (strongest soldier first,
  heaviest item first, one best-scoring item per soldier per pass, flares skipped in light, pre-primed grenades by the
  option). `addItem` gains allowInfinite and test mode; as in the reference, test mode still moves a fixed weapon into
  its default slot and still loads ammunition into a hand weapon. Moving an item to an owner takes it off its tile.
- Intentional differences: `std::sort` leaves equal load orders, strengths and weights unspecified; the port keeps the
  tile's and the battle's order. Unit inventories are the owned items in creation order rather than the order they
  arrived; nothing in generation depends on that order.
- Refusal: `reloadFixedWeaponsByLayout` walks the live tile inventory while loading erases from it; a fixed weapon that
  would load a second layout ammo item after the first (reading past the shrunken vector in the reference) refuses.
- Not ported: `BattleUnit::setInventoryTile` and the units' visibility reset (presentation, branch 3).

Tests: `CampaignSaveRegressionFixtureTests.LayoutEntriesNamingMissingRulesAreDroppedOnLoadWhileTheSoldierNodeKeepsThem`,
`BattleStartTests.ASoldierWithALayoutTakesItsLayoutWeaponLoadedWithItsLayoutAmmo`,
`ASoldierWithoutALayoutIsAutoEquippedFromTheLoadedGroundItems`,
`TheCraftsItemsStartOnTheCraftInventoryTileAndALayoutlessSoldierTakesThem`.

## S5 groundwork — Deployment data and alien quantities (2026-10-20)

Reference: `AlienDeployment::load` (`data`), the `DeploymentData` and `ItemSet` readers, `BattlescapeGenerator::deployAliens`
(race and quantity per group), `AlienRace::getMember`, `Mod::isDemigod`.

- `RuntimeDeploymentBattle.Data` (`RuntimeDeploymentData`): alien rank or custom unit type, low, medium and high
  quantities, `dQty`, `extraQty`, the chance to deploy outside the UFO, item sets and extra random items. A malformed
  `data` node is kept as `DataInvalid`, for the deployment to refuse when it runs.
- `AlienQuantities.Quantity`: the difficulty's base quantity, then the `dQty` and `extraQty` extras, their maximum with
  `difficultyDemigod` (now in `RuntimeCampaignSettings`) and otherwise random, drawn in that order.
- `AlienRaces.Resolve` (the deployment's race, else the battle's, with the `_UNDERWATER` variant below the surface; an
  unknown race refuses) and `AlienRaces.Member` (`AlienRace::getMember`: the rank's random set when the race has random
  sets, else the rank's member; a missing rank refuses). `RuntimeAlienRaceRule` gains `Members` and `RandomMembers`.
- Not yet: placement on nodes inside or outside the UFO, alien units and their items by item level, civilians and
  objectives; deployAliens runs after the X-COM deployment in S5.

Tests: `AlienQuantitiesTests`, `AlienRacesTests`; the deployment data projection in `BattleStartTests`.


## S5, part 1 — Alien units (2026-10-21)

Reference: `BattlescapeGenerator::run` (alien item level, the alien custom deployment, the "no alien units" check),
`deployAliens`, `addAlien`, `Node::nodeRank`, `SavedBattleGame::createTempUnit`, `BattleUnit::adjustStats`,
`UnitStats::percent`, `Mod::getStatAdjustment`, `getAlienItemLevels`, `TileEngine::faceWindow`, `BattleUnit::directionTo`,
`Position::distance2d`, `Mod::loadFile` (`alienItemLevels`, `statGrowthMultipliers`, `statGrowthMultipliersAbs`,
`aimAndArmorMultipliers`, `aimMultipliers`, `armorMultipliers`, `armorMultipliersAbs`, `extendedForceSpawn`).

- Settings: `alienItemLevels`, the five difficulties' stat adjustments and `extendedForceSpawn` are parsed into the
  campaign start settings and projected (`RuntimeCampaignSettings.AlienItemLevels`, `StatAdjustments`,
  `ExtendedForceSpawn`). Deployments project `forcePercentageOutsideUfo` and their civilian fields.
- `BattlePlacement` holds what X-COM and alien placement share (spawn nodes by rank, setUnitPosition's checks, placing
  near a friend).
- `AlienDeployment`: the alien custom deployment when it has data, else the battle's; the race (deployment's, else the
  battle's, `_UNDERWATER` when deep); reinforcement deployment, race and item level recorded; the alien item level is
  the month capped at the mod's last. Per group and alien, in the reference's draw order: the quantity, the damaged-UFO
  skip, the race member or custom unit, the outside chance (only with a UFO unless forced), the spawn node (outside: rank
  0 only; else the rank's seven node ranks), the item level draw, then `initUnit` with that item level and the group's
  item set and extra random items. A placed alien faces a window, or the first unit when within 20 tiles and the
  difficulty's odds hit, else a random direction; without a node it is placed near a friend on demigod or with
  `extendedForceSpawn`. Hostile statistics: the unit's plus its armor's, then percentage growth times the difficulty
  coefficient, the firing multiplier and absolute growth. Unit IDs continue after the X-COM vehicles.
- `BattleUnit.Rank` and `StartNodeId` (the AI start node); `BattleState.AlienItemLevel` and the reinforcement fields.
- Refusals: a missing alien item level row or one shorter than ten; a
  negative alien rank (the reference indexes before the table); an unknown unit.
- Fixed: the cargo distribution's flare check used the request's shade instead of the battle's world shade.
- Not yet: the armor values `adjustStats` scales (armor arrives with combat, branch 5), civilians, objectives, UFO power
  sources, the weapon pile option, crashed-UFO damage.

Tests: `BattleStartTests.AliensSpawnOnNodesByRankWithDifficultyStatisticsAndTheirItemSet`; every battle scenario now
deploys an alien, as a generated battle must have one.


## S5, part 2 — Civilians (2026-10-23)

Reference: `BattlescapeGenerator::run` (civilians before the aliens for a positive civilian spawn node rank, after them
for rank 0, none for a negative rank), `deployCivilians`, `addCivilian`.

- `CivilianDeployment`: the deployment's `civilians` (half the maximum rounded down to the maximum) of the terrain's random
  civilian types, then each `civiliansByType` entry in std::map order (half rounded up), as neutral AI units on spawn
  nodes of the civilian rank or near a friend, facing a random direction, VIPs when the deployment says so, then
  `initUnit` with an item level drawn from the alien item levels (only for a placed civilian, as in the reference).
- The alien item level is now set before either deployment, as `run` does.
- `BattleUnit.IsVip`; deployments project `markCiviliansAsVIP`.
- Refusal: civilians without a type on a terrain with no civilian types (the reference reads past the empty list).

Tests: `BattleStartTests.CiviliansOfAPositiveSpawnRankComeBeforeTheAliensAsVips`.


## S5, part 3 — Map block items (2026-10-23)

Reference: `MapBlock::load` (`items`, `fuseTimers`, `randomizedItems`, `extendedItems`), `RandomizedItems`,
`ExtendedItems`, `Position` and `std::pair` YAML reading, `BattlescapeGenerator::loadMAP` (items, `_generateFuel`).

- `RuntimeMapBlock.Items` (`RuntimeMapBlockItems`): items by type in std::map order with their positions, fuse timer
  ranges, randomized items and extended items with their ammo definitions; a malformed node is recorded and refuses
  when the block is loaded.
- `loadMAP` records each block's items after its tiles, drawing the fuse timers and random picks in the reference's
  order; a block with an items array turns off algorithmic fuel. The battle creates them in that order before the X-COM
  deployment, so they take the first item IDs; an extended weapon's ammo is created on the tile, its quantity lowered
  when asked, and loaded into the weapon's compatible slot.
- Refusals where the reference throws: an unknown item, a corpse item, a position outside the block, ammo that is not
  compatible or a slot already loaded; also a reversed fuse timer range on a block's items array (RNG::generate's
  range is undefined then).

Tests: `BattleStartTests.MapBlockItemsTakeTheFirstItemIdsWithTheirFuseTimersAndLoadedAmmo`.


## S5, part 4 — Objectives, the weapon pile and power sources (2026-10-23)

Reference: `BattlescapeGenerator::setupObjectives`, `run` (after the civilians), `sendItemsToLimbo`, `fuelPowerSources`,
`explodePowerSources`, `explodeOtherJunk`, `Mod::getAlienFuelName`, `getBughuntMinTurn`.

- Settings: `alienFuel` (a [name, quantity] pair) and `bughuntMinTurn` (999 by default). Deployments project
  `objectiveType`, `objectivesRequired`, `bughuntMinTurn`, `escapeType`, `vipSurvivalPercentage` and `noWeaponPile`.
- `setupObjectives`: bug hunt off, the deployment's minimum bug hunt turn when set (else the mod's), the VIP survival
  percentage and escape type, and the objective type and count when map parts of that special type exist (all of
  them, or the required number when fewer).
- With `noWeaponPile`, the items left on the craft inventory tile and their loaded ammo leave the map and the battle's
  items for `GuaranteedRecoveredItems` (by ID), fuse timers reset. Unless a loaded block had an items array, each UFO
  power source (object special type 2, in tile order) gets the alien fuel item.
- Refusals until explosions exist (branch 5): a crashed UFO whose map has power sources (`explodePowerSources`), and
  map grenades primed with fuse timer -2 that are neither X-COM property, special weapons nor spawners
  (`explodeOtherJunk`); also alien fuel that names no item when a power source needs it (the reference throws).

Tests: `BattleStartTests.ObjectivesCountTheTargetPartsPowerSourcesGetFuelAndTheWeaponPileGoesHome`,
`MapGrenadesPrimedToExplodeAtTheStartRefuseUntilExplosionsExist`.


## S4b, part 6 — Starting-condition vehicle and item restrictions (2026-10-23)

Reference: `BattlescapeGenerator::deployXCOM` (disabled vehicles and items), `RuleStartingCondition::isVehiclePermitted`,
`isItemPermitted`, `RuleItem::getVehicleClipsLoaded`, `Mod::getShareAmmoCategories`.

- Starting conditions project their allowed and forbidden vehicles, items and item categories; `shareAmmoCategories`
  is parsed. `isItemPermitted`: the item lists decide first, then the item's categories (with the craft's primary ammo
  categories for a firearm when ammo shares categories) against the forbidden or else the allowed categories.
- A vehicle that is forbidden, or whose clip ammunition is, stays out of the battle and goes back to base storage with
  `getVehicleClipsLoaded` clips (no unit ID is used); a forbidden craft item goes back to base storage.
- `BattleState.ReturnedToBase` records them during generation; the campaign adds them to the base's storage only when
  the battle starts, so a refused generation changes nothing. The craft keeps its own record, as in the reference
  (the debriefing settles it).

Tests: `BattleStartTests.TheStartingConditionSendsDisabledVehiclesAndItemsBackToBaseStorage`,
`RuntimeStartingConditionTests`.

## S4b, part 7 — Save-side custom deployments (2026-10-24)

Reference: `Craft::load` (`customSoldierDeployment`, `customVehicleDeployment`), `read(VehicleDeploymentData)`,
`Craft::hasCustomDeployment`, `resetTemporaryCustomVehicleDeploymentFlags`, `SavedGame::load`
(`customRuleCraftDeployments`), `SavedBattleGame::hasCustomDeployment`, `getCustomDeployment`,
`BattlescapeGenerator::deployXCOM`, `addXCOMUnit`, the `std::pair` and `Position` YAML readers.

- `CraftLogisticsState.CustomSoldierDeployment` (soldier ID → `CraftDeploymentPoint`) and `CustomVehicleDeployment`
  (`CraftVehicleDeployment`: type and point, a missing `pos` is [-1, -1, -1]), written back from the state only when not
  empty, as `Craft::save` does. `Craft::resetCustomDeployment` clears both when a soldier is assigned to the craft
  (`AssignSoldierToCraft`), a crew soldier changes to a larger armor (`EquipSoldierArmor`) or a vehicle is added
  (`ChangeCraftVehicle`). Setting a deployment (the battle's deployment preview) is UI work for later branches.
- `CampaignSnapshot.CustomRuleCraftDeployments`: read only, the save keeps its loaded node (nothing in the port changes
  it; the preview's `saveDummyCraftDeployment` would).
- `addXCOMUnit` with a deployed craft: the craft's own custom deployment when it has one and its rules are the battle's
  craft rules (a soldier takes its point, a vehicle the first unused point of its unit type); else the save's override
  for the battle's craft type, even an empty one, else the craft rule's points; else the start tiles. A unit that fits
  none of its custom points stays out of the battle, as in the reference. Positions wrap to Sint16 like `Position`.
- Refusals where the reference fails or goes undefined: a soldier without a point in the craft's custom deployment
  (`std::map::at` crashes), a vehicle without an unused point of its type ("custom craft deployment failed"), and a
  custom direction outside 0-7 (`setDirection` keeps it and later lookups index by it).

Tests: `BattleStartTests.TheCraftsOwnCustomDeploymentPlacesSoldiersByIdAndVehiclesByTypeInOrder`,
`TheSavesCraftTypeDeploymentOverridesTheRuleAndAUnitThatFitsNoPointStaysOut`,
`TheCraftRulesPointsApplyWithoutAnOverrideAndAReplacedCraftRuleIgnoresTheCraftsOwnDeployment`,
`CustomDeploymentsAreReadFromTheSaveAndWrittenBackAsTheyWere`, `ACustomCraftDeploymentWithoutAPointForAUnitIsRefused`;
`StrategicReadinessFixtureTests.AddingCrewALargerArmorOrAVehicleResetsTheCraftsCustomDeployment`.

## S3, part 5 — Craft skins (2026-10-24)

Reference: `RuleTerrain::refreshMapDataSets`, `BattlescapeGenerator::generateMap` (the craft's data sets), `Craft::load`
and `Craft::save` (`skinIndex`), `RuleCraft::load` (`maxSkinIndex`), `Mod::getMapDataSet`.

- `RuntimeCraftRule.MaximumSkinIndex`; `CraftLogisticsState.SkinIndex`, read from the craft node (a skin above the
  rule's maximum, even a negative one, falls back to 0) and written only when nonzero.
- The landing craft's skin renames the battle's craft terrain data sets, except `BLANKS`, to `name_skin` before they
  load; the terrain is the battle's craft rule's (possibly replaced by `addCraft` or the starting condition), the skin
  the craft's own. MCD patches of the skinned name apply. The port derives the names per battle, where the reference
  renames the shared rule from its last skin ([ADR 0038](decisions/0038-battle-aggregate.md)).
- A skin whose data set has no MCD refuses with the missing file, where the reference throws on loading it.

Tests: `BattleStartTests.ASkinnedCraftLoadsItsSkinsDataSetsExceptBlanks`,
`TheCraftsSkinIndexIsReadUpToTheRulesMaximumAndWrittenBack`.

## S6, part 1 — Base defense maps (2026-10-24)

Reference: `BattlescapeGenerator::generateMap` (the base defense branch and `baseDefenseMapFromLocation`),
`generateBaseMap`, `SavedBattleGame::calculateModuleMap`, `getStorageSpace`, `BaseFacility::isBuiltOrHadPreviousFacility`,
`RuleBaseFacility::load` (`verticalLevels`, `storageTiles`), `Texture::getRandomBaseTerrain`, `Mod::getTerrain`,
`RuleTerrain::getMapBlock`, `GeoscapeState` (~3700, the base defense's textures).

- Facility rules keep `verticalLevels` (parsed as a map script command's, the last mod's node replacing the earlier one;
  no longer deferred) and project them with `storageTiles` onto `RuntimeFacilityRule`; `baseDefenseMapFromLocation` is
  a global setting (`RuntimeCampaignSettings.BaseDefenseMapFromLocation`).
- `BattleMapGenerator.GenerateBaseMap` runs before the script: each built facility (or one that had a previous
  facility), in base order, places its blocks: its vertical levels as an addBlock-like command (`MapScriptCommandType.Undefined`)
  at its position, else per cell, in row order, the block named by the map name's last two characters read with
  `atoi` plus the cell number. Its cells lose their drill marks except along its south and east edges. A storage
  facility adds `StorageSpace` tiles: the rule's `storageTiles` (checked by tens, as the reference does; a
  (-1, -1, -1) entry ends the list as placed), else the checkerboard on each cell's ground level, whose first cell
  sets the inventory tile at its centre one level down (none when that is below the map). Then the module map
  (`BattleState.BaseModules`, both counts starting from 1, (-1, -1) for blocks without base module objects; a module
  tile past the last whole block of a map side that is not a multiple of 10 refuses, though since S6 part 3 the block
  that would put it there refuses first, as it runs past the map's side). The
  script fills and drills the rest; `generateBaseMap` itself clears nothing.
- With `baseDefenseMapFromLocation: 1` the base terrain comes from the base's globe texture (`baseTerrain`) and the whole
  map draws from a `SplitMix64` stream seeded with `lon * lat * 1e6` (a zero coordinate counts as 1); the campaign's
  stream is untouched, as the reference restores its seed ([ADR 0038](decisions/0038-battle-aggregate.md)).
- `BattleGenerator.GenerateMap` is the map half of `Generate` (tests read the map before units exist); the base's
  inventory tile, storage space and module map go on the `BattleState`.
- Refusals: a facility outside the map's block grid, a map name under two characters, facility vertical levels that are
  malformed, leave no level, or have no block on the first level, a map block the terrain lacks, storage tiles that place
  nothing, a missing globe texture or base terrain with the location setting. The base defense deployment itself refuses
  in `XcomDeployment` (`BaseDefenseDeployment`, Phase 7 branch 1 S6) after the map is generated.

Tests: `BaseDefenseMapTests` (layout, block names, drilling, the checkerboard's exclusions and map edges, rule storage
tiles and the lone (-1, -1, -1), the inventory tile and its absence, module map, the location seed and base terrain,
the campaign stream left alone, the seed formula, each reachable refusal, rule loading). The refusals for a request
whose base is gone and a base defense without a base layout guard states the campaign cannot reach.

## S6, part 2 — Base defense deployment (2026-10-24)

Reference: `BattlescapeGenerator::deployXCOM` (the `_base` without `_craft` branches, the dummy unit, the base's stores and
the crafts' items), `addXCOMVehicle`, `addXCOMUnit`, `Base::setupDefenses`, `Soldier::hasFullHealth`, `canDefendBase`,
`RuleItem::isInventoryItem`, `isUsefulBattlescapeItem`, `canBeEquippedBeforeBaseDefense`, `canBeEquippedToCraftInventory`,
`getVehicleClipSize`, `SavedGame::isResearched`, `SavedBattleGame::getBaseStorageItems`.

- `XcomDeployment.RunBaseDefense` (a base defense request has no craft): the vehicles `setupDefenses` collects, the
  crafts' that are not out with their own ammo, then the stored ones loaded with `getVehicleClipSize`
  (`CampaignWorld.StoredDefenders`, now static), all before the soldiers and without the extended 1x1 split; then the
  base's soldiers in base order with deployXCOM's own condition (full health or `canDefendBase`, not on a craft that is
  out), 2x2 before 1x1. That differs from `Base::getAvailableSoldiers`, which the request's `Soldiers` follow: a craft's
  wounded crew is in the request but stays out of the battle. Placement is the spawn-node path (no near-friend
  fallback).
- Nobody placed: a summoned player unit (`BattleUnit.SummonedPlayerUnit`) of the first race's first member, with its
  unit and armor statistics and no equipment, on tile (0, 0, 0), which becomes the inventory tile and the selected unit.
  No race (`NoRaceForDummy`; the reference takes `front()` of an empty list) or a member that is no unit with an armor
  refuses. The first can't be reached from the campaign: the attacking mission's race exists. A base
  defense whose deployment has no terrains under a texture with terrains refuses too: `run` picks the texture's terrain
  at the UFO or the mission site, and a base defense has neither (a null dereference in the reference).
- Items: the base's stores that are allowed before a base defense, inventory items (`RuntimeBattleItem.BigSprite` above
  -1 and not fixed), useful in battle or craft-equippable (`IgnoreInCraftEquip`), with researched requirements (debug mode
  counts), then the items of the crafts that are not out (unfiltered), on the inventory tile as X-COM property, in item
  rule order; then the usual layouts and auto-equip (`XcomCargo`). No starting-condition check applies to them or to the
  vehicles, as in the reference.
- `BattleState.BaseStorageItems` (the debriefing's record): the base's stores for every battle with a base, after
  `setupDefenses` for a base defense. `BattleState.TakenFromBase`: the stored vehicles and their clips and the equipped
  stores; `CampaignBattles` removes them from the base when the battle starts, so a refused generation changes nothing.
  The port's base attack left the stored vehicles in the stores until now (`CampaignWorldBaseAttacks`).
- Units made from unit rules (the vehicles, the dummy, aliens and civilians: `createTempUnit`) now wear the environment's
  armor transformation (`BattleState.ArmorTransformations`, set before deployXCOM as `applyEnviroEffects` does; the
  `BattleUnit(Unit*)` constructor has no size check, unlike the soldiers' pass). This also fixes the craft path's
  vehicles and the S5 aliens and civilians, which wore their own armor.
- Not needed: `disableSoldierEquipment` is set by Ctrl+Alt on the inventory screens just before `runInventory`, is not
  saved, and deployXCOM clears it, so it is always false in a battle; `_baseInventory` is only set by `runInventory`
  (the base equipment screen), never for a battle; the `getMonthsPassed() == -1` branches are unreachable, since
  `AdvanceCampaignTime` moves MonthsPassed from -1 to 0 before the world ticks.
- Not ported here: `SavedBattleGame::randomizeItemLocations` over `StorageSpace`, which runs in `startFirstTurn`
  (Phase 7 branch 6, turns).

Tests: `BaseDefenseDeploymentTests` (vehicle and defender order with a 2x2 soldier, the fit wounded soldier deployed,
the unfit crew and the out craft's crew left out, the out craft's vehicle and items left out, clip ammo and both
`getVehicleClipSize` branches, the stores taken and kept, debug mode research, the record, the dummy, the dummy whose
member is no unit, the missing ground inventory, the environment's armor on vehicles, the dummy and aliens), `BaseDefenseMapTests.ADeploymentWithoutTerrainsUnderATextureWithTerrainsRefusesTheBaseDefense`;
`BattleStartTests.TheStartingConditionSendsDisabledVehiclesAndItemsBackToBaseStorage`
(the record for a craft mission).

## S6, part 3 — Mission sites, alien bases and the final mission (2026-10-24)

Reference: `ConfirmLandingState::btnYesClick` (the mission site and alien base branches), `ConfirmCydoniaState::btnYesClick`,
`BattlescapeGenerator::run` (`setAlienCustom`, the terrain pick's target), `setMissionSite`, `setAlienBase`, `setCraft`,
`setWorldShade`, `deployAliens` (the race), `SavedBattleGame::setAlienCustom`, `Texture::getRandomTerrain`.

- The requests of the three entry points already carried what `run` reads (`CampaignWorldLandings`, Phase 6 branch 5):
  a site's deployment as the mission type, its custom deployment and race and its texture; an alien base's deployment as
  the mission type, its race's `baseCustomDeploy` and `baseCustomMission`, and mission texture -1
  (`setWorldTexture(0, ...)`); the final mission's first `finalDestination` deployment in deployment order with that
  deployment's race, no textures and shade 0 (the generator's defaults). The generator needed no new branch: the
  terrain pick is at the request's target, which is the site for a site (`if (_mission) target = _mission;`); an alien
  base or the final mission has no mission texture, so its deployment's terrains or the mod's first terrain apply.
- Yes at an alien base whose race does not exist refuses (`landing.unknown-base-race`), after the armor check and
  before the starting condition, as `checkStartingCondition` and `btnYesClick` read the race's base custom mission and
  deployment from a null `AlienRace`. The request used to fall back silently to the base's own deployment.
- `BattleState.AlienCustomDeploy` and `AlienCustomMission` (`setAlienCustom`, for briefing, debriefing and `nextStage`).
- Checked, not ported here:
  - `setInBattlescape` on the craft, the UFO, the site, the alien base and the base. Only battle-time and later code reads the
    flags: a save written during the battle (refused, `battle.save-unsupported`, branch 2), `nextStage`'s race fallback,
    `finishBattle`, the briefing reopened from the battlescape and the debriefing (branches 3 and 7); the strategic side
    is frozen meanwhile. Branch 2 sets them with the battle save. The port models the flag only on UFOs, sites and
    alien bases so far.
  - `nextStage` (multi-stage missions such as the final mission's second stage) runs when a stage ends
    (`BattlescapeState::finishBattle`), which is branch 7's.
- `loadMAP` past the map's east or south side now refuses (`BattleMapGenerator.LoadMap`, after the height checks, as
  in the reference): the reference's `getTile`
  returns null there and it crashes, where the port's tile index ran into the next row. Map blocks are multiples of 10
  and the placements check the block grid, but a vertical level's block named by index (`MapScript::getNextBlock`) may
  be larger than its cell.

Tests: `BattleEntryPointTests` (a site's terrain picked at the site from its texture, its custom deployment's aliens
with the site's race; an alien base's race custom mission and deployment without a mission texture, its own deployment
without them, the unknown race's refusal; the final mission's deployment, race, shade and landed craft),
`BattleStartTests.AVerticalLevelsBlockRunningPastTheMapsSideIsRefused` (east and south),
`BaseDefenseMapTests.ALevelBlockPastTheMapsSideRefusesBeforeTheModuleMap` (formerly the module map's refusal test).

## S7, part 1 — Headless acceptance on the corpus (2026-10-24)

Reference: `MapDataSet::loadData`, `MapData::getObjectType`, `Tile::openDoor`, `Tile::destroy`,
`TileEngine::horizontalBlockage` (the object type's readers), `SavedBattleGame::setUnitPosition`, `getSpawnNode`,
`BattlescapeGenerator::deployXCOM` (the dummy), `addAlien` (the craft's position), `loadRMP` (links and culled nodes),
`GeoscapeState::handleBaseDefense`.

- `BattleState.Validate` (run by `StartBattle` on every generated battle) now checks the plan's invariants: every unit on
  the map stands where `setUnitPosition` would put it (`BattlePlacement.TryPosition` returns its own position) on tiles
  that name it, and no tile names a unit that is not in the battle; every live route node is at its index, inside the
  map, and links only to live nodes or -1 to -5; every item has exactly one holder (a tile, an existing unit with an
  inventory section or as a special weapon, or the one weapon it is loaded in), and none is also in guaranteed recovery.
  A failure is a generator defect (`InvalidOperationException`), not a content refusal. Branch 2 runs it on restored
  battles too.
- `loadRMP` adds the block's node offset to every link up to 250 without checking the file's node count, and culling ends
  only links to nodes culled in the same file, so a malformed RMP can link past the node list or to another block's
  culled node. The reference generates that map and indexes past its nodes when the link is followed; the port refuses
  it as content after `attachNodeLinks` (`BattleMapGenerator.CheckNodeLinks`). No RMP of the corpus has such a link.
- Vanilla UFO's `BARN.MCD` object 7 has tile type 10, which refused every farm battle. The reference reads an object's
  type only when it replaces another (a door's alternative, a non-zero die MCD), so `BattleTerrainLoader` now refuses
  only an out-of-range type on such a replacement; an object nothing turns into keeps its type unread. A scan of the
  vanilla UFO and TFTD MCDs finds no other out-of-range type.
- The base defense's dummy (nobody could be placed) is placed with `setUnitPosition`'s checks on the first tile. When it
  cannot stand there it is off the map (the reference keeps it at (0, 0, 0) without a tile); the first tile stays the
  inventory tile, and `addAlien` faces the aliens towards (0, 0, 0) as the craft, which crashed on the null position. A
  campaign reaches the dummy only when its defenders exist but none can be placed (no spawn node, or crew too wounded for
  deployXCOM's own check); `handleBaseDefense` destroys a base without defenders first.
- Acceptance (`PrivateBattleGenerationTests`, with the staged installation's own files through
  `VirtualBattleTerrainFiles`): the Rosigma `Early.sav` terror landing (tick 6290, the first acceptance scenario);
  landings a headless player stages by sending a manned craft to the first landed or crashed UFO or mission site in view
  (vanilla UFO `Begining.sav`, a farm; TFTD `Begining.sav`, `IronMode.sav`, `Middle.sav`; Rosigma `Middle.sav`); and base
  attacks staged by adding, once the world has begun, a retaliation UFO (the first retaliation mission without missiles)
  on the assault run into the first base, so `handleBaseDefense` raises the request itself (vanilla UFO `Middle.sav`, TFTD
  `IronMode.sav`, Rosigma `Early.sav`); TFTD `Middle.sav`'s base, whose two soldiers are on the Triton while it is out, is
  destroyed without a battle. Each battle validates, every
  kept unit is placed (the dummy excepted), and terrain, map script, size, the block of each 10x10 cell, units per
  faction and node count are pinned under the save's seed (`artifacts/private-battle-*.json` record the measured
  shapes). The Rosigma battle also checks the crew, the player's view, the hold and the `battle.save-unsupported`
  restriction. A missing RMP or MCD, a MAP whose header differs from its block and a truncated MAP refuse the battle
  with `battle.generation-failed`, the file's path in the reason, the campaign unchanged and the request pending.
- No vanilla early save reaches a landing in 40 days without a player sending a craft, and none reaches a base defense,
  hence the staging. No acceptance battle reached a named refusal (crashed-UFO power sources and primed map grenades
  stay branch 5's).

Tests: `PrivateBattleGenerationTests` (above), `BattleStartTests.TheValidatorNamesUnitsNodesAndItemsThatBreakTheBattlesInvariants`
(every check), `BattleStartTests.AnRmpLinkToNoNodeOfTheMapIsRefused`,
`BaseDefenseDeploymentTests.ADummyThatCannotStandOnTheFirstTileStaysOffTheMap` and `ADummyOnARaisedFirstTileStandsOneLevelUp`, `BattleTerrainLoaderTests.MalformedTerrainDataIsAReason`
(a die MCD and a door's alternative with a bad type refuse, an unused one loads).

## S7, part 2 — Branch audit (2026-10-24)

Reviewed `git diff main...HEAD` against `AGENTS.md` "Completion standard", comparing `BattlescapeGenerator::run`,
`deployXCOM`, `generateMap`, `SavedBattleGame::applyEnviroEffects` and `SavedBattleGame::save` (the state the generator
leaves for the battle save) with the port.

- Named refusals left, each with its owner: `battle.save-unsupported` (saving during a battle, branch 2), the crashed
  UFO's power sources (`explodePowerSources`) and map grenades primed with fuse timer -2 (`explodeOtherJunk`), both
  branch 5. Every other `MapGenerationException` is a content error where the reference throws, crashes or goes
  undefined, recorded in its slice; none is a restriction. Of the [inherited refusals](phase-7-plan.md#inherited-refusals),
  "A confirmed landing waits for its battle" retired in S1; "Active tactical battle is preserved" stays branch 2's and
  "Crashed UFO battlescape transitions require tactical simulation" branch 7's.
- Fixed:
  - `applyEnviroEffects` draws `RNG::percent(globalChance)` for the friendly, hostile and neutral environmental
    conditions whenever the battle has enviro effects (100 for a faction without a condition). The port drew nothing,
    so every later draw of a battle with enviro effects was out of the reference's order. `RuntimeBattleRules.EnviroGlobalChances`
    projects the chances; `BattleState.EnvironmentalConditions` (the save's `ecEnabledFriendly`, `ecEnabledHostile`,
    `ecEnabledNeutral`, read by the turns in branch 6), `EnviroEffectsId` and `StartingConditionId` (`enviroEffectsType`,
    `startingConditionType`) record the outcome for the battle save.
  - `setMusic`: the battle draws one of the deployment's `music`, else one of the terrain's (`BattleState.Music`, after
    the power sources' fuel, as in `run`). Deployments and terrains project `Music`.
  - With these draws in the reference's order, the staged Rosigma `Middle.sav` landing deploys 16 hostiles instead of 19
    under its pinned seed (`PrivateBattleGenerationTests`); every other pinned shape is unchanged.
  - A deployment request saved before it recorded vehicle ammo (a port save from before S4b part 4 with a pending
    request) refused every start with "records no ammo", leaving the campaign held for good. The ammo now comes from the
    craft's vehicles, which the pending request keeps unchanged; the refusal stays for a craft whose vehicles differ.
  - The map script's default case named S3b as pending; it is a defect now (`MapScript::load` refuses unknown commands
    and `generateBaseMap`'s undefined command never reaches it). "There is no deployment request to take." names the
    retired seam; it now reads "There is no deployment request to start a battle from."
- Checked and left to their owners (nothing in branch 1 reads them, and none is a refusal):
  - The terrain's ambience (`generateMap`'s ambient sound, random ambience and delays): presentation, branch 3, written
    by the battle save in branch 2. `resetCurrentAmbienceDelay` draws from `RNG::seedless`, not the game's stream, so the
    generator's order is unaffected.
  - `calculateLighting` (light, branch 4); `isPreview`, the craft deployment preview (UI, branch 3); the briefing's
    `strTarget` and `strCraftOrBase` (`BriefingState`, branch 3).
  - `StartBattle` has no UI action yet: the landing hold's briefing and the battlescape are branch 3's, as the hold had
    no battle to open in Phase 6.
- Status-doc drift: S4b part 4 listed "a vehicle with clip ammunition and no recorded ammo (base defense, S6)"; S6 part 2
  loads a base defense's stored vehicles with `getVehicleClipSize` instead, so the refusal is only the guard above
  (`XcomDeployment.NoVehicleAmmo`). S4b part 4 now says so.

Tests: `BattleStartTests.EnviroEffectsDrawEachFactionsConditionAndTheBattleRecordsItsConditionAndEffects`,
`TheBattlesMusicIsTheDeploymentsElseTheTerrains` (deployment, terrain, none, a pick from three tracks; the pick draws),
`ARequestSavedWithoutVehicleAmmoTakesTheAmmoOfTheCraftsVehicles`, `ARequestWithoutVehicleAmmoWhoseCraftDiffersIsRefused`
(other vehicles, no craft).

## S7, part 3 — Second branch audit (2026-10-24)

Reviewed `git diff main...HEAD` again against `BattlescapeGenerator::run`, `deployXCOM`, `addXCOMUnit`,
`addXCOMVehicle`, `deployAliens`, `addAlien`, `deployCivilians`, `generateMap`, `loadMAP`, `loadVerticalLevels`,
`addCraft`, `addLine`, `addBlock`, `drillModules`, `selectPosition`, `autoEquip`, `placeItemByLayout`,
`reloadFixedWeaponsByLayout`, `loadWeapons`, `sendItemsToLimbo`, `SavedBattleGame::initUnit`, `createItemForUnit`,
`getSpawnNode`, `setUnitPosition`, `placeUnitNearPosition`, `BattleUnit::addItem`, the `BattleUnit` constructors and
`GeoscapeState::handleBaseDefense`.

- Fixed:
  - `Armor::getMovementTypeByDepth`: every `BattleUnit` constructor turns a floating armor (movement type 3, TFTD's
    `STR_MAGNETIC_ION_ARMOR_UC`) into a flyer underwater and a walker on land, and a sinking one (4) the reverse. The
    port kept the raw type, so an ion-armored soldier underwater could not use flying spawn nodes or stand over open
    water, and on land it ignored walk costs. `BattleState.MovementType` now resolves it for soldiers, vehicles, the
    dummy, aliens and civilians. No pinned corpus shape changed.
  - A deployment request for an alien base saved before S3a recorded the missing mission texture as 0 (the reference
    passes null), so the battle would take texture 0's starting condition or terrains. The save reader now gives an
    alien base request no mission texture whatever it recorded.
  - `docs/phase-6-refusal-inventory.md` had not been regenerated on this branch (`--check` failed): it now lists the
    battle reasons (931 in all).
- Checked and matching: the generation order of draws (terrain, depth, scripts, globe and base terrain, map, enviro
  conditions, X-COM, civilians, aliens, music); the base defense's separate map stream, restored only for
  `baseDefenseMapFromLocation`; map block items (fuse timers, randomized and extended items, ammo loading);
  vertical levels, landing zones, roads, tunnels and checkBlock/removeBlock; spawn nodes, placement and placing near a
  friend; built-in, special and living weapons; addItem's sections and the cheapest storage; layouts, weapon
  loading, auto-equip and the weapon pile.
- Left as recorded: `isStandardPlayerUnit` and `setXCOMProperty` also exclude summoned player units; the only one
  here is the base defense's dummy, which gets no items, so the port's check without it gives the same battles.

Tests: `BattleStartTests.AFloatingArmorWalksOnLandAndFliesUnderwater` (land, underwater),
`BattleStateTests.UnitsResolveFloatingAndSinkingArmorsByTheBattlesDepth` (every type),
`StrategicLandingTests.AnAlienBaseRequestFromAnOlderSaveHasNoMissionTexture`.

## Next

Branch 1 is complete; the maintainer merges it. Branch 2 (`claude/battle-save`, [plan](phase-7-plan.md#delivery-shape))
starts from the merged `main`. Notes for it:

- The battle state to write (`SavedBattleGame::save`) is on `BattleState`, `BattleMap` (tiles, data sets, block names,
  nodes), `BattleUnit` and `BattleItem`; the S7 part 2 fields (`EnvironmentalConditions`, `EnviroEffectsId`,
  `StartingConditionId`, `Music`) cover the generation-time keys. Not modelled yet: ambience (branch 3), the briefing's
  target names, `setInBattlescape` on the craft, UFO, site and base (S6 part 3), the soldiers' armor in the reference's
  form (S4b part 2), the port-only overlay node (ADR 0038, D6).
- `BattleState.Validate` is meant to run on restored battles too (S7 part 1).
- Gotchas: Python on Windows writes CRLF in text mode, and the repository wants LF (`.gitattributes`); a CRLF test file
  breaks the raw-string rule replacements in `BattleStartTests` silently (tests then fail in unrelated ways). Write
  with `newline` set to LF, or in binary. A Python script passed through a Bash heredoc lost the escaping of `\n` in
  C# string literals it inserted (they became real newlines); insert such literals with the Edit tool. The test runner is Microsoft Testing Platform:
  filter with `dotnet test --project <proj> --filter-class <full name>` or `--filter-method`, not `--filter`.
