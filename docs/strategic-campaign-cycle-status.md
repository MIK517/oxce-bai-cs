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
  converts it to an unsigned modulus and returns the raw value. A range of exactly -1 divides by zero; S3 fixes a
  deterministic behaviour for it and records it in an ADR.
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
an existing ending, and the storage-overfull sell flow (skipped before month zero). The instant-event checks cover
a missing rule, an allowed rule (counted in the generated history) and an interrupted one.

The multi-soldier lists are keyed by rule pointer, so soldiers are created in rule address order; the probe keeps
the rules in load order, which is the order the port uses (as for stored vehicles in branch 5).

### Projections and options

- Monthly rules (`CampaignMonthlySettings`): `chanceToDetectAlienBaseEachMonth` (20), `gameOver` `loseMoney`,
  `loseRating` and `loseDefeat` (`loseGame`) and `monthlyRatings` (each mapping replaces the previous one, as the
  reference's map read clears it); runtime settings also carry `defeatScore`, `defeatFunds` and every cutscene's
  `winGame`/`loseGame`. Research rules carry their `cutscene`.
- Event rules carry their payload (`RuntimeEventPayload`), maps in std::map byte order. The closure validator
  rejects an unknown `researchList` topic (`RuleEvent::afterLoad` links it) and reports deferred diagnostics for
  unknown payload items, soldiers, crafts and regions, which the reference looks up only when the payload runs.
- `newMonthCountry` already compiled through the generic rule-script compiler from the country's `scripts:` node;
  the loader's deferral of a top-level `newMonthCountry` key (which the reference ignores) is removed.
- Options `psiStrengthEval` (false), `oxceGeoscapeEventsInstantDelivery` (true) and `autosave` (true) are campaign
  options with save round-trip, reference snapshot seeding and fixed user options.
- `warned` is read from saves and always written, as `SavedGame::save` does.
- Content compiler/cache revision 72.

Tests: `StrategicCampaignCycleRulesTests`, `ContentSnapshotTests.CountryNewMonthScriptsCompileOnTheirCountry` and
`AnEventResearchListMustNameKnownResearch`. The oracles are consumed in S3 and S5.
