# Phase 6 branch 6 delivery plan: monthly campaign cycle and Phase 6 closure

Approved 2026-10-17. Branch `claude/strategic-campaign-cycle`, from `main` at
`3b3077f3` (merged branch 5, PR #75). Reference: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.
Scope and acceptance come from [Phase 6 branch 6](phase-6-plan.md#branch-6--monthly-campaign-cycle-and-phase-6-closure);
this plan replaces that section's commit purposes (mapping below). Slice evidence and audits are
recorded in [strategic campaign cycle status](strategic-campaign-cycle-status.md); checkpoints, current scope and remaining guards go in
[Progress](#progress).

## Starting audit (2026-10-17)

Branch 5 closed with no named refusal ([branch audit](strategic-interception-status.md#branch-audit-2026-10-17)).
These branch 6 boundaries still stop time or refuse before mutation:

- `CampaignPreflightOrder.MonthBoundary` (`CampaignState.RegisterCalendar`): every month boundary
  stops with "Monthly campaign simulation is not implemented yet".
- `CampaignWorldEvents.EventPayloadReason`: a due event that is not interrupted by research stops
  time; `AdvanceEvents` throws the same reason at execution.
- `CampaignWorldSites`: "Mission site despawn requires research or event payloads." (instant
  despawn events, `SavedGame::canSpawnInstantEvent`).
- `newMonthCountry` is deferred in `CampaignStartRuleLoaders` (no compile, no call site).
- Monthly hooks that already run in the month handler: `MonthsPassed` (100), world scheduling
  (105: arc, mission and event scripts and alien-base upgrades/evolution) and the purchase-limit
  reset (110). The reference clears the purchase log in `addMonth` *before* `determineAlienMissions`,
  so 105/110 are out of order (no reader is affected; fix in S3).
- Isolated, not yet called: `SoldierReadiness.TrainPsiMonthly`, `CampaignWorldAdhocScripts`
  (event adhoc tags).
- Not modelled: `warned`, monthly funding/report evaluation, region/country `newMonth`, pact
  signing at month end, signed-pact/rejoined events, monthly alien-base detection, soldier diary
  and monthly commendations, `oxceGeoscapeEventsInstantDelivery`,
  `selectSoldierNationalityByLocation`.
- Silent gap found by this audit: research `cutscene` is parsed (`ResearchRule.Cutscene`) but not
  projected to `RuntimeResearchRule`, so a research topic whose video has `winGame`/`loseGame`
  ends nothing. This is a strategic ending and belongs here.
- Corpus: `PrivateStrategicWorldTests.ImportedWorldRunsPastBranchFiveChoicesToItsNextStop` stops
  nine staged saves at the month boundary and Rosigma `Begining.sav` at tick 580 on the event
  payload; they become the positive cases.

## Reference behaviour that shapes the design

- `time1Month` runs first on a month-boundary tick, then the same tick falls through `time1Day`,
  `time1Hour`, `time30Minutes`, `time10Minutes` and `time5Seconds`. Its order: `addMonth`
  (months, purchase log), `determineAlienMissions`, monthly psi training (unless
  `anytimePsiTraining`; `calcStatString` follows), `timerReset`, `monthlyFunding`, the
  `MonthlyReportState` popup, then the `chanceToDetectAlienBaseEachMonth` roll that discovers the
  first undiscovered alien base.
- `MonthlyReportState` does its gameplay work in the constructor, inside the tick:
  `calculateChanges` (region `newMonth`, +400 on the closed month's research score after the first
  month, `Country::newMonth` with the `newMonthCountry` script, pact signing with the infiltration
  `points` as pact score, funding history), the rating against `defeatScore` or
  `monthlyRatingThresholds`, the performance bonus added to funds, the `warned` debt warning and the
  game-over decision (two bad months, or funds at or below `defeatFunds` while warned), then
  `spawnEvent` for each country's `signedPactEvent` / `rejoinedXcomEvent`.
- The OK click is a player action after the tick: monthly service and `manageCommendations` for
  every soldier, the psi-training reminder, ironman or autosave; on game over, a second OK plays
  `loseRatingCutscene` or `loseMoneyCutscene` and sets `END_LOSE` only if that video has
  `loseGame` (otherwise play continues).
- `monthlyFunding` uses the country funding *before* `Country::newMonth` changes it, so the month's
  funds are deterministic; the random funding change affects the next month.
- A due event runs `GeoscapeEventState::eventLogic` in its constructor, inside the 30-minute
  handler: adhoc mission scripts, region and city, score, funds, persons, soldiers from templates,
  items (or their removal with `invert`), a craft, research with lookup and `getOneFree`, and research
  side effects. Its OK click applies the event cutscene ending and the storage-overfull flow.
  Research cutscenes set the ending inside `time1Day`.
- Popups are modal and never saved; the game cannot be saved while one is open.

## Decisions

- **D1 — Staged ticks at stage points** ([ADR 0035](decisions/0035-staged-ticks-on-a-shadow-copy.md)). A month-boundary tick is preflighted in two
  stages: the month handlers' own preflights first, then the rest of the fallthrough
  (`highest = OneDay`) evaluated on a *shadow copy* of the campaign after its month handlers have
  run. The shadow is the save-neutral snapshot plus random state plus transient state
  (interceptions, the landing queue, pending acknowledgements). If the shadow's remaining preflight
  refuses, time stops before the tick and the real campaign is untouched. Otherwise the real tick
  runs, recomputes the remaining preflight on real state (its pass feeds execution) and checks that
  it agrees with the shadow. A second stage point follows the 30-minute handler when an event
  payload ran (`highest = TenMinutes`). The real campaign never rolls back, and staging costs one
  copy per staged tick (about 0.3 ms measured in 4c). The ADR amends the Phase 6 plan rule against
  speculative scripts and rollback, but only for exact shadow staging.
  *Alternative:* forecast every month outcome that later handlers in the tick read (new missions
  and bases, psi stats, pacts, discovered bases, funds) as envelopes inside the existing single
  preflight. It keeps today's model, but it repeats the forecast growth that left the 4c named
  restrictions R4–R6, now on every month tick.
- **D2 — Modal acknowledgements are gameplay state.** The monthly report and event windows become
  pending acknowledgements that hold the clock, like landing confirmations. Commands apply their
  OK effects (commendations, endings). Saving is refused while an acknowledgement with gameplay
  effects is pending, because the reference cannot save under a modal popup and a reload would lose
  those effects. Purely informational popups do not block.
- **D3 — Monthly commendations are owned here.** The monthly call site needs
  `SoldierDiary::addMonthlyService` and `manageCommendations`, so this branch types the diary fields
  that evaluation reads (imported battle entries included) and awards commendations. Phase 7 adds
  new battle entries and reuses the same evaluation in debriefing. Stat strings stay presentation
  (Phase 8).
- **D4 — Strategic endings use one ending rule.** This extends branch 5 D5. `END_WIN` / `END_LOSE`
  come from research cutscenes (inside the tick), event cutscenes and monthly failure (on
  acknowledgement), and no base left (existing), each through the video's `winGame` / `loseGame`.
  After an ending, the existing "This campaign has ended." refusal applies. Cutscene playback is
  Phase 8; the UI shows a port window. Phase 7 inputs for final-mission victory are defined but not
  claimed.
- **D5 — Phase 6 closure classifies every refusal.** The branch cannot merge with a named refusal
  that no plan assigns elsewhere (AGENTS.md). S9 lists every remaining refusal string. Each one is
  removed, re-justified as reference behaviour (modal holds, campaign ended, invalid input), or
  explicitly assigned with corpus evidence that no staged save reaches it. The 4c restrictions
  R4–R6 are first re-evaluated with the D1 mechanism, because several are intra-tick forecast
  limits of the same kind.
- **D6 — Managed extension state stays refused (`EXT1011`)** until a state-owning extension exists.

## Slices

Each slice is followed by an independent audit before the next.

- **S0** — Record this plan and [ADR 0035](decisions/0035-staged-ticks-on-a-shadow-copy.md); branch name in the
  [Phase 6 table](phase-6-plan.md#delivery-shape); the branch 6 status file; baseline validation and
  the corpus month and event stops.
- **S1** — Evidence and projections. C++ probes: `monthlyFunding` + `calculateChanges` +
  `Country::newMonth` (scripted draws: happy, unhappy, cap, zero funding, pact, cancel pact with
  average funding), the rating ladder and `monthlyRatings`, performance bonus, `warned` and both
  game-over paths; monthly alien-base detection; `eventLogic` outcomes. Project the missing rules
  and options (`chanceToDetectAlienBaseEachMonth`, `monthlyRatings`, the lose cutscenes, video
  `winGame`/`loseGame`, research `cutscene`, the event payload fields,
  `oxceGeoscapeEventsInstantDelivery`, `psiStrengthEval`, `autosave`) with a compiler/cache
  revision. Compile `newMonthCountry` with its Country/GeoscapeGame bindings. Type `warned`. Add
  ledger rows.
- **S2** — Staged ticks (D1), with no behaviour change yet: shadow copy with transient state, stage
  points, the remaining-tick preflight on the shadow, real recomputation with an agreement check.
  Tests use a test-only month provider: staged and direct runs agree, a refusal leaves state and
  random state untouched, minimized interceptions and queued landings survive. Measure the cost.
- **S3** — The month in reference order: `addMonth` (fixing the 105/110 order), scheduling,
  monthly psi, `monthlyFunding`, report evaluation including the `newMonthCountry` call site, pacts
  and the pact events, then monthly alien-base detection. Lift `MonthBoundary` through S2. The
  monthly report is a pending acknowledgement (D2). Transition reloads, and repeated small advances
  against one large advance across a month.
- **S4** — Acknowledgement effects (D2, D3): typed soldier diary, monthly service and
  commendations, the psi reminder, autosave and ironman requests, the game-over path to the lose
  cutscene ending or continued play. Save refusal while an acknowledgement is pending.
- **S5** — Event payloads: `eventLogic` complete (adhoc scripts through `CampaignWorldAdhocScripts`,
  region and city, nationality by location, soldier templates, instant delivery or transfers,
  `invert` removal across stores and crafts not out, craft spawn, research grants with diary entries
  and side effects), the 30-minute stage point, instant despawn events. Removes `EventPayloadReason`
  and the despawn refusal. Rosigma `Begining.sav` runs past tick 580.
- **S6** — Strategic endings (D4): research and `getOneFree` cutscenes in `time1Day`, event
  cutscenes on acknowledgement, monthly failure; transition reloads; Phase 7 victory inputs recorded.
- **S7** — UI: monthly report (rating, income with change, maintenance, bonus, balance, council
  text, country lists, failure screen), funding view, event window (place, transfers, research),
  commendations summary, psi reminder, ending and cutscene-unavailable windows, autosave option,
  save refusal message; a pass over missing strategic controls, options and pause/save flows.
- **S8** — Scenarios: several months of UFO, TFTD and Rosigma play with save/reload at every
  month and transition. Check resource conservation, graph identities, script events, 12-entry
  histories and bounded populations. Reclassify the 19 staged saves (positive, blocked,
  preserve-only) and measure populated month ticks and save/restore. Windows SDL smoke.
- **S9** — Phase 6 closure (D5): refusal inventory and R4–R6 decision; reconcile the
  rule/save/binding/option matrices and the ownership ledger; publish `phase-6-status.md` with
  corpus results, intentional differences, residual Phase 7/8 obligations and the first Phase 7
  acceptance scenario; update the launch-gate status; branch audit (`git diff main...HEAD`).

### Mapping of the original commit purposes

| Purpose in `phase-6-plan.md` | Slices |
|---|---|
| 1. Month/year rollover, funding, scores/pacts, chained events and ending fixtures; ledger audit | S1; ledger audit in S0 and S9 |
| 2. Monthly ledger, country/region evaluation, funding, pacts, histories, debt/failure, earlier hooks | S2, S3, S4 |
| 3. Campaign event effects, progression/objectives, endings; Phase 7 battle-result inputs | S5, S6 |
| 4. Monthly/event/failure UI, strategic controls/options, save/load/pause, unsupported-tactical states | S7 |
| 5. Integrated multi-month and repeated-save scenarios; populated measurements | S8 |
| 6. Phase 6 closure audit, matrices, evidence, launch-gate status | S9 |

## Acceptance

- The implemented strategic loop runs through several month boundaries for UFO, TFTD and the
  staged large mod, with save/reload at each major transition. Funding, maintenance, projects,
  transfers, training, missions and events happen once each, in reference order (no hook runs twice).
- Funding, pacts, the rating, the debt warning and both failure paths match the probes under
  scripted draws. `newMonthCountry` runs at its call site.
- Due and instant events apply their full payload; chained events, once-only history and
  interrupted events behave as the reference.
- Strategic endings are executable. Tactical success, recovery, battle-driven promotions and
  final victory stay unclaimed until Phase 7.
- No named refusal remains without an owner (D5); `phase-6-status.md` is published.
- Validation: Release build, unit and public compatibility tests, private corpus, formatting,
  checksums, code map, and the Windows SDL smoke.

## Progress

### S0 — Plan and baseline (2026-10-17)

This plan, [ADR 0035](decisions/0035-staged-ticks-on-a-shadow-copy.md) and the status corrections
are recorded; baseline validation and the corpus stops branch 6 must lift are in
[status S0](strategic-campaign-cycle-status.md#s0--plan-and-baseline-2026-10-17).

### S1 — Evidence and projections (2026-10-17)

The month rollover and event payload oracles, the monthly, cutscene and event projections, the three options and
`warned` are recorded in [status S1](strategic-campaign-cycle-status.md#s1--evidence-and-projections-2026-10-17).
Time still stops at the month boundary and before due event payloads.
