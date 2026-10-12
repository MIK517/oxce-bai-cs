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

## S2 — Evidence and ownership (2026-10-12)

### Rule projection

The typed catalog already parsed and validated every `RuleCraftWeapon`, `RuleCraft` and `RuleUfo`
property (including `RuleCraftWeapon::afterLoad`'s launcher, missile-speed and rearm checks). The
runtime projection now carries what interception reads:

- Craft weapons: `damage`, `shieldDamageModifier` (default 100), `range`, `accuracy`, the three reload
  times, `projectileType` (default 2, `CWPT_CANNON_ROUND`), `projectileSpeed`, `weaponType`,
  `tractorBeamPower`, `unifiedDamageFormula` and `underwaterOnly`.
- Crafts: `missilePower` (`isMissile` when positive) and `maxAltitude` (default -1; `isWaterOnly`
  above it).
- UFOs: `score`, `breakOffTime`, `softlockThreshold` (default 100), `missilePower`,
  `missileStopChance` and `splashdownSurvivalChance` (default 100), beside the already projected
  weapon power, range and reload.
- Globals (`CampaignInterceptionSettings`, `Mod::loadFile` order and `Mod::Mod` defaults):
  `chanceToStopRetaliation`, `lessAliensDuringBaseDefense`, `ufoGlancingHitThreshold`,
  `ufoBeamWidthParameter`, `ufoTractorBeamSizeModifiers` (five slots), `escortsJoinFightAgainstHK`,
  the crew and pilot evacuation survival chances, the pilot accuracy/reactions zero points and
  ranges, `pilotBraveryThresholds` (three slots), `showDogfightDistanceInKm`,
  `difficultyCoefficient` and `difficultyBasedRetaliationDelay` (five slots each; process-wide
  statics in the reference), and the `difficultyCoefficientOverrides` lists
  (`ufoFiringRateCoefficients`, `ufoEscapeCountdownCoefficients`, `retaliationTriggerOdds`,
  `retaliationBaseRegionOdds`, `aliensFacingCraftOdds`, `monthlyRatingThresholds`). Fixed slots
  layer per mod element by element; each override list replaces the previous one, even when empty,
  and its callers use it only when it has an entry for the difficulty.

Compiler/cache revision 61. `StrategicInterceptionRuleProjectionTests` pins declared values,
reference defaults and two-layer merging on fresh and cache-restored content.

### Options

`dogfightAI` (default true) and `oxceRememberDisabledCraftWeapons` (default false) are campaign
options: saved in the port options node, seeded from a reference save's option snapshot and
overridden by a mod's fixed user options (`StrategicInterceptionOptionsTests`). The option inventory
for interception paths is in the [ownership ledger](phase-6-ownership.md#branch-5-interception-ownership-s2-2026-10-12).

### Dogfight step oracle

`strategic-dogfight` compiles the reference `DogfightState::think`, `update`, `animate`,
`endDogfight`, `fireWeapon`, `ufoFireWeapon`, the distance helpers, `setStatus` and the five mode
handlers verbatim, together with the gameplay parts of the constructor (weapon enable, pilot bonuses,
hunter-killer and missile options, initial mode, escape and fire countdowns, weapon intervals, UFO
size, shield-recharge handle), `Ufo::setDamage` (including the wave interrupt roll), `CraftWeaponProjectile`, `CraftWeapon::setAmmo/fire`, the `Craft`
pilot bonuses and the `Ufo`/`Craft` damage and shield setters. Drawing and sounds are stubs; RNG
returns scripted raw values through `RandomState::generate`, so the port can replay the same calls.
Each of 15 scenarios records the start state, every step (distances, mode, UFO and craft damage and
shields, countdowns, tractor slowdown, ammunition and weapon timers, projectiles, end flags, status
timeout and text), the final world-facing state and every RNG call: standoff then aggressive cannon
crash, cautious missile crash with retaliation spawn, standard lasers against shields with all
difficulty overrides, an outrunning UFO, disengage and minimize, a hunter-killer attack with
evasive manoeuvres, a tractor-beam forced landing, a missile craft impact, a crash surviving fake
water, escape-countdown break-off, an all-miss fight, a damaged cautious craft retreating to
standoff, the first scenario with `dogfightAI` off (all others use its default, on), a large UFO
whose crash rolls its wave's `interruptPercentage`, and a weapon disabled at the start. The oracle is consumed by S3.

Findings that shape S3: the status timeout that ends a fight after a crash or a craft loss is
decremented in `animate`, which runs only while the window is not minimized; ending a break-off
moves the UFO once (`Ufo::move`) and ending a hunter-killer attack lets the craft take one
movement step (`Craft::think`), both outside the time tick; the pilot averages divide by
`size_t`; `Ufo::setDamage` rolls the wave's `interruptPercentage` when the UFO crashes; a unified
damage formula draws from the clip's or launcher's damage type (`RuleDamageType::getRandomDamage`,
not pinned here: S3 captures it with its consumer); the UFO's `shieldRechargeHandle`
and `softlockShotCounter` are saved (the port already persists them).

### Scope note

Start rules, landing starting conditions and base-defense rounds are captured with the slices that
implement them (S4, S6, S7) rather than here, so each oracle lands with its consumer; the plan
records the move.

### Audit and validation

The slice review found four items, all addressed: the oracle ran with `dogfightAI` off although the
reference default is on (now on by default, with one scenario off); it missed the wave interrupt
roll, larger UFOs and a disabled weapon (added; the unified damage formula and in-fight weapon
toggles are captured in S3 with their consumer); the reference-save option seeding of the new
options was untested (added); a comment misplaced the reset of the reference statics (corrected).
Validation on the Windows workstation: formatting and code map clean; root `dotnet test` passes
3,463 tests (private corpus included) with no failures or skips.
