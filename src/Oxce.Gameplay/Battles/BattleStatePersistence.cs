using System.Collections.ObjectModel;
using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The battle root's state that only the save, the briefing and later branches use, and the capture and restore of
/// <see cref="BattleSnapshot"/> (Phase 7 branch 2 S1). Defaults are the <c>SavedBattleGame</c> constructor's.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>SavedBattleGame's <c>_strTarget</c> and <c>_strCraftOrBase</c>: set by the briefing, "" until then.</summary>
    public string TargetName { get; internal set; } = "";
    public string CraftOrBaseName { get; internal set; } = "";

    /// <summary>SavedBattleGame's <c>_nameDisplay</c> and <c>_animFrame</c> (presentation, kept for the save).</summary>
    public bool NameDisplay { get; internal set; }
    public int AnimFrame { get; internal set; }

    /// <summary>SavedBattleGame::getUndoUnit's ID, or null.</summary>
    public int? UndoUnitId { get; internal set; }

    private readonly SortedDictionary<string, int> _reinforcementsMemory = new(StringComparer.Ordinal);

    /// <summary>SavedBattleGame's <c>_reinforcementsMemory</c>: how often each reinforcement wave has come.</summary>
    public IReadOnlyDictionary<string, int> ReinforcementsMemory => _reinforcementsMemory;

    /// <summary>SavedBattleGame's VIP counters (<c>_vipsSaved</c> to <c>_vipsWaitingOutsideScore</c>).</summary>
    public int VipsSaved { get; internal set; }
    public int VipsLost { get; internal set; }
    public int VipsWaitingOutside { get; internal set; }
    public int VipsSavedScore { get; internal set; }
    public int VipsLostScore { get; internal set; }
    public int VipsWaitingOutsideScore { get; internal set; }

    /// <summary>SavedBattleGame::getObjectivesDestroyed.</summary>
    public int ObjectivesDestroyed { get; internal set; }

    /// <summary>SavedBattleGame::getTUReserved (a BattleActionType, BA_NONE = 0) and getKneelReserved.</summary>
    public int TimeUnitsReserved { get; internal set; }
    public bool KneelReserved { get; internal set; }

    /// <summary>
    /// SavedBattleGame's ambience: the terrain's looping sound (-1 for none) and volume, its random sounds and their delay
    /// range in seconds, and the tenths of a second until the next random sound.
    /// </summary>
    public int Ambience { get; internal set; } = -1;
    public double AmbientVolume { get; internal set; } = 0.5;
    public IReadOnlyList<int> AmbienceRandom { get; internal set; } = [];
    public int MinimumAmbienceRandomDelay { get; internal set; } = 20;
    public int MaximumAmbienceRandomDelay { get; internal set; } = 60;
    public int CurrentAmbienceDelay { get; internal set; }

    /// <summary>SavedBattleGame's <c>_togglePersonalLight</c>, <c>_toggleNightVision</c> and <c>_toggleBrightness</c>.</summary>
    public bool TogglePersonalLight { get; internal set; } = true;
    public bool ToggleNightVision { get; internal set; }
    public int ToggleBrightness { get; internal set; }

    /// <summary>The root's script values (owner <c>BattleGame</c>); null only for a battle built without content.</summary>
    internal ScriptValueState? ScriptValues { get; set; }

    /// <summary>
    /// generateMap's ambience: the terrain's looping sound, or else its random sounds with their delay range and a first
    /// delay (resetCurrentAmbienceDelay); the terrain's volume either way. The delay draws from <paramref name="seedless"/>,
    /// the reference's <c>RNG::seedless</c>, never the game's stream.
    /// </summary>
    internal void SetAmbience(RuntimeTerrain terrain, IRandomSource seedless)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(seedless);
        Ambience = -1;
        AmbienceRandom = [];
        if (terrain.Ambience != -1)
        {
            Ambience = terrain.Ambience;
        }
        else
        {
            AmbienceRandom = terrain.RandomAmbience;
            MinimumAmbienceRandomDelay = terrain.MinimumAmbienceDelay;
            MaximumAmbienceRandomDelay = terrain.MaximumAmbienceDelay;
            // RNG::seedless(min * 10, max * 10), at least one second. A reversed range is undefined in the reference
            // (a modulo by a wrapped or zero range), and so is a delay whose tenths overflow an int; the port takes the
            // minimum, clamped to the int range (ADR 0039).
            var minimum = (long)MinimumAmbienceRandomDelay * 10;
            var maximum = (long)MaximumAmbienceRandomDelay * 10;
            var delay = maximum < minimum || minimum < int.MinValue || maximum > int.MaxValue
                ? (int)Math.Clamp(minimum, int.MinValue, int.MaxValue)
                : seedless.NextInclusive((int)minimum, (int)maximum);
            CurrentAmbienceDelay = Math.Max(delay, 10);
        }
        AmbientVolume = terrain.AmbientVolume;
    }

    /// <summary>The battle's saved state (SavedBattleGame::save); generation-only state is not part of it (the plan's table).</summary>
    public BattleSnapshot Capture() => new(Width, Length, Height, MissionType)
    {
        TargetName = TargetName,
        CraftOrBaseName = CraftOrBaseName,
        StartingConditionId = StartingConditionId,
        EnviroEffectsId = EnviroEffectsId,
        NameDisplay = NameDisplay,
        EnvironmentalConditionFriendly = EnvironmentalConditions.Friendly,
        EnvironmentalConditionHostile = EnvironmentalConditions.Hostile,
        EnvironmentalConditionNeutral = EnvironmentalConditions.Neutral,
        AlienCustomDeploy = AlienCustomDeploy,
        AlienCustomMission = AlienCustomMission,
        AlienItemLevel = AlienItemLevel,
        MapScriptId = MapScriptId,
        ReinforcementsDeployment = ReinforcementsDeployment,
        ReinforcementsRace = ReinforcementsRace,
        ReinforcementsItemLevel = ReinforcementsItemLevel,
        ReinforcementsMemory = Counts(_reinforcementsMemory),
        GlobalShade = GlobalShade,
        Turn = Turn,
        BughuntMinTurn = BughuntMinTurn,
        BughuntMode = BughuntMode,
        Depth = Depth,
        AnimFrame = AnimFrame,
        SelectedUnitId = SelectedUnitId ?? -1,
        UndoUnitId = UndoUnitId ?? -1,
        VipEscapeType = VipEscapeType,
        VipSurvivalPercentage = VipSurvivalPercentage,
        VipsSaved = VipsSaved,
        VipsLost = VipsLost,
        VipsWaitingOutside = VipsWaitingOutside,
        VipsSavedScore = VipsSavedScore,
        VipsLostScore = VipsLostScore,
        VipsWaitingOutsideScore = VipsWaitingOutsideScore,
        ObjectiveType = ObjectiveType,
        ObjectivesDestroyed = ObjectivesDestroyed,
        ObjectivesNeeded = ObjectiveCount,
        TimeUnitsReserved = TimeUnitsReserved,
        KneelReserved = KneelReserved,
        Ambience = Ambience,
        AmbientVolume = AmbientVolume,
        AmbienceRandom = Array.AsReadOnly(AmbienceRandom.ToArray()),
        MinimumAmbienceRandomDelay = MinimumAmbienceRandomDelay,
        MaximumAmbienceRandomDelay = MaximumAmbienceRandomDelay,
        CurrentAmbienceDelay = CurrentAmbienceDelay,
        Music = Music,
        BaseItems = Counts(BaseStorageItems),
        TurnLimit = TurnLimit,
        ChronoTrigger = ChronoTrigger,
        CheatTurn = CheatTurn,
        TogglePersonalLight = TogglePersonalLight,
        ToggleNightVision = ToggleNightVision,
        ToggleBrightness = ToggleBrightness,
        ScriptValues = Array.AsReadOnly((ScriptValues?.Capture() ?? []).ToArray()),
        ReinforcementsBlocks = Rows(Map.ReinforcementBlocks),
        FlattenedMapTerrainNames = Rows(Map.TerrainNames),
        FlattenedMapBlockNames = Rows(Map.BlockNames),
        MapDataSets = Array.AsReadOnly(Map.DataSets.Select(static set => set.Name).ToArray()),
        Tiles = CaptureTiles(),
        Nodes = Array.AsReadOnly(Map.Nodes.Select(static node => new BattleNodeSnapshot(node.Id, node.Position, node.Type, node.Rank,
            node.Flags, node.Reserved, node.Priority, node.Allocated, Array.AsReadOnly(node.Links.ToArray()), node.IsDummy)).ToArray()),
        ModuleMap = Array.AsReadOnly(BaseModules.Select(static row => (IReadOnlyList<BattleModuleSnapshot>)Array.AsReadOnly(
            row.Select(static module => new BattleModuleSnapshot(module.Total, module.Remaining)).ToArray())).ToArray()),
    };

    /// <summary>
    /// SavedBattleGame::save's tiles: every tile that is not void (Tile::isVoid: a part with map data, smoke or an item on
    /// the ground), in index order. A UFO door is open when its frame is set (Tile::isUfoDoorOpen).
    /// </summary>
    private ReadOnlyCollection<BattleTileSnapshot> CaptureTiles()
    {
        var itemTiles = new HashSet<int>();
        foreach (var item in _items)
            if (item.Tile is { } position) itemTiles.Add(Map.TileIndex(position));
        var tiles = new List<BattleTileSnapshot>();
        for (var tile = 0; tile < Map.TileCount; tile++)
        {
            if (Map.HasNoPartsOrSmoke(tile) && !itemTiles.Contains(tile)) continue;
            var saved = new BattleTileSnapshot(tile)
            {
                Smoke = Map.Smoke(tile),
                Fire = Map.Fire(tile),
                FloorDiscovered = Map.IsDiscovered(tile, BattleTilePart.Floor),
                WestWallDiscovered = Map.IsDiscovered(tile, BattleTilePart.WestWall),
                NorthWallDiscovered = Map.IsDiscovered(tile, BattleTilePart.NorthWall),
                WestDoorOpen = Map.IsUfoDoorOpen(tile, BattleTilePart.WestWall),
                NorthDoorOpen = Map.IsUfoDoorOpen(tile, BattleTilePart.NorthWall),
                LastExploredByHostile = Map.LastExplored(tile, BattleFaction.Hostile),
                LastExploredByNeutral = Map.LastExplored(tile, BattleFaction.Neutral),
                LastExploredByPlayer = Map.LastExplored(tile, BattleFaction.Player),
            };
            for (var part = 0; part < BattleMap.PartCount; part++)
            {
                var (id, set) = Map.Part(tile, (BattleTilePart)part);
                saved = saved.WithPart(part, id, set);
            }
            tiles.Add(saved);
        }
        return tiles.AsReadOnly();
    }

    /// <summary>
    /// SavedBattleGame::load and loadMapResources: a map of the saved size (initMap) with its data sets loaded through
    /// <paramref name="terrain"/> (MapDataSet::loadData with the MCD patches), its tiles, nodes and block grids, and the
    /// root's values. The deployment is derived as the reference's readers derive it (<see cref="DeriveDeploymentId"/>),
    /// with <paramref name="ufoInBattlescape"/> the rule of the campaign's UFO in battle ("" for none); the armor
    /// transformations follow the enviro effects. A value the battle cannot hold, or a restored battle that breaks
    /// <see cref="Validate"/>, is a corrupted save. The lighting and field of view loadMapResources recomputes are Phase 7
    /// branch 4's.
    /// </summary>
    public static BattleState Restore(BattleSnapshot snapshot, RuntimeContent content, BattleTerrainLoader terrain,
        string ufoInBattlescape = "")
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(ufoInBattlescape);
        if (!BattleMap.IsSupportedSize(snapshot.Width, snapshot.Length, snapshot.Height))
            throw new InvalidDataException(
                $"Battle save map size {snapshot.Width}x{snapshot.Length}x{snapshot.Height} ('width', 'length', 'height') is outside the supported range.");
        var rules = content.RuntimeRules;
        if (snapshot.StartingConditionId.Length != 0 && !rules.StartingConditions.TryGet(snapshot.StartingConditionId, out _))
            throw new InvalidDataException($"Battle save names unknown starting condition '{snapshot.StartingConditionId}'.");
        if (snapshot.EnviroEffectsId.Length != 0 && !rules.Battle.EnviroGlobalChances.ContainsKey(snapshot.EnviroEffectsId))
            throw new InvalidDataException($"Battle save names unknown enviro effects '{snapshot.EnviroEffectsId}'.");
        var battle = new BattleState(snapshot.MissionType,
            DeriveDeploymentId(rules, snapshot.MissionType, snapshot.AlienCustomMission, ufoInBattlescape),
            RestoreMap(snapshot, terrain), snapshot.TurnLimit, snapshot.ChronoTrigger, snapshot.CheatTurn)
        {
            BaseModules = [.. snapshot.ModuleMap.Select(static row => row.Select(static module => new BattleBaseModule(module.Remaining,
                module.Total)).ToArray())],
            MapScriptId = snapshot.MapScriptId,
            AlienCustomDeploy = snapshot.AlienCustomDeploy,
            AlienCustomMission = snapshot.AlienCustomMission,
            Depth = snapshot.Depth,
            GlobalShade = snapshot.GlobalShade,
            StartingConditionId = snapshot.StartingConditionId,
        };
        battle.TargetName = snapshot.TargetName;
        battle.CraftOrBaseName = snapshot.CraftOrBaseName;
        battle.EnviroEffectsId = snapshot.EnviroEffectsId;
        battle.ArmorTransformations = rules.Battle.EnviroArmorTransformations.GetValueOrDefault(snapshot.EnviroEffectsId);
        battle.EnvironmentalConditions = (snapshot.EnvironmentalConditionFriendly, snapshot.EnvironmentalConditionHostile,
            snapshot.EnvironmentalConditionNeutral);
        battle.NameDisplay = snapshot.NameDisplay;
        battle.AlienItemLevel = snapshot.AlienItemLevel;
        battle.ReinforcementsDeployment = snapshot.ReinforcementsDeployment;
        battle.ReinforcementsRace = snapshot.ReinforcementsRace;
        battle.ReinforcementsItemLevel = snapshot.ReinforcementsItemLevel;
        foreach (var (wave, count) in snapshot.ReinforcementsMemory) battle._reinforcementsMemory[wave] = count;
        battle.Turn = snapshot.Turn;
        battle.BughuntMinTurn = snapshot.BughuntMinTurn;
        battle.BughuntMode = snapshot.BughuntMode;
        battle.AnimFrame = snapshot.AnimFrame;
        // S3 resolves both against the loaded units (the first player unit standing when the ID names none).
        battle.SelectedUnitId = snapshot.SelectedUnitId == -1 ? null : snapshot.SelectedUnitId;
        battle.UndoUnitId = snapshot.UndoUnitId == -1 ? null : snapshot.UndoUnitId;
        battle.VipEscapeType = snapshot.VipEscapeType;
        battle.VipSurvivalPercentage = snapshot.VipSurvivalPercentage;
        battle.VipsSaved = snapshot.VipsSaved;
        battle.VipsLost = snapshot.VipsLost;
        battle.VipsWaitingOutside = snapshot.VipsWaitingOutside;
        battle.VipsSavedScore = snapshot.VipsSavedScore;
        battle.VipsLostScore = snapshot.VipsLostScore;
        battle.VipsWaitingOutsideScore = snapshot.VipsWaitingOutsideScore;
        battle.ObjectiveType = snapshot.ObjectiveType;
        battle.ObjectivesDestroyed = snapshot.ObjectivesDestroyed;
        battle.ObjectiveCount = snapshot.ObjectivesNeeded;
        battle.TimeUnitsReserved = snapshot.TimeUnitsReserved;
        battle.KneelReserved = snapshot.KneelReserved;
        battle.Ambience = snapshot.Ambience;
        battle.AmbientVolume = snapshot.AmbientVolume;
        battle.AmbienceRandom = Array.AsReadOnly(snapshot.AmbienceRandom.ToArray());
        battle.MinimumAmbienceRandomDelay = snapshot.MinimumAmbienceRandomDelay;
        battle.MaximumAmbienceRandomDelay = snapshot.MaximumAmbienceRandomDelay;
        battle.CurrentAmbienceDelay = snapshot.CurrentAmbienceDelay;
        battle.Music = snapshot.Music;
        battle.BaseStorageItems = Counts(snapshot.BaseItems);
        battle.TogglePersonalLight = snapshot.TogglePersonalLight;
        battle.ToggleNightVision = snapshot.ToggleNightVision;
        battle.ToggleBrightness = snapshot.ToggleBrightness;
        battle.ScriptValues = RestoreScriptValues(content, snapshot.ScriptValues);
        // load: the item counter is the highest item ID plus one, 1 without items (S4 loads the items).
        battle.NextItemId = 1;
        try
        {
            battle.Validate();
        }
        catch (InvalidOperationException failure)
        {
            throw new InvalidDataException($"Battle save is corrupted: {failure.Message}", failure);
        }
        return battle;
    }

    /// <summary>
    /// The map of a battle save: initMap, the tiles' saved state, then loadMapResources, which loads every data set and
    /// points each part at its object. A part whose ID or set is -1 is empty; a set past <c>mapdatasets</c> (an
    /// out-of-range vector index in the reference) or an object past its set (MapDataSet::getObject throws) fails, as does
    /// a data set whose files cannot be loaded.
    /// </summary>
    private static BattleMap RestoreMap(BattleSnapshot snapshot, BattleTerrainLoader terrain)
    {
        var sets = new BattleMapDataSet[snapshot.MapDataSets.Count];
        for (var index = 0; index < sets.Length; index++)
        {
            var name = snapshot.MapDataSets[index];
            if (terrain.TryLoadDataSet(name, out var set) is { } reason)
                throw new InvalidDataException($"Battle save 'mapdatasets' names map data set '{name}', which cannot be loaded: {reason}");
            sets[index] = set!;
        }
        var map = new BattleMap(snapshot.Width, snapshot.Length, snapshot.Height, sets);
        foreach (var tile in snapshot.Tiles)
        {
            if ((uint)tile.Index >= (uint)map.TileCount)
                throw new InvalidDataException(
                    $"Battle save tile index {tile.Index} lies outside the {snapshot.Width}x{snapshot.Length}x{snapshot.Height} map.");
            for (var part = 0; part < BattleMap.PartCount; part++)
            {
                var (id, set) = tile.Part(part);
                if (id == -1 || set == -1)
                {
                    map.SetPart(tile.Index, (BattleTilePart)part, -1, -1);
                    continue;
                }
                if ((uint)set >= (uint)sets.Length)
                    throw new InvalidDataException(
                        $"Battle save tile {tile.Index} part {part} names map data set {set}, past the {sets.Length} of 'mapdatasets'.");
                if ((uint)id >= (uint)sets[set].Objects.Count)
                    throw new InvalidDataException($"Battle save tile {tile.Index} part {part}: MCD {sets[set].Name} has no object {id}.");
                map.SetPart(tile.Index, (BattleTilePart)part, id, set);
            }
            if (tile.Smoke is < 0 or > byte.MaxValue || tile.Fire is < 0 or > byte.MaxValue)
                throw new InvalidDataException($"Battle save tile {tile.Index} has smoke {tile.Smoke} or fire {tile.Fire} outside 0-255.");
            map.SetSmoke(tile.Index, tile.Smoke);
            map.SetFire(tile.Index, tile.Fire);
            map.RestoreDiscovered(tile.Index, BattleTilePart.Floor, tile.FloorDiscovered);
            map.RestoreDiscovered(tile.Index, BattleTilePart.WestWall, tile.WestWallDiscovered);
            map.RestoreDiscovered(tile.Index, BattleTilePart.NorthWall, tile.NorthWallDiscovered);
            map.SetDoorFrame(tile.Index, BattleTilePart.WestWall, tile.WestDoorOpen);
            map.SetDoorFrame(tile.Index, BattleTilePart.NorthWall, tile.NorthDoorOpen);
            map.SetLastExplored(tile.Index, BattleFaction.Hostile, tile.LastExploredByHostile);
            map.SetLastExplored(tile.Index, BattleFaction.Neutral, tile.LastExploredByNeutral);
            map.SetLastExplored(tile.Index, BattleFaction.Player, tile.LastExploredByPlayer);
        }
        foreach (var node in snapshot.Nodes)
            map.AddNode(BattleNode.Restore(node.Id, node.Position, node.Type, node.Rank, node.Flags, node.Reserved, node.Priority,
                node.Allocated, node.Links, node.Dummy));
        map.RestoreBlockGrids(snapshot.FlattenedMapTerrainNames, snapshot.FlattenedMapBlockNames, snapshot.ReinforcementsBlocks);
        return map;
    }

    private static ReadOnlyCollection<IReadOnlyList<T>> Rows<T>(IReadOnlyList<IReadOnlyList<T>> grid) =>
        Array.AsReadOnly(grid.Select(static row => (IReadOnlyList<T>)Array.AsReadOnly(row.ToArray())).ToArray());

    /// <summary>
    /// The battle's alien deployment as DebriefingState derives it (the reference keeps no deployment key): the alien
    /// custom mission's when it names a deployment, else the mission type's, else the deployment of the UFO in battle;
    /// "" when none names one. The other readers differ (ADR 0039): BattlescapeState's next stage reads the mission type's
    /// and then the UFO's with the custom mission standing in for it, and AbortMissionState the mission type's only; the
    /// branches that port them follow their own reader.
    /// </summary>
    internal static string DeriveDeploymentId(RuntimeRuleCatalog rules, string missionType, string alienCustomMission, string ufoInBattlescape)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var deployments = rules.AlienDeployments;
        if (alienCustomMission.Length != 0 && deployments.TryGet(alienCustomMission, out _)) return alienCustomMission;
        if (missionType.Length != 0 && deployments.TryGet(missionType, out _)) return missionType;
        return ufoInBattlescape.Length != 0 && deployments.TryGet(ufoInBattlescape, out _) ? ufoInBattlescape : "";
    }

    /// <summary>A fresh battle's script values: none set.</summary>
    internal static ScriptValueState EmptyScriptValues(RuntimeContent content) => new(content.Tags, ScriptOwnerType(content));

    private static ScriptValueState RestoreScriptValues(RuntimeContent content, IReadOnlyList<ScriptValueEntry> entries)
    {
        if (!ScriptValueState.TryRestore(content.Tags, ScriptOwnerType(content), entries, out var state, out var diagnostics))
            throw new InvalidDataException(string.Join(Environment.NewLine, diagnostics.Select(static item => item.Message)));
        return state!;
    }

    // The script API always declares BattleGame.Tag, so every content has the owner type.
    private static ScriptTagTypeId ScriptOwnerType(RuntimeContent content) =>
        content.Tags.Types.First(static candidate => candidate.Name == BattleSnapshot.ScriptOwner).Id;

    private static ReadOnlyDictionary<string, int> Counts(IEnumerable<KeyValuePair<string, int>> values)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in values) result[key] = value;
        return new ReadOnlyDictionary<string, int>(result);
    }
}
