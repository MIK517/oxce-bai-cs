using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>The reasons gravity and falling refuse before any change (Phase 7 branch 4 S7).</summary>
internal static class BattleGravityReasons
{
    internal static readonly CampaignReason FallKnockOutUnsupported = new("battle.fall-knockout-unsupported",
        "A falling unit would land on a unit that cannot move out of the way and knock it out; the knock-out and the casualty check that follows arrive with turn processing (Phase 7 branch 6).");

    internal static readonly CampaignReason ProximityUnsupported = new("battle.proximity-unsupported",
        "A unit would land next to an item whose fuse reacts to nearby units, or on a death trap tile; proximity explosions arrive with the attack work (Phase 7 branch 5).");
}

/// <summary>
/// Gravity and falling (Phase 7 branch 4 S7): <c>TileEngine::applyGravity</c> and the fall routine of <c>UnitFallBState</c>. The animation
/// is collapsed: each cycle every falling unit that stands still either takes its next step down, or has landed. A fall that would knock
/// out the unit it lands on (no escape) is refused before any change, as are a landing the reaction fire (branch 5) or a proximity
/// explosion (branch 5) could follow.
/// </summary>
internal sealed partial class BattleState
{
    private readonly List<BattleUnit> _fallingUnits = [];

    /// <summary>SavedBattleGame::getFallingUnits: the units that fall, the most recently added first. Runtime state, not saved.</summary>
    internal IReadOnlyList<BattleUnit> FallingUnits => _fallingUnits;

    private const int DeathTraps = 200;
    private const int ProximityGrenadeBattleType = 5;

    /// <summary>Whether a unit that does not fly has no floor under it (what the battlescape's constructor queues to fall).</summary>
    internal bool HasFloorlessWalker() => _units.Any(static unit =>
        unit.Position is not null && unit.HaveNoFloorBelow && !unit.IsOutThresholdExceeded && unit.MovementType != MovementTypeFly);

    /// <summary>Whether any unit could fall when the first turn starts: one is queued, or a unit that does not fly has no floor under it.</summary>
    internal bool MayFall() => _fallingUnits.Count != 0 || HasFloorlessWalker();

    /// <summary>SavedBattleGame::addFallingUnit: a unit joins the front of the list once.</summary>
    internal bool AddFallingUnit(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_fallingUnits.Contains(unit)) return false;
        _fallingUnits.Insert(0, unit);
        return true;
    }

    /// <summary>
    /// TileEngine::applyGravity: the unit on the tile starts to fall (a flying one floats where it is), and the items on the tile drop
    /// to the lowest tile below with a floor, taking along the unit of a corpse item that lies where the tile's unit position is. Returns
    /// the tile the items end up on. The fall itself is <see cref="ResolveFalls"/>.
    /// </summary>
    internal BattlePosition? ApplyGravity(BattlePosition? at)
    {
        if (at is not { } tile) return null;
        var index = Map.TileIndex(tile);
        var occupant = Map.UnitAt(index) is { } id ? _units.Find(unit => unit.Id == id) : null;
        var items = ItemsOnTile(tile).ToArray();
        if (items.Length == 0 && occupant is null) return tile;

        if (occupant is not null)
        {
            UpdateTileFloorState(occupant);
            if (occupant.HaveNoFloorBelow && !occupant.IsOutThresholdExceeded)
            {
                if (occupant.MovementType == MovementTypeFly)
                {
                    // startWalking onto the position it has: not kneeling, floating; then abortTurn (standing).
                    occupant.LastPosition = occupant.Position;
                    occupant.Kneeled = false;
                    occupant.Floating = true;
                    occupant.AbortTurn();
                }
                else
                {
                    // setPosition(getPosition()) sets the last position.
                    occupant.LastPosition = occupant.Position;
                    AddFallingUnit(occupant);
                }
            }
        }

        var landing = tile;
        while (landing.Z > 0 && Map.HasNoFloor(landing)) landing = landing with { Z = landing.Z - 1 };
        foreach (var item in items)
        {
            if (item.CorpseUnitId is { } corpse && _units.Find(unit => unit.Id == corpse) is { } body && body.Position == tile)
            {
                // setPosition(rt) sets the last position too.
                body.LastPosition = body.Position;
                body.Position = landing;
            }
            if (landing != tile) AddToTile(item, landing);
        }
        if (landing != tile || occupant is not null) Revision++;
        return landing;
    }

    /// <summary>
    /// What stops <see cref="ResolveFalls"/> for the units now falling, and for <paramref name="extra"/> when it is not null and has no
    /// floor (the occupant <see cref="ApplyGravity"/> would add), before anything changes: null when the falls can run.
    /// </summary>
    internal CampaignReason? FallsRefusal(RuntimeRuleCatalog rules, BattleUnit? extra = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (_fallingUnits.Count == 0 && extra is null) return null;
        var saved = new FallSnapshot(this);
        try
        {
            if (extra is not null)
            {
                UpdateTileFloorState(extra);
                if (extra.HaveNoFloorBelow && !extra.IsOutThresholdExceeded && extra.MovementType != MovementTypeFly) AddFallingUnit(extra);
            }
            return RunFalls(rules, dryRun: true);
        }
        finally
        {
            saved.Restore(this);
        }
    }

    /// <summary>
    /// <c>UnitFallBState</c> to its end: the falling units fall level by level, the units below move out of the way, and each landed unit
    /// has its light and the sight of those who see it updated (and spots as <c>checkReactionFire</c> does). The commands check
    /// <see cref="FallsRefusal"/> first (or run this on a trial copy); a refusal returned here has left the falls half done.
    /// </summary>
    internal CampaignReason? ResolveFalls(RuntimeRuleCatalog rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (_fallingUnits.Count == 0) return null;
        var refusal = RunFalls(rules, dryRun: false);
        Revision++;
        return refusal;
    }

    /// <summary>
    /// The queue a battle starting its first turn has: the battlescape's constructor (<c>checkForCasualties</c>, BattlescapeGame.cpp:194 and
    /// 1026-1030) queues every unit that does not fly and has no floor, before the briefing's spawns queue theirs; the list is runtime state a save
    /// does not keep, so the units are queued again, in unit order (each goes to the front, as the spawns do).
    /// </summary>
    internal void RequeueFallingUnits()
    {
        _fallingUnits.Clear();
        foreach (var unit in _units)
            if (unit.Position is not null && unit.HaveNoFloorBelow && !unit.IsOutThresholdExceeded && unit.MovementType != MovementTypeFly)
                AddFallingUnit(unit);
    }

    private CampaignReason? RunFalls(RuntimeRuleCatalog rules, bool dryRun)
    {
        var tilesToFallInto = new List<int>();
        var unitsToMove = new List<BattleUnit>();
        BattlePathfinder? pathfinder = null;
        var limit = 4 * (Map.Height + 2) * (_units.Count + 1);
        for (var round = 0; _fallingUnits.Count != 0; round++)
        {
            if (round > limit) throw new InvalidOperationException("The falling units do not come to rest.");
            var steps = new List<(BattleUnit Unit, int Direction, BattlePosition Destination)>();
            foreach (var unit in _fallingUnits.ToArray())
            {
                // I ain't got time to panic.
                if (unit.Status is BattleUnitStatus.Turning or BattleUnitStatus.Panicking or BattleUnitStatus.Berserk) unit.AbortTurn();
                if (unit.IsOutThresholdExceeded)
                {
                    _fallingUnits.Remove(unit);
                    continue;
                }
                if (unit.Status is BattleUnitStatus.Walking or BattleUnitStatus.Flying) continue;
                if (unit.Position is not { } position) throw new InvalidOperationException($"Battle unit {unit.Id} falls but is not on the map.");
                var falling = unit.HaveNoFloorBelow && position.Z != 0 && unit.MovementType != MovementTypeFly;
                if (falling)
                {
                    for (var x = unit.ArmorSize - 1; x >= 0; x--)
                        for (var y = unit.ArmorSize - 1; y >= 0; y--)
                            tilesToFallInto.Add(Map.TileIndex(new BattlePosition(position.X + x, position.Y + y, position.Z - 1)));
                    foreach (var tile in tilesToFallInto)
                        if (Map.UnitAt(tile) is { } belowId && _units.Find(candidate => candidate.Id == belowId) is { } below &&
                            !_fallingUnits.Contains(below) && !unitsToMove.Contains(below))
                            unitsToMove.Add(below);
                    if (unit.Status == BattleUnitStatus.Standing) steps.Add((unit, BattleUnit.DirectionDown, position with { Z = position.Z - 1 }));
                }
                else if (unit.Status == BattleUnitStatus.Standing)
                {
                    if (Land(rules, unit, dryRun) is { } refusal) return refusal;
                    _fallingUnits.Remove(unit);
                }
            }

            // Find somewhere to move the units in danger of being squashed.
            var escapeTiles = new List<int>();
            while (unitsToMove.Count != 0)
            {
                var below = unitsToMove[0];
                unitsToMove.RemoveAt(0);
                pathfinder ??= new BattlePathfinder(this, rules);
                if (FindEscape(pathfinder, below, tilesToFallInto, escapeTiles) is not { } escape) return BattleGravityReasons.FallKnockOutUnsupported;
                AddFallingUnit(below);
                steps.Add((below, escape.Direction, escape.Destination));
            }

            // The steps complete: the units that moved aside are ahead of those that fall, as the list orders them.
            foreach (var (unit, direction, destination) in steps.OrderBy(step => _fallingUnits.IndexOf(step.Unit)))
                WalkStep(rules, unit, direction, destination);
        }
        return null;
    }

    private (int Direction, BattlePosition Destination)? FindEscape(BattlePathfinder pathfinder, BattleUnit unitBelow, List<int> tilesToFallInto,
        List<int> escapeTiles)
    {
        var position = unitBelow.Position!.Value;
        var total = unitBelow.ArmorSize * unitBelow.ArmorSize;
        ReadOnlySpan<(int X, int Y)> offsets = [(0, 0), (1, 0), (0, 1), (1, 1)];
        for (var direction = 0; direction < BattleUnit.DirectionUp; direction++)
        {
            var (dx, dy) = BattleBlockage.DirectionVector(direction);
            if (!pathfinder.GetTuCost(position, direction, unitBelow, BattleActionMove.Normal).IsValid) continue;
            for (var section = 0; section < total;)
            {
                var end = new BattlePosition(position.X + offsets[section].X + dx, position.Y + offsets[section].Y + dy, position.Z);
                if (!Contains(end)) break;
                var tile = Map.TileIndex(end);
                var aboutToBeOccupied = tilesToFallInto.Contains(tile);
                var alreadyTaken = escapeTiles.Contains(tile);
                var occupied = Map.UnitAt(tile) is { } other && other != unitBelow.Id;
                var hasFloor = !Map.HasNoFloor(tile);
                if (occupied || alreadyTaken || aboutToBeOccupied || !(hasFloor || unitBelow.MovementType == MovementTypeFly)) break;
                section++;
                if (section != total) continue;
                // Now ensure no other unit escapes to here too (the reference reaches from the last section's tile).
                for (var x = unitBelow.ArmorSize - 1; x >= 0; x--)
                    for (var y = unitBelow.ArmorSize - 1; y >= 0; y--)
                    {
                        var taken = new BattlePosition(end.X + x, end.Y + y, end.Z);
                        if (Contains(taken)) escapeTiles.Add(Map.TileIndex(taken));
                    }
                return (direction, position with { X = position.X + dx, Y = position.Y + dy });
            }
        }
        return null;
    }

    /// <summary>BattleUnit's <c>_specab</c>: the armor's, or when it has none the unit rule's (a soldier has no unit rule).</summary>
    private static int SpecialAbilityOf(RuntimeRuleCatalog rules, BattleUnit unit)
    {
        var armor = ArmorOf(rules, unit).SpecialAbility;
        if (armor != 0 || unit.SoldierId is not null) return armor;
        return rules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var rule) ? rule.SpecialAbility : 0;
    }

    /// <summary>
    /// A unit that has stopped falling (<c>UnitFallBState::think</c>'s else branch): a floor-burning unit burns the floor under it (a hit that reaches a unit or starts an explosion refuses), and a proximity explosion
    /// refuse; otherwise its light moves with it, everyone's sight of the area is updated, its own tiles are, and what
    /// <c>checkReactionFire</c> spots happens (a reactor refuses).
    /// </summary>
    private CampaignReason? Land(RuntimeRuleCatalog rules, BattleUnit unit, bool dryRun)
    {
        var position = unit.Position!.Value;
        // If the unit burns floor tiles, burn floor tiles (a hole it burns leaves it standing: gravity queued it, and it leaves the list below).
        if (BurnsFloor(rules, unit) && BurnFloor(rules, unit, dryRun) is { } burnRefusal) return burnRefusal;
        if (ProximityEffects(rules, unit)) return BattleGravityReasons.ProximityUnsupported;
        var reacts = unit.Faction == Side;
        // The trial copies of the first turn run this too (not a dry run), so the reactor test is part of both.
        if (reacts && HasPossibleReactor(rules, unit)) return BattleMovementReasons.ReactionFireUnsupported;
        if (dryRun) return null;

        // checkForProximityGrenades returned 0, so the units' light is what changes.
        CalculateLighting(rules, BattleLightLayer.Units, position, 2);
        var sight = Sight();
        sight.CalculateFov(position, 2, updateTiles: false);
        sight.CalculateFov(unit, true, false);
        if (reacts) SpotMover(rules, sight, unit);
        return null;
    }

    /// <summary>
    /// Whether <c>BattlescapeGame::checkForProximityGrenades</c> could change anything for the unit on its tile: a death trap floor under it
    /// whose item rule exists, or an item on a tile around it whose fuse nearby units trigger (<c>proximityTrigger</c>), or that is lit and explodes or is removed near units (a
    /// proximity grenade, <c>proximityExplode</c>)
    /// (<c>fuseProximityEvent</c> enables the fuse, may explode, or removes the item). Over-approximates: no chance is rolled.
    /// </summary>
    private bool ProximityEffects(RuntimeRuleCatalog rules, BattleUnit unit)
    {
        var position = unit.Position!.Value;
        foreach (var covered in BattleMap.Footprint(position, unit.ArmorSize))
            if (Contains(covered) && Map.FloorSpecialType(Map.TileIndex(covered)) is var special && special >= DeathTraps &&
                rules.Items.TryGet($"STR_DEATH_TRAP_{special}", out _))
                return true;
        for (var x = -1; x <= unit.ArmorSize; x++)
            for (var y = -1; y <= unit.ArmorSize; y++)
            {
                var around = new BattlePosition(position.X + x, position.Y + y, position.Z);
                if (!Contains(around)) continue;
                foreach (var item in ItemsOnTile(around))
                {
                    var trigger = rules.Battle.Equipment.Items[item.ItemType].FuseTrigger;
                    if (trigger.ProximityTrigger || item.FuseEnabled && item.FuseTimer >= 0 &&
                        (trigger.ProximityExplode || trigger.DefaultBehavior && ItemRuleOf(rules, item.ItemType).BattleType == ProximityGrenadeBattleType))
                        return true;
                }
            }
        return false;
    }

    /// <summary>The unit and map state a fall changes, taken before a trial run and put back after it.</summary>
    private sealed class FallSnapshot
    {
        private readonly BattleUnit[] _falling;
        private readonly int[] _occupants;
        private readonly long _revision;
        private readonly (BattleUnit Unit, BattlePosition? Position, BattlePosition? Last, BattleUnitStatus Status, bool Floating, bool NoFloor, int Vertical,
            bool Kneeled, int Face, int Motion, int Direction)[] _units;

        public FallSnapshot(BattleState battle)
        {
            _falling = [.. battle._fallingUnits];
            _occupants = battle.Map.CaptureUnits();
            _revision = battle.Revision;
            _units = [.. battle._units.Select(static unit => (unit, unit.Position, unit.LastPosition, unit.Status, unit.Floating, unit.HaveNoFloorBelow,
                unit.VerticalDirection, unit.Kneeled, unit.FaceDirection, unit.MotionPoints, unit.Direction))];
        }

        public void Restore(BattleState battle)
        {
            battle._fallingUnits.Clear();
            battle._fallingUnits.AddRange(_falling);
            battle.Map.RestoreUnits(_occupants);
            battle.Revision = _revision;
            foreach (var (unit, position, last, status, floating, noFloor, vertical, kneeled, face, motion, direction) in _units)
            {
                unit.Position = position;
                unit.LastPosition = last;
                unit.Status = status;
                unit.Floating = floating;
                unit.HaveNoFloorBelow = noFloor;
                unit.VerticalDirection = vertical;
                unit.Kneeled = kneeled;
                unit.FaceDirection = face;
                unit.MotionPoints = motion;
                unit.Direction = direction;
            }
        }
    }
}
