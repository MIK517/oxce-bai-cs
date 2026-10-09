# Phase 6 branch 4b: ordinary UFO and craft operations acceptance

The `codex/strategic-world-operations` branch implements bounded craft and UFO
waypoint flight, ordinary UFO landing/takeoff, mission countdowns, activity scoring and
base/craft radar detection. Its bounded closure gate passed and 4b merged in `e10d96f`;
full world-simulation acceptance remains separate. The 2026-09-26 scope revision
closes 4b after ordinary UFO lifecycle completion; branch 4c owns remaining world generation, special behavior, scripts,
pursuit; 4d owns globe UI. See [the revised Phase 6 plan](phase-6-plan.md#branch-4b--ordinary-ufo-and-craft-operations).

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
  The compiled-content cache revision is 23.
- Five-second craft shield recharge follows `GeoscapeState::time5Seconds` and
  `Craft::setShield`, including grounded craft, weapon bonuses, instant recharge (-1),
  fractional percent rolls and capacity clamping. Positive rates consume a roll even
  when divisible by 100; full shields and disabled rates consume none. Hourly base
  recharge runs first, and terminal UFO arrivals defer the whole craft pass.
  Effective shield stat overflow blocks before time advances; shield addition uses a
  wide intermediate before clamping instead of the reference's signed-overflow case.
- Dispatch and auto-patrol validate waypoint ID capacity and collisions before changing
  state. Half-hour preflight reserves an upper bound for possible relaunches, including
  hourly servicing, due craft transfers and READY craft that may reuse arriving or
  manufactured fuel. An invalid counter can therefore stop a
  boundary even when missing fuel or unfinished servicing would prevent that relaunch;
  ordinary ID ranges and ticks with no possible relaunch remain unaffected.
  `StrategicWorldCraftShieldTests`, `StrategicWorldWaypointTests` and the local
  `CraftServicingTests` cover these rules, atomic rejection and save/reload.
- In-flight craft, waypoints, and auto-patrol coordinates survive save/reload. Invalid
  dispatches leave the campaign unchanged.
- Dispatch applies `ConfirmDestinationState::btnOkClick`'s armor, onboard-item count,
  onboard-item size, and pilot gates in that order. The armor check also covers crew
  restored from a save. Fuel is not a dispatch condition; the reference lets a craft
  leave and turn back. A craft already heading home after low fuel or a completed
  mission refuses dispatch, patrol and recall, as `InterceptState` and
  `GeoscapeCraftState` offer no command for it; otherwise it would keep its low-fuel
  flag and never turn back again. Dispatch and recall cancel auto-patrol, and patrol
  starts it, only for crafts whose rules allow auto-patrol. With the `craftLaunchAlways`
  option a craft still being serviced can depart too, as in `InterceptState`.
- Campaign options follow the player's reference options: a reference save's `options`
  snapshot seeds them, `oxcePortOptions` persists them, and mod `fixedUserOptions`
  override both. Fixed booleans follow `OptionInfo::load`'s case-sensitive
  `std::boolalpha` prefix parsing, including leading classic-locale whitespace and
  unread trailing text. Empty/whitespace-only values deliberately become false rather
  than reproducing the reference's uninitialized result. BrutalAI fixes `aggressiveRetaliation: true`.
- A destroyed craft (damage at or above damageMax including weapon bonuses), in any
  status, stops time: `time5Seconds` deletes it with activity, crew and statistics
  consequences that belong to the dogfight slice. Rules without `damageMax` (default 0)
  would make every such craft destroyed in C++; the port deliberately treats those
  crafts as intact.
- Returning to base continues time silently. Automatic patrol relaunch requires a craft
  that just became ready through refuelling; unsupported pursuit and landing targets
  remain guarded. Grounded auto-patrolling crafts can be transferred: like
  `Transfer::advance`, arrival only checks the craft up, so it keeps its patrol point and
  relaunches there after refuelling at the new base. Incoming craft share the owned-craft
  condition checks on their arrival tick: invalid patrol coordinates, effective speed
  overflow and destroyed craft stop before time, transfers or servicing mutate state.
  Non-hourly ticks and transfers due in a later hour remain unaffected.
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
  Rule-derived shield capacity, including the mission race bonus, is evaluated per UFO in
  preflight, so UFOs created or re-raced by later handlers cannot slip past the guard.
  A saved wave number beyond the mission's waves loads, as in `Ufo::load`; only the
  arrival that would read it stops time. Destroyed-UFO counts
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
  that refuels and relaunches auto-patrol earlier in the same half-hour handler (including
  after an hourly rearm or an item-fuel delivery). A grounded auto-patrol flag alone does
  not count as airborne radar. Radar chances above 100 are legal, but ordinary radar
  still applies UFO visibility: 101 becomes 70 for an untracked ground contact. Craft
  radar's exactly-100 compatibility shortcut is retained. A final chance of at least
  100 always succeeds; extreme arithmetic is clamped instead of overflowing.
  Preflight checks activity and marker-ID capacity before mission spawning mutates state.
  It reserves a contact marker for every undetected alerting UFO and spawn, whatever the
  radar coverage, instead of forecasting which grounded crafts earlier handlers could
  relaunch; only an exhausted ID range can make that reservation block time.
  Detection scripts remain guarded.
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
`src/Savegame/Transfer.cpp` (`advance`, craft arrival/checkup),
`src/Engine/OptionInfo.cpp` (`load`, fixed-option boolalpha extraction),
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
`StrategicWorldMissionCountdownTests` cover bounded alien-world time and persistence;
`StrategicWorldCraftRadarTests` covers craft and base radar contact, and the rearm, refuel and
auto-patrol handlers that decide which crafts are airborne before half-hour detection.
`StrategicWorldTestSupport` holds the shared campaign setup and blocked-tick assertions.
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
The probe's `RNG::percent` counts its draw, and the driver draws `Base::detect`'s final
percent roll for every scanned UFO, so the random-choice column follows the reference rule
that `RNG::percent` always draws, even at 0% or 100%. The port's shared percent helper does
the same; only the reference's own short-circuits (a 100% hyperwave facility, a city landing
point) skip the roll. The expected rows are the MSVC capture of the current probe.
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

## Branch 4b closure evidence (2026-09-26)

| Gate | Evidence |
|---|---|
| Cleanup and ownership | `StrategicWorldCleanupTests`: several destroyed UFOs across missions, a surviving UFO, interrupted expiry, consecutive terminal arrivals across ten-minute/half-hour boundaries, deferred craft movement and cleanup, reload/batch equivalence, deleted opaque sidecars staying deleted through repeated loaded rewrites, and guarded pursuit before deletion. |
| Reference lifecycle | `StrategicWorldLifecycleTraceTests`: all 2,884 C++ trace rows agree with fresh/cached rules across ordinary, long-ground, interrupted and multiple-UFO cases. Positions, trajectory/status, ground time, mission counters/live counts, activity, detection/landing IDs, pause boundaries and random-choice counts are compared. Reloads bracket transitions; batched continuation reaches the identical complete snapshot. The half-hour activity amounts, contact-marker assignment, always-successful detection and handler order are written by the probe driver rather than extracted from the reference, so for those columns the trace checks consistency with the driver's model; their reference evidence is the separate radar, scoring and handler-order tests. See the oracle limitations above. |
| Multi-day composition | `StrategicWorldEnduranceTests`: 72 hours (51,840 ticks), 36 finite-wave UFOs, 36 contacts and landing alerts, peak population two, followed by complete UFO/mission drainage. Dispatch/patrol/recall, low-fuel return, refuelling, automatic relaunch and daily facility completion run alongside flight/landing/takeoff. One-tick, batched and repeatedly reloaded runs agree with both fresh and cached rules. |
| Terrain and malformed state | `StrategicWorldUfoLandingTests`: land selection, permitted/forbidden fake water and forced real ocean continue through takeoff, departure and mission expiry, including reload. Existing malformed timer, coordinate, mission-link, score/marker-capacity and damage tests retain their guards. Tactical depth and underwater deployment are not enabled by this strategic altitude path; those remain with deployment/tactical owners. |
| Population and allocations | Flight, half-hour and destroyed-UFO cleanup samples with 8/32/128 UFOs and 1/8 bases, plus capture/emission/restore, record costs separately. The guard bounds the bytes per additional UFO between the 32- and 128-UFO samples (flight 256 B per UFO-tick, half-hour 448 B, cleanup 128 B), so fixed per-command costs cannot hide per-entity allocations. Moving crafts are bounded at 256 B per additional craft-tick and stationary patrols at zero. See measurements below. |
| Imported saves | `PrivateStrategicWorldTests`: all 19 staged saves classified unchanged under fresh/cache rules; pre-advance loaded rewrite/reload preserves continuation, guards are repeatable without mutation, and post-run rewrites preserve state and opaque content. See the horizon and classifications below. |

Local validation after the audit fixes: `dotnet test --no-restore` passes **1,001 tests with zero failures
or skips**, including the full staged private corpus. Solution formatting verification,
generated code-map validation, fixture checksum tests and `git diff --check` pass.
No native/platform or UI code changed; the existing three-platform CI and SDL gates
remain required when their normal branch/path conditions apply. No push was performed.

No further implementation slice is required for the revised 4b scope. The supported
scenario completes spawn, detection, landing, takeoff, departure, cleanup and mission
expiry alongside craft operations. Unsupported script/event paths remain explicit
guards; local acceptance does not remove them.

### Private continuation classification

The horizon is **30 minutes**, resuming ordinary contact/landing notifications. An
executable classification establishes that horizon only; it does not establish full
world, month-boundary, tactical or mod completion. Original saves and rules were not
edited to bypass guards. Each result agrees before/after loaded-save rewrite and
between fresh and cached content.

| Corpus | Executable | Blocked | Preservation only |
|---|---:|---:|---:|
| UFO (7 saves) | 4 | 0 | 3 |
| TFTD (5 saves) | 3 | 0 | 2 |
| Rosigma (7 saves) | 4 | 0 | 3 |
| Total | 11 | 0 | 8 |

All eleven strategic saves complete 360 ticks. Cycle 14's first-airborne site
objective proof lets UFO `early/Begining.sav` pass its former tick-125 wave boundary;
the regression also requires the expected terror-mission wave to exist afterward.
All eight tactical saves retain their battle data and stop without advancing.
Exact reasons/tick counts remain regression assertions. The ignored
`artifacts/world-closure-corpus.json` records the per-save results. This is C# adapter
continuation evidence; emitted ordinary saves were not loaded into the complete C++
engine as part of this closure run.

### Populated cost measurements

`StrategicWorldEnduranceTests.PopulatedTicksScaleAndRecordSimulationAndPersistenceCosts`
writes ignored `artifacts/world-closure-measurements.json`. Windows x64 .NET 10 Debug
measurements use current-thread allocated bytes and elapsed time after a warm-up per
scenario. They are bounded regression samples, not statistical benchmarks. Flight is
12 five-second ticks; half-hour and cleanup are one tick. Craft movement and immutable
UFO updates are included; save capture/emission and restore are measured separately.
The half-hour rows were re-measured on Linux x64 after the detection allocation fix;
the 8-UFO warm-up sample is no longer recorded.

| Workload | Bases | 8 UFOs (bytes) | 32 UFOs (bytes) | 128 UFOs (bytes) |
|---|---:|---:|---:|---:|
| Flight, 12 ticks | 1 | 22,816 | 82,720 | 322,336 |
| Flight, 12 ticks | 8 | 40,784 | 100,688 | 340,304 |
| Half-hour tick | 1 | — | 13,416 | 48,744 |
| Half-hour tick | 8 | — | 18,720 | 54,048 |
| Cleanup tick | 1 | 1,592 | 3,512 | 11,192 |
| Cleanup tick | 8 | 6,288 | 8,208 | 15,888 |

At 128 UFOs/eight bases, capture plus YAML emission allocates about 2.67 MB and restore
about 4.79 MB. Across focused and full-suite runs, observed sample times were roughly
2-2.8 ms for 12 flight ticks, 2.3 ms for the half-hour tick, 16-37 ms for capture/emission
and 102-116 ms for restore. Timings are
informational and vary with concurrent workload; no elapsed-time CI assertion was
added. Half-hour detection projects each base's completed radars once per boundary, spans them
without a delegate or boxed enumerator per base, and keeps world loops on concrete lists,
so its cost per additional UFO (368 B, two immutable UFO record updates) no longer
depends on the number of bases; it was 752 B with one base and 1,536 B with eight.
The JSON artifact records the 32- and 128-UFO samples only. These measurements exclude durable disk writes and tactical data. Existing
stationary-craft allocation checks remain stricter than these moving-population checks.

## Assigned to branch 4c

Subsequent scope revision: branch 4b merged in `e10d96f`; the approved
[4c/4d delivery plan](phase-6-world-integration-plan.md) assigns headless work to 4c
and globe/UI plus combined acceptance to 4d. The event and UFO shield slices have
begun 4c; the historical closure evidence above describes the 4b baseline.

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

## Detection script caller projections (2026-10-04)

Base::detect and Craft::detect caller inputs now have an immutable allocation-free
projection. The pinned strategic-detection-scripts probe inserts both methods
verbatim and records the worker's initial outputs, scalar inputs, radar boundary
and hyperwave/final-roll order. Completed facilities contribute maximum ranges
even outside coverage; unfinished facilities are excluded by the owning caller.
The live detection VM remains the next slice.

## Bounded live detection VM (2026-10-04)

Base and active-craft callers execute compiled UFO detection programs after radar
arithmetic and before the final roll, including global before/after events with an
identity main fallback. Reusable frames and scalar buffers avoid per-contact VM
allocations after preparation. Saved and newborn UFO preflights replace blanket
script guards with precise unsupported binding, loop, divisor and wave-period
checks. Forward-only scalar programs with safe literal divisors are enabled;
unproven loops, input-dependent divisors and host providers remain guarded.
Fresh/cache cases cover scalar input branches, out-of-range detection, event
ordering, unchanged-state refusal and save reloads.

## Read-only live detection providers (2026-10-04)

Detection VM providers now expose UFO altitude/status/visibility/damage, detection
flags, race-adjusted shields, hunting/escort state and rules; craft damage/shield,
effective radar and rule capacities; and campaign difficulty, day/month and clock.
Providers preserve null-receiver defaults. The getter oracle extracts ten Ufo.cpp
methods verbatim; fresh/cache tests execute them through real detection scripts
before and after save reloads, including race bonuses, installed radar and current
tick time. Reusable input-name arrays, binding tables and a stable clock receiver
avoid rebuilding providers or boxing the clock per contact. Percentage-of-damage
and remaining bindings retain explicit capability refusals.

Batch audit: reproduced and fixed an allocation regression from cached-lookup
closures, and an aggregate execution-work gap. Program construction is separate
from allocation-free lookup. A shared half-hour budget includes saved/newborn
contacts, before/main/after programs, possible craft arrivals and installed-radar
query scans. Shield capacity is prepared once per script call. Fresh/cache
oversized contact/event regressions now stop with unchanged snapshots; existing
8/32/128-UFO allocation limits remain unchanged.

## Persisted entity detection tags (2026-10-04)

Detection scripts read typed UFO and craft instance tags, with zero defaults for
missing slots and null receivers. The native fixture now extracts
ScriptValuesBase::setBase/getBase as well as detection workers. Craft tags enter
save-neutral snapshots, restored live state and the save adapter, including
transfers and tag removal during source-preserving rewrites. Craft restoration
validates tag ownership, duplicates and value types transactionally. Fresh/cache
tests execute saved tag branches before/after reload and reject malformed state.

## Rule and global detection tags (2026-10-04)

Read-only detection providers now resolve the typed rule-tag overloads on UFO and
craft receivers as well as direct RuleUfo/RuleCraft receivers, RuleMod globals and
saved GeoscapeGame tags. Immutable content initial values supply rule/global slots;
instance slots remain separate even when their indexes coincide. RuleList values,
null receivers and absent slots preserve scalar semantics. Lookup tables are
prepared in preflight and keyed by exact rule identity. Fresh/cache scripts read
every owner through the actual VM and retain transition reload coverage.
Reference: ScriptBind.h::addScriptValue/BindPropCustomGet, Ufo/Craft/RuleUfo/
RuleCraft/SavedGame/Mod ScriptRegister and ScriptValuesBase::getBase.

## Research-aware detection providers (2026-10-04)

Detection scripts resolve RuleMod research pointers, read cost/points/lookup and
query completed research through the reference isResearchedScript semantics:
debug mode is explicitly ignored. Missing names and null mod/research receivers
return null/zero; canonical rule identities make repeated pointer comparisons
stable. The native probe extracts lookup helpers and the SavedGame wrapper for
completed/debug/null combinations. Fresh/cache live scripts match those rows
and observe research completed in the preceding daily handler, with save reloads.

The same slice fixes VM null equality: host null references now compare equal to
script null literals in either operand order, while non-null references retain
identity semantics. A failing-before-fix VM regression covers both orders and a
boxed zero reference, alongside the research fixture's missing-pointer branches.

## Detection tags and research cycle audit (2026-10-04)

Three slices add craft instance tag persistence, typed rule/mod/campaign tag
providers and research-aware detection. SavedGame::isResearchedScript explicitly
ignores debug mode; rule lookup preserves canonical pointer identity and null
defaults. The research fixtures exposed host-null/literal-null VM equality,
which is fixed with failing-before-fix regressions in both operand orders.

Batch audit reproduced an uncharged UFO tag-list scan; saved tag reads now share
the half-hour execution budget. Fresh/cache regressions stop before mutation.
Actual craft transfer orders, hourly arrivals and repeated source-preserving
rewrites retain tags and opaque fields, while deleted tags stay deleted.
All 1,966 root tests pass with zero failures/skips, including private corpus and
oracle checksum tests. The 4c headless gate remains open; 4d is untouched.

## Literal detection loop forecasts (2026-10-04)

Detection callers accept compiler-owned scalar counted loops with literal limits,
including nested loops, break/continue and changes to the exposed loop variable.
The forecast recognizes exact hidden-register initialization/check/increment/back
edges and rejects any body access to control registers. Per-instruction visit
bounds feed both VM step limits and aggregate contact/radar/tag work reservations.
Pinned script-core counted-loop/control fixtures run at the live detection call
site; zero/negative bounds, nesting and oversized inputs have focused coverage.
Input-dependent/list loops retain explicit bounded-forecast capability refusals.
Reference: Engine/Script.cpp::parseLoop/parseBreak/parseContinue.

## UFO damage detection providers (2026-10-04)

Ufo.getDamageMax and getDamagePercentage use mission-race damage stats and the
reference double division followed by floor. Native rows cover fractional
rounding, large representable values and signed maxima. Numeric forecasts stop
before zero denominators, undefined conversions or race-adjusted overflow can
mutate a tick. Null receivers retain zero defaults; fresh/cache cases reload
before and after detection. The probe now inserts Mod.h::isEmptyRuleName verbatim,
correcting an escaped-character stub and distinguishing empty/NUL names from
the valid research name "0". Reference: Ufo.cpp::getDamageMaxScript/
getDamagePercentage, Mod.h::isEmptyRuleName.

## Complete UFO statistic detection reads (2026-10-04)

Immutable runtime UFO statistics now retain all nineteen scalar fields exposed
by RuleCraftStats::addGetStatsScript, including fuel, combat bonuses, armor,
shield bleed-through and cargo/unit capacities. The compiler/cache revision
advances to 36. Read-only RuleUfo getters expose raw values; Ufo.Stats getters
add the mission race bonus without constructing per-contact stat objects.
Consumed sums are preflighted before mutation. The native probe inserts field
registration and operator+= verbatim and records raw/adjusted rows. Fresh/cache
VM tests read every field, null defaults and transition reloads; queried overflow
retains a precise unchanged-state refusal. Stats remain data, not combat execution.
Reference: RuleCraft.h::addGetStatsScript/operator+=, RuleUfo.cpp::ScriptRegister,
Ufo.cpp::setMissionInfo/ScriptRegister.

## Detection loops and complete UFO statistics cycle audit (2026-10-04)

Three slices add compiler-owned literal loop forecasts, exact UFO damage percentage
providers and all nineteen raw/race-adjusted UFO stat getters. Native captures,
fresh/cache VM execution, null defaults and transition reloads cover the change.
The cache/compiler revision is 36. No combat behavior is enabled by stat projection.

The batch audit reproduced skipped-loop overblocking: a zero/negative outer loop
was charged for an unreachable oversized inner loop. Forecasts now apply outer
loops first in explicit instruction order and count final header checks separately
from body visits. Unreachable host calls do not reserve numeric inputs. Active
nested tag reads retain aggregate work limits. All 1,985 root tests pass without
failures/skips, including private corpus, checksums and unchanged allocation bounds.
The 4c headless gate remains open; branch 4d is untouched.

## Rule lookup and linked research detection reads (2026-10-04)

Detection scripts resolve item, armor, soldier and skill pointers through the
reference Mod wrappers. Canonical immutable rule objects preserve pointer identity;
missing/empty/NUL names and null mod receivers return null, and item STR_NONE stays
null even if that name exists in the item table. Research providers expose typed
rule tags and linked needed items: needItem=false retains a null pointer, explicit
items and same-name fallback preserve identity, and legacy missing items stay null.
Native Mod getter/wrapper captures and fresh/cache VM/reload tests cover the slice.
Reference: Mod.cpp::getItem/getArmor/getSoldier/getSkill and script wrappers,
RuleResearch.cpp::afterLoad/ScriptRegister, RuleResearch.h::getNeededItem.

## Ordered country detection iteration (2026-10-04)

Detection scripts execute SavedGame's ordered, unfiltered country list through
bounded compiler-owned list loops, including null/empty lists, nesting and
continue. Country providers expose current funding/activity histories and the
signed pact flag. Loop forecasts use the immutable campaign country population;
manual iterator calls retain an explicit guard. Native captures insert the
reference list init/step templates, filter and country getters verbatim.
Fresh/cache base and craft callers preserve country order through save reloads.

This slice reproduces and fixes packed-program separator loss on reconstruction:
the constructor distinguishes source calls from already packed calls and rejects
malformed arities intentionally. A failing-before-fix VM regression and actual
cached country-loop execution cover the repair. Compiler/cache revision is 37.
Reference: ScriptBind.h::BindListInitImpl/BindListLoopImpl,
SavedGame.cpp::filterCountryConstScript/ScriptRegister, Country.cpp/h getters.

## Country and country-rule detection tags (2026-10-04)

Read-only detection providers expose Country.getRuleCountry and RuleCountry's
funding cap in its original thousands-of-currency units. Typed Country instance
tags and RuleCountry initial rule tags retain separate owners even when their
indexes coincide. Stable country/rule identities, absent slots and null receivers
preserve pointer/scalar defaults. The native probe captures getRules and
getFundingCap verbatim across signed/zero/large caps. Fresh/cache live list-loop
scripts validate every owner and reload before and after execution.
Reference: Country.cpp::getRules/ScriptRegister, RuleCountry.cpp::getFundingCap/
ScriptRegister, ScriptValuesBase::getBase and BindPropCustomGet.

## Rule lookup and country detection cycle audit (2026-10-04)

Three slices complete read-only item/armor/soldier/skill lookup and linked research
reads, ordered country iteration, and country/rule tags with funding-cap reads.
Native captures and fresh/cache execution cover both base and craft callers.
Packed host-call reconstruction now preserves separator-free cached arguments;
the compiler/cache revision is 37.

The batch audit found no additional confirmed defect. Added regressions prove
country-loop work is shared across detection callers and explicit unsafe iterator
calls stop before mutation. All 2,002 root tests pass with zero failures/skips,
including private corpus and checksum tests. Formatting, code-map validation and
diff checks pass. The 4c headless gate remains open; branch 4d is untouched.

## Complete UFO rule scalar detection getters (2026-10-04)

Immutable UFO projections and read-only detection providers expose radius and
weapon range/power/reload, alongside all nineteen stat getters. Native methods
cover the five size defaults, unknown sizes, negative radius fallback and explicit
zero/large radii; signed weapon fields retain their original keys and values.
A failing-before-fix regression corrects the typed unknown-size radius from two
to the reference zero. Fresh/cache VM execution and transition reloads cover all
rows and null receivers. Cache/compiler revision is 38.
Reference: RuleUfo.cpp::load/getRadius/getWeaponRange/getWeaponPower/getWeaponReload/
ScriptRegister. This adds read access only; interception remains in branch 5.

## Immutable mod detection constants (2026-10-04)

Detection providers expose Mod's maximum view distance and maximum darkness to
see units, preserving constructor defaults 20/9, ordered global overrides and
signed values. Typed settings, immutable runtime projection and cache restoration
retain both fields. Native inline getter captures, null receivers, malformed
schema tests and fresh/cache live VM/reload tests cover the slice. Compiler/cache
revision is 39. Smoke reduction remains guarded until its receiver and arithmetic
can be proved safe before mutation.
Reference: Mod.cpp constructor/load/ScriptRegister and Mod.h getters.

## Complete raw craft-rule detection statistics (2026-10-04)

All nineteen RuleCraft.Stats getters use immutable default statistics, preserving
the reference Stats prefix. Installed weapon bonuses and maximum-unit limit
overrides do not alter raw rule reads. The native addGetStatsScript capture now
pins the craft namespace separately; fresh/cache live craft detection and reloads
verify every field and null default while a radar weapon is installed.
Compiler/cache revision is 40.
Reference: RuleCraft.cpp::ScriptRegister, RuleCraft.h::RuleCraftStats/
addGetStatsScript and RuleCraft loading. Effective instance statistics beyond
radar remain precisely guarded until their mutation and numeric forecasts exist.

## Rule and mod detection scalar cycle audit (2026-10-04)

Three slices complete UFO rule radius/weapon getters, immutable mod view/darkness
constants, and all nineteen raw craft-rule stat getters. Native traces pin field
names and defaults; fresh/cache live execution and reloads cover each provider.
The radius slice reproduced and corrected the unknown-size fallback from two to
the reference zero. Compiler/cache revision is 40.

The batch audit removed an unintended craft-stat field from the facility
projection, avoiding a public API and allocation outside its owner. The existing
unsupported-provider regression now checks still-guarded smoke reduction; newly
supported view-distance reads have dedicated positive coverage. Effective craft
instance statistics beyond radar, additional VM providers/forecasts, actual
initial/monthly caller composition and final composed acceptance remain open.
Branch 4d is untouched.

Validation: all 2,016 root tests pass with zero failures/skips, including private
corpus, checksum and existing allocation checks. Formatting verification, code-map
validation and diff checks pass. The reference checkout remains clean.

## Item and armor detection rule tags (2026-10-04)

Detection scripts read RuleItem and RuleArmor initial tags through canonical rule
pointers. Typed tag owners retain separate indexes, missing slots and null
receivers return zero, and tag-free rules do not inherit another rule's values.
The existing pinned ScriptValuesBase oracle supplies positive/negative slot
values; new fresh/cache VM and before/after reload fixtures cover both families.
Reference: RuleItem.cpp::ScriptRegister and Armor.cpp::ScriptRegister (OnlyGet
script values), Engine/Script.cpp::ScriptValuesBase::getBase.

## Soldier and skill detection rule tags (2026-10-04)

Detection scripts read RuleSoldier and RuleSkill initial tags through canonical
rule pointers, including the skill identity projection. Read-only providers keep
typed owners and tag-free rule identity separate; absent slots/null pointers
return zero. Shared pinned ScriptValuesBase slot traces and fresh/cache VM/reload
fixtures cover signed values and rule switching. Setters remain unavailable.
Reference: RuleSoldier.cpp::ScriptRegister (OnlyGet), RuleSkill.cpp::ScriptRegister
and Engine/Script.cpp::ScriptValuesBase::getBase.

## Rule text and simple scalar detection metadata (2026-10-04)

Read-only providers expose item weight/battle type, armor size, and type-name text
for item, armor, skill and craft rule pointers. Canonical immutable projections
carry external type IDs without per-call reverse lookups. The exact native
getTypeScript wrappers preserve empty text for null, and getBattleTypeScript
preserves BT_NONE. Verbatim getter captures, actual text VM comparisons and
fresh/cache craft detection/reload tests cover signed weights and null receivers.
Compiler/cache revision is 41.
Reference: RuleItem.cpp::getTypeScript/getWeight/getBattleType/getBattleTypeScript,
Armor.cpp::getTypeScript/getSize, RuleSkill.cpp/RuleCraft.cpp::getTypeScript and
their ScriptRegister bindings. Soldier type-name binding retains its existing
guard: the pinned wrapper has a different declared receiver type.

## Linked rule tags and text metadata cycle audit (2026-10-04)

Three slices add item/armor rule tags, soldier/skill rule tags, and type-name text
with simple item/armor scalar metadata. Canonical immutable rule objects preserve
tag owner identity; native ScriptValuesBase and exact getTypeScript/getter captures
pin signed values and null defaults. Actual fresh/cache VM execution and transition
reloads cover every newly owned provider. Compiler/cache revision is 41.

The batch audit found no additional confirmed defect. An added guard regression
proves unsupported RuleItem providers still stop before mutation even though
other reads on that receiver are available. The soldier type-name binding remains
guarded because the pinned wrapper declares a RuleSkill receiver. Unavailable
providers are explicit; combat, deployment and event payload boundaries remain
unchanged. Effective craft instance statistics, wider VM providers/forecasts,
actual initial/monthly caller composition and final composed acceptance remain
4c work. Branch 4d is untouched.

Validation: all 2,024 root tests pass with zero failures/skips, including private
corpus, checksum and existing allocation checks. Formatting verification, code-map
validation and diff checks pass. The reference remains clean at the pinned commit.

## Complete soldier rule statistic detection reads (2026-10-04)

Detection providers expose all twelve UnitStats fields under StatsMin, StatsMax
and StatsCap (36 getters). Immutable rule dictionaries preserve signed 16-bit
values widened to script integers, including both extrema and mana. Native
getMaxStatScript/addGetStatsScript captures pin names, prefixes and null zeroes;
fresh/cache live scripts and transition reloads cover every getter. No personnel
or tactical statistic mutation is enabled.
Reference: Unit.h::getMaxStatScript/addGetStatsScript and
RuleSoldier.cpp::ScriptRegister.

## Complete armor statistic detection reads (2026-10-04)

All twelve Armor UnitStats getters execute under RuleArmor.Stats, preserving
signed 16-bit stat values and mana. Canonical armor projections already own these
immutable fields. The pinned UnitStats wrapper captures the exact Stats prefix
and null defaults; fresh/cache detection and before/after reload tests cover every
getter. No per-contact allocation or tactical stat mutation is introduced.
Reference: Unit.h::getMaxStatScript/addGetStatsScript and Armor.cpp::ScriptRegister.

## Immutable armor vision detection metadata (2026-10-04)

Detection scripts read eleven armor visibility, camouflage, heat vision and
personal-light fields. Immutable projections preserve external heatVision and
personalLight keys, signed integer values and the reference defaults (friendly
light 15, fire visibility 100, remaining fields zero). Exact native methods and
fresh/cache live/reload fixtures cover defaults, overrides and null zeroes.
Compiler/cache revision is 42. Tactical visibility calculations remain outside
this read-only world caller.
Reference: Armor.cpp constructor/load/vision getters/ScriptRegister and Armor.h
getVisibilityThroughSmoke/getVisibilityThroughFire.

## Soldier and armor statistics cycle audit (2026-10-04)

Three slices add all 36 RuleSoldier minimum/maximum/cap stat getters, all twelve
RuleArmor stat getters and eleven immutable armor vision/light metadata getters.
Verbatim UnitStats wrappers and Armor getter captures pin namespaces, signed
widths, defaults and null behavior. Fresh/cache live scripts and transition reloads
cover every field. Compiler/cache revision is 42.

The batch audit found no additional confirmed defect. Providers read immutable
data without contact-loop allocation or tactical mutation. The 4c headless gate
remains open: effective craft instance statistics, remaining detection providers
and dynamic forecasts, actual initial/monthly caller composition, wider generated
object/newborn forecasts and final composed acceptance/classification remain.
Combat/deployment and due event payloads retain their later-branch boundaries.
Branch 4d is untouched.

Validation: all 2,032 root tests pass with zero failures/skips, including private
corpus, checksum and existing allocation checks. Formatting verification,
code-map validation and diff checks pass. The reference remains clean at the
pinned commit. This timed run completes seven three-slice cycles and seven batch
audits, with confirmed issues repaired in their owning slices or audit commits.

## Newborn first airborne arrivals (2026-10-09)

Ordinary score/supply point births can now reach their first airborne destination
in the creation tick. The existing movement owner advances exactly one waypoint;
preflight validates its replacement altitude/speed and bounded double waypoint/
source lookup work. Ground transitions, land retry, research interruption and
special hunters/escorts retain their existing boundaries. Native references are
AlienMission::spawnUfo/ufoReachedWaypoint and GeoscapeState::time5Seconds at the
pinned commit; verified lifecycle transition and immediate-wave captures back
fresh/cache coincident/nearby origins, three-wave chains and transition rewrites.

Six new execution/capacity cases reproduce the old distance guard before the fix;
four further cases preserve later-ground and interruption refusal. Exact RNG
assertions include both waypoint draws, zero timers and zero-chance base detection.
All 186 affected tests pass, with targeted formatting, code-map and diff checks.
This is cycle 13's first slice; variable/generated origins, ground starts and the
batch audit remain. No native objects/downloads were generated; useful existing
captures remain. 4c is open and 4d has not started.

## Regional and generated first arrivals (2026-10-09)

The safe airborne proof now accepts bounded regional areas, including a mix of
reachable and distant first destinations. Both possible movement paths use the
existing owner; no RNG or future actor identity is consumed in preflight. Native
lifecycle rectangle/transition and daily/hunting generation captures back exact
selected coordinates/RNG, altitude/speed, three rewrites and subsequent ticks.
Daily births score before detection; hunting births follow that pass. Fresh/cache
fixtures include both callers together, full identity envelopes and 72 hours of
day batching versus hourly advancement/reloads with bounded populations.

All ten new regional/caller cases reproduce the point-only restriction before
the change. All 142 affected tests pass, including six further capacity/endurance
cases. Targeted formatting, regenerated code-map and diff checks pass. This is
cycle 13's second slice; overlapping ground starts and batch audit remain.

## Overlapping newborn ground starts (2026-10-09)

A grounded birth no longer requires its first destination to be farther than one
flight step. Native GeoscapeState::time5Seconds enters LANDED, consumes five
seconds and may lift without moving or recharging; subsequent ticks use the saved
UFO arrival owner. Existing ground-supply and assault starts retain their precise
checks. Lifecycle spawn/lift captures back one-, two- and ten-tick stays, exact
creation/detection RNG, double landed activity, lift points and initial shields.
Fresh/cache fixtures preserve detection/landing markers through lift, clear them
on the next airborne arrival, reserve combined activity and marker capacity,
rewrite saves three times and compose both regional generation callers.

All 16 new cases reproduce the distance refusal before the change. All 72 affected
tests pass; targeted formatting, code-map and diff checks pass. This completes
cycle 13's execution slices; their batch audit and full validation follow. 4c and
its later-branch boundaries remain open; 4d is untouched.

Cycle 13 batch audit reproduced replacement-speed overflow in both fresh/cache
content: calling the checked speed helper during proof threw instead of refusing
the tick. The proof now validates the wide product before narrowing. Both overflow
regressions refuse atomically; the maximum safe result still executes/reloads.
Three older blanket-arrival refusal tests were replaced with stronger fresh/cache
selected coordinate, discarded RNG, speed and three-rewrite checks for both
reachable and distant point/rectangle outcomes. All 71 affected cases pass.
Reviewed caller order, constructor/landed branch, counter/wave identity, pure
forecast work and precise interruption/special/ground boundaries. Full final
root/format validation follows the separately committed correction.

Cycle 13 closes with all 4,260 root tests passing without failures/skips, including
native checksums, private classifications and populated allocation gates. Full
formatting, code-map and diff checks pass; the pinned reference is unchanged.
The replacement-speed defect is fixed in its separate audit commit. Remaining
ordinary objective/interrupt/hunter first arrivals, newborn ground/conversion
consequences, linked deletion, ordered research/maintenance alternatives and final
acceptance reconciliation keep 4c open; branch 4d is untouched. Useful captures
and validation logs remain; no native probe objects were generated this cycle.

## First airborne arrivals across objectives (2026-10-09)

Site/base/infiltration births now use the same bounded first-airborne proof as
score/supply. Existing think-time owners reserve final objectives before movement;
no landing/conversion effect is enabled. Pinned AlienMission::think/spawnUfo/
ufoReachedWaypoint and verified lifecycle, objective-base, infiltration and
mission-origin captures back Space/Earth source variants. Fresh/cache seed cases
preserve three-wave identity, one-step altitude/speed/position, final base/pact
state, native revelation, activity, three rewrites and following flight. Base
birth-sequence exhaustion still refuses before any wave/research mutation.

Ten cases fail at the old objective restriction before the change; supply's two
already-supported cases pass. All 79 affected cases pass with targeted formatting,
code-map and diff checks. This is cycle 14's first slice; harmless research tags,
no-target hunter arrivals and their batch audit remain. 4d is unchanged.

## Research-tagged first airborne arrivals (2026-10-09)

A birth's interrupt tag now blocks first-airborne advancement only when the ordered
site-research forecast can explicitly grant that topic after spawning. Daily
research interruption occurs before spawning; harmless tags and unrelated later
grants use the existing ordinary movement owner. A relevant possible post-birth
grant retains an atomic terminal-arrival boundary. Pinned GeoscapeState daily/site
caller order and AlienMission interruption/arrival handling, with verified lifecycle,
site-timer and daily-generation evidence, back fresh/cache selected interruption
outcomes, complete-state/RNG controls and three save rewrites.

All 61 affected tests pass; targeted formatting, regenerated code-map and diff
checks pass. Cycle 14's second slice is complete; no-target hunter constructors
and the batch audit remain. No event payload or combat boundary is removed.

## No-target hunter first airborne arrivals (2026-10-09)

Hunter constructors can use the ordinary first-airborne arrival when the existing
bounded craft/transfer scan proves no selectable craft and the wave cannot escort.
The proof consumes no RNG; runtime keeps native percentage, randomized mode and
behavior choices before waypoint selections. Pinned Ufo constructor, AlienMission
spawnUfo and GeoscapeState hunting/detection callers, with verified hunting and
lifecycle captures, back fresh/cache 50/100-percent three-wave cases. Missing,
grounded and undetectable outbound craft preserve exact constructor/detection/
arrival RNG, all five stochastic outcomes, wave identity, shields, three rewrites
and following flight. Outbound craft still participate in detection even when
undetectable to hunters. Possible craft hunts and conditional escort leaders keep
atomic arrival boundaries.

Four positive cases reproduce the old restriction; four boundary cases already
pass. All 150 affected tests pass; regenerated code-map and diff checks pass.
Formatting reported test initializer whitespace, corrected during the batch audit.
This completes cycle 14's execution slices; their batch audit and full validation
follow. No combat or landing transition is enabled.

Cycle 14 batch review finds no additional confirmed runtime defect. Final wave
proofs see the completed site forecast; its possible explicit topics include
optional free-research outcomes. Two additional fresh/cache regressions preserve
atomic refusal for a possible later grant. Reviewed objective completion order,
constructor/escort exceptions, bounded no-target craft/transfer scans, source
geometry, single-step movement, counters and remaining combat/event boundaries.
All 30 focused audit cases pass and full formatting passes after correcting test
initializer whitespace. Root validation follows before the audit closes.

Cycle 14 closes with all 4,288 root tests passing without failures/skips, including
checksums, private fresh/cache/rewrite classifications and population gates. The
corpus expectation now requires eleven strategic saves to complete the fixed
horizon and the formerly blocked UFO save's terror wave to exist; eight tactical
saves still stop at zero ticks. Full formatting, map/diff and unchanged reference
checks pass. The quota interruption left this classification update pending; its
morning validation closes that checkpoint. Remaining conditional newborn flight/
pursuit, linked deletion, research/maintenance alternatives and final acceptance
keep 4c open. Branch 4d remains untouched; no native objects were generated.

## Forecasted no-target first arrivals (2026-10-09)

The first-airborne proof now accepts a mission/wave/mode-specific no-target result
from the existing newborn hunting owner. Active craft can be present when native
radar eligibility, bounded geometry or projected craft state excludes every target;
preflight still validates attraction before the radar test. No constructor RNG or
future identity is consumed. Pinned GeoscapeState::ufoHuntingAndEscorting, Ufo
constructor and AlienMission arrival caller, with verified hunting/lifecycle
captures, back zero, negative and separated positive-radar cases through fresh/
cache content. Exact constructor/detection/arrival RNG, every mode/behavior outcome,
three rewrites and following flight remain asserted; malformed attraction, possible
hunts and escort-leader boundaries remain atomic.

Four positive cases reproduce the prior coarse craft exclusion. All 132 affected
tests pass. This is cycle 15's first slice; safe hunt alternatives, research/escort
composition and the batch audit remain. Branch 4d and later-branch gates are unchanged.

## Safe hunt and ordinary-arrival alternatives (2026-10-09)

The first-airborne proof also consumes the existing bounded safe-hunt result for
nonescort births. A successful constructor may target a distant craft; its ordinary
alternative can advance one airborne waypoint. Pinned GeoscapeState retarget/move
ordering and Ufo/AlienMission constructor/arrival callers, with verified hunting/
lifecycle traces, back all eight three-wave constructor patterns, both native mode/
behavior choices and unchanged versus 150-percent hunt speed. Fresh/cache fixtures
pass explicit slow/day speed context, preserve original waypoints, live movement,
contact markers and notifications, exact RNG, three rewrites and following ticks.
A reachable craft retains the precise precombat refusal before any random choice.

Four cases reproduce the former no-target-only boundary. All 124 affected tests
pass. This is cycle 15's second slice; research/escort composition and the batch
audit remain. Branch 4d and unavailable combat/landing/event consequences are unchanged.
