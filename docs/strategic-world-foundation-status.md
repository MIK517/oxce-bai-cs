# Phase 6 branch 4a: world foundation

`codex/strategic-world-simulation` is a bounded foundation branch. The original branch 4
scope proved too large for one coherent review: the rule projections, arithmetic, and
save graph alone changed more than forty files. [The Phase 6 plan](phase-6-plan.md)
records the split. This branch does **not** complete world simulation.

## Implemented boundary

- Registered campaign capabilities preserve the existing single-writer command and
  time-handler order. Existing capabilities are still mostly `CampaignState` partial
  methods; registration is not a complete state-ownership split.
- World entity rules, trajectory/deployment data, globe geometry, weighted selection,
  flight and detection arithmetic are linked or pinned to the reference fixture.
- Alien missions, UFOs, waypoints, sites, alien bases, scheduled events and alien
  strategy restore through gameplay-owned snapshots. World save rewrites associate
  source nodes by identity and preserve unknown fields. Events may repeat a rule ID.
- Reference new-battle UFOs load without mission/trajectory links. Missing optional
  site UFO and custom-deployment links are cleared. The player world query excludes
  undetected UFOs/sites and undiscovered alien bases.

The relevant reference files at commit `4df3a5e` are `src/Savegame/SavedGame.cpp`,
`Ufo.cpp`, `MissionSite.cpp`, `AlienBase.cpp`, `AlienStrategy.cpp`, `Target.cpp`,
`MovingTarget.cpp`, and the world rule and region sources listed in the
`strategic-world` manifest. `SavedGame::spawnEvent` permits repeated scheduled rule
instances; `Ufo::load/save` condition mission/trajectory links on new-battle state;
`SavedGame::load` tolerates optional site links.

## Acceptance evidence and limits

`StrategicWorldPersistenceTests` covers graph round trips, repeated events, new-battle
UFOs, optional links, visibility, unknown world fields, and a bounded populated rewrite.
`StrategicWorldFixtureTests` compares geometry and strategy behavior with the extracted
C++ oracle. The save edge cases use small reference-shaped fixtures based on the
listed source paths; the arithmetic oracle does not prove full world-save compatibility. Private
UFO/TFTD/modded corpus coverage still depends on locally available assets.

Scheduled events have no reference save identity. Rewrites match complete known state
first, then occurrence order within a rule when countdown or completion changes. Two
identical instances reordered externally cannot carry distinct unknown fields with a
provable identity. This limitation should be revisited if scheduling introduces a
stable port-side event identity.

Live world entities block time advancement. No mission/arc/event scheduler, spawn
lifecycle, UFO or craft movement, detection decisions, dispatch/recall, or globe UI is
executable here. The tests and compatibility matrices must continue to report that
boundary explicitly.

## Next branch

After this foundation merges, start `codex/strategic-world-operations` from merged
`main`. Implement reference-ordered mission/arc/event scheduling and daily/monthly
hooks, then UFO/site/base lifecycle, movement/detection and craft dispatch/recall.
Close with headless multi-day and month-boundary scenarios, TFTD depth/terrain and
modded deployment waves, scripting at real event sites, and player UI. Stop at the
branch 5 interception and branch 6 campaign-evaluation boundaries as the plan says.
If the successor becomes too large, split at the headless simulation/UI boundary and
revise the plan before creating another branch.
