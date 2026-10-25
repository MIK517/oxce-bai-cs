using System.Collections.ObjectModel;
using Oxce.Core.Random;
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
    };

    /// <summary>
    /// SavedBattleGame::load for the battle root: an empty map of the saved size (initMap) and the root's values. The
    /// deployment is derived as the reference's readers derive it (<see cref="DeriveDeploymentId"/>), with
    /// <paramref name="ufoInBattlescape"/> the rule of the campaign's UFO in battle ("" for none); the armor
    /// transformations follow the enviro effects. A value the battle cannot hold is a corrupted save.
    /// </summary>
    public static BattleState Restore(BattleSnapshot snapshot, RuntimeContent content, string ufoInBattlescape = "")
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(content);
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
            new BattleMap(snapshot.Width, snapshot.Length, snapshot.Height), snapshot.TurnLimit, snapshot.ChronoTrigger, snapshot.CheatTurn)
        {
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
        return battle;
    }

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
