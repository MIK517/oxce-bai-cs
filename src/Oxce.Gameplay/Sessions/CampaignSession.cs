using Oxce.Gameplay.Campaigns;

namespace Oxce.Gameplay.Sessions;

/// <summary>
/// The host's session layer (ADR 0037): players attach to it, every command carries its sender, unit commands are
/// authorized before the simulation runs, and committed events reach every attached client. Single-player is one
/// session with one local player. The host clock and the screen stack are not part of this type.
/// </summary>
public sealed class CampaignSession
{
    private readonly IActorCommandTarget _commands;
    private readonly object _gate = new();
    private readonly List<Attachment> _attachments = [];
    private readonly List<string> _faults = [];
    private IUnitControllers? _controllers;
    private bool _dispatching;

    public CampaignSession(IActorCommandTarget commands, ICampaignQuery queries)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        Queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    internal ICampaignQuery Queries { get; }

    /// <summary>The battle's unit ownership. Without it every unit command is refused.</summary>
    public IUnitControllers? Controllers
    {
        get { lock (_gate) return _controllers; }
        set { lock (_gate) _controllers = value; }
    }

    /// <summary>Clients detached because their callback threw, in order, for the host to report.</summary>
    public IReadOnlyList<string> Faults
    {
        get { lock (_gate) return _faults.ToArray(); }
    }

    /// <summary>The players currently attached, in attach order.</summary>
    public IReadOnlyList<PlayerId> Players
    {
        get { lock (_gate) return _attachments.Select(static a => a.Player).ToArray(); }
    }

    /// <summary>
    /// Attaches a player. The handle is that player's command target and query view; disposing it detaches the player.
    /// A player attaches once at a time.
    /// </summary>
    public SessionHandle Attach(PlayerId player, ISessionClient? client = null)
    {
        lock (_gate)
        {
            if (_attachments.Exists(a => a.Player == player))
                throw new InvalidOperationException($"{player} is already attached.");
            var attachment = new Attachment(player, client);
            _attachments.Add(attachment);
            return new SessionHandle(this, attachment);
        }
    }

    internal CampaignCommandResult Execute(Attachment sender, ICampaignCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            if (_dispatching)
                throw new InvalidOperationException("Commands cannot be submitted from a session event callback.");
            var actor = sender.Player;
            if (!_attachments.Contains(sender)) return Refuse(SessionReasons.NotAttached(actor));
            if (command is IUnitCommand unit && Authorize(actor, unit) is { } refusal) return Refuse(refusal);
            var result = _commands.Execute(actor, command);
            Broadcast(new SessionEvents(actor, command, result.Events));
            return result;
        }
    }

    internal bool IsAttached(Attachment attachment)
    {
        lock (_gate) return _attachments.Contains(attachment);
    }

    internal void Detach(Attachment attachment)
    {
        lock (_gate) _attachments.Remove(attachment);
    }

    private static CampaignCommandResult Refuse(CampaignReason reason) => new([CampaignActionBlocked.From(reason)]);

    private CampaignReason? Authorize(PlayerId actor, IUnitCommand command)
    {
        if (_controllers is null) return SessionReasons.NoUnits();
        return _controllers.ControllerOf(command.UnitId) is { } controller
            ? controller == actor ? null : SessionReasons.NotControlled(actor, command.UnitId)
            : SessionReasons.Unknown(command.UnitId);
    }

    private void Broadcast(SessionEvents batch)
    {
        // A refusal changed nothing, so it is the sender's answer only (ADR 0037 rule 3).
        if (batch.Events.All(static e => e is CampaignActionBlocked)) return;
        _dispatching = true;
        try
        {
            foreach (var attachment in _attachments.ToArray())
            {
                if (attachment.Client is not { } client) continue;
                try { client.OnEvents(batch); }
                catch (Exception exception)
                {
                    // A broken observer must not undo a committed command or starve the other clients.
                    _attachments.Remove(attachment);
                    _faults.Add($"{attachment.Player} detached: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }
        finally { _dispatching = false; }
    }

    internal sealed class Attachment(PlayerId player, ISessionClient? client)
    {
        public PlayerId Player { get; } = player;
        public ISessionClient? Client { get; } = client;
    }
}

/// <summary>
/// One player's view of the session: commands are sent as that player and queries are read for that player. A
/// handle whose player was detached refuses every command.
/// </summary>
public sealed class SessionHandle : ICampaignCommandTarget, ICampaignQuery, IDisposable
{
    private readonly CampaignSession _session;
    private readonly CampaignSession.Attachment _attachment;

    internal SessionHandle(CampaignSession session, CampaignSession.Attachment attachment)
    {
        _session = session;
        _attachment = attachment;
    }

    public PlayerId Player => _attachment.Player;

    public bool IsAttached => _session.IsAttached(_attachment);

    public CampaignCommandResult Execute(ICampaignCommand command) => _session.Execute(_attachment, command);

    public CampaignOverview QueryOverview() => _session.Queries.QueryOverview();

    public CampaignStores QueryStores(int baseId) => _session.Queries.QueryStores(baseId);

    public void Dispose() => _session.Detach(_attachment);
}
