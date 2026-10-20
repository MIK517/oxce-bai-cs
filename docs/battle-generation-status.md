# Battle generation status

Phase 7 branch 1 (`claude/battle-generation`), from `main` at `2b0c81fb`. Plan:
[Phase 7 branch 1](phase-7-plan.md#branch-1--battle-generation); decisions: [ADR 0038](decisions/0038-battle-aggregate.md).

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-18) |
| S1 battle aggregate | Done (2026-10-18) |
| S2 runtime battle terrain | Done (2026-10-19) |
| S3 map generation | Done (2026-10-20) except craft skins |
| S4 X-COM deployment | S4a, large units and armor changes done (2026-10-20); items, custom deployments and unit statistics open |
| S5 alien and civilian deployment | Groundwork (deployment data, alien quantities) done (2026-10-20) |
| S6 base defense and other entry points | Not started |
| S7 acceptance and branch audit | Not started |

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
  outside the four parts after patching, a MAP whose header differs from the block's size. The header is compared as
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
- Not yet: craft skins (`RuleTerrain::refreshMapDataSets`); the port does not keep the craft's skin index, which is the
  reference's no-op case.

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
- Not yet (S4b): the save-side custom deployments (`customRuleCraftDeployments`, a craft's custom soldier and vehicle
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

## Next

- Craft skins: the craft's skin index in the strategic save and `refreshMapDataSets`.
- S4b: starting-condition vehicle and item restrictions, save-side custom deployments, the craft's items and equipment layouts (`placeItemByLayout`,
  `loadWeapons`, `autoEquip`), vehicle items, unit statistics; then the rest of S5 (`deployAliens`, civilians, objectives)
  and S6.
