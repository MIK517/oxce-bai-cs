using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The firing half of <c>TileEngine</c> (Phase 7 branch 5 S2): <c>getOriginVoxel</c> for every action, <c>canTargetTile</c> and
/// <c>adjustTargetVoxelFromTileType</c>, <c>calculateParabolaVoxel</c>, <c>validateThrow</c>, <c>ProjectileFlyBState::validThrowRange</c> and
/// <c>getMaxThrowDistance</c>, and <c>isTileInLOS</c>. The user options they read (<c>battleRealisticAccuracy</c>,
/// <c>battleRealisticImprovedLof</c>, <c>oxceEnableOffCentreShooting</c>) are at their defaults (off), so the realistic origin, the exposure and the
/// off-centre branches are not ported.
/// </summary>
internal sealed partial class BattleSight
{
    /// <summary>TileEngine::voxelTileCenter.</summary>
    internal static readonly BattlePosition VoxelTileCenter = new(TileXy / 2, TileXy / 2, TileZ / 2);

    /// <summary>Projectile::ItemDropVoxelOffset.</summary>
    internal const int ItemDropVoxelOffset = -2;

    /// <summary>The part number <c>canTargetTile</c> takes for <c>MapData::O_DUMMY</c> (only to find the obstacles of an empty tile).</summary>
    internal const int DummyPart = 999;

    private static readonly int[] SliceObjectSpiral =
    [
        8, 8, 8, 6, 10, 6, 10, 8, 10, 10, 8, 10, 6, 10, 6, 8, 6, 6, // first circle
        8, 4, 10, 4, 12, 4, 12, 6, 12, 8, 12, 10, 12, 12, 10, 12, 8, 12, 6, 12, 4, 12, 4, 10, 4, 8, 4, 6, 4, 4, 6, 4, // second circle
        8, 1, 12, 1, 15, 1, 15, 4, 15, 8, 15, 12, 15, 15, 12, 15, 8, 15, 4, 15, 1, 15, 1, 12, 1, 8, 1, 4, 1, 1, 4, 1, // third circle
    ];

    private static readonly int[] WestWallSpiral = [0, 7, 0, 9, 0, 6, 0, 11, 0, 4, 0, 13, 0, 2];
    private static readonly int[] NorthWallSpiral = [7, 0, 9, 0, 6, 0, 11, 0, 4, 0, 13, 0, 2, 0];

    // ---- origins ----

    /// <summary>getOriginVoxel's standard offsets of the weapon from the tile's corner, by the direction to the target.</summary>
    private static readonly int[] OriginShiftX = [8, 14, 15, 15, 8, 1, 1, 1];

    private static readonly int[] OriginShiftY = [1, 1, 8, 15, 15, 15, 8, 1];

    /// <summary>
    /// TileEngine::getOriginVoxel: where an action's projectile starts. From the unit's top less the weapon shift (3 for a throw, 4 otherwise), on its
    /// own tile or the tile given (a waypoint, for a launch from there: then the middle of the tile, a third up), offset towards the target by the
    /// standard shift, or a third of the way to the next direction for an origin to the left or right.
    /// </summary>
    public BattlePosition OriginVoxel(BattleAimAction action, BattlePosition? tilePosition = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        var actor = action.Actor;
        var unitSize = actor.ArmorSize;
        const int weaponShift = 4; // the original weapon position shift from the top of the unit's head
        var origin = tilePosition ?? actor.RequiredPosition;
        var tile = _map.TileIndex(origin);
        var tileAbove = _map.TileAbove(tile);
        var x = origin.X * TileXy;
        var y = origin.Y * TileXy;
        var z = origin.Z * TileZ;
        if (actor.RequiredPosition != origin && action.Type == BattleActionType.Launch)
        {
            // Not launched from a soldier (from a waypoint): the soldier's height and the terrain level are not taken into account.
            return new BattlePosition(x + 8, y + 8, z + 16);
        }
        // Take the unit's height and the terrain level into account.
        z += -_map.TerrainLevel(tile);
        z += actor.HeightOf(_rules) + actor.FloatHeightOf(_rules);
        z -= action.Type == BattleActionType.Throw ? 3 : weaponShift;
        if (z >= (origin.Z + 1) * TileZ)
        {
            if (tileAbove >= 0 && _map.HasNoFloorOnly(tileAbove)) origin = origin with { Z = origin.Z + 1 };
            else
            {
                while (z >= (origin.Z + 1) * TileZ) z--;
                z -= weaponShift;
            }
        }
        var direction = BattleGeometry.DirectionTo(origin, action.Target);
        switch (action.RelativeOrigin)
        {
            case BattleActionOrigin.Centre:
                // Standard offset.
                x += OriginShiftX[direction] * unitSize;
                y += OriginShiftY[direction] * unitSize;
                break;
            case BattleActionOrigin.Left:
                // 2:1 weighted average of the standard offset and a rotation to the left.
                x += ((2 * OriginShiftX[direction] + OriginShiftX[(direction + 7) % 8]) * unitSize + 1) / 3;
                y += ((2 * OriginShiftY[direction] + OriginShiftY[(direction + 7) % 8]) * unitSize + 1) / 3;
                break;
            case BattleActionOrigin.Right:
                x += ((2 * OriginShiftX[direction] + OriginShiftX[(direction + 1) % 8]) * unitSize + 1) / 3;
                y += ((2 * OriginShiftY[direction] + OriginShiftY[(direction + 1) % 8]) * unitSize + 1) / 3;
                break;
        }
        return new BattlePosition(x, y, z);
    }

    // ---- tile parts ----

    /// <summary>
    /// TileEngine::canTargetTile: whether a line from the origin voxel reaches a part of the tile (<paramref name="part"/> is the part's number, or
    /// <see cref="DummyPart"/>), scanning the part's height range from its middle outwards over a spiral of points. <paramref name="scanVoxel"/> is
    /// the voxel the scan ended at. <paramref name="obstacles"/> collects what stopped the lines (rememberObstacles).
    /// </summary>
    public bool CanTargetTile(BattlePosition originVoxel, int tile, int part, out BattlePosition scanVoxel, BattleUnit? excludeUnit, List<BattleObstacle>? obstacles = null)
    {
        scanVoxel = default;
        var tilePosition = _map.PositionOf(tile);
        var targetVoxel = new BattlePosition(tilePosition.X * TileXy, tilePosition.Y * TileXy, tilePosition.Z * TileZ);
        int[] spiral;
        int spiralCount;
        int minZ = 0, maxZ = 0;
        bool minZFound = false, maxZFound = false;
        var dummy = false;
        switch (part)
        {
            case (int)BattleTilePart.ObjectPart:
                spiral = SliceObjectSpiral;
                spiralCount = 41;
                break;
            case (int)BattleTilePart.NorthWall:
                spiral = NorthWallSpiral;
                spiralCount = 7;
                break;
            case (int)BattleTilePart.WestWall:
                spiral = WestWallSpiral;
                spiralCount = 7;
                break;
            case (int)BattleTilePart.Floor:
                spiral = SliceObjectSpiral;
                spiralCount = 41;
                (minZFound, minZ, maxZFound, maxZ) = (true, 0, true, 0);
                break;
            case DummyPart:
                // Used only for the no line of fire indicator.
                spiral = SliceObjectSpiral;
                spiralCount = 41;
                (minZFound, minZ, maxZFound, maxZ) = (true, 12, true, 12);
                break;
            default:
                return false;
        }

        // Find out the height range.
        BattleVoxelType Wanted() => (BattleVoxelType)part;
        if (!minZFound)
        {
            for (var j = 1; j < 12 && !minZFound; ++j)
                for (var i = 0; i < spiralCount; ++i)
                    if (VoxelCheck(new BattlePosition(targetVoxel.X + spiral[i * 2], targetVoxel.Y + spiral[i * 2 + 1], targetVoxel.Z + j * 2), null, true) == Wanted())
                    {
                        minZ = j * 2;
                        minZFound = true;
                        break;
                    }
        }
        if (!minZFound)
        {
            if (obstacles is null) return false; // empty object
            // A dummy attempt, only to highlight the obstacles.
            (minZFound, minZ, dummy) = (true, 10, true);
        }
        if (!maxZFound)
        {
            for (var j = 10; j >= 0 && !maxZFound; --j)
                for (var i = 0; i < spiralCount; ++i)
                    if (VoxelCheck(new BattlePosition(targetVoxel.X + spiral[i * 2], targetVoxel.Y + spiral[i * 2 + 1], targetVoxel.Z + j * 2), null, true) == Wanted())
                    {
                        maxZ = j * 2;
                        maxZFound = true;
                        break;
                    }
        }
        if (!maxZFound)
        {
            if (obstacles is null) return false; // it's impossible to get there
            (maxZFound, maxZ, dummy) = (true, 10, true);
        }

        if (minZ > maxZ) minZ = maxZ;
        var rangeZ = maxZ - minZ;
        if (rangeZ > 10) rangeZ = 10; // clamping the height range to the table
        var centerZ = (maxZ + minZ) / 2;
        var trajectory = new List<BattlePosition>();
        for (var j = 0; j <= rangeZ; ++j)
        {
            var z = targetVoxel.Z + centerZ + HeightFromCenter[j];
            for (var i = 0; i < spiralCount; ++i)
            {
                scanVoxel = new BattlePosition(targetVoxel.X + spiral[i * 2], targetVoxel.Y + spiral[i * 2 + 1], z);
                trajectory.Clear();
                var test = CalculateLineVoxel(originVoxel, scanVoxel, false, trajectory, excludeUnit);
                if ((int)test == part && !dummy && trajectory[0].X / TileXy == scanVoxel.X / TileXy && trajectory[0].Y / TileXy == scanVoxel.Y / TileXy &&
                    trajectory[0].Z / TileZ == scanVoxel.Z / TileZ) return true;
                if (obstacles is not null && trajectory.Count > 0)
                {
                    var stopped = _map.TileAt(trajectory[0].X / TileXy, trajectory[0].Y / TileXy, trajectory[0].Z / TileZ);
                    if (stopped >= 0) obstacles.Add(new BattleObstacle(stopped, test));
                }
            }
        }
        return false;
    }

    /// <summary>
    /// TileEngine::adjustTargetVoxelFromTileType: the voxel to aim at in a tile without a (visible) unit: a voxel of its object, else its north wall,
    /// west wall, or floor that the origin can reach, else the middle of the first of those it has, else the middle of the tile.
    /// </summary>
    public BattlePosition AdjustTargetVoxelFromTileType(BattlePosition originVoxel, int tile, BattleUnit? excludeUnit, List<BattleObstacle>? obstacles = null)
    {
        var position = _map.PositionOf(tile);
        var corner = new BattlePosition(position.X * TileXy, position.Y * TileXy, position.Z * TileZ);
        BattlePosition Offset(int x, int y, int z) => new(corner.X + x, corner.Y + y, corner.Z + z);
        if (_map.MapData(tile, BattleTilePart.ObjectPart) is not null)
            return CanTargetTile(originVoxel, tile, (int)BattleTilePart.ObjectPart, out var voxel, excludeUnit, obstacles) ? voxel : Offset(8, 8, 10);
        if (_map.MapData(tile, BattleTilePart.NorthWall) is not null)
            return CanTargetTile(originVoxel, tile, (int)BattleTilePart.NorthWall, out var voxel, excludeUnit, obstacles) ? voxel : Offset(8, 0, 9);
        if (_map.MapData(tile, BattleTilePart.WestWall) is not null)
            return CanTargetTile(originVoxel, tile, (int)BattleTilePart.WestWall, out var voxel, excludeUnit, obstacles) ? voxel : Offset(0, 8, 9);
        if (_map.MapData(tile, BattleTilePart.Floor) is not null)
            return CanTargetTile(originVoxel, tile, (int)BattleTilePart.Floor, out var voxel, excludeUnit, obstacles) ? voxel : Offset(8, 8, 2);
        // A dummy attempt (only to highlight the obstacles); the target is the middle of the tile.
        CanTargetTile(originVoxel, tile, DummyPart, out _, excludeUnit, obstacles);
        return Offset(VoxelTileCenter.X, VoxelTileCenter.Y, VoxelTileCenter.Z);
    }

    // ---- parabolas ----

    /// <summary>calculateParabolaHelper: the voxels of a parabola from the origin towards the target, each handed to <paramref name="position"/> until it returns true.</summary>
    private static bool ParabolaWalk(BattlePosition origin, BattlePosition target, double curvature, BattlePosition delta, Func<BattlePosition, bool> position)
    {
        var dx = (double)(target.X - origin.X);
        var dy = (double)(target.Y - origin.Y);
        var dz = (double)(target.Z - origin.Z);
        var ro = (double)MathF.Sqrt((float)(dx * dx + dy * dy + dz * dz)); // Position::distance returns a float
        if (Math.Abs(ro) < 1e-9) return false; // AreSame(ro, 0)

        var fi = Math.Acos((double)(target.Z - origin.Z) / ro);
        var te = Math.Atan2(dy, dx);
        te += delta.X / ro / 2 * Math.PI; // horizontal magic value
        fi += (delta.Z + delta.Y) / ro / 14 * Math.PI * curvature; // another magic value (vertical), to make it in line with fire spread

        var zA = Math.Sqrt(ro) * curvature;
        var zK = 4.0 * zA / ro / ro;
        var z = origin.Z;
        var i = 8;
        while (z > 0)
        {
            var x = (int)(origin.X + i * Math.Cos(te) * Math.Sin(fi));
            var y = (int)(origin.Y + i * Math.Sin(te) * Math.Sin(fi));
            z = (int)(origin.Z + i * Math.Cos(fi) - zK * (i - ro / 2.0) * (i - ro / 2.0) + zA);
            if (position(new BattlePosition(x, y, z))) return true;
            ++i;
        }
        return false;
    }

    /// <summary>
    /// TileEngine::calculateParabolaVoxel: the first voxel of a throw's arc that holds something (its type, or empty), the arc being a chain of
    /// straight lines. The trajectory gets the whole arc, or only the impact line's last voxel when it is not stored.
    /// </summary>
    public BattleVoxelType CalculateParabolaVoxel(BattlePosition origin, BattlePosition target, bool storeTrajectory, List<BattlePosition>? trajectory,
        BattleUnit? excludeUnit, double curvature, BattlePosition delta)
    {
        if (target == origin) return BattleVoxelType.Empty; // just in case
        var result = BattleVoxelType.Empty;
        var last = origin;
        // A small hack to glue the lines into one continuous arc.
        if (storeTrajectory && trajectory is not null) trajectory.Add(last);
        ParabolaWalk(origin, target, curvature, delta, next =>
        {
            // Remove the end point of the previous trajectory part, because the next one will add this point again.
            if (storeTrajectory && trajectory is not null) trajectory.RemoveAt(trajectory.Count - 1);
            result = CalculateLineVoxel(last, next, storeTrajectory, storeTrajectory ? trajectory : null, excludeUnit);
            if (result != BattleVoxelType.Empty)
            {
                // Pick the inside position of the impact.
                if (!storeTrajectory && trajectory is not null) result = CalculateLineVoxel(last, next, false, trajectory, excludeUnit);
                return true;
            }
            last = next;
            return false;
        });
        return result;
    }

    /// <summary>Projectile::getPositionFromStart: the voxel at an index of a trajectory, clamped to its ends.</summary>
    internal static BattlePosition PositionFromStart(IReadOnlyList<BattlePosition> trajectory, int index) =>
        index >= 0 && index < trajectory.Count ? trajectory[index] : index < 0 ? trajectory[0] : trajectory[^1];

    /// <summary>Projectile::getPositionFromEnd.</summary>
    internal static BattlePosition PositionFromEnd(IReadOnlyList<BattlePosition> trajectory, int index) => PositionFromStart(trajectory, trajectory.Count + index - 1);

    // ---- throwing ----

    /// <summary>
    /// ProjectileFlyBState::getMaxThrowDistance: how far (in voxels) a throw of a weight by a strength goes, from a height difference. The reference
    /// divides by the strength; a unit of no strength would end the program there, so the port counts a strength of at least 1 (ADR 0041).
    /// </summary>
    internal static int MaxThrowDistance(int weight, int strength, int level)
    {
        var current = level + 0.5;
        var dz = 1.0;
        var distance = 0;
        while (distance < 4000) // just in case
        {
            distance += 8;
            if (dz < -1) current -= 8;
            else current += dz * 8;

            if (current < 0 && dz < 0) // roll back
            {
                dz = Math.Max(dz, -1.0);
                if (Math.Abs(dz) > 1e-10) distance = (int)(distance - current / dz); // rollback horizontal (an int compound assignment truncates the sum)
                break;
            }
            dz -= (double)(50 * weight / Math.Max(1, strength)) / 100;
            if (dz <= -2.0) break; // become falling
        }
        return distance;
    }

    /// <summary>
    /// ProjectileFlyBState::validThrowRange: whether the target tile is within the item's throw range (the 3D distance of the unit) and the unit can
    /// throw its weight that far, from the origin voxel's height over the tile's floor. Always true for a shot.
    /// </summary>
    public bool ValidThrowRange(BattleAimAction action, BattlePosition originVoxel, int targetTile, int depth)
    {
        if (action.Type != BattleActionType.Throw) return true;
        var actor = action.Actor;
        var position = actor.RequiredPosition;
        var xDifference = action.Target.X - position.X;
        var yDifference = action.Target.Y - position.Y;
        var realDistanceSq = xDifference * xDifference + yDifference * yDifference;
        if (BattleState.IsOutOfThrowRange(_rules, action.Weapon, DistanceSquared(actor, action.Target), depth)) return false; // no need to check weight and strength
        var realDistance = Math.Sqrt(realDistanceSq);
        const int offset = 2;
        var zd = originVoxel.Z - (action.Target.Z * TileZ + offset - _map.TerrainLevel(targetTile));
        var weight = _battle.TotalWeight(_rules, action.Weapon);
        var maxDistance = (MaxThrowDistance(weight, actor.Stats.Strength, zd) + 8) / 16.0;
        var underwaterFactor = _rules.Campaign.Combat.ExtendedUnderwaterThrowFactor;
        if (depth > 0 && underwaterFactor > 0) maxDistance = maxDistance * underwaterFactor / 100.0;
        return realDistance <= maxDistance;
    }

    /// <summary>BattleUnit::distance3dToPositionSq: the squared distance in tiles from the unit's position (a big unit counts from its far corner).</summary>
    internal static int DistanceSquared(BattleUnit unit, BattlePosition position)
    {
        var from = unit.RequiredPosition;
        var x = from.X - position.X;
        var y = from.Y - position.Y;
        var z = from.Z - position.Z;
        if (unit.IsBigUnit)
        {
            if (from.X < position.X) x++;
            if (from.Y < position.Y) y++;
        }
        return x * x + y * y + z * z;
    }

    /// <summary>
    /// TileEngine::validateThrow: whether a throw (or an arcing shot) from the origin voxel can land on the target tile, trying up to nine curvatures
    /// from the strength-based one up in steps of a half. The curve and the voxel type the arc ends in are returned. A curve that fails, for a
    /// player's unit, marks the tile it hit as an obstacle.
    /// </summary>
    public bool ValidateThrow(BattleAimAction action, BattlePosition originVoxel, BattlePosition targetVoxel, int depth, out double curve, out BattleVoxelType voxelType,
        bool forced, List<BattleObstacle>? obstacles = null)
    {
        curve = 0;
        voxelType = BattleVoxelType.OutOfBounds;
        if (originVoxel == targetVoxel) return false;
        var actor = action.Actor;
        var foundCurve = false;
        double curvature;
        if (action.Type == BattleActionType.Throw)
        {
            var raw = 1.73 / Math.Sqrt(Math.Sqrt((double)actor.Stats.Strength / _battle.TotalWeight(_rules, action.Weapon))) + (actor.Kneeled ? 0.1 : 0.0);
            // std::max(0.48, NaN) is 0.48 (a strength and a weight of 0); Math.Max would give NaN.
            curvature = double.IsNaN(raw) ? 0.48 : Math.Max(0.48, raw);
        }
        else
        {
            // Arcing projectile weapons assume a fixed strength and weight (70 and 10 respectively).
            curvature = 1.73 / Math.Sqrt(Math.Sqrt(70.0 / 10.0)) + (actor.Kneeled ? 0.1 : 0.0);
        }

        var targetTile = _map.TileAt(action.Target.X, action.Target.Y, action.Target.Z);
        var targetPosition = BattleGeometry.TileOfVoxel(targetVoxel);
        // Object blocking: can't throw here.
        if (action.Type == BattleActionType.Throw && targetTile >= 0 && _map.MapData(targetTile, BattleTilePart.ObjectPart) is { } blocker &&
            BattleMap.MovementCost(blocker, 0) == BattlePathing.InvalidMoveCost && !(IsBigWall(blocker) && (blocker.BigWall < 1 || blocker.BigWall > 3)))
            return false;
        // Out of range: can't throw here.
        if (targetTile < 0 || !ValidThrowRange(action, originVoxel, targetTile, depth)) return false;

        var trajectory = new List<BattlePosition>(16 * 20);
        // We try 8 different curvatures to try and reach our goal.
        var test = BattleVoxelType.OutOfBounds;
        while (!foundCurve && curvature < 5.0)
        {
            trajectory.Clear();
            test = CalculateParabolaVoxel(originVoxel, targetVoxel, true, trajectory, actor, curvature, new BattlePosition(0, 0, 0));
            // The position that the item hit, and the position where it will land.
            var hit = trajectory[^1];
            var hitPosition = BattleGeometry.TileOfVoxel(new BattlePosition(hit.X, hit.Y, hit.Z + 1));
            var landing = BattleGeometry.TileOfVoxel(PositionFromEnd(trajectory, ItemDropVoxelOffset));
            if (forced || test != BattleVoxelType.OutOfBounds && landing == targetPosition)
            {
                voxelType = test;
                foundCurve = true;
            }
            else
            {
                curvature += 0.5;
                if (test != BattleVoxelType.OutOfBounds && actor.Faction == BattleFaction.Player && obstacles is not null) // the obstacle indicator is for the player only
                {
                    var hitTile = _map.TileAt(hitPosition.X, hitPosition.Y, hitPosition.Z);
                    if (hitTile >= 0) obstacles.Add(new BattleObstacle(hitTile, test));
                }
            }
        }
        if (curvature >= 5.0) return false;
        curve = curvature;
        return true;
    }

    /// <summary>Tile::isBigWall: the object part has a big wall (any kind but none).</summary>
    internal static bool IsBigWall(BattleMapData data) => data.BigWall != 0;

    // ---- line of sight to a tile ----

    /// <summary>
    /// TileEngine::isTileInLOS: whether the actor sees the tile, a tile that may hold no unit (the no line of sight accuracy penalty asks): its line
    /// of fire reaches a voxel of the tile (the first of the object, walls and floor, or the tile's middle), with a hit that is not on the tile
    /// itself checked as <c>Projectile::calculateTrajectory</c> does, and the sight origin sees it through the smoke and fire within the actor's
    /// view distance. (The armor's <c>visibilityUnit</c> script is refused, not run.) <paramref name="drawing"/> asks as the cursor does: the tile's
    /// own unit, not the one overlapping it.
    /// </summary>
    public bool IsTileInLos(BattleAimAction action, int tile, bool drawing)
    {
        if (tile < 0) return false;
        var actor = action.Actor;
        var tilePosition = _map.PositionOf(tile);
        // If beyond the global max. range, nobody can see anything.
        if (BattleGeometry.HorizontalDistanceSquared(actor.RequiredPosition, tilePosition) > _maxViewDistanceSq) return false;
        var maxVoxel = VisibleDistanceMaxVoxel(tile, actor, null);

        // A temporary action, because the current action does not have its target yet (when only aiming).
        var temp = new BattleAimAction(actor, action.Type, action.Weapon, tilePosition);
        var originVoxel = OriginVoxel(temp);
        BattlePosition scan;
        var seen = false;
        var forceFire = action.ForceFire && _battle.Side == BattleFaction.Player;
        var corner = new BattlePosition(tilePosition.X * TileXy, tilePosition.Y * TileXy, tilePosition.Z * TileZ);
        BattlePosition Offset(int x, int y, int z) => new(corner.X + x, corner.Y + y, corner.Z + z);

        bool Aim(BattleTilePart part, BattlePosition fallback, out BattlePosition voxel)
        {
            if (CanTargetTile(originVoxel, tile, (int)part, out voxel, actor)) return true;
            voxel = fallback;
            return false;
        }

        // Primary LOF check.
        if (forceFire) scan = Offset(VoxelTileCenter.X, VoxelTileCenter.Y, VoxelTileCenter.Z); // always aim at the centre of the target tile
        else if (_map.MapData(tile, BattleTilePart.ObjectPart) is not null) seen = Aim(BattleTilePart.ObjectPart, Offset(8, 8, 10), out scan);
        else if (_map.MapData(tile, BattleTilePart.NorthWall) is not null) seen = Aim(BattleTilePart.NorthWall, Offset(8, 0, 9), out scan);
        else if (_map.MapData(tile, BattleTilePart.WestWall) is not null) seen = Aim(BattleTilePart.WestWall, Offset(0, 8, 9), out scan);
        else if (_map.MapData(tile, BattleTilePart.Floor) is not null) seen = Aim(BattleTilePart.Floor, Offset(8, 8, 2), out scan);
        else scan = Offset(8, 8, 12);

        // Secondary LOF check.
        if (!seen)
        {
            var trajectory = new List<BattlePosition>();
            var test = CalculateLineVoxel(originVoxel, scan, false, trajectory, actor);
            if (test == BattleVoxelType.Empty) seen = true; // we have hit nothing at all, the line of fire is clear
            else if (test == BattleVoxelType.OutOfBounds) seen = false;
            else
            {
                // Let's assume we hit and double-check, as Projectile::calculateTrajectory does.
                seen = true;
                var hitPosition = BattleGeometry.TileOfVoxel(trajectory[0]);
                if (test == BattleVoxelType.Unit && _map.TileAt(hitPosition.X, hitPosition.Y, hitPosition.Z) is var hitTile and >= 0 && _battle.UnitOnTile(hitTile) is null)
                    hitPosition = hitPosition with { Z = hitPosition.Z - 1 }; // no unit? must be lower
                if (hitPosition != tilePosition)
                {
                    if (test == BattleVoxelType.NorthWall) seen = hitPosition.Y - 1 == tilePosition.Y;
                    else if (test == BattleVoxelType.WestWall) seen = hitPosition.X - 1 == tilePosition.X;
                    else if (test == BattleVoxelType.Unit)
                    {
                        var hitUnitTile = _map.TileAt(hitPosition.X, hitPosition.Y, hitPosition.Z);
                        var hitUnit = hitUnitTile >= 0 ? _battle.UnitOnTile(hitUnitTile) : null;
                        var targetUnit = drawing ? _battle.UnitOnTile(tile) : _battle.OverlappingUnit(_rules, tile);
                        seen = hitUnit == targetUnit;
                    }
                    else seen = false;
                }
            }
        }

        // The line of sight check uses the sight origin voxel (the line of fire check uses the origin voxel).
        originVoxel = SightOriginVoxel(actor);
        // Heat vision 100% = smoke effectiveness 0%.
        var smokeFactor = 100 - Clamp(actor.Vision.VisibilityThroughSmoke, 0, 100);
        var fireFactor = 100 - Clamp(actor.Vision.VisibilityThroughFire, 0, 100);
        if (seen)
        {
            var (distance, smoke, fire, smokeNear, fireNear) = TrajectoryData(actor, originVoxel, scan);
            var quality = unchecked(maxVoxel - distance -
                ((smoke - smokeNear / 2) * smokeFactor + (fire - fireNear / 2) * fireFactor) * maxVoxel / (3 * 20 * 100 * 16));
            seen = 0 < quality;
        }
        return seen;
    }
}
