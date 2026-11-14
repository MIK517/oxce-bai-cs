# Phase 7 — Playable battlescape implementation plan

Plan date: 2026-10-18. Baseline: `main` at `2b0c81fb` (audit cleanup, PR #77). Reference:
`../oxce-bai` (`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`).
The [2026-11-14 audit](project-audit-2026-11-14.md) added work to branches 5 and 6: [audit follow-up](#audit-follow-up-2026-11-14).
Status: branch 1 (`claude/battle-generation`) is complete and merged (PR #78); its slices, audits and evidence are
recorded in [battle generation status](battle-generation-status.md). Branch 2 (`claude/battle-save`) is complete and merged (PR #79):
[battle save status](battle-save-status.md). Branch 3 (`claude/battle-session-ui`) is complete and merged (PR #80):
[battle session and UI status](battle-session-ui-status.md). Branch 4 (`claude/battle-movement`) is complete and merged (PR #81):
[battle movement status](battle-movement-status.md). Branch 5 (`claude/battle-combat`) is in progress:
[battle combat status](battle-combat-status.md).

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
  the battle and the host offers the last save. No battle forecast. Branch 4 does take a per-command trial copy for walks, up and down moves and the first turn, to find late refusals (ADR 0040). Strategic ticks that reach a
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

## Audit follow-up (2026-11-14)

The [2026-11-14 audit](project-audit-2026-11-14.md) compared the battle and session code with ADR 0037, ADR 0038 and
D1–D7 above. No decision changes; the work below makes the code match them. Each finding has one owner.

| Finding | Change | Owner |
|---|---|---|
| A1 no battle registry, A2 context threaded by hand | A battle runtime object owns the state, rules, random source, campaign link, options and revision, set once when a battle is generated or restored, with its own command registry; `CampaignBattles` keeps start, restore and one forward (later debriefing) | Branch 5 S5b |
| A3 trial copy | S5b gives the copy the campaign link. S9 retires the last refusal that needs a copy, and S11 deletes `TrialRefusal`. A copy that must stay is listed in the branch 5 status record with its reason and its Release cost, and branch 6 does not add callers | Branch 5 S5b, S9, S11 |
| A4 fault path | An exception in a battle command faults the battle (ADR 0038); an unknown command is a refusal, not an exception; `GeoscapeClient` stops deciding what a host failure means | Branch 5 S5b |
| C1 combat events | An ordered event sink in the battle runtime; S3–S5's outcomes raise events through it, and S6–S9 raise theirs as they are ported | Branch 5 S5b, then each slice |
| C2 script hooks | A hooks interface with no-op defaults, called where the reference runs a tactical script. S5b adds the call sites in S1–S5's code, and every later slice of branches 5–7 adds its own while it ports the function. Branch 6 carries one hook family end to end (bindings, a mod's script, the refusal for it retired). Branch 8 then binds the remaining hooks without moving rule code | Branch 5 S5b onward; branch 6 (one family); branch 8 (the rest) |
| B1 queries, B2 events, B3 clock, B4 ownership | The session hands each player a query facade that fixes the viewer; the battle filters events per viewer and the session no longer sends one player's command to the others; clock requests are commands, and the host pumps the clock and the dogfight timer; unit ownership comes from the battle's controllers for every faction | Branch 6, first slice after its planning, before any AI action raises an event |
| A5 rule references | Units and items hold their rule objects, resolved at creation and restore; one projection per family for items, armors and units | Branch 6, with the AI's rule reads |
| B5 layering | Commands, events, views and reasons in a contracts namespace; a namespace-level architecture test; `Oxce.Engine` reads through the session, not `CampaignState` | Branch 6, with the session slice |

Branch 6's planning slice schedules its rows above before its first rule slice and may split them into slices of its
own.

## Delivery shape

Sequential feature branches, each from the previous merged `main`. Every branch keeps commands, script providers,
saves and UI actions integrated for what it adds; headless acceptance comes before UI in each branch.

| Order | Branch | Acceptance boundary |
|---|---|---|
| 1 | `claude/battle-generation` | The deployment request generates a validated battle headlessly: terrain, map script, map blocks, routes, units, inventories, objectives, viewer-scoped queries |
| 2 | `claude/battle-save` | `battleGame` loads into `BattleState` and is written back; the reference loads written saves (checked by a conformance reader, [below](#branch-decisions)); the opaque preservation retires and the "saving during a battle" restriction narrows to saving before the first turn |
| 3 | `claude/battle-session-ui` | Session layer (player identity, event broadcast, host clock and pause policy, per-actor quotes), screen stack, briefing and the first turn (`startFirstTurn`), battlescape view: camera, levels, selection, inventory, legal-action display |
| 4 | `claude/battle-movement` | Pathfinding, walking, flying, kneeling, turning, doors, falling, per-faction visibility, light, fire and smoke spread, terrain destruction from movement |
| 5 | `claude/battle-combat` | Line of fire and voxel tests, accuracy, projectiles, explosions, armor and damage, wounds, death and unconsciousness, melee, reaction fire |
| 6 | `claude/battle-turns-ai` | The session boundary for queries, events and the clock, rule references and the contracts namespace ([audit follow-up](#audit-follow-up-2026-11-14)), then: turn and phase processing, morale and panic, psi, item uses (medikit, scanner, mind probe), skills, special abilities, environmental conditions, AI controller |
| 7 | `claude/battle-outcome` | Objectives, abort and end of mission, recovery, debriefing and its campaign effects, final-mission victory; the [first acceptance scenario](phase-6-status.md#first-phase-7-acceptance-scenario) closes |
| 8 | `claude/battle-scripts-corpus` | Remaining tactical script hooks and bindings (bound at the call sites branches 5–7 left), representative deployments (UFO, terror, base defense, alien base, final, underwater, custom), named restriction retirement, Phase 7 closure |

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
  `battle.turn-one-revive-unsupported` (branch 6; reassigned to branch 5 S4), `battle.no-live-aliens` (the `AliensCrashState` exit, branch 7),
  `battle.preview-unsupported` and `battle.reinforcements-unsupported` (above), `battle.casualties-unsupported` (branch 6, reassigned to branch 5 S4: a unit `checkForCasualties` would take out; refused at the briefing and for the commands of a running battle, a loaded one included), `battle.turn-recovery-script-unsupported` (branch 8: a scripted time unit or energy recovery), `battle.spawn-gravity-unsupported` (branch 4, retired by S7: its falls are ported). Completing this retires `battle.save-before-first-turn`, the restriction branch 2 handed over.
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
  rule's two flags and takes the options' defaults. `BAM_MISSILE` (guided missiles) is branch 5's (reassigned to branch 6 by branch 5's plan: only the AI uses it) and is not
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
  (`checkForProximityGrenades`, branch 5) and the burn-floor abilities (`SPECAB_BURNFLOOR`, whose floor-burning hit S9 ports; only a hit that reaches a unit or an explosion is refused); a
  player's turn does not (only the AI's turn-then-kneel does, branch 6). A command whose planned steps could reach
  one of them is refused with a named reason before any change, using state conditions only; a walk, an up or down move and the first turn run on a trial copy of the battle first (ADR 0040), and a refusal the copy meets is the command's refusal:
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
  `battle.proximity-unsupported` (branch 5), `battle.hit-unit-unsupported` and `battle.terrain-explosion-unsupported` (branch 5; S9 retired `battle.burn-floor-unsupported`). Because these
  read units the player may not see, the refusal tells the player that an unseen unit is near; this named leak ends
  with branch 5. AI reactivation (`checkForReactivation`, the AI's `wantToEndTurn`) is AI-module state that only branch
  6's AI turn reads; it is not modelled until then and needs no refusal.
- **Falls and casualties.** A unit that falls onto another that finds no escape knocks it out (`knockOut`, or
  `convertUnit` for a unit with a `spawnUnit`), and `checkForCasualties` pushes `UnitDieBState` next, so the unit
  becomes unconscious within the same action, before a walk that fell resumes its path. Both are branch 6's. A
  command (a walk, and the first turn's spawn falls) whose planned fall lands on a unit is therefore refused before
  its first change with `battle.fall-knockout-unsupported` (branch 6; reassigned to branch 5 S4) unless `UnitFallBState`'s escape rule, applied to
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
| S7 | Gravity and falling: `applyGravity` (units and items, flying units), the fall routine of `UnitFallBState` (units falling together, units below moving out of the way, the escape rule and the `battle.fall-knockout-unsupported` refusal when a planned fall finds no escape), the reaction and proximity refusals and `getSpottingUnits`' spotting after landing; the spawn falls and `battle.spawn-gravity-unsupported` retires; the inventory's close applies gravity | a unit falling two levels; a flying unit over a hole; items falling to the floor below; a unit landing on another that escapes, and a start refused when it could not; a primed spawn item's unit falls at the first turn |
| S8 | Walking and flying: `Walk` (`UnitWalkBState` whole: the turns before and between steps and `turnBeforeFirstStep`, TU and energy per step, the reserve, doors on the way, units in the way, the ladder "jump", falls during a walk, stops on spotting and `ignoreSpottedEnemies`, `allowsMoving`, `getSpottingUnits`' spotting per step), the move types at rule level, unit up and down (`validateUpDown`), the interruption refusals; light and sight after each step; the selection kept; events | a straight walk, a diagonal, stairs, a ladder, through a door, a stop on spotting with its TU, a Shift walk past a spotted unit, a stop at a unit in the way, a fall onto a unit with and without an escape, a walk into a hole, flying up and down, running costs, a strafe step, not enough TU and energy, each refusal (each proximity case) |
| S9 | Fire, smoke and terrain destruction: `Tile::ignite`, `addSmoke`, `setFire`, `setSmoke`, `damage`, `destroy` (death objects, objective count, the base defense module map), `hitTile`, the tile path of `hit` with its light, sight and gravity; the burn-floor abilities on walks and falls (`battle.burn-floor-unsupported` retires); the fire and smoke spread routine of `prepareNewTurn` for branch 6 | a burn-floor unit burning a hole and falling through; fire spreading past and stopped by a wall; fire burning out and destroying a flammable object; smoke spreading, rising from fire and averaging; an objective destroyed; a terrain explosion and a corpse hit refused by branch 5's names |
| S10 | Battlescape client: the viewer-scoped path, path cost and reachable-tile queries over `BattlePathfinder` (S2 ports the search; plan change from S2's row), walk by tile click with the path preview and its TU and energy markers, Shift for `ignoreSpottedEnemies`, kneel, unit up and down, turn by right-click, the visible-unit buttons, discovered tiles and shade on the plan view, events replayed; the later-button refusals of this branch replaced | scripted loop tests: walk, turn, kneel, open a door, fly up; a refused walk shows its reason; only the viewer's discovered tiles are drawn |
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

## Branch 5 — Battle combat

Reference: `BattlescapeGame` (`primaryAction`'s targeting part, `handleNonTargetAction`, `checkForCasualties`,
`convertUnit`, `convertInfected`, `dropItem`, `spawnNewUnit` and `spawnNewItem` after an explosion, `setTUReserved`,
`setKneelReserved`, `checkForProximityGrenades`, `autoEndBattle`, `kneel`'s reaction check), `ActionMenuState` (the
actions a weapon offers and their checks), `PrimeGrenadeState`, `UnitTurnBState` (the targeting form),
`ProjectileFlyBState`, `Projectile` (`calculateTrajectory`, `calculateThrow`, `applyAccuracy`), `ExplosionBState`,
`MeleeAttackBState`, `UnitDieBState`, `UnitWalkBState` and `UnitFallBState` (their reaction checks), `TileEngine` (line
of fire: the firing forms of `canTargetUnit` and `canTargetTile` with `rememberObstacles`,
`adjustTargetVoxelFromTileType`, `getOriginVoxel` for every action, `calculateLineVoxel` with its trajectory,
`calculateParabolaVoxel`, `validateThrow`; damage: `hit` (its unit part), `hitUnit`, `explode`, `detonate`,
`checkForTerrainExplosions`, `awardExperience`; melee: `meleeAttack`, `meleeAttackCalculate`, `validMeleeRange`,
`validTerrainMeleeRange`; reaction fire: `checkReactionFire`, `getReactor`, `determineReactionType`, `tryReaction`;
`itemDrop`, `itemDropInventory`, `getArcDirection`), `AIModule` (the reaction form of `brutalScoreFiringMode`,
`explosiveEfficacy`, `brutalExplosiveEfficacy`, `validateArcingShot`, `getSideFacingToPosition`,
`shouldAvoidMeleeRange`), `BattleUnit` (`damage`, `getFiringAccuracy`, `getAccuracyModifier`, `knockOut`, `instaKill`,
`moraleChange`, `reduceByBravery`, `setFatalWound`, `getOverKillDamage`, `isWoundable`, the experience adders with
`rememberXP` and `nerfXP`, `getWeaponForReactions`, `getUtilityWeapon`, `getSpecialWeapon`, `aim`, `killedBy`,
`getHitState`), `BattleItem` (`getAmmoForAction`, `spendAmmoForAction`, `haveNextShotsForAction`, `fuseThrowEvent`,
`fuseProximityEvent`, `setFuseTimer`, `convertToCorpse`, `getArcingShot`, `getActionConf`), `SavedBattleGame`
(`canUseWeapon`, `reviveUnconsciousUnits`, `removeUnconsciousBodyItem`, `placeUnitNearPosition`, `convertUnit`, the
morale modifiers, `createItemForTile`), `SavedGame::killSoldier`, `Inventory` (`unload`, loading a weapon, priming),
`RuleItem` (battle type, power and its bonuses, the damage and melee types with `damageAlter` and `meleeAlter`,
accuracy and ranges, the action configs, clip and ammo slots, explosion radius, the shotgun and arcing fields,
waypoints, the `RuleStatBonus` fields), `RuleDamageType`, `RuleStatBonus`, `Armor` (side armor, damage modifiers,
corpses, the melee dodge and threat fields, `selfDestructItem`, pain and zombie immunity), `Unit` (`spawnUnit`,
`capturable`), `Mod` (the combat constants), `Options` (the defaults below).

### Branch decisions

- **Attacks are commands; each resolves whole (D3).** `Attack` (unit, weapon item, action type: aimed, snap, auto,
  throw, launch or hit; target position; the launch and spray waypoints; Ctrl's force fire), priming and unpriming,
  the inventory's ammo loading and unloading, and the reserve buttons are commands on stable IDs, authorized by the
  session as branch 3's are. One command runs the reference's state chain to its end. The reference's `statePushFront`,
  `statePushNext` and `statePushBack` become one ordered queue in the rules, so explosions, deaths and reactions happen
  in the reference's order. The chain covers the targeting turn, each shot of the action, each projectile's impact,
  the explosions and the terrain explosions they set off, the hits, the casualty checks and deaths, conversions and
  revivals, and the reaction fire the action draws with the reactions' own shots, until the queue is empty. The
  command returns ordered events (a turn, a shot with its trajectory voxels, an impact, an explosion with its center and radius, a
  unit hit, a unit dead or unconscious, an item created or removed, a terrain part destroyed, a reaction) that clients
  replay. Events carry IDs and positions, and a client draws a unit only as its query shows it (branch 4's rule).
  Animation time collapses (the projectile's flight, `ExplosionBState`'s frames, the dying unit's fall); where that
  changes a rule outcome, ADR 0041 (battle combat intentional differences) records it, created by the first slice that
  needs it. A dying unit ends facing direction 3 (`UnitDieBState`: its constructor sets it for non-direct damage or an
  unconscious unit, `think` turns it there under direct damage); that is a saved outcome and is ported.
- **Interim refusals between slices.** A slice that ports part of the chain refuses by name, before any change, what
  a later slice of this branch ports, so every completed command stays the reference's (D4, as branch 4 did):
  - from S3 until S4: a hit that would take a unit out (health to 0, or stun reaching health) or set it to respawn
    (infection, for `convertInfected`) keeps `battle.casualties-unsupported`;
  - from S3 until S5: a hit that would push the self-destruct explosion keeps `battle.terrain-explosion-unsupported`
    (or a new interim name the slice chooses and S5 retires);
  - from S6 until S9: the reaction check after a melee attack, and from S7 and S8 until S9 the one after an action's
    last projectile, keep `battle.reaction-fire-unsupported` (`HasPossibleReactor`), as walks and kneels do.

  Each slice's status section names the interim refusals it leaves, and the slice that retires them checks none is
  left. A check that depends on the hit's random draws runs the command on the trial copy first (ADR 0040).
- **A refusal before the turn, a stop after it.** The checks `ActionMenuState::handleAction` makes before an action
  starts are refusals before any change. They differ by action:
  - aimed, snap and auto shots: the weapon offers the action, its research (`getRequirements`) and `canUseWeapon`;
    no time unit or ammo check;
  - a throw: none of these (the action menu lists it);
  - a launch: research, `canUseWeapon`, `haveTU` and ammo for `BA_LAUNCH`, then the waypoint count in `primaryAction`;
  - a melee hit: research, `canUseWeapon`, `haveTU`, and a target in `validMeleeRange` in the facing direction or
    `validTerrainMeleeRange` (`STR_THERE_IS_NO_ONE_THERE`); there is no targeting turn (below);
  - priming: research and `canUseWeapon`, then the fuse (`PrimeGrenadeState` for a settable one).

  For shots, throws and launches, `primaryAction` then pushes the targeting turn (`UnitTurnBState`, which pays each
  eighth and does not check the reserve) with no further check. Only then does `ProjectileFlyBState::init` check time
  units (`haveTU`), ammo (`getAmmoForAction`: `STR_NO_AMMUNITION_LOADED`), range and throw range, and
  `createNewProjectile` the line of fire. A check that fails after the turn ends the command with its reason as an
  event (`STR_NOT_ENOUGH_TIME_UNITS`, `STR_NO_AMMUNITION_LOADED`, `STR_OUT_OF_RANGE`, `STR_NO_LINE_OF_FIRE`,
  `STR_UNABLE_TO_THROW_HERE`), as a walk's later stop does (ADR 0040). Only when the shooter already faces the target,
  so the turn changes nothing, is such a failure a refusal. The line-of-sight rule of a targeted unit (`isLOSRequired`)
  belongs to the psi amp and the mind probe, which are branch 6's.
- **Accuracy, damage and randomness (D7).** Accuracy (`getFiringAccuracy`, `getAccuracyModifier`, the kneel bonus, the
  one-handed penalty, `applyAccuracy`'s classic form), damage rolls (`RuleDamageType::getRandomDamage` for every random
  type), the side and body part hit, catching fire and the infection chance draw from the battle's stream in the
  reference's call order; exact streams stay a non-goal. Tests force hits and misses with a scripted random source and
  pin structure (which voxel, unit and tiles are hit) under fixed draws.
- **Stat bonuses.** `RuleStatBonus` (`damageBonus`, `meleeBonus`, `accuracyMultiplier`, `meleeMultiplier`,
  `throwMultiplier`, `closeQuartersMultiplier`, with the defaults `setFiring`, `setMelee`, `setThrowing`, `setStrength`,
  `setFlatHundred` and `setCloseQuarters`) is a polynomial of unit statistics that the reference compiles into a script.
  The port evaluates the coefficient form natively: the defaults and a YAML mapping, with the `statDataMap` stat
  functions, the four coefficients and the generated script's scaling by 1000 and rounding. A bonus written as script
  code is a tactical script binding (branch 8): a battle whose items or armors bind one is refused at the briefing and
  for its commands with `battle.stat-bonus-script-unsupported` (branch 8). Branch 4's
  `battle.flare-power-bonus-unsupported` narrows to that case and merges into the new name (S1).
- **Combat script hooks are branch 8's.** `hitUnit`, `damageUnit` and `damageSpecialUnit` (with their ammo forms),
  `tryMeleeAttack` (item and unit), `reactionWeaponAction`, `reactionUnitAction`, `reactionUnitReaction` and
  `awardExperience` run on every hit, reaction and experience award. A battle whose rules bind one is refused at the
  briefing and for its commands with `battle.combat-script-unsupported` (branch 8), checked where
  `battle.visibility-script-unsupported` is (`CampaignBattles`' gate). The corpses and converted units this branch
  creates follow the generator's handling of `createItem` and `createUnit` (`UnitEquipment`, branch 8). The
  `vaporParticle*`, `recolor*` and `select*Sprite` hooks are presentation (Phase 8).
- **Death and unconsciousness belong here (plan change).** A hit takes units out, and the reference handles that within
  the same action through `checkForCasualties` and `UnitDieBState`. So this branch ports the whole casualty path:
  - the kill statistics and the murderer;
  - the morale changes a death or a stun brings (the numbers only; panic and berserk are branch 6's turn processing);
  - `killSoldier`: the soldier goes to the memorial with its `SoldierDeath` during the battle;
  - corpses from the armor's `corpseBattle`, `itemDropInventory` (`weaponSelfDestruction` off), the unconscious body
    item and its removal, and `instaKill` of a unit that falls unconscious and cannot be captured;
  - the spotting resets, `convertUnit` (`spawnUnit`), `convertInfected`, `reviveUnconsciousUnits` and `knockOut`;
  - the saved side effects of `UnitDieBState`: its constructor marks every node that is not a dummy within a squared
    distance of 4 of a dying hostile unit `TYPE_DANGEROUS` (a saved node type), and `clearUnitSelection` clears the
    saved `selectedUnit` and `undoUnit` when they name the unit.

  This retires three refusals that branches 3 and 4 gave to branch 6:
  - `battle.casualties-unsupported`, with the `HasUncheckedCasualty` and floorless-walker gates: the battlescape
    constructor's `checkForCasualties` runs where the reference opens the battlescape;
  - `battle.fall-knockout-unsupported`: a fall with no escape knocks the unit below out;
  - `battle.turn-one-revive-unsupported`: `startFirstTurn`'s `reviveUnconsciousUnits(false)` and `convertInfected`,
    with the time unit and energy recovery (`updateUnitStats(true, false)`) that branch 3 ports for `prepareNewTurn`.

  Bleeding, stun recovery and burning at the start of a turn stay branch 6's. `autoEndBattle` does nothing with
  `battleAutoEnd` off (its default), so a death ends no battle; ending the battle stays branch 7's.
- **Melee joins this branch (plan change).** `determineReactionType` tries a player's preferred weapon, then the melee
  utility weapon, then the main weapon, each with `BA_HIT` before `BA_SNAPSHOT`; `brutalScoreFiringMode`
  scores melee reactions, and close-quarters combat (`enableCloseQuartersCombat`, off by default but set by mods) rolls
  `meleeAttack` before a shot. Melee therefore moves from branch 6 to this branch: `MeleeAttackBState`, `meleeAttack`,
  `meleeAttackCalculate`, `validMeleeRange`, `validTerrainMeleeRange`, the melee dodge, and a firearm's butt
  (`meleeType`). These stay branch 6's: psi (`PsiAttackBState` and the psi amp's actions), the item uses (medikit,
  scanner, mind probe), skills, and the special abilities other than burning floors (branch 4) and exploding on death (`SPECAB_EXPLODEONDEATH`,
`SPECAB_BURN_AND_EXPLODE`, this branch's self-destruct). The delivery table above
  is updated.
- **Explosions and the destruction branch 4 left.** `explode` (the radius spread over the blockage branch 4 ported,
  `RadiusReduction` and `RadiusEffectiveness`, `battleExplosionHeight` 0), `detonate`, `checkForTerrainExplosions` and
  the chain of terrain explosions, items on tiles (`ToItem`), the unit part of `hit` and `hitUnit`, self-destruct
  (`selfDestructItem`), and the units and items `ExplosionBState::explode` spawns (branch 3's spawn code). This retires
  `battle.hit-unit-unsupported` and `battle.terrain-explosion-unsupported` (branch 4), and branch 1's two generation
  refusals: a crashed UFO's power sources (`explodePowerSources`, a hidden explosion with its casualty check) and map
  grenades primed at -2 (`explodeOtherJunk`). `battle.module-map-unsupported` also retires. A module destroyed in a
  block that a saved module map lacks indexes past a `std::vector` (TileEngine.cpp:3715 and 4061), which is
  undefined. ADR 0039 says branches 5–7 must refuse such a block; for the destruction this plan changes that, as
  AGENTS.md allows for undefined behaviour: the port skips that count, S5 records it in ADR 0041 and amends ADR 0039's
  line to point there. The AI (branch 6) and the debriefing (branch 7) keep ADR 0039's rule.
- **Throwing, priming, launching and proximity.** This branch ports:
  - `BA_THROW`: `validThrowRange`, `getMaxThrowDistance`, `validateThrow`, `calculateThrow`, and the landing item
    through `itemDrop` (gravity, light) and `fuseThrowEvent`;
  - `BA_PRIME` and `BA_UNPRIME` (`PrimeGrenadeState`, fuse timers);
  - `BA_LAUNCH` with waypoints (`getCurrentWaypoints`; the missile flies from waypoint to waypoint);
  - all of `checkForProximityGrenades`: the explosion, enabling fuses, removing `proximityExplode` items, the flare's
    glow, death traps.

  This retires `battle.proximity-unsupported` and narrows branch 4's death-trap test (any `STR_DEATH_TRAP_<n>`) to
  the reference's. `BAM_MISSILE` is the AI's search for a blaster launcher's waypoints (`AIModule` only), so it moves
  to branch 6 (a plan change from branch 4's note); the player places the waypoints. Grenades exploding at the end of
  a turn (`fuseTimerEvent`) are branch 6's. The inventory's loading, unloading and quick swap (`Inventory::unload`
  with `quickUnload`, a load with its cost, the Shift swap of Inventory.cpp:1029–1067, each with its battle and
  pre-battle rules) retire `battle.inventory-ammo-unsupported`. Priming and unpriming from the inventory (a right
  click) exist only in an inventory without time units, the pre-battle one (`Inventory.cpp:1135`, `!_tu`; a base's
  inventory only with `includePrimeStateInSavedLayout`, off by default); in battle the action menu primes. They
  retire `battle.inventory-prime-unsupported`. `battle.inventory-throw-unsupported` names no reference feature (the inventory
  screen does not throw) and is removed.
- **Reaction fire is real; the leak ends.** `checkReactionFire` runs where the reference calls it: after each walk
  step, after a kneel (`BattlescapeGame::kneel` and the AI's turn-then-kneel), after a landing, after a melee attack,
  and when a shooting or throwing action's last projectile is done. It runs:
  - the reactor list of `getSpottingUnits`, whose spotting branch 4 ported: `determineReactionType` per spotter and,
    with a reaction fire threshold above 0, the accuracy filter (`calculateLimits`' dropoff, `getHitChance`, out of
    range);
  - `getReactor`;
  - `determineReactionType`: the preferred weapon, `reactionsDisabled*`, the melee priority, and `canUseWeapon` whole
    (research, AI use delay, depth, two-handed, mana);
  - `tryReaction`: the hostile reactor's `explosiveEfficacy`, the reaction form of `brutalScoreFiringMode` (with the AI
    module `tryReaction` creates for a unit that has none), and the reaction chance and its draw.

  The reaction's shot or melee hit resolves within the same command, and a walk that drew a reaction stops
  (`UnitWalkBState`). Hits set `BattleUnit.HitState` and `resetUnitHitStates` clears it where the reference does.
  `HasPossibleReactor` and `battle.reaction-fire-unsupported` retire, which ends the refusal that told the player an
  unseen unit was near. The acceptance driver's `WithOpponentsSpent` goes. The trial copies (ADR 0040) exist to catch
  refusals met after a change. When the last of those refusals retires, S11 deletes `TrialRefusal`. A copy that must
  stay is listed in the status record with the refusal that needs it and its Release cost on the largest acceptance
  map ([audit follow-up](#audit-follow-up-2026-11-14), A3).
- **The battle runtime (S5b; audit A1, A2, A4, C1).** One object holds what a battle command needs: the state, the
  rules, the random source, the campaign link, the options, the revision and an ordered event sink. It is set when a
  battle is generated or restored, so no entry point assigns the random source and no rule method takes the rules as
  a parameter it could get wrong. It has the battle's command registry; `CampaignBattles` forwards a battle command to
  it once and keeps the campaign's side (start, restore, the hand-over). An exception in a battle command faults the
  battle: later commands are refused and the host offers the last save (ADR 0038). An unknown command is a refusal.
  The slice changes no rule outcome, refusal text or save output.
- **Events come from the sink.** A rule raises its events where the outcome happens, in the reference's order; a
  command returns what the sink collected. `ExplosionLog` becomes events of the sink. S6–S9 raise the events of the
  attack chain as they port it, so S10 replays them without a second pass over the rules.
- **Script hook call sites (audit C2).** The runtime carries a hooks interface with a method per tactical script the
  reference runs (`hitUnit`, `damageUnit`, `tryMeleeAttack`, the reaction hooks, `awardExperience`, the visibility and
  stat bonus scripts, and the rest as they are met). The default does nothing and returns the unscripted values. Each
  slice calls the hook where the reference calls the script, with the values the script may change. The script
  refusals stay until a hook is bound (branch 6 for the first family, branch 8 for the rest), so behaviour is
  unchanged.
- **User options at their defaults** (added to branch 4's list):
  - `battleUFOExtenderAccuracy` off;
  - `battleRealisticAccuracy` and `oxceEnableOffCentreShooting` off: the non-realistic `applyAccuracy` and
    `canTargetUnit`, no `checkVoxelExposure`;
  - `shootingSpreadMode` 1, not vanilla's 0: `applyAccuracy`'s uniform spread (`xyShift` scaled by 0.839) and its
    redraws, which S2 ports as the default form;
  - `useChanceToHit` on: the accuracy the cursor shows (`Map`) and the reaction threshold filter of
    `getSpottingUnits` go through `Projectile::getHitChance` with the mod's hit chance tables;
  - `diagTerrainMelee` off (`validTerrainMeleeRange`);
  - `forceFire` on: the command's force-fire flag, which the client sets with Ctrl;
  - `battleConfirmFireMode` off (the desktop default), `battleExplosionHeight` 0, `battleInstantGrenade` off,
    `weaponSelfDestruction` off, `battleAutoEnd` off, `battleNotifyDeath` off (a notice only).

  The combat constants are rule data: `damageRange`, `explosiveDamageRange` and `fireDamageRange`,
  `kneelBonusGlobal`, `oneHandedPenaltyGlobal`, `noLOSAccuracyPenaltyGlobal`, the close-quarters fields, the hit
  chance tables, `ai.reactionFireThreshold` and `reactionFireThresholdCiv` (`getReactionFireThreshold`, read by
  `getSpottingUnits`), `tuRecoveryWakeUpNewTurn` (`reviveUnconsciousUnits`), `extendedExperienceAwardSystem`,
  `extendedSpotOnHitForSniping`, `stunningImprovesMorale`, and the morale loss modifiers.
- **Hidden information.** The attack queries take the viewer. These are the action menu's actions with their costs, and
  the accuracy for a target (`ActionMenuState`, the cursor). They answer only for a unit of the viewer's side on its
  turn, aiming at a tile the viewer has discovered. A refusal or stop reason never names a unit the viewer has not
  spotted. Events of a shot by a shooter the viewer has not spotted carry the trajectory (the reference draws the
  projectile), and the client does not draw the shooter.
- **Saved state.** What this branch changes is typed battle state since branch 2: health, stun, wounds per body part,
  armor per side, morale, fire, the murderer and fatal-shot fields, statistics, experience, the AI's `wasHitBy`, ammo
  quantities, fuses, and corpses with their units. A battle reloaded after combat equals the live one in its saved and
  derived state; the reload comparisons check this. The hit log (`appendToHitLog`) is runtime text that the next turn's
  screen shows; it is not saved and is left to branch 6's end of turn and Phase 8.
- **Out of scope.** Turn processing and its effects: fuses at the end of a turn, bleeding, stun and morale recovery,
  burning units, panic, smoke and fire damage (branch 6). Also branch 6's: psi, item uses, skills, and the AI's own
  turns. The end of the battle and the debriefing are branch 7's, script bindings branch 8's. Sounds, sprites and
  the projectile and explosion animations are Phase 8 presentation.

### Slices

| Slice | Scope | Tests beyond the corpus |
|---|---|---|
| S0 | This plan and the [battle combat status](battle-combat-status.md) record | none |
| S1 | Combat groundwork: the item rules' combat fields (battle type, power, the damage and melee types with `damageAlter` and `meleeAlter` as a full `RuleDamageType`, accuracy per action, ranges and dropoff, the action configs with shots and ammo slots, clip size and compatible ammo, explosion radius, shotgun, arcing, `ignoreAmmoPower`, power range reduction, waypoints, `LOSRequired`, two-handed, `specialUseEmptyHand`, fire extinguisher, zombie unit, and the research and depth fields `canUseWeapon` reads); `RuleStatBonus`'s coefficient form; the armor's damage modifiers, corpses, dodge and threat, self-destruct, pain and zombie immunity; the unit rule's capturable and spawn fields; the combat constants; `RuleDamageType::getRandomDamage` for every random type and the `get*FinalDamage` conversions; `battle.combat-script-unsupported` and `battle.stat-bonus-script-unsupported` | each projected item, armor and unit rule field at its default and read from a mod (the action configs' fallbacks, `damageAlter` over the item's type); each combat constant at its default and read; each random type's range; each conversion; a stat bonus mapping, a default, and the rounding at a half; a scripted bonus and a bound combat hook refused at the briefing and for a loaded battle's commands |
| S2 | Line of fire and accuracy: `getOriginVoxel` for each action, the firing forms of `canTargetUnit` and `canTargetTile` with `rememberObstacles`, `adjustTargetVoxelFromTileType`, `calculateLineVoxel` storing the trajectory, `calculateParabolaVoxel`, `validateThrow`, `validThrowRange`, `getMaxThrowDistance`, `getFiringAccuracy`, `getAccuracyModifier`, `Projectile::calculateTrajectory`, `calculateThrow` and the classic `applyAccuracy`, `isOutOfRange`, the whole `canUseWeapon`; the viewer-scoped aim queries (actions, costs, accuracy for a target) | a target behind a wall without a line of fire; a unit half in cover hit on an exposed voxel; a kneeling and a wounded shooter's accuracy; the one-handed penalty; range limits and the dropoff; the uniform spread of `shootingSpreadMode` 1; `getHitChance` with the hit chance tables; a throw over a low wall and one blocked by a ceiling; an arcing shot; the target voxel's order (unit, object, wall, floor); each `canUseWeapon` refusal (research, AI use delay, depth, `STR_MUST_USE_BOTH_HANDS`, mana, psi skill); the aim queries refused for another side's unit, off its side's turn and at an undiscovered tile |
| S3 | Damage to units: `hitUnit`, the unit part of `hit`, `BattleUnit::damage` (resistance, side and body part, armor and `ArmorIgnore`, every conversion, overkill, pain immunity, wounds, morale loss, fire and the extinguisher, infection, `wasHitBy` and the spotting of the attacker, sniper spotting), the armor's own damage, body items on the hit tile, experience (`awardExperience` and the adders, the extended system off), statistics; `battle.hit-unit-unsupported` retires | each side and body part; armor reducing and ignored; stun damage on pain-immune armor; a wound and its morale; a unit catching fire and the extinguisher; the infection chance and the larger-armor case; experience awarded for a hit (and none for a friend); the statistics counters; `wasHitBy` and the attacker's spotting turns; sniper spotting under `extendedSpotOnHitForSniping`; a burning floor hitting a unit (branch 4's refused case now runs); the interim refusals of a hit that takes a unit out or self-destructs |
| S4 | Casualties: `checkForCasualties`, `UnitDieBState` collapsed (corpses, inventory to the ground, body items, `instaKill`, spotting resets, light and sight), `killSoldier`, `knockOut`, `convertUnit`, `convertInfected`, `reviveUnconsciousUnits` with `placeUnitNearPosition`, `removeUnconsciousBodyItem`, the dangerous nodes around a dying hostile unit, `clearUnitSelection`; the battlescape constructor's check; `battle.casualties-unsupported`, `battle.fall-knockout-unsupported` and `battle.turn-one-revive-unsupported` retire | a death with its kill statistics and morale (enemy, friendly fire, civilian); a stun with `stunningImprovesMorale`; a soldier to the memorial; a 2x2 corpse; a carried unconscious unit dying; a zombie conversion; a revival next to a big unit; a fall knocking out a unit with no escape; a loaded battle with a unit at 0 health; a dying alien's nodes marked dangerous and saved; the selected and undo units cleared; the facing direction 3 of a dying unit |
| S5 | Explosions: `ExplosionBState` (power, radius, damage type, `hit` or `explode`), `explode`, `detonate`, `checkForTerrainExplosions` and its chain, items on tiles, self-destruct, the explosion's spawns, `itemDrop`; the two generation refusals, `battle.terrain-explosion-unsupported` and `battle.module-map-unsupported` retire (ADR 0041) | a radius stopped by a wall and passing a door; the damage falloff; a chain of terrain explosions; a crashed UFO battle generated with its power sources exploded and their casualties; a map grenade primed at -2 exploding at generation (`explodeOtherJunk`); an item destroyed by `ToItem`; a unit self-destructing on death and its explosion; an explosion's spawned unit and item; a base module lost, and one in a block the saved map lacks |
| S5b | Battle runtime ([audit follow-up](#audit-follow-up-2026-11-14)): the runtime object and the battle's command registry, with `CampaignBattles` reduced to the campaign's side; the fault path and the refusal of an unknown command; the event sink, with S3–S5's outcomes (hits, casualties, blasts, fire) raised through it; the hooks interface and its call sites in S1–S5's code; the trial copy given the campaign link | a battle command draws from the campaign's stream without a per-command assignment; a handler that throws faults the battle and the next command is refused; an unknown command refused; a trial copy reads the campaign's difficulty; a hit, a death and a blast raise their events in the reference's order; each hook call site reached with the default hooks; existing battle tests and oracles unchanged |
| S6 | Melee and the attack command: `Attack` with `BA_HIT` (`MeleeAttackBState`: the player strikes in its facing direction with no targeting turn, and only a reaction turns, for free, in `init`; the cost, the to-hit roll, the dodge and its back penalty, a firearm's butt, terrain melee, a burn-floor attacker), `meleeAttack`, `meleeAttackCalculate`, `validMeleeRange` (big units, other levels), `validTerrainMeleeRange`; the command's events, refusals and stops; the reaction check after it stays `battle.reaction-fire-unsupported` until S9 | a hit and a miss; a dodge from behind; a big unit's reach; a rifle butt; a wall struck by terrain melee; refused when not adjacent or without time units |
| S7 | Shooting: aimed, snap and auto (all of `ProjectileFlyBState`: the targeting turn with a turret turning alone, the checks and stops, each shot, ammo spent, shotgun pellets, explosive ammo exploding two voxels back, `projectileHitUnit`, statistics, the auto-shot loop, spray waypoints, force fire), close-quarters combat, `updateEnemyKnowledge` | a hit and a miss on a unit; a shot destroying an object; the out-of-range, no-line-of-fire, no-ammo and time-unit stops after a turn, and the same checks as refusals when the shooter already faces the target; an auto shot of three; spray waypoints (the shots spread between them, a waypoint past the range moved to it); force fire at a tile with a unit on it; a shotgun; explosive ammo; a turret; close-quarters on and off |
| S8 | Throwing, priming, launching and proximity: `BA_THROW`, `BA_PRIME` and `BA_UNPRIME`, `BA_LAUNCH` with waypoints, the landing item (`itemDrop`, gravity, light), `fuseThrowEvent`, all of `checkForProximityGrenades`; the inventory's load, unload, quick swap, and the pre-battle prime and unprime; `battle.proximity-unsupported`, `battle.inventory-ammo-unsupported` and `battle.inventory-prime-unsupported` retire and `battle.inventory-throw-unsupported` goes | a throw landing and one out of range; a hot grenade; a primed fuse; a proximity grenade set off by a walk; a death trap; a thrown flare lighting the floor; a launch with three waypoints; loading, unloading and the Shift quick swap with their costs, in battle and before it; priming and unpriming in the pre-battle inventory |
| S9 | Reaction fire: `checkReactionFire`, the reactor list of `getSpottingUnits` with the reaction fire threshold, `getReactor`, `determineReactionType`, `tryReaction`, the AI's reaction scoring; reactions after walk steps, kneels, landings and attacks; the walk's stop; `HitState` and its reset; `HasPossibleReactor` and `battle.reaction-fire-unsupported` retire; the trial copies reviewed | a walk drawing a snap shot and stopping; a reactor out of range; a reactor below the reaction fire threshold (`getHitChance`); the score order and its reduction per reaction; a melee reaction; a preferred and a disabled hand; a hostile reactor holding an explosive shot that would hit its own side; a kneel drawing a reaction; an attack whose reaction kills the attacker |
| S10 | Battlescape client: the action menu from the hand and special-weapon buttons with costs and accuracy; targeting with the cursor's accuracy, Ctrl for force fire, the launch waypoints and launch button; priming and throwing; the reserve buttons as commands (the saved `TUReserved` and `kneelReserved`, written by the side-owning slot as `SelectUnit` is); the replay of shots, impacts, explosions and deaths; the carried unit's visible-unit button; health and wounds in the panel. This branch's later-button refusals are replaced (`battle.shooting-unsupported`, the hand buttons, `battle.launch-unsupported`, the special-weapon button, the reserve buttons, `battle.visible-unit-carried-unsupported`). The action menu still lists the branch 6 actions of the items it shows (`ActionMenuState`): the medikit, scanner and mind probe refuse with a new `battle.item-use-unsupported` and the psi amp's actions with `battle.psi-unsupported`, both owned by branch 6; the psi and skills buttons are relabelled to branch 6 | scripted loop tests: an aimed shot, a throw, a melee hit, a refused shot showing its reason, a reaction during a walk replayed, a reserve set and saved, a medikit and a psi amp action refused with branch 6's names |
| S11 | Acceptance on branch 1's battles and the private active-battle saves (below); attack and explosion timings in the performance baselines | none beyond acceptance |
| — | Branch audit (`git diff main...HEAD`) | — |

Acceptance: this covers each acceptance battle of branch 1 that starts (the three Rosigma battles stay refused at the
briefing by branch 8's `battle.new-turn-script-unsupported`). After the first turn, and without branch 4's
opponents-spent setup, a soldier walks out and draws reaction fire wherever the rules say (or no reaction). A soldier
fires an aimed shot at a spotted unit, or at a discovered tile when none is spotted. It throws a grenade where the
battle has one, and strikes in melee where a unit can reach another. Damage, deaths, corpses and destroyed terrain
follow the rules. The battle validates, then saves and reloads with equal saved, derived and sight state, and each
viewer still sees only its own side's spotted units. A crashed-UFO deployment generates with its power sources
exploded; S11 stages one from the corpus if none of branch 1's battles is one. Each private active-battle save loads,
and a player unit with a weapon attacks, or the attack is refused for a named reason of a later branch. Attack and
explosion costs on the largest acceptance map are recorded in the performance baselines.

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
