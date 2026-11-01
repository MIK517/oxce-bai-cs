# Battle movement status

Phase 7 branch 4 (`claude/battle-movement`), from `main` after PR #80 (branch 3 merged). Plan:
[Phase 7 branch 4](phase-7-plan.md#branch-4--battle-movement); decisions:
[ADR 0037](decisions/0037-host-authoritative-session.md), [ADR 0038](decisions/0038-battle-aggregate.md),
[ADR 0039](decisions/0039-battle-save-intentional-differences.md) and the plan's branch decisions. [ADR 0040](decisions/0040-battle-movement-intentional-differences.md)
(battle movement intentional differences, created by S2).

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-11-01) |
| S1 movement groundwork: constants, action costs, reserved TUs, unit primitives | Done, awaiting audit (2026-11-01) |
| S2 pathfinding | Done, awaiting audit (2026-11-01) |
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

## S1 — Movement groundwork (2026-11-01)

Reference: `Mod.cpp` (`extendedRunningCost`, `extendedMovementCostRounding`, `tooMuchSmokeThreshold`, `lighting`), `Armor.cpp` (`moveCost`,
`turnCost`, `turnBeforeFirstStep`, the `allows*` flags, the heights), `RuleItem.cpp` (`getCost*`, `getFlat*`, `getDefault`),
`BattlescapeGame.cpp` (`BattleActionCost`, `checkReservedTU`), `BattleUnit.cpp` (`getActionTUs`, `applyPercentages`, `getMainHandWeapon`,
`lookAt`, `turn`, `abortTurn`, `directionTo`, `startWalking`, `keepWalking`, `setTile`, `updateTileFloorState`, `getReactionScore`, the turn and
kneel costs), `Tile.cpp` (`hasLadder`, `hasNoFloor`).

- **Constants.** `RuntimeCampaignSettings` (and the content settings behind it) now read `lighting` (`MaximumStaticLightDistance` 16,
  `MaximumDynamicLightDistance` 24, `EnhancedLighting` 0), `TooMuchSmokeThreshold` 10, `ExtendedRunningCost` false and
  `ExtendedMovementCostRounding` 0. S2 reads `extendedMovementCostRounding`, S3 the lighting ones, S8/S10 `extendedRunningCost` (the walk command's `ignoreSpottedEnemies`, `BattlescapeGame.cpp:2056`), S9 the smoke threshold (`TileEngine::hitTile`).
- **Armor.** `RuntimeBattleArmor` projects `StepCosts` (`RuntimeArmorStepCosts`: the twelve per-step time and energy percentages), `TurnCost`,
  `TurnBeforeFirstStep`, `AllowsMoving`, the nullable `AllowsRunning`, `AllowsStrafing`, `AllowsSneaking` and `AllowsKneeling`
  (`RuntimeBattleArmor.Allows(value, default)` is `useBoolNullable`), and `StandHeight`, `KneelHeight`, `FloatHeight` (-1 for the unit's own).
  `RuntimeBattleUnitRule.Heights` and `RuntimeBattleEquipment.SoldierHeights` carry the unit rule's and the soldier rule's heights;
  `BattleState.HeightsOf` is the constructor's choice (the armor's, else the soldier's or the unit rule's). The base `RuntimeArmorMoveCosts`
  stay as they were (S2 combines them with the unit's saved copy).
- **Action costs.** `RuntimeBattleItem.ActionCosts` (`RuntimeItemActionCosts`) holds every action's cost and flat flags with the reference's
  `getDefault` chains applied: auto and snap fall back to aimed for each resource and then each flag to the use flags, mind control and panic to the
  use cost, the use cost is zero for a psi amplifier without `psiAttackName`, the prime cost zero without `primeActionName`. All six resources
  (time, energy, morale, health, stun, mana) are projected. `BattleActionCost` (`UpdateTimeUnits`, `ClearTimeUnits`, `Shortfall` and
  `HaveTimeUnits`, which name the first missing resource as `BattleActionShortfall`) and `BattleUnit.ActionCost` / `SpendCost` port
  `updateTU`, `haveTU`, `getActionTUs`, `applyPercentages` (a percentage is a share of the base statistic, at least 1, with `float` arithmetic as
  the reference) and `spendCost`. `BattleOptions.ForcedAbsoluteCosts` is the option at its default (off). The skill form (`skillRules`) is branch 5's.
- **Reserve.** `BattleState.CheckReservedTimeUnits` is `checkReservedTU` for the player's side with the saved `TimeUnitsReserved` and
  `KneelReserved`: the auto, snap, aimed and none fallbacks, the kneel reserve (`BattleUnit.KneelDownCost`, 4, for a standing unit whose armor allows
  kneeling; `getType() == "SOLDIER"` is `SoldierId is not null`), `justChecking`, and the refusal warning as `ReservedTimeUnitsWarning` (the text is
  the client's). A unit of the side that is not acting, and any neutral unit, only needs its time units. The hostile side's percentage reserve is
  branch 6's: it is not run and answers `HostileReserveUnsupported`. The reference's warning for a kneel reserve with no weapon reserve is the
  "kneeling and firing" one (its `switch` only says "kneeling" for a type of kneel); ported as it is. `MainHandWeapon` is `getMainHandWeapon`
  (`HaveAnyAmmo`, `CurrentWaypoints` and the special firearm included).
- **Unit primitives.** `BattleUnit` has `ToDirection`, `ToDirectionTurret`, `FaceDirection`, `VerticalDirection`, `LastPosition`,
  `HaveNoFloorBelow` and `IsBigUnit`, with `SetDirection`, `DirectionTo`, `LookAt` (a position, with the turret form, and a direction, with `force`),
  `Turn` (one eighth, a half turn clockwise, the turret with the body when the unit has one), `AbortTurn`, `Kneel`, `TurnCost(rules)`,
  `KneelUpCost` 8, `KneelDownCost` 4, `KneelChangeCost` and `ReactionScore` (double). `BattleState.UpdateTileFloorState`, `SetTile` (the footprint
  hand-over, the floor state, the flying and landing status change) and `WalkStep` (`startWalking` and `keepWalking` collapsed to one whole step:
  facing, kneeling cleared, flying for a unit with no floor or on a vertical step, the arrival, landing, the strafing face direction, and the motion
  points of a step: 30 for a big unit, else 4 for a stand height above 16, else 3) are the tile changes of a walk. `BattleMap.HasLadder` is
  `Tile::hasLadder`. The floor state is computed where the reference's `setTile` computes it: when a unit joins the battle (`AddUnit`, so
  generation and load) and in the first turn's `ResetUnitTiles`.
- **Placement.** Every generated, spawned and loaded unit now gets `SetDirection` (turret and "to" directions) as `setDirection` and `BattleUnit::load` do. The hostile-side reserve is the named refusal `battle.hostile-reserve-unsupported` (`ReservedTimeUnitsResult.Unsupported`).
- **Reference quirks kept.** `DirectionTo` of a unit's own tile is south (`atan2(0, -0)` is a half turn). `Turn` sets an unconscious unit standing.
  `SetTile` replaces whoever the new tiles name, as the reference does; the walk (S8) checks for units before it.
- **No commands.** S1 adds rules and state only. Tests: `BattleMovementGroundworkTests` (21): the constants and armor fields at their
  defaults and read, the action-cost chains, percent, flat and at-least-1 costs for every resource, `haveTU`'s order, each reserve mode and fallback,
  the kneel reserve, another side, the hostile refusal, the main-hand pick, `directionTo` around the compass, every `lookAt` and turn pair, the turret,
  the reaction score, the costs, a step with its motion points and strafing face, a hole and a ladder, a big unit's four tiles.

## S2 — Pathfinding (2026-11-01)

Reference: `Pathfinding.cpp` (`calculate`, `aStarPath`, `getTUCost`, `getMovementType`, `isBlocked`, `isBlockedDirection`, `canFallDown`,
`isOnStairs`, `validateUpDown`, `bresenhamPath`, `findReachable`, `findReachablePathFindingNodes`, `getStartDirection`, `dequeuePath`,
`abortPath`), `PathfindingNode`, `PathfindingOpenSet`, `BattleUnit.cpp` (`isBrutal`, `avoidsFire`, `isAIControlled`,
`getUnitsSpottedThisTurn`), `Unit.cpp` (`avoidsFire`, `isBrutal`, `isNotBrutal`), `Tile.cpp` (`getOverlappingUnit`, `hasNoFloor`, the ladder and
grav lift flags), `Options.cpp` (the defaults of `strictBlockedChecking`, `sneakyAI`, `brutalAI`, `brutalCivilians`, `autoCombat`,
`autoCombatControlPerUnit`, `aiPerformanceOptimization`).

- **`BattlePathfinder`** (`BattlePathfinding.cs`, internal) is the port: one instance holds the nodes and the last path as the reference's
  `Pathfinding` does, reads the battle and changes nothing. `GetTuCost` is `getTUCost` for a small or a 2x2 unit (the part masks for going up
  and down stairs, falling, flying and climbing; the stairs, the hole, the ladder, the grav lift and the flying step; the wall average; the fire
  penalty; the TFTD depth surcharge of 2; the strafe surcharge; the cap of 100; the X check of a big unit's four tiles; the door refusal for a
  big unit; the unit and armor percentages and the three rounding modes of `extendedMovementCostRounding`). `Calculate` is `calculate` (the
  destination adjustments: stairs, a terrain level of -24, a hole for a walking unit, the strafe move; the Bresenham shortcut except for a
  brutal unit; then A*); `Path` is first step first (the reference keeps it reversed). `FindReachableNodes` and `FindReachable` are
  `findReachablePathFindingNodes` and `findReachable` (entire map, an alternate start, just checking, the unit's full statistics, the move
  type, the `aiPerformanceOptimization` cap). `IsBlockedDirection`, `IsOnStairs` and `ValidateUpDown` are public on it. The open set is a
  binary heap with the standard library's sift order ([ADR 0040](decisions/0040-battle-movement-intentional-differences.md)), so the A* tie
  order is the reference's.
- **Not here.** `BAM_MISSILE` and every missile-target branch (branch 5; `BattleActionMove` has no missile value, and the missile-only parts of
  `getTUCost`, `isBlocked` and `validateUpDown` are left out), `previewPath` and `refreshPath` (the client's path display, S10). A unit of armor
  size over 2 throws: the reference's part arrays hold four tiles.
- **Quirks kept.** The Bresenham shortcut ignores `maxTUCost` (only A* is held to it). After A*, `TotalTimeUnitCost` is the last cost A* tried
  and not the path's. `isBlocked` on a floor says "not blocked" for the mover's own tile and for an out unit, even over an impassable floor.
  `Calculate` keeps `StrafeMove` from the previous call when it returns early. A step onto a tile with no floor costs the default 4, plus the
  floor's cost. A big unit's cost is the average of its four tiles by integer division. A stairs whose second part is wrong answers "not
  stairs" at once, before the other orientation or TFTD's is looked at.
- **Units in the way.** `isBlocked` uses `BattleUnit.IsVisibleToPlayer(rules)` (`getVisible`: a player-faction unit, a unit in an `alwaysVisible` armor, else the saved player spotting), a unit of the mover's side,
  and `BattleUnit.UnitsSpottedThisTurn` (new, runtime and empty until S4 fills it) for a non-player mover. `Tile::getVisible` is
  `BattleMap.Visibility`/`IsVisible` (new, a `Sint16` counter as the reference's, runtime, zero until S4 adds to it; only the sneaking search reads it). `IgnoreFriends` is `setIgnoreFriends`.
- **Options.** `BattlePathingOptions` (`BattleOptions.Pathing`, the defaults) is passed to the pathfinder as a value, so a test chooses another
  set. `BattlePathing.IsBlockedDirection` takes `strict` for `strictBlockedChecking`. `BattleUnit.UsesBrutalAi(rules, options)` is `isBrutal`
  (the saved `IsBrutal` and `IsNotBrutal` stay the load-only fields) and `AvoidsFire(rules)` is `avoidsFire`.
- **Rules.** `RuntimeBattleUnitRule` projects `SpecialAbility` (`specab`), `AvoidsFire` (nullable), `IsBrutal` and `IsNotBrutal`;
  `RuntimeBattleArmor` projects `SpecialAbility` (a soldier's own special ability, for `avoidsFire`) and `AlwaysVisible`.
- **BattleMap.** Tile-index forms: `HasNoFloor(int)`, `HasNoFloorOnly` (`hasNoFloor(nullptr)`), `TileAbove`, `TileBelow`, `HasGravLiftFloor`,
  `HasLadderOn(part)`, `IsDoor`, `Visibility`/`IsVisible`/`SetVisibility`. `BattleState.UnitById` is internal.
- **Queries.** The pathfinder is the port's internal query surface; the viewer-scoped path, path cost and reachable-tile queries are now named in the plan's S10 row (plan change; S8's walk calls the pathfinder directly). A player's search does not read hidden information: it ignores units it does not see.
- **Tests.** `BattlePathfindingTests` (29, compatibility project): step costs and the three rounding modes, move types and the percentages, walls
  and their average, strict checking, the depth surcharge and the fire penalty, strafing, stairs up and down and their detection (north, east,
  TFTD), destinations above a hole or inside a -24 object, holes and falls, ladders, grav lifts and flying, units in the way for each side, the
  overlapping unit, a big unit (average, doors, the X check, falling onto and under units), straight lines, A* around a wall, the limit,
  unreachable ends, a brutal unit, a sneaking one, the reachable tiles in each form, the climb-down, fly-down, fly-run and fly-strafe percentages, wall ladders, the ladder-below no-fall exception, a big unit on stairs (hole-up mask), `alwaysVisible`, the `aiPerformanceOptimization` cap. The two A* paths (`[2, 2, 3, 3]`, `[3, 2, 2, 1]`) are pinned
  and were checked against an independent re-implementation of the reference's heap and search (`.agent-tmp/astar_sim.py`); no reference
  binary runs here.

## Next

S3 — lighting (plan row S3). Notes for its worker:

- **What S2 left for it.** `BattleMap.Visibility` (the tile's `Sint16` visible counter the sneaking search reads; S4 adds to and takes from it, as `Tile::setVisible` does) and
  `BattleUnit.UnitsSpottedThisTurn` are runtime state S4 fills; S3 does not need them. The map's tile-index helpers (`TileAbove`, `TileBelow`,
  `HasNoFloor(int)`) are reusable for the light and blockage walks.
- **Constants.** `RuntimeCampaignSettings` has `MaximumStaticLightDistance` 16, `MaximumDynamicLightDistance` 24 and `EnhancedLighting` 0 (S1).
- **Options.** `BattlePathingOptions` is pathfinding's only; give lighting its own value rather than growing it.
- **Test helpers.** `BattlePathfindingTests.Flat` (private) builds a cleared 30x30x2 arena from `BattleMovementGroundworkTests.Open` (now with a
  `ufo` argument) with TESTSET records appended after the three the generator uses (`Terrain`); copy that approach for light sources and blockers
  (an MCD record's light fields: `LightBlock` at offset 51, `LightSource` at 58). `Open`, `Place`, `Set` and `East` are internal.
- **Gotchas.** A post-edit hook reformats edited C# files, so read a file again before a second edit. A unit that still has its
  `InventoryTile` has no tile of its own: do not run the pathfinder for it.
- Later slices (for planning ahead): S4 can read voxel data through `LoftempsCodec` (`src/Oxce.Formats/Terrain`); the battle does not load it
  yet. S9 reads the smoke threshold from `RuntimeCampaignSettings`. Branch 2 and 3 reload comparisons will see recomputed light and sight from S3
  and S4 on; extend them rather than masking the new state. S8's walk calls `BattlePathfinder.Calculate` and `GetTuCost` (a walk prices each
  step from the unit's state at that step) and relies on S4 to fill `UnitsSpottedThisTurn`.
- Gotchas carried from branch 3 (see `docs/battle-session-ui-status.md` "## Next"): write test files with LF; add C# string literals with
  `\n` through the Edit tool; the test runner is Microsoft Testing Platform (`dotnet test --project <proj> --filter-class <full name>`);
  battle test helpers (`BattleStartTests.Requested`, `Started`, `Battle`, `BattleSaveRootTests.Generated`, `BattleSaveUnitTests.Loaded`) are
  internal for reuse; `BattleCampaignSaveTests.AtFirstTurn` is private; a campaign snapshot with a battle restores only with terrain files; raw
  string literals in test rules have their indentation stripped (list entries start at two spaces).
