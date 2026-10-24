# Phase 7 — Playable battlescape implementation plan

Plan date: 2026-10-18. Baseline: `main` at `2b0c81fb` (audit cleanup, PR #77). Reference:
`../oxce-bai` (`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`).
Status: branch 1 (`claude/battle-generation`) is complete and awaits merge; its slices, audits and evidence are
recorded in [battle generation status](battle-generation-status.md). Branch 2 is next.

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
| 2 | `claude/battle-save` | `battleGame` loads into `BattleState` and is written back; the reference loads written saves; the opaque preservation and the "saving during a battle" restriction are retired |
| 3 | `claude/battle-session-ui` | Session layer (player identity, event broadcast, host clock and pause policy, per-actor quotes), screen stack, battlescape view: camera, levels, selection, inventory, legal-action display |
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
