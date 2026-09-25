# Phase 6 branch 4b: operations in progress

The `codex/strategic-world-operations` branch implements bounded craft and UFO
waypoint flight, mission countdowns, activity scoring and base radar detection. This is a checkpoint, not branch
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
  craft and installed-weapon rules. Facility radar properties are projected for bounded
  UFO-spawn preflight. The compiled-content cache revision is 19.
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
  three-step movement trace matches the pinned C++ oracle across save/reload. Arrival
  at the last trajectory waypoint, or at any waypoint on an interrupted mission, marks
  the UFO destroyed and clears detection. The reference returns from the five-second
  handler at that point: later UFOs and craft do not move, and destroyed UFO cleanup
  waits until the next tick. This boundary survives save/reload. An ordinary score-mission
  UFO can also reach a nonterminal airborne waypoint: it advances its trajectory point,
  chooses the next regional destination, applies the waypoint altitude and speed, and
  resumes movement across save/reload. This path follows `AlienMission::getWaypoint`'s
  exact mission-site predicate, validates every possible regional coordinate before RNG
  is consumed, and clears a retained landing ID. Arrival that needs land-point selection,
  hunting/escorting and nonzero shields remain guarded.
  Ordinary UFOs at trajectory points 0-1 cross ten-minute boundaries: the reference
  base-detection predicate returns before scanning bases, and the other UFO ten-minute
  handlers have no effect without hunter-killers or alien bases. Later trajectory points
  remain guarded at that boundary.
  If a terminal UFO precedes a nonterminal arrival in the same tick, the global
  preflight stops time before either moves; the reference would return at the terminal UFO.
  Rule-derived shield capacity is cached when a save is restored. Destroyed-UFO counts
  remain guarded during cleanup ticks because a restored live count can become insufficient
  after terminal arrival; this rare check does not allocate per tick.
- Existing alien missions decrement their wave countdown at half-hour boundaries, with
  interruption and completed-wave cases preserved. Destroyed UFOs release their mission's
  live count after craft handling, and completed missions expire on the next half-hour
  boundary. The producing tick stops before an unsupported wave spawn. Countdown state
  survives save/reload; retaliation mission cleanup stays guarded until its base link is owned.
- A score mission can spawn an ordinary, zero-shield airborne UFO at
  a half-hour boundary between fixed, separated regional points. The mission live count,
  wave counter, next timer and unique-ID counter advance together; the new UFO then moves
  in that tick's five-second handler and continues after save/reload. The subsequent
  half-hour handler scores the UFO in its first matching region and country and detects
  it through completed base radar, including a radar that finished construction earlier
  in the same daily boundary. First contact receives its reference visible marker ID and
  pauses time for an alert. Tracked contacts lose detection when coverage disappears.
  Preflight checks activity and marker-ID capacity before mission spawning mutates state.
  Detection scripts, airborne craft radar, and radar values outside the bounded range
  remain guarded.
- A score mission with a wave that creates no UFO or site advances its wave counter and
  rolls the next wave timer at the half-hour boundary. A final empty wave removes the
  completed mission without consuming RNG. These transitions survive save/reload;
  zero-timer follow-up waves remain guarded because the reference recursively processes
  them in the same tick. The public fixture covers repeated and final empty waves.
- Ordinary flying UFO transit now composes with half-hour mission countdowns, activity
  scoring and base radar detection. Later trajectory points still stop at their ten-minute
  base-retargeting boundary, and craft radar and detection scripts remain pending.
  Restored ordinary-campaign UFOs reject a missing mission link or an out-of-range saved
  mission wave before time advances.
  The pre-campaign state follows the reference's absent mission-link case. Restore
  normalizes an ordinary UFO destination to an owned waypoint, so the destination
  preflight guard is currently defensive rather than reachable from a restored save.

Reference sources inspected at `4df3a5e`: `src/Savegame/Craft.cpp` (`setDestination`,
`think`, `consumeFuel`, `checkup`), `src/Savegame/MovingTarget.cpp` (`setDestination`,
`setSpeed`, `move`), `src/Geoscape/GeoscapeState.cpp` (`time5Seconds`, `time10Minutes`,
`time30Minutes` and waypoint cleanup), `src/Savegame/Ufo.cpp` (`think`,
`calculateSpeed`, `think`), `src/Savegame/AlienMission.cpp` (`think`,
`spawnUfo`, `ufoReachedWaypoint`, `getWaypoint`), `src/Savegame/Base.cpp` (`detect`),
`src/Geoscape/UfoDetectedState.cpp` (marker identity),
`src/Savegame/Region.cpp`/`Country.cpp` (activity histories), and
`src/Geoscape/GeoscapeCraftState.cpp`
(`btnBaseClick`, `btnPatrolClick`, `ConfirmDestinationState::btnOkClick`). The public
`strategic-world.rul` fixture supplies a moving craft and automatic patrol properties,
`strategic-world-slow.rul` a barely moving and a motionless craft, and
`strategic-world-pilots.rul` a craft that needs a pilot,
`strategic-world-capacity.rul` item limits, and `strategic-world-armor.rul` restored
crew armor limits, while `strategic-world-invalid-area.rul` isolates malformed
regional waypoint coordinates;
`StrategicWorldCraftOperationsTests` exercise command, tick, save, relaunch and
per-tick allocation behavior. `StrategicWorldUfoTransitTests` and
`StrategicWorldMissionCountdownTests` cover bounded alien-world time and persistence.
The existing `strategic-world` C++ oracle covers movement, fuel and detection arithmetic.
The command/timing, terminal-arrival and half-hour integration scenarios are
reference-shaped tests rather than extracted C++ traces.

## Still required for branch 4b

Mission/arc/event script projection and selection, general UFO/deployment wave spawning,
remaining UFO waypoint transitions and landing, UFO/site/base lifecycle, craft radar,
detection scripts and ignored-contact alerts, craft pursuit,
navigable globe controls, event-site scripting,
multi-day/month-boundary scenarios, and the corresponding compatibility fixtures and
allocation checks. Unsupported live alien world transitions remain guarded, as does the monthly campaign
transition. An over retaliation mission permanently blocks the next half-hour boundary
until base-linked cleanup is implemented. Branch 4b acceptance in
[the Phase 6 plan](phase-6-plan.md) is unchanged.
