# Battle session and UI status

Phase 7 branch 3 (`claude/battle-session-ui`), from `main` after PR #79 (branch 2 merged). Plan:
[Phase 7 branch 3](phase-7-plan.md#branch-3--battle-session-and-ui); decisions:
[ADR 0037](decisions/0037-host-authoritative-session.md), [ADR 0038](decisions/0038-battle-aggregate.md),
[ADR 0039](decisions/0039-battle-save-intentional-differences.md) and the plan's branch decisions.

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-31) |
| S1 session layer | Done, audit pending (2026-10-31) |
| S2 host clock, pause policy and screen stack | Not started |
| S3 briefing and the first turn | Not started |
| S4 inventory | Not started |
| S5 battlescape queries and view model | Not started |
| S6 battlescape screens | Not started |
| S7 acceptance and branch audit | Not started |

## S0 — Planning (2026-10-31)

Reference studied: `BriefingState` (constructor and `btnOkClick`), `SavedBattleGame::startFirstTurn`,
`randomizeItemLocations`, `newTurnUpdateScripts`, `InventoryState` (the call to `startFirstTurn` on closing the
pre-battle inventory), the audit's session-layer and screen-stack items (M2–M4, E4) and ADR 0037.

- The [plan](phase-7-plan.md#branch-3--battle-session-and-ui) splits the branch into S1–S7: session layer, host
  clock and screen stack, briefing and first turn, inventory, battlescape view model, screens, acceptance.
- Reference facts that shape S3: the briefing screen pushes the battlescape, `NextTurnState` and the pre-battle
  inventory; `startFirstTurn` runs when that inventory closes (or at once in a preview); it randomizes ground items
  from the selected unit's tile across the storage space, resets unit tiles, reselects the unit standing on the inventory tile,
  calls `prepareNewTurn(false)` on live X-COM units, sets turn 1 and runs the `NewTurnUnit` and `NewTurnItem` script
  callbacks, `reviveUnconsciousUnits` and `convertInfected`. A battle with no live aliens goes to `AliensCrashState`
  instead (branch 7's).
- Inherited from branch 2: `battle.save-before-first-turn` (retired by S3); the owned recovery-list item decision
  (S4); the single no-sound value in sound lists (a later playing branch treats -1 as silence); the fifth loaded
  special item not reported (the restore has no diagnostics sink).
- Open question for S3 (not a blocker): whether `prepareNewTurn(false)` pulls in more of branch 6 than a first-turn subset is decided
  in S3 and recorded there.
- Audit findings fixed in S0: the briefing sets (not reads) the target; `spawnFromPrimedItems`, `NextTurnState`'s
  turn-0 work (reinforcements, bug hunt), `saveEquipmentLayout` and the saved selection fields now have owners in the
  plan; refusals are named; the fifth-special-item note is assigned to S3.

## S1 — Session layer (2026-10-31)

Host side, in `Oxce.Gameplay/Sessions` (no new project: the battle's unit ownership lives in `Oxce.Gameplay` and
`Oxce.Engine` already references it).

- **Players and handles.** `PlayerId` (data; `PlayerId.Local` is the single-player one). `CampaignSession` is built over an
  `IActorCommandTarget` and an `ICampaignQuery`; `Attach(player, client?)` gives a `SessionHandle`, which is that
  player's `ICampaignCommandTarget` and `ICampaignQuery`. A detached handle refuses commands
  (`session.player-not-attached`).
- **Authorization at the boundary.** A command that is an `IUnitCommand` (addresses `UnitId`) is checked against
  `CampaignSession.Controllers` (`IUnitControllers`, to be supplied by the battle) before the simulation runs. Refusals are
  classified `CampaignActionBlocked`s: `session.unit-not-controlled`, `session.unit-unknown`,
  `session.unit-commands-unavailable` (no battle supplies controllers). The simulation is not called and nothing changes.
- **Broadcast.** After a command commits, every attached client's `ISessionClient.OnEvents(SessionEvents)` gets the
  actor, the command and the events, in commit order, the sender included (it can ignore its own). A result that holds only refusals
  (from the session or the simulation) goes to the sender alone; a callback cannot submit commands; a callback that throws is detached and recorded in `Faults`
  without undoing the command or starving the other clients.
- **Per-actor quotes.** `CampaignState.Execute(PlayerId, command)` (also on `CampaignExtensionSession`, which passes the
  actor on, and as the local player for extensions) keeps one open logistics quote per actor. A time advance, another
  actor's quote or any other actor's command no longer clears it. Submit revalidates: it rebuilds the quote from its
  request (the same request, re-priced now; ADR 0037 rule 6 and audit M3) and refuses with the existing "expired"
  text, changing nothing, when the rows differ. Still cleared for everyone: initial entry into the campaign world and
  placing the starting base (campaign phase changes, existing tests); an actor's own submit consumes its quote.
- **Clients go through it.** The SDL campaign command (`CampaignSdlCommand`, run and smoke) builds the geoscape and
  logistics clients on the local player's handle; the geoscape and logistics clients are unchanged (they already use
  `CampaignUiSession.Commands` and `Queries`).

Tests: `CampaignSessionTests` (unauthorized test-only unit command refused with the target untouched, unknown unit, no
controllers, detached and duplicate players, two clients receive an event with its sender, refused commands broadcast
nothing, broken client, no re-entrancy, a real campaign's time advance, a quote surviving another actor's quote and a time
advance, a quote belonging to its actor, a stale quote refused with no change, a consumed quote). The existing
geoscape, logistics and quote tests pass unchanged.

## Next

S2 — Host clock, pause policy and screen stack (see the plan's slice table).

- S1 left the clock where it is: `GeoscapeTimeController.Tick(elapsed, commands)` still decides pause and speed and now
  sends `AdvanceCampaignTime` through the local player's `SessionHandle`. Move the clock, the speed request and the
  covered and popup pause policy (`GeoscapeClient.GeoscapeCovered`, ADR 0034 D3) into a host-side session clock that
  clients inform; `CampaignSession` is the natural owner. No advance while a battle exists (the `battle` preflight).
- Time advances by a clock are session commands: broadcast them through `CampaignSession.Execute` so every client sees them.
- `IUnitControllers` has no implementation yet; S5 or the first real unit commands (S4) supply the battle's
  (controller slot to player) map and mark their commands `IUnitCommand`.
- Gap, owner S2: commands an extension sends (`IExtensionCampaignCommands.Execute`) run as `PlayerId.Local` and reach
  the campaign through the extension session, which sits under `CampaignSession`, so no `ISessionClient` hears them. S2 puts
  the clock on the session; route extension commands through it then.
- Untested (unreachable or needs a removed base): the `ArgumentException` path of `QuoteStillHolds`; the
  `PlaceStartingBase` quote clear (an unplaced base cannot be quoted).
- Same gotchas as before (below).
- Gotchas carried from branch 2 (see `docs/battle-save-status.md` "## Next"): write test files with LF; add C# string
  literals with `\n` through the Edit tool; the test runner is Microsoft Testing Platform
  (`dotnet test --project <proj> --filter-class <full name>`); battle test helpers (`BattleStartTests.Requested`,
  `Started`, `Battle`, `BattleSaveRootTests.Generated`, `BattleSaveUnitTests.Loaded`) are internal for reuse; a
  campaign snapshot with a battle restores only with terrain files; a test that needs a savable battle sets the
  captured snapshot's `Battle.Turn` to 1 until S3 retires the first-turn restriction.
- Named refusals this branch owns: `battle.save-before-first-turn` (S3 retires it); it introduces
  `battle.preview-unsupported`, `battle.inventory-ammo-unsupported`, `battle.inventory-throw-unsupported`,
  `battle.inventory-prime-unsupported`, `battle.inventory-layout-screens-unsupported`, and hands on
  `battle.new-turn-script-unsupported` (branch 8), `battle.turn-one-revive-unsupported` and
  `battle.reinforcements-unsupported` (branch 6), `battle.no-live-aliens` (branch 7). The buttons of later branches
  refuse by their owner's name (S6); the minimap and extended-battlescape links go to branch 8 or Phase 8. Kept by later owners:
  `OxceBattleSave.UnsupportedTagType` (branch 8), the inherited "Crashed UFO battlescape transitions require tactical
  simulation" (branch 7), the crashed UFO's power-source explosion (branch 5).
