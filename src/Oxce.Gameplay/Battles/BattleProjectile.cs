using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// A projectile's flight (<c>Projectile</c>, Phase 7 branch 5 S2): the trajectory of a straight shot (<c>calculateTrajectory</c>) or of a throw and
/// an arcing shot (<c>calculateThrow</c>), with the classic <c>applyAccuracy</c> that turns the shooter's accuracy into a deviation of the target
/// voxel. It draws from the battle's random stream, in the reference's order. The realistic accuracy (<c>battleRealisticAccuracy</c>), the
/// alternative grenade throw (<c>battleAltGrenades</c>) and the display and animation members belong to options that are off or to Phase 8.
/// </summary>
internal sealed class BattleProjectile
{
    private readonly BattleState _battle;
    private readonly RuntimeRuleCatalog _rules;
    private readonly BattleMap _map;
    private readonly BattleSight _sight;
    private readonly BattleAimAction _action;
    private readonly BattlePosition _origin;
    private BattlePosition _targetVoxel;

    /// <summary>The projectile of an action from the tile it starts in (the actor's tile, or a waypoint's) to a target voxel.</summary>
    public BattleProjectile(BattleState battle, RuntimeRuleCatalog rules, BattleAimAction action, BattlePosition origin, BattlePosition targetVoxel)
    {
        _battle = battle ?? throw new ArgumentNullException(nameof(battle));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _map = battle.Map;
        _sight = battle.Sight();
        _origin = origin;
        _targetVoxel = targetVoxel;
    }

    /// <summary>Projectile::_trajectory: the voxels of the flight; the last is where it ended.</summary>
    public List<BattlePosition> Trajectory { get; } = [];

    /// <summary>Projectile::_targetVoxel: the voxel aimed at, after the deviation.</summary>
    public BattlePosition TargetVoxel => _targetVoxel;

    /// <summary>
    /// Projectile::calculateTrajectory: the voxel type the shot ends in (a part, a unit, the map's edge or empty), the trajectory kept. For a
    /// player's first shot of a burst the straight line to the target is checked first, as the cursor does: a line that is stopped by anything but the
    /// target tile gives no line of fire (empty, with no trajectory).
    /// </summary>
    public BattleVoxelType CalculateTrajectory(double accuracy, BattlePosition? originVoxel = null, bool excludeUnit = true)
    {
        var origin = originVoxel ?? _sight.OriginVoxel(_action, _origin);
        var actor = _action.Actor;
        if (LineOfFireBlocked(origin, excludeUnit)) return BattleVoxelType.Empty;

        Trajectory.Clear();
        var extendLine = true;
        // Even guided missiles drift, but how much is based on the shooter's faction, rather than accuracy.
        if (_action.Type == BattleActionType.Launch)
        {
            accuracy = actor.Faction == BattleFaction.Player ? 0.60 : 0.55;
            extendLine = _action.Waypoints.Count <= 1;
        }
        // Apply some accuracy modifiers: this results in a new target voxel.
        ApplyAccuracy(origin, ref _targetVoxel, accuracy, false, extendLine);
        // Finally do a line calculation and store this trajectory.
        return _sight.CalculateLineVoxel(origin, _targetVoxel, true, Trajectory, actor);
    }

    /// <summary>
    /// The first check of Projectile::calculateTrajectory, which draws nothing: for a player's first shot of a burst that is not forced, a launch or a
    /// spray, the straight line from the origin to the target voxel must end on the target tile (a wall to its north or west, its unit, or nothing);
    /// else there is no line of fire, and the trajectory is cleared. The line is left in the trajectory when it passes.
    /// </summary>
    public bool LineOfFireBlocked(BattlePosition origin, bool excludeUnit = true)
    {
        var actor = _action.Actor;
        var targetTile = _map.TileIndex(_action.Target);
        var test = _sight.CalculateLineVoxel(origin, _targetVoxel, false, Trajectory, excludeUnit ? actor : null);

        if (test != BattleVoxelType.Empty && Trajectory.Count != 0 && actor.Faction == BattleFaction.Player && _action.AutoShotCounter == 1 &&
            !_action.ForceFire && _action.PanicHandled && _action.Type != BattleActionType.Launch && !_action.SprayTargeting)
        {
            var hitPosition = BattleGeometry.TileOfVoxel(Trajectory[0]);
            var hitTile = _map.TileAt(hitPosition.X, hitPosition.Y, hitPosition.Z);
            if (test == BattleVoxelType.Unit && hitTile >= 0 && _battle.UnitOnTile(hitTile) is null)
                hitPosition = new BattlePosition(hitPosition.X, hitPosition.Y, hitPosition.Z - 1); // no unit? must be lower
            if (hitPosition != _action.Target && _action.Result.Length == 0)
            {
                bool blocked;
                if (test == BattleVoxelType.NorthWall) blocked = hitPosition.Y - 1 != _action.Target.Y;
                else if (test == BattleVoxelType.WestWall) blocked = hitPosition.X - 1 != _action.Target.X;
                else if (test == BattleVoxelType.Unit)
                {
                    var below = _map.TileAt(hitPosition.X, hitPosition.Y, hitPosition.Z);
                    blocked = (below >= 0 ? _battle.UnitOnTile(below) : null) != _battle.UnitOnTile(targetTile);
                }
                else blocked = true;
                if (blocked)
                {
                    Trajectory.Clear();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Projectile::calculateThrow: the voxel type a throw (or an arcing shot) ends in, the trajectory kept; out of bounds when there is no line of
    /// fire (a throw whose arc does not reach the tile, or lands on a blocking object, is tried again with another deviation up to 100 times).
    /// A forced (Ctrl) arcing shot only fakes a hit.
    /// </summary>
    public BattleVoxelType CalculateThrow(double accuracy, List<BattleObstacle>? obstacles = null)
    {
        var arc = FindArc(obstacles, out var originVoxel, out var targetVoxel, out var curvature, out var forced);
        if (!forced && arc == BattleVoxelType.OutOfBounds) return arc; // no line of fire
        return Throw(accuracy, originVoxel, targetVoxel, curvature, forced);
    }

    /// <summary>
    /// The start of Projectile::calculateThrow: the first of the tile's target voxels (the unit's middle, bottom and top, or the object's, wall's or
    /// floor's) that <c>validateThrow</c> accepts, with the curvature of the arc and the voxel type it ends in (out of bounds when none does: no line
    /// of fire). It draws nothing, so a query can ask it.
    /// </summary>
    public BattleVoxelType FindArc(List<BattleObstacle>? obstacles, out BattlePosition originVoxel, out BattlePosition targetVoxel, out double curvature, out bool forced)
    {
        var actor = _action.Actor;
        var targetTile = _map.TileIndex(_action.Target);
        originVoxel = _sight.OriginVoxel(_action);
        var corner = new BattlePosition(_action.Target.X * 16, _action.Target.Y * 16, _action.Target.Z * 24);
        BattlePosition Offset(int x, int y, int z) => new(corner.X + x, corner.Y + y, corner.Z + z);
        targetVoxel = Offset(8, 8, 1 + -_map.TerrainLevel(targetTile));
        var targets = new List<BattlePosition>();
        forced = false;
        if (_action.Type == BattleActionType.Throw) targets.Add(targetVoxel);
        else
        {
            var unit = _battle.OverlappingUnit(_rules, targetTile);
            if (_action.ForceFire && _battle.Side == BattleFaction.Player)
            {
                targets.Add(Offset(0, 0, 12));
                forced = true;
            }
            else if (unit is not null && (actor.Faction != BattleFaction.Player || unit.IsVisibleToPlayer(_rules)))
            {
                var height = unit.HeightOf(_rules);
                targetVoxel = targetVoxel with { Z = targetVoxel.Z + unit.FloatHeightOf(_rules) }; // ground level is the base
                targets.Add(targetVoxel with { Z = targetVoxel.Z + height / 2 + 1 });
                targets.Add(targetVoxel with { Z = targetVoxel.Z + 2 });
                targets.Add(targetVoxel with { Z = targetVoxel.Z + height - 1 });
            }
            else if (_map.MapData(targetTile, BattleTilePart.ObjectPart) is not null)
            {
                targetVoxel = Offset(8, 8, 0);
                foreach (var z in (int[])[13, 8, 23, 2]) targets.Add(targetVoxel with { Z = targetVoxel.Z + z });
            }
            else if (_map.MapData(targetTile, BattleTilePart.NorthWall) is not null)
            {
                targetVoxel = Offset(8, 0, 0);
                foreach (var z in (int[])[13, 8, 20, 3]) targets.Add(targetVoxel with { Z = targetVoxel.Z + z });
            }
            else if (_map.MapData(targetTile, BattleTilePart.WestWall) is not null)
            {
                targetVoxel = Offset(0, 8, 0);
                foreach (var z in (int[])[13, 8, 20, 2]) targets.Add(targetVoxel with { Z = targetVoxel.Z + z });
            }
            else if (_map.MapData(targetTile, BattleTilePart.Floor) is not null) targets.Add(targetVoxel);
        }

        var test = BattleVoxelType.OutOfBounds;
        curvature = 0.0;
        foreach (var position in targets)
        {
            targetVoxel = position;
            if (_sight.ValidateThrow(_action, originVoxel, targetVoxel, _battle.Depth, out curvature, out var type, forced, obstacles))
            {
                test = type;
                break;
            }
        }
        return test;
    }

    /// <summary>The end of Projectile::calculateThrow: the arc with the deviation, up to 100 tries while it ends out of bounds or on a blocking object.</summary>
    private BattleVoxelType Throw(double accuracy, BattlePosition originVoxel, BattlePosition targetVoxel, double curvature, bool forced)
    {
        var actor = _action.Actor;
        var test = BattleVoxelType.OutOfBounds;
        var tries = 0;
        // Finally do a line calculation and store this trajectory, make sure it's valid.
        while (test == BattleVoxelType.OutOfBounds && tries < 100)
        {
            ++tries;
            var deltas = targetVoxel;
            Trajectory.Clear();
            if (_action.Type == BattleActionType.Throw)
            {
                // Calling for the best flavor.
                ApplyAccuracy(originVoxel, ref deltas, accuracy, true, false);
                deltas = new BattlePosition(deltas.X - targetVoxel.X, deltas.Y - targetVoxel.Y, deltas.Z - targetVoxel.Z);
            }
            else
            {
                ApplyAccuracy(originVoxel, ref targetVoxel, accuracy, true, false); // arcing shot deviation
                deltas = new BattlePosition(0, 0, 0);
            }
            test = _sight.CalculateParabolaVoxel(originVoxel, targetVoxel, true, Trajectory, actor, curvature, deltas);
            if (forced) return BattleVoxelType.Object; // fake hit
            var endPosition = BattleGeometry.TileOfVoxel(BattleSight.PositionFromEnd(Trajectory, BattleSight.ItemDropVoxelOffset));
            var endTile = _map.TileAt(endPosition.X, endPosition.Y, endPosition.Z);
            // Check if the item would land on a tile with a blocking object.
            if (_action.Type == BattleActionType.Throw && endTile >= 0 && _map.MapData(endTile, BattleTilePart.ObjectPart) is { } blocker &&
                BattleMap.MovementCost(blocker, 0) == BattlePathing.InvalidMoveCost && !(BattleSight.IsBigWall(blocker) && (blocker.BigWall < 1 || blocker.BigWall > 3)))
                test = BattleVoxelType.OutOfBounds;
        }
        return test;
    }

    /// <summary>
    /// Projectile::applyAccuracy (the classic form): the new target voxel from the accuracy. The accuracy loses the dropoff past the weapon's
    /// ranges, and a share without sight of the target (the no line of sight penalty); the deviation grows with the distance and shrinks with the
    /// accuracy (a draw of 0 to 100 against the accuracy). With <c>shootingSpreadMode</c> 1 (the default) the shift is uniform and the
    /// horizontal deviation is drawn inside a circle (up to 15 tries, the radius cut to 0.85 after the first); the line is extended to the
    /// farthest range (a thousand tiles) unless <paramref name="extendLine"/> is off.
    /// </summary>
    internal void ApplyAccuracy(BattlePosition origin, ref BattlePosition target, double accuracy, bool keepRange, bool extendLine)
    {
        var xDiff = origin.X - target.X;
        var yDiff = origin.Y - target.Y;
        var zDiff = origin.Z - target.Z;
        var realDistance = Math.Sqrt((double)(xDiff * xDiff) + (double)(yDiff * yDiff) + (double)(zDiff * zDiff));
        // maxRange is the maximum range a projectile shall ever travel in voxel space.
        var maxRange = keepRange ? realDistance : 16 * 1000; // 1000 tiles
        if (_action.Type == BattleActionType.Hit) maxRange = 46; // up to 2 tiles diagonally (as in the case of reaper against reaper)

        if (_action.Type != BattleActionType.Hit)
        {
            var dropoff = BattleState.CalculateLimits(_rules, _action.Weapon, _action.Type, out var upperLimit, out var lowerLimit);
            var distance = realDistance / 16; // distance in tiles, but still fractional
            var accuracyLoss = 0.0;
            if (distance > upperLimit) accuracyLoss = (dropoff * (distance - upperLimit)) / 100;
            else if (distance < lowerLimit) accuracyLoss = (dropoff * (lowerLimit - distance)) / 100;
            accuracy = Math.Max(0.0, accuracy - accuracyLoss);
        }

        var xDist = Math.Abs(origin.X - target.X);
        var yDist = Math.Abs(origin.Y - target.Y);
        var zDist = Math.Abs(origin.Z - target.Z);
        int xyShift, zShift;

        // Uniform shooting spread (shootingSpreadMode 1).
        if (xDist <= yDist) xyShift = xDist / 4 + yDist;
        else xyShift = xDist + yDist / 4;
        xyShift = (int)(xyShift * 0.839); // constant to match the average xyShift to vanilla

        if (xyShift <= zDist) zShift = xyShift / 2 + zDist; // slight z deviation
        else zShift = xyShift + zDist / 2;

        // Apply the penalty for having no line of sight to the target.
        var noLosAccuracyPenalty = _rules.Battle.Equipment.Items[_action.Weapon.ItemType].Combat.NoLosAccuracyPenalty;
        if (noLosAccuracyPenalty != -1)
        {
            var tile = _map.TileAt(target.X / 16, target.Y / 16, target.Z / 24);
            if (tile >= 0)
            {
                // The line of sight to a unit can be told only if it stands on the target tile.
                var hasLos = _battle.UnitOnTile(tile) is not null ? _sight.Visible(_action.Actor, tile) : _sight.IsTileInLos(_action, tile, false);
                if (!hasLos) accuracy = accuracy * noLosAccuracyPenalty / 100;
            }
        }

        var random = _battle.Random;
        var deviation = (int)(random.NextInclusive(0, 100) - accuracy * 100);

        // Shooting has per-voxel precision.
        if (deviation >= 0) deviation += 50; // add an extra spread to the "miss" cloud
        else deviation += 10; // an accuracy of 109 or greater will become 1 (the tightest spread)

        deviation = Math.Max(1, zShift * deviation / 200); // range ratio

        // Make the shooting dispersion round instead of square for the uniform options.
        const double secondarySpreadCoefficient = 0.85; // inner spread circle diameter compared to outer
        var resultShifted = false;
        int dX = 0, dY = 0;
        for (var i = 0; i < 15; ++i) // break from this cycle when a proper target is found
        {
            dX = random.NextInclusive(0, deviation) - deviation / 2;
            dY = random.NextInclusive(0, deviation) - deviation / 2;
            var radiusSq = dX * dX + dY * dY;
            var deviateRadius = deviation / 2;
            if (radiusSq <= deviateRadius * deviateRadius) break; // if we are inside of the spread circle, we're done
            if (!resultShifted)
            {
                resultShifted = true;
                deviation = (int)(deviation * secondarySpreadCoefficient); // change the spread radius for the second and later attempts
            }
        }
        target = target with { X = target.X + dX, Y = target.Y + dY };
        target = target with { Z = target.Z + random.NextInclusive(0, deviation / 2) / 2 - deviation / 8 };

        if (extendLine)
        {
            var rotation = Math.Atan2((double)(target.Y - origin.Y), (double)(target.X - origin.X)) * 180 / Math.PI;
            var tilt = Math.Atan2((double)(target.Z - origin.Z),
                Math.Sqrt((double)(target.X - origin.X) * (double)(target.X - origin.X) + (double)(target.Y - origin.Y) * (double)(target.Y - origin.Y))) * 180 / Math.PI;
            // Calculate the new target; it can be very far out of the map, which does not matter here.
            var cosFi = Math.Cos(tilt * Math.PI / 180);
            var sinFi = Math.Sin(tilt * Math.PI / 180);
            var cosTe = Math.Cos(rotation * Math.PI / 180);
            var sinTe = Math.Sin(rotation * Math.PI / 180);
            target = new BattlePosition((int)(origin.X + maxRange * cosTe * cosFi), (int)(origin.Y + maxRange * sinTe * cosFi), (int)(origin.Z + maxRange * sinFi));
        }
    }
}
