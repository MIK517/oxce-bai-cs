using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Walking and flying (Phase 7 branch 4 S8): <c>BattlescapeGame::primaryAction</c>'s walk, <c>moveUpDown</c> and <c>UnitWalkBState</c> run to
/// their end. The reference's <c>think</c> is called once per animation tick: the port runs it as one loop whose cycles are the reference's
/// (a step starts in one cycle and is finished, with everything that follows it, at the start of the next; a turn eighth is one cycle), a step
/// collapsed into <see cref="WalkStep"/>. What the reference hands to later work is refused by name: the reaction fire after a step
/// (<see cref="HasPossibleReactor"/>), proximity explosions, burning floors and a fall that finds no escape. The command checks them on a trial
/// copy of the battle first, so a refusal changes nothing.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>TUO_IGNORE_SMALL: a unit below whose top pokes less than 2 voxels into the tile is not "in the way".</summary>
    private const int OverlapIgnoreSmall = 26;

    /// <summary>What a walk did, for the command's last event.</summary>
    internal readonly record struct BattleWalkResult(BattleWalkStop Stop, int Steps, int TimeUnitsSpent, int EnergySpent);

    /// <summary>
    /// The walk command (<c>primaryAction</c>): the path to <paramref name="target"/> for the move type, then <see cref="RunWalk"/>. Refused, before
    /// any change, when the armor does not allow moving (or the move type), there is no path, or the first step cannot be taken. Ordered events are
    /// added to <paramref name="events"/>.
    /// </summary>
    internal CampaignReason? Walk(RuntimeRuleCatalog rules, int unitId, BattlePosition target, BattleMoveType move, bool ignoreSpottedEnemies,
        List<ICampaignEvent> events)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(events);
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is null) return BattleSelectionReasons.UnitUnknown;
        if (!Contains(target)) return BattleMovementReasons.WalkTargetOutside;
        var armor = ArmorOf(rules, unit);
        if (!armor.AllowsMoving) return BattleMovementReasons.WalkNotAllowed;
        if (!Enum.IsDefined(move)) return BattleMovementReasons.WalkMoveNotAllowed;
        var small = !unit.IsBigUnit;
        var allowed = move switch
        {
            BattleMoveType.Run => RuntimeBattleArmor.Allows(armor.AllowsRunning, small),
            BattleMoveType.Strafe => RuntimeBattleArmor.Allows(armor.AllowsStrafing, small),
            BattleMoveType.Sneak => RuntimeBattleArmor.Allows(armor.AllowsSneaking, small),
            _ => true,
        };
        if (!allowed) return BattleMovementReasons.WalkMoveNotAllowed;
        var bam = (BattleActionMove)move;
        var pathfinder = new BattlePathfinder(this, rules);
        pathfinder.Calculate(unit, target, bam);
        if (pathfinder.StartDirection == -1) return BattleMovementReasons.WalkNoPath;
        // if running or shifting, ignore spotted enemies (i.e. don't stop)
        var ignore = ignoreSpottedEnemies || move == BattleMoveType.Run && rules.Campaign.ExtendedRunningCost;
        return RunWalk(rules, unit, pathfinder, bam, move == BattleMoveType.Strafe, ignore, events);
    }

    /// <summary>
    /// <c>BattlescapeGame::moveUpDown</c> behind the unit up and down buttons (<c>validateUpDown</c> first): a kneeling unit stands up, the path to
    /// the tile a level above or below is calculated, and the unit walks it. A path that is not there is a walk that ends where it started.
    /// </summary>
    internal CampaignReason? MoveUpDown(RuntimeRuleCatalog rules, int unitId, bool up, List<ICampaignEvent> events)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(events);
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is not { } position) return BattleSelectionReasons.UnitUnknown;
        var pathfinder = new BattlePathfinder(this, rules);
        if (!pathfinder.ValidateUpDown(unit, position, up ? BattleUnit.DirectionUp : BattleUnit.DirectionDown)) return BattleMovementReasons.UpDownImpossible;
        var target = position with { Z = position.Z + (up ? 1 : -1) };
        if (!Contains(target)) return BattleMovementReasons.UpDownImpossible;
        // The kneel's result is not looked at: a unit that stays kneeling is stopped by the walk's own kneel.
        var kneelSpent = 0;
        var kneeled = false;
        if (unit.Kneeled && KneelUnit(rules, unit.Id, out var kneeling, out var spent) is null)
        {
            events.Add(new UnitKneeled(unit.Id, kneeling, spent));
            kneelSpent = spent;
            kneeled = true;
        }
        if (!ArmorOf(rules, unit).AllowsMoving)
        {
            // The walk state aborts silently in think, after moveUpDown's kneel.
            if (!kneeled) return BattleMovementReasons.WalkNotAllowed;
            events.Add(new UnitWalkEnded(unit.Id, BattleWalkStop.Arrived, 0, kneelSpent, 0, unit.Position!.Value));
            Revision++;
            return null;
        }
        pathfinder.Calculate(unit, target, BattleActionMove.Normal);
        return RunWalk(rules, unit, pathfinder, BattleActionMove.Normal, false, false, events, kneeled, kneelSpent);
    }

    /// <summary>
    /// <c>UnitWalkBState</c> from <c>init</c> to the pop. Returns a refusal when the walk could not take its first step (nothing changed) or when
    /// it met something unsupported (the command checks on a trial copy first). A kneeling unit first stands up (<c>BattlescapeGame::kneel</c>).
    /// </summary>
    private CampaignReason? RunWalk(RuntimeRuleCatalog rules, BattleUnit unit, BattlePathfinder pathfinder, BattleActionMove bam, bool strafeAction,
        bool ignoreSpotted, List<ICampaignEvent> events, bool kneeled = false, int kneelSpent = 0)
    {
        var sight = Sight();
        var armor = ArmorOf(rules, unit);
        var numUnitsSpotted = unit.UnitsSpottedThisTurn.Count;
        var startDirection = pathfinder.StartDirection;
        var beforeFirstStep = !strafeAction && startDirection != -1 && startDirection != unit.Direction;
        var falling = false;
        var fallingWhenStopped = false;
        var stepDone = false;
        var changed = kneeled;
        var preMovementCost = 0;
        var steps = 0;
        var timeUnitsSpent = kneelSpent;
        var energySpent = 0;
        var stop = BattleWalkStop.Arrived;
        CampaignReason? refusal = null;
        var size = unit.ArmorSize;

        // cancelCurentMove: a unit that stopped without a floor keeps falling first; otherwise the path is dropped and the state pops.
        bool Cancel(BattleWalkStop why)
        {
            if (fallingWhenStopped && !falling)
            {
                falling = true;
                return false;
            }
            pathfinder.AbortPath();
            stop = why;
            return true;
        }

        bool Spotted() => !ignoreSpotted && !falling && unit.Status != BattleUnitStatus.Panicking && numUnitsSpotted != unit.UnitsSpottedThisTurn.Count;

        // One call of think(); true when the state is popped.
        bool Think()
        {
            if (unit.Kneeled)
            {
                if (KneelUnit(rules, unit.Id, out var kneeling, out var spentOnKneel) is { } kneelRefusal)
                {
                    pathfinder.AbortPath();
                    refusal = kneelRefusal;
                    return true;
                }
                events.Add(new UnitKneeled(unit.Id, kneeling, spentOnKneel));
                timeUnitsSpent += spentOnKneel;
                changed = true;
                return false;
            }
            if (unit.IsOut)
            {
                pathfinder.AbortPath();
                stop = BattleWalkStop.Incapacitated;
                return true;
            }

            if (stepDone)
            {
                // The step is over: unit moved from one tile to the other, update the tiles.
                stepDone = false;
                var position = unit.Position!.Value;
                fallingWhenStopped = unit.HaveNoFloorBelow && position.Z != 0 && unit.MovementType != MovementTypeFly;
                // We do not have any footing but "jump" from a ladder to reach the ledge.
                var ladderJump = position.Z != 0 && Map.HasLadder(Map.TileIndex(position with { Z = position.Z - 1 })) &&
                    unit.LastPosition == position with { Z = position.Z - 1 } && pathfinder.StartDirection != -1;
                falling = fallingWhenStopped && !ladderJump;
                if (falling)
                {
                    for (var x = size - 1; x >= 0; x--)
                        for (var y = size - 1; y >= 0; y--)
                        {
                            var below = new BattlePosition(position.X + x, position.Y + y, position.Z - 1);
                            if (!Contains(below) || Map.UnitAt(Map.TileIndex(below)) is null) continue;
                            // The unit falls onto another: UnitFallBState runs first (the path loses its next step, as the reference's does).
                            falling = false;
                            fallingWhenStopped = false;
                            pathfinder.DequeuePath();
                            AddFallingUnit(unit);
                            var before = _units.Select(static candidate => (candidate, candidate.Position)).ToArray();
                            refusal = ResolveFalls(rules);
                            foreach (var (candidate, was) in before)
                                if (candidate.Position is { } now && was is { } from && now != from) events.Add(new UnitFell(candidate.Id, from, now));
                            return refusal is not null;
                        }
                }

                // The burning of floors by a unit that does so is S9's (and so is the fall it can start).
                if (!falling && SpecialAbilityOf(rules, unit) >= 2)
                {
                    refusal = BattleGravityReasons.BurnFloorUnsupported;
                    return true;
                }
                foreach (var faction in Enum.GetValues<BattleFaction>())
                    if (faction != unit.Faction) SetSpotted(unit, faction, false);
                if (ProximityEffects(rules, unit))
                {
                    refusal = BattleGravityReasons.ProximityUnsupported;
                    return true;
                }
                // Move our personal lighting with us, and update the unit visibility of everyone who can see the last and the present position.
                CalculateLighting(rules, BattleLightLayer.Units, position, 2);
                sight.CalculateFov(position, 2, updateTiles: false);
                // (The brutal units of the side wake up through checkForReactivation when a unit is spotted: the AI's, branch 6.)
                if (Spotted()) return Cancel(BattleWalkStop.SpottedUnits);
                if (!falling && !fallingWhenStopped)
                {
                    // checkReactionFire: a reactor would shoot (branch 5), the others spot the mover.
                    if (HasPossibleReactor(rules, unit))
                    {
                        refusal = BattleMovementReasons.ReactionFireUnsupported;
                        return true;
                    }
                    SpotMover(rules, sight, unit);
                }
            }

            // we are just standing around, shouldn't we be walking?
            if (unit.Status == BattleUnitStatus.Standing)
            {
                var direction = pathfinder.StartDirection;
                if (falling) direction = BattleUnit.DirectionDown;
                if (direction == -1)
                {
                    // postPathProcedures
                    CalculateLighting(rules, BattleLightLayer.Units, unit.Position);
                    sight.CalculateFov(unit);
                    if (!falling)
                    {
                        stop = BattleWalkStop.Arrived;
                        return true;
                    }
                    return false;
                }

                if (pathfinder.StrafeMove) unit.FaceDirection = unit.Direction;
                var step = pathfinder.GetTuCost(unit.Position!.Value, direction, unit, bam);
                int tu = step.Cost.Time, energy = step.Cost.Energy;
                if (!step.IsValid) return Cancel(BattleWalkStop.PathBlocked);
                if (tu > unit.TimeUnits) return Cancel(BattleWalkStop.NotEnoughTimeUnits);
                if (energy > unit.Energy) return Cancel(BattleWalkStop.NotEnoughEnergy);
                if (!falling)
                {
                    var reserve = CheckReservedTimeUnits(rules, unit, tu, energy, false);
                    if (reserve.Unsupported is { } unsupported)
                    {
                        refusal = unsupported;
                        return true;
                    }
                    if (!reserve.Allowed) return Cancel(BattleWalkStop.ReservedTimeUnits);
                }

                // we are looking in the wrong way, turn first (unless strafing); turning during walking costs no tu
                if (direction != unit.Direction && direction < BattleUnit.DirectionUp && !pathfinder.StrafeMove)
                {
                    unit.LookAt(direction, false);
                    return false;
                }

                // now open doors (if any)
                if (direction < BattleUnit.DirectionUp)
                {
                    if (UnitOpensDoor(rules, unit, false, direction, out var door) is { } doorRefusal)
                    {
                        refusal = doorRefusal;
                        return true;
                    }
                    if (door.Changed)
                    {
                        Revision++;
                        changed = true;
                        timeUnitsSpent += door.TimeUnitsSpent;
                        events.Add(new UnitWalkOpenedDoor(unit.Id, door.Result, door.DoorsOpened, door.Centre, door.TimeUnitsSpent));
                    }
                    // A UFO door: the reference waits for it to open (the port opens it at once), then thinks again.
                    if (door.Result == BattleDoorResult.UfoDoorOpened) return false;
                }
                for (var x = size - 1; x >= 0; x--)
                    for (var y = size - 1; y >= 0; y--)
                    {
                        var ahead = step.Position with { X = step.Position.X + x, Y = step.Position.Y + y };
                        if (!Contains(ahead)) continue;
                        // Can't walk into units in this tile, or on top of other units sticking their head into this tile.
                        if (!falling && sight.OverlappingUnit(Map.TileIndex(ahead), OverlapIgnoreSmall) is { } inTheWay && inTheWay != unit)
                            return Cancel(BattleWalkStop.UnitInTheWay);
                    }

                // now start moving
                direction = pathfinder.DequeuePath();
                if (falling) direction = BattleUnit.DirectionDown;
                if (unit.SpendTimeUnits(tu))
                {
                    timeUnitsSpent += tu;
                    if (unit.SpendEnergy(energy))
                    {
                        var from = unit.Position!.Value;
                        WalkStep(rules, unit, direction, step.Position);
                        beforeFirstStep = false;
                        stepDone = true;
                        changed = true;
                        steps++;
                        energySpent += energy;
                        events.Add(new UnitStepped(unit.Id, from, unit.Position!.Value, direction, tu, energy));
                    }
                }
                return false;
            }

            // turning during walking costs no tu
            if (unit.Status == BattleUnitStatus.Turning)
            {
                var paid = 0;
                if (beforeFirstStep)
                {
                    // except before the first step.
                    if (armor.TurnBeforeFirstStep)
                    {
                        var cost = unit.TurnCost(rules);
                        if (unit.SpendTimeUnits(cost)) paid = cost;
                    }
                    else preMovementCost++;
                }
                timeUnitsSpent += paid;
                unit.Turn(false);
                changed = true;
                sight.CalculateFov(unit);
                events.Add(new UnitWalkTurned(unit.Id, unit.Direction, paid));
                if (Spotted())
                {
                    if (beforeFirstStep)
                    {
                        preMovementCost *= unit.TurnCost(rules);
                        if (unit.SpendTimeUnits(preMovementCost)) timeUnitsSpent += preMovementCost;
                    }
                    unit.AbortTurn();
                    return Cancel(BattleWalkStop.SpottedUnits);
                }
            }
            return false;
        }

        var limit = 64 + 16 * (Map.Width + Map.Length + Map.Height);
        for (var cycle = 0; !Think(); cycle++)
            if (cycle > limit) throw new InvalidOperationException($"The walk of battle unit {unit.Id} does not end.");

        if (refusal is not null) return refusal;
        if (!changed)
        {
            // Nothing happened: a walk that could not start is refused.
            var why = stop switch
            {
                BattleWalkStop.PathBlocked => BattleMovementReasons.WalkPathBlocked,
                BattleWalkStop.NotEnoughTimeUnits => BattleMovementReasons.WalkNotEnoughTimeUnits,
                BattleWalkStop.NotEnoughEnergy => BattleMovementReasons.WalkNotEnoughEnergy,
                BattleWalkStop.ReservedTimeUnits => BattleMovementReasons.WalkTimeUnitsReserved,
                BattleWalkStop.UnitInTheWay => BattleMovementReasons.WalkUnitInTheWay,
                _ => null,
            };
            if (why is not null) return why;
        }
        if (stop == BattleWalkStop.SpottedUnits)
            events.Add(new UnitsSpotted(unit.Id, [.. unit.UnitsSpottedThisTurn.Skip(numUnitsSpotted)]));
        events.Add(new UnitWalkEnded(unit.Id, stop, steps, timeUnitsSpent, energySpent, unit.Position!.Value));
        Revision++;
        return null;
    }
}
