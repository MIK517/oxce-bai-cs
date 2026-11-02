using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The field of view of the battle (Phase 7 branch 4 S4): the line of fire templates sight reads, when the battle recomputes everyone's
/// sight, and the tile and spotting bookkeeping of <c>BattleUnit::addToVisibleTiles</c> and its siblings. The sight itself is
/// <see cref="BattleSight"/>.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>The line of fire templates (<c>Mod::getVoxelData</c>) the battle sees by; set when the battle is generated or restored.</summary>
    internal BattleVoxelData? VoxelData { get; set; }

    /// <summary>
    /// SavedBattleGame::isBeforeGame: true from generation until <c>resetUnitTiles</c> puts the units on their tiles (the first turn's start,
    /// or a load), while every unit is excluded from voxel lines.
    /// </summary>
    internal bool IsBeforeGame { get; set; } = true;

    /// <summary>The sight of this battle, for one operation.</summary>
    internal BattleSight Sight() => new(this);

    /// <summary>TileEngine::recalculateFOV: every unit with a tile sees again, tiles and units.</summary>
    internal void RecalculateFov()
    {
        Sight().RecalculateFov();
        Revision++;
    }

    /// <summary>
    /// BattleUnit::addToVisibleTiles: the tile joins the unit's visible tiles; the faction explores it when it is that faction's turn
    /// (<c>Tile::setLastExplored</c>), and a player's unit adds one to its visibility. True if new to the unit.
    /// </summary>
    internal bool AddToVisibleTiles(BattleUnit unit, int tile)
    {
        if (Side == unit.Faction) Map.SetLastExplored(tile, unit.Faction, Turn);
        if (!unit.AddVisibleTile(tile)) return false;
        if (unit.Faction == BattleFaction.Player) Map.SetVisibility(tile, Map.Visibility(tile) + 1);
        return true;
    }

    /// <summary>
    /// What stops sight from working: a line of fire template the battle's map objects or units point past the end of LOFTEMPS.DAT (the
    /// reference reads it with <c>vector::at</c> and throws). Null when all are present.
    /// </summary>
    internal string? VoxelDataFault(RuntimeRuleCatalog rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (VoxelData is not { } voxels) return $"The battle has no {BattleVoxelData.VirtualPath}.";
        foreach (var set in Map.DataSets)
            for (var index = 0; index < set.Objects.Count; index++)
                for (var layer = 0; layer < BattleMapData.LoftLayers; layer++)
                    if (!voxels.Has(set.Objects[index].LoftId(layer)))
                        return $"Map data {set.Name} object {index} refers to line of fire template {set.Objects[index].LoftId(layer)}, which {BattleVoxelData.VirtualPath} lacks.";
        foreach (var unit in _units)
        {
            if (!rules.Battle.Equipment.Armors.TryGetValue(unit.ArmorId, out var armor)) continue;
            foreach (var template in armor.Loftemps)
                if (!voxels.Has(template))
                    return $"Armor {unit.ArmorId} refers to line of fire template {template}, which {BattleVoxelData.VirtualPath} lacks.";
            if (armor.Loftemps.Count < unit.ArmorSize * unit.ArmorSize)
                return $"Armor {unit.ArmorId} has {armor.Loftemps.Count} line of fire templates for its {unit.ArmorSize * unit.ArmorSize} tiles.";
        }
        return null;
    }
}

internal sealed partial class BattleUnit
{
    /// <summary>Unit::getSpotterDuration: how many turns sniper units can use this unit's sightings (a soldier has no unit rule: 0).</summary>
    public int SpotterDuration(RuntimeRuleCatalog rules) =>
        SoldierId is null && rules.Battle.Equipment.Units.TryGetValue(UnitType, out var rule) ? rule.SpotterDuration : 0;

    /// <summary>
    /// BattleUnit::checkViewSector: whether a tile lies in the half-plane quadrant the unit faces from any of its tiles (the body's direction;
    /// <c>strafe</c> is off, so the turret never decides, and <c>aiCheatMode</c> is off).
    /// </summary>
    public bool CheckViewSector(BattlePosition position)
    {
        var self = Position ?? throw new InvalidOperationException($"Battle unit {Id} is not on the map.");
        for (var x = 0; x < ArmorSize; ++x)
            for (var y = 0; y < ArmorSize; ++y)
            {
                var deltaX = position.X - (self.X + x);
                var deltaY = self.Y + y - position.Y;
                var inside = Direction switch
                {
                    0 => deltaX + deltaY >= 0 && deltaY - deltaX >= 0,
                    1 => deltaX >= 0 && deltaY >= 0,
                    2 => deltaX + deltaY >= 0 && deltaY - deltaX <= 0,
                    3 => deltaY <= 0 && deltaX >= 0,
                    4 => deltaX + deltaY <= 0 && deltaY - deltaX <= 0,
                    5 => deltaX <= 0 && deltaY <= 0,
                    6 => deltaX + deltaY <= 0 && deltaY - deltaX >= 0,
                    7 => deltaY >= 0 && deltaX <= 0,
                    _ => false,
                };
                if (inside) return true;
            }
        return false;
    }
}
