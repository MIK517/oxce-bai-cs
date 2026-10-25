using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The rest of a unit's saved state (BattleUnit's members that <c>load</c> reads and <c>save</c> writes, Phase 7 branch 2
/// S3): the constructor's values until a later branch changes them. Per-faction arrays are indexed by
/// <see cref="BattleFaction"/>.
/// </summary>
internal sealed partial class BattleUnit
{
    /// <summary>UnitSide's SIDE_MAX and UnitBodyPart's BODYPART_MAX.</summary>
    public const int ArmorSides = 9;
    public const int BodyParts = 6;

    /// <summary>BattleUnit::getOriginalFaction: the faction the unit was created for (mind control changes the faction only).</summary>
    public BattleFaction OriginalFaction { get; set; }

    public BattleUnitStatus Status { get; set; }

    /// <summary>BattleUnit::isOut: dead, unconscious or removed from the game.</summary>
    public bool IsOut => Status is BattleUnitStatus.Dead or BattleUnitStatus.Unconscious or BattleUnitStatus.IgnoreMe;

    public bool WantsToSurrender { get; set; }
    public bool IsSurrendering { get; set; }

    /// <summary>BattleUnit::getPosition while the unit stands on no tile (an out unit keeps where it fell; else (0, 0, 0)).</summary>
    public BattlePosition SavedPosition { get; set; }

    public int DirectionTurret { get; set; }
    public bool Kneeled { get; set; }
    public bool Floating { get; set; }

    /// <summary>BattleUnit's <c>_currentArmor</c> per UnitSide; the four diagonal sides are kept as saved and 0 otherwise.</summary>
    public int[] CurrentArmor { get; } = new int[ArmorSides];

    public int[] FatalWounds { get; } = new int[BodyParts];
    public int Fire { get; set; }

    /// <summary>BattleUnit::getExpStats (the eight saved fields).</summary>
    public BattleUnitStats Experience { get; set; }

    public byte[] TurnsSinceSpotted { get; } = [255, 255, 255];
    public byte[] TurnsLeftSpottedForSnipers { get; } = [0, 0, 0];
    public byte TurnsSinceStunned { get; set; } = 255;
    public int[] TurnsSinceSeenBy { get; } = [255, 255, 255];
    public int[] TileLastSpottedBy { get; } = [-1, -1, -1];
    public int[] TileLastSpottedForBlindShotBy { get; } = [-1, -1, -1];

    /// <summary>BattleUnit::getRankIntUnified: the rank on the unified scale (a soldier's rank, an alien rank string's index).</summary>
    public int RankIntUnified { get; set; }

    public int MoraleRestored { get; set; }
    public int NotificationShown { get; set; }

    /// <summary>BattleUnit::killedBy (a UnitFaction value): the constructor's faction.</summary>
    public int KilledBy { get; set; }

    public int Kills { get; set; }
    public bool DontReselect { get; set; }
    public bool AiMedikitUsed { get; set; }

    /// <summary>The saved <c>isBrutal</c>, <c>isNotBrutal</c> and <c>isCheatOnMovement</c> (load-only, see the snapshot).</summary>
    public bool IsBrutal { get; set; }
    public bool IsNotBrutal { get; set; }
    public bool IsCheatOnMovement { get; set; }

    /// <summary>BattleUnit::getSpawnUnit ("" for none), isRespawn and getSpawnUnitFaction.</summary>
    public string SpawnUnit { get; set; } = "";
    public bool Respawn { get; set; }
    public int SpawnUnitFaction { get; set; } = (int)BattleFaction.Hostile;

    public int MotionPoints { get; set; }
    public int CustomMarker { get; set; }
    public bool AlreadyRespawned { get; set; }
    public string ActiveHand { get; set; } = "STR_RIGHT_HAND";
    public string PreferredHandForReactions { get; set; } = "";
    public bool ReactionsDisabledForLeftHand { get; set; }
    public bool ReactionsDisabledForRightHand { get; set; }

    /// <summary>BattleUnit::getStatistics.</summary>
    public BattleUnitStatisticsSnapshot Statistics { get; set; } = BattleUnitStatisticsSnapshot.Empty;

    public int MurdererId { get; set; }
    public int FatalShotSide { get; set; }
    public int FatalShotBodyPart { get; set; }
    public string MurdererWeapon { get; set; } = "";
    public string MurdererWeaponAmmo { get; set; } = "";

    /// <summary>BattleUnit::getRecolor.</summary>
    public List<BattleRecolor> Recolor { get; } = [];

    public int MindControllerId { get; set; }
    public bool ResummonedFakeCivilian { get; set; }
    public bool PickUpWeaponsMoreActively { get; set; }
    public bool DisableIndicators { get; set; }

    /// <summary>BattleUnit's <c>_originalMovementType</c>: the armor's by depth, which the save compares the movement type with.</summary>
    public int OriginalMovementType { get; set; }

    /// <summary>BattleUnit's move cost bases, and the armor's (the save writes those that differ).</summary>
    public RuntimeArmorMoveCosts MoveCosts { get; set; } = RuntimeArmorMoveCosts.Default;
    public RuntimeArmorMoveCosts ArmorMoveCosts { get; set; } = RuntimeArmorMoveCosts.Default;

    public bool BannedInNextStage { get; set; }
    public List<int> MeleeAttackedBy { get; } = [];
    public bool AllowAutoCombat { get; set; } = true;
    public int Aggression { get; set; }
    public bool HasPanickedLastTurn { get; set; }

    /// <summary>BattleUnit::getAIModule, or null.</summary>
    public BattleAiState? Ai { get; set; }

    /// <summary>
    /// AIModule::setStartNode, which sets the AI's <c>_fromNode</c>: the node the unit spawned on, or null (also without
    /// an AI module).
    /// </summary>
    public int? StartNodeId
    {
        get => Ai is { FromNode: >= 0 } ai ? ai.FromNode : null;
        set => (Ai ?? throw new InvalidOperationException($"Battle unit {Id} has no AI module.")).FromNode = value ?? -1;
    }

    /// <summary>BattleUnit::getPreviousOwner's ID, or null.</summary>
    public int? PreviousOwnerId { get; set; }

    /// <summary>The unit's script values (owner <c>BattleUnit</c>); null for a battle built without content.</summary>
    public ScriptValueState? ScriptValues { get; set; }
}

/// <summary>
/// AIModule's saved state: the node the unit came from and is going to (-1 for none), the AI mode (AI_PATROL 0), the
/// units that hit it, whether it picked up a weapon, and the faction it targets.
/// </summary>
internal sealed class BattleAiState
{
    /// <summary>AIModule::AIModule: the player as target, the hostile faction for a neutral or player unit.</summary>
    public BattleAiState(BattleFaction originalFaction, int fromNode = -1)
    {
        FromNode = fromNode;
        TargetFaction = originalFaction is BattleFaction.Neutral or BattleFaction.Player ? (int)BattleFaction.Hostile : (int)BattleFaction.Player;
    }

    public int FromNode { get; set; }
    public int ToNode { get; set; } = -1;
    public int Mode { get; set; }
    public List<int> WasHitBy { get; } = [];
    public bool WeaponPickedUp { get; set; }
    public int TargetFaction { get; set; }
}
