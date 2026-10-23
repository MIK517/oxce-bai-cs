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

    /// <summary>SavedBattleGame's bug hunt mode (off at the start) and the turn it may start from.</summary>
    public bool BughuntMode { get; internal set; }
    public int BughuntMinTurn { get; internal set; }

    /// <summary>SavedBattleGame::getVIPSurvivalPercentage and getVIPEscapeType.</summary>
    public int VipSurvivalPercentage { get; internal set; }
    public int VipEscapeType { get; internal set; }

    /// <summary>SavedBattleGame::getObjectiveType (a special tile type, -1 for none) and getObjectiveCount.</summary>
    public int ObjectiveType { get; internal set; } = -1;
    public int ObjectiveCount { get; internal set; }

    private readonly List<BattleItem> _guaranteedRecovered = [];

    /// <summary>SavedBattleGame::getGuaranteedRecoveredItems: items off the map that go home after the battle, by ID.</summary>
    public IReadOnlyList<BattleItem> GuaranteedRecoveredItems => _guaranteedRecovered;

    /// <summary>BattlescapeGenerator::sendItemsToLimbo: the items leave the map (and the battle's items) for guaranteed recovery.</summary>
    internal void SendToLimbo(IReadOnlyCollection<BattleItem> items)
    {
        foreach (var item in items)
        {
            item.Tile = null;
            item.FuseTimer = -1;
        }
        _items.RemoveAll(items.Contains);
        _guaranteedRecovered.AddRange(items);
        _guaranteedRecovered.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        Revision++;
    }

    /// <summary>SavedBattleGame::getAlienItemLevel: the month row of Mod::getAlienItemLevels the aliens draw from.</summary>
    public int AlienItemLevel { get; internal set; }

    /// <summary>SavedBattleGame's reinforcements deployment, race and item level (the alien deployment's, kept for later waves).</summary>
    public string ReinforcementsDeployment { get; internal set; } = "";
    public string ReinforcementsRace { get; internal set; } = "";
    public int ReinforcementsItemLevel { get; internal set; }

    /// <summary>SavedBattleGame::getItems, in creation order.</summary>
    public IReadOnlyList<BattleItem> Items => _items;

    /// <summary>SavedBattleGame::getCurrentItemId: the ID the next item gets.</summary>
    public int NextItemId { get; private set; }

    /// <summary>
    /// <c>new BattleItem(rule, getCurrentItemId())</c>: an item with the next ID, which is taken whether or not the item
    /// is kept (an item that fits nowhere is deleted but its ID stays used).
    /// </summary>
    internal BattleItem NewItem(string itemType) => new(NextItemId++, itemType);

    /// <summary>Adds a created item to the battle's items (SavedBattleGame::getItems push_back).</summary>
    internal void AddItem(BattleItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Tile is { } position && !Contains(position)) throw new InvalidOperationException($"Item tile {position} is outside the map.");
        _items.Add(item);
        Revision++;
    }

    /// <summary>SavedBattleGame::createItemForTile: the item on the ground of a tile (or nowhere when the tile is null).</summary>
    internal BattleItem AddItemOnTile(BattleItem item, BattlePosition? tile)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Tile = tile;
        item.Slot = tile is null ? "" : GroundSlot;
        AddItem(item);
        return item;
    }

    /// <summary>BattleUnit::getInventory: the items a unit carries in its inventory sections, in the order they arrived.</summary>
    internal IEnumerable<BattleItem> InventoryOf(BattleUnit unit) =>
        _items.Where(item => item.OwnerUnitId == unit.Id && item.Slot.Length != 0);

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

    /// <summary>BattleUnit::getBaseStats: the soldier's statistics with all bonuses, or the unit rule's plus its armor's.</summary>
    public BattleUnitStats Stats { get; init; }

    /// <summary>BattleUnit::getTimeUnits, getEnergy, getHealth, getMana, getMorale, getStunlevel.</summary>
    public int TimeUnits { get; set; }
    public int Energy { get; set; }
    public int Health { get; set; }
    public int Mana { get; set; }
    public int Morale { get; set; } = 100;
    public int StunLevel { get; set; }

    /// <summary>BattleUnit::isVIP: a civilian the deployment marks as a VIP.</summary>
    public bool IsVip { get; set; }

    /// <summary>BattleUnit::getRankInt (aliens: the deployment rank).</summary>
    public int Rank { get; set; }

    /// <summary>AIModule::setStartNode: the node an alien spawned on, or null.</summary>
    public int? StartNodeId { get; set; }

    /// <summary>BattleUnit::getTurretType: the turret sprite of a vehicle's weapon, -1 without one.</summary>
    public int TurretType { get; set; } = -1;

    /// <summary>BattleUnit::getSpecialWeapon: the special built-in items, at most SPEC_WEAPON_MAX.</summary>
    public List<int> SpecialWeaponIds { get; } = [];

    public bool IsSpottedBy(BattleFaction faction) => (_spottedBy & (1 << (int)faction)) != 0;

    // Through BattleState.SetSpotted, which counts the revision.
    internal void SetSpottedBy(BattleFaction faction, bool spotted)
    {
        if (!Enum.IsDefined(faction)) throw new ArgumentOutOfRangeException(nameof(faction));
        _spottedBy = spotted ? _spottedBy | 1 << (int)faction : _spottedBy & ~(1 << (int)faction);
    }
}
