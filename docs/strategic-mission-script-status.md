# Monthly mission-script subsystem

Branch 4c; pinned reference `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

Reference methods: `GeoscapeState::determineAlienMissions/processCommand`,
`RuleMissionScript::getDelay/generate/getMissionTypes`,
`RuleAlienMission::generateRace`, `AlienMission::start/setRegion`,
`AlienStrategy::init/chooseRandomRegion/chooseRandomMission/removeMission/addMissionRun`,
and `WeightedOptions::choose`.

The registered `ICampaignWorldMissionScripts` operation and internal
`ScheduleMonthlyWorldMissions` fixture command execute through the campaign
transaction gate. The caller supplies score and eligible funds. The campaign
month handler remains guarded; initial/monthly arc → mission → event composition
is still outstanding. Tagged adhoc scheduling shares the mission executor.

Space missions with every non-site objective support explicit weighted region, mission and race
timelines, implicit strategy region/mission selection, mission-rule race fallback
and base targeting. Strategy refresh rebuilds region/mission distributions while
preserving run and location histories. Base targeting retains duplicate regions,
prunes active missions when explicit mission types exist, and otherwise prunes
regions without strategy tables before choosing a table mission. Its explicit
mission list uses a uniform starting entry and circular retry, independently of
weights. Exhausted candidates set a false label without creating a mission.

The mission region follows exactly one `missionRegion` remap. `useTable` removal
uses the original selected region. Eligibility and run limits are collected
before execution; later commands observe live table removals and newly created
missions. Missing conditional labels allow execution; negative conditions match
false labels. Rule order, execution/target odds, weighted RNG order, fixed/random
delays, mission IDs and counters persist through save/reload.

Preflight validates all potentially executable choices before mutation or RNG.
Duplicate positive labels, unresolved choices, unsafe timers/delays, exhausted
IDs/populations, missing tables and potentially deleted tables required by later
explicit-region commands stop atomically. Removing a mission from an already-empty
table also deletes the region; the audit added that missing guard. Work is bounded
at 4,096 scripts, 100,000 missions and one million candidate checks. Uncertain
conditionals, removals and possible refresh paths are checked conservatively.

Site scripts support point/rectangle candidate pruning, weighted or uniform
region selection conditioned on eligible regions, circular mission fallback,
base targeting, live batch occupancy and persisted repeat history. Empty
mission listings set false labels before creation fields are used. Signed and
unsigned timeline mismatches stop atomically. Mission-operation base creation
supports bounded geographic search, fallback suppression and persisted links.
Special objective effects and integrated campaign month progression retain
capability guards; deployment and hunting/escort support is transition-specific. Existing regional/Earth base acquisition
and missing-base interruption are owned. Ordinary airborne spawns remain limited
to fixed first-leg points, and later special arrivals/takeoffs remain guarded.
This subsystem does not claim complete branch 4c acceptance.

Evidence: `strategic-script-rules` captures script timelines and delay selection;
`strategic-alien-base-generation` captures mission race selection and region
remapping; `strategic-mission-base-target` captures explicit/implicit base targeting;
`strategic-mission-strategy` captures weighted table selection, removal and refresh.
Fresh/cache regressions cover failure labels, live batches, table depletion,
transition reloads, run history, malformed inputs and work/ID bounds.

The second strategy-slice audit reproduced an already-empty table deletion followed
by an unsafe explicit-region lookup. The guard fix is committed separately and its
fresh/cache regressions verify the entire batch remains unchanged.

Cycle 1: AlienMission::start initializes space mission counters and timer independently
of objective. Scheduling now permits infiltration, base, retaliation, supply and
instant retaliation without executing future effects. The generation oracle
objective rows verify the shared initialization timer; fresh/cache tests reload each.

Cycle 2: non-space initialization now selects existing regional or Earth bases in
saved order, aborts existing-only missions when none exist, and reuses bases for
new-if-necessary modes. Forced regional new-base creation and missing fallback bases
remain guarded before scheduling. The strategic-mission-operation probe captures
the exact initialization prefix, including timer-before-base-selection order.
The misleading EarthNewBase name retains the reference whole-Earth existing-base
lookup before creation. Audit review also bounds candidate and identity scans.

Cycle 3: no-object waves now advance score, infiltration, base, site and supply
missions without restricting their operation mode. Final infiltration/base
effects stop before the entire tick mutates; retaliation spawning remains guarded.
The wave-chain capture includes the exact objective-completion predicates.
Cycle 2 post-commit audit found no confirmed functional issue.

Cycle 4: ordinary airborne non-objective waves now spawn for space-based
infiltration, base, site and supply missions. UFO identity, race stats, movement,
scoring/detection and reload use the existing ordinary-wave path. Retaliation
assault generation, objective-site waves and final infiltration/base effects
remain guarded at their transition. Cycle 3 post-commit audit found no defect.

Cycle 5: linked Earth-based ordinary airborne missions launch from the source
base. Ordinary missions retain regional selection before the override; supply
missions skip it, as captured by strategic-mission-origin. Scoring preflight uses
the actual source position and lookups are bounded. Space missions ignore the
optional source for launch origin. Cycle 4 audit found no confirmed defect.

Cycle 6: hunting creates delayed missions through the world capability, applies
the immediate think countdown, resets only the hunting timer and preserves source
links. Shorter first timers retain the immediate-wave guard. Cycle 5 audit found
no confirmed functional defect. Six-cycle acceptance and final audit follow.

Final six-cycle audit reproduced unsafe completion of final no-object waves:
saved live count zero did not guarantee absence of UFO/base references. The fix
forecasts cleanup through the entire immediate chain and applies the existing
reference/expiry guards before tick mutation. Fresh/cache regressions cover both
single final waves and immediate chains, and assert unchanged state and RNG.

Final validation: root `dotnet test --no-restore` passes 1,496 tests, with no
failures or skips, including checksum/private-corpus checks. Full formatting
verification, regenerated code-map validation and diff checks pass. Obsolete
compiler object files for the four captures were removed; useful capture outputs
remain project-local. The pinned C++ checkout is unchanged. Branch 4c remains
incomplete and branch 4d has not started.

### Additional 4c cycle: operation-base waypoint continuation

Ordinary score UFO arrivals now work for every operation mode. The last waypoint
returns to a linked alien base without consuming regional RNG; absent bases fall
back to the original trajectory. Zone validation and both reference getWaypoint
calls remain ordered. Reference: AlienMission.cpp getWaypoint/ufoReachedWaypoint.
The lifecycle oracle covers all seven operation modes, absent/linked bases, and
three non-terminal transitions. Fresh/cache tests save and reload each result.
Other objectives, special arrivals, and ordinary takeoff expansion remain pending.

### Additional 4c cycle: airborne objective continuation

Ordinary airborne arrivals now support all mission objectives and operation modes.
The pinned lifecycle rows verify waypoint coordinates, speeds, RNG and reloads.
Aggressive retaliation outside zone 5 remains blocked before its discarded
first-waypoint random adjustment; site conversion and ground objectives remain guarded.

### Additional 4c cycle: ordinary ground arrivals

Ground arrivals now use reference landing eligibility, timers, altitude, marker and
speed behavior across all objectives/operations. Guards sit at site replacement,
retaliation assault, aggressive-retaliation RNG, crash creation, and flagged
base-creation transitions. The Base objective's landing flag is checked against
the current zone independently of the wave objective flag, as in AlienMission.cpp.
Fresh/cache oracle rows and an atomic base-creation boundary regression pass.

### Additional 4c cycle: ordinary takeoffs

All normal landed UFOs may count down and take off across objectives and operation
modes. AlienMission.cpp ufoLifting/addScore supply the score semantics: positive
mission points, excluding Base and Infiltration. Preflight now uses the same lift
operation for overflow accounting. All 49 objective/operation combinations pass
fresh/cache oracle tests and reload, including excluded scores at int.MaxValue.

### Additional 4c cycle: Supply spawn decisions

Supply objective waves with no linked alien base advance counters without creating
UFOs; undefined supply UFO rules also suppress spawning. Objective supply UFOs use
the reference default constructor, ignoring configured hunter-killer percentages.
Immediate-chain forecasts now account for suppression so cleanup remains atomic.
AlienMission.cpp spawnUfo and Ufo.h defaults back captured decision rows; fresh/cache
tests cover source links, space/Earth operations, percentages, immediate chains and reloads.
Supply ground destinations and non-objective hunters remain guarded.

### Additional 4c cycle: no-object wave classification

Unknown UFO IDs and deployments with no marker can advance as no-object waves,
regardless of unrelated objective flags. The pinned think predicate guards direct
site generation for marked deployments and Site objectives with objective waves.
Infiltration completion and special retaliation spawning remain guarded. Fresh/cache
fixtures cover the predicate combinations, immediate chains, atomic guards and reloads.

### Additional 4c cycle: flag-aware ordinary airborne spawning

Objective, landing-site and XCOM-base flags no longer blanket-block ordinary
spawning. Generic spawn still guards actual getWaypoint mission-site paths;
Supply keeps its separate constructor and destination rules. Generic two-point
Earth trajectories return to the operation base at waypoint 1, matching the same
getWaypoint helper used by later arrivals. Flags without site targeting preserve
coordinates and RNG in captured ordinary-spawn rows and fresh/cache reload tests.

### Additional 4c cycle: ignored UFO contacts

IgnoreUfoContact is registered in the world capability and validates stable UFO
identity, positive current contact ID and visibility before mutation. Query targets
report alert suppression. Radar, hyperwave tracking, activity and movement continue;
only the detected-contact popup and its marker allocations are suppressed.
SavedGame.cpp addUfoToIgnoreList/isUfoOnIgnoreList and GeoscapeState.cpp ufoDetection
back the oracle. UfoDetectedState.cpp btnCancelClick is the reference caller.
The ignore set is transient, matching the reference (no save key); save-neutral
state is unchanged and reload clears suppression. Fresh/cache loss/rediscovery,
idempotence, stale-command and reload tests verify this behavior.

Validation for this checkpoint: root `dotnet test --no-restore` passes all 1,522
tests, with no failures or skips, including checksum, private corpus and populated
world scenarios. Formatting verification, regenerated code-map validation and
`git diff --check` pass. Captured probes remain available under artifacts; obsolete
probe object files were removed. The pinned C++ checkout is clean.

### Batch 1, slice 1: ground destinations during spawning

Generic airborne waves may target ground trajectory points. Supply objective waves
choose their linked base without regional destination RNG; ordinary supply land
selection retains getLandPoint semantics. AlienMission.cpp spawnUfo supplies the
captured altitude rows. Fixed-point/source forecasts still reject same-tick arrivals
and special objective consequences. Fresh/cache tests compare destination, movement,
RNG, activity and transition reloads. Grounded starts are the next slice.

### Batch 1, slice 2: ordinary grounded wave starts

Generic waves initialize Landed status and groundTimer*5 on ground starts; the
same tick decrements the timer or lifts normally. Half-hour forecasts reserve
landing markers, doubled activity and any one-tick takeoff award together. Supply
ground starts, zero/overflow timers and zero-capacity damage rules are rejected
before mutation. Captured spawn rows, fresh/cache reloads and limit regressions
cover the behavior. Reference: AlienMission.cpp spawnUfo and Ufo.cpp think/setAltitude.

### Batch 1, slice 3: multiple point destinations

Ordinary spawning may choose among up to 256 destination city/point areas in
reference list order. Pure preflight verifies separation from every candidate,
rejecting possible same-tick arrivals without consuming RNG. Supply-base and final
operation-base destinations retain their precedence. Captured selected-area rows,
seeded fresh/cache tests and reloads cover both choices and draw order. Origin
regions remain fixed-point until their activity forecasts are expanded.

### Batch 1 audit

The three spawning slices were reviewed together against AlienMission::spawnUfo,
getWaypoint and Ufo::think. The root suite exposed a stale fresh/cache test that
expected generated Supply waves with city destinations to remain guarded. It now
asserts the supported regional destination, live count, source linkage and reload,
while retaining the no-object completion and its no-floating-draw collaborator.

### Batch 2, slice 4: regional origin point choices

Ordinary waves choose among ordered origin points. Activity preflight carries
minimum/maximum totals for uncertain region and country membership, including
negative scores, so an unchosen award cannot fund a later overflow. All origin and
destination pairs are checked for same-tick arrivals, with bounded forecast work.
AlienMission::spawnUfo/getWaypoint and RuleRegion::getRandomPoint back captured
origin choices. Fresh/cache tests cover RNG, per-country activity and reload.

### Batch 2, slice 5: rectangular spawn destinations

Regional destination areas may be rectangles, including reversed bounds and
wrapped longitudes. A conservative spherical bound proves separation from every
origin before RNG; overlapping or pole-crossing unsafe domains remain rejected.
Reference getRandomPoint selects raw bounds, and target setters normalize afterward.
Captured rectangle rows and fresh/cache tests verify coordinates, draw counts,
reload and atomic rejection. LandPoint remains bounded to its reference 100 attempts.

### Batch 2, slice 6: explicit mission-site waypoints

Objective waves use their selected area before operation-base or random fallback.
The ordinary path takes the area's minimum coordinates without RNG; landing-site
selection uses fixed-area draws, no city bypass, no region containment and bounded
100-attempt fake-water checks. Source and destination spawning, ordinary arrivals
and invalid selected-area checks share this precedence. Captured getWaypoint and
getLandPointForMissionSite rows, fresh/cache RNG and reload tests stop atomically
before the still-unavailable site conversion.

### Batch 2 audit

The regional-origin, rectangle-destination and selected-area slices were reviewed
together. The full suite identified three stale arrival guard tests. Two now place
the UFO on the actual conversion leg (the previous trajectory zone controls site
creation); the third verifies the newly supported fixed-area land selection and
reload. The strict unchanged-state assertions remain on unavailable conversion.

### Batch 3, slice 7: live mission interruption

Daily guaranteed research can interrupt missions with live ordinary UFOs. A pure
daily mission projection is shared with craft preflight; interrupted arrivals
destroy the UFO and preserve the reference current-tick early return before craft
combat. Later cleanup releases live counts. Existing research uncertainty and
orphan-link guards remain precise. GeoscapeState::time1Day, AlienMission::isOver
and ufoReachedWaypoint back live oracle rows, reload and same-day research tests.

### Batch 3, slice 8: base deletion with live Earth operations

Daily base self-destruction no longer blanket-blocks live Earth missions. Shared
forecasts clear links and interrupt before UFO arrivals and craft pursuit. Space
missions retain their reference continuation. Captured live operation rows for all
seven modes, fresh/cache transition reloads, pre-daily links and terminal follower
return cover deletion; ambiguous marker identities still stop atomically.

### Batch 3, slice 9: immediate generated no-object waves

Base-generated missions whose possible first timers are due in the creation tick
join no-object wave preflight. Finite zero-timer chains reserve bounded work and
reject UFO/site creation or deferred objective completion before daily mutation.
Generation IDs and history persist even when the mission finishes immediately.
Captured initial timers, existing think-chain evidence, fresh/cache reloads and
resource-limit tests cover the safe path; immediate UFO generation remains guarded.

### Batch 3 audit

Live interruption, linked base destruction and immediate generated no-object waves
were reviewed together against the daily/half-hour/five-second caller order. No
implementation issue was confirmed. Root `dotnet test --no-restore` passes all
1,548 tests, including populated scenarios, checksum checks and all staged saves.

### Batch 4, slice 10: immediate no-object hunting missions

Hunting can immediately execute no-object waves using the shared bounded counter
operation. Its explicit think occurs after the half-hour pass; finished missions
therefore remain until the next half-hour cleanup. Hunting timers reset once and
source/count history persists. Captured baseHunting plus verbatim no-object counter
fragments, fresh/cache RNG and reload tests cover both zero and short first timers.
Immediate UFO/site/effect outcomes remain guarded.

### Batch 4, slice 11: rectangular Earth-operation origins

Existing operation bases override ordinary rectangular starting regions after
reference getWaypoint draws and land retries. Pure forecasts score and separate
the actual base origin, while validating the discarded regional domain and selected
site area before mutation. Captured rows cover all six Earth modes and ground/air
following legs. Fresh/cache tests verify coordinates, RNG, reload and the remaining
Space-operation rectangle-origin boundary.

### Batch 4, slice 12: aggressive retaliation ordinary arrivals

Ordinary retaliation arrivals retain both getWaypoint selections, with two base-near
random draws between them when aggressive retaliation finds the first regional
player base. The first point is discarded by the pinned reference's shadowed local.
Base list order and bounded preflight work are preserved. Captured enabled/disabled
and present/absent-base rows, fresh/cache rectangle destination RNG and reload tests
cover the behavior. Retaliation spawning, assault outcomes and combat stay guarded.

### Batch 4 audit

The final three slices were reviewed together. A confirmed work-limit bypass was
found in aggressive retaliation: an empty geographic area list charged zero for
each visited base. The forecast now counts the base iteration as well as its area
checks. A populated technical-region regression failed before the fix and passes
after it, preserving the strict atomic state/RNG assertion. Formatting corrections
from the earlier audit test changes are included.

All twelve implementation slices and four batch audits are complete. The full 4c
headless gate remains open: general Space-operation rectangular origins, direct
site/deployment spawning, new alien-base objectives, infiltration/pacts, detection
VM providers, hunter/escort behavior, immediate UFO hunting waves and composed
initial/monthly world hooks remain outstanding. Branch 4d remains untouched.

Validation: root `dotnet test --no-restore` passes all 1,554 tests with no failures
or skips. Formatting verification, checksum tests, regenerated code-map validation
and `git diff --check` pass. All 19 staged-save horizons remain explicit and
unchanged (ten at 360 executable ticks, one blocked at 125 ticks, eight tactical
saves preserved at zero ticks). The pinned C++ checkout is unchanged. Useful
captures are retained; the four obsolete probe object files were removed.

### Additional batch, slice 1: airborne rectangular origins

AlienMission::spawnUfo/getWaypoint and RuleRegion::getRandomPoint now support
separated rectangular Space origins, including reversed raw and wrapped longitude
bounds. Spawn forecasts carry bounded spherical origin/destination domains;
uncertain rectangular activity reserves possible positive/negative awards before
RNG, while actual scoring preserves first-containing owner order. Conservative
score ranges can reject near-limit totals in unrelated owners; exact rectangular
membership forecasts remain a refinement. Captured rectangleOriginRows,
fresh/cache tests, crossing-country awards, overflow and overlap guards, and
transition reloads cover the slice. Land-tested and selected landing rectangles
remain separate capabilities. Fifteen focused compatibility tests pass.

### Additional batch, slice 2: land-tested rectangular origins

Ordinary origin getWaypoint selection now accepts rectangular land candidates,
including the fake-water choice, geographic region restriction and forced
hundredth candidate. Existing LandPoint owns the draws; pure spherical forecasts
still prove safe separation before spawning. A per-tick land-query budget counts
polygon vertices, region checks and all 100 possible retries before mutation.
AlienMission::getLandPoint/getWaypoint/spawnUfo back landRectangleOriginRows;
fresh/cache tests assert exact interpolation, draw counts, actual scoring and
reload. A populated retry scenario checks atomic resource-limit rejection.

### Additional batch, slice 3: selected rectangular mission-site origins

Selected objective origin areas now project their full rectangle when the wave
requests landing-site selection. The fixed-area MissionSiteLandPoint path keeps
its fake-water roll and 100-attempt limit, consumes no area-selection RNG and
ignores geographic region containment. Nonlanding selected areas still use the
raw minimum coordinates without RNG. AlienMission::getLandPointForMissionSite,
getWaypoint and spawnUfo back selectedRectangleOriginRows. Fresh/cache tests
verify both paths, successful/forced retries, reload and atomic stopping before
UFO-to-site conversion; event and tactical boundaries remain guarded.

### Additional batch audit (2026-10-03)

The three origin slices were reviewed together against the pinned spawnUfo,
getWaypoint, getLandPoint, getLandPointForMissionSite and target setter paths.
Root tests passed all 1,568 cases before the audit correction. A regression was
confirmed: reusing the rectangle projection rejected point origins beyond a pole,
although the previous point projection and reference Target setters normalize
them. A captured polePointOriginRows fixture and fresh/cache regression cases
fail before the fix. Point domains now preserve that normalization; uncertain
rectangles retain the single-pole-domain guard. The corrected project builds
successfully, and the user reports all post-fix tests passing. Fixture input
checksums, code-map validation and `git diff --check` pass. The changed pinned
probe and rules text now use LF bytes, matching Git storage and clean checkouts.
The user verified formatting from normal PowerShell with
`dotnet format Oxce.slnx --verify-no-changes --no-restore` (exit code 0).
The audit fix is committed as `1d532c1`; this batch's validation is complete.
The sandbox named-pipe failure remains unresolved. No permissions were changed.

General separated Space rectangular origins are implemented. Direct site and
deployment spawning, new alien-base objectives, infiltration/pacts, detection VM
providers, hunter/escort behavior, immediate generated/hunting UFO waves and
composed initial/monthly world hooks remain outstanding. Conservative rectangular
activity and geometry forecasts still reject uncertain near-limit or overlapping
domains before mutation. Branch 4c remains incomplete; branch 4d is untouched.

- 4c site-script candidate slice: pinned the exact processCommand pruning loop
  in strategic-mission-site-candidates. The gameplay projection matches 256
  combinations of occupied regions, recent point locations, rectangular
  texture/direct/site-type eligibility, missing zones and invalid negative
  zones. Live site command scheduling remains the next slice.

- 4c live site-script slice: explicit single-region site commands now choose the
  weighted initial mission, draw target-base odds, retry mission types in
  circular order, select a valid area and store repeat/run history before
  starting the owned mission. The full pinned command block captures seven
  selection/order traces. Fresh/cache scheduling, direct site birth after
  reload and tagged adhoc acceptance replace obsolete blanket site guards.
  All 55 focused scheduling cases pass. Multi-region rejection sampling remains
  a precise boundary until a bounded selection proof is available.

- 4c bounded site-region/history slice: isolated commands now select weighted
  or uniform regions when every possible selected region has candidates.
  Unsafe retry paths and broader batches with changing multi-region candidates
  stop before RNG. The pinned command capture includes uniform selection and
  global candidate-index offsets. Point history persists across direct site
  births and suppresses exhausted location sets; live batch occupancy produces
  false labels for later conditionals. History capacity and all scan/repeat
  checks are reserved before mutation. All 66 focused scheduling cases pass
  fresh/cache and transition reloads. Due event effects remain branch 6.

- Site-script batch audit confirmed that empty location-history keys bypassed
  the new capacity check and were omitted from work accounting. The regression
  fails before the fix. Preflight now reserves both location entries and new
  variable keys across the whole batch, and charges enumeration of empty keys.
  No RNG or campaign mutation occurs when either history limit is exceeded.

- Site-script batch final validation: all 1,775 root tests pass with zero
  failures/skips, including private corpus classifications, endurance, checksums
  and allocation coverage. Formatting verification, regenerated code-map and
  diff checks pass. The pinned reference checkout remains clean; the obsolete
  site-candidate probe object was removed. Weighted/uniform safe site scheduling
  and history are accepted for these slices; arbitrary region rejection
  sampling, composed multi-region forecasts and the broader 4c gate remain
  open. Event payload execution and branch 4d remain deferred.

- 4c bounded region-selector slice: the pinned site-command probe now captures
  rejected weighted and uniform regions. WorldMissionSiteSelection conditions
  the distribution on eligible regions, preserving relative weights, uniform
  region order and per-region area choice. Fully eligible draw ranges remain
  unchanged; rejected draws are omitted under the contract's RNG-stream
  non-goal. ADR 0030 records the decision and safety requirements. Seventeen
  focused site-script cases pass; live adoption follows next.

- 4c live conditioned-region slice: weighted and uniform site scripts now
  filter regions removed by occupancy, base targeting and repeat history
  without an unbounded retry loop. Composed commands observe live candidate
  changes; earlier multi-region guards are removed. Positive selected/listed
  timeline region sets must agree before RNG, and work reservations include
  earlier possible mission/history growth. Fresh/cache composed batches,
  default countdown draws and reloads pass in 72 focused scheduling cases.

- 4c empty site-command slice: the pinned reference confirms an empty mission
  listing returns false after target-base odds, without touching race, delay,
  history or run counters. Shared monthly/adhoc execution now follows that
  path, including negative conditional fallthrough and unchanged exhausted
  counters. Fresh/cache reload coverage confirms future scheduling remains
  available while due tagged event payloads still stop before mutation.
  All 76 focused scheduling cases pass.

- Conditioned site-scheduling batch audit found no confirmed implementation
  defect. Added fresh/cache regression coverage for divergent signed listing
  and unsigned region-selection timelines: the whole batch blocks before any
  command mutates state or draws RNG. Corrected the current site status summary.
  Final validation passes all 1,786 root tests with zero failures/skips,
  including private classifications, endurance, fixture checksums and populated
  allocation gates. Formatting verification, code-map and diff checks pass.
  The reference checkout is clean; the obsolete probe object was removed.
  Broader 4c acceptance remains open and branch 4d is untouched.

- 4c operation-base evidence/option slice: a new pinned full AlienMission::start
  capture records 72 controlled creation searches, including technical regions,
  fake-water preferences, 99th-attempt acceptance and 100th fallback/suppression.
  Mod.cpp defaults allowAlienBasesOnWrongTextures to true. Its explicit false
  override now projects into immutable runtime settings; compiler revision 34
  rebuilds older caches. The existing initialization-prefix oracle is retained.
  Actual base creation remains guarded until the following gameplay slice.

- 4c bounded operation-base selector: gameplay now owns the reference's
  fake-water odds, ordered area and coordinate draws, technical-region bypass
  and finite 100-attempt search. The final attempt either returns the forced
  point or suppresses creation according to the projected option. All 72
  pinned traces match outcomes, land checks and draw counts/state. The selector
  has no state effects; live initialization follows in the next slice.

- 4c live operation-base creation: scheduled mission/adhoc commands now create
  and link operation bases for regional-new and missing fallback modes; Earth
  new-base retains reference existing-base reuse. Base age, immediate hunting
  timer, race, marker identity and mission links persist through creation and
  no-object mission cleanup reloads. Mission points are awarded at creation,
  except for infiltration. Composed batches reserve marker IDs, population,
  geographic work and worst-case activity before RNG or mutation. Empty/missing
  zones fail intentionally, and unused creation fields are skipped on reuse.
  All 94 focused operation/scheduling tests pass. Shared mission/UFO/player marker
  namespaces and broader unavailable transitions retain precise boundaries.

- Operation-base batch audit reproduced deleted sidecar resurrection when a
  replacement reused deployment and marker ID before rewrite. The regression
  fails fresh/cache before the fix. A separate world-owned 64-bit birth sequence
  now supplies persistent creation keys, survives deletion/reload and cannot be
  changed through gameplay counters. Exhaustion blocks before RNG; inconsistent
  saved birth identities fail intentionally. ADR 0031 records the save extension.

- The same audit also confirmed missing craft-marker collision and legacy terror
  namespace checks in creation. Both reproductions scheduled ambiguous targets
  before the fix. Creation now reserves existing and incoming craft identities with bounded
  scans and rejects the legacy namespace before RNG, matching the established
  alien-base upgrade/save-resolution boundary.

- The final resource audit also reproduced omitted regional containment costs.
  Candidate preflight now reserves region-area scans, fallback checks, live
  selection and earlier possible births before enumerating bases. Forced-new
  operations skip irrelevant existing-base identity scans. The regression
  exceeds the old budget and is now refused with unchanged campaign/RNG.

- Operation-base cycle final validation: all 1,813 root tests pass with zero
  failures/skips, including fixture checksums, staged-save classifications,
  composed endurance scenarios and populated allocation checks. All 109 focused
  scheduling tests pass. Formatting verification, regenerated code-map and diff
  checks pass. Confirmed sidecar, marker-identity and geometry-work findings are
  fixed with regressions; the reference checkout remains clean and the obsolete
  probe object was removed. Broader 4c acceptance remains open; 4d is untouched.

### Base-objective search evidence

The base objective in AlienMission::think, chooseAlienBaseType and
Texture::getRandomDeployment are captured verbatim at 4df3a5e. The 432-row
strategic-objective-base-spawn oracle covers fixed/texture/emergency deployment
selection, single zero-weight selection, malformed multi-entry zero weights,
technical regions, fake-water preference, 99th acceptance and 100th fallback.
WorldObjectiveBaseSpawn implements this pure search. Unlike operation creation,
it draws coordinates before deployment/preference, repeating the latter for
every dynamic attempt. Fresh/cache tests execute the actual linked deployment
selector and compare counters/errors with every captured row. Live final-wave
and landing-site creation remain guarded until their atomic reservations exist.

Final-wave base objectives now execute after the wave counter advances, including
ordinary UFO, no-object and direct-site waves and finite immediate chains.
Explicit, texture-weighted and emergency deployments share persistent birth
identity and constructor defaults with operation bases. The objective birth
does not become the mission's operation-base link; a suppressed search completes
without interruption or allocation. Preflight reserves IDs with daily research,
population, birth sequence, geometry work and combined score ranges before the
affected tick. Fresh/cache tests cover reference RNG order, composed completion,
reload before/after birth, no duplicate effects, collision/overflow and deliberate
invalid-zone/table failures. Base-generated immediate missions still stop before
unreserved objective creation. Landing-site creation remains the next slice.

Landing-site base objectives now create at the UFO's actual accepted landing
position. The pinned landing block uses the current trajectory zone, independently
of wave.objective; mission-site conversion has priority, and rejected landings
create nothing. Deployment selection uses MissionArea's default texture 0 and
does not run a fake-water preference roll/search. The expanded oracle captures
384 landing decisions, including the inspected conversion/accepted-landing
envelope. Fresh/cache tests compare every decision and deployment draw, exercise
real land/water arrivals and conversion precedence, reload through takeoff and
verify composed final-wave/landing IDs and score limits. Arrival creation shares
site-conversion counter forecasts and checks birth identity, population and
marker collisions before mutation. Interception/deployment remains branch 5.

The base-objective batch audit reproduced three omissions and fixes them in a
separate commit. Landing reservations now charge prior birth/counter forecast
scans (100 arrivals with 3,000 expiring sites exceeded the uncharged limit).
Final-wave births reserve combined existing/new-base patrol discovery work
(4,000 patrollers, 190 existing bases and 80 births exceeded one million checks).
The reference timeAdvance fallthrough also exposes newborn final-wave bases to
time10Minutes hunting. An eligible fueled craft and exhausted ALIEN_MISSIONS
IDs reproduced overflow after mutation. A precise creation-tick hunting guard
now checks all possible areas/deployments and current/autopatrol/incoming craft;
timer overflow is rejected too. Birth with no eligible craft preserves timer
fallthrough and reload. Landing births occur after the hunting/fuel pass and do
not use that guard or discovery reservation. Broad creation-tick hunting mission
forecasts remain 4c work; infiltration/pacts remain guarded.

The shared-birth audit also reproduced negative deployment hunting timers in
operation commands and landing objectives: both created state rejected on reload.
Both preflights now reject these malformed counters before mutation. Blank marker
names use the same whitespace-aware validation as site creation.

The final ID audit reproduced a shared craft/base marker collision: hourly
Production::step precedes the thirty-minute mission pass in
GeoscapeState::timeAdvance, and craft creation consumes its type's SavedGame ID
namespace. A craft build moved SHIP's counter from 2 to 3 after the preflight,
creating a second alien base with ID 3. Final-wave and landing births now stop
before hourly ticks with potentially shared craft production; these production
ID forecasts remain explicit 4c work. The scans are bounded, and fresh/cache
regressions cover both callers without advancing the clock or RNG.

Validation: root dotnet test --no-restore passes all 1,841 tests, with no failures
or skips, including checksum, private-corpus and populated world coverage.
Formatting verification, regenerated code-map validation and git diff --check
pass. Useful captured probes remain available; obsolete probe objects were
removed. The pinned reference checkout is clean. Branch 4c remains incomplete,
and branch 4d is untouched.
