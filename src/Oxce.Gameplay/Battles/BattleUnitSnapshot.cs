using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Battles;

/// <summary>UnitStatus: the reference's values (<c>src/Mod/Unit.h</c>).</summary>
public enum BattleUnitStatus
{
    Standing = 0,
    Walking = 1,
    Flying = 2,
    Turning = 3,
    Aiming = 4,
    Collapsing = 5,
    Dead = 6,
    Unconscious = 7,
    Panicking = 8,
    Berserk = 9,
    IgnoreMe = 10,
}

/// <summary>
/// One saved unit (<c>SavedBattleGame::load</c>'s unit pass and <c>BattleUnit::load</c> and <c>save</c>, Phase 7 branch 2
/// S3). A nullable value is one the save may leave out, which the load then takes from the BattleUnit constructor (the
/// soldier's or the unit rule's); <see cref="BattleState.Capture"/> fills every one. Per-faction arrays are indexed by
/// <see cref="BattleFaction"/>.
/// </summary>
/// <param name="UnitType"><c>genUnitType</c>: the unit rule, or <c>SOLDIER</c> for a geoscape soldier.</param>
/// <param name="ArmorId"><c>genUnitArmor</c>: the armor the unit wears (a soldier's load takes the soldier's own instead).</param>
public sealed record BattleUnitSnapshot(int Id, string UnitType, string ArmorId, BattleFaction Faction)
{
    /// <summary>BattleUnit's <c>_type</c> for every unit made from a geoscape soldier.</summary>
    public const string SoldierType = "SOLDIER";

    /// <summary>The tag owner of a unit's script values (ScriptValues&lt;BattleUnit&gt;).</summary>
    public const string ScriptOwner = "BattleUnit";

    /// <summary><c>originalFaction</c>, the faction when absent.</summary>
    public BattleFaction OriginalFaction { get; init; }

    public BattleUnitStatus Status { get; init; }
    public bool WantsToSurrender { get; init; }
    public bool IsSurrendering { get; init; }

    /// <summary><c>position</c>: the unit's tile, or where an out unit fell.</summary>
    public BattlePosition Position { get; init; }

    public int Direction { get; init; }
    public int DirectionTurret { get; init; }

    /// <summary><c>tu</c>, <c>health</c>, <c>mana</c>, <c>energy</c> and <c>morale</c> (the constructor's when absent) and <c>stunlevel</c>.</summary>
    public int? TimeUnits { get; init; }
    public int? Health { get; init; }
    public int? Mana { get; init; }
    public int StunLevel { get; init; }
    public int? Energy { get; init; }
    public int? Morale { get; init; }
    public bool Kneeled { get; init; }
    public bool Floating { get; init; }

    /// <summary>
    /// <c>armor</c>: the current armor per UnitSide (SIDE_MAX, 9), as saved. A side the save leaves out keeps the
    /// constructor's value; the four diagonal sides are read and kept but nothing uses them.
    /// </summary>
    public IReadOnlyList<int> Armor { get; init; } = [];

    /// <summary><c>fatalWounds</c> per body part (BODYPART_MAX, 6), 0 where absent.</summary>
    public IReadOnlyList<int> FatalWounds { get; init; } = [0, 0, 0, 0, 0, 0];

    public int Fire { get; init; }

    /// <summary><c>expBravery</c> to <c>expMelee</c>: the experience UnitStats (only those eight fields are saved).</summary>
    public BattleUnitStats Experience { get; init; }

    /// <summary><c>currStats</c>: the saved statistics by key (UnitStats::read reads each key it finds over the constructor's).</summary>
    public IReadOnlyDictionary<string, short> CurrentStats { get; init; } = new Dictionary<string, short>(StringComparer.Ordinal);

    public int TurretType { get; init; } = -1;

    /// <summary><c>visible</c>: whether the player's faction sees the unit.</summary>
    public bool Visible { get; init; }

    /// <summary>
    /// <c>turnsSinceSpotted</c>, <c>turnsSinceSpottedByXcom</c>, <c>turnsSinceSpottedByCivilian</c> and the sniper
    /// counterparts (Uint8 per faction), and <c>turnsSinceStunned</c>.
    /// </summary>
    public IReadOnlyList<int> TurnsSinceSpotted { get; init; } = [255, 255, 255];
    public IReadOnlyList<int> TurnsLeftSpottedForSnipers { get; init; } = [0, 0, 0];
    public int TurnsSinceStunned { get; init; } = 255;

    /// <summary><c>turnsSinceSeenByHostile</c> and the others, per faction.</summary>
    public IReadOnlyList<int> TurnsSinceSeenBy { get; init; } = [255, 255, 255];

    /// <summary><c>tileLastSpottedByHostile</c>, <c>tileLastSpottedForBlindShotByHostile</c> and the others, per faction.</summary>
    public IReadOnlyList<int> TileLastSpottedBy { get; init; } = [-1, -1, -1];
    public IReadOnlyList<int> TileLastSpottedForBlindShotBy { get; init; } = [-1, -1, -1];

    public int? RankInt { get; init; }
    public int? RankIntUnified { get; init; }
    public int MoraleRestored { get; init; }
    public int NotificationShown { get; init; }

    /// <summary><c>killedBy</c> (a UnitFaction value).</summary>
    public int? KilledBy { get; init; }

    public int Kills { get; init; }
    public bool DontReselect { get; init; }
    public bool AiMedikitUsed { get; init; }

    /// <summary>
    /// <c>isBrutal</c>, <c>isNotBrutal</c> and <c>isCheatOnMovement</c>: read by the load and never written; the reference
    /// reads the unit rule's flags instead, and leaves these uninitialized without the keys (the port: false).
    /// </summary>
    public bool IsBrutal { get; init; }
    public bool IsNotBrutal { get; init; }
    public bool IsCheatOnMovement { get; init; }

    /// <summary><c>spawnUnit</c> (a unit rule, "" for none; an unknown one is none), with <c>respawn</c> and <c>spawnUnitFaction</c>.</summary>
    public string SpawnUnit { get; init; } = "";
    public bool Respawn { get; init; }
    public int SpawnUnitFaction { get; init; } = (int)BattleFaction.Hostile;

    public int MotionPoints { get; init; }
    public int CustomMarker { get; init; }
    public bool AlreadyRespawned { get; init; }
    public string ActiveHand { get; init; } = "STR_RIGHT_HAND";
    public string PreferredHandForReactions { get; init; } = "";
    public bool ReactionsDisabledForLeftHand { get; init; }
    public bool ReactionsDisabledForRightHand { get; init; }

    /// <summary><c>tempUnitStatistics</c>.</summary>
    public BattleUnitStatisticsSnapshot Statistics { get; init; } = BattleUnitStatisticsSnapshot.Empty;

    public int MurdererId { get; init; }
    public int FatalShotSide { get; init; }
    public int FatalShotBodyPart { get; init; }
    public string MurdererWeapon { get; init; } = "";
    public string MurdererWeaponAmmo { get; init; } = "";

    /// <summary><c>recolor</c>: (color group shifted by 4, color) pairs; null when absent (the constructor's).</summary>
    public IReadOnlyList<BattleRecolor>? Recolor { get; init; }

    public int MindControllerId { get; init; }
    public bool SummonedPlayerUnit { get; init; }
    public bool ResummonedFakeCivilian { get; init; }
    public bool? PickUpWeaponsMoreActively { get; init; }
    public bool DisableIndicators { get; init; }

    /// <summary>
    /// <c>movementType</c>, and the armor's by depth (the constructor's), which the save compares it with; null when
    /// absent (the constructor's) or, for the original, when the snapshot was read rather than captured.
    /// </summary>
    public int? MovementType { get; init; }
    public int? OriginalMovementType { get; init; }

    /// <summary><c>moveCost</c>'s four kinds as saved (each null when absent), and the armor's (null when read).</summary>
    public BattleMoveCosts MoveCosts { get; init; } = BattleMoveCosts.None;
    public BattleMoveCosts ArmorMoveCosts { get; init; } = BattleMoveCosts.None;

    public bool? Vip { get; init; }
    public bool BannedInNextStage { get; init; }
    public IReadOnlyList<int> MeleeAttackedBy { get; init; } = [];
    public bool? AllowAutoCombat { get; init; }
    public int? Aggression { get; init; }
    public bool HasPanickedLastTurn { get; init; }

    /// <summary><c>AI</c> (AIModule::save), or null for a unit without an AI module.</summary>
    public BattleAiSnapshot? Ai { get; init; }

    /// <summary><c>previousOwner</c>: a unit ID, -1 for none (an ID that names no loaded unit is none).</summary>
    public int PreviousOwnerId { get; init; } = -1;

    /// <summary>The unit's <c>tags</c> (ScriptValues of <c>BattleUnit</c>), non-zero values in tag order.</summary>
    public IReadOnlyList<ScriptValueEntry> ScriptValues { get; init; } = [];

    /// <summary>
    /// Port-only (<c>oxcePortBattle</c>, ADR 0038): who controls the unit and whether the hostile and neutral factions
    /// have spotted it. A reference save gives the player's units the local player and the others the AI, and no spotting.
    /// </summary>
    public BattleController Controller { get; init; } = BattleController.Ai;
    public bool SpottedByHostile { get; init; }
    public bool SpottedByNeutral { get; init; }

    /// <summary>The controller a unit of <paramref name="faction"/> gets when the save names none.</summary>
    public static BattleController DefaultController(BattleFaction faction) =>
        faction == BattleFaction.Player ? BattleController.Human(0) : BattleController.Ai;
}

/// <summary>One <c>recolor</c> entry (Uint8 pair): a color group shifted left by 4, and the color replacing it.</summary>
public readonly record struct BattleRecolor(int Group, int Color);

/// <summary>ArmorMoveCost's four kinds (<c>basePercent</c>, <c>baseFlyPercent</c>, <c>baseClimbPercent</c>, <c>baseNormalPercent</c>).</summary>
public sealed record BattleMoveCosts((int Time, int Energy)? Base, (int Time, int Energy)? Fly, (int Time, int Energy)? Climb,
    (int Time, int Energy)? Normal)
{
    public static BattleMoveCosts None { get; } = new(null, null, null, null);
}

/// <summary>
/// AIModule::save: the node the unit came from and goes to (-1 for none), the AI mode, the units that hit it, whether it
/// picked up a weapon, and the faction it targets (null when the save leaves it out: the constructor's).
/// </summary>
public sealed record BattleAiSnapshot(int FromNode, int ToNode, int Mode, IReadOnlyList<int> WasHitBy, bool WeaponPickedUp)
{
    public int? TargetFaction { get; init; }
}

/// <summary>BattleUnitStatistics (<c>tempUnitStatistics</c>): what the debriefing credits the unit with.</summary>
public sealed record BattleUnitStatisticsSnapshot
{
    public static BattleUnitStatisticsSnapshot Empty { get; } = new();

    public bool WasUnconscious { get; init; }
    public IReadOnlyList<BattleUnitKillSnapshot> Kills { get; init; } = [];
    public int ShotAtCounter { get; init; }
    public int HitCounter { get; init; }
    public int ShotByFriendlyCounter { get; init; }
    public int ShotFriendlyCounter { get; init; }
    public bool LoneSurvivor { get; init; }
    public bool IronMan { get; init; }
    public int LongDistanceHitCounter { get; init; }
    public int LowAccuracyHitCounter { get; init; }
    public int ShotsFiredCounter { get; init; }
    public int ShotsLandedCounter { get; init; }
    public bool NikeCross { get; init; }
    public bool MercyCross { get; init; }
    public int WoundsHealed { get; init; }
    public int AppliedStimulant { get; init; }
    public int AppliedPainKill { get; init; }
    public int RevivedSoldier { get; init; }
    public int RevivedHostile { get; init; }
    public int RevivedNeutral { get; init; }
    public int Martyr { get; init; }
    public int SlaveKills { get; init; }
}

/// <summary>
/// BattleUnitKills: a kill or stun the unit made. <c>name</c> is read only without a <c>type</c>; the defaults are the
/// BattleUnitKills() constructor's.
/// </summary>
public sealed record BattleUnitKillSnapshot
{
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Rank { get; init; } = "";
    public string Race { get; init; } = "";
    public string Weapon { get; init; } = "";
    public string WeaponAmmo { get; init; } = "";
    public int Status { get; init; } = (int)BattleUnitStatus.IgnoreMe;
    public int Faction { get; init; } = (int)BattleFaction.Hostile;
    public int Mission { get; init; }
    public int Turn { get; init; }
    public int Side { get; init; }
    public int BodyPart { get; init; }
    public int Id { get; init; }
}
