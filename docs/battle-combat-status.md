# Battle combat status

Phase 7 branch 5 (`claude/battle-combat`), from `main` after PR #81 (branch 4 merged, `fae8bfbc`). Plan:
[Phase 7 branch 5](phase-7-plan.md#branch-5--battle-combat); decisions:
[ADR 0037](decisions/0037-host-authoritative-session.md), [ADR 0038](decisions/0038-battle-aggregate.md),
[ADR 0039](decisions/0039-battle-save-intentional-differences.md),
[ADR 0040](decisions/0040-battle-movement-intentional-differences.md), [ADR 0041](decisions/0041-battle-combat-intentional-differences.md)
(battle combat intentional differences; S2 created it, later slices add entries) and the plan's branch decisions.

## Progress

| Slice | State |
|---|---|
| S0 planning | Done and audited (2026-11-08) |
| S1 combat groundwork: item, armor and damage rules, stat bonuses, combat constants, script refusals | Implemented (2026-11-08), audit pending |
| S2 line of fire and accuracy | Implemented (2026-11-09), audit pending |
| S3 damage to units | Not started |
| S4 casualties | Not started |
| S5 explosions | Not started |
| S6 melee and the attack command | Not started |
| S7 shooting | Not started |
| S8 throwing, priming, launching and proximity | Not started |
| S9 reaction fire | Not started |
| S10 battlescape client | Not started |
| S11 acceptance | Not started |
| Branch audit | Not started |

## S0 — Planning (2026-11-08)

Reference studied:
- `BattlescapeGame`: `primaryAction`, `checkForCasualties`, `convertInfected`, `autoEndBattle`.
- The attack states: `ProjectileFlyBState` (`init`, `createNewProjectile`, `think`), `ExplosionBState` (`init`,
  `explode`), `UnitDieBState`, `UnitTurnBState`'s targeting form, and the reaction checks of `UnitWalkBState`,
  `UnitFallBState` and `MeleeAttackBState`.
- `TileEngine`: `checkReactionFire`, `getReactor`, `determineReactionType`, `tryReaction`, and the function list of
  `hit`, `hitUnit`, `explode`, `detonate` and the melee functions.
- `AIModule::brutalScoreFiringMode`, `BattleUnit` (`damage`, `getFiringAccuracy`, `getAccuracyModifier`),
  `SavedBattleGame::reviveUnconsciousUnits` and `SavedGame::killSoldier`.
- `RuleStatBonus` (`load`, the defaults, `getBonus`), the options and constants these read (`Options.cpp`, `Mod.cpp`),
  and every combat script hook the attack code runs.

- The [plan](phase-7-plan.md#branch-5--battle-combat) splits the branch into S1–S11 and a branch audit: combat
  groundwork, line of fire and accuracy, damage to units, casualties, explosions, melee and the attack command,
  shooting, throwing and priming and launching and proximity, reaction fire, the battlescape client, acceptance.
- Reference facts that shape the plan:
  - **An attack is one chain.** It runs a targeting turn, each shot, and each impact (`ExplosionBState`, which calls
    `hit` or `explode`). Then come `checkForCasualties` (deaths through `UnitDieBState`, conversions,
    `reviveUnconsciousUnits(true)`, `convertInfected`), terrain explosions chained by `checkForTerrainExplosions`, and
    the units and items the explosion spawns. When the last projectile is done, `checkReactionFire` runs, and its
    reactions push their own shots. The plan ports the reference's state queue order so that one command resolves all
    of it.
  - **Time units are spent before the shot is checked.** The targeting turn (`UnitTurnBState`) pays for each eighth
    before `ProjectileFlyBState::init` checks time units, ammo and range, and `createNewProjectile` checks the line of
    fire. A failure after the turn is therefore a stop with its reason, not a refusal.
  - **A hit needs the casualty code.** `BattleUnit::damage` itself sets fire, infection (`setRespawn`), the attacker's
    spotting and `wasHitBy`, and pushes the self-destruct explosion. A hit cannot be completed without
    `checkForCasualties`, `UnitDieBState`, `convertUnit`, `convertInfected` and `reviveUnconsciousUnits`, which earlier
    branches had given to branch 6. The plan moves them here, and with them three refusals (casualties, fall knock-out,
    turn-one revive).
  - **Reactions need melee and some AI scoring.** `determineReactionType` tries a melee weapon first, `tryReaction`
    scores the reaction with the AI's `brutalScoreFiringMode` (and `explosiveEfficacy` for a hostile reactor), and
    close-quarters combat rolls `meleeAttack` before a shot. Melee moves from branch 6 to this branch; the AI scoring
    used by reactions is ported here, the AI's own turn is not.
  - **Stat bonuses are scripts in the reference.** Every `RuleStatBonus` (accuracy, power, melee and throw multipliers)
    is compiled into a script, the defaults included. The port evaluates the coefficient form, and a bonus written as
    code refuses by name (branch 8).
  - **Combat script hooks.** The attack code runs `hitUnit`, `damageUnit`, `damageSpecialUnit` (each with its ammo
    form), `tryMeleeAttack`, the three reaction hooks and `awardExperience`. They are branch 8's bindings and refuse
    by one name, as `visibilityUnit` does.
  - **`BAM_MISSILE` is only used by `AIModule`.** It searches for a blaster launcher's waypoints, so it moves to
    branch 6. The player's launch uses the waypoints the player places.
  - **The inventory screen never throws.** `battle.inventory-throw-unsupported` (branch 3, which noted that the
    reference inventory has no throw) names no reference feature and is removed.
  - **Option defaults.** `battleAutoEnd` and `battleNotifyDeath` default to off, `forceFire` to on,
    `battleConfirmFireMode` to off on the desktop, and `battleExplosionHeight` to 0. The close-quarters combat constant
    defaults to off but mods set it.
  - **The hit log is not saved.** `appendToHitLog` is runtime text for the next turn's screen.
- Not yet checked against the reference, left to the owning slice: the detailed rules of `explode`'s spread, `detonate`,
  `hitUnit`'s body items, `Projectile::applyAccuracy`, `validateThrow` and `MeleeAttackBState`; where the battlescape constructor's casualty check runs in the port's flow (S4).

### Refusals this branch inherits, and the slice that retires each

| Refusal | From | Retired by |
|---|---|---|
| A crashed UFO's power sources (`BattleGenerator.CrashedPowerSources`) and map grenades primed at -2 (`PrimedJunk`), generation errors | branch 1 | S5 |
| `battle.hit-unit-unsupported`, `battle.terrain-explosion-unsupported`, `battle.module-map-unsupported` | branch 4 S9 | S3, S5, S5 (ADR 0041) |
| `battle.proximity-unsupported` (and the broad death-trap test, branch 4 audit L3) | branch 4 S7 | S8 |
| `battle.reaction-fire-unsupported` (`HasPossibleReactor`, the named leak) and the acceptance's `WithOpponentsSpent` | branch 4 (S5, S7, S8, S11) | S9 |
| `battle.casualties-unsupported` (`HasUncheckedCasualty`, the floorless-walker gate) | branch 3 S3, branch 4 S7 (owner was branch 6) | S4 |
| `battle.fall-knockout-unsupported` | branch 4 S7 (owner was branch 6) | S4 |
| `battle.turn-one-revive-unsupported` | branch 3 S3 (owner was branch 6) | S4 |
| `battle.flare-power-bonus-unsupported` (branch 8) | branch 4 S3 | S1 narrows it into `battle.stat-bonus-script-unsupported` (still branch 8) |
| `BattleTileDamageType.RandomDamage`'s "Only the fire random type is ported (Phase 7 branch 5)" (`BattleFire.cs`) | branch 4 S9 | S1 |
| `battle.inventory-ammo-unsupported`, `battle.inventory-prime-unsupported`; `battle.inventory-throw-unsupported` removed | branch 3 S4 | S8 |
| The client's `battle.shooting-unsupported`, `battle.left-hand-item-unsupported`, `battle.right-hand-item-unsupported`, `battle.launch-unsupported`, `battle.reserve-time-units-unsupported`, `battle.reserve-kneel-unsupported`, `battle.visible-unit-carried-unsupported` | branches 3 and 4 S10 | S10 |
| The client's `battle.special-unsupported` | branch 3 | S10: the special-weapon button opens the action menu; its branch 6 actions refuse as below |
| The client's `battle.psi-unsupported` and `battle.skills-unsupported`, labelled "Phase 7 branch 5" | branch 3 | S10 relabels them to branch 6; the menu's psi amp actions refuse with `battle.psi-unsupported` and the medikit, scanner and mind probe with the new `battle.item-use-unsupported` (both branch 6) |

Kept by their owners: `battle.hostile-reserve-unsupported`, `battle.walk-unit-busy-unsupported`,
`battle.reinforcements-unsupported`, `battle.inventory-danger-zone-unsupported` (branch 6); `battle.no-live-aliens`,
`battle.no-live-soldiers` (branch 7); `battle.new-turn-script-unsupported`, `battle.turn-recovery-script-unsupported`,
`battle.visibility-script-unsupported` (branch 8); `battle.cancel-walk-unsupported` (ADR 0037),
`battle.wounded-indicators-unsupported`, `battle.preview-unsupported` (Phase 8). The inventory screen's
`battle.inventory-layout-screens-unsupported`, `-clear-unsupported`, `-autoequip-unsupported`, `-template-unsupported`
and `-rename-unsupported` name no owner ("not ported yet", `BattleInventoryContracts.cs`); they are not this branch's.

Interim refusals this branch adds between its own slices (plan, "Interim refusals between slices"): from S3 until S4
a hit that takes a unit out or infects it keeps `battle.casualties-unsupported`; from S3 until S5 a self-destruct
keeps `battle.terrain-explosion-unsupported`; from S6, S7 and S8 until S9 the attack's reaction check keeps
`battle.reaction-fire-unsupported`.

### Audit of this plan (2026-11-08)

An independent audit found 1 High, 7 Medium and 8 Low findings; all are fixed in the plan and this record:
- **H1.** The refusals before the targeting turn are now listed per action as `ActionMenuState::handleAction` checks
  them. Shots and throws have no time unit, ammo or line-of-sight check before the turn, so those become stops after a
  turn (or refusals when the shooter already faces the target).
- **M1–M7.**
  - Option defaults added: `shootingSpreadMode` 1, `useChanceToHit` on, `diagTerrainMelee` off. Constants added: the
    reaction fire thresholds and `tuRecoveryWakeUpNewTurn`.
  - ADR 0039's "must refuse" for the module map: S5 amends it for destruction.
  - `UnitDieBState`'s saved effects are listed: dangerous nodes and `clearUnitSelection`.
  - The interim refusals between slices are named.
  - The slices' test columns now cover their scope.
  - The action menu's branch 6 actions have owners.
  - Quick swap is part of S8's ammo work.
- **Lows.**
  - The fire random type's guard is in the table above, and the unowned inventory refusals are listed.
  - The earlier plan sections point to the reassignments, and the code comments that gave `BAM_MISSILE` and the skill
    cost form to branch 5 now say branch 6.
  - Wording fixes: direction 3 for every death, the order of reaction weapons, no targeting turn for the player's
    melee, and the inventory prime before the battle only.
  - Branch 4's "## Next" now says the branch is merged.

## S1 — Combat groundwork (2026-11-08)

Reference: `RuleItem.cpp` (`load`, the getters with the mod's globals, `getAIUseDelay`, `getPowerRangeReduction`), `RuleDamageType.cpp` and the base
types of `Mod::Mod`, `RuleStatBonus.cpp` (`load`, the `set*` defaults, `getBonus` through the generated script and `statDataMap`), `Armor.cpp`
(`load`, the damage modifiers and immunities), `Unit.cpp`, `RuleSoldier.cpp` and the combat constants of `Mod::loadFile`.

- **Damage types.** `RuntimeItemDamageRule` (the strategic side's projection) is now the whole `RuleDamageType`: every field, the 20 base types
  (`Predefined`), the keys over them (`Create`), `getRandomDamage` for each random type (the dice types draw once per die, the default type follows
  `ResistType`, the fire type ignores the power; modes 1 and 2 give the lowest and highest), `getRandomDamageForTile` and every `get*FinalDamage`
  with its random form and the wounds (vanilla and spread). It replaces `BattleTileDamageType` (`BattleFire.cs` keeps `BattleDamageTypes.Fire` and the
  tile damage helper), so there is one projection per key and `HitTerrain` takes any type. The item's damage and melee types are read through its
  handle (`rules.Items[handle].Value.Damage` and `MeleeDamage`), as are the power, melee power, accuracy per action, clip size, requirements,
  `twoHanded`, `blockBothHands` and `underwaterOnly`; the new `RuntimeBattleItem.Combat` holds the rest.
- **Item combat fields** (`RuntimeItemCombat`): the four action configs (range, shots, ammo per shot, follow, ammo slot and chance overrides, arcing,
  names), ranges and dropoffs, throw ranges, `powerRangeReduction` with `PowerRangeReductionAt`, `ignoreAmmoPower`, shotgun fields, arcing, spray
  waypoints, compatible ammo by name and load and unload costs by slot, `LOSRequired`, `landOnly`, `specialUseEmptyHand`, the fire extinguisher,
  `convertToCivilian`, the target matrix, `specialType`, the experience mode, the zombie fields, and the values the mod's globals stand in for
  (`kneelBonus`, `oneHandedPenalty`, `noLOSAccuracyPenalty`, `accuracyCloseQuarters`, `explodeInventory`, the AI use delay by battle type).
- **Stat bonuses.** `RuleStatBonus` (`Oxce.Mods/Rulesets/RuleStatBonus.cs`) holds the terms (in `statDataMap` order, unknown names dropped, four
  coefficients each) or script code, and evaluates the generated script's arithmetic natively: coefficients scaled twice by 1000 in single precision,
  each term added as a whole number of thousandths, rounded to the nearest and divided by 1000, nothing added for a missing unit. `ItemRuleLoader`
  follows `RuleItem::load`'s order (the psi amp default when `battleType` is read, `skillApplied` and `strengthApplied`, then the six keys);
  `ArmorRuleLoader` reads `psiDefence` (default psi defense) and `meleeDodge`. `IStatBonusUnit` is what a bonus reads of a unit; `BattleUnit` implements it.
  The recovery bonuses stay deferred (branch 6).
- **Flares.** A flare's glow range is now its power bonus with the unit that is its body or its last owner (`BattleItem::getGlowRange`), so a damage bonus
  written as coefficients, and `strengthApplied`, work; `battle.flare-power-bonus-unsupported` is gone.
- **Armor and unit rules** (`RuntimeBattleArmor`, `RuntimeBattleUnitRule`): the 20 damage modifiers, the nullable pain, bleed, zombie, melee threat
  immunities (the size rules of `Armor::load` were already applied by the rule loader), `overKill`, `meleeDodgeBackPenalty`,
  `meleeOriginVoxelVerticalOffset`, `instantWoundRecovery`, the two bonuses; `capturable`, `moraleLossWhenKilled` (and the soldiers', by type). Corpses and
  the side armor stay the strategic projection's (`Script.CorpseBattle`, `Script.Sides`).
- **Constants** (`CampaignCombatSettings`, `RuntimeCampaignSettings.Combat`): the accuracy globals, close quarters, the hit chance tables (a table
  without 40 rows of 61 values, or without `unitSize`, is skipped; the reference logs it), the reaction fire thresholds, the AI use delays (with the
  legacy `turnAIUse*` keys and the `ai` section's `aiUseDelayProxy` spelling), `tuRecoveryWakeUpNewTurn`, the extended options and
  `stunningImprovesMorale`, `statisticalBulletConservation`. The extended options and the damage ranges (`Interception`) are read from the `constants`
  section only, as `Mod::loadConstants` does (a mapping or a sequence of them); the damage ranges were read from the root before S1, which ignored
  TFTD_Damage's `constants: - damageRange: 50`.
- **Refusals.** `battle.combat-script-unsupported` (the hit, damage, melee, reaction and experience hooks, owned by armors or, in their ammunition and
  item forms, by items, or bound as global events) and `battle.stat-bonus-script-unsupported` (an item or armor in the battle with a bonus written as code,
  or a global event for one) join the visibility check: `BattleState.CombatScriptRefusal` in `FirstTurnRefusal` (the briefing and the first turn) and in
  `CampaignBattles`' gate for every command of a running battle. A loaded battle still loads. Only the units' armors and the items the battle holds
  are looked at.
- **Left for the later slices.** `HitTerrain` gets no weapon parameter: its first reader is `hitUnit` (S3), which adds it. No command exists yet, so S1
  adds no interim refusal. `Options::ignoreDelay` and the realistic accuracy configuration stay at their defaults. `ContentSnapshot`'s generated script
  scales a coefficient with one multiplication by 1,000,000 where the reference multiplies twice by 1000 (both in single precision): the native evaluator
  follows the reference; branch 8's script bindings should compare the two.
- **Tests.** `RuleStatBonusTests` (defaults, mapping, rounding at a half, polynomial and truncation, the current and normalized terms, unknown terms and
  code, the setters' order, the armor's defaults), `RuntimeItemDamageRuleTests` (every random type and the default's resist table, the mod's ranges,
  the draws, the 20 base types, conversions with rounding, the random forms, wounds, tile damage), `BattleCombatGroundworkTests` (the constants, the hit
  chance table, the item's defaults and keys with the globals, the AI delay by type, the damage types and alter keys, the armor and unit fields, every hook
  and bonus refusal at the briefing, a global event, items out of the battle, coefficients not refused, a running and a loaded battle),
  `BattleLightingTests` (a flare's bonus from its last owner; code refused), `BattleFireTests` (the full type in place of the old subset). `ScriptedDraws`
  in `tests/Shared` forces the draws of the later slices.

## S2 — Line of fire and accuracy (2026-11-09)

Reference: `TileEngine.cpp` (`getOriginVoxel`, `canTargetUnit` with `rememberObstacles`, `canTargetTile`, `adjustTargetVoxelFromTileType`,
`calculateParabolaVoxel` and its helper, `validateThrow`, `isTileInLOS`), `ProjectileFlyBState.cpp` (`init`'s range checks and target voxel,
`createNewProjectile`, `validThrowRange`, `getMaxThrowDistance`), `Projectile.cpp` (`calculateTrajectory`, `calculateThrow`, the classic `applyAccuracy`,
`getHitChance`, `getPositionFromEnd`), `BattleUnit.cpp` (`getFiringAccuracy`, `getAccuracyModifier`, `distance3dToPositionSq`),
`SavedBattleGame::canUseWeapon`, `RuleItem` (`isOutOfRange`, `isOutOfThrowRange`, `calculateLimits`), `BattleItem` (`getTotalWeight`, `getArcingShot`,
`getAmmoForAction`), `ActionMenuState` (the menu) and `Map::drawTerrain` (the classic accuracy of the cursor).

- **Origins and tile parts** (`BattleSight.Fire.cs`, the firing half of `BattleSight`). `OriginVoxel(BattleAimAction, tile)` is `getOriginVoxel` for every
  action (throw, launch from a waypoint, left and right origins; the realistic form is off). `CanTargetTile`, `AdjustTargetVoxelFromTileType` (object,
  north wall, west wall, floor, dummy), `CanTargetUnit` with the obstacle list (`BattleObstacle`, the part that stopped each line, for the player's scan only),
  `IsTileInLos` (the no line of sight penalty's `isTileInLOS`, with the secondary line of fire check and the smoke and fire quality of `visible`).
  `VisibleDistanceMaxVoxel` takes no target for it. A floor has no line of fire template in the tests' terrain, so aiming at one falls back to `(8, 8, 2)`.
- **Throwing.** `CalculateParabolaVoxel` (a chain of straight lines), `ValidateThrow` (up to nine curves from `max(0.48, 1.73 / sqrt(sqrt(strength / weight)))`,
  +0.1 kneeling; arcing shots use a fixed 70 and 10), `ValidThrowRange`, `MaxThrowDistance`, the object block on the target tile (an object with a walking cost of 255
  that is not a big wall of kind 4 or more), the underwater throw factor. `BattleState.TotalWeight`, `IsOutOfThrowRange`.
- **The projectile** (`BattleProjectile`): `CalculateTrajectory` (the first-shot line check as `LineOfFireBlocked`, which draws nothing; the launch's fixed accuracy
  and line extension), `CalculateThrow` (split in `FindArc`, which draws nothing, and the arc with deviation, up to 100 tries), `ApplyAccuracy` (the classic form with
  `shootingSpreadMode` 1: the uniform shift scaled by 0.839 and truncated, the deviation from a 0 to 100 draw, the circle redraws with 0.85 after the first, the
  vertical shift, the line extended 1000 tiles; the dropoff, the no line of sight penalty and the 46 voxel line of a hit). The draws come from `BattleState.Random` in the
  reference's order. Realistic accuracy and `battleAltGrenades` are off.
- **Accuracy and use** (`BattleWeaponUse.cs`). `FiringAccuracy`, `AccuracyModifier` (health and the wounds of the head and of the arm or arms that hold the weapon),
  `CalculateLimits` (the aimed range for every shot while `battleUFOExtenderAccuracy` is off), `IsOutOfRange` (a range of 1 or 2 reaches one diagonal), `HitChance`
  (the table lookup, odd accuracies averaged), `ArcingShot`, `AmmoForActionType`, and the whole `CanUseWeapon`: research (`BattleResearchGate`, handed over by the campaign:
  `ArcResearch` and the debug mode), the AI use delay, psi skill, the mana feature (`mana.enabled` and `mana.unlockResearch`, now in `CampaignCombatSettings`),
  water and land equipment by the weapon's and the action's ammunition, `STR_MUST_USE_BOTH_HANDS`.
- **The target voxel of a shot** (`BattleState.ChooseTargetVoxel`, `BattleAimQueries.cs`): `ProjectileFlyBState::init`'s choice for a shot that is not forced: the seen
  unit's `canTargetUnit` voxel, the shooter's own tile (the floor under it), a tile without a seen unit through `adjustTargetVoxelFromTileType`; a forced shot, a launch and a
  panicked unit's shot at the tile's middle (`targetFloor` for two waypoints on one tile); an arcing shot left to its arc. Off-centre shooting (`oxceEnableOffCentreShooting`)
  is off. S7 calls it from the shooting command.
- **Viewer queries** (`IBattleViewQuery.QueryAimMenu`, `QueryAim`; `BattleAimContracts.cs`). The menu lists what `ActionMenuState` offers for aiming (a throw for an item that
  is not fixed, the auto, snap and aimed shots or the launch, the hit) with the accuracy and the time units of each, and why the weapon cannot be used. The aim view gives what the
  cursor and the checks of `ProjectileFlyBState` show for a shot or a throw at a tile: whether the weapon is usable, the cost and what the unit lacks, the range, whether a line of
  fire exists (without drawing), the accuracy as the cursor works it out (dropoff, no line of sight penalty, 0 out of range) and the hit chance by the mod's tables. Both answer only for a
  unit of the viewer's side on its turn holding the item, and the aim view only for a tile the viewer has discovered; a launch and a hit are refused (their waypoints and range
  belong to S8 and S6).
- **Decisions** (ADR 0041, created here): a throw by a unit of no strength counts a strength of 1; a throw target off the map cannot land.
- **Left for the later slices.** The aim view lifts a throw's target one level above a tile with a terrain level of -24 (`ProjectileFlyBState::init`); S8's command must do the same before `calculateThrow`. The priming, medikit, scanner, psi amp and mind probe lines of the action menu (S8, branch 6). Close quarters combat, which `ProjectileFlyBState::init` runs
  before the target voxel (S7), and the time unit, ammunition and range checks that follow the turn (S7). `checkVoxelExposure` and the realistic accuracy stay off.
- **Tests.** `BattleLineOfFireTests`: the origins for each action, a wall between and a clear line, a unit half in cover, the obstacles remembered by the player's scan only,
  the target voxel order (unit, object, north and west wall, floor, the shooter's own tile), forced shots and launches, a throw over a low wall and a full wall (the curve) and under a
  ceiling, the blocking object on the target tile, an arcing shot, the parabola, the accuracy of a kneeling, wounded, two-handed and half-health shooter, the range limits and dropoff
  and the diagonal allowance of short ranges, the no line of sight penalty, the hit chance tables (function and mod), the uniform spread with scripted draws (and the redraw), the
  extended line, the first-shot line check, `canUseWeapon` for each refusal (the depth ones set the depth by reflection, as it is init-only), the menu and the aim view, and the query refusals (another side, off turn, undiscovered tile, item not held).

## Next

S3, damage to units (plan row S3). Notes for the worker:

- **What S2 left to build on.** `BattleState.FiringAccuracy`, `AccuracyModifier`, `CanUseWeapon(rules, BattleResearchGate, weapon, unit, berserking, type)`, `HitChance`,
  `IsOutOfRange`/`IsOutOfThrowRange`, `CalculateLimits`, `TotalWeight`, `ArcingShot` and `AmmoForActionType` (all in `BattleWeaponUse.cs`); `BattleAimAction` (the parts of `BattleAction` the
  line of fire reads, `BattleAim.cs`); `BattleProjectile` (`CalculateTrajectory`, `CalculateThrow`, `ApplyAccuracy`, `LineOfFireBlocked`, `FindArc`; it takes `BattleState.Random`);
  `BattleState.ChooseTargetVoxel` (`ProjectileFlyBState::init`'s target voxel, S7 calls it); `BattleSight.Fire.cs` (`OriginVoxel`, `CanTargetTile`, `AdjustTargetVoxelFromTileType`,
  `CalculateParabolaVoxel`, `ValidateThrow`, `ValidThrowRange`, `MaxThrowDistance`, `IsTileInLos`). `CanTargetUnit` and `ValidateThrow` take an optional `List<BattleObstacle>` for the no line of fire
  indicator. The hit's voxel and the part of the unit it landed on are still to come: `hitUnit` (S3) reads `relative` from the trajectory's last voxel (`Projectile::getPosition`,
  `BattleSight.PositionFromEnd`/`PositionFromStart` return a trajectory voxel).
- **Research and the campaign.** A battle has no campaign of its own: `CanUseWeapon` and the queries take a `BattleResearchGate` (`CampaignBattles.ResearchGate()` builds it from
  `ArcResearch` and `CampaignState.DebugModeEnabled`). A command that checks `canUseWeapon` (S6, S7, S8, S9) gets it the same way.
- **Stat bonuses.** `Combat.StatBonuses.Damage/Melee.Evaluate(unit, externalBonus)` (the power for `Damage`) are the power bonuses; `StrengthApplied` adds the owner's strength bonus (S1). The
  code forms are refused at the briefing (`battle.stat-bonus-script-unsupported`), so S3 never meets a script. `RuntimeItemDamageRule.RandomDamage(power, rules.Campaign.Interception, random)` and the
  `get*FinalDamage` conversions are S1's.
- **Tests.** `ScriptedDraws` (`tests/Shared`) returns scripted numbers inside each drawn range and records the calls. `BattleLineOfFireTests.Armed` builds the flat sight arena with a GUN and a
  GRENADE in the soldier's hands (`Edit(gun:, grenade:, global:, clip:)` adds item keys and root rules); `BattleSightTests.Flat` takes a `terrain:` action for extra MCD records. Item costs are
  percentages of the unit's statistics (`tuAimed: 30` is 30% of the time units).
- **Script refusal pattern.** `BattleState.CombatScriptRefusal` (`BattleCombatScripts.cs`) holds the parser names; a new hook name is added there.
- **Reason strings to update when their refusal changes owner or retires.** `BattleFirstTurn.cs` (`CasualtiesUnsupported`,
  `TurnOneReviveUnsupported`) and `BattleGravity.cs` (the fall knock-out) say branch 6 until S4 retires them; S4 also
  removes them from the refusal inventory.
- **Branch 4's notes still apply.** Commands run on a trial copy first (`CampaignBattles.TrialRefusal`), which needs
  terrain files. Branch 4's gotchas (`docs/battle-movement-status.md` "## Next") also apply: the post-edit hook
  reformats C# files, so read a file again before a second edit; add string literals with a backslash-n through the
  Edit tool; write test files with LF; run tests with `dotnet test --project <proj> --filter-class <full name>`; a
  test that adds units by hand needs a `SoldierId` or a unit rule. Record equality of a rule compares its lists by reference: compare their elements.
