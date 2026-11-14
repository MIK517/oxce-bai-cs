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
| S1 combat groundwork: item, armor and damage rules, stat bonuses, combat constants, script refusals | Done and audited (2026-11-08; findings closed in 0fd385db) |
| S2 line of fire and accuracy | Done and audited (2026-11-09; follow-up f5169fef) |
| S3 damage to units | Done and audited (2026-11-09; findings closed in 6de5d650) |
| S4 casualties | Done and audited (2026-11-09; follow-up cd111347) |
| S5 explosions | Done and audited (2026-11-09; follow-up 0df0b099) |
| S5b battle runtime (2026-11-14 audit follow-up) | Implemented (2026-11-14), audit findings closed (runtime, fault path, event sink, hook call sites, trial copy link; the rules parameters, the `Revision++` sites and `BattleOptions` are S5c) |
| S5c runtime remainder: `rules` parameters, `Revision++` sites, `BattleOptions` | Not started |
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
| A crashed UFO's power sources (`BattleGenerator.CrashedPowerSources`) and map grenades primed at -2 (`PrimedJunk`), generation errors | branch 1 | S5 (done) |
| `battle.hit-unit-unsupported` (retired by S3), `battle.terrain-explosion-unsupported`, `battle.module-map-unsupported` | branch 4 S9 | S3 (done), S5 (done), S5 (done; ADR 0041) |
| `battle.proximity-unsupported` (and the broad death-trap test, branch 4 audit L3) | branch 4 S7 | S8 |
| `battle.reaction-fire-unsupported` (`HasPossibleReactor`, the named leak) and the acceptance's `WithOpponentsSpent` | branch 4 (S5, S7, S8, S11) | S9 |
| `battle.casualties-unsupported` (`HasUncheckedCasualty`, the floorless-walker gate) | branch 3 S3, branch 4 S7 (owner was branch 6) | S4 (done) |
| `battle.fall-knockout-unsupported` | branch 4 S7 (owner was branch 6) | S4 (done) |
| `battle.turn-one-revive-unsupported` | branch 3 S3 (owner was branch 6) | S4 (done) |
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

`battle.self-destruct-item-unsupported` (S5; a melee weapon as a self-destruct item; retired by S6) is the one refusal S5 adds.

Interim refusals this branch adds between its own slices (plan, "Interim refusals between slices"): from S3 until S4
a hit that takes a unit out or infects it kept `battle.casualties-unsupported` (S4 retired it); from S3 until S5 a self-destruct
kept `battle.terrain-explosion-unsupported` (S5 retired it); from S6, S7 and S8 until S9 the attack's reaction check keeps
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

## S3 — Damage to units (2026-11-09)

Reference: `BattleUnit.cpp` (`damage`, `reduceByResistance`, `reduceByBravery`, `moraleChange`, `isWoundable`, `isOutThresholdExceed`, `getOverKillDamage`, `setFire`, the
experience adders, the murderer fields), `TileEngine.cpp` (`hitUnit`, `hit`, `awardExperience`), `SavedBattleGame::getFactionMoraleModifier`,
`getHighestRankedXCom`, `AIModule::setWasHitBy`, `RuleItem::getZombieUnit`, `Armor` (the immunity getters and `getOverKill`).

- **`BattleAttack`** (`BattleActionAttack` without the skill rule, which branch 6 ports): the action type, the attacker, the weapon and the item whose damage it is.
- **`DamageUnit`** (`BattleDamage.cs`, `BattleUnit::damage`). The side from the hit's position against the facing (`IgnoreDirection`; a diagonal rolls between two sides; a hit from
  below or at the feet is the side under), the body part from the height against the unit's standing or kneeling height (head, arm of the side, or a leg with a roll for the front and
  rear), the armor's resistance (rounded down) and overkill floor (`IgnoreOverKill`), then, for a positive damage: the armor's pre damage, the armor of the side less `ArmorIgnore`
  at `ArmorEffectiveness` (truncated from a float), and the conversions to stun, morale (reduced by bravery), time, health, mana, energy, wounds and armor in the reference's draw order. Stun
  needs a unit that feels pain or a type that ignores it, wounds a woundable unit (bleed immune by default unless it is a soldier), and the wounds cost morale. The hit stores the
  fatal shot's side and part. Then the special effects: the morale loss from the health damage (the player's units over the highest ranked living X-COM unit's modifier), the fire the
  damage lights (`FireThreshold`, rolled up to five times the armor's modifier, kept if longer, put out by a weapon or ammunition that is a fire extinguisher, ignored by units that burn
  floors), the ammunition's infection (`getZombieUnit` by armor and gender, then type; the chance, its action override, none for a hostile victim of a hostile attacker, the size check of
  the unit it becomes; `Respawn`, `SpawnUnit` and `SpawnUnitFaction`), the self-destruct, and the AI direct hit tracking: the attacker's turns since spotted by the victim's faction (0
  after a direct shot of another side), the AI's `wasHitBy`, and the sniper turns under `extendedSpotOnHitForSniping` (1 only if the victim stays up, 2 always) as the victim's spotter
  duration. `BattleUnit.HitState` is set; a unit with no health takes nothing.
- **`HitUnit`** (`hitUnit`). Damage, then experience for an X-COM attacker, then the murderer (id, weapon and ammunition names, `STR_WEAPON_UNKNOWN`), kept when the hit did no harm to a
  victim that burns or has wounds. **`AwardExperience`**: the mana experience first, the weapon's `experienceTrainingMode` (1 to 32; the psi modes need a soldier with psi skill above 0, from
  `BattleUnit.SoldierPsiSkill`), or by kind: grenades and proximity grenades (firing, throwing in the extended system), melee, medikits (nothing), a gun butt (melee), a firearm (firing,
  or in the extended system arcing: throwing, by range: firing above 10, throwing above 1, melee at 1). Only hostile units by birth train (and hostile now in the extended system).
- **`Hit`** (`TileEngine::hit`, `BattleFire.cs`; `HitTerrain` is the no-weapon form). A floor or an object hit passes to the bodies on the tile (unconscious units' items), the first one
  alive takes it at relative `(0, 0, 0)` and spares the terrain (a glowing body makes the effect 2); a unit in the voxel is hit at the voxel against its bottom center (the center of its
  size, its float height and the hit tile's terrain level, 24 voxels lower when it stands on the tile below); fire left on the unit makes the effect 2, which updates gravity, light and sight.
  A dry run still asks the terrain's refusals only: a live body on the tile answers `null` at once, since the hit goes to it.
- **Interim refusals** (plan, "Interim refusals between slices"). The unit has taken the hit when a refusal comes back, so a command runs on the trial copy first (ADR 0040, as walks do):
  a hit that leaves a unit past the out threshold (health to 0, or stun reaching it) or sets it to respawn is `battle.casualties-unsupported` (S4), one that would push the self-destruct
  explosion is `battle.terrain-explosion-unsupported` (S5; `AlreadyExploded` is set). `battle.hit-unit-unsupported` is gone. A walk or fall over a floor whose burn reaches a unit now runs
  and refuses only if the burn takes it out (the old dry run refused any live body). Callers with no trial copy (the inventory's falls through `FallsRefusal`) still refuse a burn that would reach a live unit, with `battle.casualties-unsupported` (`HitWouldReachUnit`), since the damage rolled decides. A body that stays unconscious with its health is no casualty (`NeedsCasualtyCheck`).
- **Not ported here.** The hit log (`appendToHitLog`) is runtime text for the next turn's screen. The statistics counters (`hitCounter`, `shotsLandedCounter`, `shotByFriendlyCounter`...) belong
  to `ProjectileFlyBState::projectileHitUnit` (S7). The armor and soldier health recovery bonuses that `hasNegativeHealthRegen` reads (the murderer rule's third term) are branch 6's with the
  other recovery bonuses; it counts as false. The combat hooks (`hitUnit`, `damageUnit`, `damageSpecialUnit`, `awardExperience`) are refused at the briefing (S1).
- **Decisions** (ADR 0041): the reference draws the leg roll, burn time and the infection and self-destruct percents from a sub-sequence of the game generator (`subSequence()`, one global draw per
  hit on a living unit), and the side rolls from the global one; the port uses the battle's generator for all, since stream parity is out of scope. Armor and soldier recovery bonuses (a negative health
  recovery changes the murderer rule) are not read at all yet, so such a mod differs there. `Options::alienBleeding` is fixed off. A zombie unit that does not exist, or whose armor is bigger than the victim's, converts nothing (the reference logs the second and
  would dereference a missing unit rule on the first).
- **Left for the later slices.** `terrainMeleeTilePart` of `hit` (S6). `BattleUnit.Gender` and `SoldierPsiSkill` are set for soldiers only (`SoldierUnit`) and are not saved (a load takes them
  from the campaign soldier). `hitUnit`'s `relative` is each caller's: S5's `explode` passes `(0,0,0)`, `(0,0,-1)` or the direction to the explosion; S7's `projectileHitUnit` calls `Hit`.
- **Tests.** `BattleDamageTests` (44): the side and body part for each facing, hit direction and height, the diagonal and leg rolls, `IgnoreDirection` and the environment's overrides, kneeling;
  armor reduction, `ArmorIgnore`, effectiveness, the floor at zero, the pre damage and the maximum; the resistance table; every conversion and the random draw order; a spent shot; pain immunity
  and the stun cap; overkill and `IgnoreOverKill`; wounds (the unit that bleeds, the random types) and morale (bravery, the leader's modifier ladder, `IgnoreNormalMoraleLose`, fear immunity,
  the morale conversion); fire (threshold, longer fire kept, resistance, modifier two, the extinguisher in weapon and ammunition, burners); infection (chance, the action override, immune armors,
  hostile on hostile, an existing spawn unit, the size check, by armor and gender and by type, the faction); the attacker's spotting (direct shots only, throws, own side, explosive shells),
  `wasHitBy`, the three sniper options; the self-destruct and casualty refusals; `HitUnit` and the murderer; experience (kinds, the extended system, friends, modes, fractions, either-or
  draws, psi and mana); `Hit` on a unit in the voxel, a big unit, a unit on the tile below, a body on the floor (alive, dead, dry run), a lethal hit and a flame. `BattleFireTests` follow the
  new behaviour for a live body, a unit in the voxel and the walk, fall and burner cases.

## S4 — Casualties (2026-11-09)

Reference: `BattlescapeGame.cpp` (`checkForCasualties`, `convertUnit`, `convertInfected`, `dropItem`), `UnitDieBState.cpp` (constructor, `init`, `think`,
`convertUnitToCorpse`), `SavedBattleGame.cpp` (`convertUnit`, `reviveUnconsciousUnits`, `removeUnconsciousBodyItem`, `placeUnitNearPosition`, `setUnitPosition`,
`clearUnitSelection`, `startFirstTurn`, `newTurnUpdateScripts`, `getFactionMoraleModifier`, `getUnitMoraleModifier`, `getMoraleLossModifierWhenKilled`, `createItemForTile`),
`BattleUnit.cpp` (`knockOut`, `instaKill`, `instaFalling`, `startFalling`, `keepFalling`, `setTile`), `BattleItem::convertToCorpse`, `BattleUnitStatistics.h`
(`BattleUnitKills`), `TileEngine.cpp` (`itemDrop`, `itemDropInventory`, `applyGravity`), `SavedGame::killSoldier`, `Soldier::die`, `UnitFallBState.cpp` (the stomp).

- **`CheckForCasualties`** (`BattleCasualties.cs`, `BattlescapeGame::checkForCasualties`; `QueueCasualties` is its loop). For each unit that is not ignored: a kill statistic (the victim's name or type, rank and race
  as `BattleUnitKills::setUnitStats` names them, the mission count, the turn `turn * 3 + side`, the victim's original faction, fatal side and body part, the weapon and ammunition: the attack's, `"__GUNBUTT"` for a firearm's butt,
  `STR_WEAPON_UNKNOWN`, or the victim's recorded murderer weapon when the attack names no attacker and the explosion is no terrain explosion); the murderer (the attacker, or the one who killed an exploding attacker, or
  the last unit to hit); the credit (a mind controlled unit's goes to its controller with `slaveKills`; not for a cosmetic victim; never twice for the same victim and status); then for a unit with no health the murderer's kill count and
  `killedBy`, the morale (+20 for killing the other side, -2000 / modifier for friendly fire, -1000 for X-COM killing a civilian or +10 otherwise, the losing squad's loss by the unit modifier, the killed unit's `moraleLossWhenKilled` and bravery,
  the winners' +10, and the murderer exposed to the aliens), the state (below) and the soldier's memorial; for a stunned unit the `stunningImprovesMorale` bonus (once: `wasUnconcious`) and a state; for any other a unit without floor
  queues to fall. The states run in the queue's order: into an empty queue the first runs first and the rest go behind it last first, behind a running state (`statesQueued`: an explosion, a fall) last first.
- **`UnitDieBState`** collapsed to its outcome (`BeginDeath` and `RunDeath`). The constructor: a unit hit by a type that is not direct, or already unconscious, faces direction 3 and is dead or unconscious at once (`InstaFalling`);
  before the game starts it becomes its corpse there and then and nothing else runs; its sight is cleared and its AI target node freed (`freePatrolTarget`); a dying hostile marks the route nodes within a squared distance under 4 of the unit's position (tile or not) `TYPE_DANGEROUS` (saved). The think: a unit hit directly turns to direction 3, falls to dead (no health)
  or unconscious; an unconscious unit that cannot be captured is `instaKill`ed; the spotting turns, tiles last spotted and sniper turns reset; a unit with a spawn unit that did not respawn is converted (nothing if it respawned already; overkill
  converts nothing), else it becomes its corpse; the selected and undo units are cleared; light and sight update at the place; an X-COM unit's notice (`NotificationShown` 2 for a death by wounds with no spawn unit, 1 for unconscious).
  `init` skips a unit that is on no tile (any side but X-COM's, and an X-COM unit with its notice at 2). The sounds, the infoboxes and `autoEndBattle` (off) are not ported.
- **Corpses.** `ConvertToCorpse`: the body item of a unit that lies on the map is removed, the inventory goes to the ground (`ItemDropInventory`: fixed items stay, a fixed item is deleted with `deleteFixedItems` after its ammunition is
  dropped; special weapons go with it), the unit leaves its tile (`SavedPosition` keeps where, `Position` is the tile link), one corpse item per tile part is dropped through `ItemDrop` (`itemDrop`: whoever stands there learns the tile, the item lies on the
  ground, gravity, light and sight of the tile and the item's glow range), or with overkill only gravity runs; a carried unit's body item takes the corpse of the armor, or leaves with overkill. `BattleItem.ItemType` and `BattleNode.Type` became settable.
- **Soldiers.** `IBattleCampaignLink` (set by `CampaignBattles` on the live battle only, not on trial copies) gives the battle the mission count, the difficulty, a soldier's name and rank string and the memorial. `SavedGame::killSoldier(false, ...)`:
  `CampaignState.KillSoldierInBattle` moves the soldier to the memorial with `DeadSoldierSnapshot.Cause` and the armor it fought in; the save adapter writes and reads `death.cause`. The cause is the murderer's description with the victim's side, body part and id.
  `RuntimeSoldierRule.RankStrings` and `RuntimeBattleUnitRule.Cosmetic` are new projections.
- **Conversion.** `ConvertUnit` (`SavedBattleGame::convertUnit`): the unit respawns once, its body item goes, it is `InstaKill`ed, its inventory (fixed items too) goes to the ground, and a unit of its spawn type and `spawnUnitFaction` takes the place, facing as it did, no time units,
  visible as the old one was, with its equipment (`InitUnit`), field of view recalculated, gravity, `DontReselect`; nothing for a carried unit or a place another unit holds. `KnockOut` (a unit with a spawn unit converts first, the new unit's spawn unit is cleared and it is stunned; else the stun
  reaches the health), `ConvertInfected` (live units with the respawn flag convert). `ReviveUnconsciousUnits` (small units, over the out threshold no more: placed where they lie, or by `PlaceUnitNearPosition` around the place, a large friend on the tile pushes the ring out to 2, a flier may take the tile above;
  they stand, lose their time units or recover as a new turn does, cut by `tuRecoveryWakeUpNewTurn`; the body item goes).
- **Where it runs.** `OpenBattlescape` (`BattlescapeGame` constructor, hidden explosion): at the briefing's confirmation before anything else, and when a loaded battle under way is restored (then the falls it queues run if the dry run finds nothing to refuse;
  else the units stay queued and the commands wait with the dry run's refusal). `StartFirstTurn` revives and converts after the turn becomes 1 (`newTurnUpdateScripts`). A fall that finds no escape for the unit below knocks it out (`KnockOut`) and the casualty check follows the round
  (`UnitFallBState`: after the units to move are dealt with, non-dry runs only), its states run when the falls are done. No walk or burn checks casualties by itself (the reference does not): a unit the burn takes to 0 health stays standing until the next check.
- **Retired.** `battle.casualties-unsupported` (`HasUncheckedCasualty`, the floorless-walker gate, the hit refusal, the dry run's `HitWouldReachUnit` which now refuses with `battle.terrain-explosion-unsupported` only for a reached unit that blows up with its death),
  `battle.fall-knockout-unsupported` and `battle.turn-one-revive-unsupported`. `FirstTurnRefusal` now also counts a waking unit's recovery scripts. Interim refusals left: `battle.terrain-explosion-unsupported` (S5), `battle.reaction-fire-unsupported` and `battle.proximity-unsupported`.
- **Decisions** (ADR 0041): a corpse the armor does not list, a spawn unit the mod lacks or that does not fit, the memorial's cause and armor, and the position of a unit that is out.
- **Left for the later slices.** `lastSelectedUnit` (the port keeps no such state). `battleNotifyDeath` and the sounds and infoboxes. `itemDrop`'s danger zone for a primed grenade (AI state, branch 6);
  a flare's glow range for an unlit flare (S8). `appendToHitLog`. The explosion of a unit that blows up with its death (S5). The reaction fire after a death (S9). Casualty checks after walks that burn or after the end of a turn (branch 6). The kill statistics' `KIA` is not saved (the debriefing, branch 7, reads it).
- **Tests.** `BattleCasualtyTests` (33): the kill record, the murderer and the morale for an enemy, a friend and a civilian, a leader's modifier, a stun with and without `stunningImprovesMorale`, a murderer from the last hit, the weapon names and a mind controlled
  unit's kill, a cosmetic victim, the fall to direction 3 with the inventory, notices and selection, a death by explosion, a unit that cannot be captured, a 2x2 corpse and overkill, a corpse falling through a hole, a carried unit dying, the dangerous nodes (and saved), the memorial and its
  reload through the save adapter, an infected unit's conversion (once, faction, a missing unit), `ConvertInfected` and `KnockOut`, revival (in place, around a large friend, with recovery, still stunned, no floor), the battlescape's constructor before the game and for a loaded battle, the queue order and the falls a check queues.
  The knock-out of a fall is tested in `BattleGravityTests` and `BattleWalkTests`; `BattleFirstTurnTests` cover the briefing, the first turn's revival and the loaded battle; `BattleDamageTests` and `BattleFireTests` follow the retired refusals.

## S5 — Explosions (2026-11-09)

Reference: `ExplosionBState.cpp` (`init`, `think`, `explode`), `TileEngine.cpp` (`explode`, `detonate`, `checkForTerrainExplosions`, `hit`), `BattlescapeGame.cpp` (`statePushFront`, `statePushNext`,
`popState`, `spawnNewUnit`, `spawnNewItem`), `BattleUnit.cpp` (the self-destruct in `damage`), `BattlescapeGenerator.cpp` (`explodePowerSources`, `explodeOtherJunk`), `RuleItem.cpp` (`getExplosionRadius`, `getPowerBonus`).

- **The list of states.** The reference's `_states` (`statePushFront`, `statePushNext`, `popState`) is one ordered list in `BattleExplosions.cs` (`QueuedState`, `PushFront`, `PushNext`, `PopState`), with the animations
  collapsed: a state's `init` runs when it is pushed to the front, pushed into an empty list, or becomes the front; its `think` when `RunStates` reaches it, and it pops itself. `RunStates` returns the casualties of the deaths that ran and does
  nothing when called from inside a running state. The list is empty between commands and is not saved. The commands that can push a state run it at their end: `Walk` and `MoveUpDown` (inside `RunWalk`, before its falls) and the falls (after their deaths).
  `QueueCasualties` takes a `pushNext` callback, so the deaths of an explosion go into this list one by one (`DeathState`: `UnitDieBState`, whose `init` may skip the unit and whose `think` is `RunDeath`).
- **`ExplosionBState`** (`ExplosionState`). `PushExplosion(rules, attack, center, before, lowerWeapon, range)` is the `statePushFront` the shots, throws and hits will use. `init`: the power
  (the weapon's when it ignores the ammunition's power, else the damage item's; plus the damage bonus of the attacker; less `PowerRangeReductionAt(range)` in single precision), the radius (`getExplosionRadius`: the type's fixed one, or from the
  power bonus times the radius effectiveness, plus one for a fire blast, kept in 1 to 11), the damage type; a self-destruct draws `specialChance` and sets the power to 0 on failure; a firearm item with no power misses; a terrain explosion takes the tile's strength and
  clears it, with the damage type its explosive type names (0 high explosive, 5 incendiary, 6 stun, anything else smoke) and a radius of a tenth of it; with neither an item nor a tile the blast is 120 high explosive. An area blast with a power runs `Explode`; one with none pops at
  once; anything else is a bullet hit (`Hit` at the center, with the weapon's psi test for the `rangeAttack` flag). A self-destruct clears the attacker's `AlreadyExploded` at the end of `init`, as the reference does (so with an empty list the flag is clear again at once).
  `think` is `explode()`: the casualty check (`terrainExplosion` for a tile blast or one with no item), `ReviveUnconsciousUnits(noTU)`, `ConvertInfected`, the attacker puts the gun down (`aim(false)`: status standing) when `lowerWeapon`, a grenade or proximity grenade item leaves the game,
  the state pops, `checkForTerrainExplosions` pushes the next terrain blast at the front (counter + 1; its attack keeps the attacker), and the item's unit and item spawn at the tile before the impact.
  The animation, sound and camera work, `getPowerForAnimation` and the animation draws are presentation and draw nothing. Each blast raises a `BattleExploded` (center, power, radius, whether a tile set it off) through the event sink (S5b; it was `ExplosionLog`).
- **`Explode`** (`TileEngine::explode`). Rays every 5 degrees of elevation and 3 of azimuth lose the type's radius reduction per tile (single precision), a flat vertical decrement of 1000 (`battleExplosionHeight` 0), half a reduction more for a
  fire blast's diagonal, and twice the vertical and horizontal blockage between tiles (the damage block value for the high explosive and the like, the smoke one for smoke, the vision one for `DT_NONE`; the bigwall deflection of the first tile). A tile takes its damage
  roll the first time any ray reaches it: the unit on it (direct at the center, ground zero within a squared distance under 4 on the same level or from above, else from the side), what it carries (items destroyed by the item damage over their armor, bodies hit),
  the items on the ground, then `HitTile`'s fire or smoke; it keeps the highest tile damage of any ray. Types with `ToTile` above 0 then `Detonate` each tile reached, in tile index order (the reference's map orders by tile pointer), with gravity on the tile and the one above;
  lighting and sight are updated around the center and, for an attacker away from the blast, around the center once more.
- **`Detonate`** (`TileEngine::detonate`): the nine parts (the ceiling floor, the east and south neighbours' walls, the tile's own floor, walls and object, the north and west neighbours' bigwalls) lose their armor from the explosive in the reference's order and with its bigwall flags;
  a destroyed part with a dead form carries on through the form; a base module destroyed counts against its block (`CountModuleLost`); what power is left over a flammable tile sets it alight; a destroyed part makes smoke (1 to volume / 2 + 3 plus volume / 2, kept when it beats the tile's).
  `DestroyPart` already left the explosive of an exploding part on the tile (`Tile::setExplosive`): that is what `CheckForTerrainExplosions` finds and `RunTerrainExplosions` (the end of a turn: branch 6) and `ExplosionState.Think` start.
- **Self-destruct.** `DamageUnit` no longer refuses: a unit that explodes sets `AlreadyExploded` and pushes an `ExplosionState` (the special weapon, at the corner voxel of its tile) with `PushNext`; into an empty list it starts at once. Only a self-destruct item that is a melee weapon or a psi amplifier is refused
  (`battle.self-destruct-item-unsupported`, S6), after the hit; the refusal is also left in `ExplosionRefusal` (`TakeExplosionRefusal`), which `Walk`, `MoveUpDown`, the falls and the generator return or throw. A psi amplifier as the item is a plain hit (area test false), as the reference.
  The refusal is not in the visibility check: it needs a melee self-destruct item, which no mod in the corpus has.
- **Spawns.** `SpawnNewUnit` and `SpawnNewItem` (`spawnNewUnit(attack, position)` and `spawnNewItem(attack, position)`): the chance (the weapon's action `ammoSpawn*ChanceOverride` first), the owner (attacker, else item owner, else previous owner), the faction, a free place
  (checked from the attacker's opposite direction), the attacker's facing or a random one, the tank's weapon, equipment, gravity and a full recalculation of sight for the unit; the item lies on the tile with its previous owner, falls, and a glowing one lights its surroundings. The first turn's `SpawnNewUnit`
  (a primed item before the game) stays as it was: it also calls the unit's sight in the reference but nothing is seen before the first turn.
- **Generation.** `ExplodePowerSources` (each power source of a crashed UFO at 75 percent: a 180 to 250 high explosive of radius 10; then each terrain blast as a high explosive of the strength it holds) and `ExplodeOtherJunk` (grenades on the map with fuse -2 that spawn nothing, are not X-COM's and are not special weapons; the owner-ignored test is not ported are removed and explode at their power and type;
  then each terrain blast by the type its explosive names) run after the lighting of the finished map, as the reference's `run` ends. The casualties are the battlescape's opening check (`OpenBattlescape`), except those of a bomber's explosion, whose `init` goes off nested in the ray loop and whose deaths run at the end of the routine. The battle's random source is set to the campaign's for them.
  `BattleGenerator.CrashedPowerSources` and `PrimedJunk` are gone.
- **Retired.** `battle.terrain-explosion-unsupported` (the hit, the burn of a floor by a walker or a faller, the fire's spread and the self-destruct) and `battle.module-map-unsupported`. `HitWouldReachUnit`, `FireSpreadRefusal` and the `dryRun` of `Hit` and `BurnFloor` are gone; `SpreadFireAndSmoke` is void.
  A burn or hit that destroys an exploding part leaves its explosive on the tile: the reference starts the blast from `ExplosionBState` or the end of a turn only, so a walk's burn leaves it until then.
- **Decisions** (ADR 0041): a module destroyed in a block the saved module map lacks is destroyed without the count (and ADR 0039's must-refuse line now says so for destruction); the generator's explosions run their self-destruct and death states at the end of each routine (the reference would use a battlescape that does not exist yet); terrain
  detonation order is the tile index.
- **Left for the later slices.** The melee and psi `ExplosionBState` (`_hit`, `BT_MELEE`, `BT_PSIAMP`; `ExplosionState.Init` throws for them; S6 and branch 6). `terrainMeleeTilePart` (S6). The reaction check after an explosion (S9). The end of a turn's call of `RunTerrainExplosions`,
  after `SpreadFireAndSmoke` (branch 6). The commands that return the blasts' events arrive with S6 to S8 (`BattleExploded` is in the sink). `fuseTimeEvent` grenades exploding at the end of a turn (branch 6).
- **Tests.** `BattleExplosionTests` (39): the falloff and the radius, the radius reduction, a fire blast, a wall stopping the blast and a door (closed, open), a blast off the map or without power, items destroyed by `ToItem`, a body on the ground, a grenade's power, radius,
  kills and removal, a blast with no power, a bullet hit and the shooter's gun put down, a weapon's power with the range reduction, a missing bullet, a chain of three terrain explosions, the explosive types, a terrain blast's casualties, walls broken
  by the explosive, an objective and smoke, a dead form and fire, a base module (counted, and in a block the map lacks), a unit that blows itself up (power, radius, chance, a melee item, a blast that kills it), spawns (unit and item, chance and override), the power sources, the opening check, and map junk.
  The retired refusals' tests in `BattleFireTests`, `BattleDamageTests`, `BattleCasualtyTests` and `BattleStartTests` follow the new behaviour. Terrain records 35 to 38 (`BlastWall`, `BlastDoor`, `ThinWall`, `Powder`) join the sight tests' set.

## S5b — Battle runtime (2026-11-14)

Plan row S5b (the 2026-11-14 [audit follow-up](phase-7-plan.md#audit-follow-up-2026-11-14): A1, A2, A3, A4, C1, C2). No rule outcome, refusal text or save output changed: the 936 battle tests of the compatibility project passed before the new ones were added.

- **`BattleRuntime`** (`BattleRuntime.cs`). Holds the state, the rules, the campaign's stream, the campaign link, the hooks and the sink. `CampaignBattles.SetBattle` makes it when a battle is generated (`Start`) or restored, and the runtime sets `Random`, `CampaignLink` and `Hooks` on the
  state once: the seven `battle.Random = campaign.Random` assignments are gone (the generator's own `state.Random = random` stays, it is the same stream). The battle's own commands (inventory move and auto-place, `UseInventoryFeature`, selection, skip, reselect, spotter,
  turn, kneel, walk, up and down) are registered in the runtime and `CampaignBattles` forwards each through `Forward` (one line a command, the refusal for no battle unchanged). `CampaignBattles` keeps `Start`, `Restore`, the briefing and first-turn commands (they need the campaign's
  storage, layouts and the trial), the command gate, the queries and the hand-over. `BeginBriefing`, `ConfirmBriefing` and `StartFirstTurn` are still campaign-side handlers of the pre-battle flow; they run through `BattleRuntime.Guard` (`CampaignBattles.Guarded`), so a faulted battle refuses them and an exception in them faults the battle. The first-turn entry points still thread `campaign.Random` and the difficulty to `SpawnFromPrimedItems` and `StartFirstTurn` by hand (the same stream, no outcome differs); S5c removes it.
- **The fault path (A4).** `BattleRuntime.Execute` runs a command with an empty sink; a handler that throws sets `Fault` (the exception's type and message) and returns `battle.faulted`, and every later command is refused with it. A command type the runtime does not know is `battle.command-unknown`, not an
  exception, and does not fault; `CampaignState.Execute` gives the same refusal to a battle command (`IBattleCommand`) that no handler takes, and still throws for any other unknown command. A faulted battle also refuses to be saved (`registry.SaveGate`), so the host keeps the last save (ADR 0038). Handlers that used to throw to the caller (tests that expected an exception out of a battle command) now fault the battle; none of the existing tests did.
- **The event sink (C1).** `BattleEventSink` (`BattleEvents.cs`) is `BattleState.Events`. `ExplosionLog` and `BattleExplosionRecord` are gone: each blast raises `BattleExploded`. New events: `UnitHit` (after `HitUnit`'s damage, before the experience), `UnitDied` and `UnitKnockedOut` (`checkForCasualties`, after the death state's
  constructor), `UnitConverted`, `TileIgnited` (`Tile::ignite`, and a hit or a blast that sets a tile alight through `SetTileAlight`), `UnitIgnited` (a unit that was not burning catches fire in `DamageUnit`). `Walk` and `MoveUpDown` lost their `events` parameter and `ResolveFalls(rules, out moved)` raises `UnitFell` into the sink; a walk's result now holds what the rules raised
  during it (a hit, a blast) in order with its own events. A command's result is its own events followed by the sink's; a refusal returns its reason alone. A handler whose own event must follow the rules' events raises it through the sink. What is raised outside a runtime command (the generator's explosions, the battlescape's opening falls) stays in the sink until the next command empties it, and no command returns it.
- **Hooks (C2).** `IBattleHooks` (`BattleHooks.cs`) has a default-method per script: `HitUnit`, `DamageUnit`, `DamageSpecialUnit` (in `DamageUnit`, in the reference's places: after the side and body part, after the stat deltas, before the special effects are applied), `AwardExperience` (after the multiplier,
  with the effective training mode), `VisibilityUnit` (both visibility checks: `Visible` for a unit and the tile check, with the target unit or none) and `StatBonus` (the damage bonus of an item in `PowerBonus` and the glow range, and the accuracy, melee, throw and close quarters multipliers of `FiringAccuracy`).
  `DefaultBattleHooks.Instance` returns every value unchanged. `PowerBonus` and `ExplosionRadius` became instance methods to reach the hooks. The script refusals (`battle.combat-script-unsupported` and the others) stay: nothing binds a hook yet (branch 6 for the first family, branch 8 for the rest). The special damage script's `Transform` and `SelfDestruct` outputs are never read back, as in the reference (only the chances count); the hit script's side is reset to the front at 5, not at the reference's `SIDE_MAX` of 9 (ADR 0041).
- **Special effects wait for the battlescape (audit H2).** `BattleUnit::damage` skips its special effects (morale, fire, conversion, self-destruct, the attacker's spotting and the `DamageSpecialUnit` hook) when the saved game has no battlescape state, which is so during the generator's `explodePowerSources` and `explodeOtherJunk`. `BattleState.Generating` is true while they run and `DamageUnit` returns before the special block; ADR 0041's "Explosions before the battlescape exists" is corrected.
- **The trial copy (A3).** `CampaignBattles.TrialRefusal` stays until S11 and now hands the copy the live stream, hooks and a link that reads what the live link reads (difficulty, mission count, soldier names) but whose `KillSoldier` does nothing, so a death in a trial cannot reach the memorial.
- **Left out of S5b, with owners.**
  - The `rules` parameter of the battle methods (about a thousand call sites), the `Revision++` sites (38) and `BattleOptions` are not behind the runtime: the runtime supplies the rules to the commands it runs, and a rule that needs them still takes them. The plan names this S5c (a mechanical slice before S6, plan row S5c and the A1/A2 row); `BattleOptions` has no changeable option yet.
  - The fourth bullet of audit A4 (`GeoscapeClient.Tick` catching host failures itself) is the geoscape clock's, not a battle command's: the plan moved it to branch 6 B3.
  - The hooks for `tryMeleeAttackItem`/`tryMeleeAttackUnit` and the `meleeBonus` and `meleeDodge` bonuses (S6), `reactionWeaponAction`, `reactionUnitAction` and `reactionUnitReaction` (S9) and `psiDefence` (branch 6) are not in the interface: their functions arrive with those slices, which add each with its call site.
  - The `CreateUnit` and `CreateItem` scripts (`SavedBattleGame::initUnit` and `initItem`) have no hook at the unit and item creations of S4/S5 (`ConvertUnit`, explosion spawns, corpse items); branch 8 binds them with the generator's (plan C2 row).
- **Tests.** `BattleRuntimeTests` (24): the runtime sets the stream, link and hooks once (and a campaign-started battle draws from the campaign's stream with its difficulty); a trial copy reads the live link and cannot reach the memorial; a throwing handler faults the battle and later commands are refused unchanged;
  an unknown command is refused and does not fault; a command returns the blast, the hit and the death in order from an empty sink; a refusal returns its reason alone; a tile and a unit set alight raise their events; the default hooks leave a blast's outcome unchanged while a recording hook sees the hit, damage, special, experience and
  damage bonus calls in the reference's order; a hit hook that sets the damage to 0 saves the victim; the visibility and accuracy hooks are called, and a visibility hook that sees nothing hides a unit in plain view. Audit closing tests: the hooks' return values take effect (damage, special morale and fire, experience, the accuracy of every action, the blast's power, the tile visibility, the glow in `BattleLightingTests`); the special hook's `Transform` is not read back; a generation blast runs no special effects or hook; an incendiary blast and a hit raise `TileIgnited` and a burning unit does not ignite again; `UnitKnockedOut` and `UnitConverted`; a fault through the campaign refuses the pre-battle commands and the save; an exception in `StartFirstTurn` faults the battle; a battle command no handler takes is a refusal through `CampaignState.Execute`; a restored battle draws from the campaign's stream; a trial copy of a walk runs with the live hooks. `BattleExplosionTests`, `BattleStartTests` and `BattleWalkTests` read blasts from the sink and call `Walk` without a list.

## Next

S5c first (plan row S5c, a mechanical slice): the state holds the `Rules` the runtime sets, so the battle methods stop taking `rules` (about a thousand call sites); the 38 `Revision++` sites become one bump per command in the runtime; `BattleOptions` is read from the runtime; the first-turn entry points
stop threading `campaign.Random` and the difficulty. No rule outcome, refusal text or save output changes. Then S6, melee and the attack command (plan row S6). S6–S9 raise their events through the sink (`battle.Events.Add(...)`; a command registered in `BattleRuntime.RegisterCommands` returns them, and one whose
own event must follow the rules' raises it through the sink too) and call the hook of each script the reference runs in the functions they port (add the method to `IBattleHooks` with its call site): S6 owes `tryMeleeAttackItem`, `tryMeleeAttackUnit`, `meleeBonus` and `meleeDodge`; S9 `reactionWeaponAction`,
`reactionUnitAction` and `reactionUnitReaction`; branch 6 `psiDefence`; branch 8 `CreateUnit` and `CreateItem`. Notes for the worker:

- **S6 registers its commands in `BattleRuntime.RegisterCommands`, where an exception faults the battle.** `ExplosionState` throws `NotSupportedException` for a melee or psi hit until S6 ports it, so a melee command registered before that would fault the battle. `UnitIgnited`, `UnitHit` and the other events name ids, and `UnitHit.AttackerId` names the attacker even when the viewer has not spotted it: branch 6's per-viewer filtering (B2) handles that before a player sees it.

- **Falls after the list.** The reference pushes `UnitFallBState` once the list is empty (`BattlescapeGame::think`), so a command ends with `RunStates()` and only then `ResolveFalls`. `Walk` and `MoveUpDown` now do (`RunWalk` runs the list, takes `ExplosionRefusal`, then resolves the falls);
  new commands must call `RunStates()`, then `TakeExplosionRefusal()`, then `ResolveFalls`.
- **Open refusal.** `battle.self-destruct-item-unsupported` (a melee weapon as a unit's self-destruct item; S6 retires it when `ExplosionState` handles `BT_MELEE`).
- **What S5 left to build on.** `BattleState.PushExplosion(rules, attack, centerVoxel, beforeVoxel, lowerWeapon, range)` plus `RunStates()` run an `ExplosionBState` (S6 needs the melee form: `_hit`, `BT_MELEE`, `meleeAttack`/`meleeAttackCalculate` in `ExplosionState.Init`/`Think`, now a `NotSupportedException`; the power is `getMeleeBonus` and the damage type the item's `MeleeDamage`; `_radius` 0,
  `terrainMeleeTilePart` goes to `Hit`, which has no such parameter yet; `explode()`'s `_hit` part lowers the attacker and pops when the power is 0). The blasts are `BattleExploded` events in `Events` (the runtime empties the sink at the start of a command). `RunTerrainExplosions(rules)` is what `endTurn` calls (branch 6).
  `DamageUnit` returns a refusal only for a self-destruct item that is a melee weapon or psi amplifier: when `ExplosionState` handles melee (S6 and branch 6), retire `battle.self-destruct-item-unsupported`.
- **The list of states is the commands' to run.** A command that can push a state (a hit with a unit that blows itself up, a blast) ends with `RunStates()`. `Walk`, `MoveUpDown` and the falls already do. The reaction fire after an attack stays `battle.reaction-fire-unsupported` until S9.
- **Spawns and the first turn.** `SpawnNewUnit(rules, attack, position)` and `SpawnNewItem` are the attack forms; the first turn's `BattleFirstTurn.SpawnNewUnit` is the item form without sight recalculation, and could be folded into them.
- **A unit that is out has no tile.** `Position` null, `SavedPosition` where it lies; use `RestingPosition(unit)` and `IsCarried(unit)`. `SetLyingPosition` moves a body.
- **`ItemDrop`** omits the danger zone a primed grenade dropped where a unit stands sets (AI state, branch 6; ADR 0041).
- **Tests.** `BattleExplosionTests` has `Extra` (an extra unit), `Grenade` and `Armed` (a grenade of power 80 with a flat HE type); `BattleDamageTests.Duel` is internal and `Shot` builds an attack with the soldier's loaded gun; `BattleCasualtyTests.Extra`, `Lie` and `Murder` build units, bodies and murderers.
  A unit the test makes out by hand needs `Position = null` and its `SavedPosition`.
- **Branch 4's gotchas and the trial copy** (the post-edit hook reformats C# files, so read a file again before a second edit; add string literals with a backslash-n through the Edit tool;
  write test files with LF; run tests with `dotnet test --project <proj> --filter-class <full name>`; a test that adds units by hand needs a `SoldierId` or a unit rule; record equality compares lists by reference) still apply.
