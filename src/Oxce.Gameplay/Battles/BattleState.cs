namespace Oxce.Gameplay.Battles;

/// <summary>
/// The battle aggregate (ADR 0038): the state <c>SavedBattleGame</c> holds. Live state keeps runtime handles and
/// mutable classes; it is captured only at the persistence boundary. One battle belongs to one campaign, which
/// serializes access to it.
/// </summary>
internal sealed class BattleState
{
    /// <summary>BattleUnit::MAX_SOLDIER_ID: geoscape soldiers keep their IDs; other units are numbered from here.</summary>
    internal const int MaximumSoldierId = 1_000_000;

    /// <summary>The largest map side the port accepts; reference maps are at most a few hundred tiles across.</summary>
    internal const int MaximumMapSide = 1_000;

    /// <summary>The most levels the port accepts (the reference's map blocks and vertical levels stay far below).</summary>
    internal const int MaximumMapHeight = 64;

    private readonly List<BattleUnit> _units = [];
    private readonly List<BattleItem> _items = [];

    internal BattleState(string missionType, string deploymentId, BattleMap map, int turnLimit, int chronoTrigger, int cheatTurn)
    {
        ArgumentNullException.ThrowIfNull(missionType);
        ArgumentNullException.ThrowIfNull(deploymentId);
        ArgumentNullException.ThrowIfNull(map);
        MissionType = missionType;
        DeploymentId = deploymentId;
        Map = map;
        TurnLimit = turnLimit;
        ChronoTrigger = chronoTrigger;
        CheatTurn = cheatTurn;
    }

    public string MissionType { get; }
    public string DeploymentId { get; }
    public BattleMap Map { get; }
    public int Width => Map.Width;
    public int Length => Map.Length;
    public int Height => Map.Height;
    public int TurnLimit { get; }
    public int ChronoTrigger { get; }
    public int CheatTurn { get; }

    /// <summary>The terrain the map was generated with (SavedBattleGame's terrain for later stages and scripts).</summary>
    public string TerrainId { get; init; } = "";

    /// <summary>SavedBattleGame::getLastUsedMapScript.</summary>
    public string MapScriptId { get; init; } = "";

    /// <summary>SavedBattleGame::getDepth: 0 above water, otherwise the underwater depth.</summary>
    public int Depth { get; init; }

    /// <summary>SavedBattleGame::getGlobalShade (0-15).</summary>
    public int GlobalShade { get; init; }

    /// <summary>SavedBattleGame::getSelectedUnit: the first X-COM unit placed, or null.</summary>
    public int? SelectedUnitId { get; internal set; }

    /// <summary>The craft inventory tile: where the X-COM units' equipment starts (BattleUnit::setInventoryTile).</summary>
    public BattlePosition? CraftInventoryTile { get; internal set; }

    /// <summary>SavedBattleGame::_turn: 0 until the first turn starts after the briefing.</summary>
    public int Turn { get; private set; }

    /// <summary>SavedBattleGame::_side: the faction whose turn it is.</summary>
    public BattleFaction Side { get; private set; } = BattleFaction.Player;

    /// <summary>Changes with every committed battle change, so clients know to refresh.</summary>
    public long Revision { get; private set; }

    public IReadOnlyList<BattleUnit> Units => _units;

    /// <summary>SavedBattleGame::getItems, in creation order.</summary>
    public IReadOnlyList<BattleItem> Items => _items;

    /// <summary>SavedBattleGame::getCurrentItemId: the ID the next item gets.</summary>
    public int NextItemId { get; private set; }

    /// <summary>SavedBattleGame::createItemForTile: a new item on the ground of a tile (or nowhere when the tile is null).</summary>
    internal BattleItem CreateItemForTile(string itemType, int clipSize, BattlePosition? tile)
    {
        if (tile is { } position && !Contains(position)) throw new InvalidOperationException($"Item tile {position} is outside the map.");
        var item = new BattleItem(NextItemId++, itemType)
        {
            Tile = tile,
            Slot = tile is null ? "" : GroundSlot,
            AmmoQuantity = clipSize,
        };
        _items.Add(item);
        Revision++;
        return item;
    }

    /// <summary>Mod::getInventoryGround.</summary>
    internal const string GroundSlot = "STR_GROUND";

    /// <summary>SavedBattleGame::getTileIndex.</summary>
    public int TileIndex(BattlePosition position) => Map.TileIndex(position);

    public bool Contains(BattlePosition position) => Map.Contains(position);

    internal void AddUnit(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_units.Exists(existing => existing.Id == unit.Id))
            throw new InvalidOperationException($"Battle unit {unit.Id} already exists.");
        if (unit.Position is { } position)
        {
            for (var x = 0; x < unit.ArmorSize; x++)
                for (var y = 0; y < unit.ArmorSize; y++)
                {
                    var covered = new BattlePosition(position.X + x, position.Y + y, position.Z);
                    if (!Contains(covered)) throw new InvalidOperationException($"Battle unit {unit.Id} is outside the map.");
                    if (Map.UnitAt(Map.TileIndex(covered)) is { } other && other != unit.Id)
                        throw new InvalidOperationException($"Battle unit {unit.Id} stands on unit {other}.");
                }
            // BattleUnit::setTile: every tile under the unit's armor.
            for (var x = 0; x < unit.ArmorSize; x++)
                for (var y = 0; y < unit.ArmorSize; y++)
                    Map.SetUnitAt(Map.TileIndex(new BattlePosition(position.X + x, position.Y + y, position.Z)), unit.Id);
        }
        _units.Add(unit);
        Revision++;
    }

    internal void SetSpotted(BattleUnit unit, BattleFaction faction, bool spotted)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!_units.Contains(unit)) throw new InvalidOperationException($"Battle unit {unit.Id} is not in this battle.");
        if (unit.IsSpottedBy(faction) == spotted) return;
        unit.SetSpottedBy(faction, spotted);
        Revision++;
    }

    /// <summary>Whether a faction can see a unit: its own units always, others once spotted.</summary>
    public static bool Sees(BattleFaction faction, BattleUnit unit) => unit.Faction == faction || unit.IsSpottedBy(faction);

    public BattleOverview Overview(BattleViewer viewer)
    {
        if (!Enum.IsDefined(viewer.Faction)) throw new ArgumentOutOfRangeException(nameof(viewer));
        var units = new List<BattleUnitView>();
        foreach (var unit in _units)
            if (Sees(viewer.Faction, unit)) units.Add(new BattleUnitView(unit.Id, unit.Faction, unit.Position));
        return new BattleOverview(MissionType, DeploymentId, Width, Length, Height, Turn, Side, TurnLimit, Revision,
            units.AsReadOnly());
    }

    /// <summary>Checks the invariants every generated or restored battle keeps.</summary>
    public void Validate()
    {
        var ids = new HashSet<int>();
        foreach (var unit in _units)
        {
            if (!ids.Add(unit.Id)) throw new InvalidOperationException($"Battle unit {unit.Id} is listed twice.");
            if (unit.Position is { } position && !Contains(position))
                throw new InvalidOperationException($"Battle unit {unit.Id} is outside the map.");
        }
    }
}

/// <summary>A unit in a battle (BattleUnit): identity, side and controller so far.</summary>
internal sealed class BattleUnit
{
    // One bit per faction that has spotted this unit (ADR 0038: visibility is per faction).
    private int _spottedBy;

    public BattleUnit(int id, BattleFaction faction, BattleController controller)
    {
        if (!Enum.IsDefined(faction)) throw new ArgumentOutOfRangeException(nameof(faction));
        if (!Enum.IsDefined(controller.Kind)) throw new ArgumentOutOfRangeException(nameof(controller));
        Id = id;
        Faction = faction;
        Controller = controller;
    }

    public int Id { get; }
    public BattleFaction Faction { get; }
    public BattleController Controller { get; }

    /// <summary>BattleUnit::getType: the soldier type or the unit rule.</summary>
    public string UnitType { get; init; } = "";

    /// <summary>The geoscape soldier this unit is, or null for vehicles and aliens.</summary>
    public int? SoldierId { get; init; }

    /// <summary>The armor the unit fights in (after transformations and replacements) and its size and movement type.</summary>
    public string ArmorId { get; init; } = "";

    /// <summary>The geoscape soldier's own armor when the battle armor differs (transformed or replaced), else "".</summary>
    public string OriginalArmorId { get; init; } = "";
    public int ArmorSize { get; init; } = 1;
    public int MovementType { get; init; }

    /// <summary>BattleUnit::getDirection (0-7).</summary>
    public int Direction { get; set; }

    /// <summary>The unit's tile, or null while it is not on the map.</summary>
    public BattlePosition? Position { get; set; }

    public bool IsSpottedBy(BattleFaction faction) => (_spottedBy & (1 << (int)faction)) != 0;

    // Through BattleState.SetSpotted, which counts the revision.
    internal void SetSpottedBy(BattleFaction faction, bool spotted)
    {
        if (!Enum.IsDefined(faction)) throw new ArgumentOutOfRangeException(nameof(faction));
        _spottedBy = spotted ? _spottedBy | 1 << (int)faction : _spottedBy & ~(1 << (int)faction);
    }
}
