# Phase 6 world integration delivery plan

Approved 2026-09-26. Branch 4c: `codex/strategic-world-integration`, based on
`e10d96f` (merged branch 4b). Branch 4d: `codex/strategic-world-ui`, from main after
4c merges. Branch 5 follows 4d. Reference: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

## Branch 4c: tested headless slices

1. Pin reference evidence and inventory remaining rule, option, script and save ownership.
2. Project/link mission, adhoc, arc and event scheduling rules; revise the content cache.
3. Execute initial/daily/monthly scheduling and alien strategy in reference order.
4. Complete regional, deployment-only and immediate waves, objectives and operation bases.
5. Complete sites/alien bases, discovery, expiry, upgrades, evolution and retaliation cleanup.
6. Persist event requests inside gameplay transactions, generated history and countdowns.
7. Complete real detection providers, pursuit, ignored alerts, shields and hunting/escorts.
8. Close headless reference, save/corpus, multi-day and performance acceptance.

Each behavior includes exact C++ caller inspection, a small fixture before implementation,
unit and compatibility tests, snapshot/adapter coverage and matrix updates. Primary sources:
`GeoscapeState.cpp`, `AlienMission.cpp`, `AlienStrategy.cpp`, world target/entity classes,
corresponding Mod rules, and `Base.cpp`, `Craft.cpp`, `Ufo.cpp` detection bindings.

## Interfaces and boundaries

Use registered gameplay capabilities with centralized single-writer validation. Add target
dispatch using stable world identity, actionable world queries including player craft/bases,
and explicit selected-speed context (existing callers default to five-second mode).
Promote generated events, counters, retaliation links and script values through save-neutral
snapshots and the owned YAML adapter. Preserve unknown surviving entity fields.

Campaign-event consequences remain branch 6 by explicit user decision. Scheduling, history,
countdowns and interrupted-event cleanup belong to 4c; due payloads and instant-event
producers stop before the unavailable transition. Do not mark an unexecuted effect complete.
Research/manufacturing callers schedule through internal operations, not notification consumers.
Adhoc scheduling is implemented for fixtures and the later event caller. World-owned research
and counters execute at their actual call sites. Interception, deployment and base defense
remain guarded for branch 5; integrated monthly progression remains guarded for branch 6.
No speculative scripts, RNG rewinds or fabricated rollback are used for preflight.

## Branch 4d: globe and combined closure

Integrate an indexed globe into the existing campaign client, with original geography,
rotation/zoom, pointer selection and keyboard target navigation. Expose craft/target status,
unavailable reasons, confirmed dispatch, patrol/recall, lost-target feedback and notifications
through headless APIs. Pause editing/confirmation screens and preserve tick fallthrough.
Use explicit reference speeds, F5 save and confirmed F9 load; repair stale selection.
Verify structural rendering, input, SDL smoke and UFO/TFTD/modded scenarios. Full polish
remains Phase 8. Combined world acceptance closes here, with the named later-phase guards.

## Validation

Cover scheduling triggers/conditionals/labels/weights/history; all objectives and wave kinds;
site followers/UFO reactivation; base evolution/link cleanup; VM calls/script persistence;
pursuit/loss/fuel/speed context; malformed input and bounded work/populations. Save/reload
around transitions and test repeated rewrites, deleted sidecars and fresh/cache equivalence.
Use at least 72 hours of composed activity and isolated month-boundary world fixtures.
Reclassify all 19 staged saves with explicit horizons/stops; compare 8/32/128-UFO allocations.
Run root `dotnet test`, formatting verification, checksum tests and code-map verification.

## Progress

- Opening plan/scope commit: `b06cb39`.
- Event scheduling slice: runtime timer projections, transaction-owned research/manufacturing
  scheduling, generated history, countdowns/interrupted cleanup and due-payload guards.
  Extracted C++ fixture: `strategic-event`; public runtime/save tests:
  `StrategicEventSchedulingTests`. Persistent event sidecar identity follows ADR 0028.
  Validation: root `dotnet test` passes 1,043 tests with zero skips, including private
  saves and fixture checksums; formatting and code-map checks pass. The early Rosigma
  save advances 220 ticks to the mission-spawn guard instead of stopping at tick zero
  for event scheduling. Corpus totals remain six executable, five blocked and eight
  preservation-only over the existing thirty-minute horizon.
- UFO shield slice: flying initialization and recharge, race bonuses, clamping and
  fractional RNG draws; landed/takeoff and terminal-departure paths preserve reference
  ordering. Ordinary waves can spawn shielded UFOs. Evidence: `strategic-ufo-shield`,
  `StrategicWorldUfoShieldTests` and `WorldUfoShieldTests`.
- Explicit `CampaignTimeSpeed` context now accompanies time commands, advancement
  feedback, preflight and timed handlers. Existing constructors/deconstruction remain
  unchanged and default to five-second mode. Context is command-local; batch length
  does not choose it. Hunter-killer retargeting remains unimplemented.
- Current private continuation: eight executable for thirty minutes, three blocked,
  eight tactical preservation-only. Rosigma Middle and autogeoscape now continue;
  Early stops for mission-site lifecycle, and Beginning reaches the mission-spawn guard.
- Shield/speed validation: root `dotnet test` passes 1,060 tests with zero skips;
  formatting, code-map and fixture checksum checks pass.
- Pursuit slice: `DispatchCraftToTarget` validates visibility, stable UFO identity and
  launch eligibility; queries expose player bases/craft, command availability and
  target references. UFO pursuit follows the extracted C++ moving-target trace;
  lost contacts patrol and destroyed targets return followers before deletion.
  Craft escorts follow in base/craft order, use the mod's `escortRange` for fuel,
  and return after their leader reaches base. Capture resolves live target coordinates.
  Pure position/status preflight guards interception and landing before tick mutation,
  including UFO takeoff and terminal-departure fallthrough. Deployment starting-condition
  validation remains a precise branch 5 dispatch guard. The landing-suppression option
  is saved and honors fixed mod options. Focused pursuit tests pass fresh/cache and reload.
  Root validation passes 1,078 tests with zero skips; the private classification remains
  eight executable, three blocked and eight tactical preservation-only.
- Immediate-wave slice: the entire finite chain is preflighted before counters or RNG
  change. Supported no-object and ordinary UFO waves execute in reference order,
  including zero-count waves and the RNG draw for a zero timer. Positive timers that
  round to zero wait for the next half-hour call. All resulting UFO IDs, contact IDs,
  activity increments and live counts are reserved together. Work is bounded to 4,096
  wave steps per half-hour and a 100,000-UFO population. Reference: `strategic-wave-chain`;
  fresh/cache, reload, malformed-work and ID-limit tests: `StrategicWorldWaveChainTests`.
  Root validation passes 1,084 tests with zero failures or skips. Fixture checksum,
  formatting and regenerated code-map verification pass. This is an incremental
  implementation checkpoint; the branch 4c headless acceptance gate has not closed.
- Remaining mission/arc/adhoc/event-script selection, objectives, sites/bases, detection
  scripts, UFO hunting/escorts and headless closure are still outstanding. Branch 4d
  remains unstarted pending the 4c merge.
- Audit corrections: due-event preflight now credits guaranteed same-day interruption
  research and lookups while guarding possible disabling discoveries. Arrival preflight
  shares the pure ten-minute fuel/return transition with execution, so fuel return wins
  over interception or landing in the same tick. Reference at `4df3a5e`: `GeoscapeState.cpp`
  time-trigger fallthrough, `time1Day`, `time30Minutes`, `time10Minutes`, `time5Seconds`;
  `SavedGame.cpp::addFinishedResearch/selectGetOneFree`; `Craft.cpp::consumeFuel/getFuelLimit`.
  The regression fixtures in `StrategicEventSchedulingTests` and `StrategicWorldPursuitTests`
  cover fresh/cache and transition reloads, notification fallthrough, lookup completion,
  unrelated research, incomplete/disabled/repeatable research, possible free/zero-cost
  disablers, and interception guards when fuel return is not due. No 4d work is included.
  Validation: root `dotnet test` passes 1,098 tests with zero failures or skips, including
  fixture checksums and the private corpus. Formatting, code-map and diff checks pass.
- Scheduling-rule slice: mission/adhoc/arc/event families now project immutable linked
  eligibility, trigger, counter, conditional, tag and weighted timeline data. Mission
  objective consistency is validated at linking. Pure selection follows captured
  reverse timeline scans and ordinal weights, including safe malformed-input limits.
  Fresh/cache reconstruction uses compiler revision 26. See the [reference inventory,
  defaults and boundaries](strategic-script-rule-status.md). Campaign scheduler hooks,
  trigger evaluation and mission/arc/event execution remain the next slice.
  Validation: root `dotnet test` passes 1,115 tests with zero failures or skips, including
  fixture checksums and private corpus checks. Formatting, regenerated code-map and
  diff checks pass. One earlier run reported a 256-byte patrol-allocation difference;
  the isolated test, complete craft suite and two subsequent root runs passed without
  changing that test or its threshold. The cause was not established.
- Audited rule projections and preflight corrections committed as `9f7d4d0`.
- Monthly event-script subsystem: live campaign trigger facts, eligibility/counters,
  ordered odds draws, sequential/random/repeatable selection and transaction-owned
  event scheduling now execute through an internal subsystem fixture operation.
  History-key presence (including zero counts), duplicate instances and later-script
  suppression survive save/reload. Reference loop: `strategic-event-scripts`.
  See [scope and evidence](strategic-event-script-status.md). This does not yet install
  the initial/monthly campaign caller: arc and mission phases must precede events.
  Score/funding context calculations, actual hooks and the campaign month guard remain
  outstanding with the broader 4c work. No 4d work is included.
  Sequential preflight now stops at tails that cannot be exposed in the current pass,
  accounting for earlier scripts without treating the current script's selections as
  already generated. Missing/invalid-timer tails and reload transitions have regression coverage.
  Validation: root `dotnet test` passes 1,168 tests with zero failures or skips,
  including checksum and private-corpus tests. Formatting verification, regenerated
  code-map and diff checks pass. Reference checkout remains unchanged.
- Monthly mission-script subsystem: ordinary space missions with explicit weighted
  mission/region/race choices now start through an internal gameplay operation.
  Eligibility, run-limit collection, conditional labels, odds, delays, mission IDs,
  run history and strategy-table consumption have fresh/cache and transition-reload
  tests. The pinned `strategic-script-rules` probe now captures the reference delay.
  See [scope and boundaries](strategic-mission-script-status.md). Site/base/fallback
  modes, arc/adhoc scheduling, full campaign hooks, score/funding calculation and
  the month guard remain for later 4c slices. Branch 4d remains untouched.
  Validation: root `dotnet test` passes 1,188 tests with zero failures or skips;
  fixture checksums, formatting and generated code-map verification pass.
- Monthly arc-script subsystem: a registered isolated world operation now collects
  eligibility and odds before any research grant, counts duplicate researched arcs,
  applies sequential then prefiltered weighted grants, and uses the research
  capability for lookup/zero-cost chains. Reachable missing topics and possible
  score overflow stop before RNG or mutation. Fresh/cache, save/reload and
  same-pass exposure scenarios use `strategic-arc-scripts.rul`. See
  [scope and boundaries](strategic-arc-script-status.md). The actual monthly
  caller, score/funding calculation and month guard remain for later 4c work;
  branch 4d remains untouched. Preflight ignores weighted choices made unreachable
  by `maxArcs` and bounds score per reachable grant slot. Root `dotnet test` passes
  1,212 tests with zero failures or skips, including checksum and private-corpus
  tests; formatting verification passes.
- Tagged adhoc mission-script subsystem: event rules retain immutable tag lists;
  the registered gameplay operation filters the ordered adhoc list using the
  reference first-script-tag behavior and shares bounded mission execution with
  the monthly mission phase. Fresh/cache, save/reload, eligibility and atomic
  unsupported-objective fixtures are in [the status](strategic-adhoc-script-status.md).
  Due event payloads remain guarded for branch 6; full mission modes and actual
  campaign hooks remain 4c work. Branch 4d remains untouched.
- World-script context slice: the isolated monthly caller inputs now derive score,
  current/peeked funding and the mod performance bonus from live campaign state.
  The [pinned context probe](strategic-world-monthly-context-status.md) checks
  month-zero/one/two, negative/fractional bonuses and the adhoc funding path.
  Actual initial/monthly calls remain guarded; branch 4d remains untouched.
- Mission-site timer slice: the half-hour handler scores sites, decrements their
  timers and removes simple unlinked sites in reference order. Followed sites can
  remain overdue; forced expiry returns craft followers before movement and emits
  the lost-site alert. All six despawn counter changes execute in reference
  order and persist. UFO followers and other despawn consequences stop before
  the tick. The [pinned site-timer probe](strategic-world-site-timer-status.md)
  and fresh/cache transition tests cover this bounded lifecycle. Full site and
  alien-base behavior remain branch 4c work. At the 360-tick corpus horizon,
  Rosigma Early now executes; totals are nine executable, two blocked and eight
  tactical preservation-only across fresh/cache loads.
- Associated-UFO site lifecycle: ignored UFOs wait inertly while their linked site
  remains, then lift at expiry with the reference score, stored timer and landing
  identity. The same tick runs base scanning, flight and shield recharge; ordinary
  airborne site-objective outbound legs now execute. The extended
  `strategic-world-lifecycle` C++ capture pins the release block and mission-score
  filter. Fresh/cache reloads cover waiting, reactivation, UFO departure and mission
  cleanup. Follower holds, hidden-contact loss, terminal fallthrough and atomic
  interception/activity/shield guards are tested. Ordinary takeoff shares lift-speed
  validation with site reactivation; negative/overflowing inputs stop before the tick,
  including when two negatives would produce a positive speed. Despawn research/events and UFO
  follower repair remain guarded. See [status](strategic-world-site-timer-status.md).
  This advances item 5; the branch 4c headless gate remains open.
- Alien-base activity/discovery slice: passive bases accrue daily region/country
  points, completed facilities discover in reference order, and craft patrol sight
  executes after each craft's fuel transition. Safe non-generating supply checks
  retain weighted selection and frequency RNG. Discovery persists across reloads
  and loaded rewrites; deleted base overlays cannot resurrect state. The blanket
  alien-base guard is replaced with daily mission-generation/self-destruction and
  ten-minute hunting guards. The [pinned probe and status](strategic-world-alien-base-activity-status.md)
  cover boundaries and remaining work. Upgrades, evolution, generation and linked
  deletion remain open; the 4c gate has not closed and 4d remains untouched.
  Audit fixes retain merged/inherited supply weights so generating choices cannot
  escape the daily guard, and clamp distance rounding before daily sight converts
  it to integer units. Extended reference weight traces and fresh/cache regression
  tests cover both repairs.

- Daily alien-base mission-generation slice: reference mission selection, generation
  frequency/limits, targeted/weighted/local regions and optional weighted races now
  create missions linked to their source base. All objectives and linked operation
  modes may be scheduled with site-area and initial countdown ownership. Midnight
  half-hour countdown fallthrough, generation counters, ID reservations, fresh/cache
  reloads, unknown/deleted overlays and a 72-hour generation/cleanup scenario pass.
  Short first timers that could spawn in the creation tick retain an explicit guard
  until combined new-mission forecasts exist; unsupported later waves still stop
  before mutation. See [status](strategic-world-alien-base-generation-status.md).
  Hunting, linked deletion, upgrades/evolution and the remaining branch 4c work
  remain open. No branch 4d work was started.
  Audit repair adds reference-ordered daily mission interruption, zero-live cleanup,
  research-aware wave/scoring forecasts and transition reloads. Generated missions
  are first checked on the next day. Uncertain research outcomes and interruption
  with live UFOs stop before the daily tick until combined forecasts are owned.

- Monthly alien-base upgrade/evolution slice: a registered isolated operation now
  preserves weighted age selection, deployment/age/hunting-timer changes and
  ordered chained race evolution. All upgrades precede evolution. Every reachable
  outcome is checked before mutation/RNG, with reference loop and bounded work
  failures. Marker changes repair mission and follower references; stable base
  sidecar keys preserve unknown data across repeated upgraded rewrites. The pinned
  `strategic-alien-base-monthly` capture, fresh/cache and reload tests cover the
  operation. See [status](strategic-world-alien-base-monthly-status.md). The actual
  monthly caller and campaign month guard remain outstanding; 4d is untouched.
  Audit repair classifies craft destinations by actual type/ID instead of rule
  names, preserves followers of craft-named base markers, and guards actual craft
  collisions and the legacy terror-marker alias before upgrades. Three extracted
  craft loading traces and fresh/cache reload regressions cover the fixes.
  Validation: all 1,384 root tests pass with no failures or skips, including checksum
  and private-corpus tests; formatting, regenerated code-map and diff checks pass.

- Research-driven alien-base deletion: daily self-destruction clears mission links,
  interrupts Earth operations and applies optional pact cancellation before scoring,
  generation and discovery. Guaranteed daily research participates in forecasts;
  uncertain outcomes and live Earth-operation transitions retain precise guards.
  Reference capture, fresh/cache and loaded rewrite coverage: [status](strategic-world-alien-base-cleanup-status.md).
  Campaign settings/cache revision 31 preserves the default-false pact option.
  Branch 4c remains open and 4d is untouched.

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

Post-cycle audit preserves site-arrival guard tests by selecting an actual mission
site area instead of relying on the removed objective-wide ground guard. Private
Rosigma Begining's undefined STR_DUMMY wave now follows captured no-object behavior
at tick 220 and completes the 360-tick horizon. Its resolved wave is asserted to
have no UFO or site effect. All 19 classifications remain explicit: ten executable
at 360 ticks, one UFO save blocked at 125 ticks, eight tactical saves preserved at
zero ticks; fresh/cache and repeated loaded rewrite assertions are retained.

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

### Base-objective checkpoint (2026-10-04)

The latest three-slice cycle completes the pinned deployment/search projection,
live final-wave creation and accepted landing-site creation. The batch audit adds
counter-scan and aggregate patrol work guards and prevents newborn bases from
starting unreserved hunting missions during the same tick. Persistent birth keys,
combined IDs/scores, fresh/cache reloads and precise malformed-rule boundaries
are covered. The audit also rejects negative hunting timers and guards craft
production sharing a base marker on hourly ticks. Remaining 4c work includes
creation-tick hunting and shared craft-production ID forecasts, infiltration/pacts,
detection VM and actual initial/monthly caller composition.
This checkpoint does not close headless acceptance or begin 4d.

Validation: root dotnet test --no-restore passes all 1,841 tests, with no failures
or skips, including checksum, private-corpus and populated world coverage.
Formatting verification, regenerated code-map validation and git diff --check
pass. Useful captured probes remain available; obsolete probe objects were
removed. The pinned reference checkout is clean. Branch 4c remains incomplete,
and branch 4d is untouched.

### Infiltration and retaliation checkpoint (2026-10-04)

Five further cycles deliver fifteen slices with five batch audits. Infiltration now
preserves country eligibility, regional and country-border searches, option defaults,
country shuffle order, pending pacts, base creation, countdowns and bounded endless
restarts. Retaliation now preserves ordered target selection, no-target scouting and
instant interruption, default assault constructors, override ships, linked/regional
origins, ground-origin land searches, direct deployment-site fallthrough and safe
generated no-object missions in mapped regions. All transitions use registered
capabilities and existing score, identity, persistence and movement forecasts.

Three confirmed audit findings were reproduced and fixed separately: aggregate
suppressed-country search work, legacy UFO wave inference across endless restarts,
and base discovery changing an immediate hunting mission's target after preflight.
The last regression covers both fresh/cache unchanged-state refusal and an executable
out-of-range scanner. A separate batch corrected initializer formatting.

The headless gate remains open. Remaining 4c work includes wider generated object
forecasts, newborn-base hunting and shared craft-production identities, legacy-wave
forecasts at endless restarts, actual detection VM providers, initial/monthly world
caller composition and final composed acceptance. Targeted instant/base-defense
arrival stays in branch 5. Signed monthly pacts, event payload execution and integrated
month progression stay in branch 6. Pending pact and site/event scheduling do not
claim those deferred effects. Branch 4d is untouched.

Final five-cycle validation: all 1,886 root tests pass without failures or skips,
including oracle checksums and private-corpus cases. Formatting verification,
regenerated code-map checks and git diff --check pass. Useful generated probes
remain available; the obsolete retaliation-spawn object file was removed. The
pinned C++ checkout remains clean. This completes the requested fifteen slices
and five audits, while the revised 4c headless completion gate remains open.

### Legacy arrivals, shared production IDs and newborn hunting checkpoint (2026-10-04)

Four further cycles deliver twelve slices with four batch audits. Legacy UFO
arrivals now infer waves from bounded post-think mission counters, including
endless infiltration restarts; site conversion, pursuit and landing reservations
use that projection without changing saved wave identity or consuming RNG.
Production-owned bounds coordinate final-wave bases, landing bases, direct sites
and UFO conversions sharing craft counters. They include fallback staffing,
craft-material consumption, hourly engineer arrivals and daily workshop completion.
Newborn objective/infiltration bases can start delayed and immediate no-object
hunting missions with combined mission IDs, ordered rectangular-region coverage,
mapped regions, bounded work and transition reloads.

Three confirmed audit issues were fixed in separate commits: malformed finite-chain
error behavior, partition arithmetic/resource limits, and missing same-tick staffing
inputs. The second audit found no confirmed behavior defect. Fresh/cache tests cover
unchanged-state refusal, actual births, links, cleanup and persistence. Added craft
identity scans are charged on both hourly and intervening ticks.

The revised headless gate remains open. Remaining 4c work includes actual detection
VM providers, initial/monthly caller composition, wider generated-object and newborn
source forecasts, and final composed acceptance/performance classification. Newborn
immediate supply UFOs still require a live source forecast, and uncertain/decreased
shared marker counters retain precise checks. Combat/deployment stay in branch 5;
event effects, signed monthly pacts and integrated month progression stay in branch 6.
Branch 4d is untouched.

Final four-cycle validation: all 1,916 root tests pass with zero failures/skips,
including checksums and private-corpus tests. Formatting verification,
regenerated code-map validation and git diff --check pass. Three confirmed audit
findings were fixed separately; all twelve requested slices and four audits are
complete. Useful native captures remain available; the obsolete counter-probe
object file was removed. The reference remains clean at the pinned commit.
The revised 4c headless acceptance gate remains open; 4d is untouched.

### Detection VM checkpoint (2026-10-04)

The next three-slice cycle pins exact detection caller inputs/roll order, enables
bounded compiled scalar UFO programs and global before/after events, and supplies
read-only UFO, craft and campaign/clock bindings. Native getter traces, fresh/cache
execution, null receivers, race-adjusted shields, installed radar and transition
reloads cover the live call sites. Precise loop/divisor/provider checks stop before
an unsafe script can mutate the tick.

The batch audit reproduced and fixed cached-lookup closure allocations and missing
aggregate contact/event work limits. Existing 8/32/128-UFO measurements now remain
at 368 bytes per additional boundary UFO for both one and eight bases, below the
unchanged 448-byte bound. Excessive programs across contacts/events stop with
unchanged snapshots. Full root validation passes all 1,942 tests with no failures
or skips, including checksums and private corpus cases. The headless gate remains
open: script-value and remaining detection providers/loop forecasts, actual
initial/monthly caller composition, wider generated-object forecasts and final
composed classification are still required. Branch 4d remains untouched.

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
