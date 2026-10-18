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

    internal BattleState(string missionType, string deploymentId, int width, int length, int height, int turnLimit,
        int chronoTrigger, int cheatTurn)
    {
        ArgumentNullException.ThrowIfNull(missionType);
        ArgumentNullException.ThrowIfNull(deploymentId);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaximumMapSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaximumMapSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaximumMapHeight);
        MissionType = missionType;
        DeploymentId = deploymentId;
        Width = width;
        Length = length;
        Height = height;
        TurnLimit = turnLimit;
        ChronoTrigger = chronoTrigger;
        CheatTurn = cheatTurn;
    }

    public string MissionType { get; }
    public string DeploymentId { get; }
    public int Width { get; }
    public int Length { get; }
    public int Height { get; }
    public int TurnLimit { get; }
    public int ChronoTrigger { get; }
    public int CheatTurn { get; }

    /// <summary>SavedBattleGame::_turn: 0 until the first turn starts after the briefing.</summary>
    public int Turn { get; private set; }

    /// <summary>SavedBattleGame::_side: the faction whose turn it is.</summary>
    public BattleFaction Side { get; private set; } = BattleFaction.Player;

    /// <summary>Changes with every committed battle change, so clients know to refresh.</summary>
    public long Revision { get; private set; }

    public IReadOnlyList<BattleUnit> Units => _units;

    /// <summary>SavedBattleGame::getTileIndex.</summary>
    public int TileIndex(BattlePosition position) => position.X + position.Y * Width + position.Z * Width * Length;

    public bool Contains(BattlePosition position) =>
        (uint)position.X < (uint)Width && (uint)position.Y < (uint)Length && (uint)position.Z < (uint)Height;

    internal void AddUnit(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_units.Exists(existing => existing.Id == unit.Id))
            throw new InvalidOperationException($"Battle unit {unit.Id} already exists.");
        if (unit.Position is { } position && !Contains(position))
            throw new InvalidOperationException($"Battle unit {unit.Id} is outside the map.");
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
