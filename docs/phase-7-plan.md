# Phase 7 — Playable battlescape implementation plan

Plan date: 2026-10-18. Baseline: `main` at `2b0c81fb` (audit cleanup, PR #77). Reference:
`../oxce-bai` (`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`).
Status: branch 1 (`claude/battle-generation`) is complete and merged (PR #78); its slices, audits and evidence are
recorded in [battle generation status](battle-generation-status.md). Branch 2 (`claude/battle-save`) is in progress:
[battle save status](battle-save-status.md).

## Goal and scope

Deliver tactical battles that start from the strategic deployment request, can be played to an outcome through the
same headless command API and an indexed UI, and return their results to the campaign through debriefing. Battles
survive save and reload, and representative deployments of the compatibility corpus can be completed
([roadmap exit gate](implementation-plan.md#phase-7--playable-battlescape-vertical-slice)).

Phase 7 owns:

- battle generation from `CampaignDeploymentRequest` (Phase 6 D4,
  [ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md)): terrain, map scripts, map blocks,
  routes, units, inventories, objectives;
- the tactical rules: movement, visibility, light, fire and smoke, line of fire, projectiles, explosions, damage,
  morale, reactions, melee, psi, special abilities, environmental conditions, turns, AI;
- mission outcome, abort, recovery and debriefing, and their campaign effects (mission statistics, diary battle
  entries, battle research and cutscenes, commendations, final-mission victory);
- active-battle saves (the `battleGame` node, preserved opaquely today) and every tactical script hook and binding;
- the named restrictions Phase 6 handed over ([below](#inherited-refusals));
- the session layer and screen stack that the battlescape UI needs
  ([ADR 0037](decisions/0037-host-authoritative-session.md), audit step 4).

Phase 8 keeps the presentation breadth (Ufopaedia, diary and commendation screens, cutscene playback, window art).
Multiplayer proper (protocol, remote clients, speed arbitration) stays after Phase 7's first playable battle (audit
step 5), but the battle is built for several controllers from the start (D2).

## Design decisions

Recorded in [ADR 0038](decisions/0038-battle-aggregate.md); summarized here with the audit item each answers.

- **D1 — The battle is its own aggregate (M4.1, E1).** `Oxce.Gameplay/Battles` holds `BattleState` with its own
  capability registry, entered when the generator consumes the deployment request and left through debriefing. The
  campaign holds at most one battle, as `SavedGame::_battleGame` does, and refuses strategic commands while one
  exists.
- **D2 — Controllers, factions and viewers (M4.3–M4.6).** Every unit has a faction (player, hostile, neutral, as
  `UnitFaction`) and a controller (AI or a player slot). Visibility (discovered tiles, spotted units) is kept per
  faction. Every battle query takes a `BattleViewer`; commands address units by stable unit ID, and ownership is
  checked at the session boundary, not in rules.
- **D3 — Actions resolve completely; clients replay events (M4.2).** One command resolves a whole action, including
  interruptions such as reaction fire during a walk, and returns its ordered events. Animation states replay those
  events; they decide nothing.
- **D4 — No prove-before-run in battles (D1.3).** Battle commands validate totally before the first mutation and
  return a classified refusal otherwise. Resolution is total by construction: content is validated when it is
  linked, and battle state when it is generated or restored. An exception during resolution is a defect; it faults
  the battle and the host offers the last save. No battle forecast, no per-action copy. Strategic ticks that reach a
  named restriction move to *shadow promotion* (ADR 0038), measured and implemented by the restriction branch.
- **D5 — Live-state convention (D2).** Battle live state uses mutable classes and struct arrays holding runtime rule
  handles. Snapshots exist only at the persistence boundary. Tiles are stored as structure-of-arrays indexed by
  `x + y * width + z * width * length`, the reference's tile index.
- **D6 — Battle saves are their own module (E3).** `OxceBattleSave` reads and writes `battleGame`; port-only data
  (controllers, hostile-side visibility) goes in an `oxcePortBattle` overlay node that the reference ignores. Until
  branch 2, saving during a battle is refused with a named restriction, and a loaded save with a battle keeps it
  opaque as today.
- **D7 — Randomness.** The battle draws from the campaign's `IStatefulRandomSource`. Generation order follows the
  reference call order so tests can pin structure (which blocks, which nodes) under fixed seeds; exact streams are a
  non-goal.

## Delivery shape

Sequential feature branches, each from the previous merged `main`. Every branch keeps commands, script providers,
saves and UI actions integrated for what it adds; headless acceptance comes before UI in each branch.

| Order | Branch | Acceptance boundary |
|---|---|---|
| 1 | `claude/battle-generation` | The deployment request generates a validated battle headlessly: terrain, map script, map blocks, routes, units, inventories, objectives, viewer-scoped queries |
| 2 | `claude/battle-save` | `battleGame` loads into `BattleState` and is written back; the reference loads written saves (checked by a conformance reader, [below](#branch-decisions)); the opaque preservation retires and the "saving during a battle" restriction narrows to saving before the first turn |
| 3 | `claude/battle-session-ui` | Session layer (player identity, event broadcast, host clock and pause policy, per-actor quotes), screen stack, briefing and the first turn (`startFirstTurn`), battlescape view: camera, levels, selection, inventory, legal-action display |
| 4 | `claude/battle-movement` | Pathfinding, walking, flying, kneeling, turning, doors, falling, per-faction visibility, light, fire and smoke spread, terrain destruction from movement |
| 5 | `claude/battle-combat` | Line of fire and voxel tests, accuracy, projectiles, explosions, armor and damage, wounds, death and unconsciousness, reaction fire |
| 6 | `claude/battle-turns-ai` | Turn and phase processing, morale and panic, melee, psi, special abilities, environmental conditions, AI controller |
| 7 | `claude/battle-outcome` | Objectives, abort and end of mission, recovery, debriefing and its campaign effects, final-mission victory; the [first acceptance scenario](phase-6-status.md#first-phase-7-acceptance-scenario) closes |
| 8 | `claude/battle-scripts-corpus` | Remaining tactical script hooks and bindings, representative deployments (UFO, terror, base defense, alien base, final, underwater, custom), named restriction retirement, Phase 7 closure |

Branch 8's strategic restriction work (shadow promotion) does not depend on the battle and may be scheduled earlier
if the maintainer prefers.

## Branch 1 — Battle generation

Reference: `BattlescapeGenerator` (`run`, `generateMap`, `loadMAP`, `loadRMP`, `attachNodeLinks`, `deployXCOM`,
`deployAliens`, `deployCivilians`, `setupObjectives`, `generateBaseMap`), `MapScript`, `RuleTerrain`, `MapDataSet`,
`MapData`, `MapBlock`, `Node`, `Tile`, `BattleUnit` (construction), `BattleItem` (construction), `SavedBattleGame`
(the state it holds), `ConfirmLandingState::btnYesClick`, `BaseDefenseState`, `ConfirmCydoniaState`.

| Slice | Scope |
|---|---|
| S0 | This plan, ADR 0038, the inherited refusal ledger |
| S1 | Battle aggregate: `BattleState`, factions, controllers, viewers, registry, campaign hand-over from the deployment request, queries; strategic commands refused while a battle exists |
| S2 | Runtime battle terrain: map data sets (MCD with patches), map blocks (MAP, RMP), loaded through the resource system with malformed-input limits |
| S3 | Map generation: terrain, depth and shade selection; map script commands (`addBlock`, `addLine`, `fillArea`, `checkBlock`, `removeBlock`, `resize`, `addCraft`, `addUFO`, `digTunnel`, vertical levels); tiles from MAP; nodes from RMP and their links |
| S4 | X-COM deployment: units from soldiers and vehicles, craft inventory items, placement on craft tiles or base, starting conditions |
| S5 | Alien and civilian deployment: deployment data, alien item levels, node ranks, civilians, UFO power sources, objectives |
| S6 | Base defense map (`generateBaseMap`) and the remaining entry points (final mission, mission sites, alien bases) |
| S7 | Headless acceptance on the corpus, branch audit |

Acceptance: the Rosigma `early/Early.sav` terror landing (first acceptance scenario) and staged vanilla UFO, TFTD
and base-defense requests generate battles. Each generated battle passes the battle validator (every unit on a
walkable tile inside the map, every node link inside the map, items owned exactly once), and fixed-seed structure
(terrain, map script, blocks, unit counts per faction, node count) is pinned. Malformed terrain resources fail at
load with an actionable error. Saving with a battle open is refused with a named restriction that branch 2 retires.

## Branch 2 — Battle saves

Reference: `SavedBattleGame::load`, `save`, `loadMapResources` and `resetUnitTiles`, `Tile::loadBinary`, `saveBinary`,
`load` and `serializationKey`, `Node::load` and `save`, `BattleUnit::load` and `save` (with the soldier and
generated-unit constructors the loader calls), `AIModule::load` and `save`, `BattleUnitStatistics`, `BattleItem::load`
and `save`, `ItemContainer` (`baseItems`), `ScriptValues` (`tags`), `SavedGame::load` and `save` (`battleGame`, the
header's `mission`, `target`, `craftOrBase` and `turn`), `LoadGameState` (a battle save opens the battlescape), the
`inBattlescape` flags of `Craft`, `Ufo`, `MissionSite`, `Base` and `AlienBase`, `Soldier`'s `transformedArmor` and
`replacedArmor`, and `BriefingState` with `SavedBattleGame::startFirstTurn` (what precedes the first save).

### Branch decisions

- **Typed state, opaque leftovers.** Every key the reference's readers read becomes typed battle state, also where a
  later branch is the first to use it (ADR 0038, live state). Keys no reader reads survive loaded rewrites on their
  owner (the battle root, a unit by ID, an item by ID, a node by index), as the strategic overlay keeps them
  ([ADR 0018](decisions/0018-campaign-state-and-oxce-save-overlay.md)); an entity the battle creates carries none.
- **State the reference does not save.** Each piece of port state outside the reference's keys is classified, and the
  reload comparisons cover the saved and derived state only:

  | Port state | Reference | Branch 2 |
  |---|---|---|
  | `DeploymentId` | not saved; `BriefingState`, `BattlescapeState`, `AbortMissionState` and `DebriefingState` derive the deployment from `missionType`, `alienCustomDeploy` or `alienCustomMission`, and the UFO in battle | derived on load the same way |
  | `NextItemId` | rebuilt as the highest item ID plus one (`load`) | derived on load; the generator's gaps (IDs of discarded items) are not kept |
  | `TerrainId` | not saved; only the generator reads the terrain, the battle keeps `flattenedMapTerrainNames` | dropped |
  | `BattleNode.Segment` | `segment` is commented out in `Node::load` and `save`; only the generator reads it | dropped, 0 after a reload |
  | `BattleUnit.StartNodeId` | `AIModule::save` writes no start node | dropped (S3 checks that nothing after generation reads it) |
  | `StorageSpace`, `CraftInventoryTile` | not saved; read by the generator and `startFirstTurn` only | dropped; no save is written before the first turn (below) |
  | `ReturnedToBase`, `TakenFromBase` | applied to the base by the generator | dropped once `StartBattle` applies them |
  | controllers, hostile and neutral spotting | not kept | `oxcePortBattle` (ADR 0038, D6) |

  A later branch that needs dropped state derives it or adds it to `oxcePortBattle` with an ADR line.
- **No save before the first turn.** The reference can save only after the briefing (`strTarget`, `strCraftOrBase`)
  and `startFirstTurn` (item locations, the X-COM units' first `prepareNewTurn`, `turn` 1). `LoadGameState` goes
  straight to the battlescape, so a turn-0 save would stay before its first turn in the reference. S5 replaces
  `battle.save-unsupported` with the named restriction "saving before the battle's first turn", which branch 3 retires
  with the briefing and `startFirstTurn` (or hands to branch 6 for `prepareNewTurn`, recorded in its status). Turn-0
  battles are still captured and written by the save module in tests, never through the campaign's save.
- **The reference's current form is written; older forms are read.** The port writes binary tiles with the
  reference's serialization key, `items` and `itemsSpecial`, and `ammoItemSlots` with `ammoItem`. It reads what the
  reference reads: text `tiles`, a missing `tileBoolFieldsSize`, a base defense without `moduleMap`
  (`calculateModuleMap`), `ammoItem` without `ammoItemSlots`, a missing `originalFaction`. Each such path has a test
  with a synthetic node (S2–S4), since the corpus has none of them.
- **Reference quirks (an ADR records each in its slice).**
  - Duplicate keys: `BattleUnit::save` writes `turnsSinceSpotted`, `turnsLeftSpottedForSnipers` and
    `turnsSinceStunned` twice; the first `turnsSinceSpotted` and `turnsLeftSpottedForSnipers` are the arrays' pointers
    (`0x5803c54`). Reading follows the reference: the first duplicate wins (`useIndex`, and the port's
    `YamlMappingNode.TryGet`), a hex scalar is read as an integer and cut to its low byte (`tryReadAs<int>` into
    `Uint8`). Writing is an intentional difference: each key once, the hostile value in the integer form, which the
    reference reads correctly.
  - `armor` has `SIDE_MAX` (9) sides; the constructor sets 5 and nothing reads the 4 diagonal ones (diagonal damage
    uses the `Armor` rule). They are read and kept as saved; a generated unit writes them as 0.
  - Old text tiles: `discovered[0]` goes to `_objectsCache[-1]` (undefined), and `[1]` and `[2]` both to the floor.
    The port ignores `[0]` and gives the floor `[2]`, the last write; no refusal.
  - `BattleItem` reads and writes `fuseEnabed` (sic).
- **Generated battles write what the reference's constructors leave.** Fields branch 1 did not model (current armor
  per side, recolor, aggression, move costs, the alien AI module, the terrain's ambience, reserved time units and the
  toggles) are set at generation as the reference sets them, so a generated battle's save is the reference's except
  for the quirks above. The ambience delay draws from `RNG::seedless`, not the game's stream; the battle's draw order
  is unchanged.
- **Failures and skips on load.**
  - Where the reference skips, the port skips with a diagnostic, and the skipped entity's dependents end as in the
    reference: a unit whose rule is gone is left out and its items keep no owner (`findUnitById` is null); an item
    whose rule is gone is left out, and an `ammoItemSlots` ID that names no item is ignored.
  - Where the reference asserts, throws or indexes out of range, the load fails with a save error naming the key: a
    tile index outside the map (`assert`), a tile set ID past `mapdatasets` or an object past its data set
    (`loadMapResources`), a soldier ID with no soldier, a legacy `ammoItem` ID that names no item (`itemIndex.at`).
  - Where the reference goes on silently with a broken value, the port fails when the value would be used: an unknown
    `mapdatasets` name (`Mod::getMapDataSet` creates an empty set, which then has no objects); an unknown
    `startingConditionType` or `enviroEffectsType` becomes none, as in the reference.
  - A restored battle runs `BattleState.Validate`, extended in S3 and S4 to the reference's mid-battle state: units
    that are out (dead, unconscious) stand on no tile (`resetUnitTiles`), corpses are items naming their `unit`, and
    the items of a skipped unit are held by nothing. A violation is a corrupted save, not a defect.
- **Port-only data and what the load recomputes.** Controllers and the spotting the reference does not keep go in
  `oxcePortBattle`. A reference save gets the defaults: the player faction's units to the local player slot, every
  other unit to the AI, and the player's spotting from the units' `visible`. The load drops the AI module of player
  units and of dead or ignored units, as the reference does. `resetUnitTiles` (every player unit visible) is ported
  in S3; the lighting and field of view `loadMapResources` recomputes are branch 4's.
- **The strategic side of a battle.** `StartBattle` sets what `BattlescapeGenerator::run` and `deployXCOM` set on the
  campaign: the `inBattlescape` flags, the soldiers' battle armor with `transformedArmor` or `replacedArmor`. The
  header's `mission`, `target`, `craftOrBase` and `turn` are written while a battle exists.
- **"The reference loads written saves" is checked by a proxy.** No reference engine binary is part of the workspace.
  Acceptance is a conformance reader in the tests that applies `SavedBattleGame::load`'s reading rules, citing the C++
  for each: required keys (`selectedUnit`), value types (the tile key sizes as `char`), the tile buffer's size against
  `totalTiles` and the serialization key, the duplicate-key and hex rules above, and, for saves the campaign writes, turn 1 or later. It runs on every
  written battle, with the corpus round trips. A C++ engine reload stays a manual check, as for strategic saves
  ([saves matrix](compatibility/saves.md)).

### Slices

| Slice | Scope | Tests beyond the corpus |
|---|---|---|
| S0 | This plan and the [battle save status](battle-save-status.md) record | — |
| S1 | Save module and battle root: `BattleSnapshot` (the capture and restore boundary of `BattleState`), `OxceBattleSave` in `Oxce.Savegames` reading and writing every root key of `SavedBattleGame::load` and `save`, `baseItems`, the root's `BattleGame` tags, the root fields generation does not set yet, the derived `DeploymentId`, unknown root keys; module-level round trips. The campaign keeps preserving `battleGame` opaquely until S5 | VIP and objective blocks present and absent, `startingConditionType` and `enviroEffectsType` known, unknown and absent, an unknown root key |
| S2 | Map: `mapdatasets`, binary and text tiles, `loadMapResources` through `BattleTerrainLoader` (MCD patches), tile state (fire, smoke, discovered parts, open UFO doors, `lastExploredBy*`), nodes (`allocated`, `dummy`, links), `moduleMap` and its `calculateModuleMap` fallback, the flattened terrain and block names and reinforcement blocks | a generated base defense with and without `moduleMap`; text tiles with `discovered` and open doors; no `tileBoolFieldsSize`; other field sizes in the key; each malformed tile, set and object case; an unknown data set |
| S3 | Units: the soldier and generated-unit paths, every `BattleUnit` key, the duplicate-key and armor quirks, the AI module (and when the load drops it), `tempUnitStatistics`, recolor, move costs, spawn units, `BattleUnit` tags, `selectedUnit` and `undoUnit`, the status sanity check, `resetUnitTiles`, the constructors' values for generated units, controllers and spotting in `oxcePortBattle`; `Validate` for out units | a unit whose rule is gone; a missing soldier; no `originalFaction`; a mind-controlled alien with an AI node; the pointer-valued duplicates |
| S4 | Items: `items`, `itemsSpecial`, `recoverConditional`, `recoverGuaranteed`; every `BattleItem` key (owners, previous owners, corpses' units, slots and `inventoryX`/`inventoryY`, an unknown slot as the ground, ground positions, ammo slots, fuses, medikits, `inventoryMoveCost`, `droppedOnAlienTurn`, `XCOMProperty`, tags), the item ID counter and order; `BattleState.Validate` on restored battles | `ammoItem` without `ammoItemSlots`; an unknown ammo slot ID; a legacy `ammoItem` to no item; an item whose rule is gone; the items of a skipped unit; an OXC `fuseTimer` |
| S5 | Campaign integration: `battleGame` restores into `CampaignBattles`; the "Active tactical battle is preserved" restriction retires and `battle.save-unsupported` narrows to "saving before the battle's first turn"; `StartBattle` sets the strategic side; the header; loaded rewrites keep unknown battle fields; saving during a battle through the host and UI save paths; the saves matrix rows for battles ([saves](compatibility/saves.md)) | a turn-0 battle refused, a restored battle saved; `inBattlescape` and soldier armor round trips |
| S6 | Acceptance on the corpus (below), the conformance reader, the saves matrix, branch audit | — |

Acceptance: the private active-battle saves (vanilla UFO, TFTD and Rosigma `battlescape/`) load into `BattleState`,
validate, and rewrite with semantic equality to their source (with each reference quirk the port does not repeat
recorded and normalized), and a second rewrite is byte-stable. Branch 1's acceptance battles, written by the save
module, reload with the same saved and derived state and the same viewer overviews. Every written battle passes the
conformance reader. Malformed battle saves fail with an actionable error. Saving a battle after its first turn works;
the inherited "Active tactical battle is preserved" boundary retires, and branch 1's `battle.save-unsupported`
narrows to the first-turn restriction branch 3 owns.

## Inherited refusals

The Phase 6 [refusal inventory](phase-6-refusal-inventory.md) assigns two classes to Phase 7.

- **Tactical and battle boundaries (3).** "A confirmed landing waits for its battle" retires in branch 1 (S1); "Active
  tactical battle is preserved" in branch 2; "Crashed UFO battlescape transitions require tactical simulation" in
  branch 7, when a recovered crash site has a battle outcome.
- **Named restrictions (136).** Most are strategic same-tick interactions that a forecast cannot prove (R3–R6 and
  the capability boundaries). They do not touch the battle. Branch 8 retires them by shadow promotion (ADR 0038):
  a tick whose preflight would stop on a named restriction runs on a shadow copy and the shadow's result is adopted
  if it completes, so the forecast and its variant tests can be deleted. Each retirement deletes its forecast in the
  same commit (audit D1, option 2). The remainder are unsupported script bindings, which the bindings work in
  branch 8 retires, and `canTransferCraftsWhileAirborne`, which branch 8 implements or reclassifies with a reason.

A restriction a branch cannot retire names its new owner in that branch's status record.

## Validation

- Unit tests per slice; compatibility tests for every reference-visible rule, with the C++ files cited.
- Fixed seeds and `FixedClock`; battle snapshots compare structure, legal actions and rule-driven transitions, never
  individual random outcomes or pixels.
- Private-corpus tests skip without `fixtures/private/`.
- After each slice: an independent audit of the slice, fixes before the next slice, status record updated.
