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
| S1 movement groundwork: constants, action costs, reserved TUs, unit primitives | Done and audited (2026-11-01) |
| S2 pathfinding | Done and audited (2026-11-01) |
| S3 lighting | Done and audited (2026-11-01) |
| S4 field of view and per-faction visibility | Done and audited (2026-11-01) |
| S5 turning and kneeling | Done and audited (2026-11-07) |
| S6 doors | Done and audited (2026-11-07) |
| S7 gravity and falling | Done and audited (2026-11-07) |
| S8 walking and flying | Done and audited (2026-11-07) |
| S9 fire, smoke and terrain destruction | Done and audited (2026-11-07) |
| S10 battlescape client | Done and audited (2026-11-07) |
| S11 acceptance | Done and audited (2026-11-08) |
| Branch audit | Fixes done and audited, awaiting merge (2026-11-08) |
| Deduplication audit | Done (2026-11-08) |

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
  (reaction fire, proximity, a hit that reaches a unit or an explosion (S9), a fall with no escape), so every completed command matches the
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
  `CampaignBattles`). A load accepts such a battle but skips its field of view recalculation (`BattleStatePersistence`: the saved spotting and discovery stay). The private corpus's Rosigma mod binds
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
- **Landing.** (The trial copies of the first turn run it for real, so the reactor test runs on both paths.) A unit that stopped falling: a floor-burning unit (`specab` 2 or 3, the armor's, else the unit rule's, as `BattleUnit`'s constructor takes it) burns the floor under it (S9 retired `battle.burn-floor-unsupported`); a proximity event refuses
  (`battle.proximity-unsupported`, below); then the units' light with radius 2 at its tile (`CalculateLighting(Units)`, as `checkForProximityGrenades`
  returned no change), `CalculateFov(position, 2, false)` for everyone around, the unit's own `CalculateFov(unit, true, false)`, and for a unit of the side on
  turn `SpotMover` (`checkReactionFire`'s spotting) after a `HasPossibleReactor` check (`battle.reaction-fire-unsupported`).
- **Refusals before any change.** `FallsRefusal(rules, extra)` runs the whole fall as a trial on the live state and puts back the units' fields, the map's unit
  grid, the falling list and the revision (`FallSnapshot`): no escape for a unit below is `battle.fall-knockout-unsupported` (new: the stomp's knock-out, branch 6);
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
  `proximityTrigger` item, out of reach), the requeue order, a turning, panicking or berserk faller, a queued unit past the out threshold, a flier escaping onto a floorless tile, a big unit below moving aside, a lit proximity grenade, a unit rule's `specab`, the reactor refusal from the real run. Not tested: the round limit (an invariant guard, unreachable). The `alreadyTaken` conflict between two escapes and the death-trap branch were tested by the branch audit's fixes and S8's audit. `BattleFirstTurnTests`: a spawn on a floorless tile queued and landed at the first turn, a spawned item
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
  `UnitFallBState` pushed in front does) and the finished-step block (the floor burns (S9); the other factions' spotting of the mover is cleared (plan D2, any mover); a proximity event refuses; `CalculateLighting(Units, position, 2)` and `CalculateFov(position, 2, false)`; a newly spotted unit cancels unless the walk
  ignores them or falls; and when the unit is not falling the `HasPossibleReactor` refusal and `SpotMover`, `checkReactionFire`'s spotting, as `Land` does); then the
  standing block: no direction left is `postPathProcedures` (`CalculateLighting(Units, position)`, `CalculateFov(unit)`); otherwise `GetTuCost` for the action's move
  type (a falling unit goes down), then in the reference's order the invalid step, time units, energy, `CheckReservedTimeUnits` (not while falling), the free turn towards the step (not for a strafe or a
  vertical step), `UnitOpensDoor` (walking form) with a UFO door ending the cycle (the walk thinks again at once: ADR 0040), the unit in the way (`Tile::getOverlappingUnit` with
  `TUO_IGNORE_SMALL` on each tile of the footprint at the step's destination, `BattleState.OverlappingUnit(rules, tile, OverlapIgnoreSmall)`), the dequeue, and the spend of time units and then energy
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
  (`battle.proximity-unsupported`), a fall onto a unit that cannot move aside
  (`battle.fall-knockout-unsupported`, branch 6) and the hostile side's reserve. Without terrain files no copy is made and a late refusal leaves the walk half done (the method
  returns the reason). `Restore` recomputes the sight a save does not keep, so `TrialRefusal` puts the live battle's `UnitsSpottedThisTurn` and the three spotting flags of every unit into the copy;
  the walk's stops and paths are then the live walk's. An undefined move type is refused as `battle.walk-move-not-allowed`.
- **Not ported.** The AI parts of `think` and `postPathProcedures`: `checkForReactivation` of the side's brutal units on a sighting, the brutal AI's stop after a UFO door,
  `_action.desperate`, charging, hiding, `finalFacing`, `finalAction`, `dontReselect`, melee at the end of a charge, `clearTimeUnits` for an unhandled panic (all branch 6;
  the player's walk never has them); the camera, the sounds (`playMovementSound`, so `selectMoveSoundUnit` changes no state, confirmed: the hook is not read), `isPreview`'s refill;
  `BattlescapeGame`'s `getPanicHandled` is always true for a player's command. (The burn-floor `ignite` and `hit` are S9's, done.)
- **Tests.** `BattleWalkTests` (compatibility, 36, on the sight arena four levels high with a walking soldier): a straight walk (events, costs, motion points, the grid), a diagonal
  step with its free turn, the turn before the first step paid and the free turn between steps, no `turnBeforeFirstStep` (charged only on a sighting stop), a stop after the
  step that shows a new unit and the Shift walk, a stop on a sighting by the first turn (the turn paid), run costs (the default and a custom percentage), a refused run, the extended running cost, a strafe step,
  a refused sneak, TU shortage (a stop and a refusal), energy shortage, the reserve, a kneeling unit standing up (and its refusal), a refused armor, no path, targets outside and units of another side, a
  known and an unknown unit in the way, a swinging door, a UFO door (and the refusal when the door and step cannot be paid), a ramp up a level, a ladder with the jump onto the ledge, flying up and down,
  a flier over a hole, a walk off a ledge, a fall onto a unit that moves aside and the knock-out refusal (called on the battle itself), the reaction and proximity refusals (and S9's burn-floor refusal, since retired) with the state compared before and
  after, the hostile faction's spotting by a weaponless unit, the personal light, and the selection kept. Also: an undefined move type, the flag clearing, a kneeling unit with the up and down buttons (and with an armor that cannot move). Also: the kneel's reaction refusal inside a walk (called on the battle, state unchanged), a unit poking up from below a hole (a first-step refusal and a stop after one step), the `proximityTrigger` and death-trap cases of the proximity refusal,
  the light that moves with the walker (the two tiles trade shades) and the sight that grows with it (a stop on a unit seen in the dark). Not reachable, so not tested: the hostile reserve refusal (the selection rules refuse every command of the hostile side first, `battle.selection-unavailable`), the `Incapacitated` stop (nothing in a player's walk puts the walker out: damage is branch 5's). Not tested: a big unit's walk, the ladder jump cancelled midway (`falling` set by `cancelCurentMove`), the AI's stops.

## S9 — Fire, smoke and terrain destruction (2026-11-07)

Reference: `Tile.cpp` (`ignite`, `addSmoke`, `setFire`, `setSmoke`, `destroy`, `damage`, `setExplosive`, `getFlammability`, `getFuel`, `prepareNewTurn`),
`TileEngine.cpp` (`hit`, `hitTile`, `applyGravity`), `SavedBattleGame.cpp` (`prepareNewTurn`, `addDestroyedObjective`, `allObjectivesDestroyed`),
`UnitWalkBState.cpp` (the finished-step block), `UnitFallBState.cpp` (the landing), `RuleDamageType.cpp` and `Mod.cpp` (the base `DT_IN`), `MapDataSet.cpp`
(`getScorchedEarthTile`), `BattlescapeGame.cpp` (`think`'s push of the fall state), `Options` (`battleAutoEnd`).

- **Tile state.** `BattleMap` keeps three more runtime arrays no save holds (as the reference): `Overlaps` (`Tile::_overlaps`, a `Uint8`), `Explosive` and
  `ExplosiveType` (`setExplosive`'s moving maximum). `BattleFire.cs` (`BattleState`): `Flammability` (lowest of the parts, 255 for none) and `Fuel` (highest),
  `Ignite(tile, power)` (chance `power - flammability / 10 + 15` percent, the roll drawn first, then fuel; a burning tile keeps its fire; fire
  is fuel + 1 as a `Uint8`, so a fuel of 255 wraps to 0; the smoke is `15 - clamp(flammability / 10, 1, 12)`, the overlap 1), `AddSmoke` (kept in 1 to 15 until a second smoke
  arrives the same turn, then summed as a `Uint8`; none on a burning tile; the overlap counts), `SetTileFire` and `SetTileSmoke` (clamped to 0-255), `TileIsVoid`.
  The sprite animation offsets the reference draws with every fire and smoke change are presentation and draw nothing.
- **Destroying parts.** `DestroyPart` is `Tile::destroy`: the part is cleared, replaced by the object's `dieMCD` form (same data set, put in the dead object's own part), a part with
  an explosive strength marks the tile (`SetExplosive`), a grav lift is never destroyed, and a ground-level floor that is gone becomes scorched earth (data set 0, object 1, as the generator's fill
  does). It returns whether the part was the mission's objective; `DamagePart` is `Tile::damage` (the power reaches the armor). `AddDestroyedObjective` counts one (never past the
  needed count). Deterministic choices where the reference throws or dereferences null (ADR 0040): a `dieMCD` past the data set leaves the part cleared, and a first data set without a second object leaves
  a destroyed ground floor empty (the port takes scorched earth from set 0, the reference BLANKS' tile; these are the same with BLANKS first); the other S9 choices are in ADR 0040 too. The completion popup (`missionComplete`) is presentation, and `autoEndBattle` does nothing at the default `battleAutoEnd` (off), so only the count changes.
- **`HitTerrain`** is the tile part of `TileEngine::hit` for a hit with no weapon (branch 5 adds the weapon, the unit hits and the explosions to the same code): the voxel check (the attacker excluded)
  decides; a floor, wall or object runs `HitTile` (smoke at the damage type's smoke threshold replaces fire and needs less than `tooMuchSmokeThreshold` smoke and a terrain level above -24; fire
  needs a tile that is not void, has a floor or an object and no fire: fuel + 1 turns at twice the threshold, less below), the base defense module count (an object of an X-COM module whose armor the
  damage reaches) and `DamagePart` with the objective count; a unit in the voxel or a live body on the tile of a floor or object hit is `battle.hit-unit-unsupported` (branch 5), a destroyed part with an
  explosive strength `battle.terrain-explosion-unsupported` (branch 5) and, for a base module in a block a restored map lacks, `battle.module-map-unsupported`, all before any change (a dry run asks only that: it draws nothing). When the terrain changed or fire or smoke appeared: gravity on the tile, light of the ambient layer
  (a destroyed floor above the ground) or the fire layer around it with the blockage cache refreshed, and the field of view around it with the tiles appended; otherwise only the sight of the units around.
  `BattleTileDamageType` carries the parts of `RuleDamageType` the code reads (`Fire` is the base `DT_IN`: fire threshold 0, `ToTile` 0 so a hit does no damage to the part unless its armor is 0, `TileDamageMethod` 2,
  the damage rolled in `fireDamageRange`); the other random types throw until branch 5 builds the types from the item rules.
- **Burning floors.** `BurnFloor` (walks and falls): `Ignite(tile, 1)` and a `DT_IN` hit at the voxel `(x * 16 + 8, y * 16 + 8, z * 24 - terrain level)` with the unit's strength. The walk does it in the finished-step block
  after the falling test (not while falling) and then still stands: the reference's test of "not standing" is for a status the call never leaves (a hole queues the unit through `applyGravity`; it stays standing), so the path goes
  on (the next step over a hole is no valid step: the walk stops `PathBlocked`) and when the walk is over, as `BattlescapeGame::think` pushes the fall state, `ResolveFalls` runs (`UnitFell` events). The landing of
  `UnitFallBState` burns, and then, bug for bug, takes the unit off the falling list even when the hole it burnt left it no floor (it hangs in the air). `FallsRefusal`'s dry run asks only the hit's refusals
  (the burn itself would change the map the snapshot does not hold). `battle.burn-floor-unsupported` is gone, `BattleState.Random` is the stream these draw from (the campaign hands its own to the battle before each command; a trial copy draws from it and puts it
  back, so the live run draws the same numbers).
- **`SpreadFireAndSmoke(environment)`** is `SavedBattleGame::prepareNewTurn` for branch 6's end of turn: burning tiles not set alight this turn count down and ignite the four neighbours no wall shields (`horizontalBlockage` with the damage block, the
  tile's smoke the power), or burn out (smoke cleared, a burnable object and its floor destroyed with the objective count, a burnable bare floor destroyed, then gravity on the tile); smoking tiles that do not burn thin out by one and
  pass their smoke to open neighbours with none or with smoke added this turn; a burning tile's smoke rises one level through a floorless tile and spreads at half strength; the smoke added this turn is averaged and the overlaps cleared
  (the tiles that still smoke call `environment`, `Tile::applyEnvi`, which branch 6 supplies). `FireSpreadRefusal` is checked first: a fire that burns out over an exploding part (the object, the floor, or the object's dead form that takes the floor's place) is `battle.terrain-explosion-unsupported` before any change.
  Not in it (branch 6): the units' `calculateEnviDamage`, the AI's danger flags, the light and sight updates of the end of turn, and resolving the falls it queues.
- **Not ported.** `hitUnit` and the unit part of `hit` (branch 5), `explode` and the explosive chain, the hit log, the `isPreview` shortcuts, `Tile::animate`'s sprites, and a dry run of a landing burner does not apply
  the burn (a later landing of the same fall could differ in terrains the burn changes: only a fall of several burners over the same tiles).
- **Tests.** `BattleFireTests` (compatibility, 43): ignite (fuel + 1, smoke, overlap; kept fire; no flammability and no draw; no fuel; a zero chance; the wrapping fuel), the smoke clamps, byte sum and burning tile, `HitTile` (fire,
  void and burning tiles, the fire below twice the threshold, smoke replacing fire, the smoke limit, the smoke formula below twice the threshold), the tile damage fraction, rounding, limit and method 1, a hit on a floor (fire, smoke, light), a floor broken on the ground and above
  it (scorched earth, a hole and the unit queued to fall), an object that holds and one that wears to its dead form, objectives (a hit, the cap, burnt down), the dead form past the set and the grav lift, a wall part, nothing found, the unit and body refusals
  (a live body, a dead one, a unit in the voxel, the attacker excluded), the explosion refusal and its dry run, a base defense module count, a burner's walk (fire on each tile, none on the start tile, draws per step), a burner on a floor with no voxels, a walk that burns through (the stop and the fall), the walk's two
  refusals with the state compared, a unit without the ability, a landing burner (and its dry run), a landing that burns through, a landing refused for a body, fire spreading past a wall, a fire set this turn, a fire burning out (object, floor, gravity, item drop), bare floors that burn or hold, an objective burnt down, the explosion refusal of the routine,
  smoke thinning, spreading, averaged where two meet, stopped by a smoke wall, rising from fire, and no change without fire or smoke. The retired refusal's tests (first turn, spawn, gravity, inventory, walk) now assert the unit lands and walks on. Not tested: the `status != standing` branch after a hit (the call
  cannot leave the status), a base defense mission built by the generator (the test sets the mission type and module grid by reflection), the hostile reserve's interplay.

## S10 — Battlescape client (2026-11-07)

Reference: `BattlescapeState` (`mapClick`, `btnKneelClick`, `btnUnitUpClick`, `btnUnitDownClick`, `btnVisibleUnitClick`, the keys), `BattlescapeGame` (`primaryAction`,
`secondaryAction`, `moveUpDown`), `Pathfinding` (`previewPath`, `refreshPath`, `findReachable`), `Map` (the tile shade of the drawing), `Options` (`strafe`, `battleNewPreviewPath`).

- **Rules side (Oxce.Gameplay).** `BattleMovementQueries.cs` (`BattleState`): `PathView` is `Pathfinding::calculate` plus `refreshPath` as an immutable `BattlePathView`: the steps (position, direction, time units and energy
  left, not below 0, and a marker), the whole walk's time units (the kneel's standing up and, for an armor with `turnBeforeFirstStep`, the turn before the first step included) and energy, or the refusal code with no steps
  (`battle.walk-not-allowed`, `battle.walk-move-not-allowed`, `battle.walk-no-path`). The marker is the reference's colour: red when time units or energy run out, yellow when `CheckReservedTimeUnits(..., justChecking: true)`
  fails (with an auto shot reserve when none is set: `CheckReservedTimeUnits` takes the replacing reserve as an optional argument, no state is switched), else green. A hostile viewer's reserve (branch 6) is not a
  reserve: it counts as kept. `ReachableView` is `findReachable` with no action cost kept back. Both are viewer-scoped (`IBattleViewQuery.QueryPath`, `QueryReachable`): only for a live unit of the viewer's side on
  that side's turn, a path only to a tile on the map whose floor the viewer has discovered (the `LevelView` rule, so the preview tells nothing the plan does not show), and reachable tiles only where discovered.
  `BattleLegalActions` now lists `Turn` (any unit on the map), `Kneel` (the armor allows it, the unit does not float), `Walk` (the armor allows moving) and `MoveUp` and `MoveDown` (`validateUpDown`, the level exists, the armor
  moves): what the command can start; time units are the command's refusal. `BattleTileView.Shade` is `Tile::getShade` of each listed tile. `SelectSpotter` (new command, event `UnitSelected`) is the right click of a
  visible-unit button: of the selectable units of the side that have the target in their visible list, the one with the most time units as a percent of its base statistic (a tie keeps list order) is selected;
  none is `battle.selection-no-spotter` (the reference does nothing). `Walk`'s move check is the helper `MoveRefusal` the query shares.
- **Client side (Oxce.Engine).** `BattleViewModel`: `Walk` (the tile click: `WalkUnit` at the camera's level with Shift as `IgnoreSpottedEnemies`; the camera follows the unit), `Turn` (the right click), `Kneel`,
  `MoveUpDown` (the camera follows to the unit's level), `CenterOnUnit`, `SelectSpotter`, `Preview` (the pointer over a tile), `Path` and `Reachable` (re-asked on each revision, level change or
  preview change) and `LastEvents` (the events of the last command that ran). `BattleReplay.From` turns a movement command's events into frames (a step or fall with its position, and a line of text for the rest);
  `BattlescapeClient` plays one frame a `Tick`, shows its line as the message, draws the steps played as a trail and a box on the last one, and ends the replay (playing it out at once, and dropping that press, as the busy game drops clicks; a right click then shows `battle.cancel-walk-unsupported`) on a press only (a release, a key repeat or the pointer moving do not end it). The replay is presentation: the battle already holds the result (ADR 0037). Input: left click on a visible unit of the viewer's side that is not selected selects it, on a hostile one does nothing (the
  reference's `else if` skips the walk), anywhere else it walks (the unit's own tile is `battle.walk-no-path`); right click turns or opens the door in front; the middle button over a unit is
  `battle.alien-inventory-unsupported (Phase 8 (AlienInventoryState))` (`AlienInventoryState` is a Phase 8 screen), elsewhere nothing; `X` kneel, `U` up, `D` down, `G` shows the reachable tiles; the keys
  1 to 9 and 0 (a key is never a right click: Shift changes nothing) and the panel's buttons (up to `VISIBLE_MAX` 10, left click centers, right click selects the best spotter) are the visible-unit buttons. The plan view draws each
  discovered part in the shade of its tile (palette ramps 64, 80 and 96 for floor, walls and objects), the previewed path (the panel shows the walk's cost; the amounts left per tile are in the view and not drawn), and the reachable tiles as dots. A part whose tile floor is not discovered is drawn black (the reference's shade 16). The pointer preview and the `G` overlay are the port's additions (the reference previews only on a click with `battleNewPreviewPath` on, default off, and `findReachable` serves only the AI), as the plan asks. A path whose steps cross a tile the viewer has not discovered is no preview (null).
  Retired: `battle.kneel-unsupported`, `battle.walking-unsupported`, `battle.unit-up-unsupported`, `battle.unit-down-unsupported`, `battle.visible-unit-center-unsupported`,
  `battle.secondary-action-unsupported`, `battle.middle-button-unsupported` (the middle button's name changed with its reason).
- **Not ported.** `Options::strafe` (default off: Ctrl and Alt then move nothing, so the client sends `BattleMoveType.Normal` only, as the reference does with the default options), the two-click preview
  (`battleNewPreviewPath`, default `PATH_NONE`: one click walks), the camera's pixel following during a walk, sounds (`playUnitResponseSound`), the cursor types, `cancelCurrentAction` for a walk in progress (a walk resolves fully in the rules, ADR 0037: the press is dropped and `battle.cancel-walk-unsupported` names it; branch 5's targeting adds a pending action), the wounded, burning and dizzy friends of `updateSoldierInfo` beside the visible units (`battle.wounded-indicators-unsupported` is shown in the panel while the selected unit has wounded or burning units), `reShade`'s night vision and `oxceFOW`'s shading, the carried unit's carrier for a visible-unit button on a unit without a position (`battle.visible-unit-carried-unsupported`), debug mode and the minimap.
- **Tests.** `BattleClientMovementTests` (compatibility, 24): the path query (markers and costs of a straight walk and that the walk then costs the same, red steps and the zero floor, the reserve with and without a reserve
  set, the kneel and the turn before the first step, the scoping to the viewer and to discovered tiles, a no path view and an armor that cannot move or run, a run), the reachable tiles (the cost limit, a diagonal,
  discovery, another viewer), the legal actions (walker, flier, hovering, an armor that cannot kneel or move, another side's turn), the shade and discovered parts of `QueryLevel`, `SelectSpotter` (the lone spotter,
  the most time units, a tie, an unknown target, no spotter), and the screen: a click walking with its replay frame by frame and the trail, an input during a replay, Shift against a plain click that
  stops on a new unit, a refused walk (state and revision unchanged), a click on another unit and on a hostile one and the middle button, the right click turning and opening a door, the kneel key (and an armor that
  cannot), the up and down keys with the camera's level, the visible-unit keys and buttons, the pointer's preview (with its colours) and the reachable overlay, the drawn shade and the undiscovered tile, and that the
  later buttons stay refused while the walking keys are no later buttons. `BattleScreensTests` and `BattleViewTests` were adjusted (a tile click walks, the movement actions are listed, the shaded floor).
  Not tested: a mouse click on the panel's second row of visible buttons, a kneeling unit's preview after the kneel key (the query is tested with a kneeling unit), the hostile viewer's screens (no client of another side yet).

## S11 — Acceptance (2026-11-08)

Reference: the whole movement stack; no new rules except one refusal (below).

- **Generated battles** (`PrivateBattleGenerationTests`, through `AssertPlaysOrIsRefusedAtTheBriefing`). Each of the six battles that start (UFO `Begining` farm, TFTD `Begining` island, `IronMode` and `Middle`, the two staged base
  defenses; the three Rosigma battles stay refused at the briefing by `battle.new-turn-script-unsupported`) now, after the first turn, is played by `BattleAcceptanceDriver` through the client's `BattleViewModel`
  (the commands the screen sends): a soldier walks to the farthest tile its reachable-tile query lists whose previewed path leaves 28 time units, turns and kneels and stands, and a soldier opens a door where the map has
  one it can reach (it walks to a tile beside the door and turns towards it until the right click opens it). A UFO door opened on all six maps (on the UFO farm, port, UFO base defense and drill base only once the opponents were spent, see below), and the test asserts it for each. The soldier turns, kneels and stands on all six (asserted). **Leaving the craft is only partly shown:** the reachable tiles are the viewer's discovered ones, so on the TFTD island and sunken-escort maps the soldier inside the Triton has one reachable tile and its 'computed path' is one step (24,16,0 to 23,16,0, beside the Triton's door); the other four maps walk 3 to 5 steps from the craft's tile. The driver does not check that the unit ends outside the craft (the craft's tiles are not in the battle's view). The battle still validates, and every viewer still sees only its own side's discoveries and
  spotted units. The battle then saves and reloads: every viewer's discovered tiles with their parts, shade, smoke, fire, items and visible units equal the live battle's on every level
  (`BattleAcceptanceDriver.AssertSameSight`), and the units each viewer sees are the live ones and possibly more (the load's field of view looks from every unit at once).
- **Walk and reaction fire.** On every battle the soldier's walk, tried as it is, is refused with `battle.reaction-fire-unsupported` (branch 5's name): `HasPossibleReactor` over-approximates (plan D4) and an opposing
  unit with a weapon and a reaction score at least the mover's stands within the view distance on each of these maps (first turn, nothing spotted). The driver then walks with the opposing units' time units set to 0
  (their reaction score is 0, so the rules accept the walk) and gives them back; `Outcome.OpponentsSpent` says which. This is the acceptance's one setup; branch 5 replaces it with real reaction fire. The same
  applies to the door step. A reloaded battle may therefore show a viewer a unit it had not seen (the walker spotted by hostile units whose reaction was 0): the reload comparisons accept more, not fewer.
- **Private active-battle saves** (`PrivateBattleSaveTests.AssertFreeUnitWalks`, on its own load of each of the eight saves; placement is not re-validated, a saved battle keeps its units where they were): the loaded
  battle has discovered, shaded tiles for the local player and a player unit on the map, and the driver must return an outcome: a walk, turn, kneel and stand, or a named `battle.*-unsupported` refusal (own-rule refusals fail the test).
  Results (`artifacts/private-battle-acceptance-walks.json`): the vanilla UFO `battlescape.sav` and `battlescape 2.sav` and TFTD `battlescape 1.sav` walk (TFTD's also opens a UFO door); TFTD `battlescape 2.sav` is refused
  with `battle.reaction-fire-unsupported` even with the opponents spent; vanilla UFO `_autobattle_.asav` is refused with `battle.walk-unit-busy-unsupported` (its first soldier is held mid-panic); the **three Rosigma saves are
  refused with `battle.visibility-script-unsupported` (branch 8)**: the mod binds a `visibilityUnit` script, so the gate in `CampaignBattles` refuses every battle command, `SelectUnit` included, and the driver reports that
  refused selection as the outcome. Their queries (path, reachable, level) still answer, and are timed. Because of the same refusal the load skips `RecalculateFov` for them (`BattleStatePersistence`): a loaded
  Rosigma battle has its lighting computed (`CalculateLighting`) but keeps its saved spotting and discovery, so "lighting and field of view computed on load" holds for the other five saves only.
- **Found by acceptance, fixed.** A save can hold a unit mid-panic (status 8, which `OxceBattleSave` already reports as a warning): `WalkUnit` on it threw "The walk of battle unit 7 does not end" (the port's `RunWalk`
  steps `Standing` units only; the reference's `UnitWalkBState` also steps panicking and berserk ones, which are the panic's and the AI's walks, branch 6). `Walk` and `MoveUpDown` now refuse any unit whose status is not
  `Standing` with the new `battle.walk-unit-busy-unsupported` (branch 6) before anything changes. Test: `AUnitThatIsNotStandingIsRefusedTheWalkAndTheUpAndDownMove`.
- **Performance.** `BattleAcceptanceDriver.Measure` records medians of `QueryPath` (to the farthest reachable tile with a visible path), `QueryReachable`, a whole `RecalculateFov` and `QueryLevel` in
  `artifacts/private-battle-acceptance-timings.json`; the table is in [Performance baselines](performance-baselines.md#battle-movement-queries-and-field-of-view-branch-4-s11-2026-11-08). The path and reachable queries
  allocate a pathfinder on each call (S10's note); they stay well under a frame except `QueryReachable` on the vanilla UFO `_autobattle_` save (12 ms) and the full field of view on 60x60x8 maps (about 1 s in Debug, once, at a load; on Rosigma the load does not run it, see the perf table's note).
- **Tests.** `PrivateBattleGenerationTests` (the six played battles, the reload comparison, the other viewers), `PrivateBattleSaveTests` (the saves), and the walk-status refusal in `BattleWalkTests`. Not tested: a walk of
  the Rosigma battles (refused at the briefing), and the hostile viewer's own walk (no client of another side).

## Branch audit fixes (2026-11-08)

The branch audit (`git diff main...HEAD`, report in `.agent-tmp/audit13-findings.md`) found no High finding. Fixed:

- **M1.** `MoveUpDown` with no path is reachable: `validateUpDown` never looks for units, so a flier goes up when the tile above has no floor, and a known unit hovering there blocks the destination, `Calculate` leaves no path and the move ends where it
  started (`UnitWalkEnded(Arrived, 0 steps)`). Test `AnUpButtonWithAKnownUnitHoveringAboveEndsTheMoveWithoutAPath`; the old test (which only showed a roof refusing) is now `AnUpButtonWithARoofAboveIsRefused`. The status doc no longer calls the case unreachable.
- **M2.** `TheMainHandPickCoversUnloadedSpecialBlasterAndFactionCases`: two blasters at equal cost for a hostile non-quickest pick (the cost rule says left, the blaster priority right), which fails without the blaster block.
- **M3.** `ARestoredUnitTurnsToTheDirectionsItWasSavedFacing`: a unit saved with its turret facing another way than its body is restored with `ToDirection` and `ToDirectionTurret` equal to the loaded directions.
- **M4.** `AStepIntoABigWallAddsTheWallFromItsFarSideForDamageAndSmokeOnly` (new TESTSET record 16, a west big wall with a high explosive block of 50): the end tile's big wall in `horizontalBlockage`'s tail, for damage and smoke, from both sides.
- **M5.** `ABlockageBetweenLevelsMeasuresDamageAndSmokeByTheFloorArmorAndSmokeDimsEnhancedLightAcrossLevels`: `VerticalBlockage` for damage and smoke (up and down, an armored floor), and smoke on the upper level dimming enhanced light there and not below.
- **M6.** `TwoUnitsBelowCannotEscapeOntoTheSameTile`: two units below with one shared escape tile (refused with `battle.fall-knockout-unsupported`, state untouched), and with a second way open (each takes its own).
- **M7-M10, L2, L4.** Status Progress table, the plan's refusal names and trial-copy wording (`docs/phase-7-plan.md`), the implementation plan's branch 4 line, the performance doc's "not per walk" claim (the trial copy's restore costs a full lighting and field of view per walk command, unmeasured), `AGENTS.md`'s list of status records, the stale "Not tested" line, and "## Next"'s refusal list.
- **Not changed.** L1: the two loop-limit throws (`BattleGravity`, `BattleWalk`) are invariant guards and stay untested. L3: the death-trap refusal stays broader than the reference (any `STR_DEATH_TRAP_<n>` rule), as documented in S7; branch 5 narrows it with the real proximity check.

## Deduplication audit (2026-11-08)

A review of `git diff main...HEAD` for repeated code. Behaviour is unchanged except the one fix below.

- **Fix.** `PathView` priced the previewed steps on a second pathfinder, whose `StrafeMove` was never set, so a one-step strafe was previewed one time unit cheaper than its walk paid. `refreshPath` uses the pathfinder that calculated the path; the port now does too. Test: `AStrafeStepIsPreviewedWithTheExtraTimeUnitItsWalkPays` (fails without the fix: 6 instead of 7).
- **Shared geometry.** `BattleGeometry` holds the direction vectors, `VectorToDirection`, `DirectionTo`, `Step`, the 2x2 part offsets, the horizontal squared distance and the voxel size of a tile. It replaces the copies in `BattleBlockage`, `BattlePathfinder` (two), `BattleSight`, `BattleUnit`, `AlienDeployment` and `BattlePlacement`. `BattleBigWalls` holds the big wall types once for `BattleBlockage` and `BattlePathing`.
- **One copy of a rule.** `BattleState.OverlappingUnit` replaces the copies in `BattleSight` and `BattlePathfinder`. Also shared now:
  - `UnitOnTile`, `BattleUnit.RequiredPosition`, and `BattleLightCache.BlocksStep` (the tile line's big-wall exception, used by sight and enhanced light);
  - `BattleMap.Neighbour` and `BattleMap.Area`;
  - `DestroyedForm` (`dieMCD` with the data set bound), `IsFloorlessWalker`, and `ResolveFalls(rules, events, out moved)` (the walk's `UnitFell` events);
  - `MoveRefusal` (walk and preview), `ReservedKneelCost` (doors and reserve), and `SpecialWeapons` (reserve and reaction);
  - `CloseInventory` (first turn and inventory moves), `RuntimeUseCost.Zero`, and the rule heights helper.
- **Smaller cleanups.** In `BattlePathfinder`: the step check, the wall-cost sum, the knows-of-unit test, the four stairs cases, the node reset and Bresenham's local direction tables now appear once each. Sight's per-call offset and sign tables are now static.
- **Command layer.** `CampaignBattles.TrialRefusal` is now the only trial copy: the spawn and first-turn trials use it too, so they also copy the live spotting state, which matches the save there. `RunMovement` serves both `WalkUnit` and `MoveUnitUpDown`.
- **Client.** `BattlescapeClient` has one helper each for the shaded colour, the tile dot and the visible-button count.
- **Tests.** `BattleTestSupport` holds `Replaced`, `Refusal`, `Events`, `End` and `GunFor`, which eight test files had each copied.

## Next

The branch audit's findings are fixed (section above); the branch awaits its re-audit and merge. Notes for branch 5 and later workers:

- **What S11 added.** `tests/Oxce.CompatibilityTests/BattleAcceptanceDriver.cs` (`Walk`, `OpenDoor`, `Measure`, `Record`, `AssertSameSight`); the refusal `battle.walk-unit-busy-unsupported`; the acceptance records
  `artifacts/private-battle-acceptance-walks.json` and `artifacts/private-battle-acceptance-timings.json` (not committed, like the other artifacts).
- **Acceptance setup to retire in branch 5.** The driver sets the opposing units' time units to 0 so the walk is not refused by `battle.reaction-fire-unsupported` (S11 above); remove `WithOpponentsSpent` when real reaction fire exists. The subset comparisons of the reloads (`AssertSameSight`, the units in `PrivateBattleGenerationTests`) likely stay: a walk's
  field of view is limited to the event sector around the unit (`calculateFOV(pos, 2, false)`), while a load's `recalculateFOV` looks from every unit, so the load can spot more than the live battle whatever the time units.
- **Left for later branches (unchanged).** `itemDrop` (branch 5), the stomp's knock-out and `checkForCasualties` (branch 6), the hit's unit part and explosions (branch 5), the environment's damage to units,
  `calculateEnviDamage` and the end of turn's light and sight (branch 6); `canUseWeapon`'s research, AI use delay, depth and two-handed rules, `getWeaponForReactions`' preferred hand and
  `reactionsDisabled*` (branch 5, with the real reaction); `BattleUnit.HitState` is never set and no selection resets it (`resetUnitHitStates`); the AI's walk parts (S8's "Not ported"), the panic and berserk
  walks (`battle.walk-unit-busy-unsupported`) and `checkForReactivation` (branch 6). `SpreadFireAndSmoke` and `CloseUfoDoors` are routines for branch 6's end of turn, called by nothing yet.
- **S9's notes still apply.** Commands that can meet fire refuse with `battle.hit-unit-unsupported` or `battle.terrain-explosion-unsupported` (branch 5) before any change; the walk commands run on a trial copy first
  (`CampaignBattles.TrialRefusal`, not available without terrain files).
- **Refusals this branch added or touched and still leaves in place, and their owners.** Branch 5: `battle.reaction-fire-unsupported`, `battle.proximity-unsupported`, `battle.hit-unit-unsupported`, `battle.terrain-explosion-unsupported`, `battle.module-map-unsupported`. Branch 6: `battle.hostile-reserve-unsupported`,
  `battle.fall-knockout-unsupported`, `battle.casualties-unsupported`, `battle.walk-unit-busy-unsupported`. Branch 8: `battle.new-turn-script-unsupported`, `battle.flare-power-bonus-unsupported`, `battle.visibility-script-unsupported`. Refusals from earlier branches (`battle.casualties-unsupported` and the other branch 3 codes: turn recovery script, turn one revive, reinforcements, preview) stay with their owners in the plan. The S10 client's
  `battle.cancel-walk-unsupported`, `battle.wounded-indicators-unsupported` and `battle.visible-unit-carried-unsupported` wait for the branch that owns the feature (see S10's section).
- **Performance.** A walk or up and down command on a battle with terrain files restores a trial copy (a full lighting and field of view); the command's total cost is not measured (the performance doc says so).
- **Gotchas.** A post-edit hook reformats edited C# files, so read a file again before a second edit. A backslash-n written through a script or heredoc reaches a C# file as a real newline: add string literals that
  carry one with the Edit or Write tool (a Python script that needs a backslash-n builds it with `chr(92) + 'n'`; a heredoc that holds an apostrophe can also break the shell call: write scripts with the Write tool). The
  armor key for the special ability is `specab`; an armor's run, strafe and walk costs are `moveCost: {runPercent: [time, energy]}`. A test that adds `BattleUnit`s by hand needs a `SoldierId` or a unit rule, and a
  battle with such a unit cannot be restored for the trial copies. `BattleState.Revision` has no setter: a test that wants the client to read again announces a change through a command (a `SelectUnit` of the unit
  already selected). The reload comparisons go through `BattleSaveUnitTests.SightFree`.
  Gotchas carried from branch 3 (see `docs/battle-session-ui-status.md` "## Next"): write test files with LF; the test runner is Microsoft Testing Platform (`dotnet test --project <proj> --filter-class <full name>`);
  battle test helpers are internal for reuse; raw string literals in test rules have their indentation stripped.
