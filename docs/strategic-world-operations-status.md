# Phase 6 branch 4b: ordinary UFO and craft operations in progress

The `codex/strategic-world-operations` branch implements bounded craft and UFO
waypoint flight, ordinary UFO landing/takeoff, mission countdowns, activity scoring and
base/craft radar detection. This is a checkpoint, not branch
4b acceptance. The 2026-09-26 scope revision closes 4b after ordinary UFO lifecycle
completion; branch 4c owns the remaining world generation, special behavior, scripts,
pursuit and globe UI. See [the revised Phase 6 plan](phase-6-plan.md#branch-4b--ordinary-ufo-and-craft-operations).

## Implemented

- Headless commands dispatch a ready craft to a persisted waypoint, patrol at its
  current position, or recall it. Craft identity includes rule type and ID because IDs
  repeat across craft types.
- The world capability handles the reference 60-tick takeoff delay, five-second movement,
  ten-minute fuel consumption and low-fuel return, waypoint deletion after its last
  follower leaves, arrival checkup, and automatic patrol relaunch after refuelling.
- Speed includes installed weapon bonuses. `patrolWithoutFuel` and `autoPatrol` are
  runtime-linked craft properties. Item count and storage limits also reach the runtime
  craft and installed-weapon rules. Facility and craft radar properties are projected
  for half-hour detection. Mind-shield flags and power are linked for UFO base scans.
  The compiled-content cache revision is 21.
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
  is consumed, and clears a retained landing ID. Hunting/escorting and nonzero shields
  remain guarded.
  Ordinary UFOs now cross ten-minute boundaries at later trajectory points as well.
  The base-detection predicate skips points 0-1 and current zone 5; hunting, escorting
  and alien-base handlers remain outside this ordinary path.
  If a terminal UFO precedes an unsupported nonterminal arrival in the same tick, the global
  preflight stops time before either moves; the reference would return at the terminal UFO.
  Rule-derived shield capacity is cached when a save is restored. Destroyed-UFO counts
  remain guarded during cleanup ticks because a restored live count can become insufficient
  after terminal arrival; this rare check does not allocate per tick.
- Ordinary score-mission UFOs select landing points using the reference first-area city
  bypass, one fake-water preference roll and at most 100 candidates. The last candidate
  is accepted even if unsuitable. Landing on real land or permitted fake water starts
  the trajectory ground timer in five-second units; forced ocean or forbidden fake-water
  arrivals stay landed for one tick. Crash-producing damage, mission-site conversion and
  retaliation landings remain guarded. Invalid or overflowing ground timers stop before
  arrival changes time, IDs or RNG state.
- Landed UFOs decrement their timer, take off at very-low altitude using the current
  trajectory speed, award positive mission points, and retain their landing ID until the
  next airborne waypoint. Half-hour activity doubles while landed; base/craft detection
  and contact loss use the ground altitude. First detected ground contacts receive both
  missing visible and landing IDs unless the rule suppresses contact alerts. A detected
  UFO landing normally receives a landing ID even for a `noAlert` rule. The optional
  `oxceUfoLandingAlert` notification pauses after the current tick, defaults off, and
  persists in `oxcePortOptions`.
- Marker capacity and combined half-hour/takeoff scoring are checked before time advances,
  including detection immediately before landing and multiple UFOs lifting together.
  Ordinary five-second preflight avoids captured mission-lookup predicates and allocates
  no scoring arrays until a half-hour, takeoff or potential landing-marker arrival is pending.
  Terminal arrival still defers later ground timers to the next tick. A fresh/cache
  fixture completes spawn, detection, landing, takeoff, departure and mission expiry,
  including a long ground stay across ten-minute and half-hour boundaries; every transition of a
  short restored lifecycle also survives save/reload and matches a batched command.
- Ten-minute UFO scans follow `DetectXCOMBase`: strict sight-range comparison, race
  bonuses, trajectory exclusions, crash-damage checks and base-list/UFO-list ordering.
  Completed facility area raises detection chance; enabled mind shields reduce it.
  Construction finishing earlier in the same daily boundary affects the scan. Preflight
  checks the projected inputs before time or RNG advances. A healthy terminal-departure
  UFO can still scan before five-second cleanup; damage-destroyed UFOs cannot.
- A successful scan marks the gameplay-owned, saved base `retaliationTarget` flag without
  a player alert. `aggressiveRetaliation` defaults true and persists in `oxcePortOptions`.
  With it disabled, only retaliation UFOs scan and only the last successfully discovered
  base in each region is newly marked, including the reference's outside-region group.
  Previously marked bases still participate in scans and retain their flags. This owns
  discovery only: retaliation mission selection, base-to-mission links and cleanup remain 4c.
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
  it through completed base radar or airborne craft radar, including a facility that finished construction earlier
  in the same daily boundary. First contact receives its reference visible marker ID and
  pauses time for an alert. Tracked contacts lose detection when coverage disappears.
  Instant-retaliation UFOs skip this pass. Craft detection uses effective radar stats
  including installed-weapon bonuses, excludes destroyed crafts, and includes a craft
  that refuels and relaunches auto-patrol earlier in the same half-hour handler. A
  grounded auto-patrol flag alone does not count as airborne radar. The hourly rearming
  transition into refuelling is projected before marker-ID preflight. On hourly
  inventory arrivals, preflight reserves capacity only when a matching item transfer,
  production completion, or daily research reward/return can supply fuel. It excludes
  delayed and sold production and respects the item-reuse gate for grounded ready craft.
  Random rewards and shared stock/staff/workshop constraints are bounded without drawing
  RNG, so marker reservation can still be conservative when a delivery is possible.
  Incoming auto-patrol crafts retain the transfer preflight gate and are not radar sources.
  Preflight checks activity and marker-ID capacity before mission spawning mutates state.
  Detection scripts and radar values outside the bounded range remain guarded.
- A score mission with a wave that creates no UFO or site advances its wave counter and
  rolls the next wave timer at the half-hour boundary. A final empty wave removes the
  completed mission without consuming RNG. These transitions survive save/reload;
  zero-timer follow-up waves remain guarded because the reference recursively processes
  them in the same tick. The public fixture covers repeated and final empty waves.
- Ordinary flying UFO transit now composes with half-hour mission countdowns, activity
  scoring, base/craft radar detection and ten-minute UFO base scans. Detection scripts
  and special hunting/escort retargeting remain pending.
  Restored ordinary-campaign UFOs reject a missing mission link or an out-of-range saved
  mission wave before time advances.
  The pre-campaign state follows the reference's absent mission-link case. Restore
  normalizes an ordinary UFO destination to an owned waypoint, so the destination
  preflight guard is currently defensive rather than reachable from a restored save.
  A malformed live count cannot expire a mission while a UFO still references it;
  preflight stops before half-hour deletion can break the later detection/movement handlers.

Reference sources inspected at `4df3a5e`: `src/Savegame/Craft.cpp` (`setDestination`,
`think`, `consumeFuel`, `checkup`), `src/Savegame/MovingTarget.cpp` (`setDestination`,
`setSpeed`, `move`), `src/Geoscape/GeoscapeState.cpp` (`time5Seconds`, `time10Minutes`,
`time30Minutes`, `updateActiveCrafts` and waypoint cleanup), `src/Savegame/Ufo.cpp` (`think`,
`calculateSpeed`, `setAltitude`, `isCrashed`), `src/Savegame/AlienMission.cpp` (`think`,
`spawnUfo`, `ufoReachedWaypoint`, `getWaypoint`, `getLandPoint`, `ufoLifting`, `addScore`),
`src/Engine/Options.cpp` (`oxceUfoLandingAlert` and `aggressiveRetaliation` defaults),
`src/Savegame/Base.cpp` (`detect`, `getDetectionChance`, `setRetaliationTarget`, load/save),
`src/Savegame/Craft.cpp` (`detect`, effective weapon stats and `isDestroyed`),
`src/Savegame/Production.cpp` (`step`, immediate versus delayed item delivery),
`src/Savegame/SavedGame.cpp` (research item rewards),
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
`StrategicWorldUfoLandingTests` covers the ordinary ground cycle, contact/landing alerts,
aggregate score and marker bounds, fake-water/ocean outcomes, timer guards and fresh/cache
spawn-through-expiry composition. `WorldLandPointTests` checks selection and retry rules.
The area-landing scenario rejects a water candidate through the campaign handler,
then reloads and flies to the accepted land point with fresh/cache rules. Probability
tests cover rolls 49 and 50 at a 50% fake-water chance. Temporary mutations confirmed
that bypassing land selection and inverting the probability comparison fail these tests.
`StrategicWorldBaseDetectionTests` covers scan exclusions, damage versus status, sight
and probability boundaries, mind shields, daily completion, regional selection, RNG
ordering, malformed inputs and saved discovery/options. The existing C++ base-detection
chance oracle is also used by the new live handler's aggregate arithmetic.
The live scan tests distinguish completed facility area from facility count, including
disabled and unfinished facilities, and check exact half/full damage thresholds with
race bonuses (`Base::getDetectionChance`, `Ufo::isCrashed`/`isDestroyed`). Temporary
mutations that omit area, change the half-damage comparison to inclusive, or omit the
damage bonus each fail these tests; production behavior is unchanged.
The existing `strategic-world` C++ oracle covers movement, fuel and detection arithmetic.
The `strategic-world-lifecycle` oracle now captures 2,884 five-second states across
ordinary completion, a long landing, interrupted departure and two simultaneous UFOs.
It extracts pinned UFO/mission/geometry methods and bounded ordinary spawn/countdown
fragments. Unsupported objective bodies abort; the driver supplies handler ordering,
a successful radar collaborator, one region/country and pause bits instead of GUI objects.
It is an asset-free transition oracle, not a full-engine detection or script trace.
Fresh/cache C# runs match every row and random-choice count, with transition reloads
and full-snapshot batching equivalence. The trace exposed the reference's two calls to
`getWaypoint` on nonterminal arrival: both selections now execute, and only the second
destination is retained. The area-landing test independently pins rejection sequences
for both selections. Existing radar/base-scan fixtures cover the injected detector.

Landing/takeoff checkpoint (2026-09-26): 576 unit tests and 300 fast compatibility
tests pass with no skips. Solution formatting, generated code-map validation and
diff whitespace checks pass. The private content/save corpus was not rerun for this
checkpoint; full lifecycle trace, corpus and population evidence remains a closure gate.

Ten-minute base-discovery checkpoint (2026-09-26): 578 unit tests and 336 fast
compatibility tests pass with no skips. The affected private campaign save round-trip
test also passes across its 19-save corpus. Existing time-advancement allocation budgets
remain unchanged and pass. Full lifecycle trace and population evidence remain closure
gates; the full private content corpus was not rerun for this slice.

## Still required for branch 4b

- Cleanup checkpoint: multiple UFOs release their mission counts exactly once;
  survivors and interrupted-mission expiry, consecutive departures across timing
  boundaries, reload/batch equivalence, and repeated loaded rewrites of deleted
  sidecars pass. Ordinary UFO waypoints are privately owned; craft pursuit remains
  guarded before deletion. Reference-backed traces and endurance evidence below are
  still required before this cleanup guarantee is accepted for branch closure.
- Add reference-backed full-lifecycle traces, save/reload at each transition, multi-day
  composition with craft operations, relevant terrain/depth coverage, malformed-input
  and bounded-population tests, populated allocation measurements, fresh/cache and
  affected save-corpus checks. Publish the actual closure evidence in this document.

**Closure scenario:** a supported mission spawns a UFO, which flies, is detected, lands,
takes off and departs; its mission then expires. Controlled choices and reloads before
and after the major transitions preserve the resulting state and continuation. Craft
dispatch/patrol/recall, fuel use and servicing continue alongside it. Required semantics
and script hooks of enabled paths must be implemented; unsupported paths remain guarded.

## Assigned to branch 4c

- Alien strategy, mission/arc/event projection, selection and daily/monthly scheduling.
- General regional UFO/deployment waves, deployment-only and zero-timer follow-up waves,
  broader mission objectives and their immediate consequences.
- Mission-site/alien-base lifecycle, retaliation base linkage/cleanup, shielded UFOs,
  hunting/escort behavior and remaining special trajectory/retargeting paths.
- Remaining world/detection/event-site script providers, ignored-contact alerts,
  craft pursuit and lost/deleted-target responses.
- Navigable globe and command controls, target information, notifications, pause/speed,
  save/load UI, and integrated world/TFTD/modded/month-boundary acceptance evidence.

The planned `codex/strategic-world-integration` branch starts after 4b is accepted and
merged. An over retaliation mission remains a permanent half-hour stop until 4c owns
its base-linked cleanup. Sites, alien bases, events and other unsupported world paths
remain explicitly guarded. Branch 5 still owns interception/deployment, branch 6 owns
full monthly evaluation and campaign-wide event consequences, and Phase 7 owns tactical
execution. The original combined branch 4 acceptance gates 4c closure; the revised 4b
gate does not establish full world simulation or remove the playable month-boundary guard.
