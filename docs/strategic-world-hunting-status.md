# UFO hunting and escort integration

Branch 4c. Pinned reference: 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15.

The strategic-hunting capture inserts Craft::getHunterKillerAttraction,
Ufo::insideRadarRange and GeoscapeState::ufoHuntingAndEscorting's selection
fragment verbatim. Controlled collaborators supply effective capacities, craft
eligibility and distances. Ninety-six attraction rows and fourteen selection
rows pin both hunt modes, strict saved-order comparisons, original-target ties,
ignored/undetectable exclusions, zero radar and inclusive range.

The gameplay-owned selection operation preserves those rules. Arithmetic outside
signed 32-bit range and zero divisors fail intentionally. Captured outputs and
malformed inputs have focused compatibility tests. The retarget and integration slices below connect the operation to loaded actors.
Combat remains branch 5; branch 4d is untouched and the headless gate remains open.

The retarget slice adds reference destination backup, hunt speed, escort speed and
waypoint restoration operations. Captured transitions retain the original waypoint
across hunt-to-escort changes and leave speed unchanged on restoration. Explicit
selected speed controls the five-second/one-minute fast path; ten-minute retargeting
is always due. Mod::hunterKillerFastRetarget defaults true and survives fresh/cache
projection. The redundant-HK-alert option defaults true, honors fixed user options
and persists through the save adapter. Compiler revision 33 invalidates stale caches.
The option and transition fixtures cover fresh/cache projection and reload;
the live integration is described below.

Live imported hunters and escorts now use registered ten-minute retargeting after
base hunting, plus the explicit slow-speed fast path before five-second movement.
Retargeting honors projected fuel returns, preserves original waypoints, allocates
contact markers and publishes gameplay-owned hunt notifications. Alert suppression
does not prevent revelation or targeting. Movement follows craft/UFO positions in
reference list order; snapshot capture resolves final target coordinates without
changing movement. Deleted escort targets restore their original waypoint.

Combat arrivals stop atomically before mutation. Retargeting involving same-tick
grounded auto-patrol relaunches or airborne craft transfers retains specific
forecast guards. Escort chains/cycles and malformed originals fail intentionally.
Deletion scans are bounded before nested work. Grounded non-escort waves and
airborne non-escort waves with no possible craft target execute through the
constructor/spawn slices below. Creation-time escorts and candidate-bearing
airborne waves retain their atomic forecast boundary.
The headless gate remains open and branch 4d is untouched.

The three-slice batch audit reproduced a same-tick boundary bypass: a hunter's
new revelation was absent from an imported pursuer's craft-arrival forecast.
Craft interception checks now use the retarget projection before any mutation.
Regression tests also reject signed 32-bit intermediate attraction overflow even
when later terms would produce an in-range result; each C++ operation is bounded.
The root allocation gate caught eager closure allocation in the escort forecast
on ordinary flight. An explicit identity scan removes that allocation, preserving
the existing 8/32/128-UFO budgets. Handler-order assertions include both registered
retarget callers without weakening their exact ordering.

Final cycle validation passes all 1,692 root tests with no failures or skips,
including fixture checksums, all 19 staged-save classifications, 72-hour composed
scenarios and populated allocation budgets. Formatting verification, code-map
validation and diff checks pass. The pinned C++ checkout remains clean; obsolete
probe object output was removed. Branch 4c remains incomplete and 4d is untouched.

Hunter constructor initialization is now an executable gameplay operation.
The pinned Ufo constructor capture has 192 rows covering nonpositive percentages,
failed/successful rolls, percentages above 100, literal modes/behaviors and the
ordered optional random selections. Failed rolls consume no mode/behavior draws;
nonpositive percentages consume no RNG. Supply objective callers retain the
reference constructor defaults. Positive hunter spawning remains guarded until
the following slices connect safe wave contexts.

Grounded non-escort hunter waves now use the captured constructor operation
before geometry selection. Wave values inherit rule defaults only at -1.
Initial ground timers above one keep retargeting outside the creation tick;
fresh/cache tests cover constructor metadata, ground countdown, lift and reload.
Airborne hunter creation and spawn escorts remain precisely guarded.

Airborne non-escort hunter waves now execute when existing craft and undelivered
craft transfers cannot supply a current or auto-patrol target. That structural
proof keeps the creation-tick path ordinary; later dispatch uses the loaded
hunter retarget capability. Rule-derived hunter percentage/mode/behavior and
fresh/cache creation/targeting reloads are covered. Candidate-bearing airborne
spawns and creation-time escorts retain the existing atomic wave boundary.

The constructor/spawn batch audit confirmed an empty hunter trajectory could
index its first waypoint before the existing minimum-length guard. Length and
altitude validation now precede hunter context checks. Empty/one-point regressions
assert an unchanged campaign and RNG, and a separate auto-patrol regression
checks that a possible same-tick relaunch cannot enter the no-candidate path.

The previous supply constructor-choice fixture now executes its formerly guarded
no-candidate hunter cases and asserts captured hunter flags, counts, live links
and reload. Objective supply ships still assert the default non-hunter
constructor. Mode/behavior capture draws now alternate so swapped draw order
cannot pass the shared canonical oracle comparison.

Final constructor/spawn cycle validation passes all 1,701 root tests with no
failures or skips, including checksums, private classifications, endurance and
populated allocation checks. Formatting verification, code-map validation and
diff checks pass. The reference checkout is clean and obsolete probe object
output was removed. The remaining 4c headless gate is open; 4d is untouched.

Airborne hunter waves now accept existing airborne craft ignored by the pinned Craft::isIgnoredByHK rule (low fuel or mission complete). Four captured truth-table rows and fresh/cache spawn reloads cover this extension; active targets remain guarded.

New airborne hunters also accept undetectable existing craft, which the pinned saved-order selection trace excludes before attraction evaluation. Fresh/cache tests assert the effective rule, non-hunting spawn and reload. Original targets retain their separate reference semantics; transferred craft remain conservative.

New flying hunter waves reserve their aggregate craft-scan work together with loaded retarget actors before the creation tick. A reproduced 1001-by-1001 workload previously threw after creation; it now blocks without state or RNG changes. The no-target structural scan is cached only for the current preflight/tick, keeping immediate chains bounded.

The fourth batch audit checks returning/undetectable exclusions and per-tick cache lifetime. An hourly-production reservation test covers the finite queue upper bound. Suspected multi-craft output undercount was rejected: runtime linking already enforces exactly one craft per production unit. No additional gameplay defect was confirmed in these three slices.

Final fourth-cycle validation passes all 1,710 root tests with zero failures or
skips. Fixture checksums, private-corpus classifications, 72-hour scenarios,
allocation budgets, formatting verification, code-map and diff checks pass.
The pinned C++ checkout remains clean, and obsolete capture object output was
removed. Branch 4c remains incomplete; branch 4d has not started.

Creation-time escort initialization now has pinned executable evidence from
AlienMission::spawnUfo and Ufo::setEscortedUfo/backupOriginalDestination.
Eighty captured rows cover successful/failed hunter status, the escort flag,
missing leaders, mission filtering, hunter exclusions, first saved-order leader,
all leader statuses, origin relocation, original destination and full speed.
The owning gameplay operation reproduces those rows; live wave callers are the
next two slices. Combat and deployment remain branch 5.

### Creation-time escorts: live single waves

Airborne escort waves now run AlienMission::spawnUfo's constructor and initial
leader attachment through gameplay-owned mission spawning. The saved-order leader
may be flying, landed, ignored, crashed or destroyed. Timer-driven deletion repairs
the new follower's original waypoint and mission live count; no-leader waves retain
the escort request without changing their trajectory speed. Fresh/cache cases and
save/reload on both sides of the transition are covered.

Preflight reserves creation/retarget scans and cleanup over the final UFO population,
validates the effective full escort speed, and stops before craft retargeting or
grounded escort takeoff needs further forecasts. Multiple same-tick births remain
guarded for the immediate-wave slice. Reference: AlienMission::spawnUfo,
Ufo::setEscortedUfo/resetOriginalDestination and GeoscapeState::time5Seconds.

### Creation-time escorts: immediate wave chains

Finite immediate waves now preserve the live saved-list order when leaders and
escorts are created in the same half-hour tick. The reference capture adds four
sequence rows from AlienMission::spawnUfo and the idle escort fragment of
GeoscapeState::ufoHuntingAndEscorting. Earlier leaders relocate the initial escort
origin; later leaders attach during retargeting without relocating it. Multiple
escorts skip other hunters, follow the first ordinary leader and keep their own
original waypoints.

Fresh/cache integration advances both ordering variants, saves before and after
subsequent movement, and preserves references to a leader whose contact ID is
still zero using its positive STR_UFO_UNIQUE identity. Scheduling reserves newborn
and existing escort scans over the final population. Excessive immediate escort
work stops before any birth or RNG draw. Loaded idle escorts attaching to newborn
leaders still stop at a specific pursuit-forecast boundary; grounded escorts and
creation-time craft hunting remain explicit future 4c work.

### Escort batch audit

The three-slice audit reproduced a relocation scoring defect: newborn escort
activity was forecast at its ordinary origin even though AlienMission::spawnUfo
moved it to a leader before GeoscapeState::time30Minutes scored it. Overflow or
underflow in the leader's region/country therefore threw after mutation.

Activity preflight now tracks saved leader positions and all possible earlier
ordinary births in list order, including probabilistic hunter constructor outcomes.
A certain hunter escort with a certain leader scores only at the candidate leader
origins; otherwise the original birth point remains a possible outcome. Geography
work and candidate storage stay bounded and no forecast draws RNG.

Sixteen region/country boundary regressions cover saved and newly born leaders,
positive/negative scores, atomic refusal and exact-fit success with reload.
A terminal-leader regression also saves after the reference's early return,
then verifies next-tick follower restoration and mission live-count release.

Final escort-cycle validation passes all 1,745 root tests with no failures or
skips, including private classifications, endurance, checksums and allocation
checks. Formatting verification, code-map and diff validation pass. The pinned reference checkout remains
clean; obsolete probe object output was removed. Branch 4c's headless gate remains
open and branch 4d has not started.

### Grounded escort births

Grounded escort waves with a timer above one now initialize beside a saved
ordinary leader, or keep their request and trajectory speed without a leader.
The extended pinned spawn capture has 160 rows and confirms that escort
initialization retains the source status. Active craft do not block a grounded
birth because GeoscapeState skips landed UFOs in the retarget pass.

Fresh/cache scenarios assert ordered detection notifications, stationary ground
timers, untouched shields, preserved original waypoints, takeoff at trajectory
speed and save/reload before and after flight. Reference: AlienMission::spawnUfo,
GeoscapeState::ufoHuntingAndEscorting/time5Seconds and AlienMission::ufoLifting
(the existing lift oracle). Creation-tick takeoff is the next slice.
