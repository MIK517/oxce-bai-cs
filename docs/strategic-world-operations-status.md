# Phase 6 branch 4b: operations in progress

The `codex/strategic-world-operations` branch currently implements the craft-only
waypoint flight slice. This is a checkpoint, not branch 4b acceptance.

## Implemented

- Headless commands dispatch a ready craft to a persisted waypoint, patrol at its
  current position, or recall it. Craft identity includes rule type and ID because IDs
  repeat across craft types.
- The world capability handles the reference 60-tick takeoff delay, five-second movement,
  ten-minute fuel consumption and low-fuel return, waypoint deletion after its last
  follower leaves, arrival checkup, and automatic patrol relaunch after refuelling.
- Speed includes installed weapon bonuses. `patrolWithoutFuel` and `autoPatrol` are
  runtime-linked craft properties. The compiled-content cache revision is 17.
- In-flight craft, waypoints, and auto-patrol coordinates survive save/reload. Invalid
  dispatches leave the campaign unchanged.
- Returning to base continues time silently. Automatic patrol relaunch requires a craft
  that just became ready through refuelling; unsupported pursuit and landing targets
  remain guarded. Stationary zero-speed patrols advance time.
- The five-second handler resolves supported target positions without cloning target
  records or recomputing speed vectors, and collects waypoint followers in one pass.

Reference sources inspected at `4df3a5e`: `src/Savegame/Craft.cpp` (`setDestination`,
`think`, `consumeFuel`, `checkup`), `src/Savegame/MovingTarget.cpp` (`setDestination`,
`setSpeed`, `move`), `src/Geoscape/GeoscapeState.cpp` (`time5Seconds`, `time10Minutes`,
`time30Minutes` and waypoint cleanup), and `src/Geoscape/GeoscapeCraftState.cpp`
(`btnBaseClick`, `btnPatrolClick`). The public `strategic-world.rul` fixture supplies
a moving craft and automatic patrol properties; `StrategicWorldCraftOperationsTests`
exercise command, tick, save and relaunch behavior. The existing `strategic-world`
C++ oracle covers movement and fuel arithmetic, but the command/timing scenario is a
reference-shaped test rather than an extracted C++ trace.

## Still required for branch 4b

Mission/arc/event script projection and scheduling, wave spawning, UFO/site/base
lifecycle, detection, craft pursuit, navigable globe controls, event-site scripting,
multi-day/month-boundary scenarios, and the corresponding compatibility fixtures and
allocation checks. Live alien world state remains guarded, as does the monthly campaign
transition. Branch 4b acceptance in [the Phase 6 plan](phase-6-plan.md) is unchanged.
