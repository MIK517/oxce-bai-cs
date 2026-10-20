using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Terrain;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>Where units can stand when a battle is generated: spawn nodes, setUnitPosition's checks and placing near a friend.</summary>
internal sealed class BattlePlacement(BattleState battle, IRandomSource random)
{
    private const int InvalidMoveCost = BattlePathing.InvalidMoveCost;

    /// <summary>Node::TYPE_FLYING and TYPE_SMALL.</summary>
    private const int FlyingNode = 0x01;
    private const int SmallNode = 0x02;

    private readonly BattleState _battle = battle;
    private readonly BattleMap _map = battle.Map;
    private readonly IRandomSource _random = random;

    /// <summary>
    /// SavedBattleGame::getSpawnNode: a random node of the rank among the highest-priority ones that fit the unit (size,
    /// flying) and where it can stand.
    /// </summary>
    public BattleNode? SpawnNode(int rank, BattleUnit unit)
    {
        var highest = -1;
        var compliant = new List<BattleNode>();
        foreach (var node in _map.Nodes)
        {
            if (node.IsDummy || node.Rank != rank || node.Priority <= 0) continue;
            if ((node.Type & SmallNode) != 0 && unit.ArmorSize != 1) continue;
            if ((node.Type & FlyingNode) != 0 && unit.MovementType != 1) continue;
            if (TryPosition(unit, node.Position) is null) continue;
            if (node.Priority > highest)
            {
                highest = node.Priority;
                compliant.Clear();
            }
            if (node.Priority == highest) compliant.Add(node);
        }
        return compliant.Count == 0 ? null : compliant[_random.NextInclusive(0, compliant.Count - 1)];
    }

    /// <summary>
    /// SavedBattleGame::setUnitPosition's checks: every tile under the unit inside the map, free of other units, passable,
    /// with a floor unless the unit flies, and without a low big wall; a raised tile (terrain level -24) lifts the unit
    /// one level. Returns the position the unit would take, or null.
    /// </summary>
    public BattlePosition? TryPosition(BattleUnit unit, BattlePosition position)
    {
        var size = unit.ArmorSize - 1;
        var zOffset = 0;
        for (var x = size; x >= 0; x--)
            for (var y = size; y >= 0; y--)
            {
                var at = new BattlePosition(position.X + x, position.Y + y, position.Z + zOffset);
                if (!_map.Contains(at)) return null;
                var tile = _map.TileIndex(at);
                var item = _map.MapData(tile, BattleTilePart.ObjectPart);
                if (_map.UnitAt(tile) is { } other && other != unit.Id ||
                    _map.TimeUnitCost(tile, BattleTilePart.ObjectPart, unit.MovementType) == InvalidMoveCost ||
                    _map.HasNoFloor(at) && unit.MovementType != 1 ||
                    item is { BigWall: > 0 and <= 3 })
                    return null;
                if (_map.TerrainLevel(tile) == -24)
                {
                    zOffset++;
                    x = size;
                    y = size + 1;
                }
            }
        var placed = position with { Z = position.Z + zOffset };
        // A large unit must not straddle a wall between its tiles.
        if (size > 0)
            for (var direction = 2; direction <= 4; direction++)
                if (BattlePathing.IsBlockedDirection(_map, unit.MovementType, placed, direction)) return null;
        return placed;
    }

    /// <summary>
    /// BattlescapeGenerator::placeUnitNearFriend: up to ten times, a random placed unit of the same side at least as large
    /// (100 draws at most) and a place next to it.
    /// </summary>
    public bool PlaceNearFriend(BattleUnit unit)
    {
        if (_battle.Units.Count == 0) return false;
        for (var attempt = 0; attempt != 10; attempt++)
        {
            BattlePosition? entry = null;
            var largeFriend = false;
            var tries = 100;
            while (entry is null && tries != 0)
            {
                var friend = _battle.Units[_random.NextInclusive(0, _battle.Units.Count - 1)];
                if (friend.Faction == unit.Faction && friend.Position is { } at && friend.ArmorSize >= unit.ArmorSize)
                {
                    entry = at;
                    largeFriend = friend.ArmorSize > 1;
                }
                tries--;
            }
            if (tries != 0 && PlaceNearPosition(unit, entry!.Value, largeFriend)) return true;
        }
        return false;
    }

    /// <summary>
    /// SavedBattleGame::placeUnitNearPosition: the position itself, then the eight neighbours (offset by the unit's and the
    /// friend's size) not walled off from it, then one level up for a flyer over open air.
    /// </summary>
    private bool PlaceNearPosition(BattleUnit unit, BattlePosition entry, bool largeFriend)
    {
        if (TryPosition(unit, entry) is { } here)
        {
            unit.Position = here;
            return true;
        }
        var me = -unit.ArmorSize;
        var you = largeFriend ? 2 : 1;
        ReadOnlySpan<int> xs = [0, you, you, you, 0, me, me, me];
        ReadOnlySpan<int> ys = [me, me, 0, you, you, you, 0, me];
        for (var direction = 0; direction <= 7; direction++)
        {
            var offset = new BattlePosition(entry.X + xs[direction], entry.Y + ys[direction], entry.Z);
            // Position division truncates toward zero, as C++ does.
            var half = new BattlePosition(entry.X + xs[direction] / 2, entry.Y + ys[direction] / 2, entry.Z);
            if (_map.Contains(offset) && _map.Contains(half) &&
                !BattlePathing.IsBlockedDirection(_map, unit.MovementType, half, direction) && TryPosition(unit, offset) is { } placed)
            {
                unit.Position = placed;
                return true;
            }
        }
        var above = entry with { Z = entry.Z + 1 };
        if (unit.MovementType == 1 && _map.Contains(above) && _map.HasNoFloor(above) && TryPosition(unit, above) is { } flying)
        {
            unit.Position = flying;
            return true;
        }
        return false;
    }
}
