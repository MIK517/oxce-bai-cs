# Phase 7 — Playable battlescape implementation plan

Plan date: 2026-10-18. Baseline: `main` at `2b0c81fb` (audit cleanup, PR #77). Reference:
`../oxce-bai` (`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`).
Status: branch 1 (`claude/battle-generation`) is complete and merged (PR #78); its slices, audits and evidence are
recorded in [battle generation status](battle-generation-status.md). Branch 2 (`claude/battle-save`) is complete and merged (PR #79):
[battle save status](battle-save-status.md). Branch 3 (`claude/battle-session-ui`) is complete and merged (PR #80):
[battle session and UI status](battle-session-ui-status.md). Branch 4 (`claude/battle-movement`) is planned:
[battle movement status](battle-movement-status.md).

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
- active-battle saves (the `battleGame` node, typed state since branch 2) and every tactical script hook and binding;
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
  | `BattleNode.Segment` | `segment` is commented out in `Node::load` and `save`; only the generator reads it | dropped, 0 after a reload (a loaded `segment` key survives on its node like any key no reader reads) |
  | `BattleUnit.StartNodeId` | `AIModule::setStartNode` sets the AI module's `_fromNode`, which `AIModule::save` writes as `fromNode` | saved as the AI's `fromNode` (S3) |
  | `StorageSpace`, `CraftInventoryTile` | not saved; read by the generator and `startFirstTurn` only | dropped; no save is written before the first turn (below) |
  | `ReturnedToBase`, `TakenFromBase` | applied to the base by the generator | dropped once `StartBattle` applies them |
  | controllers, hostile and neutral spotting | not kept | `oxcePortBattle` (ADR 0038, D6) |
  | `BattleOverview.Revision` | no equivalent; the port's change counter for viewers | not saved; a restore counts its own `AddUnit` and `AddItem` calls (the special weapons included), so the value differs from the generated battle's; reload comparisons of overviews set it to 0 |
  | the seedless stream (`BattleState.Seedless`) | `RNG::seedless` is process state, never saved | not saved; a restored battle starts it from a fixed seed, so seedless draws after a reload (ambience delays, a new unit's rank and look) differ from a run without one, as in the reference between two runs; reload comparisons cover no seedless draw |
  | an item's `SlotX` and `SlotY` outside a grid slot | `BattleItem::save` writes `inventoryX` and `inventoryY` only in an `INV_SLOT` slot ("for hands and ground it can be `0` for both"); a load leaves the constructor's 0 | the same; a generated hand item keeps its layout position until a reload, and reload comparisons set it to 0 (S6) |

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
  - A `mapdatasets` name without its MCD file fails the load: `Mod::getMapDataSet` creates the set for any name, and
    `MapDataSet::loadData` then dereferences the null stream `FileMap::getIStream` gives for the missing file, whether
    or not a tile uses the set (S2; a name no terrain declares loads from its file). An unknown
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

## Branch 3 — Battle session and UI

Reference: `BriefingState` (constructor, `btnOkClick`), `AliensCrashState`, `NextTurnState`,
`NextTurnState` (turn 0 and 1 work), `BattlescapeGame::spawnFromPrimedItems`, `SavedBattleGame::startFirstTurn`, `randomizeItemLocations`, `resetUnitTiles`, `newTurnUpdateScripts`,
`BattleUnit::prepareNewTurn`, `BattlescapeState` (construction, selection, next and previous soldier, level and center
buttons, `updateSoldierInfo`), `Camera`, `Map` (draw order, cursor, levels), `InventoryState` and `Inventory` (the
pre-battle layout and the in-battle moves), `RuleInventory` slot rules, `LoadGameState` (a battle save opens the
battlescape), `Game` (the state stack), `GeoscapeState` (the pause policy the host takes over).

### Branch decisions

- **Session before battle UI (audit step 4, [ADR 0037](decisions/0037-host-authoritative-session.md)).** The session
  layer is built first and the geoscape and logistics clients move onto it, so the battlescape is the first new client
  and not a second pattern. It holds: a player identity on every command and query (single-player has one local
  player; a battle controller slot maps to a player), authorization at the session boundary (a unit command from a
  player who does not control the unit is refused with a classified reason; rules never check ownership, D2),
  broadcast of committed events to every attached client, the host clock and pause policy, and per-actor quotes.
  Transport, protocol and remote clients stay after Phase 7's first playable battle (audit step 5).
- **Host clock and pause policy leave the UI.** `GeoscapeTimeController` and the covered and popup pause decisions
  (ADR 0034 D3) become session state that clients inform (a client reports "covered", a client requests a speed). The
  one-client behavior is the reference's and is pinned by the existing geoscape tests. While a battle exists the
  session clock does not advance (the `battle` preflight, branch 1 S1).
- **Per-actor quotes.** The logistics quote slot becomes a quote keyed by actor and ID; a time advance or another
  actor's command no longer invalidates it (ADR 0037 rule 6).
- **Screen stack.** `Oxce.Engine` gets a screen stack (audit E4): push, pop, replace, input routed to the top, an
  opaque or translucent flag, shared widgets, and a "covers the geoscape" query the session reads. The geoscape and logistics modals
  that are screens migrate onto it; no screen is rewritten for looks. Phase 8 screens use it.
- **Briefing and the first turn belong here.** The briefing reads the deployment's briefing data and sets the
  mission target and craft or base (`strTarget`, `strCraftOrBase`: the destination's name and `STR_CRAFT_` or
  `STR_BASE_UC_` lines, and a random operation name from `RNG::seedless` when the mod has them), which are empty until
  then and which branch 2 saves. Its confirm step (`btnOkClick`) runs `spawnFromPrimedItems` (units and items from
  primed spawn grenades with fuse 0, grenades removed; branch 1 can generate such items) before the alien tally; S3
  ports it. Cutscene and music are Phase 8's; the craft deployment preview (`isPreview`: skips the inventory, calls
  `startFirstTurn`, and skips the new-turn scripts) is `battle.preview-unsupported`, refused at battle start.
  `NextTurnState` is part of the start: at turn 0 it picks a hidden-movement background (`RNG::seedless`, presentation),
  runs `checkBugHuntMode` (the saved `bughuntMode` can change) and `determineReinforcements` (allowed for the player
  side at turn 0; it draws from the game RNG before `randomizeItemLocations`, so S3 orders the draws as the reference
  does), and on close tallies units, finishes the battle when no soldiers live (branch 7), centers the camera and
  autosaves on turn 1. A start in which a reinforcement wave could deploy (it passes its difficulty range and its trigger at turn 0) is refused as
  `battle.reinforcements-unsupported`, owned by branch 6 (turn processing), which ports the deployment of waves; S3 decided this instead of porting
  `determineReinforcements`, which needs the unit placement of branch 6.
  `startFirstTurn` is ported whole: `randomizeItemLocations`
  on the selected unit's tile (storage space, reference RNG order), `resetUnitTiles`, reselecting the unit that
  stands on the inventory tile after the reset (the reference comment says closest to the ramp), `prepareNewTurn(false)` for every live X-COM unit, turn 1, `newTurnUpdateScripts`. `NewTurnUnit` and
  `NewTurnItem` callbacks refuse by name until branch 8's bindings (a battle whose rules bind them cannot start);
  `reviveUnconsciousUnits` and `convertInfected` are branch 6's and are guarded by a validated precondition (none can
  apply to a generated battle at turn 1). Refusal names: `battle.new-turn-script-unsupported` (branch 8),
  `battle.turn-one-revive-unsupported` (branch 6), `battle.no-live-aliens` (the `AliensCrashState` exit, branch 7),
  `battle.preview-unsupported` and `battle.reinforcements-unsupported` (above), `battle.casualties-unsupported` (branch 6: a unit `checkForCasualties` would take out; refused at the briefing and for the commands of a running battle, a loaded one included), `battle.turn-recovery-script-unsupported` (branch 8: a scripted time unit or energy recovery), `battle.spawn-gravity-unsupported` (branch 4: a primed spawn item whose unit would fall, or whose tile above level 0 has no floor). Completing this retires `battle.save-before-first-turn`, the restriction branch 2 handed over.
- **The pre-battle inventory is the reference's `InventoryState(false, ...)`.** Items move between slots under the
  `RuleInventory` slot rules at no TU cost before the first turn and at the reference's cost during battle; moves are
  commands on stable unit and item IDs with total validation. Closing the pre-battle inventory also runs `saveEquipmentLayout`, which
  overwrites each soldier's `equipmentLayout` in the campaign (default `oxceAlternateCraftEquipmentManagement`, no
  base); S4 ports it as a campaign effect of starting the first turn. Ammo loading, throwing and priming
  (`battle.inventory-ammo-unsupported`, `battle.inventory-throw-unsupported`, `battle.inventory-prime-unsupported`)
  and the inventory layout save and load screens (`InventorySaveState`, `InventoryLoadState`:
  `battle.inventory-layout-screens-unsupported`, and S4 adds clear, autoequip, template and rename: `battle.inventory-clear-unsupported`, `-autoequip-unsupported`, `-template-unsupported`, `-rename-unsupported`) are deferred where the first playable battle does not need them. The open ADR 0039 note (an owned recovery-list item that keeps its owner without joining
  the inventory) is resolved here: it joins the inventory or is refused.
- **Battlescape view.** The map is drawn from `QueryBattle(viewer)` only: tiles the viewer's faction discovered, units
  it spotted, never hidden state. Camera, level up and down, selection (next, previous, the selected unit) and the
  legal-action display read queries; every gameplay change is a command (ADR 0037 rules 1, 3, 4, 6). Selection is not purely view state in the
  reference: `BattlescapeState` changes `SavedBattleGame::_selectedUnit` (saved), and "next and stop" sets `undoUnit`
  and the unit's `dontReselect` (both saved; `prepareNewTurn` clears `dontReselect`). The port keeps one selected unit
  per player as client state and makes the saved fields commands: `SelectUnit` (writes `selectedUnit` for the
  controlling local player's slot) and `SkipUnit` (writes `undoUnit` and `dontReselect`). With several players only the
  battle's side-owning slot writes the saved `selectedUnit`; ADR 0039 records this intentional difference in S5.
  The legal-action display lists what is legal for the selected unit now; actions of later branches appear as they
  are ported, none is faked.
- **Queries carry no player yet (named gap, branch audit).** Three battle queries (`IBattleQuery`, `IBattleViewQuery`, `IBattleInventoryQuery`) take a `BattleViewer` that the client chooses (`IBattleBriefingQuery` takes none), and `CampaignUiSession.ForCampaign` reads them from the campaign, not through the session handle; commands do carry the player. With one local player this changes nothing. Binding the viewer to the attached player and its controller slot (a query refused for a viewer the player does not own) belongs to the remote client work after the first playable battle (audit step 5, transport and protocol), where a second player exists to test it with.
- **Indexed UI, headless first.** Battle screens run on the indexed loop client and are covered by scripted loop tests
  (no pixels), as the geoscape clients are. A missing sprite is a diagnosed placeholder, not a crash.
- **`LoadGameState` for battles.** Loading a save with a battle opens the battlescape directly, as the reference does
  (no briefing); controllers and spotting are branch 2's restore.
- **Out of scope.** Pathfinding and walking (branch 4), line of fire and shooting (5), end of turn, AI and morale (6),
  abort and debriefing (7). Their buttons are present and refuse by the owning branch's name. The minimap and the
  extended-battlescape link screens are listed in the status with an owner (branch 8 or Phase 8).

### Slices

| Slice | Scope | Tests beyond the corpus |
|---|---|---|
| S0 | This plan and the [battle session and UI status](battle-session-ui-status.md) record | none |
| S1 | Session layer: player identity, command authorization, event broadcast, per-actor quotes; the geoscape and logistics clients go through it | an unauthorized command refused with no change, using a test-only unit command (the first real unit commands are S4's); two clients both receive an event; a quote survives another actor's command and a time advance |
| S2 | Host clock and pause policy out of `GeoscapeTimeController` and `GeoscapeClient`; screen navigator in `Oxce.Engine` (`ScreenNavigator`) with the geoscape base screens on it. The geoscape popup list, naming, selecting and placing modes, the logistics client and shared widgets move to S6, with the session reading `ScreenNavigator.Covers` | one-client behavior equal to today; pause on popup and on cover; no advance during a battle; stack push, pop, replace, input routing |
| S3 | Briefing and the first turn: briefing queries, `startFirstTurn` with its parts, the `NewTurn*` script refusals, `battle.save-before-first-turn` retired, `LoadGameState` into the battle | a generated battle starts and saves after its first turn; seeded item randomization; out units skipped; a bound `NewTurnUnit` callback refused; briefing sets `strTarget` and `strCraftOrBase`; primed spawn grenades spawn; a preview and reinforcements refused; the fifth loaded special item reported (branch 2's open note, via a restore diagnostics sink); a loaded battle opens without a briefing |
| S4 | Pre-battle and in-battle inventory: slot rules, move commands, TU costs, ground and craft items, queries for the inventory screen, the recovery-list item decision | each slot type; a refused move changes nothing; TU cost; two-hand item; grid fit; an owned recovery-list item; closing the inventory writes `equipmentLayout`; each inventory refusal named |
| S5 | Battlescape queries and view model: camera, levels, selection, visible tiles and units per viewer, legal-action display | a viewer never sees an unspotted unit or an undiscovered tile; selection skips out units; `SelectUnit` and `SkipUnit` write the saved fields; level bounds; only legal actions listed |
| S6 | Battlescape screens on the stack and the indexed loop: briefing, pre-battle and in-battle inventory, map plan view with units, selection, level and camera, later branches' buttons refusing by owner's name; `ScreenNavigator` tells the screen a pop reveals | scripted loop tests over a generated battle; a missing sprite is a diagnostic |
| S6b | Host loop work S6 left: opening the battle screens from the SDL host (`BattleUiContext.Open`, with the briefing lines from the deployment request), starting the battle from the request, and the turn-1 autosave of `NextTurnState::close` | the host loop opens a generated battle's screens and writes the turn-1 autosave; a refused start offers the request again |
| S6c | Migrate the geoscape popups, naming, selecting and placing modes onto `ScreenNavigator` (S2's undelivered scope), with the shared widgets they need and the client cover derived from `ScreenNavigator.Covers`. **Plan change (branch audit):** the logistics client was already one screen on the stack; its internal views (readiness, management, personnel, research, production, orders) and its load-confirmation prompt stay one screen with a view enum, because no reference rule depends on their being stack entries; splitting them belongs to Phase 8 presentation, with translucent compositing on separate surfaces | the geoscape client behaves as before on the stack; a popup pauses the clock through `Covers`; existing geoscape and logistics tests unchanged |
| S7 | Acceptance on branch 1's battles and the private active-battle saves; branch audit | none beyond acceptance |

Acceptance: each generated acceptance battle of branch 1 passes the briefing, the pre-battle inventory and
`startFirstTurn` (the three Rosigma battles, whose mod binds `newTurnUnit`/`newTurnItem` scripts, are instead refused at the briefing by `battle.new-turn-script-unsupported`, branch 8's, and still save and reload into the briefing), is shown in the battlescape through the indexed loop for a chosen player, can be saved (the
first-turn restriction retired) and reloaded into the battlescape, and the private active-battle saves open directly.
A client of another faction sees only its own spotted state. The geoscape behaves as before through the session and the
host clock, with the existing geoscape tests unchanged.

## Branch 4 — Battle movement

Reference: `Pathfinding` (`calculate`, `getTUCost`, `isBlocked`, `isBlockedDirection`, `canFallDown`, `isOnStairs`,
`validateUpDown`, `bresenhamPath`, `aStarPath`, `findReachable`, `findReachablePathFindingNodes`, `previewPath`,
`getMovementType`), `PathfindingNode`, `PathfindingOpenSet`, `UnitWalkBState`, `UnitTurnBState`, `UnitFallBState`,
`BattlescapeGame` (`kneel`, `checkReservedTU`, `checkForProximityGrenades`, `primaryAction` and `secondaryAction` for
moves and turns), `BattleActionCost` (`updateTU`, `haveTU`), `BattleUnit` (`startWalking`, `keepWalking`, `lookAt`,
`turn`, `abortTurn`, `kneel`, `getTurnCost`, the kneel costs, `getReactionScore`, `updateTileFloorState`,
`haveNoFloorBelow`, `setTile`, `setPosition`, the visible-unit and visible-tile lists, `setTileLastSpotted`,
`updateEnemyKnowledge`, `knockOut`), `TileEngine` (lighting: `calculateLighting`, `calculateSunShading`,
`calculateTerrainBackground`, `calculateTerrainItems`, `calculateUnitLighting`, `addLight`, `togglePersonalLighting`;
blockage: `blockage`, `horizontalBlockage`, `verticalBlockage`; sight: `calculateFOV` (both forms),
`calculateTilesInFOV`, `calculateUnitsInFOV`, `setupEventVisibilitySector`, `recalculateFOV`, `calculateLineTile`,
`visible`, `getSightOriginVoxel`, `getOriginVoxel`, `getVisibleDistanceMaxHelper`, `getTrajectoryDataHelper`,
`canTargetUnit`, `calculateLineVoxel` and `voxelCheck` for sight; `getSpottingUnits` for its spotting; doors: `unitOpensDoor`, `checkAdjacentDoors`, `closeUfoDoors`; `applyGravity`, `hit` (its tile
path), `hitTile`, `getDirectionTo`), `Tile` (`openDoor`, `closeUfoDoor`, `hasNoFloor`, `hasLadder`, `getTUCost`, the
light layers, `ignite`, `addSmoke`, `setFire`, `setSmoke`, `damage`, `destroy`, `prepareNewTurn`, `setDiscovered`,
`setVisible`), `SavedBattleGame` (`prepareNewTurn`'s fire and smoke part, `addFallingUnit`, `loadMapResources`'
lighting and field of view), `InventoryState` (the gravity, lighting and field of view on closing), `Mod` (the battle
constants: `maxViewDistance`, `maxDarknessToSeeUnits`, `lighting` `maxStatic`, `maxDynamic` and `enhanced`,
`tooMuchSmokeThreshold`, `extendedMovementCostRounding`, `extendedRunningCost`), `Armor` (move costs, `turnCost`, `turnBeforeFirstStep`, the
kneel and personal-light fields, `allowsMoving`, `allowsKneeling`).

### Branch decisions

- **Moves are commands; each resolves whole (D3).** Walking (with running, strafing and flying up and down), turning,
  kneeling and opening a door are commands on stable unit IDs, authorized by the session as branch 3's are. One
  command runs the reference's state machine to the end: every step, the turns before and between steps, doors opened
  on the way, the light and field-of-view updates after each step, units spotted, stops, falls and the units that
  fall with it. It returns ordered events (a step, a turn, a door, a fall, the units spotted, the stop and its reason)
  that clients replay; animation decides nothing. Events carry IDs and positions only, and clients refresh what they
  draw from viewer-scoped queries, so an event never shows a viewer more than its query does.
- **Animation time collapses.** The reference's walk phases, the UFO door's opening frames (`Tile::animate`, frame 7
  is open) and the state intervals are presentation. A UFO door a command opens is fully open when the command ends,
  and a walk that waits for a door to open (`unitOpensDoor` returning 3) continues at once. Where this changes a rule
  outcome, the difference goes into ADR 0040 (battle movement intentional differences), which the first slice that
  needs it creates.
- **The pathfinding is the reference's, on the port's tile arrays.** `getTUCost` with every movement type and action
  move (`BAM_NORMAL`, `BAM_RUN`, `BAM_STRAFE`, `BAM_SNEAK`), the armor move costs and the
  `extendedMovementCostRounding` modes; A* and the Bresenham shortcut with the reference's tie order, so the chosen
  path is the reference's for a given map and unit. `calculate` skips the Bresenham shortcut for a brutal unit
  (`BattleUnit::isBrutal`: hostile units by `brutalAI`, default on; neutral units by `brutalCivilians`, default off;
  player units when AI-controlled; the unit rule's `isBrutal` and `isNotBrutal` override), so S2 projects the unit
  rule's two flags and takes the options' defaults. `BAM_MISSILE` (guided missiles) is branch 5's and is not
  reachable from a branch 4 command. The `sneak` flag (the `sneakyAI` option and brutal AI) is a parameter that
  branch 6's AI sets; the player's moves pass it off. Paths and reachable tiles are queries (the legal-action display
  and the path preview read them).
- **What a player's walk can ask for.** With the `strafe` option off (its default), Ctrl and Alt do nothing in
  `primaryAction`, so a player's walk is always `BAM_NORMAL`; running, strafing and sneaking are parameters of the
  same rules that branch 6's AI uses (it runs). The `Walk` command carries the target, the move type (the player's
  client sends `BAM_NORMAL`) and `ignoreSpottedEnemies`, which Shift sets with default options
  (`ignoreSpottedEnemies = (run && extendedRunningCost) || shift`): such a walk does not stop on newly spotted units.
  S8 tests the other move types at rule level. A turret unit turns its body; the turret alone turns only when
  targeting or strafing (branch 5, and the `strafe` option).
- **Action costs and reserved time units arrive here.** `checkReservedTU`, `Tile::openDoor` and the reaction check
  read `BattleActionCost` for the reserved action and the main-hand weapon, so S1 ports the time and energy cost of an
  action from the item rules (`updateTU`, `haveTU`) and `checkReservedTU` for the player's side (the hostile side's
  percentage reserve is read by branch 6's AI). The reserve buttons (`battle.reserve-time-units-unsupported`,
  `battle.reserve-kneel-unsupported`) stay branch 5's; a loaded save's `TUReserved` and `kneelReserved` are honoured.
- **Visibility is per faction (D2).** The player's spotting is the reference's `visible` flag with its set and reset
  points: `calculateUnitsInFOV` and `getSpottingUnits` set it for a unit a player unit sees, and a non-player mover's
  finished step clears it before the step's field-of-view update sets it again. The hostile and neutral factions'
  spotting flags (`SpottedByHostile`, `SpottedByNeutral`, saved by branch 2 in `oxcePortBattle`) follow the same
  rule for their own units: set where a unit of that faction adds the unit to its visible-unit list, and cleared for
  every faction other than the mover's own at the end of each step, before the update. The reference lists only
  hostile and non-hostile pairs (`calculateUnitsInFOV`), so the neutral faction never spots player units and the
  player's units never list neutrals (the player's `visible` flag still covers neutrals). The saved flags are
  authoritative: a load keeps them and the recomputation only adds, as `loadMapResources`' `recalculateFOV` only sets
  the player's `visible`. Tile discovery for the player is the reference's `discovered` flags; the
  hostile and neutral factions' discovery is set by the same `calculateTilesInFOV` for their units and saved in
  `oxcePortBattle` (an ADR 0039 line in S4), since no reference rule reads it but a viewer of that faction does. The
  per-unit lists (visible units, units spotted this turn, visible tiles, line-of-fire tiles) are runtime state,
  recomputed on load as `loadMapResources` does.
- **Sight takes the voxel tests it needs from branch 5's list.** `visible` decides through `canTargetUnit` on the
  voxel map (LOFTEMPS data, `voxelCheck`, the line from `getSightOriginVoxel`), with smoke and fire density along the
  line and the darkness and camouflage limits, and `calculateLineVoxel` under them. `getSpottingUnits` also spots
  through `canTargetUnit`, from the snap-shot origin (`getOriginVoxel` for `BA_SNAPSHOT`) and with `checkViewSector`
  or the "got hit" memory (the AI's `wasHitBy`, a player unit's runtime hit state, `meleeAttackedBy` under
  `extendedMeleeReactions` 2). S4 ports this sight subset with `getOriginVoxel`'s snap-shot form; branch 5 extends
  the same code to firing (accuracy, exposure, the off-centre origins of `battleRealisticAccuracy`). Tile sight (`calculateTilesInFOV`) is the
  tile-space Bresenham line `calculateLineTile` with the blockage cache.
- **Lighting is runtime state.** The four light layers and the blockage cache are not saved; they are computed at
  generation (`BattlescapeGenerator` after the map), on load (`loadMapResources`) and after every change the
  reference recomputes them for. `togglePersonalLight` is saved battle state already; the option
  `oxceTogglePersonalLightType` is at its default (per battle). A loaded battle's lighting and field of view equal
  those of the never-saved battle, which the reload comparisons check.
- **User options are at their defaults.** `strafe` off, `battleRealisticAccuracy` off, `oxceEnableOffCentreShooting`
  off, `sneakyAI` off, `strictBlockedChecking` off, `oxceTogglePersonalLightType` 1, `forcedAbsoluteCosts` off (read by
  `BattleActionCost::updateTU`, S1), `updateTurnsSinceSeenByClue` on (`updateEnemyKnowledge`, S6), `brutalAI` on and
  `brutalCivilians` off (`isBrutal`, S2). A rule that reads another option
  names it in its slice and takes the default; options become settings with Phase 8's options screen.
- **Interruptions owned by later branches are refused before the first change (D4).** A walk, a kneel and a fall run
  the reference's checks for reaction fire (`checkReactionFire`, branch 5), proximity grenades and death traps
  (`checkForProximityGrenades`, branch 5) and the burn-floor abilities (`SPECAB_BURNFLOOR`, S9 of this branch); a
  player's turn does not (only the AI's turn-then-kneel does, branch 6). A command whose planned steps could reach
  one of them is refused with a named reason before any change, using only state conditions (no forecast):
  - reaction fire: an opposing unit that passes the state filters of `getSpottingUnits` and `determineReactionType`
    (not out, not over its out threshold, not the side, not a civilian unless the mover is a non-ignored hostile, a
    reaction score above zero, not respawning, and a reaction weapon that `canUseWeapon` allows for `BA_HIT` or
    `BA_SNAPSHOT` with ammo for that action and the time units for its cost, range and melee reach not checked)
    within `maxViewDistance` of a planned position. A unit that fails these filters can never react, so the command
    runs without one; `getSpottingUnits`' spotting side effects (`setVisible` for a player spotter,
    `addToVisibleUnits`) still happen at every step, kneel and landing for every spotter, weapon or not, and are
    ported with the commands (S5 first);
  - proximity: a planned position from whose tiles, widened by one on each side, any item's `fuseProximityEvent`
    could change something: a primed proximity grenade or grenade that would explode, a `proximityTrigger` item whose
    fuse it would enable (the fuse type `BFT_NONE` included), a `proximityExplode` item it would remove, an item whose
    glow it would change (a flare); or a death-trap floor (`STR_DEATH_TRAP_<n>`).

  The refusals over-approximate (a walk the reference would complete may be refused), never under-approximate,
  so every completed command is the reference's. Names: `battle.reaction-fire-unsupported` and
  `battle.proximity-trigger-unsupported` (branch 5), `battle.burn-floor-unsupported` (S9 retires it). Because these
  read units the player may not see, the refusal tells the player that an unseen unit is near; this named leak ends
  with branch 5. AI reactivation (`checkForReactivation`, the AI's `wantToEndTurn`) is AI-module state that only branch
  6's AI turn reads; it is not modelled until then and needs no refusal.
- **Falls and casualties.** A unit that falls onto another that finds no escape knocks it out (`knockOut`, or
  `convertUnit` for a unit with a `spawnUnit`), and `checkForCasualties` pushes `UnitDieBState` next, so the unit
  becomes unconscious within the same action, before a walk that fell resumes its path. Both are branch 6's. A
  command (a walk, and the first turn's spawn falls) whose planned fall lands on a unit is therefore refused before
  its first change with `battle.casualties-unsupported` (branch 6) unless `UnitFallBState`'s escape rule, applied to
  the state before the command with the mover's planned positions counted as occupied, finds that unit an escape
  tile; a fall whose escape exists runs, the unit below moving out of the way. S9 applies the same rule to the falls
  a burnt floor starts.
- **Fire, smoke and terrain destruction.** The tile part of `hit` (`hitTile`, `Tile::damage`, `destroy`, the
  objective count, the base defense module map, gravity, light and sight updates) is ported for the burn-floor
  abilities and is the code branch 5's explosions call. Its unit part (a corpse in the floor's inventory, `hitUnit`)
  and a destroyed part that starts a terrain explosion (`setExplosive`) are refused by branch 5's names. The fire and
  smoke spread of `SavedBattleGame::prepareNewTurn` (spread, burn-out, destruction, gravity, `Tile::prepareNewTurn`'s
  averaging) is a rule routine that branch 6's end of turn calls; its environmental damage (`applyEnvi`,
  `calculateEnviDamage`) is branch 6's. `closeUfoDoors` is likewise a routine for branch 6's end of turn.
- **When the battle computes light and sight.** As the reference: lighting after map generation; lighting and every
  unit's field of view on load; at the first turn, on closing the pre-battle inventory (`InventoryState`'s destructor:
  gravity on the selected unit's tile, item lighting, `recalculateFOV`) and in `updateSoldierInfo`; after each command
  that the reference follows with an update. Branch 2 and 3 tests that compare a battle with its reload or its
  generated form compare the recomputed state too.
- **Retired and inherited refusals.** S7 retires `battle.spawn-gravity-unsupported` (the spawned unit falls,
  `applyGravity`). Branch 3's notes disagree on `SpawnNeedsGravity` and `FallsAt` (once "the checks it replaces",
  once "the checks to keep"): the refusal goes, and S7 reuses `FallsAt`'s predicate where the fall needs it and
  removes what nothing calls. The battlescape buttons `battle.kneel-unsupported`,
  `battle.walking-unsupported`, `battle.unit-up-unsupported`, `battle.unit-down-unsupported` and the inputs
  `battle.visible-unit-center-unsupported`, `battle.secondary-action-unsupported` and
  `battle.middle-button-unsupported` become commands or views in S10 (or are reassigned there with a reason).
- **Scripts.** The armor's `visibilityUnit` script changes whether a unit is seen. Battle script bindings are branch
  8's, so a battle whose rules bind it is refused at the briefing and for a loaded battle's commands with
  `battle.visibility-script-unsupported` (branch 8), as branch 3 refuses the new-turn scripts. `selectMoveSoundUnit`
  only picks a sound the client plays; the client's sound is presentation (Phase 8), and S8 confirms that the hook
  changes no battle state.

### Slices

| Slice | Scope | Tests beyond the corpus |
|---|---|---|
| S0 | This plan and the [battle movement status](battle-movement-status.md) record | none |
| S1 | Movement groundwork: the battle constants (`extendedRunningCost` included) and armor movement fields in the runtime rules; `BattleActionCost` (`updateTU`, `haveTU`) and `checkReservedTU` for the player's side; unit movement state and primitives (`lookAt`, `turn`, `abortTurn`, `startWalking` and `keepWalking`'s tile changes collapsed to whole steps, `setTile`, `updateTileFloorState`, `haveNoFloorBelow`, the turn and kneel costs, `getReactionScore`) | each reserve mode and its fallbacks (auto to snap to aimed to none, kneel reserve); flat and percent action costs; a big unit's tiles after a step; lookAt over each direction and the shortest turn; `forcedAbsoluteCosts` at its default |
| S2 | Pathfinding: `getTUCost` (walls, floors, stairs and ladders, up and down, falling, big units, fire penalty, TFTD smoke cost, strafe), the floor and unit parts of `isBlocked` and `isBlockedDirection`, `canFallDown`, `isOnStairs`, `validateUpDown`, `bresenhamPath`, `aStarPath`, `calculate`'s destination adjustments, `findReachable` and `findReachablePathFindingNodes`; path, path cost and reachable-tile queries | synthetic maps: a wall, a door, a diagonal past a corner, stairs up and down, a ladder, a hole to fall through, a big unit, a flying unit going up, an unreachable tile, the TU limit, energy; the movement cost rounding modes; a brutal unit taking A*; the A* and Bresenham choices pinned |
| S3 | Lighting: the light layers, sun shading, fire, item and unit light, `addLight` classic and enhanced, the blockage cache and `blockage`, `horizontalBlockage`, `verticalBlockage` (`DT_NONE`, `DT_SMOKE`, `DT_IN`), personal light and its toggle; lighting after generation and on load; the tile shade query | day and night shade, a roof's shade at dusk, a light source object, a flare on the ground and in a hand, a unit on fire, enhanced lighting blocked by a wall, smoke reducing light, the toggle |
| S4 | Field of view and per-faction visibility: `calculateLineTile`, `calculateTilesInFOV` with the event sector, discovery (the walls east and south), the voxel sight subset (`voxelCheck`, `calculateLineVoxel`, `canTargetUnit` for sight, `getSightOriginVoxel`, `getOriginVoxel`'s snap-shot form, smoke and fire density, darkness, psi vision and camouflage limits), `visible`, `calculateUnitsInFOV` (the spotting fields: `tileLastSpotted`, turns since seen and spotted, the sniper fields, `visible`), `calculateFOV` by position and radius, `recalculateFOV`; when the battle runs them (load, the first turn, the inventory's close); per-faction spotting and discovery, the hostile and neutral discovery in `oxcePortBattle`; viewer queries for discovered tiles and the selected unit's visible units; the `visibilityUnit` refusal | a unit behind a wall unseen, through a window seen, in smoke and in darkness at each limit; psi vision through a wall; a hostile viewer's discovery saved and reloaded; a loaded battle's field of view equal to the never-saved one; each faction's view of the same unit |
| S5 | Turning and kneeling: `TurnUnit` (`UnitTurnBState`: the cost per eighth, a turret unit's body, a stop when it spots a unit, the reserve check; no reaction check) and `Kneel` (`BattlescapeGame::kneel`, the armor's `allowsKneeling`, floating units, the reaction-fire refusal and `getSpottingUnits`' spotting), light and sight after each; events | each turn direction and its cost; a stop on spotting; a turn next to an armed opposing unit completes; kneel and stand up costs; a refused kneel (flying, armor, TU); a kneel refused for an armed reactor in range, completed with a weaponless one in range (which spots the kneeler) and with none in range |
| S6 | Doors: `unitOpensDoor` (the right-click form through turning, the walking form for S8), `Tile::openDoor` (a swinging door to its alternative object, a UFO door opened at once), `checkAdjacentDoors`, the TU and energy cost and the reserve, `updateEnemyKnowledge`, light and sight through the doorway; `closeUfoDoors` as a routine for branch 6 (a big unit in the doorway keeps it open) | a swinging door, a UFO door row, a big unit refused a swinging door, a door blocked by a unit, not enough TUs (result 4) and the reserve (5); `closeUfoDoors` with a big unit in the door |
| S7 | Gravity and falling: `applyGravity` (units and items, flying units), the fall routine of `UnitFallBState` (units falling together, units below moving out of the way, the escape rule and the `battle.casualties-unsupported` refusal when a planned fall finds no escape), the reaction and proximity refusals and `getSpottingUnits`' spotting after landing; the spawn falls and `battle.spawn-gravity-unsupported` retires; the inventory's close applies gravity | a unit falling two levels; a flying unit over a hole; items falling to the floor below; a unit landing on another that escapes, and a start refused when it could not; a primed spawn item's unit falls at the first turn |
| S8 | Walking and flying: `Walk` (`UnitWalkBState` whole: the turns before and between steps and `turnBeforeFirstStep`, TU and energy per step, the reserve, doors on the way, units in the way, the ladder "jump", falls during a walk, stops on spotting and `ignoreSpottedEnemies`, `allowsMoving`, `getSpottingUnits`' spotting per step), the move types at rule level, unit up and down (`validateUpDown`), the interruption refusals; light and sight after each step; the selection kept; events | a straight walk, a diagonal, stairs, a ladder, through a door, a stop on spotting with its TU, a Shift walk past a spotted unit, a stop at a unit in the way, a fall onto a unit with and without an escape, a walk into a hole, flying up and down, running costs, a strafe step, not enough TU and energy, each refusal (each proximity case) |
| S9 | Fire, smoke and terrain destruction: `Tile::ignite`, `addSmoke`, `setFire`, `setSmoke`, `damage`, `destroy` (death objects, objective count, the base defense module map), `hitTile`, the tile path of `hit` with its light, sight and gravity; the burn-floor abilities on walks and falls (`battle.burn-floor-unsupported` retires); the fire and smoke spread routine of `prepareNewTurn` for branch 6 | a burn-floor unit burning a hole and falling through; fire spreading past and stopped by a wall; fire burning out and destroying a flammable object; smoke spreading, rising from fire and averaging; an objective destroyed; a terrain explosion and a corpse hit refused by branch 5's names |
| S10 | Battlescape client: walk by tile click with the path preview and its TU and energy markers, Shift for `ignoreSpottedEnemies`, kneel, unit up and down, turn by right-click, the visible-unit buttons, discovered tiles and shade on the plan view, events replayed; the later-button refusals of this branch replaced | scripted loop tests: walk, turn, kneel, open a door, fly up; a refused walk shows its reason; only the viewer's discovered tiles are drawn |
| S11 | Acceptance on branch 1's battles and the private active-battle saves (below); pathfinding and field-of-view timings in the performance baselines | none beyond acceptance |
| — | Branch audit (`git diff main...HEAD`) | — |

Acceptance: in each acceptance battle of branch 1 that starts (the three Rosigma battles stay refused at the briefing
by branch 8's `battle.new-turn-script-unsupported`), after the first turn a soldier leaves the craft along a computed
path, turns, kneels and stands, and opens a door where the map has one; the soldier's field of view discovers tiles
and spots units as the reference's rules say, the battle saves and reloads with equal lighting, field of view and
spotting, and the battle still validates. Each private active-battle save loads with its lighting and field of view
computed, and a player unit that is free to move walks (or the walk is refused by a named reason of a later branch).
A viewer of another faction sees only that faction's discoveries and spotted units. Pathfinding and field-of-view
costs on the largest acceptance map are recorded in the performance baselines.

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
