# 0041: Battle combat intentional differences

Status: accepted for Phase 7 branch 5 (2026-11-09); later slices add entries.

## Problem

Battle combat (the [Phase 7 plan](../phase-7-plan.md#branch-5--battle-combat)) reaches reference paths whose behaviour is
undefined or depends on animation time. The port must be deterministic there and record what it does
([AGENTS.md](../../AGENTS.md)). Evidence is in [battle combat status](../battle-combat-status.md). Each entry names its slice.

## Decision

**A thrower of no strength (S2).** `ProjectileFlyBState::getMaxThrowDistance` computes `50 * weight / strength` in integers. A unit whose
strength is 0 divides by zero there, which ends the program. The port counts a strength of at least 1 for that quotient. The curvature of
`TileEngine::validateThrow` divides in floating point (`strength / weight`), where a weight of 0 gives an infinity and the curve 0.48 (so does a strength and a weight of 0, where `std::max(0.48, NaN)` is 0.48); the port
does the same, guarding `Math.Max` against the NaN.

**A target that lies off the map (S2).** `validThrowRange` dereferences the target tile of a throw (`getTerrainLevel`) after the item's throw-range check;
a target outside the map would crash. The commands refuse a target outside the map before it gets there, and the port's `validateThrow` treats
a missing tile as a throw that cannot land (for a shot as well, which the reference does not look at there).

**The generator behind a hit's rolls (S3).** `BattleUnit::damage` takes one draw of the game generator to make a sub-sequence on every hit on a living unit, and draws the leg, the burn time
and the infection and self-destruct percents from it; the side rolls use the game generator. The port draws everything from the battle's generator in the same order, without the extra draw;
exact streams are out of scope.

**Recovery bonuses in the murderer rule (S3).** `hasNegativeHealthRegen` reads the armor's and the soldier bonuses' `recovery: health`, which the port does not read yet (branch 6). It counts as false, so a
mod with a negative health recovery records a different murderer for a harmless hit on a burning or wounded victim. `Options::alienBleeding` is fixed off.

**A unit it cannot turn into (S3).** An infecting hit looks the unit it would spawn up with `Mod::getUnit`, whose result `damage` dereferences, and refuses (logs) one with a bigger armor than the
victim's. The port converts nothing for both a missing unit and a bigger armor.

**A corpse the armor does not list (S4).** `UnitDieBState::convertUnitToCorpse` reads `getCorpseBattlescape()[i]` for each tile part of the body, which is
undefined when the armor lists fewer corpses than its size squared (and a null item is dereferenced when one entry names no item). The port leaves no corpse for a part
the armor lacks (the rule loader requires a corpse per tile part, so this guard is defensive).

**A spawn unit the mod lacks or that does not fit (S4).** `convertUnit` creates the unit from `unit->getSpawnUnit()` without a check, and sets the new unit's tiles
whatever stands there. The port treats a unit whose spawn unit names no unit of the mod as having none (it dies and leaves a corpse), and spawns nothing when the new unit's
footprint would cover a tile off the map or a tile another unit stands on (the old unit is dead and its things lie on the ground).

**The memorial's cause and the dead soldier's armor (S4).** `SavedGame::killSoldier(false, ...)` keeps the soldier's geoscape armor during the battle, and the death's cause is
the murderer's statistics with the victim's side and body part. `DeadSoldierSnapshot.Cause` carries it and the save adapter writes `death.cause` for a death in battle; a loaded death keeps its node.
The `KIA` flag of the victim's statistics is not saved by the reference and not kept here.

**A unit that is out stands on no tile (S4).** The reference keeps a dead or unconscious unit's `_pos` but clears its tile; the port keeps the position in `SavedPosition`
(the save writes it) and `Position` is the tile link, null once the unit is out, so a unit that is out and lies on the map has no tile. Its body items (and the unit it carries) name it.

**A stun before the game starts (S4).** The reference converts a unit before the game only through the state constructor's instant path (explosions, units already unconscious); a stunned standing unit's state waits until the battlescape runs, after the pre-battle inventory and the first turn's preparation. The port runs the state at the briefing's confirmation, so a unit stunned out at that point is unconscious before the inventory opens.

**A module destroyed in a block the saved module map lacks (S5).** `TileEngine::hit` and `detonate` decrement `getModuleMap()[x / 10][y / 10].second` of a base defense without a check, which indexes past
the vector when a restored save's map lacks the block (undefined behaviour). The port destroys the part and skips the count (`CountModuleLost`). ADR 0039 asked branches 5 to 7 to refuse such a block; for the destruction this
replaces that (AGENTS.md allows it for undefined behaviour), while the AI (branch 6) and the debriefing (branch 7) keep ADR 0039's rule.

**Explosions before the battlescape exists (S5, corrected in S5b).** `explodePowerSources` and `explodeOtherJunk` run in the generator, before `BriefingState` creates the battlescape and calls `setBattleState`. `BattleUnit::damage` wraps its special effects
(morale loss, the burn roll, zombie conversion, self-destruct, the attacker's spotting and the `DamageSpecialUnit*` scripts) in `if (save->getBattleState())`, so none of them happens in those explosions: a unit does not blow itself up, lose morale or catch fire from a power source or
a map grenade. The port does the same (`BattleState.Generating` is true while the two routines run), and so the pushed self-destruct of the earlier S5 text never arises there. The units taken out are ordinary deaths of the battlescape's opening check, before the briefing's
hidden one. (The first S5 text of this entry assumed the reference would push a self-destruct on a battlescape game that does not exist; that was wrong.) (A self-destruct item that is a melee weapon failed by name until S6, which retired
`battle.self-destruct-item-unsupported`: the explosion state treats the weapon as the reference does, a plain hit with its power at the unit's corner.)

**The hit script's side (S5b).** `ModScript::HitUnit` may write a side; the reference resets a side at `SIDE_MAX` (9, the sides of a unit that can have more than the five of the port) to the front. The port's armor values have five sides (front, left, right, rear, under), so
it resets a side of 5 or more to the front. The two differ only for a script that answers a side of 5 to 8, which the port's five-sided armor arrays cannot index; branch 8 revisits this if a mod's script does.

**The order of detonation (S5).** `explode` keeps the tiles it reached in a `std::map` keyed by tile pointer, so the detonation order is the allocation order of the tiles. The port detonates in tile index order. The animation's draws
(`RNG::generate` for each explosion sprite and the camera) are presentation and draw nothing.

**Tile links and the danger zone (S4).** A dropped primed grenade's danger zone (`setDangerZone`) was omitted by `ItemDrop`; it is now kept (S9, second part: a tile flag on the map, which the AI's reaction score reads) and only its clearing at the end of a side's turn, and the AI's own throw, wait for branch 6.

**The Alt key of a terrain strike, and no diagonal terrain strike (S6).** `validTerrainMeleeRange` asks `SavedBattleGame::isAltPressed(true)`, a keyboard state of the battlescape. The port takes it as the attack command's `AltPressed` flag (the client sets it, as it sets Ctrl for force fire), which only
decides whether a unit on the high part of stairs aims one level up. With `diagTerrainMelee` off (its default, the plan's option list) a diagonal facing never reaches terrain, so the diagonal cases of the reference's switch and the big wall check for a diagonal are not ported; a client that makes the option available ports them.

**Deferred to branch 6, not a difference: the AI's chosen target (S6).** `MeleeAttackBState::init` takes `AIModule::getTarget()` as the victim for a non-player unit on its own turn and the unit on the target tile for everyone else. Until the AI exists
nothing calls `StrikeMelee` for a hostile unit, so the port takes the unit on the tile in both cases; branch 6 adds the AI's target where the module exists and this entry goes.

**Two crashes of `MeleeAttackBState::init` (S6).** The reference throws an `Exception` when no unit stands on the target tile (after it paid the time units) and when the victim's voxel lies outside the map ("Melee attack animation overflow"; reachable for a victim on the top level standing on
a high stair part, whose voxel height is half its height plus 16). The port picks a deterministic behaviour: with no unit nothing is struck and nothing is spent (the state ends, the command reports `battle.attack-not-performed`), and the voxel's height is cut to the map's top voxel so the strike goes on.

**A hit log and a melee attacker's reaction (S6).** `meleeAttack` appends `HITLOG_NEW_SHOT` to the hit log (runtime text, not ported). `MeleeAttackBState::think` calls `checkReactionFire` after the hit, and S9 ported it: the reactions run within the command. Only the hit log is left out.
