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
| S3 lighting | Done, awaiting audit (2026-11-01) |
| S4 field of view and per-faction visibility | Done, awaiting audit (2026-11-01) |
| S5 turning and kneeling | Done, awaiting audit (2026-11-07) |
| S6 doors | Done, awaiting audit (2026-11-07) |
| S7 gravity and falling | Done, awaiting audit (2026-11-07) |
| S8 walking and flying | Done, awaiting audit (2026-11-07) |
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

## S3 — Lighting (2026-11-01)

Reference: `TileEngine.cpp` (`calculateLighting`, `calculateSunShading`, `calculateTerrainBackground`, `calculateTerrainItems`,
`calculateUnitLighting`, `addLight`, `calculateLineHelper`, `blockage`, `horizontalBlockage`, `verticalBlockage`, `togglePersonalLighting`, the
`VisibilityBlockCache` helpers), `Tile.cpp` (`resetLightMulti`, `addLight`, `getLightMulti`, `getShade`, the object cache), `MapData.cpp`
(`getBlock`), `BattleItem.cpp` (`getGlow`, `getGlowRange`), `BattleUnit.cpp` (`getRightHandWeapon`, `getLeftHandWeapon`),
`BattlescapeGenerator.cpp` (`run`), `SavedBattleGame.cpp` (`loadMapResources`), `InventoryState.cpp` (the destructor).

- **Light state.** `BattleMap` (now `partial`, `BattleMapLight.cs`) holds the four layers per tile (`BattleLightLayer`: ambient, fire, items, units;
  `Light`, `AddLight`, `ResetLightMulti`, `LightMulti`, `Shade`) and the blockage cache (`BattleLightCache`: the blocked-direction bits, the big wall bits,
  the height; `LightCache`, `HasSmoke`, `HasFire`, `BlocksUp`, `BlocksDown`, `Blocks(direction, z)`, `IsBigWall(direction)`). Runtime only: nothing is
  saved, and `BattleMap.Reset` clears both. `TileAt(x, y, z)` is `getTile(Position)` (-1 outside the map).
- **Blockage.** `BattleBlockage` (static, reused by S4 and S9) is `blockage`, `horizontalBlockage` and `verticalBlockage` on tile indexes, for
  `BattleBlockType` `None` (`DT_NONE`: the map data's vision block), `Smoke` (`DT_SMOKE`) and `Damage` (every other damage type: the high explosive
  block, which is `getBlock` at the default `battleTerrainSquishyness` of 1). The big wall direction table, the two-way diagonal check of the original
  game for `None` (and the averaged form otherwise), the "-1 reveals a big wall" return, the open UFO door zeroing and `skipObject` are as the
  reference. Quirks kept: a solid floor blocks sight with 256 unless it has no floor; a floor with a zero block adds its armor for smoke too and then
  any part with map data stops smoke with 256 (`wall != 0`); the second way round a diagonal replaces the first's result.
- **Lighting.** `BattleState.CalculateLighting(rules, layer, position, eventRadius, terrainChanged)` is `calculateLighting`: the blockage cache refresh (the
  whole map, or the event radius plus one), the resets of the layer and those after it, then sun shading, terrain light sources and fires, glowing items
  and the units. Sun: 15 less the global shade, 2 less under a roof (a floor or an object part pointing down above, summed) only when the global shade
  is 4 or less. Terrain: the brightest light source of the four parts, 15 for a tile on fire, capped at `maxStatic - 1`. Items: a flare's glow
  (battle type 10 while `defaultBehavior` with no fuse type, or with an enabled running fuse; the range is the rule's `power`), 10 for an item that
  carries a burning unit, capped at `maxDynamic - 1`. Units (not out): the armor's personal light for the side (X-COM's only while the toggle is on),
  the hand-held flares and burning corpses of the left and right hand, 15 on fire, capped likewise, added on each tile of the footprint.
- **`addLight`.** Classic: a circle losing a level per tile, the distance `Round(sqrt(voxel distance squared) / 16)` in single precision as the reference
  computes it. Enhanced (`lighting: enhanced` bits 1 fire, 2 items, 4 units): the two voxel lines from the source to the tile (`BattleLines.Walk` is
  `calculateLineHelper`, S4 reuses it), with the cache stopping a line at a blocked direction (a big wall target excepted), smoke taking a level, a tile
  lower than the line's height taking two, the fire layer stopping at a fire, the lines' average added where it exceeds the tile's light. Iteration
  order (level, row, column) is the reference's, because the enhanced pruning reads the light already added.
- **When it runs.** At the end of `BattleGenerator.Generate` and of `BattleState.Restore` (ambient, whole map, cache included), and at the end of
  `StartFirstTurn` for the items layer (the pre-battle inventory's close; its gravity and field of view are S7 and S4). `TileShade(position)` is
  `Tile::getShade` (0 bright to 15 dark); `TogglePersonalLighting(rules)` is `togglePersonalLighting` (the per-battle toggle is saved state; the
  units layer is recomputed). Both are internal: the viewer-scoped shade query is S4's and the toggle command and button are S10's (the toggle
  also needs S4's `recalculateFOV`). The events of walks, falls, deaths and explosions that call `calculateLighting` are those slices'.
- **Gaps.** A flare's glow range is `getPowerBonus`: a flare whose rule binds a `damageBonus` script or `strengthApplied` is refused at the first turn with
  `battle.flare-power-bonus-unsupported` (branch 8 evaluates it). `strengthApplied` is now projected (`RuntimeBattleItem.StrengthApplied`) and refused the same way. The "fire on the path stops a fire light" branch is ported and tested with `maxStatic: 24` (a source above 15 can arrive
  at 15 or less; at the default 16 a fire is as bright as any source, so no layout shows it). A centre outside the map and a line leaving the map
  are dark here, where the reference indexes out of range. `oxceTogglePersonalLightType` is at its default (per battle).
- **Tests.** `BattleLightingTests` (27, compatibility project): walls, panes, UFO doors, floors, big walls (the direction table, the origin form, the
  reveal), diagonals, vertical steps; the cache (directions, big walls, smoke, fire, heights, roofs, a partial refresh equal to a full one); the sun at
  several shades and the roof at the 4/5 boundary; a light source's circle, rounding, level above and cap; fires and floor sources; ground flares and
  fused ones; a flare in a hand, a unit on fire, a unit out; a burning corpse and the dynamic cap; each side's personal light and the toggle; enhanced
  lighting per layer, windows, smoke and tall objects (hand-computed values); partial recomputes equal to a full one; a generated battle's lighting
  equal to its reload's; the first turn's item lighting. Values were worked out by hand from the reference's arithmetic, not run against it.

## S4 — Field of view and per-faction visibility (2026-11-01)

Reference: `TileEngine.cpp` (`calculateLineTile`, `calculateTilesInFOV`, `calculateUnitsInFOV`, both `calculateFOV` forms, `recalculateFOV`,
`setupEventVisibilitySector`, `inEventVisibilitySector`, `visible`, `canTargetUnit`, `getSightOriginVoxel`, `getOriginVoxel`,
`getVisibleDistanceMaxHelper`, `getTrajectoryDataHelper`, `trajectoryStepSize`, `calculateLineVoxel`, `voxelCheck`, `getDirectionTo`,
`getSpottingUnits` for its origin and sight test), `BattleUnit.cpp` (`getRadiusVoxels`, `getLoftemps`, the visible-unit, visible-tile and
line-of-fire lists, `checkViewSector`, `getMaxViewDistance*`, `setTurnsSinceSeen`, `setTileLastSpotted`, the `turnsSinceSpotted` and sniper
setters, `getSpotterDuration`, the sight values the constructors set), `Tile.cpp` (`getOverlappingUnit`, `setLastExplored`,
`setDiscovered`, `setVisible`), `Mod.cpp` (`maxViewDistance`, `maxDarknessToSeeUnits`, the voxel data), `Armor.cpp` (the vision fields),
`InventoryState.cpp` (the destructor), `SavedBattleGame.cpp` (`loadMapResources`, `resetUnitTiles`, `isBeforeGame`).

- **Line of fire templates.** `BattleVoxelData` (`GEODATA/LOFTEMPS.DAT`, 16 rows of 16 bits per template) is read through
  `BattleTerrainLoader.TryLoadVoxelData` and kept on `BattleState.VoxelData`. A battle needs the file: generation fails
  (`battle.generation-failed`) and a load is a corrupted save without it, and so when a map object (twelve layers) or a unit's armor
  names a template past its end (`BattleState.VoxelDataFault`, ADR 0040; `battle.line-of-fire-data-invalid` stops the first turn for a unit
  that arrives later). The test terrain files got `BattleTestTerrain.Loftemps()` (templates 0 empty, 1 solid, 2 empty, 3 an 8x8 unit pillar of
  radius 3, 4 a slit along the middle rows).
- **Rules and unit sight.** `RuntimeBattleArmor` projects `Loftemps`, the visibility, camouflage and anti-camouflage at dark and by day,
  `PsiVision`, `PsiCamouflage`, `VisibilityThroughSmoke` (`heatVision`), `VisibilityThroughFire` and `FearImmune` (nullable);
  `RuntimeBattleUnitRule` projects `IgnoredByAi` and `SpotterDuration` (`spotter`, or `intelligence` for -1); `RuntimeSoldierBonusRule`
  the five vision values a bonus adds. `BattleUnit.Vision` (`BattleUnitVision`: the dark and day distances, psi vision, the smoke and fire
  visibility) is set where the reference's constructors set them: a unit rule's unit sees `maxViewDistance` in the dark if hostile, else 9, by
  the armor's value when it has one; a soldier's is the armor's plus its bonuses', clamped between 1 and the maximum. Runtime, never saved.
  `BattleUnit` also has `HeightOf`, `FloatHeightOf`, `LoftempsOf`, `RadiusVoxels` (the first template, at most 5; 15 for a big unit),
  `CheckViewSector` (the body's quadrant from any tile of the unit) and `SpotterDuration(rules)`.
- **The sight engine.** `BattleSight` (`BattleState.Sight()`, one instance per operation, since it holds the event sector) is the port of
  the tile engine's sight half: `VoxelCheck` and `CalculateLineVoxel` (the trajectory keeps the impact voxel twice when it stores the path,
  as the reference does), `CalculateLineTile`, `CanTargetUnit` (the player's fine slices for a human-controlled unit, the five columns at
  four heights for the others; single-precision arithmetic as the reference), `SightOriginVoxel`, `SnapShotOriginVoxel` (`getOriginVoxel` for
  `BA_SNAPSHOT` at the default options, which S5's `getSpottingUnits` builds on), `Visible` (friends, the global distance, psi vision with
  its camouflage and fear immunity, the darkness, camouflage and anti-camouflage distance, smoke and fire density along the line with
  `visibilityThroughSmoke` and `-Fire`, and the quality formula in integers), `CalculateTilesInFov`, `CalculateUnitsInFov`,
  `CalculateFov(unit)`, `CalculateFov(position, radius, updateTiles, appendToTileVisibility)` and `RecalculateFov`, with the event sector
  (`setupEventVisibilitySector` in `MathF`, ADR 0040). `BattleState.IsBeforeGame` is `isBeforeGame`: true from generation, false after the
  first turn's `resetUnitTiles` and after a load; while it is set no unit stops a voxel line.
- **Spotting.** `calculateUnitsInFOV`'s rules are kept, quirks included: the player's sight marks every unit it checks visible (friends
  too); only a hostile/non-hostile pair is listed (`bu` hostile and the seer not, or `bu` not hostile and the seer hostile and not yet
  listing it), so the neutral faction never lists the player's units and the player never lists neutrals; a listed unit gets the seer's
  `tileLastSpotted` (and the blind-shot one), `turnsSinceSeen` 0, the hostile entry of `turnsSinceSpotted` 0 and the hostile entry of the
  sniper turns (the seer's spotter duration), and, for any other faction, that faction's entries of both arrays. The port's per-faction
  spotting flags (D2): a listed unit is also marked spotted by the seer's faction when that is hostile or neutral. A big unit is checked
  at its first tile in the view sector and later tiles inherit a negative result. Visible units leave the list by the last one taking
  their place (`removeFromVisibleUnits`); `UnitsSpottedThisTurn` is cleared in `PrepareNewTurn` as `BattleUnit::prepareNewTurn` does.
  `checkForReactivation` (the AI's state) is branch 6's.
- **Tiles and discovery.** A unit's visible tiles are kept as the reference's list and set (`VisibleTiles`, `HasVisibleTile`), with the
  line-of-fire tiles of the units it lists. A player's unit adds one to a tile's visibility count in `addToVisibleTiles` and another in
  `calculateTilesInFOV`, and `clearVisibleTiles` takes one away for any faction's unit: the reference's counts drift by one for each full
  recalculation and by one for each tile a hostile unit clears, and the port keeps that (only "is any" is read). The seeing faction
  explores a tile (`Tile::setLastExplored`) only on its own turn. Discovery marks the tile's floor (which discovers its walls) and the west
  wall of the tile east and the north wall of the tile south of each visible tile. The player's flags are the reference's; the hostile and
  neutral factions have their own (`BattleMap.IsDiscovered(tile, part, faction)`, `SetDiscovered`, `DiscoveredFlags`), set the same way
  for their units' sight and saved in `oxcePortBattle`'s `discovery` (`BattleSnapshot.FactionDiscovery`; ADR 0039).
- **When it runs.** `StartFirstTurn` recalculates every unit's sight after the item lighting (`InventoryState`'s destructor); `Restore`
  does after the ambient lighting (`loadMapResources`), keeping the saved spotting and discovery and only adding to them; and
  `TogglePersonalLighting` does after the unit lighting. Generation does not (the reference's does not), and neither does spawning (`SpawnNewUnit` and `SpawnNewItem`
  skip the reference's `calculateFOV` and glow update): the first turn's full recalculation covers a spawn at the first turn, and later
  spawns are the slices that spawn (branch 5 onwards). A move in the inventory of a running battle recomputes the items' light and everyone's
  sight (`InventoryState`'s destructor; the port has no close command, so each move does it). A restore of a save from before the first turn
  (Briefing phase) computes no sight and keeps `IsBeforeGame`, so the first turn computes it as for a never-saved battle; a restore of a
  battle with a `visibilityUnit` script keeps the saved spotting and discovery and computes none. `updateSoldierInfo` also runs
  `calculateFOV(selectedUnit)` (battlescape init, selection, after actions); the port's selection commands and `QueryUnitInfo` do not, and S5
  onwards must run it where the reference does. The walk, turn, door and fall slices call `CalculateFov` themselves.
- **Queries.** `LevelView` shows a viewer the parts its faction has discovered (plus the tiles a non-player faction explored on its turn,
  as before); `BattleUnitInfo.VisibleUnitIds` (was `VisibleHostileUnitIds`) is `updateSoldierInfo`'s list: the units the selected unit
  sees in list order (only a hostile unit's list holds neutrals), then the hostile units its side has spotted. `BattleOverview` still shows a unit to a faction
  that has spotted it.
- **Refused.** `battle.visibility-script-unsupported` (branch 8): an armor in the battle with a `visibilityUnit` script, or a global event
  of that name, refuses the briefing's confirmation, the first turn and, for a battle in progress, every battle command (the gate in
  `CampaignBattles`). A load accepts such a battle (its recalculation runs without the script). The private corpus's Rosigma mod binds
  one, so its battles stop at the briefing until branch 8. `battle.line-of-fire-data-invalid` as above.
- **Not ported.** The turret direction (`strafe`), `battleRealisticAccuracy`, `oxceEnableOffCentreShooting` and `aiCheatMode` are at their
  defaults (off), so `getSightOriginVoxel`'s off-centre form, `getOriginVoxel`'s realistic form and the cheating view sector are absent;
  `canTargetUnit`'s `rememberObstacles` and `calculateLineTile`'s `minLightBlock` belong to their callers (branch 5); the `visibilityUnit`
  script's distance in tiles (`visibleDistanceUnitMaxTile`) is not computed.
- **Tests.** `BattleSightTests` (45) and `BattleSightCoverageTests` (13: other levels, a candidate unit, grav lift and open door voxels, tall and big origins, the darkness threshold at 9, soldier bonuses, the inventory move, a briefing save, the visibility script's gate and load, the line of fire data and discovery refusals), compatibility project on a cleared 30x30x2 arena, numbers worked out from the reference's arithmetic:
  a seen pair and every spotting field (and sniper turns, intelligence), a wall hiding a unit and the partial update removing it, a low wall
  seen over, a slit that hides neither form of `canTargetUnit`, 20 tiles by day and 21 not, 9 and 10 tiles in the dark, camouflage, anti-camouflage
  and relative camouflage, fire cancelling both, a lit tile ending darkness, the personal light toggled, smoke (two, four, and the near-unit half),
  heat vision and fire density, psi vision with camouflage and fear immunity, each direction's quadrant, a big unit, neutral pairs, the
  spotted list and the visible list's swap removal, discovery and its east/south walls, the visibility counts' drift, the wall's tile revealed,
  the other factions' discovery and level views, an event sector that appends what a change reveals as a full recheck does, an event behind
  the unit, an out unit, `voxelCheck`'s every result, a line's trajectory and step sizes, the pre-game exclusion, the sight and snap-shot origins,
  a loaded battle's sight equal to the never-saved one, the hostile discovery saved, written, read and restored, and the refusals.
  The branch 2 and 3 reload comparisons now compare through `BattleSaveUnitTests.SightFree`, which drops the state a load's field of view
  recomputes (the player's discovery, the other factions' discovery, each unit's spotting); the tests that put extra units on the map
  give them the soldier's `SoldierId` (their heights are now read).

## S5 — Turning and kneeling (2026-11-07)

Reference: `UnitTurnBState.cpp` (`init`, `think`), `BattlescapeGame.cpp` (`secondaryAction`, `kneel`, `popState`), `BattlescapeState.cpp`
(`btnKneelClick`, `updateSoldierInfo`), `TileEngine.cpp` (`checkReactionFire`, `getSpottingUnits`, `determineReactionType`, `unitOpensDoor`),
`Tile.cpp` (`openDoor`), `SavedBattleGame.cpp` (`canUseWeapon`), `BattleUnit.cpp` (`spendTimeUnits`, `getWeaponForReactions`,
`getUtilityWeapon`, `getSpecialWeapon`), `Mod.cpp` (`extendedMeleeReactions`).

- **Commands.** `TurnUnit(UnitId, Target)` (the right click, `secondaryAction` into `UnitTurnBState`) and `KneelUnit(UnitId)` (the kneel button,
  `BattlescapeGame::kneel`) are `IBattleCommand`s and `IUnitCommand`s, run by `BattleState.TurnTowards` and `BattleState.KneelUnit`
  (`BattleMovementCommands.cs`; contracts and reasons in `BattleMovementContracts.cs`), registered in `CampaignBattles`. Both use the selection's
  unit rule: a live unit of the side on turn, after the briefing (`battle.selection-unavailable`, `battle.selection-unit-unknown`). Events:
  `UnitTurned(UnitId, Direction, Eighths, TimeUnitsSpent, Stop)` with `BattleTurnStop` (arrived, no turn, spotted units, not enough time units,
  reserved time units) and `UnitKneeled(UnitId, Kneeling, TimeUnitsSpent)`. The legal-action list does not offer them yet (S10 replaces
  `battle.kneel-unsupported` and the rest of the buttons).
- **Turn.** `lookAt` only turns when the tile is in another eighth than the unit's direction; the turret never turns alone (targeting and `strafe`
  are not commands), so `tu` is the armor's turn cost and the body drags a turret (`BattleUnit.Turn`). Each eighth is one `think`: the reserve
  check (`CheckReservedTimeUnits`), `spendTimeUnits`, `turn`, then the unit's full `CalculateFov`; a larger `UnitsSpottedThisTurn` aborts the turn
  (the reference also tells brutal AI units of the side, which is branch 6's AI state), and a unit that stands again has arrived. A refusal of the
  first eighth (`battle.turn-time-units-reserved`, `battle.turn-not-enough-time-units`) changes nothing; later ones end the turn (`Stop`) with what
  was done. A target outside the map is `battle.turn-target-outside-map`. The AI's turn-then-kneel (`_action.kneel`) is branch 6's.
  Light is not recomputed: a turn changes none.
- **No turn, a door.** A unit that does not turn (same direction, or the tile it stands on) makes `init` try `unitOpensDoor(unit, true)`, which
  S6 ported (below); with no door it completes as `NoTurn`. (S5 refused this case with `battle.door-unsupported`; S6 retired it.)
- **Kneel.** `allowsKneeling` (`battle.kneel-not-allowed`), not floating (`battle.kneel-floating`), and unless the unit kneels down with the kneel
  reserve on, `CheckReservedTimeUnits` (`battle.kneel-time-units-reserved`); then the time units (`battle.kneel-not-enough-time-units`). It pays
  4 (down) or 8 (up), changes stance, runs `CalculateFov(position, 1, updateTiles: false)` for everyone through the tile, then the unit's own full
  `CalculateFov` (`updateSoldierInfo`, which refreshes its tiles and rebuilds its visible units), then the spotting of `checkReactionFire`.
- **Reaction fire and spotting.** `SpotMover` is `getSpottingUnits`' side effects: each unit that passes `PassesSpottingFilters` (not out, not over
  its out threshold, reaction score at least the mover's, not of the side on turn, civilian rule, within the view distance, nobody on the neutral
  side's turn), faces the mover or was hit (`BattleUnit.Ai.WasHitBy`, else the new runtime `BattleUnit.HitState`, or `MeleeAttackedBy` under
  `extendedMeleeReactions` 2, now a rule key in the runtime catalog), can target its tile from the snap-shot origin and sees it lists the mover
  (`AddToVisibleUnits`) and its faction has spotted it (`SetSpotted`, ADR 0039; the player's spotter is the reference's `setVisible`). The
  reaction itself is branch 5's, so `HasPossibleReactor` refuses a kneel with `battle.reaction-fire-unsupported` before any change when a unit
  that passes the filters, is not respawning and has a reaction score above 0.001 carries a weapon `determineReactionType` could pick: any weapon
  with ammo for its hit (a firearm's ammo slot for the hit is itself, so a rifle butt counts) and the time units for it, or a firearm with ammo
  for its snap shot and the time units for it (a psi weapon needs a psi skill). The filters use the kneeler's reaction score after it paid, and
  compare with it cut to an integer (`int threshold`, as the reference). Over-approximation: sight, range and melee reach are not asked, every
  weapon of the hands and the special weapons counts (the preferred hand, `reactionsDisabled*` and `canUseWeapon`'s research, AI use delay,
  depth, mana, two-handed and fixed-water rules only narrow it; the rules do not project those fields yet), so a kneel the reference would complete may
  be refused, never the reverse. A unit that fails the state test never reacts.
- **Tests.** `BattleTurnKneelTests` (compatibility, on the sight tests' arena with a turn cost of 2): each turn direction and its cost, the
  shortest way (a half turn clockwise), no turn and the tile it stands on, a target outside the map, a stop on spotting then a turn that goes
  on, a turn next to an armed unit, TU shortage mid-turn and at the first eighth, the reserve mid-turn and at the first eighth, a turret unit, the
  door refusal (UFO door closed and open, other direction), selection refusals; kneel and stand-up costs, the field of view refresh, the floating,
  armor, TU and reserve refusals (kneel reserve bypass), a refused kneel for an armed reactor in range (gun, melee, a rifle butt without or with
  an unaffordable snap shot, a special weapon), completed for an unloaded gun, a psi weapon without psi skill, no time units, an out unit, a
  respawning unit, a slower unit, a far one (the view distance edge), a free snap shot and a civilian faction; the threshold's integer cut; the
  neutral side's turn and the mover's own side (state queries); the spotting of a weaponless unit, a hit unit that faces away (hit state, the AI's memory, melee memory under rule 2).

## S6 — Doors (2026-11-07)

Reference: `TileEngine.cpp` (`unitOpensDoor`, `checkAdjacentDoors`, `closeUfoDoors`), `Tile.cpp` (`openDoor`, `closeUfoDoor`, `getTUCost`,
`isUfoDoorOpen`, `setMapData`), `UnitTurnBState.cpp` (`init`), `BattleUnit.cpp` (`updateEnemyKnowledge`, `setTurnsSinceSeen`,
`setTileLastSpotted`), `BattlescapeGame.cpp` (`getReservedAction`, `getKneelReserved`).

- **`BattleState.UnitOpensDoor(rules, unit, rightClick, direction, out BattleDoorAttempt)`** (`BattleDoors.cs`, internal) is `unitOpensDoor`
  (`direction` -1 is the unit's own; the right-click form is what `TurnUnit` runs, the walking form with `rightClick: false` is for S8's walk).
  It searches the footprint's tiles (from the tile above on stairs: terrain level below -12) in the reference's order through `DoorChecks`
  (the table, with `rClick` adding the tiles beside the diagonals), takes the first part whose `openDoor` answers anything but -1, opens it,
  works out the cost, checks `CheckReservedTimeUnits`, pays and updates light (`CalculateLighting(Fire, centre, doorsOpened, terrainChanged)`)
  and sight (`CalculateFov(centre, doorsOpened, true, true)`) through the doorway and the unit's `UpdateEnemyKnowledge`.
- **`openDoor`.** A private `OpenDoor(rules, tile, part, unit, rightClick, apply)` is `Tile::openDoor`: the cost is the part's own movement
  cost for the unit's movement type plus the kneel reserve for a standing unit whose armor allows kneeling, added to the reserved action's
  cost for the main-hand weapon (`BattleActionCost`), plus half as much energy when walking; a big unit gets -1 at a swinging door; result 4
  when the cost (when non-zero) cannot be paid; -1 when the unit the tile holds is not the opener and stands elsewhere than the tile's
  position (so a big unit's *other* tiles keep a door shut, and a unit on the door's own tile does not, as the reference tests it); a swinging
  door moves to its alternative object, which goes to the part its own object type names and then empties the door's part (a door whose
  alternative has the door's own part ends empty, as in the reference's two calls; a test pins it); a closed UFO door's frame is set (open).
  Probing (`apply: false`) changes nothing, so the search can finish before the door opens.
- **Results.** `BattleDoorResult` (public): `None` (-1), `Opened` (0), `UfoDoorOpened` (1), `NotEnoughTimeUnits` (4, from `openDoor` or from a spend
  that fails after the door opened, as the reference) and `ReservedTimeUnits` (5, the door is open and nothing was paid). The reference's result 3
  (a UFO door still opening) cannot happen: the animation is collapsed and a UFO door is open at once. A swinging door opened by right click
  costs the *other* wall's part of its tile (the open door), so a door that moves into the other wall part costs that part; the walking form of a
  swinging door costs nothing here (the step pays) and a UFO door's cost is read with the door still closed, as the reference reads it at frame 1.
- **Adjacent doors.** `CheckAdjacentDoors` opens the run of UFO door parts of the same part (along y for a west wall, along x for a north
  wall) either side, counts the ones that actually opened (an open one is passed over and not counted), and the centre is the start moved by
  half the net offset (integer division towards zero).
- **Command.** `TurnUnit` runs the door when the unit does not turn: `BattleTurnResult` and `UnitTurned` carry `Door`, `DoorsOpened` and
  `DoorCentre`, and `TimeUnitsSpent` is the door's cost. The revision rises when the state changed (result 5 and a failed spend change it, result
  4 from the cost check does not). `battle.door-unsupported` is gone. The hostile side's reserve stays `battle.hostile-reserve-unsupported`: it
  is refused before the door opens, because the reference opens the door before it asks. The AI's `checkForReactivation` after a right click is branch 6's.
- **`CloseUfoDoors`.** `BattleState.CloseUfoDoors()` (internal, for branch 6's end of turn) is `closeUfoDoors`: every open UFO door closes except
  on a big unit's tile whose open north wall has the same unit's tile north of it, or open west wall has it to the west; it returns the number of
  tiles closed (a tile counts once). It recomputes nothing: the caller refreshes light and sight (`CalculateLighting(..., terrainChanged: true)`,
  `RecalculateFov`), as the reference's end of turn does, and there is no visibility cache to reset.
- **Fix to branch 3 S4 (inventory).** `BattleUnit.UpdateEnemyKnowledge` (written for the inventory drop) wrote `TurnsSinceSpotted` where the reference sets `turnsSinceSeen`; it now sets
  `TurnsSinceSeenBy` and takes the `door` flag (a door's knowledge leaves the player's side's entries alone). The inventory drop's behaviour changes with it, and `BattleInventoryTests` now asserts `TurnsSinceSeenBy`.
- **Not ported.** The sounds, `Tile::animate`'s door frames, the AI's visible-tile count check, and the reference's out-of-range alternative object
  (a door whose alternative lies past its data set is not opened and the search goes on, where the reference throws; ADR 0040).
- **Tests.** `BattleDoorTests` (26, compatibility project): a swinging door opened by a unit that faces it and the cost of its open part; the
  tiles a direction looks at, in both forms; a door of a part the direction does not look at; a turn that really turns; a door moving to its own
  part; an alternative past the data set; a unit elsewhere than on the tile, a big unit's cover; a big unit with swinging and UFO doors; stairs; a UFO
  door row with its centre, count, sight through the doorway and the enemy knowledge; an open door in the run; a north wall run and its refreshed
  blockage cache; another direction; not enough time units, the reserve (result 4 and result 5), a failed spend, the kneel reserve; the walking form's
  energy and costs; the hostile side's refusal; `CloseUfoDoors` with a big unit and with a small one.

## S7 — Gravity and falling (2026-11-07)

Reference: `TileEngine.cpp` (`applyGravity`, `itemDrop`), `UnitFallBState.cpp`, `SavedBattleGame.cpp` (`addFallingUnit`, `getFallingUnits`), `BattleUnit.cpp`
(`startWalking`, `keepWalking`, `setPosition`, `setTile`, `knockOut`), `BattlescapeGame.cpp` (`spawnNewUnit`, `spawnNewItem`, `checkForProximityGrenades`,
the fall state's push at `think`), `InventoryState.cpp` (the destructor), `BattleItem.cpp` (`fuseProximityEvent`, `getGlow`), `TileEngine::checkReactionFire`.

- **`ApplyGravity(tile)`** (`BattleGravity.cs`) is `applyGravity`: the unit on the tile has its floor state updated; with no floor and not over the out
  threshold, a flying unit floats where it is (`startWalking` onto its own position, then `abortTurn`: floating, not kneeling, last position set) and any
  other joins `FallingUnits` (`addFallingUnit`: the front of the list, once; runtime state, not saved). The tile's items go to the lowest tile below with a
  floor (only above level 0), taking along the unit of a corpse item whose unit position is the tile (the unit itself is not queued by that). It returns the
  tile the items end on, as the reference does.
- **`ResolveFalls(rules)`** is `UnitFallBState` to its end, in rounds (ADR 0040): units over the out threshold leave the list; a unit with no floor, above
  level 0, not flying, adds its fall tiles (the whole fall keeps them) and the units below them that are not falling to `unitsToMove`, then steps
  down (`WalkStep` with `DirectionDown`); a unit that does not fall lands. The units to move look for an escape in the compass order: the step must be a valid
  `GetTuCost`, and every section of the unit must reach a tile in the map that no unit stands on (but itself), no escape takes, no fall reaches, and
  that has a floor (a flying unit does not need one); the unit then steps there (turning to that direction) and joins the falling list, and its tiles are
  reserved (from the last section's tile, bug for bug, ADR 0040). With none the reference knocks the unit out and `checkForCasualties` follows.
- **Landing.** (The trial copies of the first turn run it for real, so the reactor test runs on both paths.) A unit that stopped falling: a floor-burning unit (`specab` 2 or 3, the armor's, else the unit rule's, as `BattleUnit`'s constructor takes it) refuses (S9, `battle.burn-floor-unsupported`); a proximity event refuses
  (`battle.proximity-unsupported`, below); then the units' light with radius 2 at its tile (`CalculateLighting(Units)`, as `checkForProximityGrenades`
  returned no change), `CalculateFov(position, 2, false)` for everyone around, the unit's own `CalculateFov(unit, true, false)`, and for a unit of the side on
  turn `SpotMover` (`checkReactionFire`'s spotting) after a `HasPossibleReactor` check (`battle.reaction-fire-unsupported`).
- **Refusals before any change.** `FallsRefusal(rules, extra)` runs the whole fall as a trial on the live state and puts back the units' fields, the map's unit
  grid, the falling list and the revision (`FallSnapshot`): no escape for a unit below is `battle.fall-knockout-unsupported` (new: the stomp's knock-out, branch 6); `battle.burn-floor-unsupported`;
  `battle.reaction-fire-unsupported`; `battle.proximity-unsupported`: an item with a `proximityTrigger` fuse, or a lit one (enabled and timer 0 or more)
  that is a proximity grenade or has `proximityExplode`, lies on a tile within one of the unit's tiles, or a floor special type of 200 or more under
  the unit names an item rule `STR_DEATH_TRAP_<n>` (the over-approximation: chance rolls and `isTargetAllowed` are not asked; `RuntimeFuseTrigger` now carries
  `ProximityExplode`). The reference's `isPreview` shortcut is not ported (no preview battles).
- **Callers.** The spawns: `SpawnNewUnit` and `SpawnNewItem` run `ApplyGravity` on the new unit's or item's tile (`BattlescapeGame.cpp:2382, 2445`);
  the fall waits for the first turn because the units of the craft have no tiles before it. `BattleState.StartFirstTurn` (now returns the refusal) calls
  `RequeueFallingUnits` (the battlescape's constructor queues every unit that does not fly and has no floor through `checkForCasualties`, BattlescapeGame.cpp:194 and 1026-1030, before the spawns queue theirs; a save does not keep the list, so the units are queued again in unit order, each at the front). A loaded running battle with such a unit refuses its commands (`battle.casualties-unsupported`, the command gate's `HasFloorlessWalker`), since the port queues at the first turn only, `ApplyGravity` on the selected unit's tile (the inventory's destructor), the lighting and sight it ran, then `ResolveFalls`.
  The campaign runs the first turn on a copy of the battle first (`TrialFirstTurnRefusal`, and inside `TrialSpawnRefusal` for the briefing, which therefore
  refuses a spawn whose fall cannot run), but only when `MayFall()` says a unit has no floor; the copy puts the campaign's random stream back. Without terrain
  files there is no copy and a late refusal leaves the first turn half done (`StartFirstTurn` still returns the reason). In a running battle each inventory move
  runs the destructor's part after the move: `FallsRefusal` first (refused before the move), then `ApplyGravity` on the selected unit's tile, the items' light,
  everyone's sight and `ResolveFalls`.
- **Retired.** `battle.spawn-gravity-unsupported`, `BattleState.SpawnNeedsGravity` and `FallsAt` are gone; the briefing refuses a spawn only for what its fall would
  hit above. `BattleMap.CaptureUnits` and `RestoreUnits` copy the unit grid for the trial.
- **Not ported.** `itemDrop` (a dropped item's gravity, light and sight: throws and drops are branch 5's) and the callers `UnitDieBState`,
  `SavedBattleGame::prepareNewTurn` (fire burning floors, S9), `TileEngine::hit`'s (TileEngine.cpp:3749) and `explode`'s (:3975) gravity (S9, branch 5), `SavedBattleGame::convertUnit`'s (SavedBattleGame.cpp:2192, branch 6), the other `checkForCasualties` queueing of floorless units (BattlescapeGame.cpp:1030, after explosions and the like, branch 5 and 6); the unit knock-out of a stomp and its casualty check
  (branch 6); `Pathfinding::abortPath` after a burnt floor (S9, S8's walk); `UnitWalkBState`'s own push of `UnitFallBState` (S8).
- **Tests.** `BattleGravityTests` (compatibility, 23): a unit falling three levels (position, last position, motion points, the grid), a unit with a floor,
  nothing on a tile, a flier hovering and standing up, an unconscious unit's body dropping with the tile's items, items over holes and on level 0, a landing on
  a flier (it escapes north and turns), a walker below taking north east when north has no floor, a big unit falling onto four tiles and the escape avoiding
  them, a big unit with a floor under one tile, a fall with no escape refused with the state untouched and then run with a side opened, an armed reactor refused
  and an unarmed one spotting, a landing of the side not on turn, a floor-burning unit, proximity (a lit proximity-explode fuse, a plain lit grenade, a
  `proximityTrigger` item, out of reach), the requeue order, a turning, panicking or berserk faller, a queued unit past the out threshold, a flier escaping onto a floorless tile, a big unit below moving aside, a lit proximity grenade, a unit rule's `specab`, the reactor refusal from the real run. Not tested: the `alreadyTaken` conflict between two escapes, the death-trap branch, the round limit. `BattleFirstTurnTests`: a spawn on a floorless tile queued and landed at the first turn, a spawned item
  dropping, an alien in the air falling at the first turn and a refused first turn (burn floor) that changes nothing. `BattleInventoryTests`: an item put
  down by a hovering unit drops; a floorless walker's commands are refused by the gate (so `Moved`'s own pre-move refusal can only matter once S8 can leave a walker queued) and its fall runs. The briefing refuses a spawn that could not land. (The spawn tests that asked for `SpawnNeedsGravity` are replaced.)

## S8 — Walking and flying (2026-11-07)

Reference: `UnitWalkBState.cpp` (`init`, `think`, `postPathProcedures`), `BattlescapeGame.cpp` (`primaryAction`'s walk, `moveUpDown`, `kneel`, `checkReservedTU`),
`BattlescapeState.cpp` (`btnUnitUpClick`, `btnUnitDownClick`), `BattleUnit.cpp` (`startWalking`, `keepWalking`, `spendEnergy`), `Armor.cpp` (`allowsRunning`,
`allowsStrafing`, `allowsSneaking`, `allowsMoving`), `Tile.cpp` (`getOverlappingUnit`), `Pathfinding.cpp` (the S2 port: `calculate`, `getTUCost`, `validateUpDown`).

- **Commands.** `WalkUnit(UnitId, Target, Move, IgnoreSpottedEnemies)` and `MoveUnitUpDown(UnitId, Up)` (`BattleMovementContracts.cs`; both `IBattleCommand` and
  `IUnitCommand`, registered in `CampaignBattles`). `BattleMoveType` (public: normal, run, strafe, sneak) maps to the pathfinding's `BattleActionMove`; the
  player's client sends normal. `BattleState.Walk` (`BattleWalk.cs`) is `primaryAction`'s walk: the unit of the side on turn, a target in the map, the armor's
  `allowsMoving` (`battle.walk-not-allowed`), `allowsRunning`, `allowsStrafing` or `allowsSneaking` for the move type (`Armor::allowsRunning(def)`: the armor's
  value, else true for a small unit; `battle.walk-move-not-allowed`), then `Calculate` for the move type (no path: `battle.walk-no-path`, also a unit already on the
  target) and `RunWalk`. `ignoreSpottedEnemies` is the command's flag or a run with the `extendedRunningCost` constant. The path is calculated before a kneeling unit stands up, as the reference does. The selection is not touched.
- **`MoveUpDown`** is the buttons' `validateUpDown` (`battle.up-down-impossible` when it fails or the tile a level above or below is outside the map) then
  `moveUpDown`: a kneeling unit stands up first (the kneel's own refusals pass through, nothing having changed), the path to the tile a level above or below is
  calculated with the normal move type and `RunWalk` follows. A kneeling unit stands up and pays first, then an armor that does not allow moving ends the walk with no step (the reference's `think` aborts silently
  after `moveUpDown`'s kneel); with no kneel to do, that is the refusal `battle.walk-not-allowed`. The move type is always normal and Shift is off (the reference carries the last
  `primaryAction`'s flags into `moveUpDown`; ADR 0040). A walk with no path at all (`dir == -1`) ends in `postPathProcedures` only: its light and sight updates run and nothing moves.
- **`RunWalk`** is `UnitWalkBState` as one loop whose cycles are the reference's `think` calls (the animation phases collapsed into `WalkStep`, so a step started in one
  cycle is finished at the start of the next, followed in that same cycle by the next step's start, as in the reference where the "step finished" block falls into the
  standing block). `init` sets `beforeFirstStep` (not strafing, a start direction, not the unit's facing). A cycle: a kneeling unit stands up through `KneelUnit` (the kneel's refusals refuse
  the walk; its reaction refusal likewise); an out unit stops; after a step the position block (`fallingWhenStopped`, `falling` with the ladder "jump" exception for a
  vertical step from a ladder with a path left, and the fall onto a unit below: the path loses its next step, the unit joins `FallingUnits` and `ResolveFalls` runs, as
  `UnitFallBState` pushed in front does) and the finished-step block (a burning floor is S9's, `battle.burn-floor-unsupported`; the other factions' spotting of the mover is cleared (plan D2, any mover); a proximity event refuses; `CalculateLighting(Units, position, 2)` and `CalculateFov(position, 2, false)`; a newly spotted unit cancels unless the walk
  ignores them or falls; and when the unit is not falling the `HasPossibleReactor` refusal and `SpotMover`, `checkReactionFire`'s spotting, as `Land` does); then the
  standing block: no direction left is `postPathProcedures` (`CalculateLighting(Units, position)`, `CalculateFov(unit)`); otherwise `GetTuCost` for the action's move
  type (a falling unit goes down), then in the reference's order the invalid step, time units, energy, `CheckReservedTimeUnits` (not while falling), the free turn towards the step (not for a strafe or a
  vertical step), `UnitOpensDoor` (walking form) with a UFO door ending the cycle (the walk thinks again at once: ADR 0040), the unit in the way (`Tile::getOverlappingUnit` with
  `TUO_IGNORE_SMALL` on each tile of the footprint at the step's destination, `BattleSight.OverlappingUnit(tile, 26)`), the dequeue, and the spend of time units and then energy
  and `WalkStep`; and the turning block: before the first step an armor with `turnBeforeFirstStep` pays the turn cost for each eighth (`spendTimeUnits` that fails does not stop
  the turn, as the reference), another armor counts the eighths and pays them (`preMovementCost * turnCost`) only when a sighting stops the turn; `turn`, `CalculateFov(unit)` and a new sighting abort the turn
  and cancel. `cancelCurentMove` keeps a unit that stopped without a floor falling first (a further cycle), otherwise it drops the path and pops.
  `BattleUnit.SpendEnergy` is `spendEnergy`.
- **Result.** Ordered events: `UnitKneeled` (standing up), `UnitWalkTurned` (one per eighth with the time units it cost), `UnitWalkOpenedDoor`, `UnitStepped` (from, to, direction 0-7, 8 up, 9 down,
  time units and energy), `UnitFell` (a unit moved by gravity during the walk: the walker falling onto another, and the unit that moved aside; positions only), `UnitsSpotted` (the IDs
  the stop saw) and last `UnitWalkEnded` (`BattleWalkStop`: arrived, spotted units, not enough time units, not enough energy, reserved time units, path blocked, unit in the way,
  incapacitated; steps, the walk's time units and energy in all, the position). A walk that stops before it changed anything (nothing paid, turned, opened or moved) is a
  refusal instead, with the stop's reason (`battle.walk-blocked`, `battle.walk-not-enough-time-units`, `battle.walk-not-enough-energy`, `battle.walk-time-units-reserved`,
  `battle.walk-unit-in-the-way`), and no revision change: this is the nearest to the reference's silent abort.
- **Refusals before any change (the trial copy).** What the reference hands to later work is met in the middle of a walk (after a step), so the command runs the walk
  on a copy of the battle first (`CampaignBattles.TrialRefusal`: `Capture` and `Restore` with the attached terrain files, as the first turn's trial) and only then
  on the battle: the reaction-fire refusal after a step (`battle.reaction-fire-unsupported`, `HasPossibleReactor` with the over-approximation of S5), a proximity event
  (`battle.proximity-unsupported`), a floor-burning unit (`battle.burn-floor-unsupported`, S9), a fall onto a unit that cannot move aside
  (`battle.fall-knockout-unsupported`, branch 6) and the hostile side's reserve. Without terrain files no copy is made and a late refusal leaves the walk half done (the method
  returns the reason). `Restore` recomputes the sight a save does not keep, so `TrialRefusal` puts the live battle's `UnitsSpottedThisTurn` and the three spotting flags of every unit into the copy;
  the walk's stops and paths are then the live walk's. An undefined move type is refused as `battle.walk-move-not-allowed`.
- **Not ported.** The AI parts of `think` and `postPathProcedures`: `checkForReactivation` of the side's brutal units on a sighting, the brutal AI's stop after a UFO door,
  `_action.desperate`, charging, hiding, `finalFacing`, `finalAction`, `dontReselect`, melee at the end of a charge, `clearTimeUnits` for an unhandled panic (all branch 6;
  the player's walk never has them); the camera, the sounds (`playMovementSound`, so `selectMoveSoundUnit` changes no state, confirmed: the hook is not read), `isPreview`'s refill;
  `BattlescapeGame`'s `getPanicHandled` is always true for a player's command. The burn-floor `ignite` and the `hit` it makes (S9).
- **Tests.** `BattleWalkTests` (compatibility, 36, on the sight arena four levels high with a walking soldier): a straight walk (events, costs, motion points, the grid), a diagonal
  step with its free turn, the turn before the first step paid and the free turn between steps, no `turnBeforeFirstStep` (charged only on a sighting stop), a stop after the
  step that shows a new unit and the Shift walk, a stop on a sighting by the first turn (the turn paid), run costs (the default and a custom percentage), a refused run, the extended running cost, a strafe step,
  a refused sneak, TU shortage (a stop and a refusal), energy shortage, the reserve, a kneeling unit standing up (and its refusal), a refused armor, no path, targets outside and units of another side, a
  known and an unknown unit in the way, a swinging door, a UFO door (and the refusal when the door and step cannot be paid), a ramp up a level, a ladder with the jump onto the ledge, flying up and down,
  a flier over a hole, a walk off a ledge, a fall onto a unit that moves aside and the knock-out refusal (called on the battle itself), the reaction, burn-floor and proximity refusals with the state compared before and
  after, the hostile faction's spotting by a weaponless unit, the personal light, and the selection kept. Also: an undefined move type, the flag clearing, a kneeling unit with the up and down buttons (and with an armor that cannot move). Also: the kneel's reaction refusal inside a walk (called on the battle, state unchanged), a unit poking up from below a hole (a first-step refusal and a stop after one step), the `proximityTrigger` and death-trap cases of the proximity refusal,
  the light that moves with the walker (the two tiles trade shades) and the sight that grows with it (a stop on a unit seen in the dark). Not reachable, so not tested: the hostile reserve refusal (the selection rules refuse every command of the hostile side first, `battle.selection-unavailable`), `MoveUpDown` with no path
  (`validateUpDown` refuses a tile above that holds a unit and no arena that passes it lacks a path), the `Incapacitated` stop (nothing in a player's walk puts the walker out: damage is branch 5's). Not tested: a big unit's walk, the ladder jump cancelled midway (`falling` set by `cancelCurentMove`), the AI's stops.

## Next

S9 — fire, smoke and terrain destruction (plan row S9). Notes for its worker:

- **What S8 left for it.** The walk's finished-step block refuses a floor-burning mover (`BattleWalk.cs`, `battle.burn-floor-unsupported`, tested by `BattleWalkTests.AUnitThatBurnsTheFloor...`):
  the reference there calls `_unit->getTile()->ignite(1)` and `TileEngine::hit(..., DT_IN, false)` on the unit's voxel, then, when the unit's status is not standing (a hole was burnt and it fell),
  `abortPath` and return. S9 retires the refusal in both places (the walk's, and `Land`'s in `BattleGravity.cs`); `RunWalk`'s `Think` is where the call goes (after the
  `falling` test, before the faction flag clearing), and the burnt-through case must end the walk (`pathfinder.AbortPath()`, stop) with the fall running in `ResolveFalls`.
  `FallsRefusal` and `TrialRefusal` stay the way to keep refusals before any change.
- **Walk facts S9's and later tests need.** `BattleState.Walk(rules, unitId, target, move, ignore, events)` and `MoveUpDown(...)` are the rule methods; the commands run them on a trial
  copy first (`CampaignBattles.TrialRefusal`, not available without terrain files). A walk is `RunWalk`'s loop; the finished-step block's order is burn floor, spotting flag clearing for a non-player mover, `ProximityEffects`,
  `CalculateLighting(Units, position, 2)`, `CalculateFov(position, 2, false)`, the spotted test, then `HasPossibleReactor` and `SpotMover`. `BattleWalkTests.Make` builds the walking soldier on the four-level arena
  (`suitExtra` and `rulesExtra` add armor and global rules; `flying: true` keeps the flier); `Walk`, `End` and `State` are its helpers. The ramp records `RampLow` (terrain level -8) and `RampHigh` (-16) join the sight
  tests' terrain; a ladder is `GravLift` on the object part. A wall object (`Wall`) blocks sight but not movement; an impassable tile needs a movement cost of 255 in a new record.
  A known unit stands in the end tile (`arena.See()` after `Stand`) for "no path".
- **Landing refusals are over-approximations** (`battle.proximity-unsupported`, `battle.reaction-fire-unsupported`, `battle.burn-floor-unsupported`), and the walk meets the same ones per step.
- **Not done and needed by later slices.** `itemDrop` (branch 5), the stomp's knock-out and `checkForCasualties` (branch 6), `UnitFallBState`'s burn-floor branch (S9);
  `canUseWeapon`'s research, AI use delay, depth and two-handed rules, `getWeaponForReactions`' preferred hand and `reactionsDisabled*` (branch 5, with the real reaction);
  `BattleUnit.HitState` is never set (damage is branch 5's) and no selection resets it (`resetUnitHitStates`); the legal-action list does not list turn, kneel, walk or up and down (S10);
  the AI's walk parts (S8's "Not ported") and `checkForReactivation` (branch 6).
- **S6's notes still apply.** `BattleState.UnitOpensDoor(rules, unit, rightClick: false, direction, out attempt)` is the walking form (results 0, 1, 4, 5; a UFO door is open at once); `CloseUfoDoors()` is
  ready for branch 6; `SpotMover` and `HasPossibleReactor` are the spotting and refusal after a move, called only for the mover of the side on turn (`PassesSpottingFilters` reads `Side`).
- **Test helpers.** `BattleGravityTests.Tall` builds the sight tests' `Flat` arena four levels high (a walking soldier, an armor `specab`, a reactive alien, a big alien and the grenade's fuse events
  by parameter), `Raise` puts a unit on a tile, `State` is a string of the units' fields, the map's grid, the revision and the falling list to compare a refused trial. `BattleTurnKneelTests.Make`
  builds the arena with a turn cost of 2, `tuSnap: 20`, `tuAimed: 50` and an alien with reactions 100; commands run through `arena.Campaign.Execute`; an unfinished casualty (health 0) refuses every command
  (`battle.casualties-unsupported`), so test a dying unit through state queries. A unit that stands over a floorless tile at a level above 0 makes the gate refuse commands: put its floor first, then `SetTile`.
- **Gotchas.** A post-edit hook reformats edited C# files, so read a file again before a second edit. A backslash-n written through a script or heredoc reaches a C# file
  as a real newline: add string literals that carry one with the Edit or Write tool (an apostrophe inside a heredoc can also break the shell call: write files with the Write tool; a Python script
  that needs a backslash-n builds it with `chr(92) + 'n'`). A unit that still has its `InventoryTile` has no tile of its own and `StartFirstTurn` is the first sight (and `SetTile` throws for it), so a fall before the first turn is the
  first turn's; tests that add `BattleUnit`s by hand must give them a `SoldierId` or a unit rule, since heights are read, and a battle with such a unit cannot be restored for the trial
  copies (`MayFall()` keeps the first turn's copy from being made unless a unit has no floor; a walk's copy is made for every walk, so walk tests use the arenas' generated units). The reload comparisons go through `BattleSaveUnitTests.SightFree`. The armor key for the
  special ability is `specab`; an armor's run, strafe and walk costs are `moveCost: {runPercent: [time, energy]}` (a step on the arena's floor costs 4 × percent / 100: normal walk 100/50, run 75/75).
- Later slices (for planning ahead): S9 reads the smoke threshold from
  `RuntimeCampaignSettings`, and its terrain changes call `RefreshBlockageCache` (via `CalculateLighting(..., terrainChanged: true)`) and then
  `CalculateFov(position, radius, true, appendToTileVisibility: true)`.
- Gotchas carried from branch 3 (see `docs/battle-session-ui-status.md` "## Next"): write test files with LF; the test runner is Microsoft
  Testing Platform (`dotnet test --project <proj> --filter-class <full name>`); battle test helpers (`BattleStartTests.Requested`,
  `Started`, `Battle`, `BattleSaveRootTests.Generated`, `BattleSaveUnitTests.Loaded`) are internal for reuse;
  `BattleCampaignSaveTests.AtFirstTurn` is private; a campaign snapshot with a battle restores only with terrain files; raw string
  literals in test rules have their indentation stripped (list entries start at two spaces).
