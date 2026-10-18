# Battle generation status

Phase 7 branch 1 (`claude/battle-generation`), from `main` at `2b0c81fb`. Plan:
[Phase 7 branch 1](phase-7-plan.md#branch-1--battle-generation); decisions: [ADR 0038](decisions/0038-battle-aggregate.md).

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-18) |
| S1 battle aggregate | Done (2026-10-18) |
| S2 runtime battle terrain | Not started |
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

## Next: S2 design notes

- Terrain files are read through the content's virtual file catalog: `RuntimeContent` gains the catalog (set by
  `ContentSnapshot` and the compiled-cache restore, both of which already have it), so tests that write synthetic
  `TERRAIN/*.MCD`, `MAPS/*.MAP` and `ROUTES/*.RMP` files next to their rulesets get them through the normal bootstrap.
  The resource catalog already resolves these paths (`ResourceDescriptorResolver`, `terrain/{id}/...`).
- Data sets load lazily at battle generation, as `MapDataSet::loadData` does, into a cache owned by the battle
  capability; `BattleGenerator.Refusal` loads and validates everything the battle needs before any change, so a
  malformed file refuses `StartBattle` instead of throwing (ADR 0038).
- `MapData` semantics to keep: `setBlockValue(lightBlock, Stop_LOS, HE_Block, Block_Smoke, Flammable, HE_Block)` with
  block[1] = 255 when LOS stops and block[3] = 256 when smoke blocks; `setStopLOS` also resets block[1];
  `MCDPatch::modifyData` order (bigWall, TU walk/fly/slide, deathTile, terrainHeight, specialType, explosive, armor,
  flammability, fuel, HEBlock, footstepSound, objectType, noFloor, stopLOS, LOFTS) and an out-of-range patch index
  is an error (`MapDataSet::getObject` throws).
- Runtime projections are needed for `terrains` (data sets, map blocks), `mapScripts` and `mcdPatches`; they are
  typed in `TerrainDeploymentRules.cs` but not yet in `RuntimeRuleCatalog`.
