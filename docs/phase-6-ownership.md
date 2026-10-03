# Phase 6 ownership and fixture ledger

Reference: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. Implementation
branches: `codex/strategic-base-logistics`, `codex/strategic-base-readiness`,
`codex/strategic-research-production`, the branch 4a world foundation, and
`codex/strategic-world-operations` (4b, merged in `e10d96f`).
`codex/strategic-world-integration` (4c) owns remaining headless world simulation;
`codex/strategic-world-ui` (4d) owns UI and combined closure
under the 2026-09-26 scope revision. This is
an implementation/evidence ledger; bounded acceptance is recorded in their status files.
Checkpoint notes below retain the scope and outstanding work at each implementation milestone.

| Behavior/state | Owner | Reference | Evidence/status |
|---|---|---|---|
| Ordered tick fallthrough and popup pause | Branch 1 | `GeoscapeState::timeAdvance` | `strategic-time` extracted-loop probe; handler internals excluded |
| Highest-trigger summary | Branch 1 | Existing C# event contract | Retain exclusive counts; execute effects before event publication |
| Tick eligibility and unsupported live state | Branch 1, broadened in each branch | Timed handlers, `SavedGame::load` | Port safety boundary; preserve-only imports remain readable |
| Inventory, money, research eligibility, capacities, purchase limits | Branch 1 | `PurchaseState`, `SellState`, `Base`, `SavedGame::addMonth` | Logistics/critical-sale fixtures; monthly reset in isolated handler |
| Soldier identity, initial stats/name/armor, templates | Branch 1 | `Mod::genSoldier`, `Soldier`, `PurchaseState` | Generation/piloting/template fixtures; no delayed generation |
| Craft initialization, cargo/assignment effects, arrival checkup | Branch 1 | `Craft`, `Transfer`, purchase/sale/transfer states | Starting/purchase/arrival and save-cycle fixtures |
| Transfer quantities, cost/distance, time and mobile save fields | Branch 1 | `TransferItemsState`, `TransferConfirmState`, `Transfer` | Extracted arithmetic and two-base fixtures; mobile ownership |
| Facility editing, training, recovery, transformations, servicing | Branch 2 | Base/personnel/craft states, timed handlers | Implemented with fresh/cache/save and indexed UI acceptance. See [readiness status](strategic-readiness-status.md). |
| Research and production progression | Branch 3 | `ResearchProject`, `Production`, timed handlers | Implemented with extracted C++ progression oracle, fresh/cache economy chain, save/reload and indexed UI. See [branch 3 status](strategic-research-production-status.md). |
| World rule projection, geometry and target graph | Branch 4a | `Target`, `MovingTarget`, `SavedGame::load`, mission/target classes | Implemented as a bounded foundation with arithmetic oracle, save graph and rewrite fixtures; live world state still stops time. See [world foundation status](strategic-world-foundation-status.md). |
| Ordinary UFO lifecycle and headless craft operations | Branch 4b | `GeoscapeState`, `AlienMission`, `Ufo`, `Craft` and target classes | Headless dispatch/patrol/recall and relaunch; bounded UFO transit, existing-mission countdown, ordinary airborne spawning, landing/timers/takeoff/departure, activity scoring and base/craft radar detection implemented. Later-trajectory ten-minute base scans and saved discovery are implemented. Cleanup/persistence cases, four reference lifecycle traces, 72-hour composition, terrain continuation, population/allocation measurements and fresh/cache classifications of 19 private saves satisfy the revised closure gate. See [operations status](strategic-world-operations-status.md). This accepts the bounded ordinary lifecycle locally; full world integration remains with 4c. |
| Mission/arc/event scheduling, broader world lifecycles, pursuit and scripts | Branch 4c | `GeoscapeState`, `AlienStrategy`, mission/site/base/event classes and world script callers | Event producer scheduling, history, countdowns and interrupted cleanup execute, with due payloads guarded for branch 6. UFO shields, selected-speed context, target dispatch, craft pursuit/escort and loss handling are implemented. Mission/arc/adhoc/event runtime projections and bounded timeline selection are implemented (see [evidence](strategic-script-rule-status.md)); isolated monthly [arc](strategic-arc-script-status.md), [mission](strategic-mission-script-status.md) and [event](strategic-event-script-status.md) phases and tagged [adhoc](strategic-adhoc-script-status.md) scheduling execute bounded selection and owned state changes. The [score/funding context](strategic-world-monthly-context-status.md) is calculated from live campaign state. [Mission-site timers, counters, craft-followed expiry and associated-UFO reactivation](strategic-world-site-timer-status.md) now advance, including same-tick base scans/flight/shields and ordinary airborne site-objective outbound legs. Campaign scheduler hooks, full mission modes, broader lifecycles, scripts and UFO hunting/escorts remain outstanding in the [delivery plan](phase-6-world-integration-plan.md). Globe/UI and combined closure belong to 4d. |
| Globe controls and combined world acceptance | Branch 4d | `Globe`, craft/target UI and `GeoscapeState` | Starts after 4c merges; indexed globe, target commands, notifications, explicit speed and save/load. See [approved split](phase-6-world-integration-plan.md). |
| Interception and strategic deployment | Branch 5 | `DogfightState`, landing/deployment callers | Deferred until 4d closes |
| Monthly ledger, event consequences, strategic endings | Branch 6 | `MonthlyReportState`, `GeoscapeEventState`, `SavedGame` | Deferred; earlier monthly hooks must not run twice. 4c persists scheduled events but guards due payloads and instant-event producers before unavailable effects. |
| Active battle, battle-derived personnel changes and debriefing | Phase 7 | Battlescape and debriefing | Preserve-only; tactical continuation unavailable |
| Full diaries/graphs/Ufopaedia presentation | Phase 8 | Corresponding UI states | Underlying strategic state belongs to its earlier owner |

Per-property defaults, widths, options, script dependencies and malformed-input cases
are added with each owning fixture. Unknown fields alone do not imply an unsafe action;
fields required by the action or its next tick do. Positive executable, blocked, and
inspect/preserve corpus results must be reported separately.

Alien-base daily activity, facility discovery and fuel-ordered craft patrol sight
now execute under branch 4c ownership. `strategic-alien-base-activity` pins the
reference statements; `StrategicWorldAlienBaseActivityTests` covers fresh/cache,
transition reloads, combined score overflow, passive 72-hour advancement and
unknown/deleted save overlays. Daily linked mission creation now executes with
reference region/race selection, site areas and persistent generation counters;
`strategic-alien-base-generation` and `StrategicWorldAlienBaseGenerationTests`
cover all objectives/operation modes at creation, fresh/cache, reloads, 72-hour
composition and atomic guards. Immediate creation-tick waves, hunting, linked
self-destruction remain deferred. Monthly evolution/upgrades now have an isolated
subsystem operation; campaign month rollover remains guarded. See
[generation status](strategic-world-alien-base-generation-status.md) and
[activity/discovery status](strategic-world-alien-base-activity-status.md).

Daily mission interruption belongs to branch 4c and now runs after daily research
and before alien-base activity/generation. The extended generation oracle and
`StrategicWorldMissionInterruptionTests` cover zero-live interruption/cleanup,
same-day primary/lookup completion, following-day interruption of generated
missions, save/reload and atomic guards. Shared read-only research proofs forecast
guaranteed and possible discoveries; live-UFO interruption and uncertain daily
research outcomes remain explicitly guarded until their combined effects are owned.

Monthly alien-base upgrades and race evolution belong to branch 4c. The
`strategic-alien-base-monthly` extracted probe and fresh/cache transition tests
cover weighted age selection, reset flags, hunting timers, ordered evolution,
atomic loop/resource guards, marker-link repair and stable save sidecars. The future
monthly caller runs this phase after event scheduling. See
[monthly base status](strategic-world-alien-base-monthly-status.md).

## Implemented prerequisites

`StrategicTimeFixtureTests` matches all 18 extracted-loop traces. Dispatcher unit tests
cover pause fallthrough, preflight without partial mutation, batching equivalence and
the empty million-tick allocation bound. `CampaignSaveRegressionFixtureTests` covers
soldier A -> transit -> B, reordered bases, repeated rewrites, deletion/recreation,
duplicate ownership and the guarded month boundary. Save-adapter/foundation unit tests
also cover legacy types and preservation after new-campaign emission.

Item/staff quote, order, transfer and arrival commands are implemented in the working
branch. `CampaignLogisticsTests` covers atomic rejection, incoming capacity reservation,
minimum transfer cost, purchase-log persistence, and save/reload before and after arrival.
`StrategicLogisticsFixtureTests` checks extracted `RuleItem` price, `Base::storesOverfull`,
and `TransferItemsState::getDistance` arithmetic. The corresponding probe deliberately
stubs the script hook; actual VM input/event plumbing is covered separately by
`ScriptRuntimeFrameTests`. These results do not establish complete branch acceptance.

Soldier name-pool loading and constructor generation are implemented. Reference
sources inspected: `Mod/RuleSoldier.cpp`, `Mod/SoldierNamePool.cpp`,
`Savegame/Soldier.cpp`, `Mod/Mod.cpp::genSoldier`, and
`SavedGame::selectSoldierNationalityByLocation`. The public logistics fixture checks
fixed initial stats, bravery truncation, initial psi skill, female-name/callsign fallback,
look weights and ten-attempt duplicate handling. Scalar recruit templates apply after
generation, including stat merge sentinels, mana reroll, and nationality/name refresh.
Starting soldiers load literal stats instead; random starting soldiers select all types
before generation and do not apply the recruit template. Starting craft load their
supplied weapons/cargo instead of initializing purchased fixed weapons. Complex soldier
template payloads remain guarded where unsupported. Initial crew assignment, commendations,
and negative-capacity starting weapon removal are covered by the initialization checkpoint below.

Craft purchase/transfer/sale and soldier transfer/dismissal now own cargo refunds,
crew movement and assignment removal. Arrival runs `Craft::checkup` priorities and the
same tick's first refuel operation, including item-fuel consumption/refunds. Subsequent
service progression remains guarded until branch 2. Graph restoration validates crew
references across simultaneous incoming craft/soldier transfers.

Explicit campaign options retain the reference defaults (`storageLimitsEnforced=false`,
`canSellLiveAliens=false`, `autoCombatDefaultSoldier=true`). Unit scenarios check that
purchase storage limits remain active with enforcement disabled, craft transfer capacity
checks depend on the option, and recruit defaults precede template overrides. Transfer
alien containment requires a matching facility even when its occupancy limit is disabled.
Source: `Engine/Options.cpp`, `PurchaseState::increaseByValue`,
`TransferItemsState::increaseByValue`, `SellState` catalog construction and `Soldier`.

Runtime name-pool projections retain content hashes. Cache restoration checks referenced
pool bytes, including custom file extensions; the custom-extension mutation regression
passes. Further action/provider coverage and final cached/fresh corpus closure remain outstanding.

## Logistics interface integration

`--campaign-sdl` now runs a 640x400 indexed logistics client. `I` inspects stored and
incoming inventory; `B`/`S` open a stable purchase/sale quote; `T` selects a destination
before opening a transfer quote. Up/down selects rows, +/- changes quantities (Shift
changes ten), Enter requests confirmation, and Y submits once. Tab changes the selected
base. Escape cancels confirmation, closes the current screen, then exits from the main
view. Space advances one minute; Shift+Space requests one hour, subject to simulation
guards and arrival interruptions. F5 saves and F9 requests confirmation before loading
the supplied save path. A `-` destination disables save/load. There is no automatic
background time advance while an order is being edited.

The read-only stores query does not execute price hooks or capture the save graph.
Missing craft inventory is reported as an unavailable capacity calculation and blocks
dependent actions; unknown soldier assignment state prevents craft sale/transfer.
`CampaignLogisticsClientTests` exercises keyboard purchase confirmation/cancellation,
save/load in transit, exactly-once delivery, and mutation-free stores inspection.

The App resolves English labels from common/layered language files and composed
`extraStrings`; the font loader supports the installation's indexed `FONT_SMALL` images
and uses the asset-independent interface font when no font definition exists. Source
inspection: `Engine/Font.cpp::load`, `Language.cpp::loadFile/loadRule`. The staged 40k
font (8x9) and scientist label (`Adept`) have been rendered and visually inspected using
the public logistics scenario. This is not yet the full cached/fresh playable-corpus
acceptance gate. Main navigation/help text is currently English; broader language
selection and richer layout/notification review remain open integration work.

UI checkpoint validation: 605 solution tests passed with zero skips, including the
available private corpus. The older personnel-rule fixture now supplies the synthetic
`second.nam` file that its name-list replacement test retained. `fontName` and composed
extra strings are immutable runtime projections, independent of optional diagnostic
compatibility data, and the font override survives a compiled-cache hit (revision 7).

## Randomized recruit-template bonuses

Templates now own `previousTransformations`, fixed `transformationBonuses`, and weighted
`randomTransformationBonuses`/`transformationBonusesCount`. Choices are removed after
selection; empty or NUL rule names consume a choice without awarding a bonus. Existing
counts increment, zero weights do not draw, and the loop stops when options are exhausted.
Only the awarded counts are emitted, so a recruit cannot reroll on transit reload.
These payloads cover the previously guarded randomized templates used by 40 soldier
types in the staged 40k/Rosigma content. Other complex template payloads remain guarded.

`capture-strategic-logistics-reference.ps1` now inserts `WeightedOptions::choose/set` and
the `Soldier::load` random-bonus block verbatim into the probe. Parsed map input and an
injected minimum-choice RNG are collaborators; YAML parsing and RNG stream parity are
not claimed by this probe. Five captured count/draw/result traces match the C# path.
Purchase/save/arrival tests preserve both fixed and randomized results. Negative weights
and totals outside the C++ integer RNG call's valid range fail intentionally; possible
counter overflow and excessive generation work block orders before recruitment.
The runtime cache revision is now 8.

## Starting crew and commendations

Starting craft whose combined raw capacities are negative return every mounted launcher
and loaded clip to stores, retaining physical cargo. Automatic assignment runs when the
`randomSoldiers` key exists, even for zero recruits. It considers all small soldiers in
base order, retains the first legal transport as fallback, and prefers an eligible
interceptor with an unfilled pilot requirement. Capacity includes weapon bonuses,
existing soldiers and vehicles; soldier/armor groups and per-group limits are respected.
Large soldiers keep their existing assignment, matching the reference caller.

Random starting recruits receive `STR_MEDAL_ORIGINAL8_NAME` when defined. Commendation
name/noun/level now have gameplay ownership and survive save/reload; other diary fields
remain in the mobile source overlay. Pilot eligibility includes transformation and
commendation bonuses, armor stats, short-width addition and fixed stat minimums.
Vehicle size/space overrides are retained; missing dimensions use the vehicle-unit armor.
Runtime craft constraints, pilot minimum stats, armor stats, soldier bonuses and
commendations are immutable projections; compiler/cache revision is 9.

References inspected: `Mod::newSave`, `Craft::validateAddingSoldier`, capacity/space
and soldier/vehicle count helpers, `CraftWeapon::getClipsLoaded`, `Craft::load`,
`Vehicle::load/save`, `Armor::getTotalSize/getSpaceOccupied`,
`Soldier::getBonuses/prepareStatsWithBonuses/hasAllPilotingRequirements`,
`SoldierDiary::awardOriginalEightCommendation`, `SoldierCommendations::load/save`,
`RuleCommendations::getSoldierBonus`, and `UnitStats::obeyFixedMinimum`.
The public Veteran logistics fixture checks launcher/clip refunds, interceptor-first
assignment, transport capacity, awards and repeated save rewriting. Unit tests check
bonus deduplication/count semantics and short overflow before clamping. The 612-test
solution checkpoint passes, including private saves; final branch acceptance remains open.

## Critical storage sales

With storage enforcement enabled, critical overflow exposes incoming items and craft
cargo/equipment as sale stock. Sales consume base stock, stationed craft cargo/equipment,
then transfers in their existing order. Removing a launcher or loaded clip removes the
whole mounted component and returns unsold parts to base stores. Partial transfer removal
retains identity and hours; exhausted item transfers disappear. Sales must resolve storage
overflow before confirmation can complete. The planned inventory changes are checked on
an isolated state copy before publishing, including refund overflow and capacity checks.

References: `Base::getUsedStores/storesOverfullCritical`, `Craft::getTotalItemCount`,
and `SellState` catalog construction and `btnOkClick` cleanup lambdas. Public compatibility
scenarios cover stationed and incoming craft, clip refunds, partial transfer reduction,
save/reload and exactly-once arrival. Normal-sale regressions pass in the unit suite.
Branch 2 owns mutable readiness stats. Mounted bonus equipment can be removed when the
resulting crew/vehicle loadout remains legal; fuel and shield values clamp to refreshed
maxima and the whole critical sale still publishes atomically.

## Final branch validation

Fresh/cache action and eligibility checks pass for all three staged content families.
Three of 19 private saves can purchase and reload; eight active battles and eight active
research/production states are explicitly blocked. The full solution has 627 passing
tests and no skips. Generation preflight now rejects overflowing name/look weights and
possible empty callsign outcomes before consuming RNG while allowing gender-specific
pools that cannot reach the missing gender. Restored logistics aggregates are bounded
before publication, and unplaced bases cannot open logistics. Craft bonus overflow disables
the purchase row before a mixed recruit/craft order can draw recruits. Bravery bounds
are checked after the reference integer division; initial psi skill does not draw a range.
The status document records measured allocations, UI controls and remaining guards.

See [ADR 0025](decisions/0025-strategic-time-and-mobile-save-ownership.md).

Daily alien-base self-destruction, mission unlinking and pact-cancellation flags are
gameplay-owned. See [cleanup status](strategic-world-alien-base-cleanup-status.md).

Retaliation mission IDs are now gameplay-owned player-base links. Finished
retaliation missions clear matching links before half-hour deletion; loaded
rewrites remove cleared fields while conserving unknown base data. Reference
traces and fresh/cache regressions are recorded in [retaliation cleanup status](strategic-world-retaliation-cleanup-status.md).

Existing crashed UFOs now reveal, count down and expire in reference order,
return followers and release mission live counts. Pure forecasts preserve landing
and tactical boundaries; fresh/cache, reload, resource and 72-hour scenarios are
recorded in [crashed UFO status](strategic-world-crashed-ufo-status.md).

The composed cleanup audit adds an atomic guard for alien bases sharing marker/ID
across deployments. C++ pointer-identity traces and fresh/cache reproductions
show that deletion must not detach another base's missions or followers. See
[cleanup audit](strategic-world-alien-base-cleanup-status.md).

Mission scripts now use `RuleAlienMission::generateRace` when their own race
weights are absent. The reverse mission timeline, empty/future timeline failure,
ordinal weighted selection, explicit override and RNG order are preserved.
The pinned `strategic-alien-base-generation` oracle includes the exact method;
`StrategicMissionRaceFallbackTests` covers fresh/cache and persisted run history.
Site missions, base targeting and strategy region/mission fallback remain guarded.

Ordinary script missions now apply `AlienMission::setRegion`'s single remap;
strategy removal still uses the selected source region. The existing pinned
base-generation probe inserts the exact method. Fresh/cache regressions cover
remapping, chained aliases (one hop), unresolved targets, table history and reload.

Ordinary mission scripts with explicit mission types now target player-base
regions using the reference's uniform starting list entry, circular retry,
duplicate base regions and active-mission pruning. All-busy candidates fail the
label without creating a mission or changing run/table history. Missing region
weights are permitted for guaranteed base targeting; mixed odds still validate
both paths. Candidate work is bounded before mutation. The pinned
`strategic-mission-base-target` probe and fresh/cache tests cover selection,
failure labels, RNG order, resource limits and reload. Implicit strategy fallback,
site scheduling and campaign month composition remain unavailable.

Explicit-region mission scripts can now choose their mission from the persisted
alien strategy table. Weighted order, empty-choice failure, live batch removal,
`useTable: false`, run history and reload are covered against the pinned
`strategic-mission-strategy` probe. Missing/invalid tables and batches that can
remove a later explicitly required table stop before mutation; removal bounds
count one possible removal per command and region. Region-table refresh and
implicit base-target mission selection remain for the next slice.

Strategy region selection and exhausted-table refresh now execute through the
mission scheduler. Refresh rebuilds weighted regions/missions and preserves run
and location histories. Base targeting without mission weights prunes missing
region tables, retains duplicate base regions and then chooses a table mission.
Pinned traces cover refresh and implicit-base pruning; fresh/cache composed
batches and transition reloads verify persistence. Preflight validates current
and possible refreshed selections without RNG or live mutation. Site scheduling,
non-space initialization and campaign monthly composition remain guarded.

Alien-base hunting timers now run after ten-minute fuel and base detection.
Craft eligibility, empty choices, precise generation/timer/work guards and
persistence are covered against pinned reference traces. Runtime craft
`undetectable` is projected with cache revision 32. See
[hunting timer status](strategic-world-alien-base-hunting-status.md).

### Mission initialization, wave and hunting checkpoint (2026-10-03)

Six further 4c slices now own non-site space objective initialization, existing
regional/Earth operation bases and missing-base interruption, no-object objective
waves, ordinary airborne objective waves, linked-base launch origins and delayed
hunting mission creation. Pinned operation/origin/hunting captures verify choices,
RNG order, countdown fallthrough and source links; fresh/cache transition reloads
cover each slice. The final audit adds forecast cleanup guards for surviving UFO
or retaliation references, including within immediate chains.

Forced new-base creation, deployment and special objective effects, general
regional wave geometry, immediate hunting waves, real detection VM providers,
hunter/escort paths and initial/monthly world caller composition remain open.
The branch 4c headless acceptance gate remains open; branch 4d is untouched.
See [mission status](strategic-mission-script-status.md) and
[hunting status](strategic-world-alien-base-hunting-status.md).

### Ordinary mission lifecycle and contact checkpoint (2026-10-03)

Eight further 4c implementation cycles cover operation-base waypoint returns,
ordinary airborne arrivals across objectives, ordinary ground arrivals, takeoffs
with objective score filtering, Supply spawn suppression/constructor defaults,
no-object wave classification, flag-aware airborne spawning, and transient ignored
UFO contacts. Every slice has pinned reference evidence, focused fresh/cache tests,
transition reload coverage, and an audit. Coverage corrections are a separate commit:
site guard tests now select actual site areas, and the undefined Rosigma dummy wave
has an explicit 360-tick classification with its resolved no-effect rule asserted.

All 19 staged saves retain explicit stopping horizons: ten executable at 360 ticks,
one blocked at 125 ticks, eight tactical saves preserved at zero ticks. This horizon
does not establish unrestricted continuation. The ignored-contact set clears on
reload because SavedGame does not serialize it in the reference engine.

The headless gate remains open: forced/new base creation, infiltration/pacts and
special objective effects, deployment/site spawning, regional spawn geometry,
immediate hunting waves, detection VM providers, hunter/escort behavior and actual
initial/monthly world hook composition remain outstanding. Combat, deployment,
event effects and integrated monthly progression keep their branch 5/6 boundaries.
Branch 4d is untouched. See [mission status](strategic-mission-script-status.md).

Validation for this checkpoint: root `dotnet test --no-restore` passes all 1,522
tests, with no failures or skips, including checksum, private corpus and populated
world scenarios. Formatting verification, regenerated code-map validation and
`git diff --check` pass. Captured probes remain available under artifacts; obsolete
probe object files were removed. The pinned C++ checkout is clean.

### Direct mission-site generation batch (2026-10-03)

Branch 4c now owns direct marked-deployment waves, mission-rule/terrain site
selection and player-base targeting, including no-base suppression. The
`strategic-site-spawn` capture pins AlienMission::think/spawnMissionSite/selectXcomBase
and Texture::getRandomDeployment. The batch audit adds reference-backed hourly
site discovery, forecasts its postponements and corrects null-outcome activity
reservations. Idle allocations and deployment-table work bounds are corrected;
fresh/cache reload, score ordering and atomic regressions pass. See
[site generation status](strategic-world-site-spawning-status.md). UFO conversion,
immediate despawn and other remaining 4c world effects are still guarded precisely.
The headless acceptance gate remains open; branch 4d is untouched.

### Mission-site expiry batch (2026-10-03)

Branch 4c now executes suppressed instant despawn choices and safe zero-hour site
expiry. Live UFO followers restore their original waypoint on forced deletion;
save-load destination conversion remains unchanged. Reference probes cover the
caller boundaries and reset methods. Combined counter/ID/activity guards and
fresh/cache reloads cover same-tick creation/deletion. The batch audit fixes
same-day research shifting marker allocations and bounds aggregate despawn work.
Known mission-unlock research and reachable instant-event payloads still stop
before mutation. The headless gate remains open; branch 4d is untouched.

Final root validation passes 1,630 tests with no failures or skips; formatting,
fixture checksums, private corpus, allocation budgets, code-map and diff checks
pass. The pinned C++ checkout remains clean.

### Selected objective UFO site conversion (2026-10-03)

World owns selected objective UFO conversion, immediate site revelation and
next-tick UFO cleanup. Fresh/cache captured scenarios cover selected-area and
landing-position sites. Remaining compound ticks, custom deployment, dormant
links and follower transfer are guarded precisely. See
[conversion status](strategic-world-ufo-site-conversion-status.md).

World also preserves optional custom deployment metadata from effective UFO
race stats, reusing the existing save fields. Primary site deployment and duration
remain controlled by the mission/terrain selection.

World now creates dormant UFO/site respawn links, preserves mission live counts
and resumes the prepared trajectory through the existing site-expiry lift
capability. Creation and expiry reloads cover those transitions. Combined
half-hour conversion and craft follower transfer remain guarded.

The combined conversion audit fixes suppressed respawn forecasts so unrelated
craft landing guards do not block a reference-terminal tick. Additional captures
and tests verify missing sites/links, shield recharge and later-UFO movement.

Final root validation passes 1,644 tests with no failures or skips, including
checksums, private corpus and allocation checks. Formatting, code-map and diff
verification pass. The C++ checkout remains clean; the headless gate is open.

World now owns combined-tick UFO site conversion, using the gameplay custom
counter capability to project daily research and preceding site transitions.
Positive/zero site timers, saved-target collisions and ID exhaustion retain
reference ordering and atomic guards. A shared marker decrease after direct
births remains guarded precisely pending correlated-ID forecasting.

Craft followers now transfer to a converted site when they carry any assigned
soldier or vehicle instance, independently of AllowLanding/equipment checks.
Empty followers return home only for dormant conversions. Fuel return precedes
transfer, and terminal conversions still skip the later craft movement pass.
The captured follower rows cover empty, vehicle and soldier totals; fresh/cache
reload tests check stable site references and effective destination speed.
Respawn conversions forecast every possible new-site position using the existing
spherical area bound, stopping atomically before a possible branch 5 landing.
Nonlanding craft transfer then use the existing arrival/recall behavior.

Damaged selected-site arrivals now proceed because AlienMission::ufoReachedWaypoint
replaces Ufo::setAltitude's transient crash/landed result before any later
Geoscape callback. The expanded probe inserts setAltitude, isCrashed and
isDestroyed verbatim. Forty captured combinations cover the half-damage boundary,
destruction threshold, unmanned rules, kamikaze behavior and dormancy. Fresh/cache
tests preserve damage and stable respawn links across reload. Ordinary damaged
ground arrivals retain their atomic guard; this does not enable crash creation,
interception or tactical deployment.

The batch audit reproduced premature landing guards both before and after a
terminal UFO arrival. Conversion validation now stops at the actual UFO-pass
boundary, while follower transfer validates the reachable conversions separately
from craft arrival. A later terminal arrival still permits earlier transfers but
skips all craft landings. Activity forecasts honor ordinary terminal arrivals too.
Regressions verify unchanged skipped craft positions, reload, and no reservation
of skipped conversion scores or markers. The previous crash-guard fixture now
selects an ordinary arrival, retaining its strict atomic assertion.

Root validation passes all 1,662 tests with no failures or skips, including
fixture checksums, all private-corpus classifications and allocation budgets.
The headless 4c gate remains open; branch 4d is untouched.

Hunter target selection now has pinned executable evidence and a gameplay-owned
operation for attraction, saved-order selection and original-target retention.
See [hunting status](strategic-world-hunting-status.md). Live retargeting and
movement are the next two slices; the 4c gate remains open.

The retarget slice adds reference destination backup, hunt speed, escort speed and
waypoint restoration operations. Captured transitions retain the original waypoint
across hunt-to-escort changes and leave speed unchanged on restoration. Explicit
selected speed controls the five-second/one-minute fast path; ten-minute retargeting
is always due. Mod::hunterKillerFastRetarget defaults true and survives fresh/cache
projection. The redundant-HK-alert option defaults true, honors fixed user options
and persists through the save adapter. Compiler revision 33 invalidates stale caches.
Eleven focused tests pass. The next slice connects live retargeting and movement.

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
Deletion scans are bounded before nested work. Future hunter wave creation remains
guarded until spawn/retarget forecasts compose; this slice enables loaded actors.
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

- 4c creation-time escorts: live single airborne waves attach to the first saved
  same-mission ordinary UFO, retain requests when no leader exists, and repair
  deletion through shared target cleanup. Fresh/cache, persistence, malformed
  effective-speed and craft-retarget boundary tests pass. Immediate-wave leaders
  and grounded escort forecasts remain the next boundaries.

- 4c immediate escort composition: same-tick earlier and later ordinary leaders
  now attach in reference list order with the distinct creation/retarget origin
  behavior. Positive unique UFO references permit an unassigned contact marker.
  Fresh/cache persistence and aggregate work limits are verified. Loaded idle
  escort/newborn-leader pursuit forecasts retain a precise boundary.
