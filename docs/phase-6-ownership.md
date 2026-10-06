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
| Mission/arc/event scheduling, broader world lifecycles, pursuit and scripts | Branch 4c | `GeoscapeState`, `AlienStrategy`, mission/site/base/event classes and world script callers | Headless mission/arc/adhoc/event projections, initial/daily/isolated monthly scheduling, mission objectives/waves, sites/alien-base lifecycles, event history/countdowns, shields/hunting/escorts and craft pursuit execute in registered capabilities with precise transition guards. Initial entry and the composed 72-hour fixture are validated; integrated campaign months and due event payloads retain the branch 6 boundary. Detection VM providers and bounded forecasts are being closed incrementally; current damage classification/conversion and alias/scalar cases pass fresh/cache/reload. Final branch-wide acceptance audit remains open. Globe/UI belongs to 4d and has not started. |
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

- 4c escort batch audit: confirmed and fixed scoring preflight using the wrong
  origin after creation relocation. Region/country boundary regressions cover
  saved and immediate-wave leaders; terminal leader fallthrough/reload is covered.

- Escort cycle final gate: all 1,745 root tests pass, including private/endurance
  and allocation/checksum coverage. Formatting, code-map and diff checks pass.
  The read-only reference checkout is clean;
  remaining 4c ownership is explicit and the 4d boundary is unchanged.

- 4c grounded escorts: positive long-timer births initialize without craft-hunt
  forecasts because landed UFOs miss retargeting. Ground status, contact order,
  stationary timers, shields, trajectory takeoff speed and reload are verified.

- 4c creation-tick takeoff: timer-one grounded hunter/escort births now reach
  the five-second lift handler after skipping retarget. Site-before-lift scoring,
  terminal-arrival fallthrough, save/reload and next-tick combat refusal pass.

- 4c saved idle escorts/newborn leaders: enabled the bounded composition with
  no craft or dependent UFO pursuit and no cancelled waypoint arrival. Ordered
  attachment, distinct origin semantics, fresh/cache reload and atomic specific
  boundaries pass; broader changed pursuit forecasts remain 4c-owned.

- Grounded/saved-escort batch audit: reviewed creation, landed/takeoff scoring,
  retarget ordering, terminal fallthrough, follower repair and aggregate work
  limits against the pinned C++ methods. No confirmed defect was found. All
  1,761 root tests pass with zero failures/skips, including private corpus,
  endurance, checksums and allocation checks. Formatting verification, code-map
  and diff checks pass. The reference checkout is clean and obsolete probe
  object output was removed. Branch 4c remains open; 4d has not started.

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

- 4c base-objective cycle: pinned AlienMission::think final-base and landing-base
  blocks, chooseAlienBaseType, Texture::getRandomDeployment and MissionArea
  defaults. The 432 search and 384 landing rows cover ordering, dynamic selection,
  technical regions, fake-water/fallback and conversion precedence. Final-wave
  and accepted landing creation now execute through registered world handlers,
  with persistent birth identity, score/marker/population/work reservations and
  fresh/cache transition reloads. Operation-base creation shares the birth helper.
- Batch audit confirmed omitted counter-forecast scans, combined patrol-discovery
  work and creation-tick newborn-base hunting capacity. Deterministic regressions
  fail before the fixes. The hunting reproduction overflowed after mutation;
  possible creation-tick hunting and invalid timer bounds now stop before birth.
  No-candidate birth/timer fallthrough remains executable. A legacy missing
  fallback test now asserts the precise deployment failure. Infiltration/pacts,
  broader hunting forecasts, detection VM and actual campaign hook composition
  remain open; branches 4d/5/6 retain their agreed boundaries.

- Shared birth validation additionally rejects negative deployment hunting timers
  before operation or landing creation, preventing unreloadable owned state.
  Fresh/cache reproductions fail before the fix. Whitespace-only markers are
  handled consistently with existing site creation.

- The batch ID audit reproduced hourly craft production shifting a shared base
  marker into an existing ID. Final-wave and landing preflights now guard this
  precise composition before mutation, with bounded scans and fresh/cache tests.
  Reference callers are GeoscapeState::timeAdvance and Production::step;
  production-aware base marker forecasts remain open 4c work.

- Cycle validation: all 1,841 root tests pass with no skips/failures, including
  checksums and private corpus. Formatting, code-map and diff checks pass; the
  pinned reference is clean. Three implementation commits and a separate audit
  repair commit preserve the 4c/4d and branch 5/6 boundaries.

- Five-cycle batch 1, slice 1: Country::canBeInfiltrated/setNewPact evidence and
  both Mod infiltration options are pinned. Defaults are false; immutable runtime
  projections and cache revision 35 preserve loaded options. All eight country
  flag combinations and fresh/cache options are covered. Live completion follows.

- Five-cycle batch 1, slice 2: the verbatim AlienMission::think infiltration
  completion block supplies 432 regional-search traces. Unlike base objectives,
  infiltration respects technical-region containment for country labels and
  search points. The shared selector now exposes that distinction; deployment,
  coordinate, fake-water and suppression draws remain reference ordered.

- Five-cycle batch 1, slice 3: non-endless regional infiltration now creates
  a pending pact and independently identified base after a successful search.
  It retries suppressed countries, observes live pending-pact flags across
  completions, supports same-month recapture, and does not score or execute
  signed-pact events. Marker/population/hunting/work guards precede mutation.
  Close-country search, shuffled order and endless loops retain precise guards.

- Batch 1 audit reproduced unreserved suppressed-country retries: 4,002 eligible
  countries could each run a full 100-attempt search after one birth reservation.
  Preflight now charges all possible searches, geometry and deployment selection
  before mutation; the unchanged-state regression fails before the fix.

- Batch 1 validation: root dotnet test --no-restore passes all 1,853 tests with
  no failures/skips, including checksums, corpus and world scenarios. Code-map,
  diff and formatting verification pass. Reference remains read-only and clean.

- Five-cycle batch 2, slice 1: close-country infiltration search matches 432
  pinned traces. Deployment/preference draws precede coordinates and remain
  fixed through retries; wrapping areas split at zero/359.99 degrees with the
  reference 50% draw. Country containment replaces regional containment. Empty
  country areas fail intentionally; live country-border births follow next.

- Five-cycle batch 2, slice 2: country-border infiltration now executes with
  live pending-pact/base persistence. It ignores mission spawnZone, selects from
  dummy texture 0, and reserves all possible country areas including wrapped
  halves and suppressed retries. Empty areas fail before mutation. Fresh/cache
  fixtures prove actual country placement and the zero-weight singleton lookup.

- Five-cycle batch 2, slice 3: RNG::shuffle is captured verbatim and used for
  country selection without changing saved country order. Shuffling consumes
  the reference draws even when no country can be infiltrated. Fresh/cache
  live search, no-candidate RNG, chosen-country identity and reloads pass.

- Batch 2 audit: close-country selection, wrapped longitude bounds, dummy-texture
  projection, country eligibility and shuffled RNG order were checked together.
  No confirmed implementation issue was found. All 1,862 root tests pass with
  no failures/skips; formatting, code-map and diff checks pass. Reference is clean.

- Five-cycle batch 3, slice 1: endless-infiltration restart/countdown is pinned
  separately from country effects. Short, rounded and long timers preserve the
  original-timer recursion rule and one reference countdown draw per restart.
  Pure runtime restart projection matches six steps of each fresh/cache trace.

- Five-cycle batch 3, slice 2: endless missions now restart after pact/base
  completion and then draw the first-wave countdown. Finite zero-timer followups
  preserve reference order; fully zero-timer loops stop before tick/RNG mutation
  at the existing 4,096-step bound. Live pending-pact history, no-candidate repeats,
  random order and transition reloads are covered fresh/cache.

- Five-cycle batch 3, slice 3: retaliation target selection is pinned and
  projected into a pure ordered selector. Vanilla retaliation uses the first
  discovered regional base; multi-UFO mode chooses uniformly among discoveries.
  Skip-scouting uses the first regional base regardless of discovery, while
  instant/site-wave selection considers all regional bases. All 96 traces pass.

- Batch 3 audit reproduced legacy UFO wave inference changing across an endless
  restart. A two-wave mission with a -1 UFO wave switched from ordinary landing
  to site conversion after preflight and overflowed the site ID after mutation.
  Multi-wave endless completion now checks these saved actors with a precise
  forecast guard; single-wave and explicitly numbered UFOs retain their paths.
  Fresh/cache unchanged-state regressions fail before the fix.

- Batch 3 validation: all 1,872 root tests pass with no failures/skips. The
  restart/pact and target-selection slices were audited against the pinned
  caller order; the legacy-wave finding is fixed in a separate audit commit.
  Formatting, code-map and diff checks pass; branch 4d remains untouched.

- Five-cycle batch 4, slice 1: no-target normal retaliation now falls through
  to the existing ordinary scouting spawn/no-object paths. The complete pinned
  retaliation spawn block provides target, source and interruption evidence;
  live fresh/cache UFO/no-object transitions and reloads pass. Targeted assaults,
  instant termination and marked deployment waves retain precise boundaries.

- Five-cycle batch 4, slice 2: instant retaliation without a regional base now
  interrupts, advances the current wave/countdown once and skips recursive
  followups. Live UFOs keep the interrupted mission until safe cleanup. Fresh/cache
  reloads preserve counters/RNG and dangling non-retaliation links stop atomically.
  The pinned complete spawn block establishes the no-target interruption boundary.

- Five-cycle batch 4, slice 3: targeted normal retaliation now creates airborne
  assault ships through the existing score, ID and movement forecasts. The pinned
  AlienMission::spawnUfo block supplies default-constructor, override, ordered target
  and linked/regional origin evidence. Fresh/cache live traces and reloads preserve
  assault identity and omit hunter/escort rolls. Instant base-defense arrival,
  ground starts and regional ground origins retain explicit capability boundaries.

- Batch 4 audit: no additional semantic issue was confirmed across scouting,
  interruption and safe assault births. All 1,878 root tests pass (no skips),
  including oracle checksums; source/RNG ordering and fresh/cache rewrite tests pass.
  Formatting verification identified initializer whitespace, corrected separately.
  The reference checkout remains read-only and branch 4d is untouched.

- Five-cycle batch 5, slice 1: retaliation assault regional origins now use
  the original wave altitude to choose bounded land/fake-water selection while
  flight altitude/speed remain from the assault trajectory. Linked Earth bases
  skip coordinate and land draws. The pinned spawn oracle's ground variants pass
  fresh/cache alongside ID forecasts, constructor/RNG checks and save reloads.

- Five-cycle batch 5, slice 2: no-target retaliation now falls through from
  spawnUfo to marked deployment-site creation at the exact think caller position.
  Normal missions execute finite immediate site chains; interrupted instant missions
  create at most the current wave's site. XCOM-base site selection honors discovered
  targets for normal retaliation, independently of the assault skip-scouting option.
  Pinned spawn/site-condition oracles, fresh/cache, ID guards and rewrites cover both.

- Five-cycle batch 5, slice 3: daily and hunting-generated no-object retaliation
  now forecast their actual one-hop mapped region. Safe normal missions finish
  their finite chain; instant missions with no target interrupt after one wave.
  Hunting retains completed/interrupted missions until the following half-hour
  cleanup; daily generation cleans them during its existing fallthrough. Targeted
  immediate births and deployment payloads remain guarded in these callers.
  Pinned generation/hunting/spawn evidence, fresh/cache transitions and reloads pass.

- Batch 5 audit reproduced a ten-minute discovery/hunting ordering failure:
  DetectXCOMBase marked a base before a generated normal retaliation's immediate
  think, invalidating no-object preflight after earlier mutations. Bounded checks
  now stop before a reachable positive-chance discovery could change that outcome.
  Fresh/cache unchanged-state regressions fail before the fix; out-of-range scanners
  remain executable. The audit also accounts for repeated assault classification
  work and avoids allocating candidate lists for boolean classification.

Final five-cycle validation: all 1,886 root tests pass without failures or skips,
including oracle checksums and private-corpus cases. Formatting verification,
regenerated code-map checks and git diff --check pass. Useful generated probes
remain available; the obsolete retaliation-spawn object file was removed. The
pinned C++ checkout remains clean. This completes the requested fifteen slices
and five audits, while the revised 4c headless completion gate remains open.

- Four-cycle continuation, batch 1 slice 1: bounded half-hour counter forecasts
  now precede UFO arrival preflight. Legacy wave inference observes post-think
  counters without consuming scheduling RNG or normalizing the saved wave number.
  Pinned AlienMission::think/ufoReachedWaypoint fragments and fresh/cache reloads
  verify ordinary arrival when the saved counter previously implied a site conversion.

- Four-cycle continuation, batch 1 slice 2: endless infiltration no longer
  blocks merely because a saved UFO uses legacy wave inference. Restart counters
  flow into site conversion, identity reservations and UFO cleanup forecasts.
  The earlier overflow reproduction now reaches the precise exhausted-site guard;
  valid fresh/cache cases convert, reload and release live counts without changing
  the persisted -1 wave number. Reference: think restart and arrival fallback.

- Four-cycle continuation, batch 1 slice 3: landing-marker reservations now use
  the same post-think mission counter projection as site conversion and pursuit.
  Detected endless legacy conversions no longer reserve phantom landing markers;
  ordinary legacy landings reserve IDs even when half-hour detection reveals them.
  Fresh/cache capacity failures leave the tick unchanged; successful conversions
  and ordinary arrivals retain save/reload coverage. Reference: GeoscapeState
  half-hour detection before five-second arrivals and AlienMission arrival fallback.

- Batch 1 audit: root validation reproduced an error-contract regression in the
  early counter forecast. Excessive finite wave chains retain their intentional
  InvalidDataException; endless all-zero cycles retain their explicit unavailable
  reason. Both stop before mutation/RNG. The unchanged existing malformed-input
  regression verifies this separately from the new legacy arrival coverage.

- Batch 2 slice 1: registered production-owned counter forecasts bound prepaid
  craft completions and resource-dependent followups before world allocations.
  The allocator's live/transfer identity floor is preserved; fallback staffing,
  craft consumption and excessive work/population retain precise guards.
  Reference: Production::step/getAmountProduced and existing pinned production
  fixture. Fresh/cache proofs check bounds, unchanged RNG/state and reload.

- Batch 2 slice 2: final-wave base births now reserve shared markers after hourly
  craft production through the production capability. Existing future-ID collisions
  stop the whole tick; valid craft/base births allocate consecutively and survive
  reload without duplicate effects. Reference: production preceding half-hour
  AlienMission::think and the pinned base-objective fixture. Landing integration
  remains the next slice.

- Batch 2 slice 3: landing-base shared IDs now project daily research, hourly
  production and preceding site changes in actual handler order. Production no
  longer causes a blanket landing guard. Decreases that could reuse a newborn
  craft's ID remain precisely guarded; scans are charged to bounded creation work.
  Fresh/cache final-wave and landing cases verify capacity refusal, consecutive
  identities, reload and no duplicate births.

- Batch 2 audit: production progress, shared allocator floors, handler ordering,
  resource-dependent bounds, collision refusal and persistence were reviewed
  together; no confirmed behavioral defect was found.
- Batch 3 slice 1: fixed-position newborn bases can start delayed hunting missions
  in the creation tick. Their possible mission births share capacity/ID checks
  with existing-base generation. Reference: GeoscapeState::baseHunting immediate
  think after final-wave births; pinned hunting-generation and objective fixtures.
  Fresh/cache cases verify exhausted IDs, start timers, links and reload.

- Batch 3 slice 2: newborn hunting forecasts cover regional rectangles by
  partitioning at all saved-region boundaries, preserving locateRegion order and
  hemisphere endpoint rules. Every possible region must be covered; holes and
  excessive partition work fail intentionally. All reachable remapped regions
  are checked without RNG. Fresh/cache regional births start delayed missions
  and reload through the same lifecycle path as point births.

- Batch 3 slice 3: newborn-base hunting can execute bounded immediate no-object
  chains, reusing the generated mission operation and mapped-region checks.
  Object births and objective consequences retain their existing precise limits.
  Supply UFO waves explicitly require a live newborn-source forecast; a synthetic
  preflight source must not be mistaken for a missing base and suppress the wave.
  Pinned chain/hunting fixtures and fresh/cache transition reloads verify counters.

- Batch 3 audit: confirmed hostile partition dimensions could overflow the work
  product. Division-based capacity checks now run before allocating sample arrays;
  arithmetic-limit regressions cover both overflow dimensions and exact thresholds.
  Newborn source identity, generated mission limits, first-match region ordering,
  no-object execution and creation-tick timer fallthrough were audited together.

- Batch 4 slice 1: shared world marker forecasts now include fallback staffing,
  with bounds for free engineers and possible releases from earlier projects.
  Optional completions retain the original counter as the lower bound rather than
  assuming a craft is born. Progress/population and facility scans are bounded.
  Reference: Production::step fallback staffing. Fresh/cache direct/landing births,
  fallback completion and reload verify the removed blanket staffing guard.

- Batch 4 slice 2: craft-material consumption no longer causes a blanket shared
  counter guard. ID ranges retain the saved counter as the guaranteed floor when
  earlier production may remove the highest craft; the saved craft/transfer floor
  remains an upper bound. Reference: Production::startItem required-craft removal
  before later craft allocation. Fresh/cache production order and reload prove the
  actual counter stays inside the mutation-free bounds.

- Batch 4 slice 3: direct mission sites and UFO conversions now forecast shared
  craft counters after hourly production. Existing craft/transfer identity
  collisions and decreased counters that could reuse newborn IDs stop before
  mutation. A shared bounded scan accounts for fallback facilities across base
  and site forecasts. Pinned site/production fixtures and fresh/cache combined
  births verify correct IDs, preflight refusal and transition reloads.

- Batch 4 audit reproduced missing fallback upper bounds for engineers arriving
  before production and workshops completed by the preceding daily handler.
  Fresh/cache regressions fail before the fix; forecasts now include both, and
  combined final-wave/landing collisions stop before transfers or production
  mutate the tick. Successful arrivals retain reload coverage. The audit also
  charges added craft identity scans between hourly ticks and repeated hourly
  transfer/floor scans to the world work budget.

Final four-cycle validation: all 1,916 root tests pass with zero failures/skips,
including checksums and private-corpus tests. Formatting verification,
regenerated code-map validation and git diff --check pass. Three confirmed audit
findings were fixed separately; all twelve requested slices and four audits are
complete. Useful native captures remain available; the obsolete counter-probe
object file was removed. The reference remains clean at the pinned commit.
The revised 4c headless acceptance gate remains open; 4d is untouched.

## Detection VM cycle (2026-10-04)

- Slice 1 pins exact Base::detect/Craft::detect workers and roll order with a
  verbatim-method oracle and immutable scalar caller projections.
- Slice 2 executes bounded scalar detection programs and global before/after
  events at the live half-hour call sites, with precise failure-safety checks.
- Slice 3 supplies read-only UFO, craft and campaign/time providers with native
  getter evidence, fresh/cache execution and transition reloads.
- Batch audit confirmed a closure allocation regression and missing aggregate
  script-work bounds. Both are repaired separately; existing performance limits
  are retained. Damage percentage, script values, remaining providers and loop
  forecasts still require subsequent slices. The headless gate stays open;
  branch 4d remains untouched.

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

## Effective craft detection statistics (2026-10-04)

All nineteen Craft.Stats getters now read the raw craft rule plus every installed
weapon bonus, including disabled weapons and ignoring empty slots. Evidence is
Craft.cpp::getCraftStats/load/ScriptRegister and RuleCraft.h::operator+= at the
pinned reference; the existing strategic-detection-scripts stat fixture pins all
field bindings and signed values. Fresh/cache execution and reload tests exercise
all fields. Consumed sums are checked before the tick; host work includes weapon
scans. Caller forecast precision and craft condition percentages follow in this
cycle. The 4c gate remains open and 4d is untouched.

## Craft condition detection readers (2026-10-04)

Craft damage/shield maxima and percentages now execute through the VM with exact
Craft.cpp arithmetic: floating-point floor for damage and integer division with
zero-capacity handling for shields. A verbatim native capture, fresh/cache script
tests, installed disabled-weapon bonuses and reloads cover the six condition
getters and null defaults. Invalid percentage domains stop before mutation.
Grounded craft without an auto-patrol order are excluded from numeric preflight;
same-tick relaunches and due arrivals remain included. All 42 focused binding
tests pass. Caller-specific script/work forecasts are the next slice.

## Caller-specific detection preflight (2026-10-04)

Script availability, consumed-stat validation and work accounting now use possible
craft callers rather than every saved craft. GeoscapeState::updateActiveCrafts,
time30Minutes and ufoDetection establish the boundary: airborne craft are joined
by existing auto-patrol orders that earlier servicing/fuel delivery or due craft
transfers may relaunch. Newly produced craft have no such order. Grounded craft
without orders and later transfers no longer block unsupported craft scripts.
Existing and newborn UFOs share the same before-tick checks. Fresh/cache fixtures
cover grounded/airborne, servicing, arriving/delayed and newborn transitions.
The three slices are ready for their batch audit; the 4c gate remains open.

## Effective craft detection cycle audit (2026-10-04)

The batch audit corrected a newborn test's overlapping geometry by using the
existing fixed spawn trajectory. It also reproduced a resource-ordering issue:
consumed craft fields were scanned before reserving their contact/host work.
Preflight now reserves that budget first; a fresh/cache 32-craft regression proves
the limit blocks before field scans, state or RNG mutation.

All 2,046 root tests pass with zero failures/skips, including private corpus,
fixture checksums and existing allocation bounds. Effective craft stats,
condition getters and caller-specific forecasts are covered across reloads.
Initial/monthly caller composition, remaining VM providers/forecasts and final
composed acceptance remain open. Branch 4d is untouched.

Formatting verification, code-map validation and diff checks also pass. The native
reference remains clean at the pinned revision. Useful capture outputs are
retained; the obsolete task-owned object file was removed.

## Unit rule lookup in detection scripts (2026-10-04)

Runtime rules now project a generation-scoped Unit family with immutable identity.
Mod.cpp::getUnit/getUnitScript and Unit.cpp::getTypeScript/ScriptRegister are pinned
by a verbatim native lookup/type trace. STR_NONE remains an ordinary Unit ID;
missing/empty names and null Mod receivers return null, with empty text for null
Unit receivers. Four fresh/cache base/craft VM tests cover reloads and foreign
generation rejection. Compiler/cache revision is 43. Linked Unit metadata is the
next slice; 4c remains open and 4d is untouched.

## Linked Unit metadata (2026-10-04)

Unit spawn, live-alien and civilian-recovery readers now use linked runtime
handles. A verbatim Unit.cpp::afterLoad linkage capture pins default live-item
lookup, explicit null/empty names, engineer/scientist exclusions and soldier-first
recovery precedence; exact getters feed fresh/cache base/craft VM and reload tests.
Unit nullable names now retain the reference NUL sentinel, so a literal STR_NULL
item remains a real named relationship. Missing explicit links fail during content
loading; default live items remain optional. Compiler/cache revision is 44.
All eight focused Unit detection tests pass. The 4c gate remains open.

## Mod smoke reduction in detection scripts (2026-10-04)

Mod.cpp::getSmokeReduction now executes with its in-place signed integer multiply
and divide-by-60 semantics. A verbatim native capture covers positive/negative,
zero and extreme density/view values, including multiplication overflow.
Fresh/cache base/craft scripts and reloads cover the reader. Because this static
wrapper dereferences Mod, preflight permits the immutable actual Mod caller input;
nullable aliases remain guarded pending data-flow proofs. The receiver proof is
computed once per program. Existing unsupported-provider tests now exercise
still-unavailable resource-offset bindings. All 131 focused detection tests pass.
The three slices are ready for batch audit. The 4c gate remains open; 4d is untouched.

## Unit lookup/linkage and smoke cycle audit (2026-10-04)

The batch audit found no additional confirmed runtime defect. New malformed-name
tests verify that literal STR_NULL and other explicit Unit relationships must
resolve, while default/null NUL names and explicit empty names remain distinct.
Linked Unit readers, null pointers, soldier-first recovery and foreign-generation
handles are covered by fresh/cache VM tests and reloads. Smoke arithmetic matches
the native trace; nullable Mod aliases retain an explicit forecast boundary.

All 2,065 root tests pass with zero failures/skips, including private corpus,
checksums and existing allocation gates. Compiler/cache revision is 44. Initial
and monthly caller composition, remaining detection bindings/data-flow forecasts,
generated-object forecasts and final composed acceptance remain open. Branch 4d
is untouched.

Formatting verification, code-map validation and diff checks pass. The reference
checkout remains clean at the pinned revision. The useful native probe executable,
generated source and JSON trace are retained; its obsolete object was removed.

## Inventory rule lookup in detection scripts (2026-10-04)

RuleInventory.cpp::getId and Mod.cpp::getInventory/getInventoryScript now back
an immutable, generation-scoped runtime Inventory family. The pinned native
capture covers missing/empty IDs, null Mod and the literal STR_NONE ID; fresh/cache
base/craft VM tests cover lookup identity and save/reload around detection.
Inventory movement remains guarded. Compiler/cache revision is 45.
The 4c gate remains open; 4d is untouched.

## Inventory type and hand readers (2026-10-04)

RuleInventory::getType/isRightHand/isLeftHand now execute in both world detection
callers. Raw type integers and ID-derived right/left hand flags retain reference
defaults; null Inventory receivers return zero. Native metadata traces and
fresh/cache base/craft VM tests cover every conventional slot and reloads.
Inventory movement remains guarded. The 4c gate remains open.

## Conventional inventory accessors and tick safety (2026-10-04)

Mod.h's five required inventory getters now return the canonical runtime rules
in detection scripts. Native lookup throws on a missing required role; gameplay
preflight reports the exact missing ID before mutation, for rule and global
scripts. Proven always-null local Mod receivers retain the reference member
binding's null result even when roles are missing. Other nullable receiver
flows remain conservatively guarded when a role is absent.
All 17 focused inventory tests pass, with fresh/cache and transition reloads.
Inventory movement remains unavailable pending safe target/cost forecasts.
The three inventory slices are ready for batch audit; 4c remains open.

## Inventory detection cycle audit (2026-10-04)

The three inventory slices match pinned RuleInventory.cpp, Mod.cpp/Mod.h and
ScriptBind.h member-wrapper behavior. The audit found no additional runtime
defect. Added fresh/cache regressions prove hand flags depend on ID rather than
type, non-null Mod aliases cannot bypass missing-role guards, and inventory
movement retains its precise unsupported-binding boundary. Each VM call clears
locals; the null-receiver proof rejects aliases and host-written registers.

All 2,087 root tests pass with zero failures/skips, including private corpus,
fixture checksums and allocation gates. An earlier run reported a 1,064-byte
polygon-fill allocation difference; the unchanged isolated test and repeated
root run both pass. No cause was established and no assertion was weakened.
Compiler/cache revision is 45. The obsolete native object was removed; useful
generated source, executable and trace remain. The pinned C++ tree is clean.
The 4c gate remains open; initial/monthly caller composition, remaining detection
providers/forecasts and composed acceptance still need work. 4d is untouched.

Formatting verification, code-map validation and diff checks pass.

## Item accuracy detection readers (2026-10-04)

RuleItem.cpp's eight raw accuracy readers now use an immutable runtime projection
of loaded action accuracy and psi/throw scalar fields. The native trace pins
defaults, negative values and signed extremes; all four fresh/cache base/craft
VM tests pass with null readers and transition reloads. No tactical calculation
is enabled. Compiler/cache revision is 46; the 4c gate remains open.

## Raw item power, armor and waypoint readers (2026-10-04)

RuleItem::getPower/getMeleePower/getArmor/getWaypoints now return immutable
runtime values without tactical modifiers. Native default/signed-extreme
fixtures, null pointers and fresh/cache base/craft reload tests pass (eight
focused cases). Damage-type execution and combat remain guarded.
The 4c gate remains open.

## Item handling flags and category membership (2026-10-04)

RuleItem handling/water flags and hasCategory now execute in detection scripts.
Runtime categories preserve Mod.cpp's ordered, single-pass replacement semantics:
forward chains, backward links and cycles match the verbatim native trace.
Replacement ordering uses UTF-8 bytes, including a supplementary-Unicode case
that differs from UTF-16 ordering. Replacements are resolved once in reverse
order, and immutable membership sets avoid contact-time category scans.
All 16 focused item tests pass with fresh/cache base/craft execution, null
receivers and reloads. Combat/damage readers remain precisely guarded.
The three item slices are ready for batch audit; the 4c gate remains open.

## Item detection metadata cycle audit (2026-10-04)

The audit confirmed an obsolete guard test still expected getPower to be
unsupported. It now exercises the still-unavailable isSingleTarget damage reader,
retaining both fresh/cache before-mutation assertions. No additional runtime
defect was found. Raw action/scalar reads, handling flags, null defaults and
generation-scoped item identity match RuleItem.cpp/ScriptBind.h. Category lookup
matches Mod.cpp's once-per-key replacement pass, including cycles, backward links
and UTF-8 ordering; precomputed immutable membership avoids per-contact scans.

All 2,103 root tests pass with zero failures/skips, including private corpus,
fixture checksums and allocation gates. Compiler/cache revision is 46. Generated
source, native executable and trace remain useful; the obsolete object was
removed. The reference checkout remains clean. Initial/monthly caller composition,
remaining detection providers/forecasts and composed acceptance still keep the
4c headless gate open. Branch 4d is untouched.

Formatting verification, code-map validation and diff checks pass.


Branch 4c scheduling composition: pinned caller-order and initial-entry evidence in strategic-world-composition; dependent eligibility forecast now owns research grants/disables and variable/marker increases. Composed execution and initial entry remain pending; branch 4d and integrated month progression remain deferred.


Branch 4c composed scheduling: registered ScheduleWorldScripts runs arcs, missions, events and the final alien-base monthly phase in reference order, using score/funds computed once. Whole-call preflight forecasts dependent research and variable/marker changes before RNG or mutation; tests cover fresh/cache/reload, shared run limits, fixed score, disabling research, missing event storage and final-base failure. Newly created bases with upgrades/evolution still have a specific composition guard pending their forecast; existing isolated base processing remains supported. Integrated month progression remains branch 6 guarded. Initial entry is the next slice.


Branch 4c initial entry: public registered EnterCampaignWorld matches GeoscapeState::init after first-base placement/name. It preflights all scheduling and initial accounting, advances -1 to month zero and adds one day, then schedules before charging full base maintenance minus headquarters personnel. It is once-only across save/reload and leaves time and expenditure histories unchanged. Calls before placement or after initialization are no-ops. Legacy orphan UFO mission/trajectory state and exhausted day/accounting ranges stop before mutation. Branch 4d will invoke this headless entry API; automatic integrated monthly progression remains guarded for branch 6.


Branch 4c scheduling/entry batch audit: reproduced and fixed a prospective operation-base marker collision with an existing monthly upgrade. Final-base preflight now reserves newborn identities and new mission-link work before arcs/events/RNG. Removed the invalid nonmonthly flag from the monthly seam: reference nonmonthly execution is adhoc and remains in its existing capability. Initial legacy UFO validation now uses a mission-ID set, and successful initial entry expires logistics quotes; a stale-quote regression failed before the fix. No month/event-payload/combat or UI guard was removed.


Scheduling/entry cycle verification: root dotnet test passes 2,123 tests with no failures or skips, including fixture checksums and private corpus/allocation cases. Confirmed identity and stale-quote regressions pass. Reference checkout remains clean at 4df3a5e. Generated probe object removed; source/executable/captured output retained for reproducibility. Code-map and diff checks pass.


Branch 4c newborn monthly processing: removed the composed caller's newborn upgrade/evolution guard. Mission preflight collects bounded deployment/race possibilities and marker ranges. Monthly preflight validates all reachable newborn upgrade/evolution candidates, marker collisions and link work before any phase changes state. Captured age-zero month-three native upgrades, race override/evolution, loop failure and marker changes in strategic-world-newborn-monthly; fresh/cache and transition reload comparisons pass. Native loop failure partially mutates; port intentionally stops the whole caller first. Month/event-payload/combat guards remain.


Branch 4c registered monthly hook: the handler now runs after calendar month advancement and before purchase-limit reset, matching the captured GeoscapeState::time1Month prefix. World preflight uses the prospective next month before mutation. Isolated registered-handler tests compare calendar-month scheduling to the native capture and reload its transitions; the ordinary campaign dispatcher retains its branch 6 month boundary. Full later-month fallthrough coordination remains protected by that guard.


Branch 4c startup endurance: the 72-hour world scenario now also starts from month -1 through EnterCampaignWorld, rather than injecting its mission. Arc research enables mission scheduling; the mission counter enables a future event. Stepped, batched and transition-reloaded runs agree for fresh/cache content across craft dispatch/patrol/recall, servicing/fuel return, 36 UFO detections/landings and finite cleanup. The future event remains scheduled with 1,440 minutes after 72 hours, generated history and run counters persist, and repeated entry remains a no-op. The existing populated allocation checks still pass.


Newborn/monthly/startup batch audit: root verification reproduced a legacy-save month-boundary regression caused by reading incomplete soldier armor for maintenance before the retained month guard. Registered monthly preflight now follows that guard, preserving the earlier stopping boundary. Explicit initial/monthly accounting validates consumed armor rules and reports a precise restriction before mutation; initial accounting validation precedes maintenance preparation. The new empty-armor entry regression passes without weakening legacy save coverage.


Newborn/monthly/startup cycle closes: root dotnet test passes 2,130 tests without failures or skips, including the unchanged legacy-save month-boundary regression, new explicit-entry armor guard, checksums/private corpus and allocation cases. Formatting, code-map and diff checks pass. The newborn monthly composition guard is removed; future event payloads, combat/deployment and integrated monthly campaign progression remain guarded. Remaining detection providers/forecasts and final full closure audit keep branch 4c open; branch 4d is untouched.

Branch 4c detection damage profiles: pinned RuleDamageType defaults and Mod built-in table, RuleItem damage/melee pointer identity and script classification wrappers in strategic-detection-damage. Immutable per-item runtime projections retain float storage, predefined resets and signed overrides; cache revision 47 invalidates older projections. Base/craft VM tests cover all 20 built-ins plus raw defaults, nulls and pointer identity through fresh/cache and transition reload. RuleInventory.getMoveToCost remains explicitly guarded; raw damage conversions and arithmetic forecasts follow in the next slice. No tactical damage or event payload is enabled.

Branch 4c detection damage conversions: captured and enabled all eleven RuleItem.cpp getDamageToScript float multiplications plus armor-percent round-away behavior. Tests compare raw/default/built-in/custom positive and negative values, float precision loss, defined signed boundaries and null receivers through actual base/craft VM calls, cache and reload. Whole-program reference unions follow literal item lookup, primary/melee retrieval and aliases; consumed profile arithmetic is validated before ticks with bounded forecast work. Nonfinite/out-of-range results and unproven variable powers remain precise restrictions. Tactical damage application and random damage with a state are not enabled.

Branch 4c damage-query forecasts: enabled native BindSet pointer-copy operations and captured their identity/null behavior. Conservative fixed-point unions include alias and profile reassignments; scalar intervals include all literal/copy/swap writes, with unknown arithmetic widened to the full signed range. Full-range inputs execute when the consumed multiplier is safe; null profiles bypass unused power and rule data. Candidate-union and instruction work are bounded before expansion. Fresh/cache base/craft tests cover aliases, unrelated unsafe items, null short-circuiting, scalar reassignment, profile reassignment and an intentionally exhausted forecast budget. Arbitrary unsafe power calculations remain precisely guarded; no tactical or campaign-event effect runs.

Branch 4c damage-query batch audit: reproduced and fixed a false preflight rejection when a null Mod receiver looked up an item with unsafe damage fields. Receiver and STR_NONE sentinel short-circuits now retain null profile candidates without reading unconsumed arithmetic. Fresh/cache null-Mod and sentinel regressions pass; alias/profile/scalar bounds remain conservative. The committed batch passed 2,167 root tests before the new audit regressions. Formatting also identified a CRLF rewrite in the existing binding tests; restored the repository LF convention.

Damage-profile/query cycle closes: root dotnet test passes 2,171 tests with zero failures/skips, including the confirmed audit regressions, fixture checksums, private corpus and allocation gates. Formatting verification, regenerated code-map and diff checks pass. Compiler/cache revision is 47; generated native source, executable and trace remain useful, and the temporary object was removed. The reference checkout remains clean. Detection providers/forecasts and final branch-wide closure still keep the 4c gate open; 4d remains untouched.

Branch 4c inventory movement-cost evidence/projection: captured exact RuleInventory::getCost and ScriptBind const-member behavior in strategic-detection-inventory-cost, including pointer identity, negative/int-boundary costs and null source. Immutable ordinal runtime cost maps preserve typed rule replacement/inheritance; fresh/cache projection tests pass and cache revision advances to 48. Native null destination and missing cost entries are undefined; their VM provider will be enabled only with pre-tick reference forecasts. This slice does not remove its existing guard.

Branch 4c inventory movement VM query: enabled RuleInventory.getMoveToCost with native same-pointer/null-source behavior and signed cost values. A bounded control-flow reference forecast records states before each instruction, follows typed lookups/copies and joins both branch/loop possibilities. Nonnull destination and every possible distinct source/target cost are validated before the tick; same-register queries always return zero. Actual base/craft VM tests compare the native fixture through cache and reload, and null/conditional missing-cost cases stop without mutation. The previous generic binding guard is replaced by precise destination/cost restrictions.

Branch 4c shared reference forecasting: damage queries and missing conventional inventory checks now use the bounded instruction-level forecast, removing the obsolete whole-program reference union. Single eq/neq null branches refine possible pointers on both successors; aliases propagate the refined values. Tests cover profile replacement before a query, null-checked inventory aliases, null Mod branches with missing roles, loop-carried invalid targets and intentionally exhausted state-expansion budgets through fresh/cache/base/craft paths. Unproven correlated or arbitrary scalar paths retain precise guards. No branch 5/6 transition is enabled.

Inventory movement/reference-forecast cycle closes: batch audit found no confirmed issue. Root dotnet test passes 2,197 tests without failures/skips, including native fixture checksums, private corpus and allocation gates. Formatting verification, code-map and diff checks pass. Cache revision is 48. The native source/executable/trace are retained and the obsolete object removed; the pinned reference remains clean. Movement queries now execute with precise destination/cost guards and shared pointer forecasts. Branch 4c remains open for remaining detection providers/forecasts and final closure audit; 4d is untouched.

Branch 4c detection value copies/metadata alias: enabled native BindSet value copies for text and typed tag IDs as well as existing references. The pinned RuleSoldier.getType binding deliberately receives RuleSkill, matching its native wrapper/catalog; it now executes with native text/null defaults. strategic-detection-value-copy captures the exact template/wrapper. Fresh/cache base/craft VM tests cover text copies, tag copies, skill identity, nulls and reload. This adds no mutable world operation or tactical behavior.

Branch 4c random-damage short-circuits: captured the exact RuleItem.cpp getRandomDamageScript wrapper with a throwing unconsumed damage stub and recording RNG. Null profile or null RandomState returns zero without reading power/modifiers or drawing RNG. The VM provider now executes those provable paths, including editable state aliases, while nonnull profile/state computation retains a precise forecast restriction and runtime backstop. Fresh/cache base/craft/reload tests preserve native zero results even with unconsumed unsafe damage data. Actual random/tactical damage remains deferred.

Branch 4c scalar query forecasts: replaced the whole-program scalar union with bounded intervals at each instruction. Literal/copy/swap writes and item getPower/getMeleePower queries overwrite the interval; branch/loop joins retain possible values, and arbitrary arithmetic widens conservatively. Null random-damage results supply a zero interval to later conversions. Actual VM tests compose native power readers and all eleven float conversions, overwrite caller outputs, ignore later unused assignments and guard loop-carried overflow; explicit state-expansion budget tests pass fresh/cache. This is read-only script query support, with nonnull random damage and unproven arithmetic still guarded.

Branch 4c value/scalar batch audit: reproduced two confirmed regressions. Text declarations incorrectly stored scalar zero, and explicit text clear was unavailable; compiler initialization/clear now emits native empty text, verified against captured Script.cpp Func_clear_text and standalone VM plus base/craft/reload cases. Compiler/cache revision advances to 49. Forecast initialization also threw for legitimate reused block register offsets; reference/scalar state maps now initialize each offset once while declaration instructions reset its value at scope entry. The new fresh/cache scoped-register regression failed before the fix. Native Script.cpp variable/clear and scope handling plus the existing compiler scope-reuse tests establish the behavior.

Value-copy/null-random/scalar cycle closes: both confirmed audit regressions are fixed. Root dotnet test passes 2,239 tests with no failures/skips, including text defaults/clear, reused scalar/reference scopes, native fixture checksums, private corpus and allocation gates. Formatting verification, code-map and diff checks pass. Compiler/cache revision is 49. Generated native source/executables/traces remain useful; their obsolete objects were removed, and the pinned reference remains clean. Remaining detection providers/forecasts and final full closure audit keep branch 4c open. Branch 4d has not started.

Branch 4c typed detection declarations: the pinned full native parser capture in script-declaration establishes 23 initializer acceptance/rejection cases via Script.cpp parseVariableImpl and ScriptBind.h BindSet/ArgSelector. Variable initialization now validates exact type/reference compatibility and editable-pointer direction, preserving native null pointer/tag alternatives. Invalid initializers fail during content compilation instead of corrupting typed VM/forecast state. Unit regressions reproduced all nine tested invalid declarations before the fix; native fixture comparison and fresh/cache base/craft reload scenarios cover valid copies feeding damage queries. Compiler/cache revision advances to 50. Remaining detection providers/forecasts and final closure audit keep 4c open; 4d is untouched. Validation: root dotnet test passes 2,254 tests with no failures/skips; formatting verification, fixture checksums, code-map and diff checks pass. Native capture was repeated with identical output. Twenty obsolete probe objects were removed; the executable and trace remain useful, and the reference checkout is clean.

Typed declaration slice audit confirmed a remaining native-overload gap: pointers to scalar/text/tag/Position value types were accepted with null initializers, although native BindSet/BindValue exposes only value assignment for them. Declaration type validation now rejects these pointer forms before emitting a program, also covering absent initializers. The native fixture expands to 30 cases and malformed RUL tests prove invalid detection scripts cannot publish ScriptsCompiled or RuntimeLinked content. Compiler/cache revision advances to 51. Legitimate reference/tag/text copies retain the existing fresh/cache/reload acceptance. Audit fix validation: root dotnet test passes 2,262 tests without failures/skips; formatting verification, fixture checksums, code-map and diff checks pass. All 30 native cases match the retained trace, the probe objects are removed, and the reference checkout is clean. No further confirmed issue was found in this slice. Branch 4c remains open and 4d is untouched.

Branch 4c bounded detection arithmetic: captured 24 full native Script.cpp cases in strategic-detection-numeric, including arithmetic, inverted clamp ordering, signed wrap and calculated numeric operands. Instruction-level intervals now retain add/subtract/multiply/aggregate/offset, clamp, absolute, bit-count and square-root results; uncertain wrap widens and growing back edges widen before transfer to ensure bounded fixed points. Actual base/craft queries consume computed powers through fresh/cache/reload paths. Eleven focused numeric tests pass, including sign/wrap envelopes and exhausted forecast work. Reference: Script.cpp scalar operation definitions and Script.h parser/worker. Runtime forecast changes require no cache schema change. Combat/event/month guards and 4d are untouched.

Branch 4c calculated numeric safety: divisor and wave-period checks now consume bounded per-instruction scalar forecasts instead of requiring literals. All four divide/modulo helpers and five wave operations execute with proven nonzero divisors or positive periods; zero-inclusive, nonpositive, loop-carried and exhausted-work cases stop before the tick. Nine native calculated-operand traces run through both detection callers, fresh/cache and transition reload; caller input clamping also proves a safe divisor. Existing restriction messages remain precise and the structural loop/instruction limits remain in their owner. Reference: Script.cpp division/mulDiv/mulAddMod and wave helper bodies captured in strategic-detection-numeric. No combat/event/month or UI boundary changes.

Branch 4c native Mod receiver aliases: removed the obsolete whole-program direct-rules-input restriction for getSmokeReduction. A bounded instruction-level reference proof now requires the actual runtime catalog at each reachable call; typed declaration/set copies and null-tested repair paths execute, while possible-null branches, loop-carried nulls and exhausted forecast work stop before mutation. Existing native signed smoke arithmetic and ScriptBind pointer-copy evidence feed fresh/cache base/craft/reload regressions. Reference: Mod.cpp getSmokeReduction/ScriptRegister and ScriptBind.h BindSet/Script.cpp variable and condition handling. No tactical visibility calculation or later-branch transition is enabled. Native explicit pointer/editable-pointer/tag set-null assignments, needed for alias repair and loss, now compile through the target's typed BindSet provider; 34 full-parser declaration/copy cases and three compiler regressions pin the semantics. Compiler/cache revision advances to 52.

Detection arithmetic/numeric safety/Mod alias cycle audit closes: root dotnet test passes 2,292 tests without failures/skips, including private corpus, native fixture checksums and allocation gates. The batch audit found no additional confirmed runtime defect; it corrected the new numeric oracle comparison to canonical JSON while retaining all case/status checks, and corrected switch-expression indentation flagged by formatting verification. The focused 20-test numeric suite and final formatting verification pass, as do code-map and diff checks. Native captures/executables remain useful; forty obsolete objects were removed and the pinned reference remains clean. Compiler/cache revision is 52. Remaining forecast/provider and final closure work keep 4c open; 4d remains untouched.

Branch 4c scalar condition forecasts: per-instruction scalar states now refine comparison successors for six signed comparisons, true conjunctions and false disjunctions. Impossible intervals remove that numeric successor; divisor/damage preflight skips scalar-unreachable calls. Endpoint exclusions remain conservative when an interior not-equal hole cannot be represented. The native numeric trace expands to 29 cases with sign, zero, conjunction and extreme paths. Fresh/cache base/craft/reload tests prove safe branch divisors, bounded conditional powers and impossible calls, and boundary/empty-domain tests pass. Reference: Script.cpp parseCondition and test_le/test_eq operations plus Script.h signed scalar ABI. No binding capability, combat, event or month boundary is relaxed.

Branch 4c damage output forecasts: immutable RuleDamageType classification, armor-percent and all eleven float conversion outputs now propagate into later arithmetic/query inputs. Endpoint conversion checks preserve float rounding/truncation and widen invalid native float-to-int results; null candidates contribute zero and unknown profiles retain all primary/melee possibilities within the work budget. Classification execution and forecast share the same reader. Native strategic-detection-damage rows feed composed divisors and subsequent conversions through fresh/cache base/craft reload tests. Reference: RuleItem.cpp getResistTypeScript/getRandomTypeScript/getArmorIgnoreScript/getAoeScript/getArmorEffectivenessScript/getDamageToScript and RuleDamageType::isDirect. No random damage, tactical effect, event payload or month boundary is enabled; cache revision remains 52.

Branch 4c text lookup forecasts: bounded literal/copy/default/clear text states now resolve dynamic RuleMod.getRuleItem/getRuleInventory names at each call. Branch and loop joins retain every possible name, native STR_NONE item and null Mod lookups yield null, and unknown host text outputs remain conservative. Text-state work including string lengths is charged before reference expansion. Native damage/cost and text-copy evidence feeds fresh/cache base/craft/reload composition tests, unrelated-rule isolation, unsafe joins and work exhaustion. Reference: Mod.cpp getItemScript/getInventoryScript/getItem/getInventory, ScriptBind.h BindSet and Script.cpp text initialization/clear. Cache revision remains 52; no later-branch boundary changes.

Condition/damage-output/text-name cycle audit closes: no additional confirmed runtime defect was found in the batch. Root dotnet test passes 2,324 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates. Formatting verification, code-map and diff checks pass. Forecasts remain conservative for interior not-equal holes, unknown host text and unsupported bindings, and invalid arithmetic/query inputs stop before the tick. Twenty obsolete native numeric objects were removed; captures/executables remain useful and the pinned reference is clean. Compiler/cache revision remains 52. Remaining read-query forecasts/providers and the final full closure audit keep 4c open; branch 4d is untouched.

Branch 4c division/modulo output forecasts: div/mod/muldiv/offsetmod now retain conservative signed result intervals, enabling subsequent damage reads and computed divisors. muldiv and offsetmod preserve native 64-bit intermediates rather than wrapping early; ordinary remainder follows the dividend sign and normalized remainder follows the divisor sign. Zero-inclusive divisors and uncertain narrowing widen, with the existing pre-tick guard unchanged. The native arithmetic capture expands to 34 cases with composed helpers and negative normalized modulo; 30 focused numeric tests pass, including exhaustive small signed-domain containment. Reference: Script.cpp mulDiv_h/mulAddMod_h/div/mod and ScriptVmCore signed ABI. Cache revision remains 52; later-branch boundaries and 4d are unchanged.

Branch 4c immutable item scalar forecasts: the VM and forecast now share all eighteen enabled RuleItem scalar readers, including accuracy, power, armor, waypoints, weight, battle type and handling/single-target flags. Per-call reference candidates retain exact signed values, null zero and conservative unknown-rule unions before subsequent arithmetic or damage queries. Native strategic-detection-scripts default/custom reader rows feed calculated divisors and half-health conversion through fresh/cache base/craft/reload. All 113 focused item-rule tests pass. Reference: RuleItem.cpp ScriptRegister and isSingleTargetScript/getBattleTypeScript with RuleItem.h getters. No new binding surface or tactical effect is enabled; cache revision stays 52.

Branch 4c inventory cost output forecasts: validated RuleInventory.getMoveToCost results now retain signed intervals for later divisors and damage queries. Same-register identity and null-source calls produce zero without consuming destinations; candidate pairs preserve native same-slot identity, exact costs and null zero, while missing/unproven destinations or costs widen and retain existing precise guards. Candidate work is reserved before pair enumeration. Native strategic-detection-inventory-cost rows cover all seven identity/null/signed cases through composed fresh/cache base/craft/reload VM tests. Reference: RuleInventory.cpp getCost/ScriptRegister and ScriptBind.h null-receiver wrapper. No inventory movement or tactical effect is executed; cache revision remains 52.

Division/item-reader/inventory-cost cycle audit closes: no additional confirmed defect was found. Root dotnet test passes 2,333 tests with zero failures/skips, including native checksums, private corpus and populated allocation checks. Formatting, regenerated code-map and diff checks pass. The 34-case native numeric capture and exhaustive signed-domain containment verify the new arithmetic envelopes; item/cost composition retains precise unavailable-input guards. Twenty obsolete numeric probe objects were removed, while useful captures/executables remain. Reference checkout is clean and cache revision remains 52. Final query/provider and full acceptance reconciliation keep 4c open; 4d is untouched.

Branch 4c linked rule reference forecasts: all seven enabled named RuleMod lookup families now preserve literal/copied text candidates, null receivers, missing names and item-only STR_NONE behavior. Unit spawn/live/recovery and Research lookup/needed-item getters follow immutable linked projections, using canonical research identity strings. Reference work is charged before candidate/link expansion. Eight focused fresh/cache base/craft/reload tests use native ruleLookup and Unit-link evidence to prove null branches and compose linked-item damage reads without unrelated unsafe rules. Reference: Mod.cpp getItemScript/getInventoryScript/getArmorScript/getSoldierScript/getSkillScript/getUnitScript/getRuleResearch; Unit.cpp getters/ScriptRegister; RuleResearch.h getLookup/getNeededItem and RuleResearch.cpp ScriptRegister. No script provider/effect or cache schema changes; 4d remains untouched.

Branch 4c immutable rule scalar forecasts: enabled Armor/Soldier/Craft/UFO/Inventory/Research readers and Mod settings share the same execution/forecast implementation. Known rule references preserve exact values; unknown receivers conservatively include every family rule plus null zero, with candidate/string work charged before reads. Existing native Soldier/Armor stat fixtures now consume each output as a divisor before equality checks. Native armor-vision and Mod-settings rows compose with float conversions through fresh/cache base/craft/reload. Eight new composition and all 48 existing binding tests pass. Reference: Armor.cpp, RuleSoldier.cpp, RuleCraft.cpp, RuleUfo.cpp, RuleInventory.cpp, RuleResearch.cpp and Mod.cpp ScriptRegister/getter bodies, with ScriptBind null ABI captured in strategic-detection-scripts. Dynamic instance/campaign readers retain conservative ranges; no binding/effect, cache schema or later-branch boundary is changed.

Branch 4c bitwise result forecasts: complements, nonnegative masks and bitwise identities preserve conservative signed intervals; known valid shift counts retain bounded arithmetic-right or nonwrapping-left results. Unknown counts/wrap/sign combinations widen. Native strategic-detection-numeric expands to 41 cases with masks, complements and signed shifts, consumed by actual base/craft VM damage reads across fresh/cache/reload. Thirty-five focused tests pass, including exhaustive small signed bitwise domains and masked caller-input powers. Reference: Script.cpp bit_and/bit_or/bit_xor/bit_not/shl/shr and the signed scalar VM ABI. No compiler/cache schema or effect/transition boundary changes.

Linked-reference/immutable-reader/bitwise cycle audit closes: no additional confirmed runtime defect was found. Root dotnet test passes 2,354 tests with zero failures/skips, including native fixture checksums, private corpus and populated allocation gates. Formatting, regenerated code-map and diff checks pass. Immutable reader dispatch retains existing native values/null ABI, reference links use canonical identities, and bitwise envelopes passed exhaustive signed-domain checks. Twenty obsolete numeric probe objects were removed; useful captures/executables remain and the pinned reference is clean. Cache revision stays 52. Remaining query composition/forecasts and final branch-wide acceptance reconciliation keep 4c open; 4d is untouched.

Branch 4c wave output forecasts: native rectangle/saw/triangle and sine/cosine result ranges now compose with subsequent numeric/damage operations. Exact argument points follow the pure native helper bodies; interval results retain normalized phases, signed caps, negative-size zero and finite amplitude envelopes. Unproven periods, unsafe trigonometric narrowing and extreme unbounded amplitudes widen while existing period/conversion guards remain. Native numeric capture expands to 47 cases with negative caps/sizes/amplitudes. Forty focused tests pass, including native query composition through fresh/cache base/craft/reload and exhaustive small integer wave domains. Reference: Script.cpp wavegen_rect_h/wavegen_saw_h/wavegen_tri_h/wavegen_sin_h/wavegen_cos_h. No speculative script execution, effect or cache-schema changes.

Branch 4c bounded read-query domains: native boolean getters, category/research predicates and validated clock fields now retain result intervals including null zero. Smoke reduction composes its native unchecked signed product with division by sixty, retaining bounded outputs even when the product wraps; receiver proof remains mandatory. Eight focused tests consume boolean/calendar and native signed smoke traces in later damage queries through fresh/cache base/craft/reload. Reference: Ufo/ Country bool getters, RuleItem.cpp hasCategoryScript, SavedGame script research queries, GameTime getters and Mod.cpp getSmokeReduction; CampaignTime validation supplies calendar invariants. Unbounded years/counters/difficulty and unrelated mutable state remain conservative. No new provider/effect, cache revision or transition boundary changes.

Branch 4c query composition acceptance: the 72-hour stepped/batched/reloaded scenario now has fresh/cache and initial-entry variants with copied-name lookups, chained damage/arithmetic/bit/wave/smoke reads, clock fields, research predicates and country-list booleans through both detection callers. Existing 36-contact/landing, servicing, recall/relaunch, finite population and exact snapshot equivalence assertions remain. Populated workloads record 8/32/128 UFOs at one/eight bases, including cold and naturally reached warm boundaries plus persistence costs, without loosening allocation gates. Integration reproduced a signature-separator indexing exception in text/reference/scalar forecasts; all three now skip nonruntime separator parameters like ScriptVmCore.InvokeHostBinding. Two focused ABI regressions and all ten endurance/population cases pass. Reference: ScriptBind/Script.h list ABI and ScriptCompiler list lowering, Geoscape time/detection ordering and the existing native query fixtures. Cache revision stays 52; branch-wide closure remains pending and 4d is untouched.

Wave/bounded-query/endurance cycle audit closes: the confirmed list-signature separator indexing defect was fixed in the composition slice; the subsequent batch audit found no additional defect. Root Release dotnet test passes 2,374 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates. Formatting verification, regenerated code-map and diff checks pass. The 47-case numeric oracle pins signed waves and arithmetic, and 72-hour query scenarios preserve stepped/batched/reloaded equality. Query boundary measurements retain cold setup (~1.3 MB) and naturally reached warm costs (~576-579 bytes per additional UFO); existing cold differential allocation limits are unchanged. Twenty obsolete numeric probe objects were removed; useful captures/executables remain and the pinned reference is clean. Cache revision remains 52. Remaining query/provider reconciliation and final branch-wide acceptance keep 4c open; 4d is untouched.

Branch 4c country-rule query composition: Country.getRuleCountry now forecasts linked immutable country projections, including proven null and conservative unknown-country candidates within the work budget. FundingCap uses the shared execution/forecast reader, preserving native signed units and null zero. Native strategic-detection-scripts countryRules rows feed half-health conversions through fresh/cache base/craft and reload; explicit saved country identity avoids the fixture master overriding a same-name test rule. All twelve focused immutable-rule tests pass. Reference: Country.cpp getRules/ScriptRegister, RuleCountry.cpp getFundingCap/ScriptRegister and ScriptBind const-member null ABI. No mutable funding consequence, new provider, cache schema or later-branch transition is enabled.

Branch 4c null entity links: proven-null Ufo.getRules/getRuleUfo, Craft.getRuleCraft and GeoscapeGame.getTime now retain null output references at each instruction rather than widening to unrelated rules or clocks. Unknown nonnull entities remain conservative. Extended the pinned native detection capture with exact ScriptBind const-member scalar/pointer output-overwrite templates; existing trace fields remain byte-value equivalent. Twelve focused reference-link tests pass, including fresh/cache base/craft, impossible nonnull branches and save reload. Craft rule calls remain confined to the native craft parser surface. Reference: ScriptBind.h BindFuncImpl const specializations, Ufo.cpp/Craft.cpp rule registration and SavedGame.cpp getTimeScript. Cache revision stays 52; no new provider or mutable effect.

Branch 4c null scalar query composition: native zero results from proven-null UFO/craft instance/stat getters, country histories, campaign counters/difficulty and clock year now overwrite unbounded scalar inputs before later conversion/divisor forecasts. Nonnull or nullable mutable receivers remain unknown. Native const-member output-overwrite evidence and existing wrapper/stat fixtures feed fresh/cache base/craft and reload tests for every craft stat and dynamic getter families. Fourteen focused bounded-query tests pass; explicit nullable receiver and exhausted reference/scalar work cases preserve conservative behavior. Reference: ScriptBind.h const member null ABI, Ufo.cpp/Craft.cpp scalar/stat bindings, Country.h history getters, SavedGame.cpp wrappers and GameTime getters. No mutable consequence, new provider, cache revision or branch 5/6 boundary change.

Country/null-link/null-scalar cycle audit closes: no additional confirmed runtime defect was found. Root dotnet test passes 2,388 tests with zero failures/skips, including native fixture checksums, private corpus and populated allocation gates. Formatting verification, regenerated code-map and diff checks pass. The native capture adds scalar/pointer null-output overwrite evidence without changing existing trace values; fresh/cache, both caller APIs and transition reload tests cover country units, null links and scalar conversions. Nullable mutable receivers and exhausted work remain conservative. Minor test spacing and the previously recorded warm allocation lower bound (~576 bytes/UFO) were corrected. One obsolete native detection object was removed; useful captures/executables remain and the pinned reference is clean. Cache revision remains 52. Remaining read-query/provider reconciliation and full branch-wide acceptance keep 4c open; 4d remains untouched.

Branch 4c immutable identity text forecasts: enabled RuleItem/Armor/Craft/Skill/Soldier/Unit type and inventory ID providers now share their execution reader with finite text forecasts. Unknown receivers include every immutable family identity plus native empty text; candidate/string expansion is charged before retaining names. Dynamic lookups exclude unrelated rule families, while possible unsafe returned identities still stop before the tick. Native strategic-detection-scripts metadata/inventory rows feed fresh/cache base/craft composition and transition reload; all 73 focused text/binding tests pass. Reference: RuleItem.cpp/Armor.cpp/RuleCraft.cpp/RuleSkill.cpp/RuleSoldier.cpp/Unit.cpp getTypeScript and RuleInventory.cpp getIdScript. The native RuleSoldier wrapper continues to receive RuleSkill. No provider, effect, compiler/cache revision or later-branch boundary change.

Branch 4c text condition forecasts: finite identity/name sets refine equality and inequality successors, true conjunctions and false disjunctions. Aggregate feasibility also removes proven-impossible true disjunctions and false conjunctions; reference/numeric guards skip those unreachable instructions. Unknown text and NUL-bearing domains remain conservative, with string work reserved before comparison/filtering. Native numeric capture expands from 47 to 50 cases with text copy/equality/combined branches; all old traces remain unchanged. Seventy focused text/numeric tests pass, including fresh/cache base/craft and reload, unsafe alternative retention and direct impossible-instruction checks. The oracle harness now supplies the typed catalogue needed for native text declarations. Reference: Script.cpp Func_test_eq_text/parseConditionImpl and Script.h text ABI. No VM speculation, cache revision, effect or later-branch boundary changes.

Branch 4c exact identity feedback: reference and text forecasts now alternate conservative proofs until metadata-derived names stabilize. Known receivers retain exact type/ID text, including native empty text from null; later named lookups exclude unrelated unsafe rules even through several acyclic identity chains and guarded loops. Initial unknown metadata avoids eagerly expanding unrelated catalogues. Every pass and state comparison consumes one shared bounded work budget, and exhaustion rejects preflight intentionally. Thirty-five focused text tests pass through fresh/cache, both detection callers and transition reload, including four successive identity lookups, null chains, unsafe names and resource limits. Reference: native metadata/null wrappers in strategic-detection-scripts, Script.cpp text conditions/copies and Mod.cpp named lookup wrappers. This runs abstract proofs only, with no script execution, RNG or campaign mutation; cache revision remains 52 and later-branch boundaries remain guarded.

Identity-text/condition/feedback cycle audit closes: no additional confirmed runtime defect was found. Root dotnet test passes 2,402 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates. Formatting verification, regenerated code-map and diff checks pass. The audit checked native empty text, read-only reader alias types, branch set feasibility, scoped register reuse, and shared-budget accounting across feedback passes. Metadata refinement remains conservative for unknown/NUL-bearing comparisons and unproved loop correlations. Twenty obsolete numeric probe objects were removed; useful captures/executables remain and the pinned reference is clean. Native numeric evidence now has 50 cases; cache revision remains 52. Final query/provider and branch-wide acceptance reconciliation keep 4c open; 4d is untouched.

Branch 4c detection caller domains: scalar forecasts now retain the actual detection callers' boolean already_tracked and 0..10800 globe distance inputs, plus validated UFO altitude/status domains including IgnoreMe. Mutable radar inputs, counters and output registers remain conservative. Five fresh/cache base/craft composition and endpoint tests pin native damage-query use, oracle distances and output isolation. Reference: Base.cpp/Craft.cpp detection callers, Target.cpp getDistance, fmath.h XcomDistance and Ufo.h status/altitude getters; CampaignWorld.Restore validates entity domains. No provider, cache revision or later-branch transition change.

Branch 4c consumed detection fields: bounded reference/scalar reachability now limits UFO/craft statistic, damage/shield and tag metadata to reads that can execute on a nonnull receiver. Native null wrappers overwrite outputs with zero without consuming live overflow/undefined-denominator fields; impossible scalar/text paths no longer block those fields. Nullable live reads retain precise pre-tick guards and every instruction remains in the execution work bound. Six fresh/cache base/craft/reload tests reproduce the old null-read failure and cover overflow, unreachable calls and live denominator rejection. Reference: ScriptBind.h BindMember/BindFunc const null wrappers, Ufo.cpp/Craft.cpp getDamagePercentage and ScriptRegister. A live craft with installed armor overflow proves the craft path; zero-maximum craft are destroyed and cannot serve as actual detection callers. No provider, cache revision or later-branch boundary is changed.

Branch 4c composed identity/caller/field acceptance: the existing 72-hour fresh/cache, initial-entry and stepped/batched/reloaded workloads now execute repeated metadata-to-name lookups, bounded caller/UFO values and null instance reads through both detection parsers. An unrelated unsafe damage rule and an impossible metadata-name branch with a zero divisor verify proof isolation during actual VM execution. The populated 8/32/128-UFO workloads at one/eight bases record cold/warm simulation and persistence allocations with every prior differential bound unchanged. All ten endurance/population cases pass; 36-contact/landing, fuel/servicing/recall/relaunch and exact snapshot equality assertions remain intact. Reference: pinned strategic-world, strategic-detection-scripts, strategic-detection-damage and strategic-detection-numeric captures; ScriptBind, Mod/RuleItem identity readers and Geoscape detection ordering. No cache revision or later-branch boundary change; 4d is untouched.

Caller-domain/consumed-field/composed-acceptance cycle audit closes: no additional confirmed runtime defect was found. The implementation slice fixed the reproduced null-read preflight defect; the batch corrected test spacing and initializer layout. Root dotnet test passes 2,413 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates. Final formatting verification, regenerated code-map and diff checks pass. The 19 staged saves classify as ten executable at the thirty-minute horizon, one blocked after 125 ticks and eight tactical preservation-only. Expanded query cold setup allocates about 8 MB per campaign; naturally reached warm boundaries remain about 576-579 bytes per additional UFO, with prior differential limits unchanged and exact reload equality retained. No new native objects were generated; useful captures, corpus classifications and allocation measurements remain, and the pinned reference is clean. Cache revision remains 52. Remaining query/provider reconciliation and full branch-wide acceptance keep 4c open; 4d is untouched.

Branch 4c half-hour hunting maintenance: pure target-selection forecasts now apply ordinary-fuel refuelling and auto-patrol relaunch before the ten-minute hunter caller. Native setFuel/refuel/setDestination and the time30Minutes auto-patrol block expand strategic-hunting with 48 maintenance rows, retaining all prior traces. Fifty focused hunting tests pass, including fresh/cache and reload before/after relaunch, low-fuel retention and launch delay. Grounded craft stay excluded unless refuelling actually produces an eligible relaunch. Item-stock and hourly servicing forecasts remain precisely guarded for the next slices. No RNG, stock, identity or campaign mutation occurs in preflight; combat/event/month boundaries and 4d remain unchanged.

Branch 4c shared hunting fuel stocks: target forecasts now apply item refuelling to every grounded craft in saved order, using a bounded private stock projection. Ordinary non-patrolling consumers can prevent a later auto-patrol launch; fuel refunds, missing-item notifications, low-fuel clearing and full-tank launches follow the existing shared CraftLogistics operation. Eighteen native two-craft rows supplement the 48 maintenance rows, with every earlier trace retained. All 54 focused hunting tests pass through fresh/cache and transition reloads, with canonical native comparisons for item maintenance and scarce shared stocks. Hourly changes remain guarded pending the next slice. No stock, RNG or campaign state is mutated during preflight, and later-branch boundaries are unchanged.

Branch 4c hourly hunting maintenance: the pure craft projection now applies hourly repairs and the native empty-rearming transition before half-hour refuelling. It preserves the one-hour delay after repair completion and never speculates on ammunition-saving RNG. Twenty-four native hourly rows cover repairs, rearming, refuelling and already-ready craft; all prior hunting traces remain unchanged. Fifty-six focused hunting tests pass through fresh/cache and save reloads. Hourly item stocks, production ammunition reuse and incoming craft still have explicit composition guards for subsequent work. Reference: Craft.cpp repair/rearm/refuel and GeoscapeState time1Hour/time30Minutes fallthrough. No new combat/event/month transition or 4d work is enabled.

Hunting-maintenance cycle audit closes: confirmed incoming grounded auto-patrol and relaunched-leader escort-fuel forecast gaps now refuse the affected tick atomically; three regressions and two malformed-maintenance cases cover the boundaries. Craft-reference fuel scans are charged against the shared work budget. Root dotnet test passes 2,426 tests with zero failures/skips, including native checksums, the private corpus and populated allocation gates. The first root run hit a cold query allocation outlier (462.6 B/UFO against 448); focused endurance and the final root run pass with the original bounds unchanged. Test formatting was corrected. The obsolete native hunting object was removed; useful captures/executables remain and the pinned reference is clean. Hourly stocks, incoming craft and escort fuel composition remain subsequent 4c slices; no 4d or later-branch effect work was begun.

Branch 4c composed escort fuel: hunter preflight now builds one pure ordered craft projection, applies all maintenance/relaunches, then fuel use in saved base/craft order. The shared AdvanceCraftFuel transition accepts projected leader states, so a relaunch or an earlier low-fuel return supplies the speed consumed by later escorts. This removes the audit-added relaunch-leader guard. Four new native strategic-hunting rows insert getFuelConsumption/consumeFuel/returnToBase and the exact time10Minutes escort/fuel-return caller; all older traces remain unchanged. Eighty-eight focused hunting/craft-operation tests pass, including fresh/cache canonical order comparisons, notification fallthrough and reload. Incoming craft and hourly stock/ammunition effects remain precise subsequent work; 4d and combat/event/month boundaries are unchanged.

Branch 4c incoming hunter targets: due craft transfers now append in native base/transfer order to the pure projection, apply setBase/checkup and half-hour maintenance without receiving the earlier hourly service pass. Projected coordinates and live craft health participate in UFO arrival/combat preflight. The prior arriving-craft guard is removed for these supported transitions. Forty-eight native transfer/checkup rows retain all older hunting traces; fresh/cache canonical tests cover due/pending transfers, imported status, damage, full tanks, relaunch delays and reload. A close incoming target refuses combat before delivery. Hourly item-stock and ammunition-reuse composition remain explicit subsequent work; later-branch boundaries and 4d remain unchanged.

Branch 4c supplied hunting stocks: hourly projections now separate servicing, all transfer deliveries/item reuse, half-hour refuelling and ordered fuel use. Item arrivals feed shared fuel stocks and existing/new craft reuse without selecting RNG. The reference ItemsArrivingState batch ordering is now explicit in the transfer handler, with bounded all-arrival reuse work even when no UFO exists. Seventy-two supplied fuel/ammunition rows and twelve combined item/craft order rows retain all earlier native traces. Focused tests cover fresh/cache canonical outcomes, pending transfers, scarce hourly stocks, transition reloads and malformed work limits. Facility/craft ammunition consumption, production stock/craft consumption and finishing daily research stocks retain precise pre-tick composition checks. Reference: Transfer::advance, ItemsArrivingState constructor, Craft::reuseItem/refuel/rearm and GeoscapeState hourly fallthrough. Later-branch boundaries and 4d remain unchanged.

Composed escort/incoming/supplied-stock cycle audit closes: the confirmed malformed transfer-hour boundary now stops before delivery or item reuse, with two native invalid-arrival rows and hunter/non-hunter reload regressions. Reviewed saved craft order, per-base transfer batches, incoming checkup/service delay, projected target coordinates/health, combat refusal, scarce stocks and aggregate reuse work. Root dotnet test passes 2,448 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates; their original limits are unchanged. Formatting corrections and unused-import cleanup were applied. The obsolete hunting probe object was removed, useful captures/executables remain and the pinned reference is clean. Facility/craft ammunition, production stock/consumption and research-stock composition are still precise 4c work; branch-wide closure remains open and 4d is untouched.

Branch 4c facility ammunition stocks: execution and hunter forecasts now share the pure BaseFacility::rearm transition, preserving alert suppression, full-ammo reset, build-time eligibility and disabled-facility behavior. Facility consumption runs after craft servicing and before transfers/refuelling, removing the prior facility-stock composition guard. Thirty-six native facility/fuel rows retain all earlier traces and compare fresh/cache outcomes with transition reloads. All 117 focused hunting/readiness tests pass; malformed consumed fields and int-max rates have focused unit coverage. Reference: BaseFacility.cpp rearm and GeoscapeState time1Hour facility loop. No host/native dependency, new save field or later-branch transition is introduced; 4d remains untouched.

Branch 4c deterministic craft ammunition stocks: hunter preflight now reuses CraftServicing.Rearm with a refusing random provider, applying deterministic stock consumption in native craft order. It never selects a bullet-saving draw or touches campaign RNG. A draw sharing the fuel item retains a precise atomic stochastic-stock guard; unrelated ammunition cannot change the craft's service status during that hour. Forty-eight native CraftWeapon::rearm/Craft::rearm shared ammunition/fuel rows retain all older traces, covering missing stock, clip rounding, completion delay and stochastic boundaries. Fresh/cache cases reload before and after supported transitions; blocked cases preserve the entire snapshot. All 100 focused hunting/readiness and 17 servicing tests pass. Production and daily research stock composition remain subsequent 4c work; later branch boundaries and 4d remain unchanged.

Branch 4c incomplete production composition: execution and hunter preflight share the pure ProductionProgress completion count. Existing non-fallback units paid at start cannot consume or deliver stocks/craft until a completion, so their unrelated pending work no longer blocks hunting. Fallback staff assignment and completing production retain precise composition guards. The pinned capture adds 96 Production::getAmountProduced/step rows; fresh/cache cases cover pending fuel, craft-consumption and ammunition projects, fallback refusal, progress overflow and transition reload. Focused native hunting/economy and pure progress tests pass. Reference: Production.cpp completion-count prefix and getAmountProduced; no event payload, combat, month guard or 4d behavior changes.

The facility/ammunition/production batch audit closes with two confirmed fixes: unchanged facility-service results retain their existing records (the idle-campaign allocation test caught the unconditional hourly copy), and daily research composition checks cover ammunition reuse without item fuel plus cross-base held-item refunds. The latter has fresh/cache reload regressions and a non-hunter control showing cancellation/refund followed by same-tick relaunch. Research scanning is bounded and shared once per hunting forecast. Reference: Base::removeResearch, GeoscapeState::time1Day and SavedGame primary research side effects before hourly servicing/transfers. Final root dotnet test passes 2,477 tests with zero failures/skips; native checksums, private corpus, populated measurements and code-map checks pass. One initial query allocation sample exceeded its original 448 B/UFO limit; focused endurance and both subsequent root runs passed without changing any threshold. Useful captures/executables remain; the obsolete hunting object was removed and the pinned reference remains clean. Completing production, stochastic shared ammunition, research stock forecasts and branch-wide closure remain open 4c work; later branches and 4d remain unchanged.

Branch 4c stock-neutral daily research: the research capability now supplies a pure bounded proof that daily item stocks and incoming item deliveries remain unchanged. It includes direct completions and every possible free/protected-free topic, preserves global cancellation uncertainty when any project holds an item, and distinguishes lookup/automatic addFinishedResearch from primary item side effects. Stock-free completions no longer trigger hunter fuel/ammunition guards. Fresh/cache loaded fixtures complete research plus a free topic, launch fuel/ammunition craft and reload both sides; guarded stock-changing fixtures remain intact. Reference: GeoscapeState::time1Day, SavedGame::handlePrimaryResearchSideEffects and Base::removeResearch. Event scheduling remains gameplay-owned; due payloads and later branch boundaries remain guarded. Branch 4c closure is still open and 4d is untouched.

Branch 4c deterministic research refunds: the research capability now projects simple daily completions and global obsolete-project cancellation in saved base order, returning immutable per-base stock changes before hourly servicing. Choices involving rewards, lookups, protected unlocks, disablers and matching corpse units remain explicit uncertainties. The pinned Base::removeResearch fragment contributes sixteen native held/finished/return/stock rows; cancellation now correctly avoids refunding the needed item for an already-finished destroying project. Fresh/cache fixtures compare every native row and reload cancellations, direct refunds and cross-base refund/relaunch transitions. Stock overflow stops atomically. No campaign RNG, identity or state is changed by this forecast. Spawned item deliveries are the next slice; branch 4c and later transition boundaries remain open/guarded, and 4d is untouched.

Branch 4c fixed research item deliveries: deterministic daily research projections now append the direct spawned item and every list entry as one-hour arrivals, preserving native count minimums, list order, duplicate entries and missing-item behavior. Hunter forecasts apply refunds before hourly service, then existing plus research arrivals as one batch before refuelling/reuse. Seventy-two pinned primary research/transfer/refuel rows compare canonical fresh/cache output and reload both sides. Fixed ammunition delivery now prevents a no-item-fuel craft's same-tick relaunch correctly. Transfer identity and stock overflow refuse before research, RNG, clock or inventory changes. Reward/disabler/lookup/protected-unlock and corpse uncertainties remain precise research composition boundaries. Reference: SavedGame::handlePrimaryResearchSideEffects spawned-item fragment, Transfer::advance and ItemsArrivingState batch reuse. Branch 4c closure is still open; due event payloads, combat/month progression and 4d remain deferred.

The research-stock batch audit closes with a confirmed end-of-day guard fix: already-disabled held projects can cancel/refund without any fresh research completion. A bounded research capability query now identifies this case, and the projection applies disabled cleanup after all daily base loops. The regression first reproduced a close hunter consuming an unavailable combat tick, then passes atomically with the fix; fresh/cache cases also verify the safe refund/relaunch and reload. Reviewed deterministic source ordering, global cancellation, finished-item semantics, duplicate/missing spawned items, all-arrival reuse, ID/stock limits and pure RNG/identity handling. Root dotnet test passes 2,497 tests with zero failures/skips, including native checksums, 19 staged save classifications and populated performance gates with unchanged limits. The corpus retains ten executable 360-tick horizons, one 125-tick guard stop and eight tactical preservation cases. Code-map/diff checks pass; useful captures/executables remain, the obsolete hunting object is removed and the pinned reference is clean. Compound research stock consequences, completing/fallback production, stochastic shared ammunition, remaining hunter/escort births, detection reconciliation and branch-wide headless closure remain 4c work. Branch 4d is untouched; due event payloads, combat/deployment and unrestricted month progression remain later-branch boundaries.

Branch 4c lookup stock composition: daily stock forecasts accept status-neutral lookup discovery while preserving its addFinishedResearch-only semantics. Lookup item payloads and matching projects remain untouched by primary side effects; lookup disablers/re-enablers and automatic disablers retain precise uncertainty guards. Four captured native primary-topic rows pin direct/free membership with lookups excluded. Fresh/cache scenarios exercise held lookup projects, direct fuel arrivals, unchanged lookup stocks and reloads before/after hunter selection. Reference: GeoscapeState::time1Day primary topic collection, SavedGame::handlePrimaryResearchSideEffects and Base::removeResearch. No RNG, identity or campaign state is changed during the forecast; later branch boundaries and 4d are unchanged.

Branch 4c research discovery stock composition: execution and preflight now share the addFinishedResearch discovery transition over live or copied completion/status sets. Direct and lookup disablers, re-enablers, automatic zero-cost chains and protected-unlock cancellation eligibility now participate in ordered daily stock refunds. Forecasts account for completing facility functions, active projects and refunded needed items before automatic discovery; work stays bounded and no RNG/state is consumed. Sixteen pinned native discovery/disabled-refund rows compare fresh/cache daily results and transition reloads, including already-complete sources and already-finished held projects. Reward choices and corpse item projections remain for subsequent slices. Reference: SavedGame::addFinishedResearch/hasUndiscoveredProtectedUnlock and GeoscapeState::time1Day construction/research ordering. Branch 4c closure and later-branch guards remain open; 4d is unchanged.

Branch 4c deterministic daily research rewards: stock forecasts now select sequential free topics and unambiguous eligible outcomes without consuming RNG, including duplicate candidates and prerequisite-gated protected rewards. Shared discovery runs the bonus and its lookup before the direct topic, then catalogue-ordered primary item arrivals and global obsolete-project cancellation. Seventy-two pinned selectGetOneFree/remaining-reward rows cover completed/disabled candidates, gated groups and duplicates; fresh/cache fixtures verify fuel, retained/cancelled cross-base held projects, reloads and atomic refusal of distinct random outcomes. Focused hunting/research compatibility passes 148 tests. Reference: SavedGame::selectGetOneFree/hasUndiscoveredGetOneFree and GeoscapeState::time1Day bonus-before-core ordering. Completing production, stochastic stock choices, corpse projections and final headless closure remain open 4c work; later branch boundaries and 4d are unchanged.

The research-composition batch audit confirms and fixes automatic discovery ordering: SavedGame::getAvailableResearchProjects iterates the research std::map in UTF-8 name order, independently of ruleset insertion and listOrder. Runtime catalogs now derive that immutable order on both fresh and cache construction; execution and forecast share it. Native competing-disabler and BMP/supplementary-name traces pin queued topics that become disabled after queueing, and fresh/cache reload scenarios verify safe hunter selection plus atomic combat stops. Protected-unlock membership scans are now charged at their actual nested cost; an 1,100-topic malformed-work scenario refuses before any tick mutation. Reviewed live/projected discovery semantics, global cancellations, reward eligibility, duplicate/missing arrivals and RNG/identity purity. Branch 4c remains open; 4d and later-branch boundaries remain unchanged.

Cycle validation: root dotnet test passes 2,511 tests with no failures/skips, including checksum verification, 19 staged-save classifications and populated 8/32/128-UFO measurements under unchanged limits. The corpus retains ten executable 360-tick horizons, one 125-tick stop and eight tactical preservation-only cases. Code-map/diff checks and reference-checkout cleanliness pass. Useful probe captures/executables remain; the obsolete compiled hunting object is removed at this milestone.

Branch 4c zero-quantity production delivery: the pinned Production::step item fragment retains zero-valued outputs as observable delivery/reuse operations. Immediate zero outputs now interrupt eligible craft ammunition servicing exactly like nonzero outputs; delayed zero item transfers retain their identity, hours and payload through save rewrites and arrive/clean up once. Snapshot validation permits zero only for item transfers and still rejects negative quantities and empty staff transfers. Seventy-two native delivery/refuel/reuse rows include zero quantities; fresh/cache tests compare zero rows and reload both sides, plus incoming-zero cleanup regressions. Reference: Production.cpp fixed item delivery, Transfer::advance/save, Craft::reuseItem and Geoscape hourly ordering. This fixes delivery semantics without enabling speculative production forecasts; branch 4c and later transition gates remain open, and 4d is unchanged.

Branch 4c final prepaid production forecasts: registered production-owned operations now project final fixed item outputs before hunter retargeting, after servicing/transfers and before every base refuels. Final units never charge inputs again, including paid materials/craft with insufficient current funds. Immediate outputs preserve native zero-quantity reuse; delayed outputs stay future arrivals and consume bounded transfer identity headroom, coordinated with earlier daily research arrivals and same-hour cleanup. Seventy-two native fixed-delivery rows compare fresh/cache hunting outcomes and both transition reloads; stock/identity overflow stops atomically. Ongoing/fallback, sold, random, craft and person production remain precise stock composition boundaries. Reference: Production::step final-unit break and item delivery, GeoscapeState::time1Hour ordering. Focused hunting/economy tests pass 169 cases; later branches and 4d are unchanged.

Branch 4c recurring free production forecasts: non-fallback zero-cost/no-input projects now project every fixed item completion in native order, honoring finite caps and infinite production. Native completion-count and delivery fragments contribute 192 composed rows for zero/multiple units, zero/two outputs, delayed transfers, shared fuel and ammunition reuse. Fresh/cache scenarios reload before/after and preserve negative funds for free unit starts. Work, cumulative future item quantities, identity slots and captive populations are bounded before any campaign mutation. Sold, funded/material-consuming, fallback, random/craft/person outputs and nonzero research-score effects retain explicit composition guards pending their projections. Focused hunting/economy tests pass 181 cases. Reference: Production::step, getAmountProduced, haveEnoughMoneyForOneMoreUnit and fixed delivery/reuse. Branch 4c closure remains open; later boundaries and 4d are unchanged.

The production batch audit fixes a confirmed mixed-producer identity hole: an unsupported random producer could allocate transfers omitted from the fixed-producer budget, overflowing only after tick mutation began. A precise capability guard now blocks that mixed future stream before clock/RNG/inventory changes; fresh/cache regressions reproduced the overflow before the fix and pass after it. Cross-handler cases also verify daily research consuming the final identity slot and same-hour cleanup releasing an imported highest ID. A production-funded relaunch remains atomically blocked at same-tick hunter interception. Reviewed prepaid/recurring unit counts, all-base ordering, zero quantity reuse and persistence, aggregate incoming/captive quantities, resource limits and remaining score/accounting/fallback uncertainty. Branch 4c remains open; 4d and later-branch boundaries are unchanged.

Cycle validation: all 2,544 root tests pass with zero failures/skips, including native checksums, private save horizons and original populated performance gates. Code-map/diff checks and pinned-reference cleanliness pass. Useful captures/executables remain; the regenerated hunting object is removed at this milestone.

Branch 4c funded production composition: non-fallback fixed item projects without consumed inputs now forecast paid continuation in saved base/project order. The current unit is prepaid; later starts use the same checked accounting operation as execution, stop on insufficient money, and avoid charging after the finite last unit. Unknown earlier sale/cost effects retain a precise shared-accounting guard. Sixty-four native Production::step continuation/startItem/money rows compare fresh/cache unit counts, funds, stop reasons, stock and hunter targets with both transition reloads. Focused hunting/research compatibility passes 193 tests. Material consumption and fallback staffing remain subsequent slices; later-branch boundaries and 4d are unchanged.

Branch 4c consumed production materials: fixed item completions now apply immediate output/reuse before each continuation input check. Delayed outputs stay unavailable to the next unit, and failed input checks leave funds unchanged. The production-owned forecast consumes only projected stocks and preserves saved project order; unknown earlier material effects retain a precise shared-material guard. Seventy-two native money/material/startItem rows compare unit counts, stop reasons, stocks, future arrivals and hunter outcomes with fresh/cache reloads. Craft input consumption and fallback staffing remain subsequent work; branch 4d and later-branch transitions are unchanged.

Branch 4c fallback production composition: execution and forecasts share the queued-space/engineer assignment transition. Forecasts include due engineer arrivals and workshop construction completed at the preceding daily hook, then preserve project order and ongoing occupancy. The exact hourly reference caller confirmed that project removal and staff release occur after every production step in a base; execution now retains completed/stopped projects until that pass, so later fallback work cannot borrow their engineers early. Seventy-two native staffing rows and four deferred-release rows compare fresh/cache counts, stocks, funds-independent stops and hunter results with transition reloads. Focused hunting/research compatibility passes 203 cases. Capture inserts the native progress enum instead of synthesized values. Sold, craft-consuming, random/craft/person outputs and score effects remain explicit subsequent 4c work; later-branch guards and 4d are unchanged.

The paid/material/fallback production batch audit fixes two confirmed issues. A fallback project could acquire staff after ordinary preflight and overflow its spent time after the tick began when no hunter existed; fresh/cache regressions reproduced the exception. A shared bounded staffing forecast now validates every hourly production pass and supplies the hunter projection, preserving due transfers, daily construction and deferred project removal. Idle bases also bypass empty removal-list allocation; the unchanged 16,384-byte summary test caught that regression and now passes. Reviewed prepaid units, money-before-material checks, output-before-input reuse, saved project/base order, future quantity/identity bounds and transition reloads. Focused compatibility/endurance passes 215 tests and foundation/progress passes 22. One initial populated query sample exceeded its existing limit; the focused rerun passes without changing it. Branch 4c remains open; 4d and later-branch boundaries are unchanged.

Cycle validation: all 2,563 root tests pass with zero failures/skips, including reference checksums, 19 private save classifications and unchanged populated performance limits. Code-map/diff checks pass. Useful captures/executables remain; the obsolete compiled hunting object is removed at this milestone. The pinned reference checkout is unchanged.

Branch 4c native craft inputs: Production::haveEnoughMaterialsForOneMoreUnit uses the required quantity only to count grounded craft, while startItem removes one first matching craft per listed type, including zero-valued entries and airborne first matches. Twenty-four native eligibility/selection traces reproduced the zero-input mismatch before the fix and now compare fresh/cache results with empty unload payloads and reloads. Execution preserves that split; counter lower bounds treat zero craft inputs as possible consumption. The hunter forecast retains a precise consumption guard for these now-correct transitions until the next slice. Existing hunting/research compatibility passes 207 tests. Reference: Production::startItem, Base::getCraftCountForProduction/removeCraft and Craft::unload. Branch 4d and later-branch boundaries are unchanged.

Branch 4c craft consumption forecasts: execution and preflight share material eligibility and first-craft selection. Each projected start removes item inputs before craft unloading, adds inventory/weapons/vehicle ammunition through the existing unload operation, and removes consumed actors before refuelling/retargeting. Twenty-eight pinned item-unload/refuel rows cover missing input, payload-driven relaunch and a valid int-max inventory that requires item-first ordering. Fresh/cache cases reload both sides; unsafe unload stock/quantity arithmetic refuses atomically. The prior ordinary craft-consumption stock guard is removed for fixed zero-score item producers. Focused hunting/research/counter tests pass 223 cases. Reference: Production::startItem, Craft::unload and Base::getCraftCountForProduction. Deleted-target pursuit composition is the next slice; later branches and 4d remain unchanged.

Branch 4c consumed target repair: a registered world-owned operation now applies Target::~Target follower consequences when production removes a craft. Craft return destinations/speeds change before later hourly, refuel and fuel hooks; hunted UFOs restore the original waypoint without changing speed. Forecast sources share the craft return operation, and absent projected identities remain absent even for imported hunting UFOs with the hunter-killer flag clear. Four native destructor/fuel rows compare initial production commands and same-hour removal with fresh/cache reloads. Work bounds include repeated removals, arrivals and possible production births before any tick mutation; a 1,001-by-1,001 follower workload refuses atomically. Focused hunting/research/counter tests pass 229 cases. Reference: Target::~Target, Craft::returnToBase/setDestination and Ufo::resetOriginalDestination. No notification performs mutation; command/tick operations retain the single writer. Branch 4c remains open; later-branch transitions and 4d are unchanged.

The craft-input/unload/pursuit batch audit fixes a confirmed late-validation boundary. An invalid grounded craft follower at an unplaced base could cause a production stop only after its prepaid output and clock were mutated; fresh/cache regressions reproduced this for current and same-hour arriving followers. Production preflight now supplies bounded possible removal types, and world preflight validates existing plus due follower payloads before the tick. The exact registry-order assertion includes the new preflight without reducing strictness. Reviewed zero/multiple craft quantities, first-craft selection, item-before-unload overflow, identity lower bounds, synchronous return/restoration, future arrivals/birth work budgets and deleted-target reloads. Focused existing/arriving follower, native removal and populated performance checks pass with unchanged limits. The first full query allocation sample exceeded its original bound; both focused populated reruns pass without relaxing it. Branch 4c remains open; sold/random/craft/person production, score effects, stochastic/corpse projections and final headless reconciliation remain subsequent work. Later-branch boundaries and 4d are unchanged.

The final ordering check also reproduced an unstable material partition: sorting equal craft entries reordered an 18-craft native input list and its follower notifications. Material planning now appends craft inputs after items while retaining the reference order within each group, without allocating a craft list for item-only projects. The pinned startItem trace and fresh/cache transition-reload regression cover the complete deletion and feedback sequence.

Two default-runtime root runs exceeded the unchanged query boundary allocation gate (501.8 and 450.1 B per additional UFO); isolated default and process-local non-tiered runs passed. Inspection found interface enumeration in WorldGeometry.InsideRegion. Indexed traversal preserves the existing ordered geographic predicate and removes that enumeration allocation regardless of JIT optimization. The original populated measurements fall from approximately 369 to 208 B per additional UFO for eight-base query boundaries; warm-boundary costs fall by 160 B/UFO. Existing native geography fixtures and allocation thresholds remain unchanged. This addresses the concrete allocation source without claiming a proven explanation for runtime-dependent sample variation.

Cycle validation: all 2,583 root tests pass with zero failures/skips after the fixes, including native checksums, 72-hour scenarios, all 19 private save classifications and the original populated allocation gates. No test or threshold was relaxed. Code-map/diff checks pass; the pinned reference is clean. Useful captures/executables remain, and the compiled hunting object was removed. Branch 4c headless closure remains open and branch 4d is untouched.

Branch 4c fixed production sales: hunter projections now apply unscripted, fixed item sales before each continuation money/material check in saved base/project order. Sold outputs create neither stock/reuse nor delayed transfers, and sale coefficients truncate before quantity multiplication. The pinned Production::step, RuleItem::getSellCostAdjusted and SavedGame::setFunds fragments add 128 continuation rows plus two mixed-sign ledger rows, with fresh/cache same-base and cross-base reload coverage. The audit reproduced and fixed netting mixed sale signs in live execution, which previously lost separate income/expenditure entries, and a no-hunter accounting exception after tick mutation. Production-owned preflight now reserves signed capacity for every potential completion/start, bounding scripted prices by their int range without executing a future-context VM. These conservative capacity bounds intentionally refuse extreme imported ledgers without relying on later opposite-sign offsets or uncertain production stops. Local capacity tests cover signed headroom, independent ledgers, large counts and malformed bounds. Direct/global time-reading sale scripts retain a precise hunter forecast guard while ordinary execution still runs the real VM at the advanced clock. Random/craft/person production, score effects and price-script projections remain subsequent 4c work; later-branch boundaries and 4d are unchanged. Reference checkout remains read-only.

Slice validation: all 2,617 root tests pass with zero failures/skips, including native fixture checksums, private save classifications, 72-hour scenarios and populated allocation gates. Full formatting verification and code-map/diff checks pass. No test or threshold was relaxed. The pinned reference checkout is clean; useful captures remain and the compiled probe object was removed. Branch 4c remains open and branch 4d is untouched.

Branch 4c scored production: fixed item and sale output with positive/negative manufacture points now participates in hunter stock/funding forecasts. Twenty-four pinned Production::step / SavedGame::addResearchScore rows cover per-unit points before continuation, finite/infinite caps, material stops and subsequent refuel/hunting. A shared signed-capacity preflight bounds daily primary/reward/lookup/automatic discoveries plus hourly output before tick mutation, without selecting random rewards. Extreme score histories are conservatively refused rather than relying on cancellation or uncertain stops. Fresh/cache fixtures and transition reloads pass; later-branch boundaries and 4d remain unchanged.

Branch 4c deterministic random manufacture: positive-weight choices with identical ordered item outputs now compose with fixed output, sales, continuation materials, reuse/refuel and hunting without speculative RNG. Sixty-four pinned random-delivery traces cover one/two identical choices, zero quantities, auto-sale, delayed output and finite/infinite continuation. Random output is delivered even when fixed output is sold, and every queued zero-quantity entry reserves its transfer identity. Per-project history preflight bounds all reachable positive-weight item increments plus unsold fixed output, preventing overflow before mutation even without hunters. Fresh/cache and history-limit reload tests pass. Genuinely different outcomes keep precise composition guards; the mixed uncertain-transfer fixture now contains distinct choices. Later-branch boundaries and 4d are unchanged.

Branch 4c scientist/engineer production: delayed staff output now composes with fixed/deterministic-random item output, funding, reuse and hunter selection. Forty-eight pinned Production::step / Base::getUsedQuarters rows cover default/minimum personnel delay, finite/infinite completion, intermediate living-space fallthrough and final continuation checks. Inspection found and corrected the missing one-quarter reservation for each person-producing project, including queued projects; deferred removal/cancellation releases that reservation. Production-owned preflight bounds possible transfer creation and personnel headroom before mutation, including ordinary worlds with no hunters. Fresh/cache native rows, queued cancellation/reloads, transfer exhaustion and population limits pass. Soldier generation and genuinely uncertain outputs remain guarded in hunter composition; later-branch boundaries and 4d are unchanged.

The score/random/personnel batch audit bounds empty protected-reward groups, accounts for the existing saved transfer population before production creates more, and rejects imported personnel totals that overflow after adding project reservations. Regression fixtures cover 1,001-by-1,001 empty groups, 100,000 existing transfers and signed personnel limits. Twenty-four additional native rows pin omitted personnel transfer delay at 24 hours while preserving the original 48 rows. No speculative VM/RNG, fixture strictness or allocation threshold was weakened.

Cycle validation: all 2,647 root tests pass with zero failures/skips after the batch audit fixes, including fixture checksums, the 19 private save classifications, composed 72-hour scenarios and unchanged population/allocation checks. Full formatting and regenerated code-map/diff verification pass. The reference checkout remains clean; useful native captures remain and the compiled object was removed. Branch 4c remains open; branch 4d is untouched.

Branch 4c corpse evidence and projections: 512 pinned removal/daily-interrogation traces distinguish held-item return from corpse recovery across finished/cancelled research, retainCorpses, unit lookup and both recovery flags. Runtime units link armor, armor links its geoscape corpse, and items retain recover/recoverCorpse defaults; fresh/cache tests pass with compiler revision 53. Campaign retainCorpses defaults false per Options.cpp, honors fixedUserOptions and survives the save adapter/reference option snapshot. The separate native callers can award two corpses on completion; gameplay application and forecasting follow in the next slices. Reference: Base::removeResearch, GeoscapeState::time1Day, RuleResearch::isHoldingNeededItem, Armor::getCorpseGeoscape and RuleItem recovery getters. Later-branch boundaries and 4d remain unchanged.

Branch 4c live research recovery: completion applies the Base::removeResearch held/corpse effect and the separate daily interrogation corpse effect before discoveries. Cancellation applies only the removal effect, looking up the unit by research name even when neededItem differs. All 512 native rows pass fresh/cache and before/after reloads; the shared local lifecycle rule uses the same verified oracle. Command/daily preflight bounds recovered stocks, including possible earlier cross-base cancellations, so corpse overflow refuses before staff/project/time mutation. Completed projects remain deleted after rewriting saves. Hunter corpse stock composition follows in the next slice; later-branch boundaries and 4d are unchanged.

Branch 4c corpse composition: daily stock projection now uses the same removal/daily recovery operation as execution before automatic discoveries and hourly inputs, including earlier-base cancellations and final disabled-project cleanup. The old unit/corpse forecast guard is removed. Another 128 pinned recovery/refuel/hunting rows pass fresh/cache plus transition reloads; cross-base fixtures prove a cancelled finished project receives only the removal corpse and cannot run its later daily caller. Recovery changes retain exact item identities and do not invoke item reuse for direct corpse addition. Random reward choices and other uncertain compositions retain their specific guards. Later-branch boundaries and 4d remain unchanged.

The corpse batch audit removes empty-research preflight allocations and avoids duplicate unit-armor runtime diagnostics: typed validation still rejects absent/missing armor, with two focused regressions. All 2,669 root tests pass with zero failures/skips, including the unchanged allocation gates and private corpus classifications. Native checksums and code-map/diff verification pass; no fixture expectations or limits were relaxed. Branch 4c remains open and 4d is untouched.

Branch 4c native craft manufacture: runtime STR_CRAFT projections now retain only the first producedItems entry, matching RuleManufacture::afterLoad; later undefined names are ignored. Production creates fixed weapons, moves to its base and runs checkup before immediate insertion or delayed transfer, sharing existing craft operations. Thirty-two pinned Production::step / Craft::setBase/checkup rows cover weapon rearming, zero/nonzero fuel, negative/zero/positive craft delays and coordinates; fresh/cache transition reloads plus local first-output tests pass. Compiler revision 54 invalidates old projections. Hunter birth composition follows in subsequent slices; later-branch boundaries and 4d are unchanged.

Branch 4c delayed craft composition: deterministic delayed craft manufacture now participates in ordered funding/material continuation and transfer-identity forecasts without inserting a future craft into current hunter candidates. Native birth fixtures now run alongside an existing item-fuel auto-patrol craft through fresh/cache reloads. Shared production preflight bounds produced craft IDs against saved counters, existing craft and transfer payloads, total craft population, transfer births and malformed service capacities before ordinary or hunter ticks mutate. Twelve focused failure cases plus the existing native initialization rows pass with unchanged atomicity assertions. Immediate craft births remain the next slice; later-branch boundaries and 4d are unchanged.

Branch 4c immediate craft composition: deterministic craft completions now append in reference base/project order before continuation, random item reuse, half-hour shared refuelling and hunting. A pure identity allocator includes due/remaining transfers, saved craft floors and prior delayed births; uncertain daily research or other production identities retain precise guards. Another 256 pinned craft-birth/continuation/fuel/hunting traces pass fresh/cache and reloads, covering funds/material stops, fixed weapons and immediate/delayed output. A same-hour newborn input fixture verifies later craft consumption and deleted-actor reloads. All 285 hunting cases pass before the added chain cases. Later-branch boundaries and 4d are unchanged.

The three-slice craft audit reproduced two missing preflight bounds: daily research could increment the craft counter into exhaustion before production, and repeated identity scans did not charge empty bases. Shared production capacity now uses bounded post-research counter ranges and charges base visits plus research/reward/counter scans. Four fresh/cache regressions reproduce the prior overflow/affected-tick mutation and now refuse atomically, including a 1,001-base by 1,001-type workload. Reviewed first-output selection, fixed weapon/checkup ordering, delayed transfer timing, continuation stops, shared fuel, same-hour newborn consumption and deleted-actor reloads. No expectations or allocation/work limits were relaxed.

Cycle validation: all 2,696 root tests pass with zero failures/skips after the confirmed craft audit fixes, including native checksums, 19 private save classifications, composed 72-hour scenarios and unchanged population/allocation gates. Full formatting verification and code-map/diff checks pass, the reference checkout remains clean, and the compiled hunting object was removed while useful captures remain. Branch 4c headless closure remains open; 4d is untouched.

Branch 4c ordered research counter projection: the daily research forecast now records primary/reward counter changes in reference catalog order after discovery and cross-base cancellation, with duplicate side effects suppressed. Counter-only completions no longer take the unchanged-stock shortcut. Sixteen native SavedGame primary-counter/default/clamp traces pass local arithmetic and fresh/cache forecast-vs-execution/reload fixtures, including earlier-base cancellation without duplicate effects. The forecast remains pure and work-bounded; the following slices connect its exact changes to craft identity allocation and capacity. Later-branch boundaries and 4d are unchanged.

Branch 4c exact research-to-craft identities: the world passes the pure ordered daily counter stream into production forecasting, removing the ambiguous-ID guard for single, sequential and repeated-identical free rewards. Eight additional native traces pin one/two-base sequential completion: a later base keeps its project while another free reward remains. Fresh/cache craft identities match both counter oracles with before/after reloads and no speculative RNG. Genuinely different random outcomes retain their precise forecast guard. The next slice shares exact counter bounds with ordinary production preflight; later-branch boundaries and 4d are unchanged.

Branch 4c exact ordinary craft capacity: production preflight now uses the same deterministic daily counter stream as hunter identity projection. Valid ordered decreases can free counter headroom, while existing craft/transfer payloads remain identity floors independently of the persisted counter. Six additional native near-limit traces preserve all earlier rows; fresh/cache single/sequential/identical reward births pass at int-max-minus-one. Saved actor floors and genuinely different reward choices still refuse atomically, preserving RNG and the affected tick. Unknown outcomes retain conservative bounded ranges. Later-branch boundaries and 4d are unchanged.

The counter batch audit reproduced research-only counter overflow with no production or world sites. Research-owned daily preflight now validates every deterministic primary counter transition and reserves positive bounds for unknown reward paths without crediting unselected decreases/cancellation. It also bounds potential cross-base counter work; a 1,001-base by 1,001-counter fixture refuses on repeated attempts without clock/RNG/project mutation. Presence caching is published only after a completed scan. Six fresh/cache audit regressions pass. Native near-limit ordered decreases remain enabled; later-branch boundaries and 4d are unchanged.

The final counter audit restricts uncertain bounds to direct completions and possible free rewards: lookup and automatic addFinishedResearch discoveries do not execute primary counter effects. A near-limit lookup-only counter regression passes fresh/cache and reloads without a false refusal. All prior bounds and RNG-preservation assertions remain unchanged.

Cycle validation: all 2,717 root tests pass with zero failures/skips after the counter batch audit fixes, including native fixture checksums, all 19 private save classifications, composed 72-hour scenarios and unchanged population/allocation gates. Full formatting verification and regenerated code-map/diff checks pass. The pinned reference checkout remains clean; useful captures remain and the compiled hunting object was removed. Branch 4c headless closure remains open; branch 4d is untouched.

Branch 4c soldier production preflight: full pinned Production::step personnel delivery and Mod::genSoldier duplicate retry traces establish one transfer/identity per birth, minimum/default delay, template/name ordering and the ten-attempt pending-name limit with controlled constructor/name/nationality collaborators. Production-owned preflight bounds generation/template work, malformed stats, possible bonus overflow, soldier population and post-research identities including pending actors and craft types sharing STR_SOLDIER. Twelve fresh/cache atomic-refusal fixtures pass, with save/reload identity coverage. The next slices apply nationality/pending-name semantics and compose soldier births with hunting; branch 4d and later-branch boundaries are unchanged.

Branch 4c manufactured soldier caller semantics: production now uses the shared base-location nationality selector and includes pending-transfer names in Mod::genSoldier's duplicate check. All 24 pinned personnel/duplicate traces pass fresh/cache, with generated identity, minimum/default delay, template-before-final-name ordering, RNG retry counts and reloads before birth, after birth and after arrival. A two-name-pool country fixture selects the first country match. Generation remains inside live execution; forecasts do not consume its draws. Reference: Production::step, Mod::genSoldier, SavedGame::selectSoldierNationalityByLocation and Soldier::genName. Fourteen combined focused cases pass; branch 4d and later-branch boundaries remain unchanged.

Branch 4c soldier production composition: deterministic strategic outputs now compose soldier births with continuation accounting/materials, living quarters, item reuse/refuelling and hunting without speculative soldier generation or RNG. Thirty-six new native soldier/quarters/hunting rows pass fresh/cache and transition reloads. Production uses a shared pure counter stream for person-before-craft, craft-before-person and same-project births when a craft is named STR_SOLDIER; three native ordering traces and reloads pin both identities. Unknown prior production/research identities retain precise guards. Soldier transfers remain delayed by at least one hour. The six composed cases pass; branch 4d and later-branch boundaries remain unchanged.

The soldier production batch audit reproduced and fixed three gaps: growing pending-name scans now charge births across all projects/bases and transfers from other production; world marker forecasts include soldier births and preserve conservative bounds for mixed craft/soldier allocator floors; site-expiry counter capacity includes preceding actor production. The expiry fixture also reproduced the existing craft variant as an overflow after tick mutation. Fourteen fresh/cache regressions now refuse atomically, and safe marker births retain reload coverage. Generation work arithmetic is checked before multiplication; production identity scans charge soldiers, bases and cleanup. No native expectations, fixture strictness or allocation/work thresholds were relaxed. Branch 4c remains open; branch 4d and later-branch boundaries are unchanged.

Cycle validation: all 2,749 root tests pass with zero failures/skips after the soldier production batch audit fixes, including native fixture checksums, all 19 private save classifications, composed 72-hour scenarios and unchanged population/allocation gates. Full formatting verification and regenerated code-map/diff checks pass. The reference checkout is clean at the pinned revision; useful captures remain and the compiled probe object was removed. No unexpected access denials occurred. Branch 4c headless closure remains open; branch 4d is untouched.

Branch 4c ordered UFO escort movement: pinned Ufo::think/MovingTarget::move traces now cover 54 chain, ring and self-target steps across all six saved list orders. Movement preflight uses an iterative per-tick position prefix, following earlier actors after movement and later actors before movement; no recursive graph traversal or speculative RNG is needed. Saved-chain guards are removed, while craft combat/deployment guards remain. Fresh/cache fixtures reload before and between every step, and large escort graphs refuse atomically at the existing work limit. Reference: GeoscapeState::time5Seconds and ufoHuntingAndEscorting, Ufo::think, MovingTarget::move. Newborn escort composition and terminal-arrival fallthrough are subsequent slices. Branch 4d is untouched.

Branch 4c newborn escort chains: the ordinary-wave guard for imported hunting/escort leaders is removed. Eight new pinned AlienMission::spawnUfo traces establish that leader flags never change first-non-hunter selection, relocation, original waypoint backup or full-speed attachment, including grounded sources. Four fresh/cache composed cases cover ordinary, hunting, chained and self-target leaders with reloads before birth, after birth and after the following movement tick. Existing no-potential-craft and newborn resource/pursuit guards remain until those contexts can be projected. Branch 4d and later-branch boundaries are unchanged.

Branch 4c terminal escort forecasts: the ordered position prefix now stops after a native terminal arrival, retaining later actors at their old coordinates. Site-conversion suppression invalidates the prefix because it can change dormancy into destruction. Six new pinned list-order traces preserve every prior oracle row. The combat capability check now follows the same saved-order terminal fallthrough: a skipped later hunter remains unchanged, then refuses atomically on the following tick; an earlier hunter still blocks the entire tick. Four fresh/cache boundary fixtures and six combined focused cases pass with transition reloads. Unsupported crash-state validation remains intact. Reference: GeoscapeState::time5Seconds destroyed-UFO return, Ufo::think and MovingTarget::move. Branch 4d and later-branch ownership are unchanged.

The escort batch audit reproduced and fixed two gaps: the lifecycle fixture also owns a checksum for the shared world probe (a fresh native recapture proves its oracle is unchanged), and ordered escort prefixes must charge linear mission lookups for takeoff forecasts. Two fresh/cache resource regressions previously advanced a tick and now refuse atomically at the unchanged work limit. No native oracle rows, malformed-state checks or performance limits were weakened. Branch 4c remains open; 4d is untouched.

Escort cycle validation: all 2,763 root tests pass with zero failures/skips after the audit fixes, including native fixture checksums, all 19 private save classifications, composed 72-hour scenarios and the unchanged population/allocation gates. Full formatting verification, code-map and diff checks pass. The pinned C++ checkout is clean. Useful captures remain; the three compiled probe objects were removed. No unexpected access denials occurred. Branch 4c remains open; branch 4d is untouched.

Branch 4c hourly hunter population forecasts: a production-owned internal operation bounds craft births with the pinned Production::step/getAmountProduced completion count and shared fallback staffing. Infinite queues now reserve only possible births in the current hour instead of triggering a blanket finite-queue guard; finite queues also stop reserving their lifetime quantity. Existing and due incoming craft participate without predicting random continuation or generating actors. The existing native production-progress rows cover both valid time values, spent/assigned combinations and finite/infinite queues, with fresh/cache reloads around the creation tick. Large birth/retarget combinations retain the unchanged work-limit refusal. Branch 4d is untouched.

Branch 4c delayed craft population boundary: positive manufacture transfer delays keep births out of the current tick's hunter candidate population, because hourly arrivals have already run. The native productionCraftBirthRows establish immediate versus delayed delivery; six fresh/cache composed cases pair 1,001 possible craft births with 1,001 new hunters. Immediate births retain an atomic work-limit refusal; one/three-hour births remain pending, permit the creation tick and survive reload with unchanged timers and craft identities. Production's own total-population/transfer guards remain in force. No RNG or actors are generated by the forecast. Branch 4d is untouched.

Branch 4c shared hunter forecasts and candidate work: common servicing/transfer/production inputs are projected and charged once per tick, while each hunter charges actual craft candidates plus base and mission scans. Four delayed-birth continuation fixtures reproduced false following-tick refusals and now advance after reload with pending transfers unchanged. Empty-base scans also reproduced a missing work bound; saved and newborn hunter populations now refuse atomically, including the creation tick. Native selection/order semantics and all existing work/allocation limits remain unchanged. Newborn work reserves both actor and base/mission visits. Reference: GeoscapeState::ufoHuntingAndEscorting and existing strategic-hunting traces. Branch 4d is untouched.

Hunter population cycle validation: all 2,775 root tests pass with zero failures/skips, including pinned fixture checksums, 19 private save classifications, composed 72-hour scenarios and the unchanged allocation gates. The batch audit checked hourly completion bounds, fallback staffing, transfer arrival order, common-input charging and candidate scans; no additional confirmed defect was found after the three slice fixes. Full formatting verification and code-map/diff checks pass. Branch 4c remains open and 4d is untouched.

Branch 4c escort ring endurance: four fresh/cache cases compare 72 hours of single-step, batched and hourly-reloaded simulation in forward/reverse saved order. Opening positions match the pinned Ufo::think/MovingTarget movement rows; every hourly checkpoint validates live counts and stable target links. No actors are deleted or duplicated across reload. Reference: GeoscapeState::time5Seconds, Ufo::think and the strategic-world escortMovement fixture. All four checks pass; branch 4d and later-branch boundaries are unchanged.

Branch 4c populated escort allocation: 8/32/128-actor forward/reverse rings now record cold and warm ticks plus transition reloads. Measurement reproduced 2.47 KB per additional UFO/tick from repeated immutable preflight projections. Tick-local source-identity memoization and reusable prefix buffers reduce this to at most 883 B in the measured cases, guarded by a new 1 KB actor/tick budget. Site-suppression and each new preflight clear both caches. All eleven native movement, terminal, endurance and allocation checks pass; no existing ordinary-flight limits changed. Reference: Ufo::think/MovingTarget::move and GeoscapeState saved-order flight. Branch 4d is untouched.

Branch 4c successive escort cleanup: three new pinned Target::~Target/resetOriginalDestination chain traces distinguish direct repair from transitive links and preserve original waypoints and escort speeds. Four fresh/cache forward/reverse cases follow terminal arrival, retained dead actors, next-tick cleanup, all subsequent deletions and reloads around every transition; no deleted actor or dangling link returns. The deletion oracle compares stable links and waypoint coordinates, since live UFO target coordinates depend on the separate movement pass. All four checks pass. Reference: Ufo::~Ufo, Target::~Target, Ufo::resetOriginalDestination and GeoscapeState::time5Seconds. Branch 4d and later-branch boundaries remain unchanged.

Escort closure batch validation: all 2,784 root tests pass with zero failures/skips, including native checksums, all 19 private save classifications, ordinary and escort 72-hour runs and populated allocation checks. The audit reviewed memo source identity, per-tick/site-suppression invalidation, terminal fallthrough, direct follower repair and saved-order persistence; no further confirmed defect was found. Full formatting and code-map/diff checks pass. The pinned reference remains clean. Useful captures and measurements remain; the compiled hunting object was removed. Branch 4c remains open; branch 4d is untouched.

Branch 4c saved idle escorts with UFO followers: the newborn leader guard now permits this formation when ordinary-arrival and craft-pursuit dependencies are absent. Six new pinned movement rows compose native idle attachment with an appended leader and an existing follower in both saved orders. Four fresh/cache cases compare exact positions, speeds and stable links with reload after every step. All 68 escort spawn/endurance checks pass, including unchanged affected-tick atomicity and movement work limits. The obsolete follower scan is removed; craft pursuit and original-waypoint arrival guards remain. The shared lifecycle probe was recaptured with unchanged oracle and refreshed input checksum. Branch 4d is untouched.

Branch 4c escort detection VM acceptance: four fresh/cache forward/reverse rings execute native-bound isEscorting/isHunting/isHunterKiller reads and persisted instance tags through the real detection VM. Four successive half-hour boundaries and double save rewrites preserve flags, tags, hyperwave detection and live mission counts. Native strategic-detection-scripts tag rows and Ufo detection bindings provide evidence; all four cases pass. No script is executed speculatively by preflight. Branch 4d and later-branch boundaries are unchanged.

Branch 4c craft/escort endurance composition: fresh/cache cases compare 72 hours of stepped, batched and reloaded escort rings with real post-takeoff craft pursuit, patrol, recall, automatic fuel return and servicing. Stable UFO identity survives commands and save rewrites; live counts remain exact while detection runs through the real VM. Native checkup clears LowFuel on same-tick return, so the fixture proves later automatic fuel return through post-patrol servicing with depleted fuel. Both composed cases pass. Existing native pursuit, movement, fuel and detection fixtures back the participating operations. Branch 4d and later-branch guards remain unchanged.

The newborn-follower batch audit confirmed a portability defect in the hunting input manifest: it hashed a CRLF worktree template while Git committed LF according to .gitattributes. The template is now LF and a fresh native recapture proves every oracle field unchanged; its manifest hashes the committed bytes. An inventory of all manifest-owned text inputs found no other worktree/index size mismatch. Arrival/pursuit guards, follower ordering, VM execution and 72-hour craft composition were also reviewed without another confirmed gameplay defect. Branch 4d is untouched.

Newborn-follower batch validation: all 2,793 root tests pass with zero failures/skips after the committed-byte checksum fix, including all manifests, all 19 private save classifications, ordinary/escort/craft 72-hour runs and unchanged population/allocation gates. Full formatting and code-map/diff checks pass; the pinned reference checkout is clean. Useful native captures and measurements remain; compiled probe objects were removed. No unexpected access denials occurred. Branch 4c remains open; branch 4d is untouched.

Branch 4c zero-radar newborn hunters: native Ufo::insideRadarRange cannot select a craft at range zero, but GeoscapeState::ufoHuntingAndEscorting evaluates attraction first. Mission preflight now validates candidates through the existing pure craft forecast before permitting these births alongside active craft. Random constructor modes validate both outcomes without RNG; proofs are tick-local and include mission, UFO type and mode. Twelve fresh/cache cases cover ordinary/escort births, exact links, reloads and atomic malformed-attraction refusal. All 71 focused newborn/escort checks pass. Reference: Ufo.cpp, Craft::getHunterKillerAttraction, GeoscapeState.cpp and strategic-hunting selectionRows. Positive-radar retarget and later-branch guards remain; branch 4d is untouched.

Branch 4c newborn hunter transfer timing: the no-target proof now includes pending craft only on an hourly tick when their remaining timer is at most one hour. Six fresh/cache cases exercise ordinary and escort births before the hour, with two-hour transfers and with due one-hour arrivals. Previously blocked safe births advance, pending timers and identities survive reload, and due arrivals still refuse atomically before mutation. All 77 focused newborn/escort tests pass. Native evidence: Transfer::advance, GeoscapeState::time1Hour and strategic-hunting incomingCraftRows. No scheduling or later-branch boundary changes; branch 4d is untouched.

Branch 4c incoming craft pursuit timing: saved idle escorts can attach to newborn leaders while potential craft pursuers remain in transit beyond this tick. Six fresh/cache cases compose early/late leader creation with pre-hour and two-hour transfers, exact native-backed movement, stable targets and transition reloads. Due one-hour arrivals retain the atomic pursuit forecast refusal, including automatic patrol craft. All 83 focused newborn/escort tests pass. Reference: Transfer::advance, GeoscapeState::time1Hour/ufoHuntingAndEscorting and existing incoming/movement rows. Craft pursuit and waypoint dependencies still guard affected transitions; branch 4d is untouched.

The zero-radar/transfer batch audit checked native candidate arithmetic before radar selection, random and negative constructor modes, mode-specific proof keys across immediate waves, tick-local invalidation, hourly delivery/checkup ordering and loaded-escort pursuit dependencies. Ten additional fresh/cache cases cover valid mode zero, overflow in mode one/negative modes, both random outcomes and a later immediate wave with a different mode. All 28 newborn checks pass; no further confirmed gameplay defect was found. Due-arrival, craft pursuit, waypoint, work-limit and later-branch guards remain. Branch 4c remains open; branch 4d is untouched.

Zero-radar/transfer cycle validation: all 2,827 root tests pass with zero failures/skips, including fixture checksums, all 19 private save classifications, composed 72-hour runs and unchanged population/allocation gates. Full formatting verification and code-map/diff checks pass. The pinned C++ reference remains clean; no new native artifacts need cleanup in this batch and useful earlier captures remain. No unexpected access denials occurred. Branch 4c remains open; branch 4d is untouched.

Branch 4c grounded auto-patrol newborn proofs: existing pure servicing/fuel forecasts now establish that a positive-radar newborn has no eligible craft when auto-patrol craft remain grounded this tick. Native maintenanceRows back four fresh/cache ordinary/escort cases; refuelling that actually launches a craft retains the retarget refusal. Forecast candidate scans are bounded and shared; zero-radar arithmetic validation remains unchanged. Guard fixtures explicitly distinguish native ready-at-arrival craft from refuel-triggered relaunch. All 97 focused newborn/escort checks pass. Reference: Craft::checkup/refuel, GeoscapeState half-hour servicing and ufoHuntingAndEscorting. Branch 4d is untouched.

Branch 4c grounded craft pursuit proofs: saved idle escorts can attach to newborn leaders when the shared servicing forecast leaves every potential pursuer grounded. Two fresh/cache native maintenance cases preserve exact source movement and stable links through reload; refuel-triggered launches retain atomic pursuit refusal. Hidden, ignored and damaged airborne craft still count as pursuers. Incoming guard fixtures use native arrival/refuel semantics explicitly. All 99 focused newborn/escort checks pass. Reference: Craft::checkup/refuel, Transfer::advance, GeoscapeState::ufoHuntingAndEscorting and maintenance/movement rows. Branch 4d is untouched.

Branch 4c shared-stock newborn acceptance: four fresh/cache ordinary/escort cases compose all 18 pinned sharedFuelRows with future hunter births. Exact fuel, statuses and remaining item stocks match native ordered servicing whenever both craft stay grounded; any actual launch retains an atomic retarget refusal. Reloads before/after birth and following ticks preserve actor identities and avoid duplicate effects. All four checks pass, and the broader hunting run passed its other 457 cases; its four shared fixture failures were corrected to represent zero stock by absence, retaining strict input validation. Reference: Craft::refuel, GeoscapeState ordered servicing and strategic-hunting sharedFuelRows. Branch 4d is untouched.

Grounded-craft batch audit: reviewed forecast ownership, candidate versus pursuer eligibility, native checkup/refuel launch ordering, shared stock consumption, tick-local proof invalidation, constructor-mode keys and unchanged work limits. No additional gameplay defect was confirmed. The first root run reported a patrol allocation-gate failure in a path without mission births; three isolated reruns, all 23 craft-operation tests and the unchanged full root rerun passed. The allocation threshold and measurement code remain unchanged, and the non-reproduced observation is retained here for future audits. All 2,837 root tests pass with zero failures/skips, including fixture checksums, 19 private save classifications, 72-hour scenarios and population/allocation gates. Branch 4c remains open; branch 4d is untouched.

Grounded-craft cycle closure: full formatting verification and code-map/diff checks pass. The pinned reference checkout remains clean. Useful previous native captures remain; this batch generated no additional native probes requiring cleanup. No unexpected access denials occurred. All three slices and their batch audit are committed; branch 4c remains open and 4d is untouched.

Branch 4c existing-base grounded craft forecasts: alien-base hunting now uses the shared pure servicing/fuel forecast before treating a grounded automatic patrol craft as airborne. Native strategic-hunting maintenanceRows back fresh/cache cases for incomplete refuelling and actual launches, with exact fuel/status, timer persistence and atomic immediate-mission refusal. All 27 focused base-hunting tests pass. Reference: GeoscapeState::baseHunting/time30Minutes, Craft::refuel/checkup. Branch 4d remains untouched.

Branch 4c incoming base-hunting craft forecasts: due hourly arrivals now use native checkup plus shared half-hour refuelling before hunting eligibility. Fresh/cache cases cover partial fuel, refuel-triggered launches, ready-at-arrival craft and future transfers; safe arrivals persist while unsupported immediate hunting remains atomic. All 29 focused base-hunting tests pass. Reference: Transfer::advance, Craft::checkup/refuel, GeoscapeState::time1Hour/baseHunting and strategic-hunting incomingCraftRows. Branch 4d remains untouched.

Branch 4c newborn objective-base hunting forecasts: creation-tick hunting now applies shared servicing and due-arrival eligibility instead of assuming any automatic patrol flag launches a craft. Fresh/cache native maintenance cases cover existing and incoming craft, safe base births, exact fuel/status/timer reloads and retained live-supply-source refusal after an actual launch. All 40 objective-base tests pass. Reference: GeoscapeState::baseHunting/time30Minutes, Craft::checkup/refuel, Transfer::advance and maintenanceRows. Branch 4d remains untouched.

Base-hunting craft batch audit: reviewed all three slices against native caller order, eligibility, due-transfer boundaries, shared-stock ownership, tick-local forecast reset and work limits. No confirmed defect found. All 2,843 root tests pass with zero failures/skips, including manifests, private save classifications and existing endurance/allocation gates. Branch 4c remains open and 4d untouched.

Branch 4c spatial newborn hunter proofs: ordinary positive-radar births may proceed when every eligible forecast craft is outside every bounded spawn origin. Inclusive native radar edges remain guarded, uncertain areas use conservative radii and checked work, and proofs include the wave index. Native capture adds distant and negative-range selection cases with all prior properties unchanged. Six focused checks pass for native selection, point/regional fresh/cache births, transition reloads and nearby refusal; fixtures assert their distinct compiled regions. Reference: Ufo::insideRadarRange, GeoscapeState::ufoHuntingAndEscorting, AlienMission::spawnUfo/getWaypoint. Branch 4d untouched.

Branch 4c spatial newborn escort proofs: positive-radar escorts use the native first saved non-hunter leader origin before testing craft separation. With no saved leader, prior immediate-wave births retain a source forecast guard. Fresh/cache cases place craft near nominal spawn origins but far from the stationary saved leader, verify stable escort identity and transition reloads, and retain atomic refusal near the actual leader. Guard fixtures now explicitly colocate eligible craft and leaders. Reference: AlienMission::spawnUfo and strategic-hunting spawnEscortRows/selectionRows. Branch 4d untouched.

Branch 4c negative-radar newborns: native negative ranges cannot select any craft, including colocated targets. Preflight now permits these ordinary/escort births while validating attraction for every possible constructor mode and retaining signed stat range checks. Fresh/cache cases cover minus one and int-min ranges, stable links/reloads and atomic attraction overflow; all 115 newborn/escort checks pass. Reference: Ufo::insideRadarRange and newly captured strategic-hunting selectionRows variant eight. Branch 4d untouched.

Spatial radar batch audit: reviewed inclusive radar boundaries, conservative region radii, actual saved leader relocation, prior-wave leader guards, signed range/mode validation, wave-specific proof keys, reset and bounded scans. No confirmed issue found in these three slices. All 2,855 root tests pass with zero failures/skips; committed fixture bytes match manifest hashes and prior native rows remain unchanged. Code-map/diff checks pass. An older base-hunting escort-fuel forecast risk is queued for a deterministic reproduction in the next cycle. Branch 4c remains open; 4d untouched.

Branch 4c existing-base ordered escort fuel: reproduced a preflight bypass with an undetectable leader refuelling/relaunching before its visible escort consumes fuel. The old saved-speed projection marked the escort low-fuel, then runtime entered unavailable immediate hunting after mutation. Escort candidates now use the shared ordered forecast; both fresh/cache reproductions stop atomically and all 31 base-hunting tests pass. Native evidence: Craft::getFuelConsumption/consumeFuel, GeoscapeState::time10Minutes/baseHunting and strategic-hunting escortFuelRows. Branch 4d untouched.

Branch 4c newborn base ordered escort fuel: reproduced the same hidden-leader relaunch mismatch during objective-base creation, where runtime reached unsupported live-source supply hunting after the base birth. Active escorts now use the shared ordered craft forecast for newborn-base eligibility as well. Both fresh/cache reproductions stop before mutation; all 73 existing/newborn base-hunting tests pass. Reference: GeoscapeState::baseHunting/time10Minutes, Craft fuel methods and strategic-hunting escortFuelRows. Branch 4d untouched.

Branch 4c zero-chance hunting compositions: existing and newborn bases skip speculative craft/stock forecasts when detection chance cannot generate a mission, retaining timer range checks and live native percent-zero draws. Fresh/cache cases reproduce the former false production-stock guard, compare actual servicing/production with an independent world-free control, verify persisted timers and keep positive-chance stochastic guards. All 77 base-hunting/objective-base tests pass. Reference: GeoscapeState::baseHunting, native RNG::percent behavior and strategic-alien-base-hunt-timer zeroChance row. Branch 4d untouched.

Ordered-fuel/zero-chance batch audit: confirmed and reproduced absent forecast entries when native zero-quantity production inputs remove an airborne escort before hunting. Existing/newborn base consumers now skip removed actors, including grounded entries, rather than throwing or falling back to deleted state. The audit also restored bounded craft-population accounting for zero-chance runtime scans, with a tick-local shared population bound. Fresh/cache removal/reload cases and positive/zero-chance work-limit cases pass; all 82 focused tests pass. Native evidence: Production::startItem, Target destructor/follower repairs, GeoscapeState::baseHunting and deletedCraftFollowerRows. These fixes are an additional audit commit. Branch 4c remains open; 4d untouched.

Three-cycle closure: all nine 4c slices and three batch audits are committed, including an additional commit for confirmed deleted-candidate and work-bound findings. All 2,868 root tests pass with zero failures/skips, including checksums, all 19 private save classifications, 72-hour scenarios and population/allocation gates. Full formatting verification and regenerated code-map/diff checks pass. The pinned C++ checkout is clean. Useful native source/executable/capture outputs remain; the rebuilt hunting object was removed. A self-caused overlapping test/build executable lock was reported and resolved by waiting; no sandbox access failure or host configuration/ACL change occurred. Branch 4c remains open; in-range newborn hunting and immediate UFO-generating base missions remain guarded. Branch 4d is untouched.

Branch 4c creation-time ordinary hunter pursuit: airborne births now use the shared servicing/production/fuel forecast and bounded spawn regions to prove that every possible eligible target remains beyond the first movement step. Runtime retains native saved-order attraction selection, random constructor modes, hunt speed, waypoint backup, revelation and alerts; preflight draws no RNG and allocates no actor identity. Fresh/cache fixtures compare pinned selectionRows/retargetRows, transition reloads, due craft arrivals and shared fuel relaunches. Colocated births retain the precise branch 5 combat refusal, and noAlert hunters reserve contact markers because hunting still reveals them. All 481 hunting/newborn/escort checks pass. Reference: AlienMission::spawnUfo, GeoscapeState::time30Minutes/time10Minutes/ufoHuntingAndEscorting/time5Seconds, Ufo hunting destination methods and Craft fuel/attraction. Escort candidate births and branch-wide closure remain subsequent work; 4d is untouched.

Branch 4c creation-time escort hunter pursuit: candidate-bearing escort births use the native first saved ordinary leader origin, then may switch to craft hunting while retaining their original trajectory waypoint. Combat reach uses initialized escort speed when huntSpeed is nonpositive, rather than the nominal trajectory speed. The pinned capture adds the negative hunt-speed transition with all previous rows/properties unchanged. Ten fresh/cache constructor-mode and retained-speed cases reproduce the old guard and verify targeting, movement, reloads and atomic combat refusal. Colocated servicing/arrival fixtures now retain the branch 5 reason. Prior immediate waves without a saved leader remain a specific source-forecast boundary. Reference: AlienMission::spawnUfo escort initialization, Ufo::setTargetedXcomCraft/setEscortedUfo and GeoscapeState retarget/movement ordering. Branch 4d remains untouched.

Branch 4c loaded escort craft dependencies: attaching a saved idle escort to a newborn leader no longer blocks unrelated airborne, ignored/hidden, incoming or refuel-relaunched craft. A bounded iterative walk identifies direct and transitive UFO escort dependencies, including cycles, before applying the existing pure production/fuel forecast to affected pursuers. Deleted or returning actors cannot resurrect a dependency; actual active pursuit retains its specific creation-tick boundary. Eight fresh/cache chain/cycle/patrol fixtures reproduce the false guard; existing hidden/ignored and hourly/refuel cases now verify both permitted independence and atomic dependent refusals. All 77 escort compatibility checks pass with transition reloads. Reference: GeoscapeState::ufoHuntingAndEscorting/time5Seconds, Craft::think and existing spawnEscortSequenceRows/movement fixtures. Ordinary waypoint arrival and affected craft pursuit forecasts remain subsequent work; 4d is untouched.

Creation-time hunting batch audit: confirmed and reproduced proof reuse across two immediate births in the same wave. The first possible ordinary birth can initialize a later escort at full speed; its predecessor's no-leader trajectory-speed proof is insufficient. Tick-local proofs now include the within-wave UFO counter as well as mission, wave, type and mode, and both fresh/cache regressions refuse before mutation. Reviewed conservative spherical bounds, inclusive radar versus combat reach, nonpositive hunt-speed preservation, all constructor modes, noAlert marker capacity, saved-order targeting, follower chains/cycles and forecast purity. All 2,898 root tests pass with zero failures/skips after the fix, including native checksums, all 19 private save classifications, endurance and populated allocation gates. Code-map and diff checks pass; committed fixture bytes match the manifest. The native object was removed, while useful capture/source/executable artifacts remain and the pinned reference is clean. Branch 4c remains open; 4d untouched.

Branch 4c immediate escort source envelopes: creation-time hunting now includes first possible ordinary leaders from earlier validated waves and births in the same wave. Saved-order selection retains earlier stochastic origins until the first guaranteed ordinary birth; the no-leader outcome preserves nominal position/speed. Bounded region envelopes and both initialization-speed outcomes are proved without RNG or identities. Eight fresh/cache point/regional cases verify retargeting from a prior leader beyond the nominal radar footprint; 32 deterministic seeds in each cache case cover all four two-birth constructor outcomes, marker capacity and reloads. The previous audit regression still refuses atomically, now with the precise combat reason. All 155 newborn/escort checks pass. Reference: AlienMission::spawnUfo, Ufo initialization/retarget methods and pinned spawnEscortSequenceRows/constructorRows. Branch 4d untouched.

Branch 4c newborn leader pursuit: direct and transitive craft pursuers may advance when conservative movement envelopes prove they cannot reach a saved escort after its creation-tick attachment. The proof includes both idle/full-speed outcomes and bounds the reference latitude/longitude Euler step, falling back to the whole globe near a pole; it draws no RNG or identities. Four fresh/cache distant-pursuit cases verify actual craft movement, stable target coordinates and transition reloads, while colocated dependencies remain atomic refusals. All 81 escort compatibility checks and 12 focused pursuit cases pass. Reference: GeoscapeState::time5Seconds/ufoHuntingAndEscorting, Craft::think, MovingTarget::move and pinned escort spawn/movement traces. Branch 4d untouched.

Branch 4c cancelled idle-escort arrivals: a bounded flight-only projection identifies the first guaranteed ordinary point leader in a due score/space mission. Saved escorts that would otherwise reach an ordinary, terminal or landing waypoint now forecast the native attachment before all arrival, follower and craft checks, without allocating future identities or changing radar script inputs. Six fresh/cache arrival cases and four added ordered-follower cases match existing pinned attachment/movement evidence with transition reloads. Uncertain constructor/area cases retain precise guards, and a cancelled terminal arrival cannot hide same-tick craft interception. All 96 escort checks pass. Reference: AlienMission::think/spawnUfo, GeoscapeState::ufoHuntingAndEscorting/time5Seconds, Ufo::setEscortedUfo and MovingTarget movement. Branch 4d untouched.

Escort-source/pursuit/arrival batch audit: reviewed first-leader constructor/order outcomes, flight-only projection lifetime, radar script separation, original waypoint backup, terminal fallthrough, craft interception and conservative Euler/pole bounds. A suspected prior-birth overflow was disproved: centralized mission-counter preflight already refuses it before mutation; two fresh/cache regressions record that boundary. No confirmed gameplay defect remained. All 2,929 root tests pass with zero failures/skips, including native checksums, all 19 private-save classifications, endurance and populated allocation gates. Committed hunting fixture bytes match their manifest; code-map/diff checks pass and the pinned C++ checkout is clean. Branch 4c remains open; 4d untouched.

Branch 4c deterministic point-leader pursuit: the flight-only attachment projection now applies to every eligible saved idle escort, beyond cancelled waypoint arrivals. Direct/transitive stationary pursuers remain safely separated from the moved target; nearby fast craft use the precise branch 5 interception guard. Hidden/ignored pursuers retain live links, and radar loss reports the actual moved source before switching the craft to patrol. Eight new fresh/cache cases cover stationary pursuit, contact-loss reloads and uncertain-leader atomic refusal. All 106 escort checks pass; the unchanged work limit now refuses a large future formation in the earlier escort-position preflight. Reference: native retarget/list-order movement and Craft contact-loss handling at the pinned commit. Branch 4d untouched.

Branch 4c base-origin leader forecasts: guaranteed ordinary leaders now project linked alien-base position overrides for regional and Earth operations. The shared origin helper retains native first-leader order and race-specific escort speed. Four fresh/cache fixtures exercise both operation modes with both races, actual pursuit coordinates and reloads; the same cases verify daily source self-destruction interrupts the mission, consumes no future UFO identity and creates no phantom leader. All 139 escort/operation-base checks and four expanded transition cases pass. Evidence: strategic-mission-origin rows and AlienMission::spawnUfo ordinary base override at the pinned reference. Branch 4d untouched.

Branch 4c objective-aware first leaders: flight projections now cover ordinary births in infiltration, base, site and supply missions as well as score missions. Suppressed no-object supply waves are skipped, and objective supply constructors force the ordinary-leader outcome even with a 100-percent hunter rule. Retaliation remains outside this proof. Twelve new fresh/cache cases exercise all four objectives, suppressed versus immediate later supply leaders, stable links, untouched site/base state and transition reloads. The 134 escort/objective checks and four added suppression cases pass. Evidence: AlienMission::spawnUfo/think, strategic-mission-origin supplyRows and existing constructor/escort movement traces. Branch 4d untouched.

Point/base/objective leader batch audit: reviewed deterministic versus uncertain constructors, first-leader order, source overrides and daily deletion, forced/suppressed supply births, terminal fallthrough, craft interception and bounded work. Two added fresh/cache VM regressions verify half-hour detection reads idle state before attachment and later reads persisted escort state; future flight proofs never appear in script inputs or saves. No confirmed gameplay defect was found. All 2,953 root tests pass with zero failures/skips, followed by both added VM cases, including existing private corpus/endurance/allocation/checksum gates. Code-map/diff checks pass and the pinned reference is clean. Branch 4c remains open; 4d untouched.

Branch 4c production price time forecasts: the existing read-only price VM now runs in hourly production preflight with the next tick's time and day count, unchanged campaign difficulty/month count and native before/current/after script order. VM instructions share the one-million-work budget with production, and event counts retain the existing cap. Eight fresh/cache item/global VM cases compare controlled native caller-context accounting traces at hourly/daily boundaries and reload before/after delivery. Daily research queries retain a precise refusal pending the next slice. Evidence: RuleItem::getSellCostAdjusted, Production::step and SavedGame time/day/month/difficulty bindings at the pinned reference; the native fixture explicitly uses controlled pure script callbacks rather than claiming C++ VM execution. Branch 4d untouched.
Branch 4c production price research forecasts: actually executed isResearched queries now use bounded guaranteed/possible post-daily research sets. Known completions and unchanged answers execute; consumed uncertain random-reward answers refuse before mutation. Null references and unexecuted query branches keep native false/control-flow behavior. The native context fixture now covers research with actual C# VM execution for item/global events, debug-mode exclusion and daily completion ordering; eight further fresh/cache cases verify uncertainty, unrelated/dead/null queries, transition reloads and RNG equivalence against the same world with a constant callback. Evidence: SavedGame::isResearchedScript and GeoscapeState daily research before Production::step at the pinned reference. Branch 4d untouched.
Branch 4c shared scripted-sale acceptance: ordinary production now validates prices in the future hourly/daily context before mutation, reserves exact read-only scripted accounting values and bounds repeated VM delivery work across requested units. This closes a reproduced world-free divide-by-zero after time/progress mutation and a repeated-script budget gap. Ordered global before/current/after price callbacks now compose with paid continuation, shared/cross-base funds, delivery delays, refuelling and hunter targeting against the existing native sale traces. Eight failure/work-limit cases and four near-64-bit accounting cases cover fresh/cache hunting and ordinary campaigns; existing 14 overflow refusals remain intact. Evidence: RuleItem::getSellCostAdjusted, Production::step and SavedGame::setFunds at the pinned reference. Branch 4d untouched.
Production price batch audit: reviewed native price inputs, null/debug research semantics, daily/hourly fallthrough, before/current/after event order, shared accounting, scalar widths and aggregate VM/research work. Four additional fresh/cache regressions prove inactive projects never invoke their price script and a due midnight caller uses the valid future branch rather than today's failing branch. No further confirmed defect was found. All 2,983 root tests pass with zero failures/skips, plus four added audit cases; committed native fixture checksums and captured output match, code-map/diff checks pass and the pinned C++ checkout remains clean. The disposable native object was removed while the useful capture/executable were retained. Branch 4c remains open; 4d untouched.
Branch 4c ordinary craft consumption before base hunting: loaded alien-base preflight now consults bounded production removal types and the shared post-production craft forecast for affected independent airborne craft. Deleted craft no longer trigger an unavailable hunting-mission guard; surviving craft retain it. Four new fresh/cache independent-craft variants reproduce the stale actor and verify consumed/surviving outcomes, native hunt timer progression and transition reloads; all 40 alien-base hunting checks pass. Evidence: Production::startItem craft removal followed by GeoscapeState::baseHunting/updateActiveCrafts, pinned production input/deletion and alien-base hunt timer traces. Unrelated types keep the local fuel path. Branch 4d untouched.
Branch 4c newborn-base craft consumption: creation-tick base hunting now shares the bounded production removal-type proof used by loaded bases. Independent airborne craft removed by an earlier production continuation cannot generate phantom hunting missions or reserve their counters; surviving craft retain precise supply/source boundaries. Four new fresh/cache variants reproduce and close the stale ordinary-craft guard, verify the newborn timer and transition reloads, and pass with all 90 loaded/newborn base hunting checks. Evidence: GeoscapeState::time1Hour production precedes time30Minutes base creation and time10Minutes::baseHunting; pinned base-objective, craft consumption and hunting timer fixtures. Branch 4d untouched.
Branch 4c correlated first-leader origins: a saved idle escort may now use a flight-only proof when stochastic earlier ordinary constructors and the first guaranteed later ordinary birth all share one exact point. The proof preserves native first-leader order without choosing identities or drawing RNG; differing points and a chain without a guaranteed leader retain precise guards. Four new native retarget traces pin same/different origins and both earlier constructor outcomes. Four fresh/cache composed cases use 32 persisted seeds, verify both outcomes, stationary pursuit coordinates and reloads; all 128 escort checks pass. Evidence: AlienMission::spawnUfo, constructor flags, GeoscapeState::ufoHuntingAndEscorting and existing ordered movement fixtures at the pinned reference. Branch 4d untouched.
Craft consumption/correlated leader batch audit: reviewed native production-before-hunting order, affected-type scoping, shared forecast lifetime, surviving and deleted candidates, newborn timer/counter reservations, stochastic first-leader ordering and guaranteed point proofs. Two additional fresh/cache cases exercise all 32 seeds with a fast pursuer and retain the precise atomic branch 5 interception guard. No additional confirmed defect was found. All 3,001 root tests pass with zero failures/skips, including private classifications, endurance/allocation and checksum gates. Committed native inputs match their manifest and capture; code-map/diff checks pass, the pinned reference is clean and the disposable native object was removed. Branch 4c remains open; 4d untouched.
Branch 4c exact daily research discoveries: the shared research preflight can now return an immutable completed-topic set while bypassing its stock-only shortcut when discoveries are requested. It preserves ordered deterministic bonuses, protected/sequential rewards, lookup and automatic discovery, disabling/re-enabling and cross-base cancellation without RNG or campaign mutation. Six fresh/cache fixture cases cover all pinned reward/discovery rows plus a stock-free completion, compare projected sets with actual daily progression and reload transitions; all pass. Evidence: GeoscapeState::time1Day and SavedGame::selectGetOneFree/addFinishedResearch at 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15, strategic-hunting researchRewardRows/researchDiscoveryRows. Price callers are connected in the next slice; genuine random alternatives retain the existing refusal. Branch 4d untouched.

Branch 4c exact research-dependent prices: consumed daily isResearched queries now use the shared exact completed-topic projection before the existing guaranteed/possible fallback. Deterministic free/protected/sequential rewards, duplicate candidates, automatic/lookup discoveries and disabling/re-enabling execute in actual post-daily order; genuine alternatives still refuse atomically. Projection work and immutable-set creation share the production budget, and repeated queries reuse the result. Six new fresh/cache item/global price cases compose all native reward and discovery rows, verify accounting, unchanged RNG against constant callbacks and transition reloads; all 16 focused price/discovery/uncertainty checks pass. Evidence: SavedGame::isResearchedScript/selectGetOneFree/addFinishedResearch, GeoscapeState daily-before-hourly ordering and the pinned strategic-hunting research fixtures. Branch 4d untouched.

Branch 4c research metadata price providers: buy/sell price VMs now expose the four reference-registered RuleResearch getters for cost, points, lookup and needed item. Immutable linked metadata retains null outputs and signed scalar widths; item and research pointers are canonical across lookups and item inputs, fixing a reproduced pointer-comparison failure. Twenty-four newly captured native getter traces import the pinned methods with collaborator field renaming and explicitly exclude the C++ VM; prior oracle rows are unchanged. Four fresh/cache item/global VM cases exercise every trace, pointer identity, hourly hunting composition and transition reloads; all 20 focused getter/research price checks pass. Evidence: RuleResearch.cpp/h ScriptRegister/getters, RuleResearch::afterLoad, Mod rule readers and the verified script API catalog at the pinned reference. Branch 4d untouched.

Research-price batch audit: reviewed ordered daily discoveries/rewards, exact and conservative research answers, forecast purity and work limits, linked metadata, native null/default/sentinel rules, pointer identity and before/current/after price execution. Confirmed and reproduced two additional provider defects: repeated getTime calls boxed different pointers and getRuleItem returned a defined STR_NONE item. A shared per-invocation clock reference and native sentinel exclusion fix both; eight fresh/cache item/global regressions and two extended atomic budget cases pass. Two new pinned clock-pointer traces preserve all earlier oracle rows. All 3,021 root tests pass with zero failures/skips, including private classifications, endurance/allocation and checksum gates; full formatting, code-map/diff and native input/output checks pass. The pinned C++ checkout is clean and the disposable native object was removed. The original timed window elapsed during interruption; this finishes the announced sixth cycle and stops. Branch 4c remains open; 4d untouched.

Branch 4c price item metadata: production and current price VMs now share the existing immutable RuleItem scalar readers and ordered category queries, including native signed values, defaults and nulls. Current-price callers use the same bounded before/current/after execution as future callers, and category scans charge the common work budget. Eight fresh/cache item/global VM scenarios exercise pinned itemReaders/category replacement fixtures, actual hunting and save transitions. Evidence: RuleItem.cpp ScriptRegister/readers/hasCategoryScript and strategic-detection-scripts at the pinned reference. Branch 4d untouched.
