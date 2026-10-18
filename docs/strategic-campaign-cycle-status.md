# Strategic campaign cycle status (branch 6)

Branch `claude/strategic-campaign-cycle`; plan and decisions in
[the branch 6 plan](phase-6-campaign-cycle-plan.md). Reference:
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. This document records each slice's evidence once; the
plan's Progress section links here.

## S0 — Plan and baseline (2026-10-17)

Recorded the [branch 6 plan](phase-6-campaign-cycle-plan.md) and
[ADR 0035](decisions/0035-staged-ticks-on-a-shadow-copy.md) (staged ticks on a shadow copy, plan D1).
Corrected the stale branch 5 status lines and the branch 6 name in the [Phase 6 plan](phase-6-plan.md)
and [implementation plan](implementation-plan.md). Pointed the Phase 6 plan's branch 6 commit
purposes at the slice mapping, updated the [ownership ledger](phase-6-ownership.md) row and named the
branch 6 records in `AGENTS.md`. The plan's
[starting audit](phase-6-campaign-cycle-plan.md#starting-audit-2026-10-17) lists the remaining branch 6
refusals and the silent research-cutscene gap.

Baseline on the Windows workstation at `3b3077f3`: Release build with no warnings; root
`dotnet test` passes 3,562 tests (unit and compatibility, including the private corpus) with no
failures or skips in 7 min 56 s; `dotnet format --verify-no-changes` and
`tools/generate-code-map.py --check` are clean.

Corpus stops that branch 6 must turn into positive cases
(`PrivateStrategicWorldTests.ImportedWorldRunsPastBranchFiveChoicesToItsNextStop`, one-day speed,
headless choices from branch 5 S9):

| Save | Ticks to the stop | Stop |
|---|---:|---|
| `vanilla-ufo/early/Begining.sav` | 525,365 | month boundary |
| `vanilla-ufo/early/IronMode.sav` | 527,039 | month boundary |
| `vanilla-ufo/early/Middle.sav` | 396,541 | month boundary |
| `vanilla-ufo/early/_autogeo_.asav` | 396,558 | month boundary |
| `vanilla-tftd/early/Begining.sav` | 526,924 | month boundary |
| `vanilla-tftd/early/IronMode.sav` | 527,039 | month boundary |
| `vanilla-tftd/early/Middle.sav` | 90,174 | month boundary |
| `modded/rosigma/early/Begining.sav` | 580 | event payload |
| `modded/rosigma/early/Early.sav` | 435,988 | month boundary |
| `modded/rosigma/early/Middle.sav` | 429,681 | month boundary |
| `modded/rosigma/early/_autogeo_.asav` | 429,698 | month boundary |

The other eight of the 19 staged saves are preservation-only tactical saves.

## S1 — Evidence and projections (2026-10-17)

### Month rollover oracle

`strategic-monthly-report` (`tools/capture-strategic-monthly-report-reference.ps1`) runs the verbatim
`GeoscapeState::time1Month` with `SavedGame::addMonth`, `monthlyFunding`, `getCountryFunding`,
`getBaseMaintenance`, `getDifficultyCoefficient`, `getFunds`/`setFunds`, `Region::newMonth`,
`Country::newMonth`/`getSatisfaction`/`addActivityAlien`, `Mod::getRandomMission`,
`RuleAlienMission::getWeight` and the `MonthlyReportState` constructor from `calculateChanges()` on, with
`calculateChanges`, `countryList` and `btnOkClick`. Widgets and states are stubs that record calls; the
`newMonthCountry` script is an optional per-country override of its four outputs. Its 17 scenarios cover the
first month (no council bonus, no previous rating), the +400 research bonus, the funding cap, happy and unhappy
countries, pact signing (with the infiltration score) and cancellation (with the average funding), script
overrides, zero funding growing by 1,000, the clamp at zero, the rating ladder, `monthlyRatings` and negative
`monthlyRatingThresholds`, the performance bonus (credited to the new month's income), the debt warning and its
reset, both game-over paths with and without a `loseGame` video, ironman and autosave, 12-entry history trimming,
monthly psi and its `anytimePsiTraining` skip, and the `chanceToDetectAlienBaseEachMonth` roll.

Reference facts this pins, besides the plan's
[behaviour notes](phase-6-campaign-cycle-plan.md#reference-behaviour-that-shapes-the-design):

- `monthlyFunding` credits the funding before `Country::newMonth` changes it; the performance bonus goes through
  `setFunds`, so it adds to the new month's income entry, not the closed month's.
- `getRandomMission(OBJECTIVE_INFILTRATION)` draws before the countries when an infiltration mission has weight
  this month, and the countries draw in save order.
- A country with negative activity can draw over a negative range (`RNG::generate(0, -50)`): `RandomState::generate`
  takes the raw value modulo the range converted to an unsigned 64-bit number (almost always the raw value itself),
  adds the minimum and truncates to `int`. A range of exactly -1 divides by zero; S3 fixes a deterministic behaviour
  for it and records it in an ADR.
- The monthly alien-base roll draws only when an alien base exists, and discovers the first undiscovered one.

### Event payload oracle

`strategic-event-payload` (`tools/capture-strategic-event-payload-reference.ps1`) runs the verbatim
`GeoscapeEventState::eventLogic` and `btnOkClick`, `SavedGame::canSpawnInstantEvent` and
`selectSoldierNationalityByLocation`, and `WeightedOptions::choose`. Soldier generation, templates, crafts and
research completion are recorded calls, because the logistics, readiness and research probes already pin them.
Its 16 scenarios cover region and city choice (a region event still picks a city for nationality), score to the
region or to research, funds, scientists and engineers, named soldiers from templates, merged multi-soldier lists
with location-based nationality, items through every list (std::map aggregation) with instant delivery or
one-hour transfers, `invert` removal from stores and then from crafts that are not out, crafts by instant
delivery (`checkup`) or transfer (`STR_REFUELLING`), research picks that skip known topics unless a free topic
remains, lookups, free topics and diary entries, adhoc scripts running first, cutscene endings that never replace
an existing ending, the storage-overfull sell flow (skipped before month zero), empty names (`""`, `STR_NULL`, and
`STR_NONE` for items) that name no rule, an unknown `spawnedPersonType` that spawns nobody, and an unknown item name
that throws (`Mod::getRule` with its error flag) after the score and funds have already changed. The reference game
stops there; S5 chooses a deterministic behaviour for unknown payload names and records it in an ADR. The
instant-event checks cover a missing rule, an allowed rule (counted in the generated history) and an interrupted one.

The multi-soldier lists are keyed by rule pointer, so soldiers are created in rule address order; the probe keeps
the rules in load order, which is the order the port uses (as for stored vehicles in branch 5).

### Projections and options

- Monthly rules (`CampaignMonthlySettings`): `chanceToDetectAlienBaseEachMonth` (20), `gameOver` `loseMoney`,
  `loseRating` and `loseDefeat` (`loseGame`) and `monthlyRatings` (each mapping replaces the previous one, as the
  reference's map read clears it); runtime settings also carry `defeatScore`, `defeatFunds` and every cutscene's
  `winGame`/`loseGame`. Research rules carry their `cutscene`.
- Event rules carry their payload (`RuntimeEventPayload`), maps in std::map byte order. The existing event check in
  `MissionEventRuleCatalog` rejects an unknown `researchList` topic (`RuleEvent::afterLoad` links it) and defers
  unknown payload items, soldiers, crafts and regions, which the reference looks up (and throws on) only when the
  payload runs. It now follows `Mod::isEmptyRuleName` (`""` and `STR_NULL` name no rule; `STR_NONE` too for items),
  reports each list under its own key, and no longer reports `spawnedPersonType`, which the reference looks up
  without an error. A `spawnedSoldier` template is parsed when content links, as for manufacture and recruit
  templates; the reference parses it only when the event fires, so a malformed template fails the load earlier.
- `newMonthCountry` already compiled through the generic rule-script compiler from the country's `scripts:` node;
  the loader's deferral of a top-level `newMonthCountry` key (which the reference ignores) is removed.
- Options `psiStrengthEval` (false), `oxceGeoscapeEventsInstantDelivery` (true) and `autosave` (true) are campaign
  options with save round-trip, reference snapshot seeding and fixed user options.
- `warned` is read from saves and always written, as `SavedGame::save` does.
- Content compiler/cache revision 72.

Tests: `StrategicCampaignCycleRulesTests`, `ContentSnapshotTests.CountryNewMonthScriptsCompileOnTheirCountry`,
`AnEventResearchListMustNameKnownResearch` and `EventPayloadNamesAreCheckedOnceAndEmptyNamesNameNoRule`. The
oracles are consumed in S3 and S5.

### S1 audit (2026-10-17)

An independent review of the slice (`git diff 2043a07d`) confirmed the oracles match their captures, the stub
fidelity of the monthly probe, the option defaults, `warned` and the `scripts:` source of country scripts. Fixes:

- The event probe's rule lookups ignored their error flag, so the oracle showed unknown names silently dropped;
  they now throw as `Mod::getRule` does, the scenarios use empty names instead, and a new scenario pins the throw.
- S1 had added event checks to the closure validator that duplicated `MissionEventRuleCatalog`'s, reporting each
  unknown reference twice; the duplicate is removed and the catalog check is corrected as described above.
- `gameOver` and `monthlyRatings` rejected values the reference accepts: a `gameOver` that is not a mapping is now
  ignored, and `monthlyRatings` is read like the reference's map (null or a scalar clears it, a repeated score
  keeps its first text).
- Tests now count diagnostics, cover empty names, repeated scores, a cleared mapping, a `gameOver` split across
  layers, and keys whose UTF-8 and UTF-16 orders differ; the monthly probe keeps mission weights in a `std::map`.
- Not changed: a country's `scripts:` key still carries the generic "dynamic country script values" deferral that
  item rules also show; the script compiler reads it from the preserved documents.

Validation after the fixes: formatting and code map clean; root `dotnet test` ran 3,575 tests, all green, none skipped (private corpus included).

## S2 — Staged ticks (2026-10-17)

Implements [ADR 0035](decisions/0035-staged-ticks-on-a-shadow-copy.md) without a production stage point yet; the
month boundary still refuses in S2.

- `CampaignTimeDispatcher` asks the effects for the next tick's stage points after its preflight passes, checks
  the later stages before mutating anything, and after each staged level rechecks the rest of the tick on real
  state. `ICampaignTimeEffects` gains default `StagePoints`, `PreflightStages` and `Restage` members, so other
  implementations are unchanged.
- Capabilities register stage points (`CampaignCapabilityRegistry.StagePoint`) and transient copies
  (`Transient`). The world copies open and pending interceptions, the interception UFOs, the landing queue and its
  numbering, the request hand-off flag, ignored UFO contacts and the damaged-base notice.
- `CampaignState.CreateShadow` restores the captured snapshot with the campaign's own random source and copies the
  transient state, including the live UFO records and craft destinations that loading would normalize;
  `PreflightStages` repeats the tick's preflight on the shadow, applies the levels down to each
  stage point, preflights there and puts the random state back. `Restage` throws if the real recheck refuses
  where the shadow passed, and the campaign then refuses every later command because it is mid-tick.
- Test seams: `CampaignState.StageEveryTick` (per async flow, ahead of) `StageEveryTickEverywhere`, set by
  `OXCE_VERIFY_STAGING=1` in `GameplayVerification`; `VerifyStagedSnapshots`, on in every test, compares each
  shadow's snapshot with the original's when it is created and at every stage point; `StagedTicksOnThisThread`
  counts staged ticks; `CampaignState.TestCapabilities` adds capabilities to campaigns created on a flow and to
  their shadows. The benchmark assembly can now see gameplay internals.
- `IStatefulRandomSource` now states that `State` must capture everything the next draws depend on.

Tests: `CampaignTimeDispatcherTests` (stage order and recheck, shadow refusal, ignored stage bits),
`CampaignCapabilityRegistryTests.StagePointsNeedALaterLevelAndTransientCopiesMustMatchByName`,
`StrategicStagedTickTests` (a refusal after the stage point stops before the tick with the snapshot and random state
unchanged; a passing shadow lets the tick run identically to an unstaged campaign; two days of a UFO lifecycle staged
at all 288 ten-minute boundaries match the direct run, serialized events and final snapshot; a shadow keeps an ignored
contact) and `StrategicInterceptionTests.AShadowCopyKeepsAMinimizedInterceptionAndTimeRunsTheSameStaged`.

Verification run with every tick staged (`OXCE_VERIFY_STAGING=1`) before the audit: the unit and public compatibility
tests (3,548) and the private corpus tests (30) all pass. Nine tests turn the mode off for themselves because they measure
unstaged cost (allocation budgets, the time profile's call counts, the endurance measurement) or record every random
draw (the alien-base generation and hunting oracles, the generated-variant draw count); a shadow's draws would
appear in their records although the state is put back.

Cost: [performance baselines](performance-baselines.md#staged-ticks-on-a-shadow-copy-branch-6-s2-2026-10-17).

### S2 audit (2026-10-17)

An independent review found no defect in the dispatcher or the staged preflight, and confirmed that the shared
interception objects are only read by levels a shadow runs. Fixes:

- ADR 0035 still forbade rewinding the random source while the implementation shares and restores it; the ADR now
  allows exactly that restore and the random source contract requires complete state.
- The staged-vs-direct tests ran their direct baselines staged in a verification run, and the every-tick test could
  pass without simulating anything; baselines now run unstaged, and the tests count staged ticks, check the final
  time and compare serialized events.
- No test covered the transient copies that matter (minimized interceptions, ignored contacts); two tests now do.
- Verification now compares snapshots, not only preflight outcomes: `VerifyStagedSnapshots` (see above). Snapshots
  are compared after one restore each, so repairs that loading makes and the next tick would make anyway do not
  count. Its first run found a real gap: loading turns a UFO that follows a mission site into one bound for an
  anonymous waypoint (`Ufo::finishLoading`), so a shadow lost the follower and its site expiry diverged
  (`StrategicWorldSiteExpiryTests.ForcedSiteExpiryRestoresUfoWaypointBeforeMovementAndReload`). The shadow now takes
  the live UFO records and craft destinations.
- A recheck failure after the higher levels ran now leaves the campaign refusing every command.
- Transient copies are named and matched by name; the base-attack notices, set and taken within one five-second
  handler, are no longer copied; the benchmark measures a fresh midnight tick per invocation with the switch set
  outside the measurement.
- Validation after the fixes: formatting and code map clean; root `dotnet test` ran 3,581 tests, all green, none
  skipped (private corpus included), and again with `OXCE_VERIFY_STAGING=1`, all 3,581 green.
- Not changed: tests that record every random draw keep turning staging off. A forked random source would remove that,
  but scripted test sources cannot be forked faithfully, and sharing with an exact restore is equivalent.

## S3 — The month in reference order (2026-10-17)

### Part 1: the month rollover

The month boundary no longer refuses. `GeoscapeState::time1Month` runs in order: `addMonth` (months passed, then
the purchase log; the reset now precedes scheduling as in `SavedGame::addMonth`), `determineAlienMissions` (the
existing world scheduling), monthly psi training (`SoldierReadiness.TrainPsiMonthly`, skipped with
`anytimePsiTraining`; stat strings are presentation), `monthlyFunding`, the monthly report and the monthly alien-base
roll. The month tick is a stage point ([ADR 0035](decisions/0035-staged-ticks-on-a-shadow-copy.md)), so the rest of
the tick is preflighted on a shadow copy that has run the month; a month handler that finds input it cannot use
(malformed histories, overflow, a country script the port cannot run) refuses the tick there before anything changes.

- `MonthlyCycle` (gameplay) ports `monthlyFunding`, `calculateChanges`, `Region::newMonth`, `Country::newMonth`,
  `Mod::getRandomMission` and the report constructor's rating, performance bonus, debt warning and game over;
  `StrategicMonthlyReportOracleTests` replays all 17 `strategic-monthly-report` scenarios exactly, with every
  random call of the report (the probe's `spawnEvent` stub makes no draw, so the pact events' timer draws are pinned
  by the campaign tests only).
- The report (`CampaignMonthlyReport`, `MonthlyReportReady`) is a modal hold ([ADR 0036](decisions/0036-campaign-cycle-intentional-differences.md)):
  it holds the clock from the next tick (the opening tick's later levels still run, as a popup pauses after the
  tick), refuses every command but `AcknowledgeMonthlyReport`, is copied to shadows and dropped by a reload. A
  game-over report's acknowledgement plays the lose cutscene, which ends the campaign when its video is `loseGame`.
- `newMonthCountry` runs at its call site with the global event scripts around the country's own script, through
  the price-script bindings with a live (non-forecast) context: Country, RuleCountry, GeoscapeGame, RuleMod and tag
  reads. A binding the port does not support refuses the month.
- Signed and cancelled pacts schedule the countries' `signedPactEvent` and `rejoinedXcomEvent`.
- The monthly alien-base roll draws only while an alien base exists and discovers the first undiscovered one, after
  the report.
- Country draws over negative ranges, a missing country and an undefined lose cutscene are recorded in ADR 0036.

Tests: `StrategicMonthlyReportOracleTests`, `StrategicMonthlyCycleTests` (reference order, the modal hold, monthly psi
with and without `anytimePsiTraining`, a country script's funding change and pact event, an unsupported country
binding refusing before anything changes, a two-bad-months game over ending the campaign, the monthly alien-base
roll). Tests that pinned the old month refusal now run the month: `StrategicWorldCompositionTests`,
`StrategicEventScriptTests`, `StrategicWorldArrivalResearchEnduranceTests`, and the allocation budgets in
`CampaignFoundationTests` now stop before the month tick.

Corpus (`PrivateStrategicWorldTests`, one-day speed): every vanilla UFO and TFTD save now runs its month and stops on
the open report. Acknowledging every report, six of the seven run to the 40-day limit and TFTD `Middle.sav`
(Superhuman, `defeatScore` -900, rated -982 and -974) loses the campaign at its second report, as the reference
would. Rosigma's saves stopped at the month boundary until part 2.

### Part 2: missions without waves and real-globe work bounds

- A mission rule without waves is valid for mission scripts. Rosigma's `STR_ORK_BASE_START` (and its other
  base-starting missions) has none: `AlienMission::start` places or picks the operation base, `think` has nothing to
  spawn, and the mission ends on its next pass. With a script delay the port follows the reference exactly; without
  one the reference reads past the wave list, and the port takes a zero timer (ADR 0036).
- The forecast work bounds were calibrated on fixture globes. On Rosigma's 3,253-point globe one base objective is
  charged about 650,000 alien-base checks, its monthly scripts about 1,180,000 checks for site and base targeting and
  over 10,000,000 for the possible operation-base searches, and one month tick of `Early.sav` about 183,000,000 land
  work (each newborn replacement about 5,560,000). The production bounds are now 100,000,000 alien-base checks,
  100,000,000 mission-script checks and 1,000,000,000 land work. They limit what the forecasts admit as a worst case
  (every land selection retried 100 times), not the work a tick does.
- The fixture tests keep the calibrated bounds (1,000,000 and 10,000,000 for land) through a test-only seam,
  `CampaignWorld.FixtureWorkBoundsEverywhere`, set by the test assemblies; tests that load real mods set
  `CampaignWorld.ProductionWorkBounds` on their flow. The bound tests therefore still reach their refusals with
  small workloads.

Tests: `StrategicMonthlyCycleTests` gains a zero-wave mission started by a script: it picks the region's existing
base, survives a reload and ends on its next pass.

Corpus after part 2: unanswered, Rosigma `Middle.sav` and `_autogeo_.asav` stop on their open report (429682,
429699), `Early.sav` on its landing (6290) and `Begining.sav` on an event with consequences (580, S5). Answering every
window, Rosigma `Middle.sav` and `_autogeo_.asav` run to the 40-day limit and `Early.sav` runs through its month to an
event with consequences (436348, S5). The records are written to `artifacts/private-horizon-stops*.json` on every run.

### S3 audit (2026-10-18)

An independent review of ff847989 confirmed the month's order, monthly psi, the alien-base roll, the report's
decisions, the country arithmetic, the script's inputs and atomic refusal, the pact events, the zero-wave missions
against `AlienMission::start`/`isOver`, and the work-bound seam. Fixes:

- A base defense opened by the month tick's later levels deadlocked with the report: each window's gate refused the
  other's answer while the report held the clock. The report is now answered first, as its popup is queued first:
  its preflight precedes the interception and landing ones, and the base-defense gate passes its acknowledgement
  (ADR 0036). A pending deployment request already stops time, so no report opens beside one.
- A later country's `newMonthCountry` read the earlier countries' pacts from before the month; `Country::newMonth`
  sets each pact at once, so the pacts are now written back before every country script.
- A performance bonus outside the `int` range refused the month; it now pays nothing (ADR 0036).
- An undefined lose video named its cutscene in the acknowledgement; it now names none, as nothing plays.
- New tests: the report and a base defense in the same tick, a later country script seeing an earlier new pact (it
  fails without the fix), a reload dropping an open report, an undefined lose video, out-of-range bonuses.

Refusal owners: the new country-script refusals ("Country script requires unsupported binding ...", "Country
scripts exceed the supported event execution limit.", "Country scripts exceed the supported work limit.") and the
zero-wave refusals that remain in alien-base generation, hunting and site research are S9's inventory (D5); a site
script naming a mission without waves is recorded in ADR 0036. Interim gaps with owners: saving while a report is
open (a game-over report then escapes on reload) is refused in S4; the app does not yet show or answer the report
(`MonthlyReportReady`) or model `timerReset()` before `monthlyFunding`, which is S7. Not changed: the private test
classes set the production bounds in their constructors, which relies on xUnit carrying the constructor's
execution context into the test; a class that forgets it runs with fixture bounds and stops at a work limit.

## S4 — Acknowledgement effects (2026-10-18)

`MonthlyReportState::btnOkClick` now runs on `AcknowledgeMonthlyReport` (D2, D3):

- Every soldier at a base, in base and soldier order, gains a month of service (`SoldierDiary::addMonthlyService`)
  and then its commendations (`manageCommendations`). `SoldiersCommended` lists what each soldier was given or
  raised to (CommendationState). Soldier bonuses follow from the commendation levels, as before.
- `PsiTrainingReminder` follows when a base has a finished psi lab and `anytimePsiTraining` is off.
- `CampaignSaveRequested` asks for an ironman save, or an autosave when the option is on. The windows stack in event
  order, so the save shows first. A failed month gives no service; it plays its cutscene and then requests the ironman
  save in an ironman campaign.
- `CampaignState.QuerySaveRefusal` names the pending acknowledgement that forbids saving (capabilities register
  `SaveGate`). The open monthly report does; the SDL host skips its exit save and says why. The message window is
  S7.

Data:

- `SoldierPersonalState.Diary` types the diary's counters (as `SoldierDiary::save` names them, the two flags as 0
  or 1), its kill list and its mission IDs; `CampaignSnapshot.MissionStatistics` types the save's
  `missionStatistics`. Saves write the counters as the reference does (non-zero only) and keep the kill list, the
  mission IDs and the statistics from the source, since only battles (Phase 7) change them; a diary without a source
  writes its kill list and mission IDs from the typed state.
- Commendation rules project their criteria (std::map order), kill criteria, mission filters, `requires` and `units`.
  Content compiler/cache revision 73.

`SoldierCommendations` ports `manageCommendations` and every total it reads: the criteria without nouns (including
friendly fire failing for the dead or missing, unknown names passing, and statistics loops counting a repeated
mission ID again), the four noun criteria in byte order of their nouns, and the career, mission and turn kill criteria
with rank, race, weapon, ammo, status, faction, side, body part, battle-type and damage-type details. The
`strategic-commendations` oracle (`tools/capture-strategic-commendations-reference.ps1`) compiles the reference
methods verbatim over its scenarios (16 after the audit), and `StrategicCommendationOracleTests` replays them
all. A commendation awarded
repeatedly in one call (several levels at once) is pinned there. The paths the reference leaves undefined (no
criteria, a level past the list, a kill without a span, an absent kill field) are recorded in ADR 0036.

Tests: `StrategicCommendationOracleTests`; `StrategicMonthlyCycleTests` (service, a commendation for every soldier,
the psi reminder, the autosave and the save refusal, with a save round trip; ironman, autosave and no save
requests; a failed ironman month requesting its save without service).

### S4 audit (2026-10-18)

An independent review of 3620549e checked `manageCommendations` line by line and ran 6 further probe scenarios
through the port, all matching; it confirmed the `btnOkClick` effects, the event order, the projection and the ADR
0036 entries. Fixes:

- A base defense is not saved and its opening has already parked the UFO, so it now also refuses saving (D2); the
  branch 5 interception status claimed a reload reopened it and is corrected.
- `CampaignState.CaptureForSave` checks the refusal and captures under one lock; the SDL host uses it.
- `Mod::getItem` names no item for `STR_NONE`; the evaluator now follows it (and the probe stub).
- Mission statistics without `region`, `country` or `ufo` take the reference constructor's values
  (`STR_REGION_UNKNOWN`, `STR_UNKNOWN`, `NO_UFO`), so `IsUfoMission` is false for them.
- A commendation whose rule no longer exists is dropped on load, as `SoldierDiary::load` does.
- The psi reminder counts any non-zero psi-lab capacity, as the reference's truth test does.
- A new soldier's diary writes its typed kill list and mission IDs (kills now also type their victim's type, name
  and ID).
- The oracle gains the review's 6 scenarios (a noun criterion beside a plain one, career batches across OR groups,
  unsorted spans, an unknown weapon with known ammo, zero thresholds, duplicate nouns, owned levels beside nouns) and a
  `STR_NONE` and repeated-region scenario: 16 in all. New tests: projected items, research and soldier types through
  the campaign (`CommendationsReadProjectedItemsResearchAndSoldierTypes`), the base-defense save refusal, and the
  private `ImportedDiariesAndMissionStatisticsAreTypedAndKept` (vanilla UFO `Middle.sav`: typed kills, counters,
  mission IDs and statistics read back equal after a rewrite).

Not changed: a soldier without personal state (a port fixture form; real saves always carry it) gets no service.
`Options::soldierDiaries` off, which makes the reference write no diaries or statistics, is not modelled (it predates
this branch).

## S5 — Event payloads (2026-10-18)

A popped event now runs `GeoscapeEventState::eventLogic` inside the half-hour tick, and its window holds the clock
until `btnOkClick` (D2):

- `time30Minutes` counts every event down in list order; one whose time has come and that research has not
  interrupted runs its payload at once (`CampaignState.RunEventPayload`), then the finished events are removed. A mission
  site's despawn event is an instant event (`SavedGame::canSpawnInstantEvent`): it counts in the generated history and
  runs where the site despawns, after its research and counters and before its score.
- The payload, in reference order: adhoc mission scripts (the existing `CampaignWorldAdhocScripts` with this month's
  score and funds), the region and the city that places it (RuleRegion::getCities: the named point areas), score to the
  region or to research, funds, persons (staff by 24-hour transfer; soldiers from the template, named or with a new
  name, their nationality by the city's country or region), the merged soldier lists (load order), items (std::map by
  type) given at once, by one-hour transfer or taken (`invert`: stores, then crafts not out, base by base), a craft
  (ready, or by transfer as if bought), and research: an unknown topic or a known one with a free topic left, its diary
  entries, lookup, free topic and the research side effects. Manufacture now shares the soldier spawn.
- A stage point after the half-hour level of every tick where a payload can run (ADR 0035) runs it on the shadow first,
  so a payload that cannot run refuses the tick with nothing changed (ADR 0036); the forecasts that refused every due event whose interruption
  they could not prove are gone, with the named refusals "Strategic event consequences require campaign event
  execution (Phase 6 branch 6)." and "Mission site despawn requires research or event payloads.".
- `CampaignEventWindow` (rule, region, city, the item rows, the research articles) is queued per popped event,
  copied to shadows, dropped by a reload and refuses saving (`SaveGate`). Commands other than
  `AcknowledgeCampaignEvent` wait; this tick's monthly report is answered first, and a base defense opened later in
  the tick after the events. `AcknowledgeCampaignEvent` reports the cutscene (which wins or loses an unended campaign by
  its video), the storage-overfull sell screen from the first month, and the research articles.
- The adhoc scheduling inputs pay no out-of-range performance bonus, as the report does (ADR 0036).
- The production land-work bound rises to 10,000,000,000: Rosigma `Begining.sav`'s month tick charges about
  2,550,000,000 (worst cases, as in S3).

Tests: `StrategicEventPayloadTests` replays the `strategic-event-payload` scenarios on a campaign built like the probe's
world: region, city, score, funds and staff; items given at once or by transfer; `invert`; a craft at once or by
transfer; adhoc scripts; every random draw. The research scenarios run on real rules (the probe's getOneFree stub draws
nothing), as do soldiers (named from a template, merged lists). Further tests: an unknown item refusing the tick unchanged,
cutscene endings, the overfull sell screen from month zero, events popping in list order, the report before the
month's events, and a reload dropping an open window. About forty tests that pinned the old refusals now assert that the
tick runs (`AssertEventTickRuns`); the persistence and site-timer tests pin the popped window and despawn event.

Corpus (`PrivateStrategicWorldTests`): Rosigma `Begining.sav` runs past tick 580 and stops on its event window at 581.
Answering every window, all four Rosigma saves and every vanilla save but TFTD `Middle.sav` (which loses the campaign)
run to the 40-day limit.

### S5 audit (2026-10-18)

An independent review of 0792cb3b confirmed the `eventLogic` statement order, draws and lookups, the countdown loop,
the window ordering and the `btnOkClick` effects. Fixes:

- The stage point read the sites before the tick, so a site born and despawned in the same half-hour tick ran its
  despawn payload unstaged (a failing payload then escaped mid-tick). The stage point now also covers ticks with a due
  mission wave, a mission generated this tick, or a month: every way a site is born (ADR 0035). Staging every half-hour
  tick was tried and broke the allocation budgets (about 1 MB a copy).
  `ASiteBornAndDespawnedInOneTickRunsItsDespawnPayloadStaged` fails with the old condition.
- Payload failures from argument checks (craft or soldier values) and a later site's research grant left without
  capacity by an earlier payload now refuse the tick instead of escaping.
- Manufacture and event soldiers always load their template, an empty one included, so a zero mana is re-rolled as
  `Soldier::load` does.
- An empty or `STR_NULL` region now names no region (ADR 0036); instant events respect the event-history population
  bound; the unused `highest` parameter of the despawn check is gone.
- The rewritten tests now say which events pop (`AssertEventTickRuns(campaign, popped: [...])`), recorded from the
  run and checked: several pin an empty list, the interruption research completing earlier in the tick. Shadow draws
  leave the recording random sources (`Restore` drops them).
- New tests: an interrupted event beside one that pops, and an event scheduled by a payload's research joining after
  the countdown.
- ADR 0036 records the string-ID person name, the load-order soldier lists, the unmodelled debug log, and the save the
  report requests under event windows (the host saves after them, S7).

Not changed: `ApplyResearchSideEffects` applies counters and events before removing obsolete projects, the reverse of
`handlePrimaryResearchSideEffects`; nothing observable depends on the order today. Nationality by city with
hire-by-country or hire-by-region odds is exercised only through the shared `SelectNationality` (recruitment and
production tests).

## S6 — Strategic endings (2026-10-18)

Plan D4's one ending rule, `CampaignState.EndByVideo`: while the campaign has no ending, a video that wins ends it
won and one that loses ends it lost (a video with both loses, as the reference sets the win first).

- Research (`GeoscapeState::time1Day`, 3e): after a discovery and its lookup, the topic's cutscene is queued
  (`CampaignCutscenePlayed`), then its free topic's; the first video that wins or loses ends the campaign inside the
  day tick. An undefined video plays nothing (ADR 0036).
- The tick's five-second level reports an ending set in the tick once (`CampaignEnded` with the video that ends the
  game: the defeat video for a loss, as `time5Seconds` plays it at once, otherwise the first video the tick queued)
  and, in an ironman campaign, the save that `time1Day` and `time5Seconds` push. Losing every base reports the same way.
- Event cutscenes (S5) use the same rule on their acknowledgement; the monthly failure (S3, S4) keeps
  `MonthlyReportState::btnOkClick`'s own rule (only `loseGame`, without the unended check).
- An ended campaign's open windows no longer forbid saving it (ADR 0036).
- `time5Seconds` returns early only once the campaign is lost; a won campaign's tick runs on, as in the reference.
  Losing every base still ends the campaign lost there (branch 5).
- An ended campaign refuses every command ("This campaign has ended.") and keeps its ending in saves (`end`).
- Phase 7 inputs, defined and not claimed: the final mission's victory (`BattlescapeState` sets `END_WIN` after the last
  stage) and a battle's research and cutscenes reach the same rule when tactical play exists.
- Cutscene playback is Phase 8 presentation; the windows that show an ending are S7.

Tests: `StrategicEndingTests` (a winning topic, a plain topic whose free topic loses, a winning topic whose free topic
would lose, an undefined video, with the game-over video; the ended campaign refusing time before and after a reload;
the ironman save request; an ending on the month boundary leaving the report moot and the campaign savable).

### S6 audit (2026-10-18)

An independent review of 73ac8c5e confirmed the cutscene rule against `time1Day` 3e (the project's own video, then
the free topic's, the unended check, a both-flags video losing), that only `time1Day` plays research videos, the
`time5Seconds` change, and that no path faults the campaign. Fixes:

- The ironman save after an ending inside a tick was missing (research and losing every base); the tick's five-second
  level now requests it once.
- `CampaignEnded` names the video that ends the game; the research path no longer reports each queued video as if it
  ended the game, and the loss window no longer says "No X-COM base remains." for every loss.
- Windows opened in an ending's tick could never be answered and forbade saving; an ended campaign is now savable
  (ADR 0036).
- ADR 0036 records the undefined research cutscene, the moot windows and the preflight that cannot foresee a loss;
  the docs no longer claim the monthly failure uses `EndByVideo`.

Not changed: a won campaign then losing every base in the same tick keeps the win (practically unreachable); the
pre-existing `MonthsPassed` guard on the no-bases loss stays.
