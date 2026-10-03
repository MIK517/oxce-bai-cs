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

Site-script scheduling, new-base creation when required, deployment waves, special
objective effects, hunting/escort UFO behavior and integrated campaign month
progression retain capability guards. Existing regional/Earth base acquisition
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
