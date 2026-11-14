# Project audit, 2026-11-14

Baseline: `claude/battle-combat` at `0df0b099` (Phase 7 branch 5, S5 audit follow-up). Scope: how the parts of the
system interact in code, against what [ADR 0037](decisions/0037-host-authoritative-session.md),
[ADR 0038](decisions/0038-battle-aggregate.md) and the [Phase 7 plan](phase-7-plan.md) say they do and will do once the
planned branches are complete. Focus: `Oxce.Gameplay` (`Battles`, `Sessions`, the campaign command path), `Oxce.Engine`
and the `Oxce.App` composition. `Oxce.Formats`, `Oxce.Scripting`, `Oxce.Resources` and `Oxce.Platform.Sdl` internals
were not reviewed. Nothing was changed except this document; the build and the tests were not run.

It follows the [2026-10-18 audit](project-audit-2026-10-18.md), whose findings on the strategic layer (D1–D5, E1–E6)
are not repeated here.

## Outcome

- **Project boundaries hold.** The project graph matches [architecture](architecture.md) and its test.
- **The battle is not yet the aggregate ADR 0038 describes.** It is a campaign capability over one partial class
  (A1–A4). Branches 6–8 add turns, the AI, psi, item uses and debriefing to that shape.
- **The host-client boundary covers unit-command authorization only.** Queries, events and the clock go around the
  session (B1–B4). The AI turn of branch 6 produces the first events a player must not see, so this is where it
  starts to cause defects.
- **Two pieces of work are deferred in a way that forces a retrofit:** combat events (C1) and tactical script
  hooks (C2).

## Part A — The battle aggregate

### A1. The battle has no registry of its own

ADR 0038 says `BattleState` "has its own capability registry; it does not become another partial class of
`CampaignState`". In code:

- `CampaignBattles` (`Oxce.Gameplay/Battles/CampaignBattles.cs`) is a campaign capability that registers 16 battle
  commands by hand in `CampaignCapabilityRegistry`. Each handler repeats the same wrapper: check that a battle exists,
  call a `BattleState` method, wrap its result in events.
- `BattleState` is one `partial class` across 23 files. This is the shape the October audit recorded for
  `CampaignState` and `CampaignWorld` (E1), repeated.
- Battle commands are `ICampaignCommand`, battle events `ICampaignEvent` and battle refusals `CampaignReason`; 27 files
  of `Battles` import `Campaigns`.

### A2. Battle context is threaded by hand

- `battle.Random = campaign.Random` is assigned at seven entry points before a rule runs. An entry point that forgets it
  draws from a stale or missing stream.
- `RuntimeRuleCatalog rules` is a parameter of almost every battle method.
- `Revision++` appears at 38 sites.
- `CampaignLink` (difficulty, mission count, soldier names, the memorial) is null on a battle built by hand and on a
  trial copy (A3), so a trial run and the live run can read different values.
- `BattleOptions` is a static class of defaults; the campaign's options do not reach it.

### A3. The per-command trial copy contradicts the failure model

ADR 0038: "Battles have no preflight, no forecast and no per-action copy." ADR 0040 then allows a trial copy for walks,
up and down moves and the first turn, and `CampaignBattles.TrialRefusal` implements it:

- Every `WalkUnit` and `MoveUnitUpDown` restores a full copy of the battle through the save snapshot
  (`BattleState.Restore(battle.Capture(), …)`), which rebuilds the map and reruns lighting and field of view. The
  [performance baselines](performance-baselines.md) record 11 to 255 ms for that restore in Debug; the walk command's
  total and a Release build were not measured.
- State a save does not keep is copied into the trial by hand (the units spotted this turn, the spotting flags). Each
  new transient field is a way for the copy to stop somewhere the live run does not.
- Without attached terrain files there is no copy, and a late refusal leaves the walk half applied (the code comment
  says so).
- The copy exists only to find refusals of unported features after the first mutation. The plan's only mention of its
  end is "the trial copies reviewed" in branch 5 S9.

### A4. The fault path is not implemented

ADR 0038: "An exception during resolution is a defect. It faults the battle: the battle refuses every later command."

- `CampaignState.ExecuteCore` has no catch. A handler that throws leaves partly mutated state, and the campaign accepts
  the next command. `_faulted` is set only by a staged-tick mismatch.
- An unknown command type throws `ArgumentException` instead of returning a refusal (ADR 0037 rule 2).
- `GeoscapeClient.Tick` catches three exception types around the clock and halts it with its own message, so a UI
  client decides what a host failure means.

### A5. Battle rules do not follow the live-state convention

ADR 0038: "Battle live state is mutable classes and struct arrays holding runtime rule handles."

- Units and items hold string IDs (`ArmorId`, `ItemType`); battle code does 67 string lookups into
  `rules.Battle.Equipment.Items`, `.Armors` and `.Units`.
- Items, armors and units each have two projections: the handle family in `RuntimeRuleCatalog` and a string-keyed
  dictionary in `RuntimeBattleEquipment`.
- ADR 0038 also says the battle's rule families are "linked through per-family registrations". `RuntimeRuleLinker.Link`
  is still one method (the file is 83 KB), and `RuntimeBattleEquipment.Link` takes `Phase3ContentCatalog`.

## Part B — The session boundary

### B1. Queries go around the session

- `CampaignUiSession.ForCampaign` takes the `CampaignState` and reads every typed query object from it. `SessionHandle`
  implements only `ICampaignQuery` (overview and stores).
- The client chooses its own `BattleViewer`: `GeoscapeClient.Battle.cs` passes `BattleViewer.LocalPlayer`. Nothing binds
  a viewer to the player that asks, so any client can ask as any faction.
- `Oxce.Engine` holds `CampaignState` (in that factory) and `RuntimeRuleCatalog` (the geoscape screens).

### B2. Events are broadcast unfiltered, and nothing listens

- No type in `src/` implements `ISessionClient`. Clients use the result of their own command and query revisions, so
  the broadcast path runs only in tests.
- `SessionEvents` carries the sender's command and every event to every attached client. Battle events have no
  audience. Once hostile units act (branch 6), that sends a viewer what its faction has not seen.

### B3. The clock is host state driven by the client

- Clients call `SessionClock.Select`, `SetCovered`, `Interrupt` and `Advance` directly. They are method calls on a
  shared object, not commands with a sender, and only `Detach` takes the session's lock around them.
- The client's frame loop calls `SessionClock.Tick`. Two attached clients would tick it twice.
- The dogfight timer (`GeoscapeClient.Interceptions.cs`, `_dogfightElapsed`) and the "time can run" decision are still
  in `GeoscapeClient` (ADR 0037 rule 5).

### B4. Unit ownership assumes the player faction

`BattleUnitControllers.ControllerOf` looks the unit up in the player faction's view (a linear search per command). A
human on the hostile side can never own a unit, which ADR 0038 names as a goal.

### B5. No layering is enforced inside `Oxce.Gameplay`

`Sessions` and `Battles` reference each other (`IUnitCommand` lives in `Sessions`; `BattleUnitControllers` in
`Sessions` reads `Battles`). `ProjectDependencyTests` checks the project graph only.

## Part C — Deferred work that forces a retrofit

### C1. Combat resolution has no event stream

Only walking and gravity take a `List<ICampaignEvent>`. Damage, casualties and fire (S3–S5) emit nothing; explosions
append to a side list (`BattleState.ExplosionLog`) that a command is meant to turn into events. S10 replays "shots,
impacts, explosions and deaths", so events have to be added to finished code, in the reference's order.

### C2. Tactical scripts are deferred whole to branch 8

- `VisibilityScriptRefusal` and `CombatScriptRefusal` refuse every command of a battle whose content binds a
  visibility, combat, new-turn, recovery or stat-bonus script. The rules are written without hook call sites.
- The three Rosigma acceptance battles are refused at the briefing, so each branch until 8 is accepted on content
  without those scripts.
- Branch 8 must then insert hooks that change values in the middle of a calculation into finished sight, damage and
  reaction code, and check ADR 0038's "resolution cannot fail on valid input" against script effects for the first
  time.

## Part D — Recommended order

1. **Before S6 (the attack command): a battle runtime object.** It owns the state, rules, random source, campaign link,
   options, revision and an event sink, set once when a battle is created or restored, and it has its own command
   registry. `CampaignBattles` keeps starting, restoring, debriefing and one forward. Answers A1, A2 and C1.
2. **In the same change: the fault path.** Catch at the command boundary, fault the battle, and refuse an unknown
   command instead of throwing (A4).
3. **While porting S6–S9: script hook call sites.** A hooks interface with no-op defaults, called where the reference
   calls its scripts, and one hook family (visibility or `damageUnit`) carried end to end to prove the unit and item
   bindings (C2).
4. **S9 exit criterion: delete the trial copy.** Until then run it only when a cheap check finds an unported trigger
   (as `HasPossibleReactor` does for reaction fire), and give the copy the campaign link (A3).
5. **Before branch 6: the session.** A per-player query facade that fixes the viewer; events filtered per viewer by the
   battle; clock requests as commands; the pump and the dogfight timer on the host (B1–B4).
6. **When touched:** rule references resolved on units and items at creation and restore, one projection per family
   (A5); commands, events, views and reasons in a contracts namespace with a namespace-level architecture test (B5).
   The extension API and a wire protocol need that same contract layer (October audit, M2).

The 136 strategic named restrictions and their forecasts are as the October audit left them. The Phase 7 plan notes
that shadow promotion does not depend on the battle, so it can run before branch 8.

## Progress

2026-11-14: the Phase 7 plan assigns every finding to a slice or branch in its
[audit follow-up](phase-7-plan.md#audit-follow-up-2026-11-14): steps 1–3 to branch 5 S5b (and the slices after it),
step 4 to S9 and S11, steps 5 and 6 to the start of branch 6. Nothing is implemented yet.
