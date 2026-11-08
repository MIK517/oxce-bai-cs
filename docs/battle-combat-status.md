# Battle combat status

Phase 7 branch 5 (`claude/battle-combat`), from `main` after PR #81 (branch 4 merged, `fae8bfbc`). Plan:
[Phase 7 branch 5](phase-7-plan.md#branch-5--battle-combat); decisions:
[ADR 0037](decisions/0037-host-authoritative-session.md), [ADR 0038](decisions/0038-battle-aggregate.md),
[ADR 0039](decisions/0039-battle-save-intentional-differences.md),
[ADR 0040](decisions/0040-battle-movement-intentional-differences.md) and the plan's branch decisions. ADR 0041 (battle
combat intentional differences) is created by the first slice that needs it.

## Progress

| Slice | State |
|---|---|
| S0 planning | Done and audited (2026-11-08) |
| S1 combat groundwork: item, armor and damage rules, stat bonuses, combat constants, script refusals | Not started |
| S2 line of fire and accuracy | Not started |
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
  `hitUnit`'s body items, `Projectile::applyAccuracy`, `validateThrow` and `MeleeAttackBState`; the `statDataMap` stat
  functions; where the battlescape constructor's casualty check runs in the port's flow (S4).

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

## Next

S1, combat groundwork (plan row S1). Notes for the worker:

- **Where the rules live.**
  - `src/Oxce.Mods/Rulesets/Runtime/RuntimeBattleEquipment.cs`: `RuntimeBattleItem` holds the equipment fields only
    (no power, damage type, accuracy or range yet). `RuntimeBattleArmor` has `Sides` but no damage modifiers or
    corpses.
  - `RuntimeItemDamageRules.cs`: `RuntimeItemDamageRule` is the strategic side's projection of an item's damage type,
    which the script queries read. It covers the predefined types, `FixRadius`, `RandomType`, `ResistType`,
    `ArmorIgnore`, `ArmorEffectiveness` and the `To*` conversions; the random flags, thresholds and `Ignore*` fields
    are missing. Extend it or build the battle's `RuleDamageType` beside it, but keep one projection of each key.
  - `BattleFire.cs`: `BattleTileDamageType` is branch 4 S9's subset for fire. S1's full type replaces it, and
    `HitTerrain` gets the weapon branch 4 S9 left out.
- **Stat bonuses.** `ContentSnapshot.CompileStatBonuses` and `GenerateStatBonus` turn a coefficient mapping into script
  source, so the runtime only sees compiled scripts. `BattleFirstTurn`'s flare check (`HasRuleScript(..., "damageBonusBonusStats")`)
  therefore cannot tell a mapping from code. S1 needs the coefficients (or a parse of the generated source) and the
  defaults of `RuleItem::load` (`setFiring`, `setMelee`, `setThrowing`, `setCloseQuarters`, `setStrength` with
  `_modded` for `strengthApplied`, and `setFlatHundred` and `setPsiAttack` for the psi amp's cases).
- **Script refusal pattern.** `BattleFirstTurn.VisibilityScriptRefusal`, `HasRuleScript` and `HasEventPlan` with the
  gate in `CampaignBattles` (briefing, first turn, every command of a running battle). The combat hooks join the same
  checks, and a load still accepts such a battle.
- **Reason strings to update when their refusal changes owner or retires.** `BattleFirstTurn.cs` (`CasualtiesUnsupported`,
  `TurnOneReviveUnsupported`) and `BattleGravity.cs` (the fall knock-out) say branch 6 until S4 retires them; S4 also
  removes them from the refusal inventory.
- **Random sources.** Several compatibility tests define their own scripted `IRandomSource`. If the attack slices
  force outcomes, put one shared helper in `tests/Shared` (AGENTS.md test conventions).
- **Branch 4's notes still apply.** Commands run on a trial copy first (`CampaignBattles.TrialRefusal`), which needs
  terrain files. Branch 4's gotchas (`docs/battle-movement-status.md` "## Next") also apply: the post-edit hook
  reformats C# files, so read a file again before a second edit; add string literals with a backslash-n through the
  Edit tool; write test files with LF; run tests with `dotnet test --project <proj> --filter-class <full name>`; a
  test that adds units by hand needs a `SoldierId` or a unit rule.
