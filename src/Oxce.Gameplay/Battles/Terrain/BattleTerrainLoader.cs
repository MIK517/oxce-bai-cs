using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Terrain;

/// <summary>
/// Loads terrain files on demand and keeps them for later battles: <c>MapDataSet::loadData</c> with
/// <c>MCDPatch::modifyData</c>, and the files <c>loadMAP</c> and <c>loadRMP</c> read for a map block. Each method
/// returns the reason the content is unusable, or null; the reference throws or crashes in those cases, and the port
/// refuses the battle before it changes anything (ADR 0038).
/// </summary>
internal sealed class BattleTerrainLoader(RuntimeBattleRules rules, IBattleTerrainFiles files)
{
    private readonly RuntimeBattleRules _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    private readonly IBattleTerrainFiles _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly Dictionary<string, BattleMapDataSet> _dataSets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BattleMapBlockData> _blocks = new(StringComparer.Ordinal);

    public string? TryLoadDataSet(string name, out BattleMapDataSet? dataSet)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_dataSets.TryGetValue(name, out dataSet)) return null;
        var path = $"TERRAIN/{name}.MCD";
        if (Read(path, out var bytes) is { } missing) return missing;
        McdTerrainData mcd;
        try
        {
            mcd = McdTerrainCodec.Decode(new BinaryDataReader(bytes));
        }
        catch (InvalidDataException exception)
        {
            return $"Invalid MCD file {path}: {exception.Message}";
        }
        var objects = new BattleMapData[mcd.Records.Count];
        for (var index = 0; index < objects.Length; index++) objects[index] = new BattleMapData(mcd.Records[index]);
        if (_rules.McdPatches.TryGetValue(name, out var patch) && Patch(name, patch, objects) is { } invalid) return invalid;
        // A TilePart outside the four parts would index past a tile's parts in the reference; a patch may repair it.
        for (var index = 0; index < objects.Length; index++)
            if ((uint)objects[index].ObjectType > (uint)BattleTilePart.ObjectPart)
                return $"MCD {name} object {index} has an invalid tile type {(int)objects[index].ObjectType}.";
        dataSet = new BattleMapDataSet(name, Array.AsReadOnly(objects));
        _dataSets.Add(name, dataSet);
        return null;
    }

    /// <summary>The MAP and RMP files of a map block, checked against the block's rule as <c>loadMAP</c> does.</summary>
    public string? TryLoadBlock(RuntimeMapBlock block, out BattleMapBlockData? data)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (_blocks.TryGetValue(block.Name, out data)) return null;
        var mapPath = $"MAPS/{block.Name}.MAP";
        if (Read(mapPath, out var mapBytes) is { } missingMap) return missingMap;
        XcomMapData map;
        try
        {
            map = XcomMapCodec.Decode(new BinaryDataReader(mapBytes));
        }
        catch (InvalidDataException exception)
        {
            return $"Invalid MAP file: {mapPath}: {exception.Message}";
        }
        // The reference reads the header as signed chars, so a side above 127 never matches a block's size.
        if (map.Width != block.Width || map.Length != block.Length || map.Width > sbyte.MaxValue || map.Length > sbyte.MaxValue)
            return $"Map block is not of the size specified {mapPath} is {(sbyte)map.Width}x{(sbyte)map.Length} , expected: {block.Width}x{block.Length}";
        if (map.Levels > sbyte.MaxValue) return $"Map block {mapPath} has a negative height.";
        var routePath = $"ROUTES/{block.Name}.RMP";
        if (Read(routePath, out var routes) is { } missingRoutes) return missingRoutes;
        data = new BattleMapBlockData(block.Name, map, routes);
        _blocks.Add(block.Name, data);
        return null;
    }

    /// <summary>
    /// The height a map block has once its MAP has been read: <c>loadMAP</c> sets the block's size from the header, and the
    /// reference keeps that on the shared block for later battles. Null before the first read.
    /// </summary>
    public int? LoadedLevels(string blockName) => _blocks.TryGetValue(blockName, out var block) ? block.Map.Levels : null;

    /// <summary>Every data set of a terrain, in order (<c>RuleTerrain::getMapData</c> indexes across them).</summary>
    public string? TryLoadTerrain(RuntimeTerrain terrain, out IReadOnlyList<BattleMapDataSet>? dataSets)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        dataSets = null;
        var loaded = new BattleMapDataSet[terrain.MapDataSets.Count];
        for (var index = 0; index < loaded.Length; index++)
        {
            if (TryLoadDataSet(terrain.MapDataSets[index], out var set) is { } reason) return reason;
            loaded[index] = set!;
        }
        dataSets = Array.AsReadOnly(loaded);
        return null;
    }

    private string? Read(string path, out ReadOnlyMemory<byte> bytes)
    {
        bytes = default;
        if (_files.Read(path) is not { } data) return $"Terrain file {path} is missing.";
        bytes = data;
        return null;
    }

    /// <summary>MCDPatch::modifyData: each property in the reference's order, its entries in rule order.</summary>
    private static string? Patch(string name, RuntimeMcdPatch patch, BattleMapData[] objects)
    {
        foreach (var entry in patch.Entries)
        {
            if (entry.Index >= (ulong)objects.Length) return $"MCD {name} has no object {entry.Index}";
            if (entry.Lofts is { Count: > BattleMapData.LoftLayers })
                return $"MCD patch {name} object {entry.Index} has more than {BattleMapData.LoftLayers} LOFTS.";
        }
        Apply("bigWall", static (target, value) => target.BigWall = value);
        Apply("TUWalk", static (target, value) => target.TimeUnitsWalk = value);
        Apply("TUFly", static (target, value) => target.TimeUnitsFly = value);
        Apply("TUSlide", static (target, value) => target.TimeUnitsSlide = value);
        Apply("deathTile", static (target, value) => target.DieMcd = value);
        Apply("terrainHeight", static (target, value) => target.TerrainLevel = value);
        Apply("specialType", static (target, value) => target.SpecialType = value);
        Apply("explosive", static (target, value) => target.Explosive = value);
        Apply("armor", static (target, value) => target.Armor = value);
        Apply("flammability", static (target, value) => target.Flammable = value);
        Apply("fuel", static (target, value) => target.Fuel = value);
        Apply("HEBlock", static (target, value) => target.SetHighExplosiveBlock(value));
        Apply("footstepSound", static (target, value) => target.FootstepSound = value);
        Apply("objectType", static (target, value) => target.ObjectType = (BattleTilePart)value);
        foreach (var entry in patch.Entries)
            if (entry.Booleans.TryGetValue("noFloor", out var noFloor)) objects[entry.Index].HasNoFloor = noFloor;
        foreach (var entry in patch.Entries)
            if (entry.Booleans.TryGetValue("stopLOS", out var stop)) objects[entry.Index].SetStopsLineOfSight(stop);
        foreach (var entry in patch.Entries)
            if (entry.Lofts is { } lofts)
                for (var layer = 0; layer < lofts.Count; layer++) objects[entry.Index].SetLoftId(layer, lofts[layer]);
        return null;

        void Apply(string key, Action<BattleMapData, int> set)
        {
            foreach (var entry in patch.Entries)
                if (entry.Integers.TryGetValue(key, out var value)) set(objects[entry.Index], value);
        }
    }
}
