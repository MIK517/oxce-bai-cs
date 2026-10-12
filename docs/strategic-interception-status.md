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

## S3 — Interception state and combat step (2026-10-12)

### Dogfight engine (2026-10-12)

`Dogfight` (`Campaigns/World/Interception`) ports the gameplay of `DogfightState`: the constructor's
weapon enable, pilot bonuses, hunter-killer and missile options, initial mode, escape and fire
countdowns, weapon intervals, UFO size and shield-recharge handle; `think`, `update`, the status
timeout and hit-frame counters of `animate`, `fireWeapon`, `ufoFireWeapon`, the distance helpers and
mode handlers, `endDogfight`, minimize and self-destruct (`btnMinimizeClick`) and the weapon toggle
(`weaponClick`, remembered with `oxceRememberDisabledCraftWeapons`). `DogfightProjectile` ports
`CraftWeaponProjectile`, `DogfightWeapon.SetAmmo` `CraftWeapon::setAmmo`, and the craft and UFO
views port the `Craft`/`Ufo` damage, shield, crash and destruction rules, including the wave
interrupt roll of `Ufo::setDamage`. Everything outside the window goes through `IDogfightWorld`
in reference order (experience, globe tests, UFO move, craft return/think/pursuit, hunter reset,
shot-down bookkeeping, retaliation odds and spawn, activity scores, IDs, mission interruption).
`DogfightItemDamage` ports `RuleDamageType::getRandomDamage` for unified-formula weapons, with the
`damageRange`, `explosiveDamageRange` and `fireDamageRange` globals now projected (compiler/cache
revision 62).

`StrategicDogfightOracleTests` replays all 18 `strategic-dogfight` scenarios (three new ones use
the unified formula: standard, default-to-fire and four dice) from their recorded inputs and checks
the start state, every step row, the final world-facing state and every random call against the
reference. The engine is not yet driven by time advance or commands; the world capability, save
behaviour and UI follow in the rest of S3, S4 and S8.
Validation: formatting and code map clean; root `dotnet test` passes 3,481 tests with no failures
or skips.

### Interceptions in the campaign

`CampaignWorld` keeps the open interception windows (`CampaignWorldInterceptions`) and offers
`AdvanceInterceptions` (one dogfight timer step: `GeoscapeState::handleDogfights`, including the
per-step `interceptionProcessed` reset and the depth/land wait checks), `SetInterceptionMode`,
`MinimizeInterception` (only at standoff range; the self-destruct switch of a defenceless craft),
`RestoreInterception`, `ToggleInterceptionWeapon` and `ICampaignInterceptionQuery`. Windows are numbered
like `getFirstFreeDogfightSlot`. The craft and UFO sides are mirrors of the saved state: before each
command they are refreshed from the craft logistics and UFO snapshots, and afterwards damage, shield,
ammunition, disabled weapons, interception order, escape and fire countdowns, shield-recharge handle,
softlock counter, hunter flag and speed are written back. Transient `Ufo` fields (tractor slowdown,
firing window, hit frame) stay on a per-UFO mirror until the UFO leaves the world, as on the reference
object, and are dropped by a reload. Craft statistics are the rule's statistics plus installed-weapon
bonuses; pilot bonuses use `Craft::getPilotList(false)` with soldier-bonus statistics, and the chosen
pilots are written to the craft's `pilots` list, which the port now owns (saved and loaded like
`Craft::save`/`load`).

A step is forecast first on copies of every window, craft and UFO, with the random source rewound
afterwards so the real step draws the same values. Outcomes whose world consequences belong to later
slices (pilot experience once the UFO is down, crash or destruction bookkeeping, retaliation, activity
scores, IDs, wave interruption, break-off movement, hunter-killer resets and craft steps, crash
followers, and a forced landing even when no new ID is needed) refuse the step before anything changes
(`boundary.interception-outcome`). Disengaging is complete: the craft returns to base
(`Craft::returnToBase`) and the window ends with `InterceptionEnded`. Time refuses to advance while any
interception is open (`boundary.interception-time`) until S4 schedules the clock against open windows.
Saves never contain combat: `StrategicInterceptionTests` shows a reload dropping the window while the
saved UFO and craft fields survive.

Audit (code review of the slice) found five items, all fixed: a tractor-beam forced landing of a UFO
that already had a landing ID escaped the forecast (now any status, timer, altitude or ID change is
refused), a craft without pilot seats had its pilot list cleared, weapon-slot indexing could overrun a
shorter saved weapon list, a hunter-killer's target craft was not refreshed, and UFO mirrors were never
pruned.
Validation on the Windows workstation: formatting, code map and Release build clean; root
`dotnet test` ran 3,488 tests with two test-expectation failures (the new preflight in the handler
order list, and `STR_MEDIUM`, which `RuleUfo::load` renames to `STR_MEDIUM_UC` and so has blob size 2);
both tests were corrected and their classes pass, with every other test unchanged and green.

## S4 — Interception start (2026-10-12)

`CampaignWorldInterceptionStart` starts interceptions inside `GeoscapeState::time5Seconds`:

- UFO pass: a hunting UFO that reaches a live craft starts a hunter-killer window. Only one
  hunter-killer fight runs at a time (a second attacker waits); starting one ends every other window
  without their end handling. Escorts within `escortRange` with a loaded weapon join, up to three,
  when `escortsJoinFightAgainstHK` is on. The UFO skips its shield recharge, and the rest of the craft
  pass neither moves, recharges shields nor arrives (`ufoIsAttacking`).
- Craft pass: a craft reaching a flying UFO it can match for speed opens a window, at most four at a
  time except against hunter-killers; a hunter-killer UFO makes it an attack window with escorts. A
  water-only craft below the UFO's altitude, or over water, gets `InterceptionWaiting` with the
  `DogfightErrorState` text and a minimized window waiting for depth or land.
- Windows opened in a tick wait like `_dogfightsToBeStarted` and open at the end of the craft pass,
  latest first, taking `getFirstFreeDogfightSlot` numbers; each emits `InterceptionStarted`, which
  pauses time (and lets the UI reset the speed, as `timerReset` does).
- The destroyed-UFO cleanup removes that UFO's windows, and a waiting window whose UFO came within
  reach emits `InterceptionReady` at the end of the tick, which pauses time.

`MoveCrafts` now follows the reference order: movement and the return-to-base checkup, then the
shield recharge, then the other arrivals. Time runs while every window is minimized
(`interception.open` otherwise). The preflight no longer refuses a craft reaching a flying UFO or a
hunter-killer reaching its craft; it marks the hunter-killer attack so the craft-arrival forecast
expects no arrivals that tick. A hunter-killer reaching a craft on its own birth tick keeps its
earlier refusal (named restriction). Window numbering happens at the end of the tick rather than
after the reference's zoom animation, and start sounds are presentation.

Tests: the earlier guard tests now check, at the same tick, that the tick runs and opens an
interception (`AssertInterceptionStarts`), including a hunter-killer attack window with its
restricted modes; one conservative guard case now runs without an interception because its UFO is
destroyed on arrival first. `StrategicInterceptionTests` covers ordinary and water-only starts and
the minimized clock rule.

Validation on the Windows workstation: formatting, code map and Release build clean; root
`dotnet test` ran 3,491 tests with one expectation failure (the new five-second handlers in the
handler order list), corrected and re-run with the unit suite (820 pass); everything else green. The
slice audit was a focused self-review of the start paths against `time5Seconds` (the code-review
pass is repeated with S5, which builds on these paths).


## S5 — Interception outcomes in the world (2026-10-13)

`CampaignWorldInterceptionOutcomes` implements the `DogfightState` calls into the geoscape, so an
interception step no longer refuses its outcomes (`boundary.interception-outcome` is gone):

- Crash bookkeeping: crash and forced-landing status, timer, altitude and crash or landing IDs are
  written back to the saved UFO; `Ufo::setDestination(0)` clears a shot-down UFO's destination (a
  reload gives it a waypoint at its own position, as `Ufo::load` does); a hunter-killer that escapes
  or crashes resets its original destination only while it still targets that craft.
- `AlienMission::ufoShotDown` delays the next wave; `Ufo::setDamage` rolls the wave's interrupt
  chance (the battleship of a retaliation assault run counts as the last wave).
- Retaliation: mission odds, race aggression, difficulty overrides and the retaliation delay; the
  target region (UFO mission region or `locateRegion` of the craft's base); `findAlienMission` by the
  race's retaliation mission names or by objective; the race's weights or `getRandomMission`, then a
  new mission with the first wave's spawn timer. Alien races now project `retaliationAggression` and
  `retaliationMissionWeights` (compiler/cache revision 64).
- X-COM activity for the first country and first region containing the UFO.
- Pilot experience (`awardExperienceToPilots`): firing, reactions and bravery (+10) rolls under the
  soldier's caps with the new `dogfightExperience` projection, the diary's `ufosShotDownTotal` and
  `ufosDamageTotal`, and the daily `dailyDogfightExperienceCache`, all owned and saved; `time1Day`
  clears the cache (`DayDogfightExperience`). The stat string is presentation and is not kept.
- Endings: a breaking-off UFO moves one step and a pursuing craft keeps chasing at full speed; a
  hunter-killer fight's craft returns, thinks one step and, if home, stops being hunted; crafts that
  follow a crashed UFO without units or landing ability return to base.
- Craft loss: a destroyed craft is lost at the start of the next craft pass, with its score taken
  from X-COM activity, its crew evacuated as personnel transfers (`personnelTime`), hunters and
  followers released and `craftLostDogfight` (now owned and saved) increased. A crew member whose
  evacuation roll could fail would die; soldier deaths and the memorial are not owned yet, so the
  preflight refuses that tick (named restriction, default survival chances are 100%). The craft pass
  also takes a craft out of its interception when its UFO landed or its destination is no longer a
  UFO.

The step forecast keeps every world change in overlays (crafts, UFOs, missions, new missions,
soldiers, IDs and activity), so later callbacks of the same step see what they would see live and
draw the same random values; it refuses only outcomes the port cannot apply: a retaliation mission
run from an alien base (operation-base simulation), a base outside every region (undefined in the
reference), and out-of-range counters.

Tests: `StrategicInterceptionTests` covers destruction at sea with the mission delay and UFO removal,
a crash on land with its crash ID, timer, followers returning and reload, a tractor-beam landing that
keeps its landing ID, pilot experience with the diary and daily cache through reload and the day
change, and the loss of a destroyed craft with crew evacuation and `craftLostDogfight` through reload.
The earlier destroyed-craft guard tests now check the loss; a craft under repair at an hour boundary
is mended first because the hourly handlers fall through to `time5Seconds`.

Audit: the code-review pass over S4 and S5 found that the hunter-killer attack forecast ignored an
already open hunter-killer fight (the attacker then only waits, so crafts still arrive) and that the
destroyed-craft preflight allocated a closure per craft and tick; both are fixed. Accepted: the
activity range check is per lost craft rather than cumulative, and a step refused for an undefined
retaliation region keeps its window open (malformed content).

Validation on the Windows workstation: formatting, code map and Release build clean; root
`dotnet test` ran 3,495 tests with one expectation failure (the new daily handler and preflight in
the order lists), corrected and re-run with the unit suite (820 pass); everything else green.

## S6 — Landing and deployment request (in progress)

### Landing-site shade (2026-10-13)

`WorldShading` (gameplay) now owns the globe shading math the renderer used: the terminator
gradient, `Globe::getSunDirection` with and without `globeSeasons`, `CreateShadow::getShadowValue`
(with the reference's `x + (z + y)` summation) and the battlescape shade conversion of
`Globe::getPolygonTextureAndShade`. The engine's `GlobeShading` keeps only its noise table and
delegates. The `strategic-landing-shade` oracle compiles the reference Cord, GlobeStaticData,
getShadowValue, getSunDirection, GameTime::getDaylight and the conversion verbatim
(`tools/capture-strategic-landing-shade-reference.ps1`) and records 5,376 shades over six dates
(including leap days and a non-leap century), eight clock times, eight longitudes and seven
latitudes, with seasons off and on; `StrategicLandingShadeOracleTests` matches every one.

### Landing confirmations (2026-10-13)

`CampaignWorldLandings` queues `ConfirmLandingState` from the craft pass: a craft that may land
(units on board, `allowLanding`, not suppressed by `oxceGeoSuppressLandingWithoutEquipment`) reaching
a landed, crashed or expiring UFO outside an interception, a mission site or a discovered alien base
emits `LandingConfirmationRequested` with the mission and globe texture ids (a site on a cosmetic
ocean texture uses -1; a site's own texture is its mission texture) and the `WorldShading` shade, read
with the new `globeSeasons` campaign option (saved like the other options). The queue is the popup
list: it pauses time (`landing.open`), shows one window at a time, is not saved (a craft still at its
target asks again after a reload), and applies `ConfirmLandingState::init` before each window: a
craft now heading for its own base, or (intentional difference, ADR 0032) left without a destination,
closes it. `AnswerLanding` No sends the craft home; Patrol leaves it circling at half speed; Yes still
refuses with `boundary.landing` until the starting conditions and the deployment request land.

The craft-movement preflight no longer refuses landing arrivals; only the self-escort check remains,
and its dead landing and discovery forecasts are removed. A follower landing at a site respawned by a
UFO conversion keeps `boundary.landing` for now. The earlier landing guard tests now check that the
same tick opens a confirmation (`AssertLandingRequested`); the Rosigma `Early.sav` horizon now runs one
tick further and stops at the open confirmation. `StrategicLandingTests` covers the texture and shade,
the clock hold, a wrong confirmation id, No, Patrol, a reload with seasons on, and a crew-less craft
sent home from a crash site.

Validation on the Windows workstation: formatting, code map and Release build clean; root
`dotnet test` ran 3,500 tests, all green (including the private horizon test).

