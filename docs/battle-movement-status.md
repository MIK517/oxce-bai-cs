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
| S1 movement groundwork: constants, action costs, reserved TUs, unit primitives | Done, awaiting audit (2026-11-01) |
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

## Next

S2 — pathfinding (plan row S2). Notes for its worker:

- **What S1 left for it.** `RuntimeBattleArmor.StepCosts`, `TurnCost`, `AllowsMoving`, `AllowsRunning`, `AllowsStrafing`, `AllowsSneaking` and the
  heights; `RuntimeCampaignSettings.ExtendedRunningCost` and `ExtendedMovementCostRounding`; `BattleUnit.MoveCosts` and `ArmorMoveCosts` (the base
  four kinds, saved), `HaveNoFloorBelow`, `Floating`, `MovementType`, `KneelUpCost`, `IsBigUnit`; `BattleMap.HasNoFloor`, `HasLadder`,
  `TerrainLevel`, `TimeUnitCost`; `BattleState.CheckReservedTimeUnits`. `Pathfinding::getTUCost` reads the unit's per-kind move cost multipliers
  (`getMoveCostWalk` and the others, combined with the base costs: read `Pathfinding.cpp` and `BattleUnit.cpp` for where they meet) (the kneel-up cost at `Pathfinding.cpp:1266` and `1270` is in `refreshPath`, the path preview, not `getTUCost`).
- **Unit rule flags.** Project the unit rule's `isBrutal` and `isNotBrutal` for `BattleUnit::isBrutal` (`BattleUnit.IsBrutal` and `IsNotBrutal` are the
  saved load-only ones); `brutalAI` (hostile, default on) and `brutalCivilians` (off) are options at their defaults.
- **Where options go.** `BattleOptions` (`BattleActions.cs`) holds the user options at their defaults as static properties; add `strictBlockedChecking`,
  `brutalAI` and the others there as a slice reads them. Do not make them process-wide mutable switches.
- **`BattlePathing`** has the wall checks; the floor and unit parts of `isBlocked` and `isBlockedDirection` are S2's. Units out of play or before
  the first turn: use `BattleState.TileOf`. `BattleState.SetTile` and `WalkStep` throw for a unit that still has its `InventoryTile`.
- **Test helpers.** `BattleMovementGroundworkTests.Open` (a suited soldier past its first turn; `terrain:` adds MCD records such as a hole
  and a ladder through `BattleTestTerrain`) and its `WithGun` / `WithSuit` rule edits are private; make them internal or copy the steps. The soldier's
  `SUIT` armor has movement type 1 (fly), so a test of walking rules should replace it (`movementType: 0`).
- Gotchas carried from branch 3 (see `docs/battle-session-ui-status.md` "## Next"): write test files with LF; add C#
  string literals with `\n` through the Edit tool; the test runner is Microsoft Testing Platform
  (`dotnet test --project <proj> --filter-class <full name>`); battle test helpers (`BattleStartTests.Requested`,
  `Started`, `Battle`, `BattleSaveRootTests.Generated`, `BattleSaveUnitTests.Loaded`) are internal for reuse;
  `BattleCampaignSaveTests.AtFirstTurn` (plays `BeginBriefing`, `ConfirmBriefing` and `StartFirstTurn`) is private, so
  make it internal or follow its steps for a battle past its first turn; a campaign snapshot with a battle restores
  only with terrain files; raw string literals in test rules have their indentation stripped (list entries start at two
  spaces).
- Later slices (for planning ahead): S4 can read voxel data through `LoftempsCodec` (`src/Oxce.Formats/Terrain`); the battle does
  not load it yet. S3 reads the lighting constants and S9 the smoke threshold from `RuntimeCampaignSettings`. Branch 2 and 3 reload
  comparisons will see recomputed light and sight from S3 and S4 on; extend them rather than masking the new state.
