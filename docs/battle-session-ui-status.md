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
| S3 briefing and the first turn | Done, audit pending (2026-10-31) |
| S4 inventory | Done, audit pending (2026-10-31) |
| S5 battlescape queries and view model | Done, audit pending (2026-10-31) |
| S6 battlescape screens | Done, audit pending (2026-10-31) |
| S6b geoscape and logistics migration onto `ScreenNavigator` | Not started (S2's undelivered scope, moved here from S6) |
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

## S3 — Briefing and the first turn (2026-10-31)

Reference: `BriefingState` (constructor, `btnOkClick`), `NextTurnState` (turn-0 work), `BattlescapeGame::spawnFromPrimedItems`,
`spawnNewUnit`, `spawnNewItem`, `tallyUnits`, `SavedBattleGame::startFirstTurn`, `randomizeItemLocations`, `resetUnitTiles`,
`newTurnUpdateScripts`, `BattleUnit::prepareNewTurn(false)`, `prepareTimeUnits`, `prepareEnergy`, `TileEngine::isPositionValidForUnit`.

- **Phases and commands.** A battle is in a `BattlePhase`: `Briefing` (generated; also a restored battle below turn 1),
  `Inventory` (after the briefing) and `InProgress` (turn 1 on; also every restored battle past its first turn, which opens
  with no briefing). `IBattleCommand`s are not held back by the battle (`CampaignBattles`' command gate): `BeginBriefing`,
  `ConfirmBriefing` and `StartFirstTurn`; `StartBattle(Preview: true)` is refused (`battle.preview-unsupported`).
  `IBattleBriefingQuery.QueryBriefing` answers `BriefingState`'s page.
- **Briefing.** `BeginBriefing(TargetName, CraftOrBaseName)` is the constructor: the client composes the destination's name and
  the `STR_CRAFT_` or `STR_BASE_UC_` line with its language (ADR 0039); a mod's `operationNamesFirst` and `operationNamesLast` replace the target with a
  name from the battle's seedless stream; a base defense calls `BriefBaseDefense` (the base is no retaliation target, a multi-UFO
  mission is in progress). The page's deployment follows the constructor (the mission's, a base defense's custom one with a
  description, else the landing UFO's); title and description default to the mission type and `_BRIEFING`. New rule values:
  `RuntimeBriefing`, the deployment's `Reinforcements` waves, the bug hunt settings, the operation names, a unit's
  `energyRecovery` and an item's `spawnUnit`, `spawnItem`, their chances and `spawnUnitFaction`.
- **Confirming.** `ConfirmBriefing` is `btnOkClick` with `NextTurnState`'s turn-0 work. Checked before anything changes: no live
  aliens (`battle.no-live-aliens`, refused instead of the crash screen of branch 7; the reference tallies after the spawn, the
  port before), a reinforcement wave that could deploy (`battle.reinforcements-unsupported`: difficulty range and trigger pass;
  odds and runs are not drawn), a primed spawn item within two tiles of a tile without a floor
  (`battle.spawn-gravity-unsupported`: `applyGravity` is branch 4's), a spawn without alien item levels
  (`battle.generation-failed`), and what `startFirstTurn` would refuse. Then `spawnFromPrimedItems`: for each grenade primed
  with fuse 0 (enabled, on a tile, not X-COM's, spawning a unit or an item) `spawnNewUnit` (chance, owner's or the rule's
  faction, `createTempUnit`, `isPositionValidForUnit` with its surrounding tiles, direction, a tank's weapon and clip, the item
  level, `initUnit`), `spawnNewItem` and `removeItem` (whatever the chances). Then `checkBugHuntMode`.
- **First turn.** `StartFirstTurn` (the pre-battle inventory's close; S4 calls it) refuses with no live soldiers
  (`battle.no-live-soldiers`, the battle's end is branch 7's), then ports `startFirstTurn`: the selected unit's tile leaves the
  storage space and its ground items go to random storage tiles (seeded; a base defense), `resetUnitTiles`, the unit on that
  tile is selected, `prepareNewTurn(false)` for every live X-COM unit (flags reset, stun counter, a mind-controlled unit returns
  to its side and recovers nothing, else time units and energy recover with the default recovery, cut by encumbrance and
  fatal wounds, and kneeling ends), turn 1. Refused beforehand and named: a bound `newTurnUnit` or `newTurnItem` script
  (`battle.new-turn-script-unsupported`, only for rules the battle uses, and a global event), a scripted time or energy
  recovery of a live X-COM unit's armor or soldier bonuses (`battle.turn-recovery-script-unsupported`), an unconscious
  small unit over the out threshold or an infected unit (`battle.turn-one-revive-unsupported`).
- **Open question from S0, decided.** `prepareNewTurn(false)` needs only the time unit and energy recovery of its first stage
  (no fire, morale or health), so the first-turn subset is complete; the scripted forms are refused.
- **Saving.** `battle.save-before-first-turn` is retired (`CampaignBattles.SaveGate` is gone): a battle saves at any time, and a
  save from before the first turn reloads into the briefing (ADR 0039). The fifth special built-in item of a unit is reported in
  `BattleState.RestoreNotes` and `BattleOverview.RestoreNotes`.

Tests: `BattleFirstTurnTests` (briefing names, operation names, briefing data, phase order, the preview, primed spawn
items and their chances, which grenades spawn, the refusals before any change, reinforcement waves, bug hunt, the first turn's
preparation, encumbrance and leg wounds, out and controlled units, reselection, seeded item randomization, no soldiers, script
refusals, revive refusals, a reload past and before the first turn, the rule values), `StrategicBaseAttackTests` (`BriefBaseDefense` only; the `BeginBriefing` path to it has no test), `BattleSaveItemTests` (the restore note); the old save-gate tests now save from the start and play
the briefing and the first turn to reach turn 1.

Gaps with an owner:

- `tallyUnits` ignores surrender mode and captured units (a surrendering alien still counts) and the exit areas (branch 7).
- `checkForCasualties` of the battlescape's constructor is refused (`battle.casualties-unsupported`, branch 6) when a unit is past the out threshold but not out.
- A save from before the first turn: the briefing's craft or base is derived from the campaign's `inBattlescape` flags on restore (not saved).
- `applyGravity`, `calculateFOV` and the glow of a spawned item are branch 4's; `initItem`/`initUnit` script hooks are branch 8's.
- The briefing's `Seedless` stream is the battle's; the hidden-movement background pick of `NextTurnState` is presentation.
- `BriefingData` merges key by key across mods as in the reference (`MergeBriefing`); no gap.
- The autosave on turn 1 and the camera centering of `NextTurnState::close` are S6's (the screens) and the host's.
- `saveEquipmentLayout` on closing the inventory is S4's.

## S4 — Inventory (2026-10-31)

Reference: `Inventory::mouseClick` (pick up, drop, Ctrl click), `fitItem`, `overlapItems`, `canBeStacked`, `RuleInventory::fitItemInSlot`,
`RuleItem::canBePlacedIntoInventorySection`, `BattleItem::getMoveToCost`, `occupiesSlot`, `TileEngine::itemMoveInventory`,
`BattleUnit::getItem`, `InventoryState::btnOkClick` and `saveEquipmentLayout`, `EquipmentLayoutItem`.

- **Commands and query.** `MoveInventoryItem(UnitId, ItemId, Slot, SlotX, SlotY)` and `AutoPlaceInventoryItem(UnitId, ItemId, Smart)` are
  `IBattleCommand`s and `IUnitCommand`s (so a hosted session authorizes them); `UseInventoryFeature` refuses what is deferred. A move
  raises `InventoryItemMoved` (with the time units spent). `IBattleInventoryQuery.QueryInventory(viewer, unitId)` answers the screen
  (sections with their cells, carried items with size, fuse and loaded ammo, the items on the unit's tile, the unit's time units and
  whether moves are charged); only the unit's own faction gets an answer. `CheckInventoryMove` returns the refusal code a move would
  get, from the same planning code, so a screen can grey out a drop without changing anything.
- **When it opens.** After the briefing (`Inventory` phase: free moves) and in `InProgress` on X-COM's side (moves charge the section
  cost); the unit must be a live player-faction unit on the map whose armor has an inventory (`battle.inventory-unavailable`,
  `battle.inventory-unit-unavailable`). The enemy-side refusal has no test (the side cannot be set before branch 6).
- **A move** (`PlanMove`, then `ApplyMove`): the item must be carried by the unit in a section or lie on its tile
  (`inventory-item-unknown`) and not be fixed (`inventory-item-fixed`); the section must exist (`inventory-slot-unknown`) and take the
  item (`inventory-section-unsupported`, `supportedInventorySections`, the ground always); a grid or hand position must not be taken
  by another item (a hand holds one item; `occupiesSlot`) and every cell must be a slot (`fitItemInSlot`); an occupied position
  holding a weapon that takes ammo is the reference's "put item in weapon" (`inventory-ammo-unsupported`), any other occupied or
  non-fitting position is `inventory-no-room`. In the battle the cost is `getMoveToCost` (none within a section, a drop from a hand
  as the table says, else the item's percentage, at least 1) and must not exceed the unit's time units
  (`inventory-not-enough-time-units`); a missing table entry is `inventory-cost-unknown` (the reference reads past its table; read
  only where charged, as the reference's `!_tu ||` does). `itemMoveInventory`: to the ground the item leaves its owner (the old owner
  becomes the previous owner) and lies on the unit's tile, an unconscious unit it is lands on the tile; from the ground it joins the unit
  (`droppedOnAlienTurn` cleared) and an unconscious unit it is leaves the map. Dropping an enabled grenade is refused
  (`inventory-danger-zone-unsupported`, branch 6: the reference sets a danger zone for the AI; the port refuses every enabled grenade,
  whatever its damage type, since it does not read the resist type).
- **Ctrl click** (`PlanAutoPlace`): a carried item drops to the ground; a ground item goes to the first section that takes it.
  Smart (`oxceSmartCtrlEquip`, the client's option, passed as `Smart`): of all sections (alphabetical, as the reference's map) that
  take it, the one with the lowest cost to a hand, +10 when the other hand holds a two-handed item, skipping one that the other
  hand's both-hands item blocks, ties to the later. Otherwise the item's `defaultInventorySlot`, the first category with an
  inventory order, the vanilla section for its battle type (firearm right hand; mind probe, psi-amp, melee, corpse left hand; tall
  items back pack; else belt), then all sections alphabetically. A smart click with no section "moves" the item to the ground it lies
  on, at no cost (the reference shows no warning). The refusal is the last warning the reference would show.
  `extendedInventorySlotSorting` (the last fallback then follows the list order) and `extendedItemReloadCost` (used with ammo loading, later) are read
  by the mod loader into the runtime campaign rules. A smart click on a mod whose hand costs are missing is `inventory-cost-unknown`
  (the reference reads past its table).
- **Ground positions.** The ground holds any number of items and `arrangeGround` (the screen's layout of ground items, stack levels,
  scrolling) is the client's: a ground drop stores position (0, 0) and ignores the one asked for. `canBeStacked` is therefore a
  display rule, not ported. Ground positions are not saved (branch 2).
- **Order.** `BattleItem.ArrivalOrder` keeps `Tile::getInventory` and `BattleUnit::getInventory` order (0 until an item moves, then the
  battle's counter); `ItemsOnTile` and `OrderedInventoryOf` read it. `RandomizeItemLocations` now walks the tile in that order and
  appends the relocated items (S3's note).
- **Closing.** `StartFirstTurn` first runs `saveEquipmentLayout` (unless `oxceAlternateCraftEquipmentManagement`, as `btnOkClick`
  with no base): each soldier's unit gives the campaign soldier an `EquipmentLayout` of its inventory in arrival order; fixed
  items only for the ammo in their first slot, other items only when the mod lets them on a craft (`ignoreInCraftEquip`, which supplies
  and corpses have by default); the layout stores the loaded ammo types per slot and the fuse timer.
  `BattleFirstTurnStarted.LayoutsSaved` counts the soldiers written; a refused start saves nothing.
- **Recovery-list decision (ADR 0039).** A restored battle whose recovery list holds an item owned by a unit in an inventory section
  is refused by `Validate`; a special built-in item (owned, in no section) stays as it was loaded.
- **Controllers.** `BattleOverview` units now carry `ControllerSlot`; `BattleUnitControllers` (in `Sessions`) is the
  `IUnitControllers` for a session: a human slot maps to a player (slot n to `PlayerId(n)` unless the host maps it otherwise), AI and
  unknown units to nobody. Nobody installs it yet: the host loop that creates the session does (S6).

Deferred by name (always refused by `UseInventoryFeature`; a drop onto a weapon that takes ammo is `inventory-ammo-unsupported`):
`inventory-ammo-unsupported` (load, unload, quick swap), `inventory-throw-unsupported` (the reference inventory has no throw; the
name is the plan's, kept for a throw from the inventory screen), `inventory-prime-unsupported` (prime, unprime),
`inventory-layout-screens-unsupported` (save and load named layouts).

Tests: `BattleInventoryTests` (free pre-battle moves; battle costs, the percentage and the free stay-free rule; each refusal and the
unchanged state; the closed inventory and the query check; time units and the unknown cost; the section list; ground order, pick up
and drop; unconscious unit items; the enabled grenade; fixed items; Ctrl click vanilla, default section and smart; the screen query;
each feature refusal; the saved layout, with the alternate management option and a refused start; the controllers map);
`BattleSaveItemTests` (the owned recovery item refusal), `BattleFirstTurnTests` (`LayoutsSaved`).

Gaps with an owner:

- A drop to the ground runs `updateEnemyKnowledge(tile, clue)` on the unit on the tile (every faction's last spotted tile and the turns
  since seen); the danger zone of a dropped enabled grenade is branch 6's (refused until then).
- Closing the inventory in the reference also applies gravity on the selected unit's tile, recalculates item lighting and the field of
  view (`InventoryState`'s destructor): branch 4's (gravity, lighting, FOV); the port's close does none of it.
- `Tile::addItem` puts an item that carries a unit at the front by swapping with the first item; `AddToTile` does the same (and
  renumbers the tile's arrival order). A soldier without `Personal` state gets no saved layout (the convention of the armor change).
- Deferred by name, also through `UseInventoryFeature`: `inventory-clear-unsupported` (clear inventory), `-autoequip-unsupported`,
  `-template-unsupported` (global and personal templates, which write the soldier's personal layout) and `-rename-unsupported`
  (soldier rename from the screen); the number-key global layouts are `-layout-screens-unsupported`.
- The battlescape button's conditions (`BattlescapeState::btnInventoryClick`: a selected unit, not busy) are S5's selection and S6's
  screens; the command only needs the side to be X-COM's.
- Armor changes from the inventory screen (`btnArmorClick`, the base equipment) and the base-equipment inventory (`_base`) are Phase 8's
  base screens.
- The pre-battle inventory's unit order (previous and next soldier) is the client's over `QueryBattle`.

## S5 — Battlescape queries and view model (2026-10-31)

Reference: `SavedBattleGame::selectPlayerUnit`, `selectNextPlayerUnitByDistance`, `selectUnit`, `BattleUnit::isSelectable`,
`BattlescapeState` (`btnNextSoldierClick`, `btnPrevSoldierClick`, `btnNextStopLClick`, `-MClick`, `-RClick`, `btnMapUpClick`,
`btnMapDownClick`, `btnCenterClick`, `btnInventoryClick`, `updateSoldierInfo`, `allowButtons`), `BattlescapeGame::primaryAction`'s unit
selection, `Camera` (`up`, `down`, `setViewLevel`, `centerOnPosition`, `toggleShowAllLayers`), `Map::drawTerrain` (what a tile shows), `InventoryState`'s
previous and next soldier.

- **Commands** (`IBattleCommand` and `IUnitCommand`, on the unit the client has selected): `SelectUnit` (a click: a live unit of the side on turn
  that stands on the map), `SelectNextUnit(Backwards, InventoryOnly)` (the soldier buttons; `InventoryOnly` is the inventory screen's walk: reselect
  flag ignored, an armor with an inventory required), `SkipUnit(ByDistance)` and `ReselectSkippedUnit`. Open after the briefing (before the first turn only the inventory walk, as `InventoryState` has it; confirming the briefing runs `InventoryState::init`'s move off a unit without an inventory, and when only vehicles are left the selection stays and the client sends `StartFirstTurn`) and on X-COM's turn (`allowButtons`; nothing is ever busy, the actions resolve fully). Refusals:
  `battle.selection-unavailable`, `battle.selection-unit-unknown`, `battle.selection-nothing-to-undo`. Events `UnitSelected` and `UnitSkipped` carry `Saved`.
  The loop of `selectPlayerUnit` is ported whole, including its wrap-around and "back at the start" cases; by distance ties go to the list order.
- **Saved fields.** Only the side-owning slot writes `selectedUnit` and `undoUnit` (ADR 0039, intentional difference); every selection is also the client's own.
- **Queries** (`IBattleViewQuery`, all take the viewer): `QuerySelection` (selected and undo unit when they are the viewer's side's, the selectable units in list
  order, the writing slot), `QueryLevel(viewer, level)` (null outside the map; the discovered parts of each tile with their data set and object index, smoke, fire and
  ground items only where the floor is discovered, the unit standing there when the viewer sees it; a tile with nothing is not listed; X-COM's viewer uses the discovered flags,
  another side the tiles it explored, `lastExplored > 0`), `QueryUnitInfo` (`updateSoldierInfo`: bars with maxima, hands, spotted hostile units, wounded and burning units of the side; only the viewer's own live units) and
  `QueryLegalActions` (the actions the rules accept now: next, previous, skip, skip by distance, take back, open inventory and the inventory walk, with the other units the viewer may click; before the first turn only the inventory walk).
  No action of a later branch is listed.
- **View model** (`Oxce.Engine/Battlescape`): `BattleCamera` (view level `up`/`down`/`setViewLevel`, `centerOnPosition` cut to one tile outside the map, show all layers) and
  `BattleViewModel` (reads the queries when the battle revision or the view level changes, keeps this client's selection, sends the commands, centers the camera on a newly
  selected unit unless asked not to, level buttons only on X-COM's turn). It decides nothing.

Tests: `BattleViewTests` (selection refused before the briefing and on the enemy's turn; click selection and its refusals; the unit-list walk with wrap-around, out and flagged units, the
inventory walk; skip, undo and the lone-unit case; by distance and ties; the writing slot and session authorization; a viewer sees only discovered parts, spotted units;
another side's explored tiles and items; ground items on discovered floors; the init skip; the fallback to the saved selection and a battle that is gone; unit info; legal actions; camera bounds; the view model end to end).

Gaps with an owner:

- The reference's `visibleUnits` of a unit (field of view) and the tiles the selected unit sees now (`calculateFOV`, `isTileVisible`, the fog of war shade) are branch 4's; the view lists
  spotted hostile units and discovered tiles only.
- `clearUnitSelection` when a unit dies and `_lastSelectedUnit`, `handlePanickingUnit`'s selection and the reselection at the end of a turn (`getNextUnitToSelect`) are branch 6's.
- With no selection a client starts from `SelectUnit` on the first listed unit; a command on a selected unit is what the session can authorize.
- `updateSoldierInfo`'s list of stunned allies with negative health regeneration, the `VISIBLE_MAX` cap and the burning option (`oxceShowBurningAsWounded`) are the screen's over `QueryUnitInfo`. Fire is reported on every discovered floor (the reference draws it with smoke).
- The camera's pixel offset, scrolling, the cursor and `Map` drawing are the screens' (S6); the view model keeps only the map-side state.

## S6 — Battlescape screens (2026-10-31)

Reference: `BriefingState`, `InventoryState`, `BattlescapeState` (soldier, level, center and inventory buttons, `updateSoldierInfo`), `Game::_states`
(the stack, `State::init` after a pop).

- **`ScreenNavigator`**: a pop (and an exit request) now tells the screen it reveals (`IRevealedScreen.Revealed`, the reference's `init`/`resetAll`); a `Replace` does not.
- **`BattleUiContext`** (`Oxce.Engine/Battlescape`): command target, the queries for one viewer, font, language, the stack and this client's `BattleViewModel`;
  `Open()` pushes the screen of the battle's phase (briefing, pre-battle inventory, or the battlescape directly for a loaded battle). The host loop still sets
  `Controllers = new BattleUnitControllers(battleQuery)` where it creates the session.
- **Screens** (all `IIndexedLoopClient`, 640x400, keyboard and pointer, no decisions): `BattleBriefingScreen` (sends `BeginBriefing`, Enter is `ConfirmBriefing`, then replaces itself with
  the inventory), `BattleInventoryScreen` (pre-battle or in battle: item and section cursors, Enter drops at the first cell `CheckInventoryMove` accepts, `G` is the Ctrl click, Tab walks soldiers
  through `NextInventoryUnit`, feature keys send `UseInventoryFeature`; closing before the first turn sends `StartFirstTurn` and replaces itself with the battlescape),
  `BattlescapeClient` (flat plan of the level the viewer may see with units, items, smoke, fire; unit bars and hands; Tab/next, `K` skip, `Shift-K` by distance, `R` reselect, `C` center, PgUp/PgDn or `[`/`]` level,
  arrows scroll the camera center, `I` inventory over the map, click selects a unit). A refusal of the rules shows its code on the screen. Screens re-read the queries on `Tick` (`Poll`), so another client's command shows.
- **Later buttons** refuse by owner (`BattlescapeClient.LaterButtons`, shown as `code (owner)`, no command sent): kneel and walking (branch 4), fire and reserve TU (5), end turn (6), abort (7), minimap (8 or Phase 8).
  They are client messages, not rule refusals; the rules refuse the same actions when the commands arrive in their branches.
- **Missing sprite**: a terrain object without a sprite (`BattleUiContext.Sprites`, none supplied yet) is a placeholder and one `BattleUiContext.Diagnostics` entry per data set and index.

Tests: `BattleScreensTests` (the briefing-to-battlescape flow on one stack; a loaded battle opens directly, no battle opens nothing; the briefing refusal stays; inventory drop, Ctrl click, feature refusals, soldier walk;
selection, skip, undo, level, panel; a click selects a unit and a tile click is refused by walking's owner; later buttons; the inventory over the map and the reveal; a change made elsewhere on tick), `ScreenNavigatorRevealTests`.

Names and opening: `BattleUiContext.Open(targetName, craftOrBaseName)` passes the briefing's already localized lines to `BeginBriefing` (the rules keep the first ones sent, so a client that has them
must pass them on the first open; empty is what a client without them gets). The battlescape, when it opens, selects the first selectable unit if none is selected and centers the camera on it (`BattlescapeState::init`, `NextTurnState::close`'s `btnCenterClick`);
closing the pre-battle inventory replaces it straight with the battlescape. The briefing closes with Enter or Esc and shows the operation-name line when the briefing says so.

Not done (owner in brackets):

- **Geoscape/logistics migration**: S6b (see the plan).
- **`NextTurnState` itself**: the end-of-battle check and the turn-1 autosave of `NextTurnState::close` (S7, with the save acceptance); the centering is done.
- Real map drawing: isometric terrain sprites, pixel scrolling, the cursor, fog shade [Phase 8 presentation]; the plan view is a placeholder. The briefing's cutscene, music and background are listed, not played.
- Inventory drag and drop, paperdoll and item sprites; the keyboard drop picks the first accepted cell.
- The SDL application does not open these screens yet (the host loop that creates the session and calls `BattleUiContext.Open`) [S7].
- Buttons refused by owner as client messages (`BattlescapeClient.LaterButtons` plus the `*Code` constants): kneel, walk, unit up and down, fire, both hand items, psi, special, skills, launch, reserve TU and kneel, zero TUs, end turn, abort, stats, options, minimap, Esc deselect, Ctrl-B, the digit keys (visible units), right and middle mouse buttons. The rules refuse the same actions in their branches.
- Default keys differ from the reference's (it centers with Home, which also works; Ctrl-C toggles all layers as there; skip and undo use `K` and `R`, the level keys `[` `]`).
- Navigator: two pops in a row reveal the middle screen once before it goes (harmless today). A map wider than 224 or longer than 200 tiles overflows the plan view.
- Untested: the arrow scroll keys.

## Next

S6b — geoscape popups, naming, selecting and placing modes, logistics client and shared widgets onto `ScreenNavigator`, with the client cover from `Covers` (the plan's S6b row); then S7 — acceptance and branch audit (it also takes the `NextTurnState` turn-1 autosave and end-of-battle check, and wiring `BattleUiContext.Open` into the SDL host loop). From S6: the screens are in `Oxce.Engine/Battlescape`; `BattleUiContext.Open()` is the entry. From S5: the screens read `BattleViewModel` (`Oxce.Engine/Battlescape`) and `IBattleViewQuery`; the host loop that creates a session sets `Controllers = new BattleUnitControllers(battleQuery)`, and a screen's soldier buttons are `Next`, `Skip`, `ReselectSkipped`, the level buttons `LevelUp` and `LevelDown`, the center button `CenterOnSelection`. Buttons of later branches refuse by their owner's name. From S4:

- The inventory is a command set on stable IDs (`MoveInventoryItem`, `AutoPlaceInventoryItem`, both `IUnitCommand`) with
  `IBattleInventoryQuery`; the host loop that creates a
  `CampaignSession` must set `Controllers = new BattleUnitControllers(battleQuery)` (S6 wires it; `BattleUnitView.ControllerSlot` is the data).
- The pre-battle inventory's close is still `StartFirstTurn` (it saves the equipment layout first). The inventory screen's previous and next soldier are `SelectNextUnit(InventoryOnly)` (`BattleViewModel.NextInventoryUnit`); the inventory takes any live X-COM unit by ID.
- Ground items are per tile in `ArrivalOrder` (`ItemsOnTile`); any new code that puts an item on a tile or in an inventory should take
  `NextArrival()` (`AddItemOnTile` and the inventory moves do).
- The inventory's `Smart` flag is the client's `oxceSmartCtrlEquip` option (default true in the reference); the client keeps its options.
- Not ported, named: `battle.inventory-ammo-unsupported`, `-throw-unsupported`, `-prime-unsupported`, `-layout-screens-unsupported`,
  `-clear-unsupported`, `-autoequip-unsupported`, `-template-unsupported`, `-rename-unsupported`  (each refused by `UseInventoryFeature`; a drop onto a weapon that takes ammo is also the ammo one) and
  `battle.inventory-danger-zone-unsupported` (branch 6). The screens (S6) show the buttons and send `UseInventoryFeature`, so the
  refusals come from the rules.
- From S3, still open: the briefing's names are set by the client's `BeginBriefing`; the SDL client does not send it yet (S6 builds the
  briefing screen).
- `PrimedSpawnItems`, `SpawnNeedsGravity` and `FirstTurnRefusal` are the checks to keep when branch 4 adds gravity and branch 8 script
  bindings: retire `battle.spawn-gravity-unsupported`, `battle.new-turn-script-unsupported`, `battle.turn-recovery-script-unsupported`
  where their owner lands.

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
- `BattleUnitControllers` is the `IUnitControllers` (S4); the host loop installs it (S6).
- Untested (unreachable or needs a removed base): the `ArgumentException` path of `QuoteStillHolds`; the
  `PlaceStartingBase` quote clear (an unplaced base cannot be quoted).
- Same gotchas as before (below).
- Gotchas carried from branch 2 (see `docs/battle-save-status.md` "## Next"): write test files with LF; add C# string
  literals with `\n` through the Edit tool; the test runner is Microsoft Testing Platform
  (`dotnet test --project <proj> --filter-class <full name>`); battle test helpers (`BattleStartTests.Requested`,
  `Started`, `Battle`, `BattleSaveRootTests.Generated`, `BattleSaveUnitTests.Loaded`) are internal for reuse; a
  campaign snapshot with a battle restores only with terrain files; a test that needs a battle past its first turn plays
  `BeginBriefing`, `ConfirmBriefing` and `StartFirstTurn` (`BattleCampaignSaveTests.AtFirstTurn`); raw string literals in test rules
  have their indentation stripped (list entries start at two spaces).
- Named refusals this branch owns, done in S3: `battle.preview-unsupported` (Phase 8, with the craft screens), `battle.casualties-unsupported` (branch 6), `battle.no-live-aliens`, `battle.no-live-soldiers`,
  `battle.spawn-gravity-unsupported` (branch 4), `battle.turn-recovery-script-unsupported` (branch 8) and the ones it hands on below;
  `battle.save-before-first-turn` is retired. S4 introduced the `battle.inventory-*-unsupported` refusals above (ammo, throw, prime, layout screens, clear, autoequip, template, rename, danger zone); S3 hands on
  `battle.spawn-gravity-unsupported` (branch 4), `battle.turn-recovery-script-unsupported` and `battle.new-turn-script-unsupported` (branch 8), `battle.turn-one-revive-unsupported` and
  `battle.reinforcements-unsupported` (branch 6), and `battle.no-live-aliens` and `battle.no-live-soldiers` (branch 7). The buttons of later branches
  refuse by their owner's name (S6); the minimap and extended-battlescape links go to branch 8 or Phase 8. Kept by later owners:
  `OxceBattleSave.UnsupportedTagType` (branch 8), the inherited "Crashed UFO battlescape transitions require tactical
  simulation" (branch 7), the crashed UFO's power-source explosion (branch 5).
