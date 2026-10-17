# 0035: Staged ticks on a shadow copy

Status: accepted for Phase 6 branch 6 (2026-10-17).

## Problem

Each tick is preflighted once, completely, before it changes anything, so a tick the port cannot
simulate is refused atomically ([ADR 0025](0025-strategic-time-and-mobile-save-ownership.md),
[ADR 0033](0033-shared-per-tick-flight-projection.md)). The preflight reads the state as it is
before the tick and forecasts whatever earlier handlers in the same tick change.

On a month-boundary tick, `GeoscapeState::time1Month` runs first. The same tick then falls through
the day, hour, half-hour, ten-minute and five-second handlers. The month changes a lot of what
those handlers read: new missions and alien bases from `determineAlienMissions`, psi stats, funds
and score histories, country funding and pacts, a discovered alien base, months passed. Most of
these outcomes are random. A due event payload (`GeoscapeEventState::eventLogic`, in the half-hour
handler) also changes research, funds, stores, crafts and personnel before the ten-minute and
five-second handlers run.

Forecasting all of this in the single preflight would mean outcome envelopes for every month
effect that a later handler reads. That is the same forecast growth that left branch 4c with named
restrictions R4–R6
([4c closure](../phase-6-world-integration-plan.md#branch-4c-closure-2026-10-11)), and it would
apply to every month. The [Phase 6 plan](../phase-6-plan.md#work-required-before-the-first-new-gameplay-action)
forbids running scripts speculatively, rewinding random choices or inventing rollback to discover
eligibility. The 4c closure rejected transactional ticks because execution handlers do not detect
boundaries themselves: the boundary reasons live only in the preflight.

## Decision

A tick that contains a stage point is preflighted in stages. Every stage is checked by the existing
preflights, used as designed, on the state that stage really starts from.

- **Stage points.** A stage point falls between two trigger levels of the fallthrough. There are
  two: after the month handlers (the rest of the tick is checked with `highest = OneDay`), and after
  the half-hour handlers when an event payload is due (the rest with `highest = TenMinutes`). The
  event handler is the last half-hour handler, so both points fall on level boundaries. Handlers
  before the first stage point are checked by the ordinary preflight with the tick's real
  `highest`. That check is conservative for the later levels: stage two checks them again.
- **Shadow copy.** To check a later stage, the campaign is copied into a shadow: the save-neutral
  snapshot restored with `CampaignState.Restore`, and the transient state that saves drop, which each
  capability copies through a named `Transient` registration. It also gets the same selected speed. The shadow first repeats the
  tick's own preflight, because execution reads the pass that preflight builds, then runs the handlers
  down to each stage point and preflights the rest of the tick there. It is discarded, together with its
  notifications.
- **Random state.** The shadow draws from the campaign's own random source, and the source's state is
  put back when the shadow is done (`IStatefulRandomSource.Restore`, the same contract saves use). The
  real tick then draws exactly the values the shadow drew. A shadow never runs five-second handlers or
  dogfight steps, so the interception objects it shares with the original are only read.
- **Real tick.** If any shadow stage refuses, time stops before the tick with that reason and the
  real campaign is untouched, random state included. Otherwise the real tick runs. At each stage
  point it computes the remaining preflight again on real state, because execution reads that
  pass. The result must agree with the shadow's; a mismatch is a determinism defect. It throws, and
  because the higher levels have already run, the campaign refuses every later command.
- **No rollback.** The real campaign never runs a handler it may have to undo. Speculative work
  happens only on the shadow, which is an exact copy and has no observable effects. This amends the
  Phase 6 plan rule for exact shadow staging only: the shared random source is put back to the state it
  had before the shadow drew, and no real draw happens in between. That relies on `State` and `Restore`
  capturing all of a source's state, which `IStatefulRandomSource` now requires. Probabilistic guesses,
  partial undo of real state and any other rewinding of the random source stay forbidden.
- **Completeness of the copy.** Anything a handler or preflight reads must be in the snapshot or in
  the declared transient capture. A capability that adds state outside both must add it to the
  transient capture. A test-only mode (`CampaignState.StageEveryTick`; `OXCE_VERIFY_STAGING=1` turns it
  on for a whole test run) stages every tick above five seconds after its highest level. The test
  assemblies always compare each shadow's snapshot with the original's when the shadow is created and at
  every stage point (`CampaignState.VerifyStagedSnapshots`), so a staged tick in any test checks that the
  shadow started from and reached the real state; dedicated tests compare staged and direct runs,
  events and final snapshots included. Tests that measure the cost of unstaged ticks or record every
  random draw turn the mode off for themselves. The transient state each capability copies is listed in
  the [branch 6 status](../strategic-campaign-cycle-status.md#s2--staged-ticks-2026-10-17).

## Consequences

- The `MonthBoundary` and `EventPayloadReason` refusals can be lifted without month-effect
  forecasts. A later handler that cannot handle a month outcome refuses through its ordinary
  preflight, before the month is applied.
- Cost is one shadow copy per staged tick: once a month, plus ticks with a due event. Branch 4c
  measured whole-state capture at 0.03 ms and restore at 0.27 ms (TFTD `Middle.sav`). Ordinary ticks
  are unchanged, so ADR 0033's per-tick budget still holds. Branch 6 S2 measures the staged tick.
- Preflights keep their single-state contract. No preflight learns about stages; only the
  dispatcher boundary does.
- Instant despawn events (`SavedGame::canSpawnInstantEvent`) run inside the half-hour site handler,
  before the event countdown handler, so their stage point does not fall on a level boundary.
  Branch 6 S5 either generalizes stage points to handler positions or shows that the event handler's
  own preflight covers the payload. It records the choice here.
- The 4c restrictions R4–R6 are re-evaluated against this mechanism in branch 6 S9
  ([plan D5](../phase-6-campaign-cycle-plan.md#decisions)).
