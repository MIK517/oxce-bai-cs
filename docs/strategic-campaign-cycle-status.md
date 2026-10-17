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
