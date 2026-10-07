# Daily alien-base mission generation

Branch 4c incremental slice, pinned to reference
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

The existing daily world handler now executes `GenerateSupplyMission` after
alien-base activity and before facility discovery. Weighted mission selection
precedes the generation limit and frequency checks. Empty choices skip the
frequency roll; exhausted limits skip it; zero frequency still draws it.
Undefined nonempty mission choices fail before the affected tick mutates time,
scores, counters or RNG, even when their generation limit is exhausted.

Region selection preserves target-base odds, player-base order and duplicate
regions, weighted monthly regions, or the first containing saved region of the
alien base. `missionRegion` remaps exactly once. Race selection preserves the
deployment's `genMissionRaceFromAlienBase` option, optional mission race weights
and reverse timeline scan. A race timeline before its first entry returns the
reference's empty race without drawing a weighted roll; the existing load repair
interrupts that mission and assigns the first race on reload. A region timeline
before its first entry instead fails intentionally, defining the reference's
invalid iterator dereference as a port input error.

Creation owns mission ID allocation, zeroed wave/live counters, site-area selection,
initial timer rounding, source-base link and generation-count increment. All seven
mission objectives may be scheduled. All six non-space operation modes reuse the
already linked source base: `AlienMission::start` does not acquire or create a new
base in this caller. This does not implement their later wave/objective effects.
Neither strategy run counts nor script repeat/location tables change in this caller.

Midnight falls through to the half-hour handlers. Creation is currently allowed
only when the first timer guarantees a countdown above thirty minutes at creation
(`timer / 30 >= 4`). That pass subtracts thirty minutes immediately. Shorter timers
stop before the daily tick with an explicit immediate-generation boundary until
new mission candidates can participate in the combined wave/scoring/detection
forecasts. Later waves use existing capability checks; supply, site, retaliation
and other unavailable wave paths stop before their affected tick. Event payloads,
combat, deployment and campaign month rollover retain their established guards.

Preflight checks all reachable weighted choices and selection branches without
RNG. It reserves one possible mission per generating base, bounds population to
100,000 and prevents exhausted or colliding IDs. Weighted totals and initial
countdown maxima must fit signed integers. Generation selection and region scans
share the existing one-million daily base-work budget. Disabled frequency/limit
paths do not inspect unreachable region/race dependencies. The absent-base fast
path remains allocation-free; the existing campaign allocation gates pass.

Daily mission interruption now executes after the player-base/research loop and
before alien-base activity/generation, as in `GeoscapeState::time1Day`. It checks
completed research without debug shortcuts. Missions created later in that same
day are first checked at the following daily boundary. Zero-live missions become
interrupted and the same tick's half-hour cleanup removes them; generation limits
and base links retain their existing ownership across this transition.

The shared research preflight provides guaranteed and possible daily discoveries
without rolling rewards or executing research. Guaranteed interruption is applied
to mission-wave and UFO scoring forecasts, preventing obsolete wave candidates
from blocking the tick or reserving effects. Uncertain reward/disabler outcomes
retain an explicit guard; the possible set conservatively includes free/protected
topics, their lookups and zero-cost discoveries. Interruption with live UFOs also
retains an explicit daily guard until combined arrival, pursuit and site forecasts
support it. Missing interruption research, malformed live counts with remaining
UFO links, and deferred retaliation cleanup stop before mutation. Already
interrupted missions use the existing lifecycle and save behavior.

The extracted `strategic-alien-base-generation` probe inserts the exact
`GenerateSupplyMission::operator()`, `AlienMission::setRegion`, start timer block,
and `RuleAlienMission::generateRace/generateRegion` methods into asset-free rule
and target collaborators. Minimum integer choices record call ranges; weighted
collaborators use ordinal map order. Twenty-three traces cover local/weighted/
player-base targeting, race choices, site areas, all objectives and base-operation
modes, exhausted limits, zero frequency and empty choices. The oracle ends at
creation; fresh/cache gameplay comparisons separately check current-tick
half-hour fallthrough. This is controlled-decision evidence, not RNG stream parity
or a full C++ engine reload of port saves.

The extended capture also inserts the exact daily interruption loop and
`AlienMission::isOver`; four additional traces cover completed/uncompleted research
and existing interruption flags. `StrategicWorldMissionInterruptionTests` compares
those zero-live traces through real daily/half-hour processing, exercises the audit
reproduction through generation and transition reloads, and checks same-day primary
and lookup research, discarded wave score forecasts, uncertainty/live-UFO guards,
missing rules and malformed links. The installed-handler order test records the
new daily phase explicitly.

Additional tests cover real deterministic frequency outcomes, malformed region
timelines/weights/zones/fallbacks, immediate boundaries, combined ID reservations,
future supply-wave guards, and 72 hours of generation/countdown/cleanup with a
persisted limit across reloads. Repeated loaded rewrites conserve unknown mission
fields only while their mission survives; deletion does not resurrect sidecars or
reset the source base's count. Existing save fields `alienBase`, `uniqueID`,
`missionSiteZone`, wave/countdown counters and `genMissionCount` already have
gameplay ownership, so no save schema or compiled-cache revision changes.

References also inspected: `AlienDeployment` constructor/load/chooseGenMissionType,
`SavedGame::locateRegion`, `AlienMission` constructor/load/save, and the mission
spawning branches. Defaults remain frequency 0, limit 1000 and race-from-base true.
Hunting, linked self-destruction, upgrades/evolution, broad wave spawning and the
remaining scheduler/script work are still open. Branch 4c is incomplete; 4d is
untouched.

Validation checkpoint: all 1,358 root tests pass with no failures or skips,
including 20 new generation compatibility cases, 18 interruption cases, ten local
selection tests and the existing allocation gates. Fixture checksums, formatting verification,
regenerated code-map validation and `git diff --check` pass. The private fresh/cache
corpus retains nine executable saves through 360 ticks, two stopping at mission
spawning (125 and 220 ticks), and eight active-battle preservation-only imports.
These horizons establish bounded continuation, not branch 4c closure.
## Immediate airborne generation (2026-10-07)

Daily base-generated score/supply missions can now produce ordinary airborne UFOs
in their creation tick. Pure candidate missions cover every reachable region and
race without allocating identities or drawing RNG. The existing wave preflight
reserves UFO identities/population and checks geometry, scoring and detection;
optional candidates never contribute negative score headroom. New missions bypass
the earlier daily interruption pass, as in the reference. Hunters, initial ground
states and other immediate objectives retain a creation-lifecycle restriction.

Evidence: pinned `GeoscapeState::GenerateSupplyMission/time1Day/time30Minutes`,
`AlienMission::start/think/spawnUfo`, the verified `strategic-alien-base-generation`
initial timer rows and `strategic-world` ordinary-wave/scoring fixtures. Fresh/cache
seeded tests cover zero/random timers, same-tick points, before/after reloads and
atomic UFO-identity exhaustion. No persistence/cache schema changes are needed.

Composed acceptance now compares both creation callers with the native
`strategic-wave-chain` five-object zero-timer trace. Midnight produces daily
UFOs first, scores/detects them, then creates the hunting chain; hunting births
miss that earlier handler. Both weighted races, one region remap, allocated
mission/UFO order, live counters and reloads execute. Already completed
interruption research does not retroactively affect either newborn mission.
Combined identity and activity overflow reject before any tick mutation.
The prior shared-execution broad check passed all 1,838 strategic-world cases.

The batch audit confirmed three gaps: a heap iterator allocated on empty-world
ticks, hunting wave classification did not charge its future-wave scan, and
daily creation could resolve a duplicate marker/source ID to the wrong base.
The additional fix uses stack traversal, reserves classification work before
scanning, and refuses ambiguous source links before mutation. The original
campaign allocation thresholds now pass all twelve foundation cases; fresh/cache
large-wave and shared-marker regressions retain atomic snapshot/RNG assertions.

Final batch validation: all 3,240 root tests pass without failures or skips,
including the unchanged allocation gates, native checksums, private corpus and
endurance cases. Formatting, code-map and diff checks pass; the reference checkout
is clean. No unexpected access denial occurred. The iterator and identity fixes
are committed separately from the three implementation slices.

## Initial ground and site-wave creation (2026-10-07)

Daily score missions now admit initial ground UFOs when their positive timer
keeps them landed through the creation tick. They join the existing doubled
half-hour activity/detection reservations, then decrement once at five seconds.
The next tick uses the existing takeoff preflight and execution. Native
`strategic-world-lifecycle/spawnPathRows` pins status, ten-second initial timer
and speed; fresh/cache tests compare that trace and reload before birth, after
birth and after takeoff. A one-step initial timer retains a precise guard.
Reference: pinned `AlienMission::spawnUfo`, `Ufo::think/takeoff` and the daily,
half-hour and five-second Geoscape caller positions.

The hunting caller now also admits those ground births, including non-escort
hunter initialization. Landed UFOs miss both retarget passes; their future
takeoff uses the saved-mission forecasts with real identity. Fresh/cache ordinary
and 100-percent-hunter cases cover both callers, native timer countdown, no
premature hunting/escort state, correct half-hour points and three reloads.
Creation-time airborne hunters, ground escorts and same-tick ground releases
retain the lifecycle restriction.

Generated site missions now admit ordinary non-objective opening waves. Their
selected site area is persisted but unconsumed until an objective wave; a due
objective follow-up retains the creation boundary. Fresh/cache daily-plus-hunting
cases run 72 hours from both airborne and ground openings, reloading every day,
with native generation/site-area and lifecycle evidence. Future objective waves
remain pending, avoiding unavailable payload/combat effects. Separate zero-timer
objective cases verify rejection before even the first ordinary wave mutates state.

The ground/site batch audit found no additional confirmed defect. It checked
native ground timers, constructor hunter state, both retarget caller positions,
site-area consumption, half-hour scoring/detection, future objective boundaries,
identity reservations and transition reloads. All 3,250 root tests pass without
failures/skips, including private corpus, checksums and populated allocation gates.
Formatting, regenerated code-map and diff checks pass; the reference is clean.

## Alternative generation forecasts (2026-10-07)

An immediate generated mission before its first race timeline now retains the
native empty race instead of stopping at a forecast restriction. UFOs use default
stats; subsequent save loading interrupts the mission and substitutes the first
mod race, as `AlienMission::load` does. Verified generation `futureRace` rows and
pinned `RuleAlienMission::generateRace` back fresh/cache daily/hunting tests,
exact repaired-snapshot comparison and continued UFO persistence. No RNG is
consumed during forecasting and no schema/cache revision changes are needed.

Duplicate target-base regions now share one immediate-wave forecast after their
single region remap. Live generation keeps every base entry and its native index
roll, including duplicates. A fresh/cache 64-base regression previously rejected
the final available UFO identity; it now creates one UFO, retains the `[0,63]`
selection range and reloads successfully. Evidence: pinned
`GeoscapeState::GenerateSupplyMission` and verified generation `target` rows.

UFO identity/population reservations now take the largest immediate chain for
each unique source and daily/hunting caller. Different rules, regions and races
are all validated but cannot create simultaneous missions at one caller. The
callers remain separate, so midnight daily-plus-hunting births add together.
Fresh/cache seeded alternatives consume one or two identities at the allocator
boundary and preserve the actual selected rule/race, live counts and reloads.
All existing combined exhaustion regressions retain their strict refusal.

The batch audit reproduced a post-mutation retarget work failure: 750 daily
generated missions enlarge the lookup list for 1,400 saved airborne hunters.
The earlier forecast counted only saved missions. The separate audit fix now
reserves the enlarged list before the tick, including regular newly spawned
airborne hunters and escort lookup actors, while retaining the existing craft,
selection and follower work bounds. Pass-local reservations reset on every world
preflight. Fresh/cache regressions cover both saved hunters and a 1,500-hunter
saved-mission wave; all four cases refuse with unchanged snapshot and RNG state.
All 56 focused generation cases pass. Final root validation passes all 3,260
tests without failures or skips, including native fixture checksums, private
corpus, endurance and unchanged allocation gates. Formatting, regenerated
code-map and diff checks pass; the pinned reference remains clean. The audit
fix is separate from the three implementation commits. Existing native captures
and diagnostic outputs remain useful; this cycle created no compiled probes.

## Generated airborne hunting (2026-10-07)

Daily-generated airborne hunters now execute when no current or incoming craft
can become a target. They retain their trajectory waypoint and native constructor
state through both retarget callers. Pinned `AlienMission::spawnUfo`,
`Ufo::Ufo` and `GeoscapeState::ufoHuntingAndEscorting` establish the behavior;
verified `strategic-hunting/constructorRows` and generation/lifecycle fixtures
back fresh/cache seeded 50/100-percent cases, scoring, strict transition reloads
and atomic identity exhaustion.

Daily target-dependent hunters now use the ordinary newborn reach/attraction
forecasts with an explicit, unallocated mission context for race stats. Proofs
include rule, region, race and source alongside wave/counter/mode, then apply to
the same allocated mission after creation. They cannot leak between candidates
sharing the private ID zero. Fresh/cache native selection/retarget rows cover
all three constructor modes, backup waypoints, notifications, pursuit reloads,
hidden-contact marker exhaustion and atomic interception refusal. A separate
two-source regression proves that a safe first origin cannot hide combat at the
second source. The focused generation/newborn suite passes 146 cases.

The later base-hunting caller now admits airborne hunter births too. Their
possible hunt contacts reserve the largest reachable chain per source before
mutation, even on ten-minute ticks with no half-hour detection work. Daily
detection and later hunting contacts share the contact allocator reservation.
Fresh/cache tests cover both ten-minute/half-hour caller positions, all constructor
modes, strict pursuit reloads and the last two identities in a combined midnight
birth. Existing base-hunting eligibility tests use an immediate site objective
for their unavailable-effect boundary; their strict snapshot/RNG assertions remain.
Generated escorts and immediate objective/ground-release transitions stay guarded.

The batch audit confirmed an uncaught race-stat overflow in hunting generation:
the daily caller already refused it, but hunting exposed `OverflowException`.
The separate fix converts consumed hunting field overflow into an explicit
preflight refusal. Four fresh/cache malformed radar-bonus cases retain strict
snapshot/RNG atomicity checks. The initial root batch passed all 3,280 cases;
final root validation passes all 3,284 cases without failures or skips. Formatting,
regenerated code-map and diff checks pass, including native checksums, private
corpus and unchanged allocation gates. The pinned reference remains clean;
existing captures remain useful and this cycle created no disposable probes.
