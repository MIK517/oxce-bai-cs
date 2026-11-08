using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>TileEngine's <c>VoxelType</c>: what a voxel holds.</summary>
internal enum BattleVoxelType
{
    Empty = -1,
    Floor = 0,
    WestWall = 1,
    NorthWall = 2,
    Object = 3,
    Unit = 4,
    OutOfBounds = 5,
}

/// <summary>
/// The sight half of <c>TileEngine</c> (Phase 7 branch 4 S4): the voxel map (<c>voxelCheck</c>, <c>calculateLineVoxel</c>), the tile line
/// (<c>calculateLineTile</c>), <c>canTargetUnit</c>, <c>getSightOriginVoxel</c>, <c>getOriginVoxel</c> for a snap shot, <c>visible</c>, and the
/// field of view (<c>calculateTilesInFOV</c>, <c>calculateUnitsInFOV</c>, both <c>calculateFOV</c> forms and <c>recalculateFOV</c>). It reads the
/// battle's map, units and light cache and changes the units' sight lists, spotting fields and the map's visibility and discovery. The user
/// options it reads (<c>strafe</c>, <c>battleRealisticAccuracy</c>, <c>oxceEnableOffCentreShooting</c>, <c>aiCheatMode</c>) are at their defaults
/// (off), so the turret-direction, off-centre and cheating branches are not ported. One instance serves one operation, as it keeps the event
/// sector the reference keeps in the engine.
/// </summary>
internal sealed class BattleSight
{
    private const int TileXy = BattleGeometry.VoxelsPerTile;
    private const int TileZ = BattleGeometry.VoxelsPerLevel;

    /// <summary>TileEngine::heightFromCenter.</summary>
    private static readonly int[] HeightFromCenter = [0, -2, +2, -4, +4, -6, +6, -8, +8, -10, +10, -12, +12];

    private static readonly BattlePosition Invalid = new(-1, -1, -1);

    private readonly BattleState _battle;
    private readonly BattleMap _map;
    private readonly RuntimeRuleCatalog _rules;
    private readonly BattleVoxelData _voxels;
    private readonly int _maxViewDistance;
    private readonly int _maxViewDistanceSq;
    private readonly int _maxVoxelViewDistance;
    private readonly int _maxDarknessToSeeUnits;

    // TileEngine::_eventVisibilityObserverPos, _eventVisibilitySectorL and _eventVisibilitySectorR.
    private BattlePosition _eventObserver = Invalid;
    private (int X, int Y) _sectorLeft;
    private (int X, int Y) _sectorRight;

    public BattleSight(BattleState battle)
    {
        _battle = battle ?? throw new ArgumentNullException(nameof(battle));
        _map = battle.Map;
        _rules = battle.Rules ?? throw new InvalidOperationException("The battle has no rules to see by.");
        _voxels = battle.VoxelData ?? throw new InvalidOperationException("The battle has no line of fire templates (LOFTEMPS.DAT) to see by.");
        _maxViewDistance = _rules.Campaign.MaximumViewDistance;
        _maxViewDistanceSq = _maxViewDistance * _maxViewDistance;
        _maxVoxelViewDistance = _maxViewDistance * TileXy;
        _maxDarknessToSeeUnits = _rules.Campaign.MaximumDarknessToSeeUnits;
    }

    /// <summary>fmath.h's Clamp: <c>min(max(x, low), high)</c>.</summary>
    internal static int Clamp(int value, int low, int high) => Math.Min(Math.Max(value, low), high);

    // ---- voxels ----

    /// <summary>TileEngine::voxelCheck: what a voxel holds, from the terrain's line of fire templates and the unit standing in it.</summary>
    public BattleVoxelType VoxelCheck(BattlePosition voxel, BattleUnit? excludeUnit, bool excludeAllUnits = false, bool onlyVisible = false,
        BattleUnit? excludeAllBut = null)
    {
        if (voxel.X < 0 || voxel.Y < 0 || voxel.Z < 0) return BattleVoxelType.OutOfBounds;
        var tile = _map.TileAt(voxel.X / TileXy, voxel.Y / TileXy, voxel.Z / TileZ);
        if (tile < 0) return BattleVoxelType.OutOfBounds;
        var below = _map.TileBelow(tile);
        // Tile::isVoid also asks for no smoke and no items, which hold no voxels: such a tile falls through every check below to empty.
        if (_map.HasNoParts(tile) && _map.UnitAt(tile) is null && (below < 0 || _map.UnitAt(below) is null)) return BattleVoxelType.Empty;

        if (_map.HasGravLiftFloor(tile) && (voxel.Z % TileZ == 0 || voxel.Z % TileZ == 1) && !(below >= 0 && _map.HasGravLiftFloor(below)))
            return BattleVoxelType.Floor;

        // First the terrain voxel data, not to allow 2x2 units stick through walls.
        var column = TileXy - 1 - voxel.X % TileXy;
        var row = voxel.Y % TileXy;
        for (var part = BattleTilePart.Floor; part <= BattleTilePart.ObjectPart; part++)
        {
            if (part is BattleTilePart.WestWall or BattleTilePart.NorthWall && _map.IsUfoDoorOpen(tile, part)) continue;
            if (_map.MapData(tile, part) is { } data && (_voxels.Row(data.LoftId(voxel.Z % TileZ / 2), row) & 1 << column) != 0)
                return (BattleVoxelType)(int)part;
        }

        if (!excludeAllUnits)
        {
            var unit = _battle.OverlappingUnit(_rules, tile);
            if (unit is not null && !unit.IsOut && unit != excludeUnit && (excludeAllBut is null || unit == excludeAllBut) &&
                (!onlyVisible || unit.IsVisibleToPlayer(_rules)) && unit.Position is { } unitPosition)
            {
                var terrainHeight = 0;
                for (var x = 0; x < unit.ArmorSize; x++)
                    for (var y = 0; y < unit.ArmorSize; y++)
                        if (_map.TileAt(unitPosition.X + x, unitPosition.Y + y, unitPosition.Z) is var covered and >= 0 && _map.TerrainLevel(covered) < terrainHeight)
                            terrainHeight = _map.TerrainLevel(covered);
                // The bottom most voxel; terrain heights are negative, so they are subtracted.
                var bottom = unitPosition.Z * TileZ + unit.FloatHeightOf(_rules) - terrainHeight;
                if (voxel.Z > bottom && voxel.Z <= bottom + unit.HeightOf(_rules))
                {
                    var part = 0;
                    if (unit.IsBigUnit)
                    {
                        // The reference's order 0, 1, 2, 3 becomes 1, 0, 3, 2.
                        var tilePosition = _map.PositionOf(tile);
                        part = BigUnitParts[tilePosition.X - unitPosition.X + (tilePosition.Y - unitPosition.Y) * 2];
                    }
                    if ((_voxels.Row(unit.LoftempsOf(_rules, part), row) & 1 << column) != 0) return BattleVoxelType.Unit;
                }
            }
        }
        return BattleVoxelType.Empty;
    }

    private static readonly int[] BigUnitParts = [1, 0, 3, 2];

    // calculateTilesInFOV's quadrant signs of the view cone, by the unit's direction.
    private static readonly int[] FovSignX = [+1, +1, +1, +1, -1, -1, -1, -1];
    private static readonly int[] FovSignY = [-1, -1, -1, +1, +1, +1, -1, -1];

    /// <summary>
    /// TileEngine::calculateLineVoxel: the first voxel of the line from origin to target that holds something. The trajectory gets every
    /// voxel passed when <paramref name="storeTrajectory"/> is set, and the impact voxel (again, after the others) in any case, as the
    /// reference does.
    /// </summary>
    public BattleVoxelType CalculateLineVoxel(BattlePosition origin, BattlePosition target, bool storeTrajectory, List<BattlePosition>? trajectory,
        BattleUnit? excludeUnit, BattleUnit? excludeAllBut = null, bool onlyVisible = false)
    {
        var result = BattleVoxelType.Empty;
        // Units are not spotted before the pre-game inventory (a large unit on the craft inventory tile would crash).
        var excludeAllUnits = _battle.IsBeforeGame;
        bool Check(BattlePosition point)
        {
            result = VoxelCheck(point, excludeUnit, excludeAllUnits, onlyVisible, excludeAllBut);
            if (result == BattleVoxelType.Empty) return false;
            trajectory?.Add(point);
            return true;
        }
        var hit = BattleLines.Walk((origin.X, origin.Y, origin.Z), (target.X, target.Y, target.Z), point =>
        {
            var position = new BattlePosition(point.X, point.Y, point.Z);
            if (storeTrajectory) trajectory?.Add(position);
            return Check(position);
        }, point => Check(new BattlePosition(point.X, point.Y, point.Z)));
        return hit ? result : BattleVoxelType.Empty;
    }

    /// <summary>
    /// TileEngine::calculateLineTile: the tile line from origin to target against the blockage cache. 256 when something stops it before the
    /// target (the trajectory then ends with the tile that stopped it), else 0. (The <c>minLightBlock</c> form belongs to the callers that use it.)
    /// </summary>
    public int CalculateLineTile(BattlePosition origin, BattlePosition target, List<BattlePosition> trajectory)
    {
        var last = origin;
        var hit = BattleLines.Walk((origin.X, origin.Y, origin.Z), (target.X, target.Y, target.Z), tuple =>
        {
            var point = new BattlePosition(tuple.X, tuple.Y, tuple.Z);
            trajectory.Add(point);
            var direction = BattleGeometry.VectorToDirection(point.X - last.X, point.Y - last.Y);
            var result = _map.LightCache(_map.TileIndex(last)).BlocksStep(direction, point.Z - last.Z, point == target);
            last = point;
            return result;
        });
        return hit ? 256 : 0;
    }

    /// <summary>TileEngine::trajectoryStepSize: the length one voxel of a trajectory stands for.</summary>
    internal static float TrajectoryStepSize(IReadOnlyList<BattlePosition> path, int index)
    {
        if ((uint)index >= (uint)path.Count) return 0.0f;
        if (index > 2) return 0.5f * Distance(path[index], path[index - 2]);
        return index != 0 ? Distance(path[1], path[0]) : 0.0f;
    }

    private static float Distance(BattlePosition one, BattlePosition other)
    {
        var x = one.X - other.X;
        var y = one.Y - other.Y;
        var z = one.Z - other.Z;
        return (float)Math.Sqrt(x * x + y * y + z * z);
    }

    // ---- targeting ----

    /// <summary>
    /// TileEngine::canTargetUnit: whether a line from the origin voxel reaches the unit on the tile (or <paramref name="potentialUnit"/>, as if it
    /// stood there). A human player's unit scans slices of the target finely; the artificial intelligence's scans five columns at four heights.
    /// <paramref name="scanVoxel"/> is the voxel the scan ended at.
    /// </summary>
    public bool CanTargetUnit(BattlePosition originVoxel, int tile, out BattlePosition scanVoxel, BattleUnit? excludeUnit, BattleUnit? potentialUnit = null)
    {
        scanVoxel = default;
        var hypothetical = potentialUnit is not null;
        BattleUnit? targetUnit;
        if (potentialUnit is null)
        {
            targetUnit = _battle.UnitOnTile(tile);
            if (targetUnit is null) return false; // no unit in this tile, even if it elevated and appearing in it.
        }
        else targetUnit = potentialUnit;
        if (targetUnit == excludeUnit) return false; // skip self

        var isPlayer = true;
        var isUnderAiControl = false;
        if (excludeUnit is not null)
        {
            if (excludeUnit.Faction != BattleFaction.Player) isPlayer = false;
            if (IsAiControlled(excludeUnit)) isUnderAiControl = true;
        }
        var targetPosition = targetUnit.RequiredPosition;
        var targetVoxel = new BattlePosition(targetPosition.X * TileXy, targetPosition.Y * TileXy, targetPosition.Z * TileZ);
        var targetMinHeight = targetVoxel.Z - _map.TerrainLevel(tile) + targetUnit.FloatHeightOf(_rules);
        var heightRange = !targetUnit.IsOut ? targetUnit.HeightOf(_rules) : 12;
        var targetMaxHeight = targetMinHeight + heightRange;
        var unitRadius = targetUnit.RadiusVoxels(_rules);
        var targetSize = targetUnit.ArmorSize;
        targetVoxel = targetVoxel with { X = targetVoxel.X + 8 * targetSize, Y = targetVoxel.Y + 8 * targetSize }; // center of unit

        var minX = targetVoxel.X - unitRadius - 1;
        var minY = targetVoxel.Y - unitRadius - 1;
        var maxX = targetVoxel.X + unitRadius + 1;
        var maxY = targetVoxel.Y + unitRadius + 1;
        var trajectory = new List<BattlePosition>(1);

        // The voxel of the hit must be inside the scanned box.
        bool Hits(BattlePosition from, BattlePosition to)
        {
            trajectory.Clear();
            var test = CalculateLineVoxel(from, to, false, trajectory, excludeUnit);
            if (test == BattleVoxelType.Unit)
            {
                var impact = trajectory[0];
                return impact.X >= minX && impact.X <= maxX && impact.Y >= minY && impact.Y <= maxY &&
                    impact.Z >= targetMinHeight + 1 && impact.Z <= targetMaxHeight;
            }
            return test == BattleVoxelType.Empty && hypothetical && trajectory.Count != 0;
        }

        if (isPlayer && !isUnderAiControl)
        {
            // Precise targeting for a human player: up to 11 frontal section points and 2 for front and back.
            var slices = new int[26];
            var aimFromAbove = originVoxel.Z > targetMaxHeight;
            var aimFromBelow = originVoxel.Z < targetMinHeight;
            var shiftCount = 1; // indexes 0 and 1 are the centre
            var relativeX = targetVoxel.X - originVoxel.X;
            var relativeY = targetVoxel.Y - originVoxel.Y;

            void AddSlices(int radius, bool endsWithFrontBack)
            {
                var normal = radius / MathF.Sqrt((float)(relativeX * relativeX + relativeY * relativeY));
                var x = (int)Math.Floor(relativeY * normal + 0.5);
                var y = (int)Math.Floor(-relativeX * normal + 0.5);
                slices[++shiftCount] = x;
                slices[++shiftCount] = y;
                slices[++shiftCount] = -x;
                slices[++shiftCount] = -y;
                if (endsWithFrontBack && (aimFromAbove || aimFromBelow))
                {
                    slices[++shiftCount] = y;
                    slices[++shiftCount] = -x;
                    slices[++shiftCount] = -y;
                    slices[++shiftCount] = x;
                }
            }

            if (targetSize == 1)
            {
                // Add the (radius - 1) and (radius) slices.
                for (var radius = unitRadius - 1; radius <= unitRadius; ++radius) AddSlices(radius, radius == unitRadius);
            }
            else
            {
                for (var radius = 3; radius <= 15; radius += 3) AddSlices(radius, radius == 15);
            }
            var pointsCount = (shiftCount + 1) / 2;
            var centerHeight = (targetMaxHeight + targetMinHeight) / 2;
            centerHeight += (targetMaxHeight - centerHeight) % 2; // The center should have an even number of voxels above it.
            var horizontalCount = heightRange / 2; // The horizontal slices of a target are symmetrical around the center, so even in number.
            horizontalCount += horizontalCount % 2;
            if (horizontalCount > 12) horizontalCount = 12;
            if (horizontalCount <= 0) horizontalCount = 0;

            var z = 0;
            for (var horizontal = 0; horizontal <= horizontalCount; ++horizontal)
            {
                // Start from the center, gradually increasing the vertical distance in both directions.
                z = centerHeight + HeightFromCenter[horizontal];
                scanVoxel = scanVoxel with { Z = z };
                if (z < targetMinHeight + 1 || z > targetMaxHeight) continue;
                for (var vertical = 0; vertical < pointsCount; ++vertical)
                {
                    // Skip unnecessary checks.
                    var checkTopBottom = (aimFromAbove || aimFromBelow) && vertical >= pointsCount - 2;
                    if (checkTopBottom && z > targetMinHeight + 2 && z < targetMaxHeight) continue;
                    scanVoxel = new BattlePosition(targetVoxel.X + slices[vertical * 2], targetVoxel.Y + slices[vertical * 2 + 1], z);
                    if (Hits(originVoxel, scanVoxel)) return true;
                }
            }
            return false;
        }

        // Simplified targeting for the artificial intelligence: vector manipulation to make the scan work in view-space.
        {
            var relativeX = targetVoxel.X - originVoxel.X;
            var relativeY = targetVoxel.Y - originVoxel.Y;
            var normal = unitRadius / MathF.Sqrt((float)(relativeX * relativeX + relativeY * relativeY));
            var x = (int)Math.Floor(relativeY * normal + 0.5);
            var y = (int)Math.Floor(-relativeX * normal + 0.5);
            // Targeting order: center, right, left, front, back.
            int[] verticalSlices = [0, 0, x, y, -x, -y, y, -x, -y, x];
            // Targeting order: 3/4 height, 1/4 height, top, bottom.
            int[] horizontalSlices =
            [
                targetMinHeight + heightRange * 3 / 4,
                targetMinHeight + 1 + (int)MathF.Ceiling(heightRange * 0.25f),
                targetMaxHeight,
                targetMinHeight + 1,
            ];
            for (var horizontal = 0; horizontal < 4; ++horizontal)
            {
                var z = horizontalSlices[horizontal];
                for (var vertical = 0; vertical < 5; ++vertical)
                {
                    // Scan front and back slices only on the top and bottom planes.
                    if (horizontal < 2 && vertical > 2) break;
                    scanVoxel = new BattlePosition(targetVoxel.X + verticalSlices[vertical * 2], targetVoxel.Y + verticalSlices[vertical * 2 + 1], z);
                    if (Hits(originVoxel, scanVoxel)) return true;
                }
            }
            return false;
        }
    }

    /// <summary>BattleUnit::isAIControlled: any unit of another side, and a player's unit under auto combat.</summary>
    private static bool IsAiControlled(BattleUnit unit)
    {
        if (unit.Faction != BattleFaction.Player) return true;
        var options = BattleOptions.Pathing;
        return options.AutoCombat && (!options.AutoCombatControlPerUnit || unit.AllowAutoCombat);
    }

    // ---- origins ----

    /// <summary>TileEngine::getSightOriginVoxel: roughly the unit's eyeball, a voxel below its top.</summary>
    public BattlePosition SightOriginVoxel(BattleUnit unit)
    {
        var position = unit.RequiredPosition;
        var tile = _map.TileIndex(position);
        var x = position.X * TileXy + 8;
        var y = position.Y * TileXy + 8;
        var z = position.Z * TileZ;
        z += -_map.TerrainLevel(tile);
        z += unit.HeightOf(_rules) + unit.FloatHeightOf(_rules) - 1; // one voxel lower (eye level)
        var above = _map.TileAbove(tile);
        if (unit.IsBigUnit)
        {
            x += 8;
            y += 8;
            z += 1; // topmost voxel
        }
        if (z >= (position.Z + 1) * TileZ && (above < 0 || !_map.HasNoFloorOnly(above)))
            while (z >= (position.Z + 1) * TileZ) z--;
        return new BattlePosition(x, y, z);
    }

    /// <summary>
    /// TileEngine::getOriginVoxel for a snap shot (<c>BA_SNAPSHOT</c>) from the unit's own tile at a target tile: the unit's top less four, a
    /// standard offset towards the target (<c>battleRealisticAccuracy</c> off), as <c>getSpottingUnits</c> builds its false action.
    /// </summary>
    public BattlePosition SnapShotOriginVoxel(BattleUnit unit, BattlePosition target)
    {
        var origin = unit.RequiredPosition;
        var unitSize = unit.ArmorSize;
        const int weaponShift = 4; // the original weapon position shift from the top of the unit's head
        var tile = _map.TileIndex(origin);
        var tileAbove = _map.TileAbove(tile);
        var x = origin.X * TileXy;
        var y = origin.Y * TileXy;
        var z = origin.Z * TileZ;
        // Take the unit's height and the terrain level into account.
        z += -_map.TerrainLevel(tile);
        z += unit.HeightOf(_rules) + unit.FloatHeightOf(_rules);
        z -= weaponShift;
        if (z >= (origin.Z + 1) * TileZ)
        {
            if (tileAbove >= 0 && _map.HasNoFloorOnly(tileAbove)) origin = origin with { Z = origin.Z + 1 };
            else
            {
                while (z >= (origin.Z + 1) * TileZ) z--;
                z -= weaponShift;
            }
        }
        // TileEngine::getDirectionTo, then the centre origin: the standard offset.
        var direction = BattleGeometry.DirectionTo(origin, target);
        return new BattlePosition(x + SnapShiftX[direction] * unitSize, y + SnapShiftY[direction] * unitSize, z);
    }

    /// <summary>getOriginVoxel's standard offsets of the weapon from the tile's corner, by the direction to the target.</summary>
    private static readonly int[] SnapShiftX = [8, 14, 15, 15, 8, 1, 1, 1];
    private static readonly int[] SnapShiftY = [1, 1, 8, 15, 15, 15, 8, 1];

    // ---- seeing ----

    /// <summary>BattleUnit::getMaxViewDistance: a camouflage sets or cuts the distance, an anti-camouflage gives some back, never above the base.</summary>
    private static int MaxViewDistance(int baseVisibility, int camouflage, int antiCamouflage)
    {
        var result = baseVisibility;
        if (camouflage > 0) result = camouflage; // fixed distance nerf
        else result += camouflage; // relative distance nerf
        if (result < 1) result = 1; // can't go under melee distance
        result += antiCamouflage; // relative distance buff
        if (result > baseVisibility) result = baseVisibility; // don't overbuff
        return result;
    }

    /// <summary>
    /// getVisibleDistanceMaxHelper: how far the unit sees a target on the tile in voxels: its distance at dark or by day (cut by the target's
    /// camouflage and its own anti-camouflage; a burning target has none and is never dark), the tile's darkness deciding, and a quarter tile of
    /// buffer.
    /// </summary>
    private int VisibleDistanceMaxVoxel(int tile, BattleUnit current, BattleUnit target)
    {
        var targetIsDark = _map.Shade(tile) > _maxDarknessToSeeUnits;
        BattleUnit? seen = target;
        if (target.Fire > 0)
        {
            // Fire cancels the target's camouflage.
            seen = null;
            targetIsDark = false;
        }
        var vision = current.Vision;
        var dark = vision.MaxViewDistanceAtDark;
        var day = vision.MaxViewDistanceAtDay;
        if (seen is not null)
        {
            var targetArmor = BattleState.ArmorOf(_rules, seen);
            var ownArmor = BattleState.ArmorOf(_rules, current);
            dark = MaxViewDistance(dark, targetArmor.CamouflageAtDark, ownArmor.AntiCamouflageAtDark);
            day = MaxViewDistance(day, targetArmor.CamouflageAtDay, ownArmor.AntiCamouflageAtDay);
        }
        var result = Math.Min(_maxVoxelViewDistance, (targetIsDark ? dark : day) * TileXy);
        // A small buffer that lets a very short visibility distance work in smoke, and some diagonal directions still be visible.
        return result + TileXy / 4;
    }

    /// <summary>
    /// getTrajectoryDataHelper: the line from the origin to the scan voxel with the smoke and fire density along it, in single precision:
    /// the distance, the smoke, the fire (a tile on fire counts its smoke as fire density), and the smoke and fire within two tiles of the unit.
    /// </summary>
    private (int Distance, int Smoke, int Fire, int SmokeNear, int FireNear) TrajectoryData(BattleUnit current, BattlePosition origin, BattlePosition scan)
    {
        var trajectory = new List<BattlePosition>(Math.Max(Math.Max(Math.Abs(origin.X - scan.X), Math.Abs(origin.Y - scan.Y)), Math.Abs(origin.Z - scan.Z)) + 1);
        CalculateLineVoxel(origin, scan, true, trajectory, current);
        float smoke = 0, fire = 0, smokeNear = 0, fireNear = 0, distance = 0;
        var tracked = Invalid;
        var tile = -1;
        for (var index = 0; index < trajectory.Count; index++)
        {
            var at = BattleGeometry.TileOfVoxel(trajectory[index]);
            var step = TrajectoryStepSize(trajectory, index);
            if (tracked != at)
            {
                tracked = at;
                tile = _map.TileAt(at.X, at.Y, at.Z);
            }
            if (tile < 0) continue;
            distance += step;
            if (_map.Fire(tile) == 0) smoke += step * _map.Smoke(tile);
            else fire += step * _map.Smoke(tile); // this boosts fire blocking visibility for thermo vision, as smoke is usually bigger
            if (distance < TileXy * 2)
            {
                if (_map.Fire(tile) == 0) smokeNear += step * _map.Smoke(tile);
                else fireNear += step * _map.Smoke(tile);
            }
        }
        return ((int)distance, (int)smoke, (int)fire, (int)smokeNear, (int)fireNear);
    }

    /// <summary>
    /// TileEngine::visible: whether the unit sees the unit on a tile. Friends are always seen; nobody sees beyond the maximum view distance;
    /// psi vision senses a unit through anything; otherwise the unit's eye must reach the target (<see cref="CanTargetUnit"/>) with enough
    /// visibility quality left after the distance, smoke and fire. (The armor's <c>visibilityUnit</c> script is refused, not run.)
    /// </summary>
    public bool Visible(BattleUnit current, int tile)
    {
        // If there is no tile or no unit, we can't see it.
        if (tile < 0 || _battle.UnitOnTile(tile) is not { } target) return false;
        // Friendlies are always seen.
        if (current.Faction == target.Faction) return true;
        var from = current.RequiredPosition;
        var to = _map.PositionOf(tile);
        var distanceSq = BattleGeometry.HorizontalDistanceSquared(from, to);
        // If beyond the global max. range, nobody can see anyone.
        if (distanceSq > _maxViewDistanceSq) return false;

        var targetArmor = BattleState.ArmorOf(_rules, target);
        var psiVision = current.Vision.PsiVision;
        if (psiVision > 0 && !(targetArmor.FearImmune ?? false))
        {
            var camouflage = targetArmor.PsiCamouflage;
            if (camouflage > 0) psiVision = Math.Min(psiVision, camouflage);
            else if (camouflage < 0) psiVision = Math.Max(0, psiVision + camouflage);
            if (distanceSq <= psiVision * psiVision) return true; // we already sense the unit, no need to check obstacles or smoke
        }

        var maxVoxel = VisibleDistanceMaxVoxel(tile, current, target);
        var origin = SightOriginVoxel(current);
        var seen = CanTargetUnit(origin, tile, out var scan, current);

        // Heat vision 100% = smoke effectiveness 0%.
        var smokeFactor = 100 - Clamp(current.Vision.VisibilityThroughSmoke, 0, 100);
        var fireFactor = 100 - Clamp(current.Vision.VisibilityThroughFire, 0, 100);
        if (seen)
        {
            var (distance, smoke, fire, smokeNear, fireNear) = TrajectoryData(current, origin, scan);
            // 3 is the coefficient of getTrajectoryDataHelper, 20 the maximum view distance in vanilla, 100 the percent of the factors and
            // 16 the voxel scale; a larger maximum view distance from the ruleset keeps the smoke's effect.
            var quality = unchecked(maxVoxel - distance -
                ((smoke - smokeNear / 2) * smokeFactor + (fire - fireNear / 2) * fireFactor) * maxVoxel / (3 * 20 * 100 * 16));
            seen = 0 < quality;
        }
        return seen;
    }

    // ---- field of view ----

    /// <summary>
    /// TileEngine::setupEventVisibilitySector: the narrow sector from an observer that covers an event (a circle of the radius around a
    /// position) and the units beyond it. True when the whole view is to be checked (no radius, no position, or the event overlaps the observer).
    /// </summary>
    private bool SetupEventVisibilitySector(BattlePosition observer, BattlePosition eventPosition, int eventRadius)
    {
        if (eventRadius == 0 || eventPosition == Invalid ||
            BattleGeometry.HorizontalDistanceSquared(observer, eventPosition) <= eventRadius * eventRadius)
        {
            _eventObserver = Invalid;
            return true;
        }
        // Use search space reduction by updating within a narrow circle sector covering the event and any units beyond it.
        var differenceX = eventPosition.X - observer.X;
        var differenceY = eventPosition.Y - observer.Y;
        var a = MathF.Asin(eventRadius / MathF.Sqrt(differenceX * differenceX + differenceY * differenceY));
        var b = MathF.Atan2(differenceY, differenceX);
        var t1 = b - a;
        var t2 = b + a;
        // The points where the lines tangent to the circle intersect it, relative to the observer.
        _sectorLeft = ((int)(RoundF(eventPosition.X + eventRadius * MathF.Sin(t1)) - observer.X),
            (int)(RoundF(eventPosition.Y - eventRadius * MathF.Cos(t1)) - observer.Y));
        _sectorRight = ((int)(RoundF(eventPosition.X - eventRadius * MathF.Sin(t2)) - observer.X),
            (int)(RoundF(eventPosition.Y + eventRadius * MathF.Cos(t2)) - observer.Y));
        _eventObserver = observer;
        return false;
    }

    /// <summary>C's roundf: halves away from zero.</summary>
    private static float RoundF(float value) => MathF.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>TileEngine::inEventVisibilitySector: whether a position lies within the sector set up (always, without one).</summary>
    private bool InEventVisibilitySector(BattlePosition position)
    {
        if (_eventObserver == Invalid) return true;
        var differenceX = position.X - _eventObserver.X;
        var differenceY = position.Y - _eventObserver.Y;
        // Is the position within the arc defined by the two tangent points?
        return !(-_sectorLeft.X * differenceY + _sectorLeft.Y * differenceX > 0) && -_sectorRight.X * differenceY + _sectorRight.Y * differenceX > 0;
    }

    /// <summary>
    /// TileEngine::calculateTilesInFOV: the tiles the unit sees, by the tile lines of the blockage cache, within the view cone (all of it, or the
    /// sector of an event). A player's unit marks them visible and discovered, with the walls east and south of each; the port marks a hostile
    /// or neutral unit's discovery the same way (ADR 0039).
    /// </summary>
    public void CalculateTilesInFov(BattleUnit unit, BattlePosition? eventPosition = null, int eventRadius = 0)
    {
        var eventAt = eventPosition ?? Invalid;
        var direction = unit.Direction;
        if (eventRadius == 1 && !unit.CheckViewSector(eventAt)) return; // The event wasn't meant for us and/or visible for us.
        if (unit.IsOut)
        {
            unit.ClearVisibleTiles(_map);
            return;
        }
        var self = unit.RequiredPosition;
        var skipNarrowArcTest = false;
        if (SetupEventVisibilitySector(self, eventAt, eventRadius))
        {
            // Asked to do a full check, or the unit is within the event: update all.
            unit.ClearVisibleTiles(_map);
            skipNarrowArcTest = true;
        }
        // Only recalculate the lines to the tiles that are at the event or further away.
        var distanceSqrMin = skipNarrowArcTest ? 0
            : Math.Max(BattleGeometry.HorizontalDistanceSquared(self, eventAt) - eventRadius * eventRadius, 0);

        // The tiles to test, by the view direction.
        var swap = direction is 0 or 4;
        if (unit.HeightOf(_rules) + unit.FloatHeightOf(_rules) + -_map.TerrainLevel(_map.TileIndex(self)) >= 24 + 4)
        {
            var above = _map.TileAt(self.X, self.Y, self.Z + 1);
            if (above >= 0 && _map.HasNoFloorOnly(above)) self = self with { Z = self.Z + 1 };
        }
        var trajectory = new List<BattlePosition>();
        var faction = unit.Faction;
        // Test all tiles within the view cone for visibility.
        for (var x = 0; x <= _maxViewDistance; ++x)
        {
            int y1, y2;
            if ((direction & 1) != 0)
            {
                y1 = 0;
                y2 = _maxViewDistance;
            }
            else
            {
                y1 = -x;
                y2 = x;
            }
            for (var y = y1; y <= y2; ++y)
            {
                var distanceSqr = x * x + y * y;
                if (distanceSqr > _maxViewDistanceSq || distanceSqr < distanceSqrMin) continue;
                var test = new BattlePosition(self.X + FovSignX[direction] * (swap ? y : x), self.Y + FovSignY[direction] * (swap ? x : y), 0);
                // Only continue if the column of tiles at (x, y) is within the narrow arc of interest.
                if (!InEventVisibilitySector(test)) continue;
                for (var z = 0; z < _map.Height; z++)
                {
                    test = test with { Z = z };
                    if (_map.TileAt(test.X, test.Y, test.Z) < 0) continue; // inside the map?
                    // This sets tiles to discovered if they are in line of sight; tile visibility is calculated in tile space, and a large
                    // unit has "four pairs of eyes".
                    for (var xo = 0; xo < unit.ArmorSize; xo++)
                        for (var yo = 0; yo < unit.ArmorSize; yo++)
                        {
                            var start = new BattlePosition(self.X + xo, self.Y + yo, self.Z);
                            trajectory.Clear();
                            // Vision impacted something before reaching the tile: throw away the impact point.
                            if (CalculateLineTile(start, test, trajectory) > 127) trajectory.RemoveAt(trajectory.Count - 1);
                            // Reveal all tiles along the line of vision (needed due to the width of the stroke).
                            foreach (var visited in trajectory)
                            {
                                var tile = _map.TileAt(visited.X, visited.Y, visited.Z);
                                // Add tiles to the visible list only once, but calculate the whole trajectory: this line's period might be
                                // different from the one that originally revealed the tile.
                                if (unit.HasVisibleTile(tile)) continue;
                                _battle.AddToVisibleTiles(unit, tile);
                                if (faction == BattleFaction.Player) _map.SetVisibility(tile, _map.Visibility(tile) + 1);
                                _map.SetDiscovered(tile, BattleTilePart.Floor, true, faction);
                                // Walls to the east or south of a visible tile are seen too.
                                if (_map.TileAt(visited.X + 1, visited.Y, visited.Z) is var east and >= 0) _map.SetDiscovered(east, BattleTilePart.WestWall, true, faction);
                                if (_map.TileAt(visited.X, visited.Y + 1, visited.Z) is var south and >= 0) _map.SetDiscovered(south, BattleTilePart.NorthWall, true, faction);
                            }
                        }
                }
            }
        }
    }

    /// <summary>
    /// TileEngine::calculateUnitsInFOV: the units the unit sees, within the sector of an event or all of them. A seen unit joins the visible
    /// list, the player's sight marks it visible, and the spotting fields (<c>tileLastSpotted</c>, the turns since seen and spotted, the sniper
    /// turns) are set for the seeing faction. True when new units were spotted this turn and some are visible.
    /// </summary>
    public bool CalculateUnitsInFov(BattleUnit unit, BattlePosition? eventPosition = null, int eventRadius = 0)
    {
        var eventAt = eventPosition ?? Invalid;
        var oldCount = unit.UnitsSpottedThisTurn.Count;
        if (unit.IsOut) return false;
        var self = unit.RequiredPosition;
        if (SetupEventVisibilitySector(self, eventAt, eventRadius))
        {
            // Asked to do a full check, or the event overlaps our tile: better check everything.
            unit.ClearVisibleUnits();
        }
        // Loop through all units and figure out which ones we can actually see.
        foreach (var other in _battle.Units)
        {
            if (other.IsOut || unit.Id == other.Id || other.Position is not { } otherPosition) continue;
            var sizeOther = other.ArmorSize;
            var totalUnitTiles = 0;
            var notInViewSector = 0;
            var visibilityChecked = false;
            var visibilityStatus = false;
            for (var x = 0; x < sizeOther; ++x)
                for (var y = 0; y < sizeOther; ++y)
                {
                    totalUnitTiles++;
                    var toCheck = new BattlePosition(otherPosition.X + x, otherPosition.Y + y, otherPosition.Z);
                    // If we can now find any unit within the arc defined by the event tangent points, its visibility may have been affected.
                    if (!InEventVisibilitySector(toCheck)) continue;
                    if (!unit.CheckViewSector(toCheck))
                    {
                        // Unit within the arc but not in the view sector: if it just walked out we need to remove it.
                        notInViewSector++;
                    }
                    else if (visibilityChecked ? visibilityStatus : Visible(unit, _map.TileAt(toCheck.X, toCheck.Y, toCheck.Z)))
                    {
                        visibilityChecked = true;
                        visibilityStatus = true;
                        // The unit (or part of it) is visible to one or more eyes of this unit.
                        MarkSpotted(unit, other);
                        x = y = sizeOther; // If a unit's tile is visible there's no need to check the others: break the loops.
                    }
                    else
                    {
                        visibilityChecked = true;
                        visibilityStatus = false;
                        // Within the arc, but not visible: whatever happened at the event may have blocked a previously seen unit.
                        notInViewSector++;
                    }
                }
            if (notInViewSector == totalUnitTiles) unit.RemoveFromVisibleUnits(other.Id);
        }
        // (BattleUnit::checkForReactivation, which the reference runs when the spotted count changed, is the artificial intelligence's state
        // that branch 6 models.)
        // We only react when there are more units spotted than before and some are visible.
        return unit.UnitsSpottedThisTurn.Count > oldCount && unit.VisibleUnits.Count != 0;
    }

    /// <summary>The spotting of calculateUnitsInFOV once <paramref name="unit"/> sees <paramref name="other"/>.</summary>
    private void MarkSpotted(BattleUnit unit, BattleUnit other)
    {
        var faction = unit.Faction;
        if (faction == BattleFaction.Player) _battle.SetSpotted(other, BattleFaction.Player, true);
        if (other.Faction == BattleFaction.Hostile && faction != BattleFaction.Hostile ||
            other.Faction != BattleFaction.Hostile && faction == BattleFaction.Hostile && !unit.HasVisibleUnit(other))
        {
            unit.AddToVisibleUnits(other.Id);
            var otherTile = _map.TileIndex(other.Position!.Value);
            _battle.AddToVisibleTiles(unit, otherTile);
            unit.AddLofTile(otherTile);
            // The port's per-faction spotting (ADR 0039): the hostile and neutral factions spot the units their own units list.
            if (faction != BattleFaction.Player) _battle.SetSpotted(other, faction, true);
            other.TileLastSpottedBy[(int)faction] = otherTile;
            other.TileLastSpottedForBlindShotBy[(int)faction] = otherTile;
            other.TurnsSinceSeenBy[(int)faction] = 0;
            // setTurnsSinceSpotted and setTurnsLeftSpottedForSnipers are the hostile faction's entries.
            other.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 0;
            other.TurnsLeftSpottedForSnipers[(int)BattleFaction.Hostile] = (byte)Math.Max(unit.SpotterDuration(_rules), other.TurnsLeftSpottedForSnipers[(int)BattleFaction.Hostile]);
        }
        if (faction != other.Faction)
        {
            other.TurnsSinceSpotted[(int)faction] = 0;
            other.TurnsLeftSpottedForSnipers[(int)faction] = (byte)Clamp(
                Math.Max(unit.SpotterDuration(_rules), other.TurnsLeftSpottedForSnipers[(int)faction]), 0, 255);
        }
    }

    /// <summary>TileEngine::calculateFOV(unit): a full recheck of the unit's tiles and units. True when new units were spotted.</summary>
    public bool CalculateFov(BattleUnit unit, bool doTileRecalc = true, bool doUnitRecalc = true)
    {
        if (doTileRecalc) CalculateTilesInFov(unit);
        return doUnitRecalc && CalculateUnitsInFov(unit);
    }

    /// <summary>
    /// TileEngine::calculateFOV(position, radius, ...): the sight of the units that could have observed a change around a position (used
    /// when terrain changed). A radius of -1 asks for the whole view.
    /// </summary>
    public void CalculateFov(BattlePosition position, int eventRadius = -1, bool updateTiles = true, bool appendToTileVisibility = false)
    {
        int updateRadius;
        if (eventRadius == -1)
        {
            eventRadius = _maxViewDistance;
            updateRadius = _maxViewDistanceSq;
        }
        else
        {
            // Need to grab units which are out of range of the centre of the event, but can still see the edge of the effect.
            updateRadius = _maxViewDistance + (eventRadius > 0 ? eventRadius : 0);
            updateRadius *= updateRadius;
        }
        foreach (var unit in _battle.Units)
        {
            if (unit.Position is not { } at) continue;
            // Could this unit have observed the event?
            if (BattleGeometry.HorizontalDistanceSquared(position, at) > updateRadius) continue;
            if (updateTiles)
            {
                if (!appendToTileVisibility) unit.ClearVisibleTiles(_map);
                CalculateTilesInFov(unit, position, eventRadius);
            }
            CalculateUnitsInFov(unit, position, eventRadius);
        }
    }

    /// <summary>TileEngine::recalculateFOV: every unit that has a tile.</summary>
    public void RecalculateFov()
    {
        foreach (var unit in _battle.Units)
            if (unit.Position is not null) CalculateFov(unit);
    }
}
