# Strategic research and production status

Branch: `codex/strategic-research-production`. Date: 2026-09-13.

Phase 6 branch 3 implements a persisted research-to-manufacture economy chain. Research
eligibility, randomized project cost, allocation, held specimens, daily completion,
ordered free/lookup/zero-cost discoveries, disables/re-enables, score and item rewards
plus reference-encoded custom-counter changes are gameplay-owned. Production owns first-unit prepayment, engineer/workshop allocation,
hourly multi-unit progression, material/craft consumption, item/craft/person outputs,
transfer delays, autosell, refund-on-cancel, random output bookkeeping and completion or
shortage pauses.

The indexed campaign client exposes research with `H` and manufacture with `M`; Enter
starts the selected row and `X` cancels the first active project. Queries show staff,
capacity, progress, costs, eligibility, active queues and completion state. Research
and manufacture unlocks immediately flow into the existing purchase, facility,
equipment and craft eligibility checks.

## Compatibility evidence

Authoritative sources inspected at `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`:

- `src/Savegame/ResearchProject.cpp`, `Production.cpp`, `Base.cpp`, and the
  research availability/completion helpers in `SavedGame.cpp`.
- `src/Basescape/ResearchInfoState.cpp`, `NewResearchListState.cpp`,
  `ManufactureInfoState.cpp`, and `ManufactureStartState.cpp`.
- daily and hourly ordering in `src/Geoscape/GeoscapeState.cpp`.
- `src/Mod/RuleResearch.cpp` and `RuleManufacture.cpp`.

`capture-strategic-research-production-reference.ps1` compiles extracted reference
methods and records daily research completion/progress labels plus production boundary
arithmetic. `StrategicResearchProductionFixtureTests` runs fresh and compiled-cache
content through research, free and protected unlocks, item rewards, manufacture,
save/reload, resource/fund accounting, cancellation, atomic rejection and preservation
of unknown nested save fields. Coverage includes simultaneous daily completions,
insufficient funds, zero staff, the legacy `INT_MAX` infinite/autosell migration and
strict ruleset diagnostics for unresolved or invalid values. Runtime compiler/cache
revision 14 carries the new dense research and manufacture projections.

## Deliberate later boundaries

Strategic event choices are emitted as deterministic `CampaignStrategicEventRequested`
notifications. Branch 4 owns creation and persistence of live event/world entities.
Cutscene/Ufopaedia presentation remains Phase 8. Production-created tactical equipment
is usable strategically, while tactical behavior remains Phase 7. The monthly ledger,
report and month-boundary advancement remain branch 6 and are still guarded.
