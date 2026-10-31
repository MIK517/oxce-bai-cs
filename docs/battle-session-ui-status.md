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
| S2 host clock, pause policy and screen stack | Done, audit pending (2026-10-31) |
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

## S2 — Host clock, pause policy and screen stack (2026-10-31)

- **Host clock.** `GeoscapeTimeController` moved to `Oxce.Gameplay/Sessions/SessionClock` with the same step arithmetic
  (80 ms steps, speed table, frame-budget chunks, notification interrupt, halt on a refused tick). `CampaignSession`
  owns one (`CampaignSession.Clock`, `SessionHandle.Clock`); its time advances run through `ExecuteAsHost` as
  `PlayerId.Host`, so every attached `ISessionClient` hears them. The battle stop is the campaign's `battle`
  preflight: the refused advance halts the clock until a speed is selected. The clock test uses a refusing target with a made-up reason; the real `battle.in-progress` path is tested only through direct campaign calls (`BattleStartTests`), not through the clock.
- **Pause policy.** Clients report a cover (`SessionClock.SetCovered(player, covered)`; `GeoscapeClient` reports
  `!TimeCanRun` each tick, which is the old popup, screen, destination, placing, naming and world-not-entered rule).
  The clock does not run, and banks no time, while any player reports one. Speed requests and the popup interrupt
  stay client calls on the clock. A bare campaign (`CampaignUiSession` without a `Clock`) gets a private clock for its
  one client, so the existing geoscape tests ran unchanged except for the type name.
- **Extension commands.** `CampaignExtensionSession` implements `IHostRoutedCommands`; `CampaignSession`'s constructor
  gives it a route, so `IExtensionCampaignCommands.Execute` runs through the session as `PlayerId.Host` and is
  broadcast (the S1 gap). Without a session above it, it behaves as before.
- **Manual advances.** `SessionClock.Advance(ticks)` applies the same policy (refused with `session.clock-covered` or `session.clock-halted`; a refusal halts). The logistics client uses it on a hosted session; a bare campaign keeps the direct command.
- **Screen navigator.** `Oxce.Engine/ScreenNavigator` (named so, not "stack": CA1711): push, pop, replace, input and
  ticks to the top screen only, pop on `ExitRequested` with a close callback, `Covers(screen)`, an opaque flag and
  `Visible`. Screens are `IIndexedLoopClient`s. The geoscape's base screens (`OpenBases`) now live on it and
  `GeoscapeCovered` / `BasesOpen` read it.

Tests: `SessionClockTests` (the old controller tests, cover pause and release, host-actor broadcast to two clients,
battle refusal halt), `ScreenNavigatorTests`, `GeoscapeClientTests` (a hosted client shares the host clock; another
player's cover pauses it), `CampaignSessionTests` (extension commands broadcast; the host controls no unit).

## Next

S3 — Briefing and the first turn (see the plan's slice table).

- Gaps with owner S6 (moved there in the plan's slice table): the plan's shared widgets and the `ScreenNavigator.Covers` query
  the session should read are not done (the cover comes from the client's `TimeCanRun`); `ScreenNavigator` has no hook for the screen
  a pop reveals (the reference's `init`/`resetAll`) and keeps routing input after a stack change.
- Extension time advances (`PlayerId.Host`) deliberately ignore covers and the clock's halt: they are explicit host commands (pinned by `ExtensionCommandsGoThroughTheSessionAndEveryClientHearsThem`). The simulation's preflights still apply.
- A detached player's cover is cleared on detach. `SessionClock` is not thread-safe (the host loop is one thread). A second session over one extension session takes over the route.
- Not migrated in S2, owner S6: the geoscape's popup list (`_popups` with groups, the
  naming, selecting and placing modes) is still client code inside `GeoscapeClient`; only the base screens use
  `ScreenNavigator`. The logistics client was not changed. The navigator composites nothing: `Visible` lists the
  screens to draw and the loop draws the top frame, so translucent compositing is for the first screen that needs it.
- The host loop is still the single-player client: `GeoscapeClient.Tick` calls `SessionClock.Tick`. A battlescape
  client must not tick the clock; a future host loop will.
- `IUnitControllers` has no implementation yet; S5 or the first real unit commands (S4) supply the battle's
  (controller slot to player) map and mark their commands `IUnitCommand`.
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
