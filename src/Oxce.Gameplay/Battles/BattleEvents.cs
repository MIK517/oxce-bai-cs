using Oxce.Gameplay.Campaigns;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// A blast the explosion state set off (<c>ExplosionBState</c>): where its center is (a voxel), its power and radius, and whether a terrain
/// explosion (<c>Tile::getExplosive</c>) started it. Raised when the blast goes off, before its damage.
/// </summary>
public sealed record BattleExploded(BattlePosition Center, int Power, int Radius, bool Terrain) : ICampaignEvent;

/// <summary>
/// A unit took a hit (<c>TileEngine::hitUnit</c>): who, by whom (-1 for no attacker), the health it lost and what it has left. Raised after the
/// damage, so a hit that kills shows its health at or under 0 and is followed by the death.
/// </summary>
public sealed record UnitHit(int UnitId, int AttackerId, int HealthLost, int Health) : ICampaignEvent;

/// <summary>A unit died (<c>checkForCasualties</c>'s death state): its murderer's ID, or -1 when there is none (bleeding, fire, a terrain blast).</summary>
public sealed record UnitDied(int UnitId, int KillerId) : ICampaignEvent;

/// <summary>A unit's stun reached its health and it fell unconscious.</summary>
public sealed record UnitKnockedOut(int UnitId, int KillerId) : ICampaignEvent;

/// <summary>A unit turned into another (<c>convertUnit</c>): the one that died and the one that stands where it was.</summary>
public sealed record UnitConverted(int UnitId, int NewUnitId) : ICampaignEvent;

/// <summary>A tile caught fire (<c>Tile::ignite</c>): where, and for how many turns it burns.</summary>
public sealed record TileIgnited(BattlePosition Position, int Turns) : ICampaignEvent;

/// <summary>A unit started burning (<c>BattleUnit::damage</c> set its fire): the turns it burns for.</summary>
public sealed record UnitIgnited(int UnitId, int Turns) : ICampaignEvent;

/// <summary>
/// The battle's ordered event stream. A rule raises its event where the outcome happens, in the order the reference reaches it; a command empties the
/// sink when it starts and returns what it raised. Every state has a sink of its own: what a trial copy raises reaches nobody, and what is raised outside
/// a runtime command (the generator's explosions, the battlescape's opening falls) stays in the sink until the next command empties it, so no
/// command returns it. Where a handler's own event must follow the events of the rules it runs, it raises it through the sink.
/// </summary>
internal sealed class BattleEventSink
{
    private readonly List<ICampaignEvent> _events = [];

    /// <summary>Raises <paramref name="battleEvent"/> after what was raised before it.</summary>
    public void Add(ICampaignEvent battleEvent)
    {
        ArgumentNullException.ThrowIfNull(battleEvent);
        _events.Add(battleEvent);
    }

    public int Count => _events.Count;

    /// <summary>The events raised so far, in order.</summary>
    public IReadOnlyList<ICampaignEvent> All => _events;

    /// <summary>The raised events of one type, in order.</summary>
    public IReadOnlyList<T> Of<T>() where T : ICampaignEvent => [.. _events.OfType<T>()];

    /// <summary>Forgets what was raised (a command starts with an empty sink).</summary>
    public void Clear() => _events.Clear();

    /// <summary>Takes the raised events and empties the sink.</summary>
    public IReadOnlyList<ICampaignEvent> Drain()
    {
        var taken = _events.ToArray();
        _events.Clear();
        return taken;
    }
}
