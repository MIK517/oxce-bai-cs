# Phase 6 branch 4b: operations in progress

The `codex/strategic-world-operations` branch implements bounded craft and UFO
waypoint flight plus existing-mission countdowns. This is a checkpoint, not branch
4b acceptance.

## Implemented

- Headless commands dispatch a ready craft to a persisted waypoint, patrol at its
  current position, or recall it. Craft identity includes rule type and ID because IDs
  repeat across craft types.
- The world capability handles the reference 60-tick takeoff delay, five-second movement,
  ten-minute fuel consumption and low-fuel return, waypoint deletion after its last
  follower leaves, arrival checkup, and automatic patrol relaunch after refuelling.
- Speed includes installed weapon bonuses. `patrolWithoutFuel` and `autoPatrol` are
  runtime-linked craft properties. Item count and storage limits also reach the runtime
  craft and installed-weapon rules. The compiled-content cache revision is 18.
- In-flight craft, waypoints, and auto-patrol coordinates survive save/reload. Invalid
  dispatches leave the campaign unchanged.
- Dispatch applies `ConfirmDestinationState::btnOkClick`'s armor, onboard-item count,
  onboard-item size, and pilot gates in that order. The armor check also covers crew
  restored from a save. Fuel is not a dispatch condition; the reference lets a craft
  leave and turn back.
- Returning to base continues time silently. Automatic patrol relaunch requires a craft
  that just became ready through refuelling; unsupported pursuit and landing targets
  remain guarded.
- A craft that cannot move is not a blocked campaign. Stationary zero-speed patrols and
  zero-speed craft holding a destination both let time advance and remain recallable.
  `Craft::getFuelLimit` evaluates a division by zero for the latter case, leaving its
  C++ result undefined. Skipping the low-fuel threshold is a deliberate port decision;
  zero-speed fuel consumption still follows the reference. Preflight reports the
  condition when state this slice cannot simulate or trust stops time.
- The five-second handler resolves supported target positions without cloning target
  records or recomputing speed vectors, writes a craft back only when its state changed,
  and collects waypoint followers in one pass. Automatic patrol candidates are carried by
  base, rule type and ID so a craft list that grows within a tick cannot misdirect them.
- A restored ordinary flying UFO moves toward its anonymous waypoint every five seconds,
  updates its cached speed and direction, and initializes a zero-capacity shield. The
  three-step movement trace matches the pinned C++ oracle across save/reload. Arrival,
  hunting/escorting, nonzero shields, and ten-minute detection/retargeting stop before
  mutation until their handlers exist.
- Existing alien missions decrement their wave countdown at half-hour boundaries, with
  interruption and completed-wave cases preserved. Destroyed UFOs release their mission's
  live count after craft handling, and completed missions expire on the next half-hour
  boundary. The producing tick stops before an unsupported wave spawn. Countdown state
  survives save/reload; retaliation mission cleanup stays guarded until its base link is owned.
- An active flying UFO reaches the unsupported detection/retargeting boundary after ten
  minutes. Its transit cannot yet compose with the half-hour mission countdown.
  Restored ordinary-campaign UFOs reject a missing mission link before time advances.
  The pre-campaign state follows the reference's absent mission-link case. Restore
  normalizes an ordinary UFO destination to an owned waypoint, so the destination
  preflight guard is currently defensive rather than reachable from a restored save.

Reference sources inspected at `4df3a5e`: `src/Savegame/Craft.cpp` (`setDestination`,
`think`, `consumeFuel`, `checkup`), `src/Savegame/MovingTarget.cpp` (`setDestination`,
`setSpeed`, `move`), `src/Geoscape/GeoscapeState.cpp` (`time5Seconds`, `time10Minutes`,
`time30Minutes` and waypoint cleanup), `src/Savegame/Ufo.cpp` (`think`,
`calculateSpeed`), `src/Savegame/AlienMission.cpp` (`think`), and
`src/Geoscape/GeoscapeCraftState.cpp`
(`btnBaseClick`, `btnPatrolClick`, `ConfirmDestinationState::btnOkClick`). The public
`strategic-world.rul` fixture supplies a moving craft and automatic patrol properties,
`strategic-world-slow.rul` a barely moving and a motionless craft, and
`strategic-world-pilots.rul` a craft that needs a pilot,
`strategic-world-capacity.rul` item limits, and `strategic-world-armor.rul` restored
crew armor limits;
`StrategicWorldCraftOperationsTests` exercise command, tick, save, relaunch and
per-tick allocation behavior. `StrategicWorldUfoTransitTests` and
`StrategicWorldMissionCountdownTests` cover bounded alien-world time and persistence.
The existing `strategic-world`
C++ oracle covers movement and fuel arithmetic, but the command/timing scenario is a
reference-shaped test rather than an extracted C++ trace.

## Still required for branch 4b

Mission/arc/event script projection and selection, wave spawning, UFO waypoint arrival,
UFO/site/base lifecycle, detection, craft pursuit, navigable globe controls, event-site scripting,
multi-day/month-boundary scenarios, and the corresponding compatibility fixtures and
allocation checks. Unsupported live alien world transitions remain guarded, as does the monthly campaign
transition. An over retaliation mission permanently blocks the next half-hour boundary
until base-linked cleanup is implemented. Branch 4b acceptance in
[the Phase 6 plan](phase-6-plan.md) is unchanged.
