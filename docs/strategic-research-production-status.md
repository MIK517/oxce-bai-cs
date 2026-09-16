# Strategic research and production status

Branch: `codex/strategic-research-production`. Date: 2026-09-14.

Phase 6 branch 3 implements a persisted research-to-manufacture economy chain. Research
eligibility, randomized project cost, allocation, held specimens, daily completion,
ordered free/lookup/zero-cost discoveries, disables/re-enables, score and item rewards
plus reference-encoded custom-counter changes are gameplay-owned. Production owns first-unit prepayment, engineer/workshop allocation,
hourly multi-unit progression, material/craft consumption, item/craft/person outputs,
transfer delays, autosell, refund-on-cancel, random output bookkeeping and completion or
shortage pauses. Compatibility-audit corrections defer disabled-project cleanup until all
same-boundary completions and re-enables have run, then cancel projects that remain disabled;
a later re-enable restores availability but does not restore the cancelled project. They also
deduplicate primary research side effects across bases, route research rewards through
one-hour transfers, unload consumed craft, enforce living-space limits for manufactured
personnel, assign fallback engineers, and apply adjusted sell prices to autosold output.
Production configuration validates everything before mutating state, counts a project's
required workshop space when staff is added to it, reserves one hangar per pending craft
(`Base::getUsedHangars`), rejects infinite craft queues and duplicate projects per base.
Like `Base::load`, saves drop projects whose research or manufacture rule no longer exists and
return their assigned staff. Hourly preflight revalidates restored production rules so invalid
random-output or event weights stop time instead of failing mid-tick.

The indexed campaign client exposes research with `H` and manufacture with `M`; Enter
starts the selected row and `X` cancels the first active project. Queries show staff,
capacity, progress, costs, eligibility, active queues and completion state. Research
and manufacture unlocks immediately flow into the existing purchase, facility,
equipment and craft eligibility checks.

## Compatibility evidence

Authoritative sources inspected at `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`:

- `src/Savegame/ResearchProject.cpp`, `Production.cpp`, `Base.cpp`, `Craft.cpp`, and the
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
strict ruleset diagnostics for unresolved or invalid values. Audit coverage also locks
down same-boundary disable/re-enable ordering and later project cancellation, implicit
research items, protected rewards, repeatable zero-cost chains, workshop and hangar
admission, autosell eligibility, airborne craft materials, immediate ammo reuse, cross-base
primary-side-effect deduplication, fallback allocation, personnel living-space rejection,
craft cargo unloading, adjusted autosell prices, and complete fixed-plus-random output
bookkeeping. Runtime compiler/cache revision 14 carries the new dense research and
manufacture projections.

## Deliberate later boundaries

Strategic event choices are emitted as deterministic `CampaignStrategicEventRequested`
notifications. Branch 4 owns creation and persistence of live event/world entities.
Cutscene/Ufopaedia presentation remains Phase 8. Production-created tactical equipment
is usable strategically, while tactical behavior remains Phase 7. The monthly ledger,
report and month-boundary advancement remain branch 6 and are still guarded.
