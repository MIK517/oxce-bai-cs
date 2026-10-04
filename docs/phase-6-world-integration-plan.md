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
