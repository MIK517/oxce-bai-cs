# Battle generation status

Phase 7 branch 1 (`claude/battle-generation`), from `main` at `2b0c81fb`. Plan:
[Phase 7 branch 1](phase-7-plan.md#branch-1--battle-generation); decisions: [ADR 0038](decisions/0038-battle-aggregate.md).

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-18) |
| S1 battle aggregate | Done (2026-10-18) |
| S2 runtime battle terrain | Done (2026-10-19) |
| S3 map generation | Not started |
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

## Next: S3 map generation

`BattlescapeGenerator::run` (terrain choice from the deployment, texture or fallback; depth; shade; map script
choice), `generateMap` and its commands, `loadMAP` into tiles, `loadRMP` and `attachNodeLinks`, with
`RuleTerrain::getMapData` indexing across data sets. Battle tiles go into `BattleState` as structure-of-arrays;
`StartBattle` needs `BattleTerrainFiles` and refuses without them.
