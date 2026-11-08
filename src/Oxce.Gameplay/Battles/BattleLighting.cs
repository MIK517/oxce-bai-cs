using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>GraphSubset's <c>MapSubset</c>: a square of map columns, the ends excluded.</summary>
internal readonly record struct BattleMapSubset(int BeginX, int EndX, int BeginY, int EndY)
{
    /// <summary>TileEngine's <c>mapArea</c>: the columns within a radius of a position.</summary>
    public static BattleMapSubset Around(BattlePosition position, int radius) =>
        new(position.X - radius, position.X + radius + 1, position.Y - radius, position.Y + radius + 1);

    /// <summary>TileEngine's <c>mapAreaExpand</c>.</summary>
    public BattleMapSubset Expand(int radius) => new(BeginX - radius, EndX + radius, BeginY - radius, EndY + radius);

    public BattleMapSubset Intersect(BattleMapSubset other) => new(Math.Max(BeginX, other.BeginX), Math.Min(EndX, other.EndX),
        Math.Max(BeginY, other.BeginY), Math.Min(EndY, other.EndY));

    public bool IsEmpty => EndX <= BeginX || EndY <= BeginY;
}

/// <summary>
/// TileEngine's <c>calculateLineHelper</c>: the 3D Bresenham line of voxels from an origin to a target (both ends visited).
/// </summary>
internal static class BattleLines
{
    public static bool Walk((int X, int Y, int Z) origin, (int X, int Y, int Z) target, Func<(int X, int Y, int Z), bool> position,
        Func<(int X, int Y, int Z), bool>? drift = null)
    {
        int x0 = origin.X, y0 = origin.Y, z0 = origin.Z;
        int x1 = target.X, y1 = target.Y, z1 = target.Z;
        var swapXy = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
        if (swapXy)
        {
            (x0, y0) = (y0, x0);
            (x1, y1) = (y1, x1);
        }
        var swapXz = Math.Abs(z1 - z0) > Math.Abs(x1 - x0);
        if (swapXz)
        {
            (x0, z0) = (z0, x0);
            (x1, z1) = (z1, x1);
        }
        var deltaX = Math.Abs(x1 - x0);
        var deltaY = Math.Abs(y1 - y0);
        var deltaZ = Math.Abs(z1 - z0);
        var driftXy = deltaX / 2;
        var driftXz = deltaX / 2;
        var stepX = x0 > x1 ? -1 : 1;
        var stepY = y0 > y1 ? -1 : 1;
        var stepZ = z0 > z1 ? -1 : 1;
        var y = y0;
        var z = z0;
        (int, int, int) Unswap(int cx, int cy, int cz)
        {
            if (swapXz) (cx, cz) = (cz, cx);
            if (swapXy) (cx, cy) = (cy, cx);
            return (cx, cy, cz);
        }
        for (var x = x0; ; x += stepX)
        {
            if (position(Unswap(x, y, z))) return true;
            if (x == x1) break;
            driftXy -= deltaY;
            driftXz -= deltaZ;
            if (driftXy < 0)
            {
                y += stepY;
                driftXy += deltaX;
                if (drift?.Invoke(Unswap(x, y, z)) == true) return true;
            }
            if (driftXz < 0)
            {
                z += stepZ;
                driftXz += deltaX;
                if (drift?.Invoke(Unswap(x, y, z)) == true) return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Battle lighting (Phase 7 branch 4 S3): <c>TileEngine::calculateLighting</c> and its layers (sun shading, fire, items, units),
/// <c>addLight</c> in the classic and the enhanced mode, the visibility blockage cache, <c>togglePersonalLighting</c> and the tile shade.
/// The light is runtime state, computed at generation, on load and after the changes the reference recomputes it for.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>TileEngine.cpp's fireLightPower and unitFireLightPower: the light of a fire on a tile and of a unit on fire.</summary>
    private const int FireLightPower = 15;

    /// <summary>TileEngine.cpp's unitFireLightPowerStunned: the light of a burning unit lying as an item.</summary>
    private const int StunnedFireLightPower = 10;

    private const int FlareBattleType = 10;
    private const int NoFuseType = -3;

    /// <summary>Tile::getShade: 0 (full light) to 15 (dark), the brightest of the tile's light layers.</summary>
    internal int TileShade(BattlePosition position) =>
        Map.Contains(position) ? Map.Shade(Map.TileIndex(position)) : throw new ArgumentOutOfRangeException(nameof(position));

    /// <summary>
    /// TileEngine::togglePersonalLighting: the soldiers' personal light goes on or off (kept with the battle, as the default
    /// <c>oxceTogglePersonalLightType</c> 1 does), and the units' light is recomputed, then every unit's field of view (a unit sees
    /// others by their tiles' light).
    /// </summary>
    internal void TogglePersonalLighting(RuntimeRuleCatalog rules)
    {
        TogglePersonalLight = !TogglePersonalLight;
        CalculateLighting(rules, BattleLightLayer.Units);
        Sight().RecalculateFov();
        Revision++;
    }

    /// <summary>
    /// TileEngine::calculateLighting: recomputes the layer and the layers after it. With a position, only the columns within the event
    /// radius plus the light distance are recomputed; <paramref name="terrainChanged"/> first refreshes the blockage cache within the
    /// event radius plus one (the whole map without a position).
    /// </summary>
    internal void CalculateLighting(RuntimeRuleCatalog rules, BattleLightLayer layer, BattlePosition? position = null, int eventRadius = 0,
        bool terrainChanged = false)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var settings = rules.Campaign;
        var whole = Map.Area;
        var dynamic = whole;
        var @static = whole;
        if (position is { } center)
        {
            dynamic = BattleMapSubset.Around(center, eventRadius + settings.MaximumDynamicLightDistance);
            @static = BattleMapSubset.Around(center, eventRadius + settings.MaximumStaticLightDistance);
        }
        if (terrainChanged) RefreshBlockageCache(position is { } at ? BattleMapSubset.Around(at, eventRadius + 1) : whole);
        if (layer <= BattleLightLayer.Fire)
            foreach (var tile in TilesIn(@static)) Map.ResetLightMulti(tile, layer);
        var from = (BattleLightLayer)Math.Max((int)layer, (int)BattleLightLayer.Items);
        foreach (var tile in TilesIn(dynamic)) Map.ResetLightMulti(tile, from);
        if (layer <= BattleLightLayer.Ambient) CalculateSunShading(@static);
        if (layer <= BattleLightLayer.Fire) CalculateTerrainBackground(@static, rules);
        if (layer <= BattleLightLayer.Items) CalculateTerrainItems(dynamic, rules);
        if (layer <= BattleLightLayer.Units) CalculateUnitLighting(dynamic, rules);
    }

    /// <summary>TileEngine.cpp's iterateTiles: the tiles of a subset on every level, level by level, row by row.</summary>
    private IEnumerable<int> TilesIn(BattleMapSubset subset)
    {
        var clipped = subset.Intersect(Map.Area);
        if (clipped.IsEmpty) yield break;
        for (var z = 0; z < Map.Height; z++)
            for (var y = clipped.BeginY; y < clipped.EndY; y++)
                for (var x = clipped.BeginX; x < clipped.EndX; x++)
                    yield return Map.TileAt(x, y, z);
    }

    /// <summary>The blockage cache part of calculateLighting: per tile the blocked directions, big walls, smoke, fire and height.</summary>
    private void RefreshBlockageCache(BattleMapSubset area)
    {
        foreach (var tile in TilesIn(area))
        {
            var position = Map.PositionOf(tile);
            var cache = default(BattleLightCache);
            var height = -Map.TerrainLevel(tile);
            if (Map.MapData(tile, BattleTilePart.ObjectPart) is { } data && BattleMap.MovementCost(data, 0) == BattlePathing.InvalidMoveCost) height = 24;
            cache = (cache with { Height = unchecked((byte)height) }).WithSmoke(Map.Smoke(tile) > 0).WithFire(Map.Fire(tile) > 0)
                .WithBlockUp(BattleBlockage.VerticalBlockage(Map, tile, Map.TileAbove(tile), BattleBlockType.None) > 127)
                .WithBlockDown(BattleBlockage.VerticalBlockage(Map, tile, Map.TileBelow(tile), BattleBlockType.None) > 127);
            for (var direction = 0; direction < 8; direction++)
            {
                var next = Map.Neighbour(position, direction);
                var result = BattleBlockage.HorizontalBlockage(Map, tile, next, BattleBlockType.None, true);
                cache = cache.WithBigWall(direction, result == -1);
                result = BattleBlockage.HorizontalBlockage(Map, tile, next, BattleBlockType.None);
                cache = cache.WithBlock(direction, 0, result > 127 || result == -1);
                for (var levelOffset = -1; levelOffset <= 1; levelOffset += 2)
                    cache = cache.WithBlock(direction, levelOffset,
                        BattleBlockage.VerticalBlockage(Map, tile, Map.Neighbour(position, direction, levelOffset), BattleBlockType.None) > 127);
            }
            Map.SetLightCache(tile, cache);
        }
    }

    /// <summary>TileEngine::calculateSunShading: the daylight of every tile, two less under a roof at dusk and night.</summary>
    private void CalculateSunShading(BattleMapSubset area)
    {
        var power = 15 - GlobalShade;
        foreach (var tile in TilesIn(area))
        {
            var light = power;
            // At night and dusk the sun does not light what a roof covers.
            if (GlobalShade <= 4)
            {
                var position = Map.PositionOf(tile);
                var block = 0;
                for (var z = Map.Height - 1; z > position.Z; z--)
                {
                    var above = Map.TileAt(position.X, position.Y, z);
                    block += BattleBlockage.Blockage(Map, above, BattleTilePart.Floor, BattleBlockType.None);
                    block += BattleBlockage.Blockage(Map, above, BattleTilePart.ObjectPart, BattleBlockType.None, BattleUnit.DirectionDown);
                }
                if (block > 0) light -= 2;
            }
            Map.AddLight(tile, light, BattleLightLayer.Ambient);
        }
    }

    /// <summary>TileEngine::calculateTerrainBackground: light sources in the terrain and fires.</summary>
    private void CalculateTerrainBackground(BattleMapSubset area, RuntimeRuleCatalog rules)
    {
        var maxStatic = rules.Campaign.MaximumStaticLightDistance;
        foreach (var tile in TilesIn(area.Expand(maxStatic - 1)))
        {
            var light = 0;
            foreach (var part in (ReadOnlySpan<BattleTilePart>)[BattleTilePart.Floor, BattleTilePart.ObjectPart, BattleTilePart.WestWall, BattleTilePart.NorthWall])
                if (Map.MapData(tile, part) is { } data) light = Math.Max(light, data.LightSource);
            if (Map.Fire(tile) != 0) light = Math.Max(light, FireLightPower);
            if (light >= maxStatic) light = maxStatic - 1;
            AddLight(area, Map.PositionOf(tile), light, BattleLightLayer.Fire, rules);
        }
    }

    /// <summary>
    /// TileEngine::calculateTerrainItems: the glow of flares lying on the ground and the fire of burning units lying as items. A
    /// flare's glow range is its power (<c>getPowerBonus</c> without the owner's stat bonuses, which are Phase 4's).
    /// </summary>
    private void CalculateTerrainItems(BattleMapSubset area, RuntimeRuleCatalog rules)
    {
        var maxDynamic = rules.Campaign.MaximumDynamicLightDistance;
        var glow = new int[Map.TileCount];
        foreach (var item in _items)
        {
            if (item.Tile is not { } at || item.Slot != GroundSlot || !Map.Contains(at)) continue;
            var tile = Map.TileIndex(at);
            if (GlowRange(rules, item) is { } range) glow[tile] = Math.Max(glow[tile], range);
            if (IsBurningBody(item)) glow[tile] = Math.Max(glow[tile], StunnedFireLightPower);
        }
        foreach (var tile in TilesIn(area.Expand(maxDynamic - 1)))
        {
            var light = glow[tile];
            if (light >= maxDynamic) light = maxDynamic - 1;
            AddLight(area, Map.PositionOf(tile), light, BattleLightLayer.Items, rules);
        }
    }

    /// <summary>
    /// BattleItem::getGlow and getGlowRange: a flare glows while it has no fuse to wait for or its fuse is running; its range is its power.
    /// Null when the item does not glow.
    /// </summary>
    private static int? GlowRange(RuntimeRuleCatalog rules, BattleItem item)
    {
        if (!rules.Items.TryGet(item.ItemType, out var handle)) return null;
        var rule = rules.Items[handle].Value;
        if (rule.BattleType != FlareBattleType) return null;
        var equipment = rules.Battle.Equipment.Items[item.ItemType];
        var glows = equipment.FuseTrigger.DefaultBehavior && equipment.FuseType == NoFuseType || item.FuseEnabled && item.FuseTimer >= 0;
        return glows ? rule.Power : null;
    }

    /// <summary>The item is the body of a unit that burns (a stunned unit lying as an item).</summary>
    private bool IsBurningBody(BattleItem item) => item.CorpseUnitId is { } corpse && UnitById(corpse) is { Fire: not 0 };

    /// <summary>TileEngine::calculateUnitLighting: personal light, hand-held flares and fire of the units that are not out.</summary>
    private void CalculateUnitLighting(BattleMapSubset area, RuntimeRuleCatalog rules)
    {
        var maxDynamic = rules.Campaign.MaximumDynamicLightDistance;
        foreach (var unit in _units)
        {
            if (unit.IsOut || unit.Position is not { } position || !Map.Contains(position)) continue;
            var light = 0;
            if (rules.Armors.TryGet(unit.ArmorId, out var armorHandle))
            {
                var vision = rules.Armors[armorHandle].Value.Vision;
                light = Math.Max(light, unit.Faction switch
                {
                    BattleFaction.Player => TogglePersonalLight ? vision.PersonalLight : 0,
                    BattleFaction.Hostile => vision.PersonalLightHostile,
                    _ => vision.PersonalLightNeutral,
                });
            }
            foreach (var weapon in (ReadOnlySpan<BattleItem?>)[HandWeapon(unit, LeftHand), HandWeapon(unit, RightHand)])
            {
                if (weapon is null) continue;
                if (GlowRange(rules, weapon) is { } range) light = Math.Max(light, range);
                if (IsBurningBody(weapon)) light = Math.Max(light, StunnedFireLightPower);
            }
            if (unit.Fire != 0) light = Math.Max(light, FireLightPower);
            if (light >= maxDynamic) light = maxDynamic - 1;
            foreach (var covered in BattleMap.Footprint(position, unit.ArmorSize))
                AddLight(area, covered, light, BattleLightLayer.Units, rules);
        }
    }

    /// <summary>
    /// TileEngine::addLight: a circle of light around a tile, losing a level per tile of distance. The classic mode adds it as it is;
    /// the enhanced mode (<c>lighting: enhanced</c> bits 1 fire, 2 items, 4 units for that layer) traces two lines of voxels from the
    /// source to every tile, which walls, smoke, fire and low objects dim or stop.
    /// </summary>
    private void AddLight(BattleMapSubset area, BattlePosition center, int power, BattleLightLayer layer, RuntimeRuleCatalog rules)
    {
        if (power <= 0 || !Map.Contains(center)) return;
        var settings = rules.Campaign;
        var fire = layer == BattleLightLayer.Fire;
        var items = layer == BattleLightLayer.Items;
        var units = layer == BattleLightLayer.Units;
        var ground = items || fire;
        var centerTile = Map.TileIndex(center);
        var tileHeight = Map.TerrainLevel(centerTile);
        var divide = fire ? 8 : 4;
        var accuracyXy = 16 / divide;
        var accuracyZ = 24 / divide;
        var offsetCenter = (X: accuracyXy / 2 - 1, Y: accuracyXy / 2 - 1, Z: accuracyZ / 2 + (ground ? 0 : accuracyZ / 4) - tileHeight * accuracyZ / 24);
        var offsetTarget = (X: accuracyXy / 2 - 1, Y: accuracyXy / 2 - 1);
        var classic = (settings.EnhancedLighting & ((fire ? 1 : 0) | (items ? 2 : 0) | (units ? 4 : 0))) == 0;
        var topTargetVoxel = (short)(Map.Height * accuracyZ - 1);
        var topCenterVoxel = (short)((Map.LightCache(centerTile).BlocksUp ? center.Z + 1 : Map.Height) * accuracyZ - 1);
        var maxFirePower = Math.Min(15, settings.MaximumStaticLightDistance - 1);
        var reach = area.Intersect(BattleMapSubset.Around(center, power - 1)).Intersect(Map.Area);
        if (reach.IsEmpty) return;
        for (var z = 0; z < Map.Height; z++)
            for (var y = reach.BeginY; y < reach.EndY; y++)
                for (var x = reach.BeginX; x < reach.EndX; x++)
                {
                    var tile = Map.TileAt(x, y, z);
                    // Round(Position::distance(voxels) / 16), in single precision as the reference does.
                    int dx = (x - center.X) * 16, dy = (y - center.Y) * 16, dz = (z - center.Z) * 24;
                    var distance = (int)MathF.Floor((float)Math.Sqrt(dx * dx + dy * dy + dz * dz) / 16f + 0.5f);
                    var targetLight = Map.LightMulti(tile, layer);
                    var light = power - distance;
                    if (light <= targetLight) continue;
                    if (classic)
                    {
                        Map.AddLight(tile, light, layer);
                        continue;
                    }
                    var lit = EnhancedLight(new BattlePosition(x, y, z), tile, center, light, targetLight, fire, divide, accuracyXy,
                        accuracyZ, offsetCenter, offsetTarget, topTargetVoxel, topCenterVoxel, maxFirePower);
                    if (lit > targetLight) Map.AddLight(tile, lit, layer);
                }
    }

    /// <summary>The enhanced branch of addLight for one target tile: the light that reaches it along the two voxel lines.</summary>
    private int EnhancedLight(BattlePosition target, int targetTile, BattlePosition center, int currentLight, int targetLight,
        bool fire, int divide, int accuracyXy, int accuracyZ, (int X, int Y, int Z) offsetCenter, (int X, int Y) offsetTarget,
        short topTargetVoxel, short topCenterVoxel, int maxFirePower)
    {
        var differenceX = target.X - center.X;
        var differenceY = target.Y - center.Y;
        (int X, int Y, int Z) start = (center.X * accuracyXy + offsetCenter.X, center.Y * accuracyXy + offsetCenter.Y, center.Z * accuracyZ + offsetCenter.Z);
        var targetHeight = Math.Max(0, (Map.LightCache(targetTile).Height - 1) / (2 * divide));
        (int X, int Y, int Z) end = (target.X * accuracyXy + offsetTarget.X, target.Y * accuracyXy + offsetTarget.Y, target.Z * accuracyZ + accuracyZ / 2 + targetHeight);
        (int X, int Y, int Z) offsetA = (1, 0, 0);
        (int X, int Y, int Z) offsetB = (-1, 1, 0);
        if (differenceX > 0 ^ differenceY > 0)
        {
            offsetA = (1, 1, 0);
            offsetB = (-1, -1, 0);
        }
        start = (start.X + offsetA.X, start.Y + offsetA.Y, start.Z + offsetA.Z);
        end = (end.X + offsetA.X, end.Y + offsetA.Y, end.Z + offsetA.Z);
        // Do not peek out of the map.
        start.Z = Math.Min(start.Z, topCenterVoxel);
        end.Z = Math.Min(end.Z, topTargetVoxel);

        bool Block((int X, int Y, int Z) voxel, ref BattlePosition lastPoint, ref int light, ref int steps)
        {
            var height = voxel.Z % accuracyZ * divide;
            var point = new BattlePosition(voxel.X / accuracyXy, voxel.Y / accuracyXy, voxel.Z / accuracyZ);
            if (light <= 0) return true;
            if (point == lastPoint) return false;
            var direction = BattleGeometry.VectorToDirection(point.X - lastPoint.X, point.Y - lastPoint.Y);
            var lastTile = Map.TileAt(lastPoint.X, lastPoint.Y, lastPoint.Z);
            if (lastTile < 0)
            {
                light = 0;
                return true;
            }
            var cache = Map.LightCache(lastTile);
            var result = cache.BlocksStep(direction, point.Z - lastPoint.Z, point == target);
            if (steps > 1)
            {
                if (cache.HasFire && fire && light <= maxFirePower) result = true;
                else if (cache.HasSmoke) light -= 1;
                if (height < cache.Height) light -= 2;
            }
            steps++;
            lastPoint = point;
            if (result || light < targetLight)
            {
                light = 0;
                return true;
            }
            return false;
        }

        // The closure cannot hold the ref locals, so the two lines keep their state in these.
        var stateA = (Last: center, Light: currentLight, Steps: 0);
        var stateB = (Last: center, Light: currentLight, Steps: 0);
        BattleLines.Walk(start, end, voxel =>
        {
            var resultA = Block(voxel, ref stateA.Last, ref stateA.Light, ref stateA.Steps);
            var resultB = Block((voxel.X + offsetB.X, voxel.Y + offsetB.Y, voxel.Z + offsetB.Z), ref stateB.Last, ref stateB.Light, ref stateB.Steps);
            return resultA && resultB;
        });
        return (stateA.Light + stateB.Light) / 2;
    }
}
