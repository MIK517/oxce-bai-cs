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
