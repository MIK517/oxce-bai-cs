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
