# Phase 6 status

Phase 6 (the strategic layer, [plan](phase-6-plan.md)) closes with branch 6, the campaign cycle
([plan](phase-6-campaign-cycle-plan.md), [status](strategic-campaign-cycle-status.md)). This page records the closure
evidence, the refusal inventory with its owners, the intentional differences, and what Phase 7 and Phase 8 inherit.
It claims no completion percentage.

## Outcome

The implemented strategic loop runs through month boundaries in reference order: `addMonth`, mission scheduling,
monthly psi training, `monthlyFunding`, the monthly report (ratings, country decisions, pacts, `newMonthCountry`,
debt warning, failure), the monthly alien-base roll, and the report's acknowledgement (service, commendations, psi
reminder, autosave or ironman requests). Events pop in the half-hour tick and run their payloads (score, funds, staff
and soldiers, items given or taken, crafts, research). Research, events and the monthly failure end the campaign
through one ending rule. Ticks that the forecasts cannot prove are staged on a shadow copy
([ADR 0035](decisions/0035-staged-ticks-on-a-shadow-copy.md)), so a tick that cannot run is refused before
anything changes. The SDL geoscape shows every window of the cycle as a text panel.

Tactical success, recovery, battle-driven promotions and final victory stay unclaimed: a landing ends at the
persisted deployment request (branch 5), which Phase 7 consumes.

## Compatibility evidence

- Oracles compiled from the reference: `strategic-monthly-report` (17 scenarios), `strategic-event-payload` (16 and
  the instant-event checks), `strategic-commendations` (16), and the earlier branches' strategic oracles.
- Private corpus (`PrivateStrategicWorldTests`, one-day speed). Of the 19 staged saves, 11 play their months
  (positive), none is blocked, and 8 battles stay preservation-only
  (`ImportedWorldContinuationHasEquivalentFreshAndCachedClassifications`).
  - First stops (`ImportedWorldRunsToItsNamedLongHorizonStop`): every vanilla save on its monthly report; Rosigma on
    its report (`Middle`, `_autogeo_`), its landing (`Early`) and an event window (`Begining`, tick 581).
  - Answering every window (`ImportedWorldRunsPastBranchFiveChoicesToItsNextStop`): every save runs 40 days except
    TFTD `Middle.sav`, which loses the campaign on the council's rating.
  - Three months with a save and strict-equivalence reload at every report
    (`ImportedCampaignsPlayThreeMonthsAcrossReloadsAtEveryReport`): every history grows by one month up to twelve,
    and populations stay bounded. The passive player loses most campaigns on the rating at their second or third
    report; Rosigma `Begining.sav` plays all three.
- Costs: [performance baselines](performance-baselines.md#campaign-cycle-months-branch-6-s8-2026-10-18) (month tick
  7–151 ms Debug, save and reload 4–13 ms) and the staged tick costs (S2). The Windows SDL3 smoke passes.
- Root `dotnet test` at the closure commit: every unit and compatibility test passes, the private corpus included
  (the last run is recorded in the branch 6 status).

## Refusal inventory

Plan D5: every reason string in `src/Oxce.Gameplay` is listed in the
[refusal inventory](phase-6-refusal-inventory.md), 664 in all, each in one class:

| Class | Count | Disposition |
|---|---|---|
| Modal holds and the ended campaign | 8 | Reference behaviour: the reference's windows and game over stop time and input (D2, D4). |
| Internal consistency checks | 47 | Not reachable refusals. They state invariants the code guarantees (a provider preflighted, a stage that agrees with its shadow, one registration each). |
| Player command refusals | 42 | Reference behaviour: the reference's states disable or refuse the same actions. |
| Invalid content or save input | 217 | Re-justified as invalid input: the reference throws, crashes or misbehaves there. The chosen behaviours are recorded in the decision records ([ADR 0036](decisions/0036-campaign-cycle-intentional-differences.md) and earlier). |
| Port work and range bounds | 236 | Re-justified: deterministic limits on forecast work, identities and values the reference leaves unbounded. Branch 6 raised the forecast bounds to fit real globes (S3, S5). No staged save reaches one (corpus above). |
| Tactical and battle boundaries | 2 | Phase 7. |
| Named restrictions | 112 | Same-tick interactions the forecasts do not resolve: the 4c restrictions R3–R6 and their kind. Assigned to Phase 7 (below). |

**R4–R6 (plan D5).** Re-evaluated against staged ticks: the staging mechanism can lift them. A tick that reaches one
would run its levels on a shadow and stop only for a real failure. Doing so means replacing each forecast with a
stage point and an execution-time check, which is the same soundness work ADR 0035 describes. It is not done in
branch 6. They stay precise, atomic refusals, and none of the 19 staged saves reaches one in the runs above.
Retiring them is assigned to the Phase 7 plan, whose planning slice inventories them against its own battle
transitions ([implementation plan](implementation-plan.md#phase-7--playable-battlescape-vertical-slice)).

## Intentional differences

Recorded per decision: [ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md) (interception and
deployment), [ADR 0035](decisions/0035-staged-ticks-on-a-shadow-copy.md) (staged ticks) and
[ADR 0036](decisions/0036-campaign-cycle-intentional-differences.md) (the campaign cycle), with the earlier phases'
records in [decisions](decisions/). The text-panel windows' differences are listed in the branch 6 status (S7 audit).

## Residual obligations

- **Phase 7:**
  - the battle generator consuming the deployment request;
  - tactical saves, mission results and debriefing (mission statistics, diary battle entries, battle research and
    cutscenes, commendations from debriefing, which reuse the same evaluation);
  - final-mission victory;
  - retiring the named restrictions.
- **Phase 8 (presentation):**
  - cutscene playback, Ufopaedia, graphs, the commendation and soldier diary screens;
  - the options screen and the save and load menus;
  - the reference's window art and layout over the text panels.
- **Later completion roadmap:** full original binary-save conversion and release breadth.

## First Phase 7 acceptance scenario

Load Rosigma `early/Early.sav`, run at the one-day speed to its terror-site landing (tick 6290), and answer Yes. The
campaign stops on the persisted deployment request. Phase 7 generates the battle from that request, plays it to an
outcome, and debriefs. The mission statistics and soldier diaries then gain the battle, its research and score reach
the month, and the strategic loop continues to the next monthly report with a save and reload on both sides of the
battle.

## Launch gate

The Phase 6 exit gate holds: a modded campaign can be operated from creation (or import) through a validated,
persisted strategic deployment request, with strategic save and reload at each major transition. The broader playable
mission-launch gate stays open until Phase 7's first battle generator consumes that request; a request alone is not a
playable battle.
