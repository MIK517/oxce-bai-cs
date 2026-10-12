# 0033: Atomic tick preflight with shared per-tick flight projection

Status: accepted for Phase 6 branch 5 (kickoff decision, 2026-10-12).

## Problem

Every five-second tick is preflighted completely before it mutates anything, so a tick the port
cannot simulate is refused atomically
([ADR 0025](0025-strategic-time-and-mobile-save-ownership.md),
[4c closure](../phase-6-world-integration-plan.md#branch-4c-closure-2026-10-11)). Branch 5 makes
interception, landing and base defense executable, and the 128-UFO one-day scenario exceeds the soft
0.6 s per in-game day [target](../performance-targets.md) at 0.87 s. The
[Phase 6 plan](../phase-6-plan.md#branch-5--interception-and-strategic-deployment-handoff) asked
the branch 5 kickoff to choose between keeping the preflight and sharing each UFO's flight forecast
within a tick, or moving to capture-and-restore ticks with execution-time boundary checks.

The measurement ([branch 5 plan](../phase-6-interception-plan.md#starting-audit-2026-10-12)) puts
about 28 of the 31–32 us of Flight128 preflight in the three UFO steps (movement, site conversion,
half-hour). Each of them re-projects every UFO's flight, about five times per UFO per tick, besides
linear mission scans and rule lookups.

## Decision

Keep the atomic per-tick preflight. Share one flight projection per UFO per tick.

- Capture-and-restore does not pay for itself: a whole-state capture costs about 30 us per tick
  (TFTD `Middle.sav`, 4c closure), as much as the whole Flight128 preflight before any restore, and
  it needs an execution-time check for every boundary and an audit of every preflight consumer.
- Branch 5 mostly removes boundaries. Inside a tick the reference only starts dogfights and landing
  confirmations and pauses, which the preflight detects without resolving. The new in-tick effects
  fit the existing terminal-arrival model: a hunter-killer attack skips the rest of the craft pass,
  and the base attack resolves (defense setup, missile damage, base loss) and then ends the tick, so
  the preflight validates its inputs and execution runs it last.

The shared projection is constrained because its inputs change during the preflight pass:

- The cached value is `ProjectFlightTarget(UfoForFlight(ufo))` per UFO index. It is filled no
  earlier than after `PrepareHuntingGeneratedWaveForecasts`, when `RetargetForecast`,
  `NewLeaderFlightForecast`, `HuntingCraftForecast` and the escort target tables are final; earlier
  callers compute directly.
- `FlightFuelRefundReason` temporarily swaps `HuntingCraftForecast` and computes directly. Any other
  temporary swap is bypassed the same way.
- A `ForecastUfoStep` status is cached only where it does not read `SuppressedUfoSites`, which
  `UfoSiteConversionsReason` fills, or the entry is invalidated when that set changes.
- A test-only mode recomputes every cached read and asserts equality. It runs over the public
  compatibility tests and the private corpus.

The expected gain is measured before it is claimed: S1 first measures the share of the three steps
that is projection, and an indexed mission lookup for the tick is in scope if the scans matter.

## Consequences

- Preflight code may read the shared projection after the freeze point; the restriction that
  preflight code does not cache flight forecasts across checks is lifted only for this cache.
- If Flight128 still exceeds the target after S1, the measured value and this reason are recorded
  in [performance targets](../performance-targets.md) under the soft-target rule instead of changing
  the architecture during branch 5. Capture-and-restore remains the documented alternative.
