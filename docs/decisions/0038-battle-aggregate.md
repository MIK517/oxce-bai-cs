# ADR 0038: The battle aggregate

Status: accepted for Phase 7, 2026-10-18.

## Context

Phase 7 adds the largest state graph in the game: a map of up to tens of thousands of tiles, units with per-unit
randomness and script hooks, items, routes, visibility and interleaved reactions. The
[2026-10-18 audit](../project-audit-2026-10-18.md) asked the planning slice to settle four things before battle code:

- how a tick or battle action that cannot complete is undone (D1.3), so that prove-before-run does not carry into
  the battlescape;
- one live-state convention for new aggregates (D2);
- the multiplayer checklist (M4): own aggregate, atomic actions, controllers, per-faction visibility, viewer-scoped
  queries, unit IDs on commands, a separate save module;
- per-family registration and battle save parts (D4, E3).

[ADR 0035](0035-staged-ticks-on-a-shadow-copy.md) showed that an exact shadow copy of the campaign works and costs
about 0.3 ms (capture 0.03 ms, restore 0.27 ms on TFTD `Middle.sav`). [ADR 0037](0037-host-authoritative-session.md)
records the host-client rules battles must follow.

## Decision

### The battle is its own aggregate

`BattleState` (`Oxce.Gameplay/Battles`) owns the map, units, items, nodes, factions, turn state and per-faction
visibility. It has its own capability registry; it does not become another partial class of `CampaignState`. The
campaign holds at most one battle (the reference's `SavedGame::_battleGame`). A battle starts when the generator
consumes the pending `CampaignDeploymentRequest` and ends through debriefing, which is the only path by which battle
results change the campaign. While a battle exists the campaign refuses strategic commands.

### Controllers, factions, viewers

- Every unit has a faction (player, hostile, neutral: `UnitFaction`) and a controller: the AI or a player slot. In
  single-player, the player faction's units belong to the local player slot and every other unit to the AI.
- Visibility is kept per faction: discovered tiles and spotted units for each faction that has a human controller,
  the player faction always. The reference only keeps player-side state; hostile-side state is port-only data.
- Every battle query takes a `BattleViewer` (faction, and later player). A viewer sees what its faction sees.
- Commands that act on a unit carry its stable battle unit ID (soldiers keep their soldier ID; other units are
  numbered from `BattleUnit::MAX_SOLDIER_ID`, as in the reference). The session checks that the sender controls the
  unit; rules never ask who sent a command.

### Actions resolve completely

A battle command resolves the whole action it starts, interruptions included (reaction fire during a walk, a door
opening, a unit spotted mid-path), and returns the ordered events it produced. The reference interleaves logic with
animation states (`UnitWalkBState`, `ProjectileFlyBState`); in the port, presentation replays events and decides
nothing. Clients refresh from viewer-scoped queries and the battle revision.

### Failure model: validate, then resolve; no forecasts

- A battle command validates everything it depends on before its first mutation and returns a classified refusal
  otherwise (ADR 0037 rule 2).
- Resolution cannot fail on valid input. Content is validated when it is linked (terrain resources, map scripts,
  deployments, items, armors, units) and battle state when it is generated or restored. A rule that could fail on a
  reachable state is a validation gap to close, not a reason for a forecast.
- An exception during resolution is a defect. It faults the battle: the battle refuses every later command and the
  host offers the last save, as a determinism mismatch does for staged ticks.
- Battles have no preflight, no forecast and no per-action copy.
- Battle generation is a function of the request, the campaign, the terrain files and the random source. Some of its
  failures depend on random choices and cannot be checked first (a block that cannot be placed, a map that does not
  fill); the reference throws there. The port builds the battle aside and adopts it only when generation completes.
  A failed generation puts the random source back to its state before generation (`IStatefulRandomSource.Restore`), so
  the refused `StartBattle` changes nothing and a retry fails the same way. This is the one rewind of real random
  state besides ADR 0035's exact shadow staging: no draw is kept from a generation that did not happen.

### Strategic ticks: shadow promotion for named restrictions

The 136 named restrictions inherited from Phase 6 are cases a forecast cannot prove, not cases execution cannot run.
Where the preflight would refuse a tick with a named restriction, the tick runs on an ADR 0035 shadow instead; if the
shadow completes, its state, random source state and notifications are adopted as the tick's result, and if it throws,
the tick is refused with the shadow's reason and the real campaign is untouched. Ordinary ticks keep their existing
preflights and run directly, so ADR 0033's per-tick budget is unchanged; the extra cost is one capture and restore on
the rare ticks that reach a restriction. Adoption replaces the real campaign's capability state with the shadow's; it
does not undo anything already done to real state, so ADR 0035's "no rollback" rule holds. The branch that implements
it measures adoption on the largest staged saves before deleting the first forecast, and each retired restriction
deletes its forecast and variant tests in the same change.

A journal on real state was rejected: every mutation site would need an undo record, including in the battle, which
is the same double-writing the audit found in forecasts.

### Live state

Battle live state is mutable classes and struct arrays holding runtime rule handles. Snapshots and string rule IDs
exist only at the persistence boundary. Tiles are structure-of-arrays indexed `x + y * width + z * width * length`
(`SavedBattleGame::getTileIndex`). `CampaignWorld`'s snapshot-records-as-live-state style is not repeated.

### Saves and registration

`OxceBattleSave` in `Oxce.Savegames` reads and writes `battleGame` as its own module; port-only data (controllers,
hostile-side visibility) goes in an `oxcePortBattle` node that the reference ignores. Battle capabilities register
their save parts with the battle registry. Rule families the battle adds are linked through per-family registrations,
not new cases in `RuntimeRuleLinker`.

### Generation choices where the reference depends on history or crashes

- Craft skins: `RuleTerrain::refreshMapDataSets` renames the shared terrain rule from the skin last used, so a negative
  skin gives `name_skin` only when the previous skin was 0 or the same. The port derives the names from the rule's own
  each battle (`name_skin` for any nonzero skin), as a first battle does.
- Custom craft deployments: a soldier without a point (`std::map::at` crashes) and a saved direction outside 0-7
  (`BattleUnit::setDirection` keeps it; later lookups index by it) refuse the generation with a named reason.
- Base defense maps from the base's location (`baseDefenseMapFromLocation: 1`): `generateMap` reseeds the one RNG
  stream from `lon * lat * 1e6` and restores it after `attachNodeLinks`. The port has no global seed: the base terrain
  pick and the whole map generation draw from a separate `SplitMix64RandomSource` seeded with that value, so the same
  base position gives the same layout and the campaign's stream is left as the reference leaves its own. The C++
  conversion of a negative product to `uint64_t` is undefined; the port wraps it through a signed 64-bit integer, as
  x86-64 compilers do. A missing globe texture (dereferenced by the reference), a texture without a base terrain for
  the base's position (`getRandomBaseTerrain` draws `RNG::generate(1, 0)`, a modulo by zero), a facility outside the
  map's block grid (written out of bounds), a map name shorter than two characters (`substr` throws), facility vertical
  levels that leave no level, and a base module tile past the last whole block (`calculateModuleMap` indexes past its
  grid) refuse with a named reason. So do a base defense deployment without terrains under a texture with terrains (the
  texture pick dereferences the missing UFO or mission site) and a base defense dummy unit with no alien race to take it
  from (`front()` of an empty list).

## Consequences

- Battle rules are written once. The cost is up-front validation of content and restored state, which the
  compatibility contract requires anyway.
- Remote clients, several human players and a human alien side need no rework of the battle: they are another
  controller and another viewer.
- Animation timing can differ from the reference (for example, the order in which reaction shots are shown), but the
  rule outcomes cannot.
- Until branch 2 writes `battleGame`, saving with an open battle is refused.
