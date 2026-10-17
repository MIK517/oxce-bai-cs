# Phase 6 ownership and fixture ledger

Reference: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. Implementation
branches: `codex/strategic-base-logistics`, `codex/strategic-base-readiness`,
`codex/strategic-research-production`, the branch 4a world foundation, and
`codex/strategic-world-operations` (4b, merged in `e10d96f`).
`codex/strategic-world-integration` (4c) owns remaining headless world simulation;
`claude/strategic-world-ui` (4d) owns UI and combined closure
under the 2026-09-26 scope revision. This is
an implementation/evidence ledger; bounded acceptance is recorded in their status files.
Checkpoint narratives are recorded once, in the subsystem status documents or the
[4c plan's progress](phase-6-world-integration-plan.md#progress); entries below link to them.

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
| Mission/arc/event scheduling, broader world lifecycles, pursuit and scripts | Branch 4c | `GeoscapeState`, `AlienStrategy`, mission/site/base/event classes and world script callers | Headless mission/arc/adhoc/event projections, initial/daily/isolated monthly scheduling, mission objectives/waves, sites/alien-base lifecycles, event history/countdowns, shields/hunting/escorts and craft pursuit execute in registered capabilities with precise transition guards. Initial entry and the composed 72-hour fixture are validated; integrated campaign months and due event payloads retain the branch 6 boundary. Detection providers are closed by a fixed binding ledger. Closed on 2026-10-11 with named restrictions (conditional pursuit/escorts, shared stochastic servicing/production, nonconvergent daily consequences); see [4c closure](phase-6-world-integration-plan.md#branch-4c-closure-2026-10-11). Globe/UI belongs to 4d; see [4d slices](phase-6-world-integration-plan.md#branch-4d-slices-approved-2026-10-11). |
| Globe controls and combined world acceptance | Branch 4d | `Globe`, craft/target UI and `GeoscapeState` | Indexed globe, target commands, notifications, explicit speed and save/load are implemented ([4d closure](phase-6-world-integration-plan.md#branch-4d-closure-2026-10-12), [ADR 0032](decisions/0032-geoscape-ui-intentional-differences.md)). |
| Interception and strategic deployment | Branch 5 | `DogfightState`, landing/deployment callers | S0-S9 implemented, S10 closing ([branch 5 plan](phase-6-interception-plan.md), [status](strategic-interception-status.md)); remaining named refusals are listed in [ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md). |
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

World-owned research consequences and shared diary persistence belong to 4c.
The `strategic-research-diary` native capture and ordered save/reload tests are
recorded in [world research status](strategic-world-research-status.md).

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

Recorded in [`phase-6-world-integration-plan.md`](phase-6-world-integration-plan.md#progress).

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

Recorded in [`phase-6-world-integration-plan.md`](phase-6-world-integration-plan.md#progress).

### Mission initialization, wave and hunting checkpoint (2026-10-03)

Recorded in `phase-6-world-integration-plan.md`: [Mission initialization, wave and hunting checkpoint (2026-10-03)](phase-6-world-integration-plan.md#mission-initialization-wave-and-hunting-checkpoint-2026-10-03).

### Ordinary mission lifecycle and contact checkpoint (2026-10-03)

Recorded in `phase-6-world-integration-plan.md`: [Ordinary mission lifecycle and contact checkpoint (2026-10-03)](phase-6-world-integration-plan.md#ordinary-mission-lifecycle-and-contact-checkpoint-2026-10-03).

Recorded in [`strategic-mission-script-status.md`](strategic-mission-script-status.md#additional-4c-cycle-ignored-ufo-contacts).

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

Recorded in [`strategic-world-ufo-site-conversion-status.md`](strategic-world-ufo-site-conversion-status.md#selected-objective-ufo-conversion-to-mission-sites).

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

Recorded in [`strategic-world-hunting-status.md`](strategic-world-hunting-status.md#ufo-hunting-and-escort-integration).

Combat arrivals stop atomically before mutation. Retargeting involving same-tick
grounded auto-patrol relaunches or airborne craft transfers retains specific
forecast guards. Escort chains/cycles and malformed originals fail intentionally.
Deletion scans are bounded before nested work. Future hunter wave creation remains
guarded until spawn/retarget forecasts compose; this slice enables loaded actors.
The headless gate remains open and branch 4d is untouched.

Recorded in [`strategic-world-hunting-status.md`](strategic-world-hunting-status.md#ufo-hunting-and-escort-integration).

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

Recorded in [`strategic-world-hunting-status.md`](strategic-world-hunting-status.md#saved-idle-escort-and-newborn-leader-composition).

Recorded in [`strategic-mission-script-status.md`](strategic-mission-script-status.md#additional-batch-audit-2026-10-03).

- 4c base-objective cycle: pinned AlienMission::think final-base and landing-base
  blocks, chooseAlienBaseType, Texture::getRandomDeployment and MissionArea
  defaults. The 432 search and 384 landing rows cover ordering, dynamic selection,
  technical regions, fake-water/fallback and conversion precedence. Final-wave
  and accepted landing creation now execute through registered world handlers,
  with persistent birth identity, score/marker/population/work reservations and
  fresh/cache transition reloads. Operation-base creation shares the birth helper.
  The objective-base capture now includes the 24 native post-think revelation
  rows; see [caller and option evidence](strategic-mission-script-status.md#mission-think-base-revelation-2026-10-09).
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

Recorded in [`strategic-mission-script-status.md`](strategic-mission-script-status.md#base-objective-search-evidence).

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

Recorded in `strategic-world-operations-status.md`: [Detection tags and research cycle audit (2026-10-04)](strategic-world-operations-status.md#detection-tags-and-research-cycle-audit-2026-10-04).

## Detection loops and complete UFO statistics cycle audit (2026-10-04)

Recorded in `strategic-world-operations-status.md`: [Detection loops and complete UFO statistics cycle audit (2026-10-04)](strategic-world-operations-status.md#detection-loops-and-complete-ufo-statistics-cycle-audit-2026-10-04).

## Rule lookup and country detection cycle audit (2026-10-04)

Recorded in `strategic-world-operations-status.md`: [Rule lookup and country detection cycle audit (2026-10-04)](strategic-world-operations-status.md#rule-lookup-and-country-detection-cycle-audit-2026-10-04).

## Rule and mod detection scalar cycle audit (2026-10-04)

Recorded in `strategic-world-operations-status.md`: [Rule and mod detection scalar cycle audit (2026-10-04)](strategic-world-operations-status.md#rule-and-mod-detection-scalar-cycle-audit-2026-10-04).

## Linked rule tags and text metadata cycle audit (2026-10-04)

Recorded in `strategic-world-operations-status.md`: [Linked rule tags and text metadata cycle audit (2026-10-04)](strategic-world-operations-status.md#linked-rule-tags-and-text-metadata-cycle-audit-2026-10-04).

## Soldier and armor statistics cycle audit (2026-10-04)

Recorded in `strategic-world-operations-status.md`: [Soldier and armor statistics cycle audit (2026-10-04)](strategic-world-operations-status.md#soldier-and-armor-statistics-cycle-audit-2026-10-04).

## Effective craft detection statistics (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Effective craft detection statistics (2026-10-04)](phase-6-world-integration-plan.md#effective-craft-detection-statistics-2026-10-04).

## Craft condition detection readers (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Craft condition detection readers (2026-10-04)](phase-6-world-integration-plan.md#craft-condition-detection-readers-2026-10-04).

## Caller-specific detection preflight (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Caller-specific detection preflight (2026-10-04)](phase-6-world-integration-plan.md#caller-specific-detection-preflight-2026-10-04).

## Effective craft detection cycle audit (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Effective craft detection cycle audit (2026-10-04)](phase-6-world-integration-plan.md#effective-craft-detection-cycle-audit-2026-10-04).

## Unit rule lookup in detection scripts (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Unit rule lookup in detection scripts (2026-10-04)](phase-6-world-integration-plan.md#unit-rule-lookup-in-detection-scripts-2026-10-04).

## Linked Unit metadata (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Linked Unit metadata (2026-10-04)](phase-6-world-integration-plan.md#linked-unit-metadata-2026-10-04).

## Mod smoke reduction in detection scripts (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Mod smoke reduction in detection scripts (2026-10-04)](phase-6-world-integration-plan.md#mod-smoke-reduction-in-detection-scripts-2026-10-04).

## Unit lookup/linkage and smoke cycle audit (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Unit lookup/linkage and smoke cycle audit (2026-10-04)](phase-6-world-integration-plan.md#unit-lookuplinkage-and-smoke-cycle-audit-2026-10-04).

## Inventory rule lookup in detection scripts (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Inventory rule lookup in detection scripts (2026-10-04)](phase-6-world-integration-plan.md#inventory-rule-lookup-in-detection-scripts-2026-10-04).

## Inventory type and hand readers (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Inventory type and hand readers (2026-10-04)](phase-6-world-integration-plan.md#inventory-type-and-hand-readers-2026-10-04).

## Conventional inventory accessors and tick safety (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Conventional inventory accessors and tick safety (2026-10-04)](phase-6-world-integration-plan.md#conventional-inventory-accessors-and-tick-safety-2026-10-04).

## Inventory detection cycle audit (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Inventory detection cycle audit (2026-10-04)](phase-6-world-integration-plan.md#inventory-detection-cycle-audit-2026-10-04).

Formatting verification, code-map validation and diff checks pass.

## Item accuracy detection readers (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Item accuracy detection readers (2026-10-04)](phase-6-world-integration-plan.md#item-accuracy-detection-readers-2026-10-04).

## Raw item power, armor and waypoint readers (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Raw item power, armor and waypoint readers (2026-10-04)](phase-6-world-integration-plan.md#raw-item-power-armor-and-waypoint-readers-2026-10-04).

## Item handling flags and category membership (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Item handling flags and category membership (2026-10-04)](phase-6-world-integration-plan.md#item-handling-flags-and-category-membership-2026-10-04).

## Item detection metadata cycle audit (2026-10-04)

Recorded in `phase-6-world-integration-plan.md`: [Item detection metadata cycle audit (2026-10-04)](phase-6-world-integration-plan.md#item-detection-metadata-cycle-audit-2026-10-04).

Formatting verification, code-map validation and diff checks pass.

Branch 4c scheduling composition: pinned caller-order and initial-entry evidence in strategic-world-composition; dependent eligibility forecast now owns research grants/disables and variable/marker increases. Composed execution and initial entry remain pending; branch 4d and integrated month progression remain deferred.

Recorded in `phase-6-world-integration-plan.md`: [Item detection metadata cycle audit (2026-10-04)](phase-6-world-integration-plan.md#item-detection-metadata-cycle-audit-2026-10-04).

Branch 4c typed detection declarations: the pinned full native parser capture in script-declaration establishes 23 initializer acceptance/rejection cases via Script.cpp parseVariableImpl and ScriptBind.h BindSet/ArgSelector. Variable initialization now validates exact type/reference compatibility and editable-pointer direction, preserving native null pointer/tag alternatives. Invalid initializers fail during content compilation instead of corrupting typed VM/forecast state. Unit regressions reproduced all nine tested invalid declarations before the fix; native fixture comparison and fresh/cache base/craft reload scenarios cover valid copies feeding damage queries. Compiler/cache revision advances to 50. Remaining detection providers/forecasts and final closure audit keep 4c open; 4d is untouched. Validation: root dotnet test passes 2,254 tests with no failures/skips; formatting verification, fixture checksums, code-map and diff checks pass. Native capture was repeated with identical output. Twenty obsolete probe objects were removed; the executable and trace remain useful, and the reference checkout is clean.

Typed declaration slice audit confirmed a remaining native-overload gap: pointers to scalar/text/tag/Position value types were accepted with null initializers, although native BindSet/BindValue exposes only value assignment for them. Declaration type validation now rejects these pointer forms before emitting a program, also covering absent initializers. The native fixture expands to 30 cases and malformed RUL tests prove invalid detection scripts cannot publish ScriptsCompiled or RuntimeLinked content. Compiler/cache revision advances to 51. Legitimate reference/tag/text copies retain the existing fresh/cache/reload acceptance. Audit fix validation: root dotnet test passes 2,262 tests without failures/skips; formatting verification, fixture checksums, code-map and diff checks pass. All 30 native cases match the retained trace, the probe objects are removed, and the reference checkout is clean. No further confirmed issue was found in this slice. Branch 4c remains open and 4d is untouched.

Recorded in `phase-6-world-integration-plan.md`: [Item detection metadata cycle audit (2026-10-04)](phase-6-world-integration-plan.md#item-detection-metadata-cycle-audit-2026-10-04).

Recorded in [`strategic-world-hunting-status.md`](strategic-world-hunting-status.md#maintenance-before-hunter-selection).

Recorded in `phase-6-world-integration-plan.md`: [Item detection metadata cycle audit (2026-10-04)](phase-6-world-integration-plan.md#item-detection-metadata-cycle-audit-2026-10-04).

Recorded in [`strategic-world-hunting-status.md`](strategic-world-hunting-status.md#maintenance-before-hunter-selection).

Native flight-fuel setter ownership, registered stock refunds and pre-tick safety
are recorded in [the flight fuel correction](strategic-world-operations-status.md#flight-fuel-setter-correction-2026-10-09).

## Branch 5 interception ownership (S2, 2026-10-12)

Owners are branch 5 slices ([plan](phase-6-interception-plan.md)); evidence is recorded in
[strategic interception status](strategic-interception-status.md#s2--evidence-and-ownership-2026-10-12).

| Behavior/state | Owner | Reference | Evidence/status |
|---|---|---|---|
| Craft-weapon, craft and UFO dogfight rules; interception globals and difficulty overrides | S2 | `RuleCraftWeapon`, `RuleCraft`, `RuleUfo`, `Mod::loadFile` | Projected with reference defaults and layering (compiler/cache revision 61; damage ranges 62); `StrategicInterceptionRuleProjectionTests` |
| `dogfightAI`, `oxceRememberDisabledCraftWeapons` | S2 (read in S3) | `Options` | Campaign options with save round-trip and fixed user options; `StrategicInterceptionOptionsTests` |
| Dogfight step: distance, modes, weapons, projectiles, shields, tractor beams, UFO fire/escape, break-off, crash/destroy/forced landing, retaliation spawn, scores | S3 (state and step), S5 (world consequences) | `DogfightState`, `CraftWeaponProjectile`, `CraftWeapon`, `Craft`, `Ufo` | `strategic-dogfight` verbatim step oracle (18 scenarios); the S3 `Dogfight` engine replays it exactly (`StrategicDogfightOracleTests`) |
| Open interceptions, dogfight timer step, modes, minimize, weapon toggles; craft `pilots` list | S3 | `GeoscapeState::handleDogfights`, `DogfightState`, `Craft::getPilotList` | Implemented headlessly with forecast refusal of later consequences; `StrategicInterceptionTests` |
| Pilot experience, diary and daily dogfight cache | S5 | `DogfightState::awardExperienceToPilots`, `GeoscapeState::handleDogfightExperience`, `time1Day` | Owned and saved (`dailyDogfightExperienceCache`, diary `ufosShotDownTotal`/`ufosDamageTotal`); `StrategicInterceptionTests` |
| Interception world consequences: crash bookkeeping, `ufoShotDown`, wave interrupt, retaliation, activity, endings, crash followers | S5 | `DogfightState::update`, `Ufo::setDamage`, `AlienMission::ufoShotDown`, `SavedGame::findAlienMission` | Implemented with forecast overlays; race retaliation rules projected (revision 64); `StrategicInterceptionTests` |
| Destroyed craft loss, crew evacuation, `craftLostDogfight` | S5 | `GeoscapeState::time5Seconds`, `Craft::evacuateCrew`, `SavedGame::killSoldier` | Implemented; crew who fail the roll die into the memorial (`deadSoldiers`, campaign `DeadSoldiers`) |
| Interception start rules, waits and HK exclusivity | S4 | `GeoscapeState::time5Seconds`, `startDogfight`, `handleDogfights` | Implemented in time advance; focused tests (no native probe) |
| Landing confirmation and starting conditions | S6 | `ConfirmLandingState`, `GeoscapeState::time5Seconds`, `Globe::getPolygonTextureAndShade` | Queue, answers and starting conditions implemented; shade pinned by the `strategic-landing-shade` oracle; `StrategicLandingTests` |
| Deployment request (port-only `oxcePortDeploymentRequest`) | S6 (D4) | `ConfirmLandingState::btnYesClick`, `BattlescapeGenerator` inputs | Recorded, saved and taken once by the Phase 7 seam; `StrategicLandingTests` |
| `globeSeasons` option | S6 | `Options::globeSeasons`, `Globe::getSunDirection` | Campaign option with save round-trip; read by the landing shade |
| Base defense, missile damage, base loss | S7 | `BaseDefenseState`, `GeoscapeState::handleBaseDefense`, `Base::damageFacilities`, `Base::setupDefenses` | Defense rounds pinned by the `strategic-base-defense` oracle; assault arrival (including instant retaliation over the base), defense window, shot-down and battle-request outcomes, stored-vehicle defenders, nuclear destruction, base loss and `END_LOSE` implemented (`StrategicBaseAttackTests`); facility-damaging missiles and competing stored-vehicle ammunition refused ([ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md)) |
| Final mission (Cydonia) request | S7 | `SelectDestinationState`, `ConfirmCydoniaState` | `LaunchFinalMission` and the destination sidebar button; the final research is chosen as `Mod::loadAll` does (compiler/cache revision 69; `StrategicFinalMissionTests`) |
| Geoscape windows: landing, refusals, base defense, destroyed base, campaign end, battle request, interception windows, dogfight timer (`dogfightSpeed`) | S8 | `ConfirmLandingState`, `CraftErrorState`, `BaseDefenseState`, `BaseDestroyedState`, `DogfightState`, `GeoscapeState::think` | Implemented as popups and text-panel windows ([ADR 0032](decisions/0032-geoscape-ui-intentional-differences.md)); sprites, sounds, blobs and the round animation are Phase 8 polish |

Option inventory on these paths: gameplay-affecting `dogfightAI` (UFO-faster distance rule),
`oxceRememberDisabledCraftWeapons` (weapon toggle persists `CraftWeapon` `disabled`),
`oxceGeoSuppressLandingWithoutEquipment` and `oxceUfoLandingAlert` (already campaign options),
`craftLaunchAlways` (interception list, already an option); presentation-only `dogfightSpeed`,
`psiStrengthEval` (stat strings after experience), `oxceInterceptGuiMaintenanceTime`,
`oxceInterceptTableSize`, `oxceShowETAMode`, `oxceGeoSortCraftByDistanceToTarget`.
