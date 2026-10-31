using Oxce.Gameplay.Campaigns;

namespace Oxce.Gameplay.Sessions;

/// <summary>
/// A player of the host's session (ADR 0037). Single-player has one: <see cref="Local"/>. The value is data, so it
/// can be sent over a wire later.
/// </summary>
public readonly record struct PlayerId(int Value)
{
    /// <summary>The one player of a single-player session.</summary>
    public static PlayerId Local => new(0);

    /// <summary>The host itself: the clock's time advances and extension commands.</summary>
    public static PlayerId Host => new(-1);

    public override string ToString() => Value == -1 ? "the host" : $"player {Value}";
}

/// <summary>A command target that also learns who sent the command.</summary>
public interface IActorCommandTarget : ICampaignCommandTarget
{
    CampaignCommandResult Execute(PlayerId actor, ICampaignCommand command);
}

/// <summary>
/// A layer under the session that originates commands of its own (extensions). The session hands it a route so
/// those commands are executed and broadcast through the session instead of bypassing it.
/// </summary>
public interface IHostRoutedCommands
{
    ICampaignCommandTarget? HostRoute { get; set; }
}

/// <summary>
/// A command that addresses one battle unit by its stable ID. The session checks that the sender controls the unit
/// before the simulation sees the command; rules never check ownership (Phase 7 plan D2).
/// </summary>
public interface IUnitCommand : ICampaignCommand
{
    int UnitId { get; }
}

/// <summary>Which player controls a unit. The battle supplies it; the session only asks.</summary>
public interface IUnitControllers
{
    /// <summary>The controlling player, or null when the unit does not exist or no player controls it (AI units).</summary>
    PlayerId? ControllerOf(int unitId);
}

/// <summary>
/// Receives every event the session commits, whoever caused it (ADR 0037 rule 3). Calls come in commit order, on
/// the thread that executed the command, and must not submit commands.
/// </summary>
public interface ISessionClient
{
    void OnEvents(SessionEvents batch);
}

/// <summary>The events of one committed command and the player who sent it.</summary>
public sealed record SessionEvents(PlayerId Actor, ICampaignCommand Command, IReadOnlyList<ICampaignEvent> Events);

/// <summary>Classified refusals the session itself produces.</summary>
public static class SessionReasons
{
    public const string PlayerNotAttached = "session.player-not-attached";
    public const string UnitNotControlled = "session.unit-not-controlled";
    public const string UnitUnknown = "session.unit-unknown";
    public const string UnitCommandsUnavailable = "session.unit-commands-unavailable";

    public static CampaignReason NotAttached(PlayerId player) =>
        new(PlayerNotAttached, $"{player} is not attached to the session.");

    public static CampaignReason NotControlled(PlayerId player, int unitId) =>
        new(UnitNotControlled, $"{player} does not control unit {unitId}.");

    public static CampaignReason Unknown(int unitId) => new(UnitUnknown, $"Unit {unitId} does not exist.");

    public static CampaignReason NoUnits() =>
        new(UnitCommandsUnavailable, "The session has no battle whose units can be commanded.");
}
