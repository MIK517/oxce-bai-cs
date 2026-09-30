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
  remain overdue; unsupported forced follower return and despawn consequences
  stop before the tick. The [pinned site-timer probe](strategic-world-site-timer-status.md)
  and fresh/cache transition tests cover this bounded lifecycle. Full site and
  alien-base behavior remain branch 4c work. At the 360-tick corpus horizon,
  Rosigma Early now executes; totals are nine executable, two blocked and eight
  tactical preservation-only across fresh/cache loads.
