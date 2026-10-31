# Battle session and UI status

Phase 7 branch 3 (`claude/battle-session-ui`), from `main` after PR #79 (branch 2 merged). Plan:
[Phase 7 branch 3](phase-7-plan.md#branch-3--battle-session-and-ui); decisions:
[ADR 0037](decisions/0037-host-authoritative-session.md), [ADR 0038](decisions/0038-battle-aggregate.md),
[ADR 0039](decisions/0039-battle-save-intentional-differences.md) and the plan's branch decisions.

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-31) |
| S1 session layer | Not started |
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

## Next

S1 — Session layer (see the plan's slice table). Its authorization test uses a test-only unit command; the first real unit commands are S4's.

- Read `docs/decisions/0037-host-authoritative-session.md` rules 1–10, the audit's Part 3 (M2, M3) and step 4, and the
  existing ports: `ICampaignCommandTarget`, `ICampaignQuery`, `CampaignUiSession` (`Oxce.Gameplay/Campaigns`; `CampaignUiSession` is in `Oxce.Engine/CampaignLogisticsClient.cs`),
  `GeoscapeClient`, `CampaignLogisticsClient`, `CampaignExtensionSession`.
- Scope: player identity on commands and queries; authorization at the session boundary with a classified refusal;
  event broadcast to every attached client; per-actor quotes (the logistics quote becomes keyed by actor and ID). The
  host clock and the screen stack are S2, not S1.
- Keep single-player behavior identical: one local player, every existing geoscape and logistics test unchanged.
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
