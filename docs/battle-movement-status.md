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
- **No turn, a door.** A unit that does not turn (same direction, or the tile it stands on) makes `init` try `unitOpensDoor(unit, true)`. That is
  S6's: when a swinging door (not for a big unit) or a closed UFO door stands in the right-click tiles of its direction (`DoorChecks`, the
  reference's table with `rClick` set, from the tile above when on stairs) the command is refused with `battle.door-unsupported` before any change
  (over-approximating: payment and a blocking unit are not asked); with no door it completes as `NoTurn`. S6 retires the refusal.
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
  neutral side's turn and the mover's own side (state queries); every direction's right-click door tiles, a swinging door, a big unit, the
  stairs offset; the spotting of a weaponless unit, a hit unit that faces away (hit state, the AI's memory, melee memory under rule 2).

## Next

S6 — doors (plan row S6). Notes for its worker:

- **What S5 left for it.** `TurnUnit` refuses with `battle.door-unsupported` (`BattleMovementReasons.DoorUnsupported`) when `unitOpensDoor(unit,
  true)` would find a door in front of a unit that does not turn (`BattleState.DoorInFront`, `DoorChecks`: the reference's right-click table).
  S6 ports `unitOpensDoor` (the right-click form from `UnitTurnBState::init` with `BA_NONE`, including its result 4 -> `STR_NOT_ENOUGH_TIME_UNITS`
  and 5 for the reserve; the walking form is S8's), `Tile::openDoor`, `checkAdjacentDoors`, `calculateLighting(LL_FIRE, doorCentre, doorsOpened,
  true)`, `calculateFOV(doorCentre, doorsOpened, true, true)` and `updateEnemyKnowledge` (`BattleUnit.UpdateEnemyKnowledge(tile)` exists, S3), then
  retires the refusal and reuses `DoorChecks` (it takes the direction and the footprint tile). The walk's door form is called from S8. A
  `TurnUnit` that does turn never reaches the door code.
- **Spotting after a mover.** `BattleState.SpotMover(rules, sight, unit)` and `HasPossibleReactor(rules, unit)` are what S7 (landing) and S8 (each step)
  run after the move, as `checkReactionFire` does; call `HasPossibleReactor` before the first change with the mover's planned reaction score (the
  kneel evaluates it with the time units already paid) and refuse with `BattleMovementReasons.ReactionFireUnsupported`. `PassesSpottingFilters`
  reads `Side`, so call them only for the mover of the side on turn.
- **Not done and needed by later slices.** `canUseWeapon`'s research, AI use delay, depth and two-handed rules, `getWeaponForReactions`' preferred
  hand and `reactionsDisabled*` (branch 5, with the real reaction); `BattleUnit.HitState` is never set (damage is branch 5's) and no selection
  resets it (`resetUnitHitStates`); the legal-action list does not list turn or kneel (S10); the AI's turn-then-kneel and
  `checkForReactivation` (branch 6).
- **Test helpers.** `BattleTurnKneelTests.Make` builds the sight tests' `Flat` arena with a turn cost of 2, `tuSnap: 20`, `tuAimed: 50` and an
  alien with reactions 100 (`BattleSightTests.SwingDoor` and `StairFloor` are TESTSET records 9 and 10); `Arm` gives the alien a loaded gun, `Hold` any item. Commands run through `arena.Campaign.Execute`; an unfinished
  casualty (health 0) refuses every command (`battle.casualties-unsupported`), so test a dying unit through state queries.
- **Gotchas.** A post-edit hook reformats edited C# files, so read a file again before a second edit. A backslash-n written through a
  script or heredoc reaches a C# file as a real newline: add string literals that carry one with the Edit or Write tool. A unit that still
  has its `InventoryTile` has no tile of its own and `StartFirstTurn` is the first sight; tests that add `BattleUnit`s by hand must give
  them a `SoldierId` or a unit rule, since heights are read. The reload comparisons go through `BattleSaveUnitTests.SightFree`.
- Later slices (for planning ahead): S8's walk calls `BattlePathfinder.Calculate` and `GetTuCost`, recomputes lighting with
  `CalculateLighting(Items or Units, position, 2)` after each step as `UnitWalkBState.cpp:224` does, then `CalculateFov(unit)` and
  compares `UnitsSpottedThisTurn` for its stop on newly spotted units. S9 reads the smoke threshold from `RuntimeCampaignSettings`, and its
  terrain changes call `RefreshBlockageCache` (via `CalculateLighting(..., terrainChanged: true)`) and then
  `CalculateFov(position, radius, true, appendToTileVisibility: true)`.
- Gotchas carried from branch 3 (see `docs/battle-session-ui-status.md` "## Next"): write test files with LF; the test runner is Microsoft
  Testing Platform (`dotnet test --project <proj> --filter-class <full name>`); battle test helpers (`BattleStartTests.Requested`,
  `Started`, `Battle`, `BattleSaveRootTests.Generated`, `BattleSaveUnitTests.Loaded`) are internal for reuse;
  `BattleCampaignSaveTests.AtFirstTurn` is private; a campaign snapshot with a battle restores only with terrain files; raw string
  literals in test rules have their indentation stripped (list entries start at two spaces).
