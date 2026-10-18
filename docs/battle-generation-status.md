# Battle generation status

Phase 7 branch 1 (`claude/battle-generation`), from `main` at `2b0c81fb`. Plan:
[Phase 7 branch 1](phase-7-plan.md#branch-1--battle-generation); decisions: [ADR 0038](decisions/0038-battle-aggregate.md).

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-18) |
| S1 battle aggregate | Done (2026-10-18) |
| S2 runtime battle terrain | Done (2026-10-19) |
| S3 map generation | S3a, S3c and `addLine` done (2026-10-19); rest of S3b (digTunnel, craft and UFO maps, vertical levels) open |
| S4 X-COM deployment | Not started |
| S5 alien and civilian deployment | Not started |
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
- Not yet: `digTunnel`, `addCraft`, `addUFO` and vertical levels refuse with a named S3b reason; base defense maps
  refuse until S6; map block items are S5.
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
- Not yet: the landing-zone exception for blocks under the craft, craft and UFO routes and vertical-level segments
  arrive with S3b.

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

## Next: rest of S3b

- Craft and UFO battlescape terrains: `battlescapeTerrainData` is deferred in the typed craft and UFO loaders
  (`EquipmentRuleLoaders`); it needs the nested `RuleTerrain::load`, a runtime projection on the craft and UFO rules, and
  `RuleTerrain::refreshMapDataSets` for craft skins.
- `addCraft` and `addUFO` (`addCraft`: landing zone blocks from the command's selection, the craft name and the starting
  condition's craft replacement, the UFO name or the deployment's `customUfo`), the craft and UFO map and route loads
  after the script (`_craftZ`, `_ufoZ`, segments, the craft's discovered tiles), the landing-zone exception in
  `loadNodes`, and `craftGroups` conditions.
- `digTunnel` (`drillModules` with `tunnelData` MCD replacements) and vertical levels (`populateVerticalLevels`,
  `loadVerticalLevels`, their segments in `loadNodes` and the second pass of `attachNodeLinks`); both are still raw YAML
  in the typed map script.
