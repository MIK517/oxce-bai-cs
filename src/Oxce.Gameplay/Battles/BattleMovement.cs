using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The unit's movement state and primitives (Phase 7 branch 4 S1): <c>BattleUnit</c>'s <c>lookAt</c>, <c>turn</c>,
/// <c>abortTurn</c>, <c>directionTo</c>, the turn and kneel costs and <c>getReactionScore</c>. The animation phases are not
/// state: a step is whole (<see cref="BattleState.WalkStep"/>).
/// </summary>
internal sealed partial class BattleUnit
{
    /// <summary>Pathfinding::DIR_UP and DIR_DOWN: the vertical "directions" of a step.</summary>
    public const int DirectionUp = 8;
    public const int DirectionDown = 9;

    /// <summary>BattleUnit::_toDirection and _toDirectionTurret: where the unit (and its turret) is turning to.</summary>
    public int ToDirection { get; set; }
    public int ToDirectionTurret { get; set; }

    /// <summary>BattleUnit::getFaceDirection: the direction a strafing step ends facing, -1 for none.</summary>
    public int FaceDirection { get; set; } = -1;

    /// <summary>BattleUnit::_verticalDirection: <see cref="DirectionUp"/> or <see cref="DirectionDown"/> during a vertical step, else 0.</summary>
    public int VerticalDirection { get; set; }

    /// <summary>BattleUnit::getLastPosition: where the unit stood before its last step.</summary>
    public BattlePosition? LastPosition { get; set; }

    /// <summary>BattleUnit::_haveNoFloorBelow (<see cref="BattleState.UpdateTileFloorState"/>).</summary>
    public bool HaveNoFloorBelow { get; set; }

    /// <summary>
    /// BattleUnit::getUnitsSpottedThisTurn: the IDs of the units this unit has seen this turn (runtime state the field of view fills;
    /// not saved).
    /// </summary>
    public List<int> UnitsSpottedThisTurn { get; } = [];

    /// <summary>BattleUnit::isBigUnit.</summary>
    public bool IsBigUnit => ArmorSize > 1;

    /// <summary>BattleUnit::getKneelUpCost and getKneelDownCost.</summary>
    public const int KneelUpCost = 8;
    public const int KneelDownCost = 4;

    /// <summary>BattleUnit::getKneelChangeCost: the cost of the transition from the unit's present stance.</summary>
    public int KneelChangeCost => Kneeled ? KneelUpCost : KneelDownCost;

    /// <summary>BattleUnit::getTurnCost: the armor's time units per eighth of a turn.</summary>
    public int TurnCost(RuntimeRuleCatalog rules) => BattleState.ArmorOf(rules, this).TurnCost;

    /// <summary>
    /// BattleUnit::isBrutal: hostile units by <c>brutalAI</c>, neutral ones by <c>brutalCivilians</c>, a player's unit when the AI controls
    /// it (<c>isAIControlled</c>), then the unit rule's <c>isBrutal</c> and <c>isNotBrutal</c> (a soldier has no unit rule).
    /// </summary>
    public bool UsesBrutalAi(RuntimeRuleCatalog rules, BattlePathingOptions options)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(options);
        var brutal = Faction switch
        {
            BattleFaction.Hostile => options.BrutalAi,
            BattleFaction.Neutral => options.BrutalCivilians,
            _ => options.AutoCombat && (!options.AutoCombatControlPerUnit || AllowAutoCombat),
        };
        if (SoldierId is null && rules.Battle.Equipment.Units.TryGetValue(UnitType, out var rule))
        {
            if (rule.IsBrutal) brutal = true;
            if (rule.IsNotBrutal) brutal = false;
        }
        return brutal;
    }

    /// <summary>BattleUnit::getVisible: a player's unit and a unit in an <c>alwaysVisible</c> armor always are, others once the player spotted them.</summary>
    public bool IsVisibleToPlayer(RuntimeRuleCatalog rules) =>
        Faction == BattleFaction.Player || BattleState.ArmorOf(rules, this).AlwaysVisible || IsSpottedBy(BattleFaction.Player);

    /// <summary>BattleUnit::avoidsFire: the unit rule's (a soldier's armor's special ability is below burning floors).</summary>
    public bool AvoidsFire(RuntimeRuleCatalog rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (SoldierId is null && rules.Battle.Equipment.Units.TryGetValue(UnitType, out var rule)) return rule.AvoidsFireOrDefault;
        return BattleState.ArmorOf(rules, this).SpecialAbility < 2;
    }

    /// <summary>BattleUnit::setDirection: the initial placement, no turning left.</summary>
    public void SetDirection(int direction)
    {
        Direction = direction;
        ToDirection = direction;
        DirectionTurret = direction;
        ToDirectionTurret = direction;
    }

    /// <summary>BattleUnit::kneel.</summary>
    public void Kneel(bool kneeled) => Kneeled = kneeled;

    /// <summary>
    /// BattleUnit::directionTo: the direction to a point (0 north, clockwise to 7 north-west) from the unit's position, by dividing the
    /// pie into eighths centred on the directions.
    /// </summary>
    public int DirectionTo(BattlePosition point)
    {
        var from = Position ?? throw new InvalidOperationException($"Battle unit {Id} is not on the map.");
        double ox = point.X - from.X;
        double oy = point.Y - from.Y;
        var angle = Math.Atan2(ox, -oy);
        // Divide the pie in 4 angles each at 1/8th before each quarter.
        var pie = new[]
        {
            Math.PI / 4.0 * 4.0 - Math.PI / 4.0 / 2.0,
            Math.PI / 4.0 * 3.0 - Math.PI / 4.0 / 2.0,
            Math.PI / 4.0 * 2.0 - Math.PI / 4.0 / 2.0,
            Math.PI / 4.0 * 1.0 - Math.PI / 4.0 / 2.0,
        };
        if (angle > pie[0] || angle < -pie[0]) return 4;
        if (angle > pie[1]) return 3;
        if (angle > pie[2]) return 2;
        if (angle > pie[3]) return 1;
        if (angle < -pie[1]) return 5;
        if (angle < -pie[2]) return 6;
        if (angle < -pie[3]) return 7;
        return 0;
    }

    /// <summary>BattleUnit::lookAt(point, turret): sets the direction to turn to; the unit (or turret) turns when it differs.</summary>
    public void LookAt(BattlePosition point, bool turret)
    {
        var direction = DirectionTo(point);
        if (turret)
        {
            ToDirectionTurret = direction;
            if (ToDirectionTurret != DirectionTurret) Status = BattleUnitStatus.Turning;
        }
        else
        {
            ToDirection = direction;
            if (ToDirection != Direction && ToDirection is < 8 and > -1) Status = BattleUnitStatus.Turning;
        }
    }

    /// <summary>BattleUnit::lookAt(direction, force): turn to a direction (ignored outside 0-7), or with <paramref name="force"/> set it at once.</summary>
    public void LookAt(int direction, bool force)
    {
        if (!force)
        {
            if (direction is < 0 or >= 8) return;
            ToDirection = direction;
            if (ToDirection != Direction) Status = BattleUnitStatus.Turning;
        }
        else
        {
            ToDirection = direction;
            Direction = direction;
        }
    }

    /// <summary>
    /// BattleUnit::turn: one eighth towards the direction to turn to, the shorter way (a half turn goes clockwise, the way a positive
    /// difference of four does); a unit with a turret turns it with the body. The unit stands when it has arrived.
    /// </summary>
    public void Turn(bool turret)
    {
        int difference;
        if (turret)
        {
            if (DirectionTurret == ToDirectionTurret)
            {
                AbortTurn();
                return;
            }
            difference = ToDirectionTurret - DirectionTurret;
        }
        else
        {
            if (Direction == ToDirection)
            {
                AbortTurn();
                return;
            }
            difference = ToDirection - Direction;
        }

        if (difference != 0)
        {
            var clockwise = difference > 0 ? difference <= 4 : difference <= -4;
            var step = clockwise ? 1 : -1;
            if (turret) DirectionTurret += step;
            else
            {
                if (TurretType > -1) DirectionTurret += step;
                Direction += step;
            }
            if (Direction < 0) Direction = 7;
            if (Direction > 7) Direction = 0;
            if (DirectionTurret < 0) DirectionTurret = 7;
            if (DirectionTurret > 7) DirectionTurret = 0;
        }

        if (turret)
        {
            if (ToDirectionTurret == DirectionTurret) Status = BattleUnitStatus.Standing;
        }
        else if (ToDirection == Direction || Status == BattleUnitStatus.Unconscious) Status = BattleUnitStatus.Standing;
    }

    /// <summary>BattleUnit::abortTurn.</summary>
    public void AbortTurn() => Status = BattleUnitStatus.Standing;

    /// <summary>BattleUnit::spendTimeUnits: spends the time units when the unit has them, else nothing.</summary>
    public bool SpendTimeUnits(int timeUnits)
    {
        if (timeUnits > TimeUnits) return false;
        TimeUnits -= timeUnits;
        return true;
    }

    /// <summary>BattleUnit::spendEnergy: spends the energy when the unit has it, else nothing.</summary>
    public bool SpendEnergy(int energy)
    {
        if (energy > Energy) return false;
        Energy -= energy;
        return true;
    }

    /// <summary>
    /// BattleUnit::getHitState: the unit was hit since a unit of its side was last selected. Runtime state (not saved) that a player unit's
    /// reaction reads in <c>getSpottingUnits</c>; the damage that sets it and the selection that resets it belong to branch 5.
    /// </summary>
    public bool HitState { get; set; }

    /// <summary>
    /// BattleUnit::getReactionScore: the reactions statistic scaled by the share of time units left. A unit with no time units
    /// statistic divides by zero, as the reference does (infinity or not a number).
    /// </summary>
    public double ReactionScore => Stats.Reactions * (double)TimeUnits / Stats.TimeUnits;
}

internal sealed partial class BattleState
{
    /// <summary>BattleUnit::getStandHeight, getKneelHeight and getFloatHeight: the armor's, else the soldier's or the unit rule's.</summary>
    internal static RuntimeUnitHeights HeightsOf(RuntimeRuleCatalog rules, BattleUnit unit)
    {
        var armor = ArmorOf(rules, unit);
        var equipment = rules.Battle.Equipment;
        var own = unit.SoldierId is not null
            ? equipment.SoldierHeights.GetValueOrDefault(unit.UnitType)
            : equipment.Units.TryGetValue(unit.UnitType, out var unitRule) ? unitRule.Heights : null;
        if (own is null) throw new InvalidOperationException($"Battle unit {unit.Id} is a {unit.UnitType}, which does not exist.");
        return new RuntimeUnitHeights(armor.StandHeight == -1 ? own.Stand : armor.StandHeight, armor.KneelHeight == -1 ? own.Kneel : armor.KneelHeight,
            armor.FloatHeight == -1 ? own.Floating : armor.FloatHeight);
    }

    /// <summary>
    /// BattleUnit::updateTileFloorState: whether nothing holds the unit up. A big unit has floor when any of its tiles does; a small
    /// one has none when its tile has none and no ladder. A unit on no tile has floor.
    /// </summary>
    internal void UpdateTileFloorState(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.Position is not { } position)
        {
            unit.HaveNoFloorBelow = false;
            return;
        }
        unit.HaveNoFloorBelow = true;
        if (unit.IsBigUnit)
        {
            var last = unit.ArmorSize - 1;
            for (var x = last; x >= 0; x--)
                for (var y = last; y >= 0; y--)
                {
                    var covered = position with { X = position.X + x, Y = position.Y + y };
                    if (Contains(covered) && !Map.HasNoFloor(covered))
                    {
                        unit.HaveNoFloorBelow = false;
                        return;
                    }
                }
        }
        else unit.HaveNoFloorBelow &= Map.HasNoFloor(position) && !Map.HasLadder(Map.TileIndex(position));
    }

    /// <summary>
    /// BattleUnit::setTile: the unit moves to a tile. The tiles it leaves name it no more, the tiles under its armor size from the new
    /// one do (whoever stood there is replaced, as the reference does), the floor state is updated, and a unit mid-step takes to the air or
    /// lands as the floor requires. Nothing happens for the tile it is on.
    /// </summary>
    internal void SetTile(BattleUnit unit, BattlePosition tile)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!_units.Contains(unit)) throw new InvalidOperationException($"Battle unit {unit.Id} is not in this battle.");
        if (unit.InventoryTile is not null) throw new InvalidOperationException($"Battle unit {unit.Id} has not started its first turn.");
        if (!Contains(tile)) throw new InvalidOperationException($"Tile {tile} is outside the map.");
        if (unit.Position == tile) return;

        if (unit.Position is { } previous)
            foreach (var covered in BattleMap.Footprint(previous, unit.ArmorSize))
                if (Contains(covered) && Map.UnitAt(Map.TileIndex(covered)) == unit.Id) Map.SetUnitAt(Map.TileIndex(covered), null);
        unit.Position = tile;
        UpdateTileFloorState(unit);
        foreach (var covered in BattleMap.Footprint(tile, unit.ArmorSize))
            if (Contains(covered)) Map.SetUnitAt(Map.TileIndex(covered), unit.Id);

        // The unit could have changed from flying to walking or the reverse.
        if (unit.Status == BattleUnitStatus.Walking && unit.HaveNoFloorBelow && unit.MovementType == MovementTypeFly)
        {
            unit.Status = BattleUnitStatus.Flying;
            unit.Floating = true;
        }
        else if (unit.Status == BattleUnitStatus.Flying && !unit.HaveNoFloorBelow && unit.VerticalDirection == 0)
        {
            unit.Status = BattleUnitStatus.Walking;
            unit.Floating = false;
        }
        else if (unit.Status == BattleUnitStatus.Unconscious) unit.Floating = unit.MovementType == MovementTypeFly && unit.HaveNoFloorBelow;
        Revision++;
    }

    /// <summary>MovementType MT_FLY.</summary>
    internal const int MovementTypeFly = 1;

    /// <summary>
    /// A whole step of <c>BattleUnit::startWalking</c> and <c>keepWalking</c> (the animation phases collapsed): the unit faces the
    /// direction (a vertical step keeps its facing), stops kneeling, takes to the air when it has no floor or climbs, arrives on the
    /// destination tile, lands when it has floor there, finishes a strafing step facing its face direction and gains the motion points
    /// of a step. The cost, doors, other units and falls belong to the walk that calls this.
    /// </summary>
    internal void WalkStep(RuntimeRuleCatalog rules, BattleUnit unit, int direction, BattlePosition destination)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(unit);
        if (direction is < 0 or > BattleUnit.DirectionDown) throw new ArgumentOutOfRangeException(nameof(direction));
        if (unit.Position is not { } start) throw new InvalidOperationException($"Battle unit {unit.Id} is not on the map.");

        // startWalking
        if (direction >= BattleUnit.DirectionUp)
        {
            unit.VerticalDirection = direction;
            unit.Status = BattleUnitStatus.Flying;
        }
        else
        {
            unit.Direction = direction;
            unit.Status = BattleUnitStatus.Walking;
        }
        if (unit.HaveNoFloorBelow || direction >= BattleUnit.DirectionUp)
        {
            unit.Status = BattleUnitStatus.Flying;
            unit.Floating = true;
        }
        else unit.Floating = false;
        unit.LastPosition = start;
        unit.Kneeled = false;

        // keepWalking, to the end of the cycle
        SetTile(unit, destination);
        if (unit.Floating && !unit.HaveNoFloorBelow) unit.Floating = false;
        unit.Status = BattleUnitStatus.Standing;
        unit.VerticalDirection = 0;
        if (unit.FaceDirection >= 0)
        {
            unit.Direction = unit.FaceDirection;
            unit.FaceDirection = -1;
        }
        // Motion points for the motion scanner blips: a big unit 30, a tall small one 4, else 3.
        unit.MotionPoints += unit.IsBigUnit ? 30 : HeightsOf(rules, unit).Stand > 16 ? 4 : 3;
        Revision++;
    }
}
