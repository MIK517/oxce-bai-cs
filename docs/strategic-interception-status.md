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

### Starting conditions on Yes (2026-10-13)

Starting conditions are a runtime rule family (`RuntimeStartingCondition`: allowed and forbidden
craft and soldier types, required items, `destroyRequiredItems`, `requireCommanderOnboard`), globe
textures project their `startingCondition`, and alien races their `baseCustomDeploy` and
`baseCustomMission` (compiler/cache revision 65). Yes runs `ConfirmLandingState::checkStartingCondition`
in order: banned armor, then the condition of the deployment it would use (the UFO type, with
`_UNDERWATER` on a fake-underwater mission texture; the site's deployment; the race's custom base
mission before the base's own deployment), falling back to the mission texture's condition; the
commander (not asked for a destroyed UFO), craft, soldier-type and required-item checks. A failure
sends the craft home and emits `LandingRefused` with the `CraftErrorState` text. A pass still refuses
with `boundary.landing`, before any change, until the deployment request lands; required items are
destroyed together with that request. Dispatching to a target with a starting condition is no longer
refused (`boundary.starting-condition` is gone): the reference checks it only on landing.


Validation: formatting, code map and Release build clean; root `dotnet test` ran 3,503 tests, all green.

### Deployment request (2026-10-13)

A Yes that passes the starting condition destroys the required items when `destroyRequiredItems` is
set and records the `CampaignDeploymentRequest` (D4): the mission type (`STR_UFO_CRASH_RECOVERY`,
`STR_UFO_GROUND_ASSAULT`, or the site's or alien base's deployment), the deployment, the alien custom
deployment and custom mission ids `BattlescapeGenerator` receives (the UFO's `craftCustomDeploy`, now
projected, and its `_UNDERWATER` mission; the site's custom deployment; the race's base custom
deployment and mission), the race, the mission and globe textures (none for an alien base, as
`setWorldTexture(0, ...)`), the shade, the target, the craft, the soldiers on board, its vehicles and
its remaining items. The craft keeps its pre-battle world state. The request is saved as the port-only
`oxcePortDeploymentRequest` node; while it is pending, time (`deployment.pending`) and landing answers
refuse, and confirmations queued in the same tick stay behind it. `TakeDeploymentRequest` is the
Phase 7 seam: it hands the request over once per load (`deployment.none` afterwards) and the campaign
keeps waiting, since the request cannot be withdrawn. The world target list no longer names an
unavailable interception or landing action. Remaining in S6: the follower landing at a site respawned
by a UFO conversion still refuses with `boundary.landing`, and world commands other than time and
landing answers are not yet refused while a request is pending.

Validation: formatting, code map and Release build clean (compiler/cache revision 66 for the UFO
`craftCustomDeploy` projection); root `dotnet test` ran 3,504 tests, all green.

### Holds and the remaining landing cases (2026-10-13)

The registry gained a command gate: while a deployment request is pending every command except the
time advance (held by its preflight) and `TakeDeploymentRequest` refuses with `deployment.pending`, so
nothing changes the craft, its crew or the world before the battle. `boundary.landing` is gone: a
follower redirected to a site respawned by a UFO conversion may now ask to land there, and a damaged
UFO that crashes on its ground arrival lets its pursuing crafts continue (they land or return home in
the craft pass). Only escorts of such a UFO keep a refusal (escort loss of a crashed leader is not
simulated). A Yes whose target no longer offers a mission refuses with `landing.no-mission` (No and Patrol
still answer the window) where the reference throws. The private Rosigma `Early.sav` campaign now runs 6,290 ticks to a terror
site landing, and Yes records its request (`STR_TERROR_MISSION` for the early Slaanesh cult, texture 3,
night shade, ten soldiers) that then holds the campaign (`RosigmaEarlyReachesADeploymentRequest`).


Validation: formatting, code map and Release build clean; root `dotnet test` ran 3505 tests, all green.

Audit: a review of the S6 source against `ConfirmLandingState` and the landing branches of
`time5Seconds` found one defect, fixed: a Yes without a mission closed the window while reporting a
refusal; it now refuses without a change. Checked and matching: the commander rule's UFO statuses,
the missing-deployment early return before the texture fallback, the base custom-mission fallback,
crash recovery versus ground assault for expiring UFOs, and the order of item destruction and request.


## S7 — Base defense, base loss and Cydonia (in progress)

### Defense rounds (2026-10-13)

`BaseDefense.Fire` resolves `BaseDefenseState::nextStep` headlessly: each defense facility (in
`Base::setupDefenses` order) fires once per pass when it has ammunition (its own, or the ammunition
item from base stores), rolls its hit ratio less the UFO's `avoidBonus2`, applies `applyDamage` on a
hit (the unified damage formula with shield modifier, bleed-through and armor, or the vanilla 50-150%
without bleed-through scaling) and spends ammunition on any shot; each grav shield adds a pass, and a
destroyed UFO ends the rounds. `Ufo::setDamage` sets the crash or destroyed status and rolls the
wave's interrupt chance on every damaging hit after that. Presentation steps draw no randomness. The
`strategic-base-defense` oracle compiles `nextStep`, `applyDamage`, the Ufo damage and shield
setters and `RuleDamageType::getRandomDamage` verbatim (`tools/capture-strategic-base-defense-reference.ps1`)
over nine scenarios (misses and hits, destruction stopping the rounds, grav-shield passes, own and
stored ammunition running out, shield modifiers 0/50/100/150/200, the unified formula with
bleed-through and armor, the interrupt roll, an unmanned UFO); `StrategicBaseDefenseOracleTests`
replays every one, including each random call. The base attack itself (the UFO reaching the base,
`setupDefenses`, the outcomes and base loss) is next.

Validation: formatting, code map and Release build clean; root `dotnet test` ran 3514 tests, all green.

### Defense setup (2026-10-13)

Base facilities project their base-defense fields (`defense`, `hitRatio`, `grav`, `ammoNeeded`,
`unifiedDamageFormula`, `shieldDamageModifier`, with the reference defaults; compiler/cache revision
67). `BaseDefenseSetup` builds what `Base::setupDefenses` and `getGravShields` give the defense
window: the built facilities with a defense value in facility order, each with its own ammunition or
the stores item it draws on (and that item's damage type for the unified formula), and the number of
built grav shields (`StrategicBaseDefenseSetupTests`).


Validation: formatting, code map and Release build clean; root `dotnet test` ran 3515 tests, all green.

### Base attack (2026-10-13)

`CampaignWorldBaseAttacks` runs the base assault of `time5Seconds`: a retaliation assault UFO
reaching its ground waypoint makes `AlienMission::ufoReachedWaypoint`'s waypoint draws, loses its
detection (contact-lost alert for followers) and, at the base whose coordinates match (`fmath`
`AreSame`), draws the next wave's countdown, loses its destination and meets `Base::setupDefenses`
(the base remembers the retaliation mission); without a base it is destroyed. Either way the
five-second pass ends there, which the preflight's step forecast models as a terminal arrival. With
defenses and without `ignoreBaseDefenses`, `BaseDefenseStarted` opens the modal window (time and other
commands refuse with `base-defense.open`); `ResolveBaseDefense` fires the rounds (`BaseDefense.Fire`)
or skips them. A UFO shot down ends the attack (an instant retaliation is interrupted; a retaliation
may give up with `chanceToStopRetaliation`, deleting its mission and UFOs or, for multi-UFO
retaliations, marking it in progress). Otherwise `handleBaseDefense` spends the UFO and records the
`STR_BASE_DEFENSE` deployment request: the UFO's `missionCustomDeploy`, race, texture and shade at
the base, the `lessAliensDuringBaseDefense` damage percentage, the defenders (soldiers on crafts that
are not out and unassigned soldiers fit to defend at the default `oxceWoundedDefendBaseIf` of 100%)
and the vehicles of crafts at the base. Named restrictions, refused before anything changes: missile
UFOs (`boundary.base-missile`), an undefended base (`boundary.base-loss`), and an instant retaliation
spawned at the base (its existing refusal). The base's stored vehicles are not counted yet, and the
reference's store adjustments in `setupDefenses` are left to battle generation.
`StrategicBaseAttackTests` covers the window and a shot-down UFO, skipping into the battle request,
an undefended base's immediate request with reload, and the base-loss refusal.


Validation: formatting, code map and Release build clean; root `dotnet test` ran 3,519 tests, all green.

### Base loss and the campaign ending (2026-10-13)

A base without defenders is destroyed (`BaseDestroyedState`): its retaliation mission is deleted
with that mission's UFOs, UFOs hunting its crafts return to their waypoints, crafts following its
crafts go home, its open or pending interceptions close, and the base is removed with everything it
holds (`BaseDestroyed`). The campaign ending is the reference rule (D5): the next five-second tick
that finds no base sets `END_LOSE` (`CampaignEnded`), ends that pass, and every later command
refuses as for any ended campaign. A started campaign without bases is now a valid save, as in the
reference. `boundary.base-loss` is gone; missile attacks keep `boundary.base-missile`.


Validation: formatting, code map and Release build clean. Root `dotnet test` ran 3,519 tests: two expectation updates (the new five-second handler in the order list; a started campaign without bases is now valid) were corrected and re-run with the unit suite (820 pass) and their compatibility classes; two engine allocation tests (`GlobeRendererTests`) failed under the full parallel run and pass alone (9/9), a load-sensitive measurement unrelated to this change.

### S7 review and what remains (2026-10-13)

A self-review of the S7 source against `GeoscapeState::time5Seconds`, `BaseDefenseState`,
`handleBaseDefense` and `BaseDestroyedState` checked the arrival snap to the base position, the
detection loss and contact-lost alert, the wave countdown draw before the `isOver` check, the
retaliation-mission link, the RNG rewind of a refused resolution, the write-back of ammunition and
stores, the deleted mission's UFOs in open interceptions, and the ending check's place before UFO
movement; no defect was found. The full code-review pass is still owed for S7. Remaining in S7: missile
attacks (`Base::damageFacilities`, nuclear destruction, `missileStopChance`), an instant retaliation
spawned at the base, the final-destination (Cydonia) request (done since, see below), stored vehicles among the defenders and the stores moves of
`setupDefenses`, and (done since) the `oxceWoundedDefendBaseIf` option.

`oxceWoundedDefendBaseIf` is now a campaign option (default 100, saved like the other options) and
`Soldier::canDefendBase` reads it for unassigned wounded defenders; unlike the boolean options it is not
yet forced by a mod's fixed user options.

Validation: root `dotnet test` ran 3,519 tests, all green.

Code-review pass over S7 (2026-10-13): the base-assault branch skipped the next-zone validation that
`CanAdvanceOrdinaryWaypoint` gives the waypoint draws; the preflight now validates it. A base whose only
possible defenders are vehicles in its stores (which `setupDefenses` loads with clip ammunition) would
have been destroyed instead of defended; until vehicle ammunition rules are projected it is refused
(`boundary.base-stored-vehicles`).

Validation: root `dotnet test` ran 3,519 tests, all green.

### Final mission (2026-10-13)

`LaunchFinalMission` is the Cydonia button of `SelectDestinationState` with `ConfirmCydoniaState`'s
Yes: a single craft that can launch (the dispatch checks, including armor, items and pilots), fully
fuelled, a `spacecraft`, with the `unlockFinalMission` research completed, a commander on board when
the final deployment's starting condition requires one, and some units, records the deployment
request of the first `finalDestination` deployment with that deployment's race (no world texture,
shade 0). Research, craft and deployment rules project those fields (compiler/cache revision 68).
When several research topics unlock the final mission, the reference picks the one from the latest
mod; the port refuses that case (`boundary.final-research`) instead of guessing.
`StrategicFinalMissionTests` covers the refusal before the research and the recorded request.

Validation: formatting, code map and Release build clean; root `dotnet test` ran 3,520 tests, all green.

### Nuclear missiles and instant retaliation (2026-10-13)

A UFO with negative `missilePower` (a nuclear warhead) destroys the base it reaches, garrison or not,
as `BaseDestroyedState` with missiles (`BaseDestroyed.Missiles`); `missileStopChance` is then rolled
and interrupts the mission only if it still exists (the reference reads the deleted UFO there).
Missiles that damage facilities (`missilePower` above zero: `Base::damageFacilities`, the facility
destruction cascade and the stores check) keep `boundary.base-missile`.

An instant retaliation (`objective: 6`) is born over its target base (`AlienMission::spawnUfo`
draws the base, no position) and, since mission waves spawn in the half-hour handler before the
five-second pass, reaches the base in the same tick. The wave preflight forecasts that assault for
every candidate base (next-zone validation, waypoint land work and the attack outcome), and names an
unsupported outcome such as facility missiles. A retaliation assault born within one step of its base
takes the same path. The assault arrival now also leaves the reference's saved direction and speed
vector: none at the base, or towards the new waypoint when the base is gone (which also becomes the
spent UFO's destination). Waves without any UFO rule keep `Retaliation assault spawning requires a UFO
rule.`, where the reference would dereference a missing rule.

Stored vehicles now defend a base (`Base::setupDefenses`): after the vehicles of crafts at the base,
each stored vehicle type without clip ammunition joins in full and one with clip ammunition joins as
far as the stores hold its loaded clips. The defense rounds draw on the stores without those vehicles
and clips; the port keeps them stored for battle generation to move. The reference walks its stores in
`RuleItem` address order, so two vehicle types competing for too little of the same ammunition, or a
vehicle that loads no clips (a division by zero there), keep `boundary.base-stored-vehicles`.
`StrategicBaseAttackTests` covers the nuclear strike, the facility-missile refusal, an instant
retaliation opening the defense window and its missile refusal, stored vehicles defending, and the
competing-ammunition refusal. Several final-mission research topics remain refused: the reference picks
the one last updated by mod order, which the compiled rules do not record.

A self-review of this diff against `spawnUfo`, `ufoReachedWaypoint`, `handleBaseDefense`,
`BaseDestroyedState` and `setupDefenses` found no defect.

Validation: formatting, code map and Release build clean; root `dotnet test` ran 3,526 tests, all green.

## S8 — UI (in progress)

### Landing, base defense and battle request windows (2026-10-13)

`GeoscapeNotifications` maps the branch 5 events to geoscape windows with the reference's texts and
keys. `ConfirmLandingState`: the craft is ready to land near its target, Yes on Enter and No on Esc,
and No reads Patrol while Ctrl is held (`GeoscapePopupAction.ControlAlternative`; the client tracks
Ctrl and relabels the open window). A refused starting condition shows its message as
`CraftErrorState` does. `BaseDefenseState` offers Start Firing and Skip Firing, each resolving the
defense in one step (the round-by-round animation is not drawn yet); a shot-down UFO is reported, and
an attack that goes on leads to the battle request or the destroyed-base window (undefended or nuclear
wording). The campaign's end and a battle request get port windows in plain text, since there is no
defeat cutscene or tactical combat yet. Queued landing windows are dropped when their confirmation
closes or a battle request is pending; a loaded save reopens a pending battle request or base defense.
`CampaignUiSession` carries the landing and base-defense queries, and `EnterCampaignWorld` passes the
battle-request gate (it is a no-op once the world has begun).

The destination sidebar shows the Cydonia button for a lone craft whose world view says
`CanFlyFinalMission` (the same checks as `LaunchFinalMission`); a craft with units asks
`ConfirmCydoniaState`, whose Yes launches the final mission and closes the selection.

Tests: `GeoscapeNotificationsTests` (landing and base-attack windows), `StrategicLandingTests`
(Ctrl+Esc patrols through the client; the battle request window reopens after a load),
`StrategicFinalMissionTests` (the Cydonia button through the client).

### Interception windows and the dogfight timer (2026-10-13)

The geoscape draws a window for each open interception in four grid slots: craft and UFO names,
distance and target distance, the status text, the five mode buttons (modes the interception does not
offer are greyed), the minimize button (self-destruct for a defenceless craft) and a toggle per craft
weapon with its ammunition. Minimized windows become icons at the globe's lower left showing the
distance; clicking one restores it. D3 follows `GeoscapeState::think`: while interceptions exist and no
popup is up, `AdvanceInterceptions` runs every `dogfightSpeed` (30 ms) of host time, banking at most
one extra step; game time runs only when every window is minimized. A water-only craft's
`InterceptionWaiting` shows the `DogfightErrorState` message. The reference's sprites, range bar and
weapon icons are not drawn yet; the windows are text panels. `StrategicInterceptionTests` drives a
window through the client: stepping with time held, mode buttons, minimize and time resuming, and the
icon restoring the window.

The globe now shades seasons from the campaign's `globeSeasons` option (`CampaignWorldOverview.GlobeSeasons`),
so the drawn shadow and the landing shades agree. `StrategicWorldGeoscapeScenarioTests`' crewless-craft
scenario now meets the interception window instead of the old halt.

A self-review found the dogfight timer and windows still active under the destination choice, base
placement and naming; as in the reference, whose think waits beneath those states, they now pause there.

Validation: formatting, code map and Release build clean; root `dotnet test` ran 3,533 tests, all green.
One earlier full run had `StrategicWorldEnduranceTests.PopulatedTicksScale...(detectionQueries: False)`
fail its allocation bound under parallel load; it passed in the runs before and after, and S8 does not
touch the time tick.

## S9 — Scenarios (in progress)

### Geoscape scenarios (2026-10-13)

`StrategicInterceptionTests.AnInterceptionShootsAUfoDownAndTheLandingRequestsItsBattleThroughTheGeoscape`
drives the whole chain through the client: the crewed ship reaches its UFO, the interception window
opens, Aggressive and the dogfight timer bring the UFO down over land, time resumes, the ship asks to
land at the crash site, Yes records the `STR_UFO_CRASH_RECOVERY` request, and a reload keeps the request
and reopens its window. `StrategicBaseAttackTests.TheGeoscapeOpensTheDefenseWindowAndStartFiringShootsTheUfoDown`
runs a defended base through its window, and
`StrategicWorldHuntingTests.TheGeoscapeShowsTheHunterKillerAttackWindowWhichCannotBeMinimized` a
hunter-killer attack (only its modes offered, minimize refused, the timer stepping it while time waits).
Validation: root `dotnet test` ran 3,536 tests, all green, before the hunter-killer scenario, which passes
on its own.

`StrategicLandingTests.LandingAtASiteOrAnAlienBaseRequestsItsDeploymentsBattle` lands at a terror site
and at an alien base: each records its deployment as the mission type, the site its custom deployment
and race, and both survive a reload. It found the alien-base request recording mission texture -1 where
`ConfirmLandingState` calls `setWorldTexture(0, globeTexture)`; the request now records 0.

Validation: formatting, code map and Release build clean; root `dotnet test` ran 3,538 tests, all green.

`StrategicBaseAttackTests` adds skipping the defenses into the battle request window and an
ungarrisoned base's destruction followed by the lost campaign, both through the client.
`StrategicWorldHuntingTests.AnArmedCraftBesideTheHuntedOneJoinsTheHunterKillersFight` covers
`escortsJoinFightAgainstHK`: an armed craft within escort range opens its own attack window beside the
hunted one. Target loss is covered by the pursuit and crashed-UFO tests and the lost-UFO craft screen.

`StrategicInterceptionTests.TheGeoscapeShowsTheDepthMessageAndLetsTimeRunBesideTheWaitingWindow` covers
the TFTD depth case through the client: the water-only craft's `STR_UNABLE_TO_ENGAGE_DEPTH` window, the
minimized waiting interception, and time running beside it.

Validation: root `dotnet test` ran 3,542 tests, all green, before the depth scenario, which passes on its
own; formatting clean.

`PrivateStrategicWorldTests.ImportedWorldRunsPastBranchFiveChoicesToItsNextStop` reclassifies the 19
staged saves with a headless player who declines every landing, disengages every interception and fires
every base defense: Rosigma `Early.sav` now runs past its terror-site landing to the month boundary
(435,988 ticks at one-day speed); every other strategic save keeps its stop (ten at the month boundary,
Rosigma `Begining.sav` at the branch 6 event boundary after 580 ticks), and the eight tactical saves stay
preservation-only. The original first-stop horizon test is unchanged and shares the runner.

The Windows SDL smoke (`--campaign-sdl-smoke artifacts/private-install xcom1 -`, 2026-10-13, Direct3D 11)
ran with the branch 5 client: 481 ticks, 149 presented frames (average presentation 0.99 ms, maximum
6.0 ms; conversion and upload 0.58 ms average), client tick p50 1.8 us and p95 5.2 ms, nine scripted
inputs replayed, campaign time 12:00:00 to 12:35:30 and no popup left open.

S9 is complete.

## S10 — Closure (in progress)

### Decision records (2026-10-13)

[ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md) records D2-D6 and the
branch's intentional differences and named restrictions; [ADR 0032](decisions/0032-geoscape-ui-intentional-differences.md)
gains the branch 5 window differences (text-panel interception windows, one-step base defense, port
windows for the campaign's end and battle requests, the closed stale landing confirmation).
### Interception performance (2026-10-13)

`InterceptionStepBenchmarks` measures one dogfight step at 3.8 us and 2.68 KB with 8 or 128 other flying
UFOs ([performance baselines](performance-baselines.md)): flat in world population and negligible against
the 30 ms dogfight timer; game time does not run while a window is open.

### Ownership and acceptance (2026-10-13)

The [ownership](phase-6-ownership.md#branch-5-interception-ownership-s2-2026-10-12) rows now record the
S7 and S8 outcomes and the final mission, and the plan maps each acceptance criterion to its tests
([plan](phase-6-interception-plan.md#acceptance)). Branch 5 is complete apart from the named refusals in
[ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md) and Phase 8 window polish.

D3's comparison is `StrategicLandingTests.SteppedAndChunkedRunsWithTheSameChoicesAgree`: a landing
scenario run tick by tick and in one-hour chunks, answering the confirmation with Patrol in both, ends in
the same campaign.

Validation: Release build (no warnings), formatting and code map clean; root `dotnet test` ran 3,544 tests,
all green, none skipped (private corpus included), before the D3 test, which passes on its own.

### Refusals resolved before merge (2026-10-13)

Stored vehicles sharing too little ammunition are now served in rule order, the first-loaded rule first,
which is what the reference's `RuleItem` address order follows; a vehicle that loads zero clips (a
division by zero in the reference) stays in the stores. `boundary.base-stored-vehicles` is gone
([ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md));
`StrategicBaseAttackTests.StoredVehiclesCompetingForTooFewClipsAreServedInRuleOrder`.
Validation: formatting and code map clean; root `dotnet test` ran 3,544 tests, all green, none skipped.

A UFO shot down by a craft whose base lies outside every region no longer stops the step: the reference
dereferences the missing region there, and the port starts no retaliation against that base
([ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md));
`StrategicInterceptionTests.AShotDownUfoRetaliatesAgainstTheCraftsBaseUnlessNoRegionHoldsIt` covers both the
retaliation inside a region and none outside.
Validation: formatting and code map clean; root `dotnet test` ran 3,546 tests with one failure, the known
`GlobeRendererTests` allocation flake under the parallel run, which passes alone (9/9).

A leader that crashes on reaching its ground waypoint no longer stops time when it has escorts: as in the
reference, the escorts keep following the wreck, and the crash site's removal sends them back to their own
waypoints (`Target::~Target`, `Ufo::resetOriginalDestination`);
`StrategicWorldHuntingTests.EscortsFollowALeaderThatCrashesOnLandingUntilItsSiteExpires`.
Validation: code map clean; root `dotnet test` ran 3,547 tests, all green, none skipped; the test's formatting
was fixed afterwards.

Crew who fail a destroyed craft's evacuation roll now die instead of stopping time: `CampaignState.KillSoldier`
follows `SavedGame::killSoldier` (default armor, no craft or training, wounds and recovery cleared) and keeps
the soldier in the memorial with the death time. Saves read and write `deadSoldiers`, overlaying each entry
on its saved node (or, for a death in this session, on the living soldier's node) so unmodelled fields and a
battle death's cause survive ([ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md));
`StrategicInterceptionTests.CrewWhoFailTheEvacuationRollDieWithTheCraftAndJoinTheMemorial`.
Validation: formatting and code map clean; root `dotnet test` ran 3,548 tests, all green, none skipped
(the private saves, whose `deadSoldiers` nodes now load, included).

Several research topics marked `unlockFinalMission` no longer refuse the final mission: the runtime linker
keeps the reference's pick (`Mod::loadAll`): walking research by name, a marked topic replaces the one found
so far unless the mod that last updated it loads earlier (`getModLastUpdatingRule` offset), and only that
topic is projected as unlocking the final mission (compiler/cache revision 69).
`StrategicFinalMissionTests.OfSeveralFinalResearchTopicsTheReferencePickMustBeDone`.
Validation: formatting and code map clean; root `dotnet test` ran 3,550 tests, all green, none skipped.

