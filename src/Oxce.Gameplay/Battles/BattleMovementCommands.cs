using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>What <see cref="BattleState.TurnTowards"/> did.</summary>
internal readonly record struct BattleTurnResult(int Direction, int Eighths, int TimeUnitsSpent, BattleTurnStop Stop,
    BattleDoorResult Door = BattleDoorResult.None, int DoorsOpened = 0, BattlePosition? DoorCentre = null);

/// <summary>
/// Turning and kneeling (Phase 7 branch 4 S5): <c>UnitTurnBState</c> (<c>init</c> and <c>think</c>, the eighths collapsed into one command),
/// <c>BattlescapeGame::kneel</c>, and the part of <c>TileEngine::getSpottingUnits</c> that changes spotting (the reaction itself is
/// <see cref="CheckReactionFire"/>).
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>
    /// <c>BattlescapeGame::secondaryAction</c> and <c>UnitTurnBState</c> for a unit of the side on turn (a player's turn: the targeting and strafe
    /// forms and the AI's turn-then-kneel are not commands). The unit looks at the tile; when that turns it, each eighth pays the turn cost
    /// (reserved time units checked first, as <c>think</c> does), turns, and updates the unit's field of view, and the turn stops on a new
    /// sighting or when the next eighth cannot be paid. Nothing changes when the first eighth cannot be paid.
    /// </summary>
    internal CampaignReason? TurnTowards(int unitId, BattlePosition target, out BattleTurnResult result)
    {
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
            if (UnitOpensDoor(unit, true, -1, out var door) is { } unsupported) return unsupported;
            if (door.Changed) MarkChanged();
            result = new BattleTurnResult(unit.Direction, 0, door.TimeUnitsSpent, BattleTurnStop.NoTurn, door.Result, door.DoorsOpened, door.Centre);
            return null;
        }

        var cost = unit.TurnCost(RequiredRules);
        // The first eighth: nothing changes when it is refused.
        if (!CheckReservedTimeUnits(unit, cost, 0, false).Allowed) return BattleMovementReasons.TurnTimeUnitsReserved;
        if (unit.TimeUnits < cost) return BattleMovementReasons.TurnNotEnoughTimeUnits;

        var sight = Sight();
        unit.LookAt(target, turret: false);
        var eighths = 0;
        var spent = 0;
        BattleTurnStop stop;
        while (true)
        {
            // think
            if (eighths > 0 && !CheckReservedTimeUnits(unit, cost, 0, false).Allowed)
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
        MarkChanged();
        result = new BattleTurnResult(unit.Direction, eighths, spent, stop);
        return null;
    }

    /// <summary>
    /// <c>BattlescapeGame::kneel</c> for a unit of the side on turn: the armor must allow kneeling, the unit not float, and unless it is
    /// kneeling down with the kneel time units reserved the change must respect the reserved time units; then it pays, changes stance, and the
    /// field of view of everyone through its tile and of the unit itself (<c>updateSoldierInfo</c>) is updated, and the spotting of
    /// <c>checkReactionFire</c> happens. Refused, before any change, when an opposing unit could react.
    /// </summary>
    internal CampaignReason? KneelUnit(int unitId, out bool kneeling, out int spent, bool raise = false)
    {
        kneeling = false;
        spent = 0;
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is not { } position) return BattleSelectionReasons.UnitUnknown;

        var cost = unit.KneelChangeCost;
        if (!AllowsKneeling(unit)) return BattleMovementReasons.KneelNotAllowed;
        if (unit.Floating) return BattleMovementReasons.KneelFloating;
        if (!(!unit.Kneeled && KneelReserved) && !CheckReservedTimeUnits(unit, cost, 0, false).Allowed)
            return BattleMovementReasons.KneelTimeUnitsReserved;
        if (unit.TimeUnits < cost) return BattleMovementReasons.KneelNotEnoughTimeUnits;
        var sight = Sight();
        unit.TimeUnits -= cost;
        unit.Kneel(!unit.Kneeled);
        // Kneeling or standing up can reveal new terrain or units: update the field of view of everyone through this position, tiles skipped
        // (updateSoldierInfo updates the unit's own, which also covers its tiles).
        sight.CalculateFov(position, 1, updateTiles: false);
        sight.CalculateFov(unit);
        // The command's event comes before what the reactions raise (the first one starts at once, on the empty list).
        if (raise) Events.Add(new UnitKneeled(unitId, unit.Kneeled, cost));
        // checkReactionFire(unit, kneel): the reactions are pushed to the back of the list; the caller runs them (the command, or the walk that ends first).
        CheckReactionFire(unit, new BattleReactionOrigin(BattleActionType.Kneel));
        MarkChanged();
        kneeling = unit.Kneeled;
        spent = cost;
        return null;
    }

    /// <summary>
    /// The kneel button's command: <see cref="KneelUnit(int, out bool, out int)"/>, its event, then the reactions it drew run (<see cref="RunStates"/>, <see cref="TakeExplosionRefusal"/>)
    /// and the falls they start.
    /// </summary>
    internal CampaignReason? KneelAction(int unitId)
    {
        if (KneelUnit(unitId, out _, out _, raise: true) is { } refusal) return refusal;
        RunStates();
        if (TakeExplosionRefusal() is { } explosionRefusal) return explosionRefusal;
        MarkChanged();
        return ResolveFalls(out _);
    }

    // ---- reaction fire's state, for the commands that move a unit (kneeling, landing, walking) ----

    /// <summary>
    /// The state filters of <c>TileEngine::getSpottingUnits</c> a unit passes to be a spotter of the mover, no sight asked: not out, not over its
    /// out threshold, a reaction score that reaches the mover's, not of the side on turn (and nobody reacts on the neutral side's turn), not a
    /// civilian unless the mover is a hostile that is not ignored by the AI, and within the maximum view distance.
    /// </summary>
    private bool PassesSpottingFilters(BattleUnit mover, BattleUnit other)
    {
        if (Side == BattleFaction.Neutral) return false;
        if (other.IsOut || other.IsOutThresholdExceeded || other.ReactionScore < Threshold(mover) || other.Faction == Side) return false;
        if (other.Faction == BattleFaction.Neutral && !(mover.Faction == BattleFaction.Hostile && !IgnoredByAi(mover))) return false;
        if (mover.Position is not { } at || other.Position is not { } from) return false;
        var maximum = RequiredRules.Campaign.MaximumViewDistance;
        return BattleGeometry.HorizontalDistanceSquared(at, from) <= maximum * maximum;
    }

    /// <summary><c>int threshold = unit->getReactionScore()</c>: the mover's score cut to an integer (a score that is not finite stays as it is).</summary>
    private static double Threshold(BattleUnit mover) => double.IsFinite(mover.ReactionScore) ? Math.Truncate(mover.ReactionScore) : mover.ReactionScore;

    /// <summary>BattleUnit::isIgnoredByAI: the unit rule's (a soldier has none).</summary>
    private bool IgnoredByAi(BattleUnit unit) =>
        unit.SoldierId is null && RequiredRules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var rule) && rule.IgnoredByAi;

    /// <summary>
    /// <c>TileEngine::getSpottingUnits</c> whole (what <c>checkReactionFire</c> runs first): for every opposing unit that passes the state filters, faces the mover (or was hit
    /// by it) and sees its tile from its snap shot origin, the player's spotter marks the mover seen by the player, and every spotter lists the mover among its visible units
    /// (the port's per-faction spotting, ADR 0039, marks the mover spotted by a hostile or neutral spotter's faction); and for each spotter its <c>determineReactionType</c>; a spotter with
    /// a reaction (and, with a reaction fire threshold of its faction above 0, a hit chance at that distance that reaches it, inside the weapon's range) is in the list
    /// <c>checkReactionFire</c> goes through.
    /// </summary>
    internal List<BattleReaction> SpottingUnits(BattleSight sight, BattleUnit mover)
    {
        var spotters = new List<BattleReaction>();
        if (mover.Position is not { } position) return spotters;
        var tile = Map.TileIndex(position);
        foreach (var other in _units)
        {
            if (other == mover || other.Position is null || !PassesSpottingFilters(mover, other)) continue;
            var origin = sight.SnapShotOriginVoxel(other, position);
            // A unit that was hit sees its attacker whatever way it faces: the AI's memory, a player unit's hit state, or a melee attacker.
            var gotHit = other.Ai is { } ai ? ai.WasHitBy.Contains(mover.Id) : other.HitState;
            if (!gotHit && RequiredRules.Campaign.ExtendedMeleeReactions == 2) gotHit = other.MeleeAttackedBy.Contains(mover.Id);
            if (!(other.CheckViewSector(position) || gotHit) || !sight.CanTargetUnit(origin, tile, out _, other) || !sight.Visible(other, tile)) continue;
            SetSpotted(mover, other.Faction, true);
            other.AddToVisibleUnits(mover.Id);
            var reaction = DetermineReactionType(other, mover);
            if (reaction.AttackType == BattleActionType.None) continue;
            if (ReactionFireThresholdOf(other.Faction) is var threshold && threshold > 0)
            {
                var weapon = reaction.Weapon!;
                var accuracy = FiringAccuracy(other, weapon, reaction.AttackType);
                var distanceSquared = DistanceSquaredBetween(mover.RequiredPosition, other.RequiredPosition);
                var distance = (int)MathF.Ceiling(MathF.Sqrt(distanceSquared));
                var dropoff = CalculateLimits(weapon, reaction.AttackType, out var upperLimit, out var lowerLimit);
                if (distance > upperLimit) accuracy -= (distance - upperLimit) * dropoff;
                else if (distance < lowerLimit) accuracy -= (lowerLimit - distance) * dropoff;
                var outOfRange = IsOutOfRange(weapon, distanceSquared);
                // Options::useChanceToHit is on, battleRealisticAccuracy off.
                accuracy = HitChance(distance, accuracy, RequiredRules.Campaign.Combat.HitChances(mover.ArmorSize));
                if (!(accuracy >= threshold && !outOfRange)) continue;
            }
            spotters.Add(reaction);
        }
        return spotters;
    }

    /// <summary><c>Mod::getReactionFireThreshold</c>: X-COM's is the user option, the hostile and civilian ones the mod's.</summary>
    private int ReactionFireThresholdOf(BattleFaction faction) => faction switch
    {
        BattleFaction.Player => Options.ReactionFireThreshold,
        BattleFaction.Hostile => RequiredRules.Campaign.Combat.ReactionFireThreshold,
        BattleFaction.Neutral => RequiredRules.Campaign.Combat.ReactionFireThresholdCivilians,
        _ => 0,
    };

    /// <summary><c>Position::distanceSq</c>: the squared distance in tiles between two positions, level included.</summary>
    private static int DistanceSquaredBetween(BattlePosition one, BattlePosition other)
    {
        int x = one.X - other.X, y = one.Y - other.Y, z = one.Z - other.Z;
        return x * x + y * y + z * z;
    }
}
