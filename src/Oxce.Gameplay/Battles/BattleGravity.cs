using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Gravity and falling (Phase 7 branch 4 S7): <c>TileEngine::applyGravity</c> and the fall routine of <c>UnitFallBState</c>. The animation
/// is collapsed: each cycle every falling unit that stands still either takes its next step down, or has landed. A fall onto a unit that finds no
/// escape knocks it out, and the casualty check that follows takes it out; a landing draws the reaction fire (S9) the live fall runs.
/// A landed unit's proximity check (S8, <c>BattleProximity.cs</c>) may start an explosion.
/// </summary>
internal sealed partial class BattleState
{
    private readonly List<BattleUnit> _fallingUnits = [];

    /// <summary>SavedBattleGame::getFallingUnits: the units that fall, the most recently added first. Runtime state, not saved.</summary>
    internal IReadOnlyList<BattleUnit> FallingUnits => _fallingUnits;

    private const int DeathTraps = 200;
    private const int ProximityGrenadeBattleType = 5;

    /// <summary>A unit on the map that does not fly, has no floor under it and is not out (what the battlescape's constructor queues to fall).</summary>
    private static bool IsFloorlessWalker(BattleUnit unit) =>
        unit.Position is not null && unit.HaveNoFloorBelow && !unit.IsOutThresholdExceeded && unit.MovementType != MovementTypeFly;

    /// <summary>Whether a unit that does not fly has no floor under it (<see cref="IsFloorlessWalker"/>).</summary>
    internal bool HasFloorlessWalker() => _units.Any(IsFloorlessWalker);

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
        var occupant = UnitOnTile(index);
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
            if (item.CorpseUnitId is { } corpse && UnitById(corpse) is { } body && (body.Position ?? body.SavedPosition) == tile)
            {
                // setPosition(rt) sets the last position too. A body off the map (out) keeps its place in the saved position.
                body.LastPosition = body.Position ?? body.SavedPosition;
                SetLyingPosition(body, landing);
            }
            if (landing != tile) AddToTile(item, landing);
        }
        if (landing != tile || occupant is not null) MarkChanged();
        return landing;
    }

    /// <summary>
    /// What stops <see cref="ResolveFalls"/> for the units now falling, and for <paramref name="extra"/> when it is not null and has no
    /// floor (the occupant <see cref="ApplyGravity"/> would add), before anything changes: null when the falls can run. The knock-outs of a fall
    /// are not tried (they refuse nothing).
    /// </summary>
    internal CampaignReason? FallsRefusal(BattleUnit? extra = null)
    {
        if (_fallingUnits.Count == 0 && extra is null) return null;
        var saved = new FallSnapshot(this);
        try
        {
            if (extra is not null)
            {
                UpdateTileFloorState(extra);
                if (IsFloorlessWalker(extra)) AddFallingUnit(extra);
            }
            return RunFalls(dryRun: true);
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
    internal CampaignReason? ResolveFalls()
    {
        if (_fallingUnits.Count == 0) return null;
        // (Only a walk reads whether a landing drew a reaction: a fall outside one leaves nothing behind.)
        var insideWalk = _deferredBack is not null;
        var refusal = RunFalls(dryRun: false);
        if (!insideWalk) _reactionDuringFall = false;
        MarkChanged();
        return refusal;
    }

    /// <summary><see cref="ResolveFalls(RuntimeRuleCatalog)"/>, raising a <see cref="UnitFell"/> for each unit that ended elsewhere; <paramref name="moved"/> when any did.</summary>
    internal CampaignReason? ResolveFalls(out bool moved)
    {
        var before = _units.Select(static unit => (unit, unit.Position)).ToArray();
        var refusal = ResolveFalls();
        moved = false;
        foreach (var (unit, was) in before)
            if (unit.Position is { } now && was is { } from && now != from)
            {
                Events.Add(new UnitFell(unit.Id, from, now));
                moved = true;
            }
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
            if (IsFloorlessWalker(unit)) AddFallingUnit(unit);
    }

    private CampaignReason? RunFalls(bool dryRun)
    {
        var tilesToFallInto = new List<int>();
        var unitsToMove = new List<BattleUnit>();
        BattlePathfinder? pathfinder = null;
        var limit = 4 * (Map.Height + 2) * (_units.Count + 1);
        // What the fall pushes behind itself (statePushNext), in the order it pushes: a landed unit's proximity explosions, then each round's knock-out deaths. The list runs the last
        // push first when the fall state is done.
        var pushes = new List<QueuedState>();
        // The reactions a landed unit draws go to the back of the list, behind the deaths and explosions (statePushBack); a fall inside a walk leaves them to the walk.
        var holdsBack = !dryRun && HoldBackPushes();
        CampaignReason? stop = null;
        for (var round = 0; _fallingUnits.Count != 0 && stop is null; round++)
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
                        if (UnitOnTile(tile) is { } below &&
                            !_fallingUnits.Contains(below) && !unitsToMove.Contains(below))
                            unitsToMove.Add(below);
                    if (unit.Status == BattleUnitStatus.Standing) steps.Add((unit, BattleUnit.DirectionDown, position with { Z = position.Z - 1 }));
                }
                else if (unit.Status == BattleUnitStatus.Standing)
                {
                    if (Land(unit, dryRun, pushes) is { } refusal)
                    {
                        stop = refusal;
                        break;
                    }
                    _fallingUnits.Remove(unit);
                }
            }

            if (stop is not null) break;

            // Find somewhere to move the units in danger of being squashed.
            var escapeTiles = new List<int>();
            var hadUnitsToMove = unitsToMove.Count != 0;
            while (unitsToMove.Count != 0)
            {
                var below = unitsToMove[0];
                unitsToMove.RemoveAt(0);
                pathfinder ??= new BattlePathfinder(this, RequiredRules);
                if (FindEscape(pathfinder, below, tilesToFallInto, escapeTiles) is not { } escape)
                {
                    // STOMP THAT GOOMBA!
                    if (!dryRun) KnockOut(below);
                    continue;
                }
                AddFallingUnit(below);
                steps.Add((below, escape.Direction, escape.Destination));
            }
            // statePushNext puts every push right behind the running fall state, so a later push (a later round's death or landing) runs before an earlier one.
            if (hadUnitsToMove && !dryRun) QueueCasualties(null, default, pushNext: death => pushes.Add(new DeathState(this, death)));

            // The steps complete: the units that moved aside are ahead of those that fall, as the list orders them.
            foreach (var (unit, direction, destination) in steps.OrderBy(step => _fallingUnits.IndexOf(step.Unit)))
                WalkStep(unit, direction, destination);
        }
        if (dryRun) return stop;
        // The deaths and the proximity explosions run behind the fall state; the explosions a unit that blows itself up, or a proximity grenade, push run in turn.
        PushBehindRunning(pushes);
        if (holdsBack) ReleaseBackPushes();
        RunStates();
        if (TakeExplosionRefusal() is { } explosionRefusal) return explosionRefusal;
        // The falls the deaths start (a converted unit, a corpse over a hole) are a fall state of their own.
        return stop ?? (_fallingUnits.Count != 0 ? RunFalls(dryRun: false) : null);
    }

    private (int Direction, BattlePosition Destination)? FindEscape(BattlePathfinder pathfinder, BattleUnit unitBelow, List<int> tilesToFallInto,
        List<int> escapeTiles)
    {
        var position = unitBelow.Position!.Value;
        var total = unitBelow.ArmorSize * unitBelow.ArmorSize;
        var offsets = BattleGeometry.BigUnitOffsets;
        for (var direction = 0; direction < BattleUnit.DirectionUp; direction++)
        {
            var (dx, dy) = BattleGeometry.DirectionVector(direction);
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
    private int SpecialAbilityOf(BattleUnit unit)
    {
        var armor = ArmorOf(unit).SpecialAbility;
        if (armor != 0 || unit.SoldierId is not null) return armor;
        return RequiredRules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var rule) ? rule.SpecialAbility : 0;
    }

    /// <summary>
    /// A unit that has stopped falling (<c>UnitFallBState::think</c>'s else branch): a floor-burning unit burns the floor under it (a hit that reaches a unit or starts an explosion refuses),
    /// <see cref="CheckForProximityGrenades"/> runs (its explosions are collected in <paramref name="explosions"/> for the fall to push behind itself; a dry run does not run it, as it
    /// draws and changes items, but refuses a landing that could start one); then its light moves with it (the items' layer when the check changed anything), everyone's sight of the area is updated, its own tiles are, and what
    /// <c>checkReactionFire</c> spots happens (a live fall draws the reactions; a dry run reacts to nothing).
    /// </summary>
    private CampaignReason? Land(BattleUnit unit, bool dryRun, List<QueuedState> explosions)
    {
        var position = unit.Position!.Value;
        // If the unit burns floor tiles, burn floor tiles (a hole it burns leaves it standing: gravity queued it, and it leaves the list below).
        if (BurnsFloor(unit) && !dryRun && BurnFloor(unit) is { } burnRefusal) return burnRefusal;
        // The dry run is for the callers that act on the live battle (an inventory move, a loaded battle, the commands' gate): the check draws and changes items, and the explosion it
        // starts has knock-on effects that can refuse after changes, so a landing that could start one is refused before any (the commands that run on a trial copy meet it there).
        if (dryRun && ProximityMayExplode(unit)) return BattleMovementReasons.ProximityExplosionOnFallUnsupported;
        var change = dryRun ? 0 : CheckForProximityGrenades(unit, explosions);
        if (dryRun) return null;

        CalculateLighting(change != 0 ? BattleLightLayer.Items : BattleLightLayer.Units, position, 2);
        var sight = Sight();
        sight.CalculateFov(position, 2, updateTiles: false);
        sight.CalculateFov(unit, true, false);
        // checkReactionFire(unit, fall): the action is a walk of the normal move type. A reaction aborts the path of the walk the fall belongs to.
        if (CheckReactionFire(unit, new BattleReactionOrigin(BattleActionType.Walk))) _reactionDuringFall = true;
        return null;
    }

    /// <summary>The unit and map state a fall changes, taken before a trial run and put back after it.</summary>
    private sealed class FallSnapshot
    {
        private readonly BattleUnit[] _falling;
        private readonly int[] _occupants;
        private readonly (long Committed, bool Pending) _revision;
        private readonly (BattleUnit Unit, BattlePosition? Position, BattlePosition? Last, BattleUnitStatus Status, bool Floating, bool NoFloor, int Vertical,
            bool Kneeled, int Face, int Motion, int Direction)[] _units;

        public FallSnapshot(BattleState battle)
        {
            _falling = [.. battle._fallingUnits];
            _occupants = battle.Map.CaptureUnits();
            _revision = battle.CaptureRevision();
            _units = [.. battle._units.Select(static unit => (unit, unit.Position, unit.LastPosition, unit.Status, unit.Floating, unit.HaveNoFloorBelow,
                unit.VerticalDirection, unit.Kneeled, unit.FaceDirection, unit.MotionPoints, unit.Direction))];
        }

        public void Restore(BattleState battle)
        {
            battle._fallingUnits.Clear();
            battle._fallingUnits.AddRange(_falling);
            battle.Map.RestoreUnits(_occupants);
            battle.RestoreRevision(_revision);
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
