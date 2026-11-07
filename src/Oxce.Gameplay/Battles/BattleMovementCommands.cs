using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>What <see cref="BattleState.TurnTowards"/> did.</summary>
internal readonly record struct BattleTurnResult(int Direction, int Eighths, int TimeUnitsSpent, BattleTurnStop Stop,
    BattleDoorResult Door = BattleDoorResult.None, int DoorsOpened = 0, BattlePosition? DoorCentre = null);

/// <summary>
/// Turning and kneeling (Phase 7 branch 4 S5): <c>UnitTurnBState</c> (<c>init</c> and <c>think</c>, the eighths collapsed into one command),
/// <c>BattlescapeGame::kneel</c>, and the part of <c>TileEngine::getSpottingUnits</c> that changes spotting (the reaction itself is branch 5's,
/// refused by <see cref="HasPossibleReactor"/>).
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>
    /// <c>BattlescapeGame::secondaryAction</c> and <c>UnitTurnBState</c> for a unit of the side on turn (a player's turn: the targeting and strafe
    /// forms and the AI's turn-then-kneel are not commands). The unit looks at the tile; when that turns it, each eighth pays the turn cost
    /// (reserved time units checked first, as <c>think</c> does), turns, and updates the unit's field of view, and the turn stops on a new
    /// sighting or when the next eighth cannot be paid. Nothing changes when the first eighth cannot be paid.
    /// </summary>
    internal CampaignReason? TurnTowards(RuntimeRuleCatalog rules, int unitId, BattlePosition target, out BattleTurnResult result)
    {
        ArgumentNullException.ThrowIfNull(rules);
        result = default;
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is not { } position) return BattleSelectionReasons.UnitUnknown;
        if (!Contains(target)) return BattleMovementReasons.TurnTargetOutside;

        // lookAt: only a different direction turns the unit.
        var turns = position != target && unit.DirectionTo(target) != unit.Direction;
        if (!turns)
        {
            // init: a unit that is not turning tries the door in front of it (unitOpensDoor with the right click).
            if (UnitOpensDoor(rules, unit, true, -1, out var door) is { } unsupported) return unsupported;
            if (door.Changed) Revision++;
            result = new BattleTurnResult(unit.Direction, 0, door.TimeUnitsSpent, BattleTurnStop.NoTurn, door.Result, door.DoorsOpened, door.Centre);
            return null;
        }

        var cost = unit.TurnCost(rules);
        // The first eighth: nothing changes when it is refused.
        if (!CheckReservedTimeUnits(rules, unit, cost, 0, false).Allowed) return BattleMovementReasons.TurnTimeUnitsReserved;
        if (unit.TimeUnits < cost) return BattleMovementReasons.TurnNotEnoughTimeUnits;

        var sight = Sight();
        unit.LookAt(target, turret: false);
        var eighths = 0;
        var spent = 0;
        BattleTurnStop stop;
        while (true)
        {
            // think
            if (eighths > 0 && !CheckReservedTimeUnits(rules, unit, cost, 0, false).Allowed)
            {
                unit.AbortTurn();
                stop = BattleTurnStop.ReservedTimeUnits;
                break;
            }
            if (!unit.SpendTimeUnits(cost))
            {
                unit.AbortTurn();
                stop = BattleTurnStop.NotEnoughTimeUnits;
                break;
            }
            spent += cost;
            var spotted = unit.UnitsSpottedThisTurn.Count;
            unit.Turn(turret: false);
            eighths++;
            sight.CalculateFov(unit);
            if (unit.UnitsSpottedThisTurn.Count > spotted)
            {
                // (The brutal units of the side learn of it through checkForReactivation, the AI's state of branch 6.)
                unit.AbortTurn();
                stop = BattleTurnStop.SpottedUnits;
                break;
            }
            if (unit.Status == BattleUnitStatus.Standing)
            {
                stop = BattleTurnStop.Arrived;
                break;
            }
        }
        Revision++;
        result = new BattleTurnResult(unit.Direction, eighths, spent, stop);
        return null;
    }

    /// <summary>
    /// <c>BattlescapeGame::kneel</c> for a unit of the side on turn: the armor must allow kneeling, the unit not float, and unless it is
    /// kneeling down with the kneel time units reserved the change must respect the reserved time units; then it pays, changes stance, and the
    /// field of view of everyone through its tile and of the unit itself (<c>updateSoldierInfo</c>) is updated, and the spotting of
    /// <c>checkReactionFire</c> happens. Refused, before any change, when an opposing unit could react.
    /// </summary>
    internal CampaignReason? KneelUnit(RuntimeRuleCatalog rules, int unitId, out bool kneeling, out int spent)
    {
        ArgumentNullException.ThrowIfNull(rules);
        kneeling = false;
        spent = 0;
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is not { } position) return BattleSelectionReasons.UnitUnknown;

        var cost = unit.KneelChangeCost;
        if (!AllowsKneeling(rules, unit)) return BattleMovementReasons.KneelNotAllowed;
        if (unit.Floating) return BattleMovementReasons.KneelFloating;
        if (!(!unit.Kneeled && KneelReserved) && !CheckReservedTimeUnits(rules, unit, cost, 0, false).Allowed)
            return BattleMovementReasons.KneelTimeUnitsReserved;
        if (unit.TimeUnits < cost) return BattleMovementReasons.KneelNotEnoughTimeUnits;
        // checkReactionFire follows the change: a reactor with a weapon would shoot (branch 5).
        // The threshold is the kneeling unit's reaction score after it paid.
        unit.TimeUnits -= cost;
        var reactor = HasPossibleReactor(rules, unit);
        unit.TimeUnits += cost;
        if (reactor) return BattleMovementReasons.ReactionFireUnsupported;

        var sight = Sight();
        unit.TimeUnits -= cost;
        unit.Kneel(!unit.Kneeled);
        // Kneeling or standing up can reveal new terrain or units: update the field of view of everyone through this position, tiles skipped
        // (updateSoldierInfo updates the unit's own, which also covers its tiles).
        sight.CalculateFov(position, 1, updateTiles: false);
        sight.CalculateFov(unit);
        SpotMover(rules, sight, unit);
        Revision++;
        kneeling = unit.Kneeled;
        spent = cost;
        return null;
    }

    // ---- reaction fire's state, for the commands that move a unit (kneeling, landing, walking) ----

    /// <summary>
    /// The state filters of <c>TileEngine::getSpottingUnits</c> a unit passes to be a spotter of the mover, no sight asked: not out, not over its
    /// out threshold, a reaction score that reaches the mover's, not of the side on turn (and nobody reacts on the neutral side's turn), not a
    /// civilian unless the mover is a hostile that is not ignored by the AI, and within the maximum view distance.
    /// </summary>
    private bool PassesSpottingFilters(RuntimeRuleCatalog rules, BattleUnit mover, BattleUnit other)
    {
        if (Side == BattleFaction.Neutral) return false;
        if (other.IsOut || other.IsOutThresholdExceeded || other.ReactionScore < Threshold(mover) || other.Faction == Side) return false;
        if (other.Faction == BattleFaction.Neutral && !(mover.Faction == BattleFaction.Hostile && !IgnoredByAi(rules, mover))) return false;
        if (mover.Position is not { } at || other.Position is not { } from) return false;
        var maximum = rules.Campaign.MaximumViewDistance;
        return (at.X - from.X) * (at.X - from.X) + (at.Y - from.Y) * (at.Y - from.Y) <= maximum * maximum;
    }

    /// <summary><c>int threshold = unit->getReactionScore()</c>: the mover's score cut to an integer (a score that is not finite stays as it is).</summary>
    private static double Threshold(BattleUnit mover) => double.IsFinite(mover.ReactionScore) ? Math.Truncate(mover.ReactionScore) : mover.ReactionScore;

    /// <summary>BattleUnit::isIgnoredByAI: the unit rule's (a soldier has none).</summary>
    private static bool IgnoredByAi(RuntimeRuleCatalog rules, BattleUnit unit) =>
        unit.SoldierId is null && rules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var rule) && rule.IgnoredByAi;

    /// <summary>
    /// Whether an opposing unit could react to the mover at its tile: one that passes <see cref="PassesSpottingFilters"/>, is not respawning, has
    /// a reaction score above 0.001 and a weapon <c>determineReactionType</c> could pick. Sight, range and melee reach are not asked, and every
    /// weapon it carries (its hands and special weapons) counts, so this over-approximates (Phase 7 plan D4): a unit that fails it never reacts,
    /// and the preferred hand, the research, depth and two-handed requirements of <c>canUseWeapon</c> only narrow what passes.
    /// </summary>
    internal bool HasPossibleReactor(RuntimeRuleCatalog rules, BattleUnit mover)
    {
        foreach (var other in _units)
        {
            if (other == mover || !PassesSpottingFilters(rules, mover, other) || other.Respawn || other.ReactionScore <= 0.001) continue;
            if (CanReactWithAnyWeapon(rules, other)) return true;
        }
        return false;
    }

    /// <summary>
    /// determineReactionType's weapon tests for every weapon the unit could use: any weapon with ammo for its hit (a firearm's butt too) and the time units for it,
    /// or a firearm with ammo for its snap shot and the time units for it, unless a psi weapon needs a psi skill the unit lacks.
    /// </summary>
    private bool CanReactWithAnyWeapon(RuntimeRuleCatalog rules, BattleUnit unit)
    {
        var weapons = new List<BattleItem>();
        if (HandWeapon(unit, RightHand) is { } right) weapons.Add(right);
        if (HandWeapon(unit, LeftHand) is { } left) weapons.Add(left);
        foreach (var id in unit.SpecialWeaponIds)
            if (_items.FirstOrDefault(item => item.Id == id) is { } special) weapons.Add(special);
        foreach (var weapon in weapons)
        {
            var rule = ItemRuleOf(rules, weapon.ItemType);
            if (rules.Battle.Equipment.Items[weapon.ItemType].PsiRequired && unit.Stats.PsiSkill <= 0) continue;
            if (AmmoForAction(rules, weapon, 3) is not null &&
                BattleActionCost.For(rules, BattleActionType.Hit, unit, weapon).HaveTimeUnits()) return true;
            if (rule.BattleType == BattleTypeFirearm && AmmoForAction(rules, weapon, 2) is not null &&
                BattleActionCost.For(rules, BattleActionType.SnapShot, unit, weapon).HaveTimeUnits()) return true;
        }
        return false;
    }

    /// <summary>
    /// The spotting of <c>TileEngine::getSpottingUnits</c> (what <c>checkReactionFire</c> runs first), for every opposing unit that passes
    /// the state filters, faces the mover (or was hit by it) and sees its tile from its snap shot origin: the player's spotter marks the mover
    /// seen by the player, and every spotter lists the mover among its visible units (the port's per-faction spotting, ADR 0039, marks the
    /// mover spotted by a hostile or neutral spotter's faction). The reactors it finds are branch 5's; <see cref="HasPossibleReactor"/> refused
    /// the command when there could be one.
    /// </summary>
    internal void SpotMover(RuntimeRuleCatalog rules, BattleSight sight, BattleUnit mover)
    {
        if (Side != mover.Faction || mover.Position is not { } position) return;
        var tile = Map.TileIndex(position);
        foreach (var other in _units)
        {
            if (other == mover || other.Position is null || !PassesSpottingFilters(rules, mover, other)) continue;
            var origin = sight.SnapShotOriginVoxel(other, position);
            // A unit that was hit sees its attacker whatever way it faces: the AI's memory, a player unit's hit state, or a melee attacker.
            var gotHit = other.Ai is { } ai ? ai.WasHitBy.Contains(mover.Id) : other.HitState;
            if (!gotHit && rules.Campaign.ExtendedMeleeReactions == 2) gotHit = other.MeleeAttackedBy.Contains(mover.Id);
            if (!(other.CheckViewSector(position) || gotHit) || !sight.CanTargetUnit(origin, tile, out _, other) || !sight.Visible(other, tile)) continue;
            SetSpotted(mover, other.Faction, true);
            other.AddToVisibleUnits(mover.Id);
        }
    }
}
