using Oxce.Gameplay.Campaigns;

namespace Oxce.Gameplay.Battles;

/// <summary>UnitFaction: the side a battle unit fights for. The values are the reference's.</summary>
public enum BattleFaction
{
    Player = 0,
    Hostile = 1,
    Neutral = 2,
}

public enum BattleControllerKind
{
    Ai,
    Player,
}

/// <summary>Who decides a unit's actions (ADR 0038): the AI, or the human in a player slot.</summary>
public readonly record struct BattleController(BattleControllerKind Kind, int PlayerSlot)
{
    public static BattleController Ai { get; } = new(BattleControllerKind.Ai, -1);

    public static BattleController Human(int playerSlot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(playerSlot);
        return new(BattleControllerKind.Player, playerSlot);
    }
}

/// <summary>
/// Whose view a battle query answers for (ADR 0038): a faction, and the player slot within it. A viewer sees
/// what its faction has discovered and spotted.
/// </summary>
public readonly record struct BattleViewer(BattleFaction Faction, int PlayerSlot = 0)
{
    /// <summary>The single local player of a single-player battle.</summary>
    public static BattleViewer LocalPlayer { get; } = new(BattleFaction.Player);
}

/// <summary>A tile position: x along the width, y along the length, z the level (0 is the ground level).</summary>
public readonly record struct BattlePosition(int X, int Y, int Z);

/// <summary>
/// Starts the battle the pending deployment request asks for (<c>ConfirmLandingState::btnYesClick</c>,
/// <c>BaseDefenseState</c> and <c>ConfirmCydoniaState</c> running <c>BattlescapeGenerator</c>). The request is
/// consumed only when the battle is generated.
/// </summary>
public sealed record StartBattle(bool Preview = false) : ICampaignCommand;

/// <summary>The battle a deployment request asked for has started.</summary>
public sealed record BattleStarted(string MissionType, string DeploymentId) : ICampaignEvent;

/// <summary>
/// Where a battle stands before and in play: the briefing (<c>BriefingState</c>), the pre-battle inventory
/// (<c>InventoryState(false)</c>) and then the turns. A restored battle that is past its first turn is in progress.
/// </summary>
public enum BattlePhase
{
    Briefing,
    Inventory,
    InProgress,
}

/// <summary>A command of the running battle: it is not held back by the battle itself, as the campaign's commands are.</summary>
public interface IBattleCommand : ICampaignCommand;

/// <summary>
/// <c>BriefingState</c>'s constructor: the mission target and the craft or base line (the already localized
/// <c>strTarget</c> and <c>strCraftOrBase</c> lines, which the client composes with its language). The target is the
/// craft's destination name, or empty when it has none; a mod's random operation name replaces it.
/// </summary>
public sealed record BeginBriefing(string TargetName, string CraftOrBaseName) : IBattleCommand;

/// <summary>
/// <c>BriefingState::btnOkClick</c> with <c>NextTurnState</c>'s turn-0 work: spawns the units and items of primed spawn
/// items, tallies the units and checks bug hunt mode; the pre-battle inventory follows.
/// </summary>
public sealed record ConfirmBriefing : IBattleCommand;

/// <summary>
/// <c>SavedBattleGame::startFirstTurn</c>, run when the pre-battle inventory closes: the first turn of the X-COM side.
/// </summary>
public sealed record StartFirstTurn : IBattleCommand;

/// <summary>The briefing named the mission target and the craft or base.</summary>
public sealed record BriefingOpened(string TargetName, string CraftOrBaseName) : ICampaignEvent;

/// <summary>The briefing was confirmed; primed spawn items created units and items.</summary>
public sealed record BriefingConfirmed(int SpawnedUnits, int SpawnedItems) : ICampaignEvent;

/// <summary>The first turn began (turn 1, the X-COM side).</summary>
public sealed record BattleFirstTurnStarted(int SelectedUnitId) : ICampaignEvent
{
    /// <summary>The soldiers whose equipment layout the closing inventory saved (<c>InventoryState::saveEquipmentLayout</c>).</summary>
    public int LayoutsSaved { get; init; }
}

/// <summary>
/// What the briefing screen shows (<c>BriefingState</c>): title and description keys, music, background and cutscene, the
/// palette and text offset, whether the target and the craft lines show, and the lines the briefing set.
/// </summary>
public sealed record BattleBriefing(
    string Title,
    string Description,
    string Music,
    string Background,
    string Cutscene,
    int Palette,
    int TextOffset,
    bool ShowTarget,
    bool ShowCraft,
    bool OperationName,
    string TargetName,
    string CraftOrBaseName,
    BattlePhase Phase);

/// <summary>The briefing of the current battle, or null while there is none or it is in progress.</summary>
public interface IBattleBriefingQuery
{
    BattleBriefing? QueryBriefing();
}

/// <summary>A unit as a viewer sees it.</summary>
public sealed record BattleUnitView(int Id, BattleFaction Faction, BattlePosition? Position)
{
    /// <summary>The player slot that controls the unit (ADR 0038), or -1 for the AI.</summary>
    public int ControllerSlot { get; init; } = -1;
}

/// <summary>The battle as a viewer sees it; <see cref="Revision"/> changes with every committed battle change.</summary>
public sealed record BattleOverview(
    string MissionType,
    string DeploymentId,
    int Width,
    int Length,
    int Height,
    int Turn,
    BattleFaction Side,
    int TurnLimit,
    long Revision,
    IReadOnlyList<BattleUnitView> Units)
{
    public BattlePhase Phase { get; init; }

    /// <summary>What the restore noticed and kept going (a fifth special built-in item the load leaves in no slot).</summary>
    public IReadOnlyList<string> RestoreNotes { get; init; } = [];
}

/// <summary>The current battle, if any, as a viewer sees it (ADR 0038: every battle query takes a viewer).</summary>
public interface IBattleQuery
{
    BattleOverview? QueryBattle(BattleViewer viewer);
}
