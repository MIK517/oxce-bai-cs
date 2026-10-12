# Strategic interception and deployment status (branch 5)

Branch `claude/strategic-interception-deployment`; plan and decisions in
[the branch 5 plan](phase-6-interception-plan.md). Reference:
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. This document records each slice's evidence once; the
plan's Progress section links here.

## S0 — Plan and baseline (2026-10-12)

Recorded the [branch 5 plan](phase-6-interception-plan.md) and
[ADR 0033](decisions/0033-shared-per-tick-flight-projection.md); corrected the stale 4d status
lines and the branch 5 name in [Phase 6 plan](phase-6-plan.md) and
[implementation plan](implementation-plan.md); replaced the Phase 6 plan's branch 5 commit purposes
with the slice mapping; amended the extension-state rule per D6; named the branch 5 records in
`AGENTS.md`.

Baseline on the Windows workstation at `5a78fe3d`: Release build with no warnings; root
`dotnet test` passes 3,457 tests (unit and compatibility, including the private corpus) with no
failures or skips in 10 min 28 s. The kickoff time profile reproduces on this machine (Flight128
50.2 us/tick, 0.87 s per in-game day); see the
[plan's starting audit](phase-6-interception-plan.md#starting-audit-2026-10-12).

## S1 — Shared per-tick flight projection (2026-10-12)

Implements [ADR 0033](decisions/0033-shared-per-tick-flight-projection.md). `SharedFlightAt`
returns `ProjectFlightTarget(UfoForFlight(_ufos[index]))` and the distance to its destination, and
stores them per saved UFO while `SharedFlightsReady` is set: from the UFO movement step (after
`PrepareHuntingGeneratedWaveForecasts`, when the hunting and new-leader forecasts are final) to the
end of the half-hour step (`SharedFlightStepsReason`, closed in a `finally` so a refusal also ends
it), before the flight fuel step swaps the hunting craft forecast. Hunting and
escorting UFOs are never stored, because later steps can still create the hunting craft forecast
their projection reads; an entry is valid only for the saved snapshot it came from. The movement,
site-conversion (including landing objective bases) and half-hour steps read it through index
overloads of `ArrivesAtSite`, `EndsUfoFlightPass` and `LandingObjectiveBaseReason`; the step status
of `EndsUfoFlightPass` is not stored, because a suppressed site conversion changes it during the
pass. Other callers (craft movement, hunting, new-leader flight) still compute directly.

Verification: `CampaignWorld.VerifySharedFlights` recomputes every stored read and throws on a
difference; a module initializer in `tests/Shared/GameplayVerification.cs` enables it in both test
assemblies, so every unit, compatibility and private corpus test runs with it.
`StrategicWorldSharedFlightTests` pins that ticks with a flying UFO read and verify stored entries
(a thread-local count), so the recomputation cannot pass vacuously. Measurement and
before/after timings: [performance baselines](performance-baselines.md#shared-per-tick-flight-projection-branch-5-s1-2026-10-12).
Flight128 stays above the soft target with the reason recorded in
[performance targets](performance-targets.md).

Audit (code review of the slice) found five items, all fixed: the shared window stayed open after
a refusal (now closed in `finally`), no test proved the store was used (added), the cache-miss path
and the site arrival condition were duplicated (now one `ComputeFlight`/`FlightDistance` and one
`ProjectedArrivesAtSite`), and the distance-memo comment overstated reuse. Validation on the
Windows workstation: formatting, code map and Release build clean; root `dotnet test` passes 3,458
tests (private corpus included) with no failures or skips, every stored read verified.

## S2 — Evidence and ownership (in progress)

### Craft-weapon and UFO dogfight rule projection (2026-10-12)

The typed catalog already parsed and validated every `RuleCraftWeapon` and `RuleUfo` property
(including `RuleCraftWeapon::afterLoad`'s launcher, missile-speed and rearm checks). The runtime
projection now also carries what interception reads: craft-weapon `damage`,
`shieldDamageModifier` (default 100), `range`, `accuracy`, the three reload times,
`projectileType` (default 2, `CWPT_CANNON_ROUND`), `projectileSpeed`, `weaponType`,
`tractorBeamPower`, `unifiedDamageFormula` and `underwaterOnly`; UFO `score`, `breakOffTime`,
`softlockThreshold` (default 100), `missilePower`, `missileStopChance` and
`splashdownSurvivalChance` (default 100), beside the already projected weapon power, range and
reload. Compiler/cache revision 60. `StrategicInterceptionRuleProjectionTests` pins declared values
and reference defaults on fresh and restored content. The C++ probes, craft and global rules,
the ownership ledger and the option inventory remain for the rest of S2. Validation: formatting
and code map clean; root `dotnet test` passes 3,459 tests with no failures or skips.
