# Battle movement status

Phase 7 branch 4 (`claude/battle-movement`), from `main` after PR #80 (branch 3 merged). Plan:
[Phase 7 branch 4](phase-7-plan.md#branch-4--battle-movement); decisions:
[ADR 0037](decisions/0037-host-authoritative-session.md), [ADR 0038](decisions/0038-battle-aggregate.md),
[ADR 0039](decisions/0039-battle-save-intentional-differences.md) and the plan's branch decisions. ADR 0040 (battle
movement intentional differences) is created by the first slice that records one.

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-11-01) |
| S1 movement groundwork: constants, action costs, reserved TUs, unit primitives | Planned |
| S2 pathfinding | Planned |
| S3 lighting | Planned |
| S4 field of view and per-faction visibility | Planned |
| S5 turning and kneeling | Planned |
| S6 doors | Planned |
| S7 gravity and falling | Planned |
| S8 walking and flying | Planned |
| S9 fire, smoke and terrain destruction | Planned |
| S10 battlescape client | Planned |
| S11 acceptance | Planned |
| Branch audit | Planned |

## S0 — Planning (2026-11-01)

Reference studied: `Pathfinding` (`calculate`, `getTUCost`, the cost multipliers and rounding), `UnitWalkBState`
(`think`, `postPathProcedures`), `UnitTurnBState`, `UnitFallBState`, `BattlescapeGame` (`kneel`, `checkReservedTU`,
`checkForProximityGrenades`, `endTurn`'s door closing), `TileEngine` (`calculateLighting` and its layers, `addLight`,
`calculateUnitsInFOV`, `calculateTilesInFOV`, `calculateFOV`, `visible`, `checkReactionFire`, `getSpottingUnits`,
`determineReactionType`, `tryReaction`, `hit`, `unitOpensDoor`, `checkAdjacentDoors`, `closeUfoDoors`,
`applyGravity`), `Tile::openDoor` and `prepareNewTurn`, `SavedBattleGame::prepareNewTurn` and `loadMapResources`,
`InventoryState`'s destructor, `BattleUnit` (`startWalking`, `keepWalking`, `knockOut`, `checkForReactivation`,
`getReactionScore`, the kneel and turn costs), `Mod` (the battle constants) and `Options` (the defaults of the
battle options the rules read).

- The [plan](phase-7-plan.md#branch-4--battle-movement) splits the branch into S1–S11 and a branch audit: movement
  groundwork, pathfinding, lighting, field of view, turning and kneeling, doors, gravity and falling, walking and
  flying, fire and smoke and terrain destruction, the battlescape client, acceptance.
- Reference facts that shape the plan:
  - A walk is one state machine that also opens doors, turns, falls, updates light and sight per step and stops on
    newly spotted units. It calls three things later branches own: `checkReactionFire` (it needs `canTargetUnit`'s
    firing form, the AI's `brutalScoreFiringMode`, reaction scripts and RNG), `checkForProximityGrenades` (explosions)
    and, for burn-floor units, `TileEngine::hit`. Kneeling and landing after a fall call `checkReactionFire` too; a
    player's turn does not (only the AI's turn-then-kneel path). Before any reaction is chosen, `getSpottingUnits`
    changes spotting (`setVisible`, `addToVisibleUnits`) for every opposing unit that sees the mover, armed or not,
    so that part is ported with the commands. `checkForProximityGrenades` also enables `proximityTrigger` fuses,
    removes `proximityExplode` items and changes flare glow without any explosion.
  - A fall onto a unit with no escape knocks it out and `checkForCasualties` makes it unconscious within the same
    action, before the walk resumes; the plan refuses such falls before the command (branch 6's casualty work).
  - A player's walk is always `BAM_NORMAL` with the `strafe` option off; Shift sets `ignoreSpottedEnemies`. Brutal
    units (hostile ones by default, `brutalAI`) skip the Bresenham shortcut.
  - `checkReservedTU`, `Tile::openDoor` and `determineReactionType` all read `BattleActionCost` of the reserved action
    and the main-hand weapon, which the port has not projected yet (item action costs).
  - `visible` decides through `canTargetUnit` on the voxel map; the plan moves this sight subset of "voxel tests" from
    branch 5 to S4.
  - Fire and smoke spread only in `SavedBattleGame::prepareNewTurn`, called from the end of turn (branch 6); branch 4
    ports the spread as a routine, branch 6 calls it. `closeUfoDoors` is the same.
  - A UFO door opens over animation frames (`Tile::animate`, frame 7 open) and a walk waits for it; the port collapses
    animation time (plan decision; ADR 0040 if an outcome changes).
  - Light and sight are runtime state: generation computes lighting, `loadMapResources` computes lighting and every
    unit's field of view, and the first turn's field of view comes from closing the pre-battle inventory and
    `updateSoldierInfo`.
- Interruptions of later branches are refused before the first change with state-only, over-approximating checks
  (reaction fire, proximity, burn floor until S9, a fall with no escape), so every completed command matches the
  reference. The refusal reveals that an unseen unit is near; the plan names this leak and branch 5 ends it.
- The plan was audited before this commit. Fixed from the audit: turns carry no reaction check; `getSpottingUnits`'
  spotting is ported with the commands and the reaction filter is the exact state part of `determineReactionType`;
  the proximity refusal covers every `fuseProximityEvent` change and death traps; a fall with no escape is refused
  instead of leaving the knock-out to the next command; `ignoreSpottedEnemies` and `extendedRunningCost`; the player's
  move type and the turret; the options `forcedAbsoluteCosts`, `updateTurnsSinceSeenByClue`, `brutalAI` and
  `brutalCivilians`; which spotting state is authoritative on load and the hostile/non-hostile pair rule;
  `calculateLineVoxel` and `getOriginVoxel` in S4; the rounding tests moved to S2; branch 3's conflicting notes on
  `SpawnNeedsGravity`.
- Inherited from branch 3: `battle.spawn-gravity-unsupported` (S7 retires it); the battlescape buttons and inputs
  owned by branch 4 (S10); `TileOf` for a unit's tile before the first turn; ground-item arrival order
  (`NextArrival()`); selection changes reach clients through events; `HasUncheckedCasualty` gates commands until
  branch 6.

## Next

S1 — movement groundwork (plan row S1). Notes for its worker:

- **Constants.** `RuntimeRules.Campaign` already has `MaximumViewDistance` and `MaximumDarknessToSeeUnits`
  (`CampaignStartSettingsComposer`). Not read yet: `lighting` (`maxStatic` 16, `maxDynamic` 24, `enhanced` 0),
  `tooMuchSmokeThreshold` (10), `extendedMovementCostRounding` (0, a `Mod` static read at `Mod.cpp:2730`) and
  `extendedRunningCost` (false, `Mod.cpp:2729`; S8's `ignoreSpottedEnemies` reads it). Put the
  battle ones where S3 and S4 can read them from the battle's rules; keep the fresh/cache content comparison passing
  (`StrategicReadinessTestContent.Load`).
- **Armor.** The typed armor keys `turnCost`, `turnBeforeFirstStep`, `allowsKneeling`, `allowsMoving` and the personal
  lights exist in `PersonnelTacticalRules.cs`; `RuntimeArmorMoveCosts` (`RuntimeBattleEquipment.cs`) holds only the
  four base kinds. S2 needs the per-move multipliers (`Armor::getMoveCostWalk`, `Run`, `Strafe`, `Sneak`, `FlyWalk`,
  `FlyRun`, `FlyStrafe`, `FlyUp`, `FlyDown`, `ClimbUp`, `ClimbDown`, `GravLift`); project them in S1 with the other
  armor fields. `allowsKneeling`, `allowsRunning`, `allowsStrafing` and `allowsSneaking` are nullable booleans
  (`useBoolNullable`: unset takes the caller's default; for kneeling, whether the unit is a soldier).
- **Action costs.** `ItemRuleLoader.ApplyActions` already reads every `tu*` and `cost*` node, `flatRate` and the
  per-cost flat flags into the typed item rules; what is missing is their runtime projection and
  `BattleActionCost::updateTU` (see `RuleItem::load`). Port the time and energy parts that `checkReservedTU` and
  `Tile::openDoor` read, with `forcedAbsoluteCosts` at its default (off, `BattleUnit.cpp:2407`); other resources of a
  cost (morale, health, stun, mana) are read by branch 5's actions and may be projected now since they come with the
  same nodes.
- **Reserve.** `BattleState` keeps the saved `TimeUnitsReserved` (the reference's `TUReserved`, a
  `BattleActionType`, 0 none) and `KneelReserved`.
  `checkReservedTU` for the player side: the auto to snap to aimed to none fallbacks, the kneel reserve
  (`getKneelDownCost`, 4, only when the armor allows kneeling and the unit is standing), `justChecking`. Its warning
  messages are client text; the rule returns a classified result. The hostile branch (percentages, AI reserve mode)
  is branch 6's: leave it out with a comment, since no hostile unit acts before branch 6.
- **Unit primitives.** `BattleUnit` has `Direction`, `DirectionTurret`, `Kneeled`, `Floating`, `MoveCosts` and the
  saved TU and energy. Add what S2 onward need as runtime state with whole-step semantics: `lookAt` (shortest turn,
  `directionTo` and the turret), `turn` one eighth per call, `abortTurn`, a whole step (`startWalking` and
  `keepWalking` reduced to moving the unit's tiles: `setTile` for every footprint tile through `BattleMap.SetUnitAt`,
  `lastPos`, `Floating` from `haveNoFloorBelow`, kneeling cleared), `updateTileFloorState`, `getReactionScore`
  (double), `getTurnCost`, the kneel costs (up 8, down 4). Units out of play or before the first turn: use
  `BattleState.TileOf` (branch 3 gotcha).
- **No commands yet.** S1 adds rules and state only; the first commands are S5's. Tests are unit tests on synthetic
  battles (`BattleStartTests.Started` and the battle helpers listed below).
- Gotchas carried from branch 3 (see `docs/battle-session-ui-status.md` "## Next"): write test files with LF; add C#
  string literals with `\n` through the Edit tool; the test runner is Microsoft Testing Platform
  (`dotnet test --project <proj> --filter-class <full name>`); battle test helpers (`BattleStartTests.Requested`,
  `Started`, `Battle`, `BattleSaveRootTests.Generated`, `BattleSaveUnitTests.Loaded`) are internal for reuse;
  `BattleCampaignSaveTests.AtFirstTurn` (plays `BeginBriefing`, `ConfirmBriefing` and `StartFirstTurn`) is private, so
  make it internal or follow its steps for a battle past its first turn; a campaign snapshot with a battle restores
  only with terrain files; raw string literals in test rules have their indentation stripped (list entries start at two
  spaces).
- Later slices (for planning ahead): S2 builds on `BattlePathing` (the wall checks are there; its floor and unit
  parts are missing) and projects the unit rule's `isBrutal` and `isNotBrutal` for `BattleUnit::isBrutal`. S4 can read voxel data through `LoftempsCodec` (`src/Oxce.Formats/Terrain`); the battle does
  not load it yet. Branch 2 and 3 reload comparisons will see recomputed light and sight from S3 and S4 on; extend
  them rather than masking the new state.
