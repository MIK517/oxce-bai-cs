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
public sealed record StartBattle : ICampaignCommand;

/// <summary>The battle a deployment request asked for has started.</summary>
public sealed record BattleStarted(string MissionType, string DeploymentId) : ICampaignEvent;

/// <summary>A unit as a viewer sees it.</summary>
public sealed record BattleUnitView(int Id, BattleFaction Faction, BattlePosition? Position);

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
    IReadOnlyList<BattleUnitView> Units);

/// <summary>The current battle, if any, as a viewer sees it (ADR 0038: every battle query takes a viewer).</summary>
public interface IBattleQuery
{
    BattleOverview? QueryBattle(BattleViewer viewer);
}
