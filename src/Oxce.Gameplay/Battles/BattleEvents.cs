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
/// A shot left the weapon (<c>ProjectileFlyBState::createNewProjectile</c>, after the ammunition was spent): the unit, the weapon, the ammunition item it fired
/// (the weapon itself for a self-fed one), the kind of shot, the voxels the projectile flew over (the last is where it ended) and what it ended in (the
/// reference's <c>VoxelType</c>: -1 empty, 0 floor, 1 west wall, 2 north wall, 3 object, 4 unit, 5 out of the map). The impact follows as the blast or the hit.
/// </summary>
public sealed record UnitFired(int UnitId, int ItemId, int AmmoItemId, BattleAimKind Kind, IReadOnlyList<BattlePosition> Trajectory, int Impact) : ICampaignEvent;

/// <summary>
/// An attack that turned its unit stopped before it fired (<c>ProjectileFlyBState::init</c>'s checks, or the turn running out of time units): the reason is the
/// reference's message key (<c>STR_NOT_ENOUGH_TIME_UNITS</c>, <c>STR_NO_AMMUNITION_LOADED</c>, <c>STR_OUT_OF_RANGE</c>, <c>STR_NO_LINE_OF_FIRE</c>, <c>STR_NO_TRAJECTORY</c>); empty when
/// the reference gives none. An attack that stops before changing anything is a refusal instead. A shot that did fire raises it too after its last event when the action
/// ended with a message (<c>popState</c> shows it whatever the outcome): <c>STR_FAILED_CQB_CHECK</c> after a failed close quarters check on the player's turn, or
/// <c>STR_NO_LINE_OF_FIRE</c> / <c>STR_NO_TRAJECTORY</c> for a later shot of a burst.
/// </summary>
public sealed record UnitAttackStopped(int UnitId, int ItemId, BattleAimKind Kind, string Reason) : ICampaignEvent;

/// <summary>
/// A close quarters combat check failed before a shot (the mod's <c>enableCloseQuartersCombat</c>): the shooter, the neighbour who struck first, the tile the shot is
/// turned to (the shooter's own, or one level up, when the roll said so), the direction the shooter now faces (its turret's for a turret unit; the turn is free) and the time units
/// and energy the neighbour paid.
/// </summary>
public sealed record UnitCloseQuartersFailed(int UnitId, int DefenderId, BattlePosition Target, int Direction, int DefenderTimeUnitsSpent, int DefenderEnergySpent) : ICampaignEvent;

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

/// <summary>
/// A melee hit was struck (<c>ExplosionBState</c> for <c>BA_HIT</c>, after the roll): the unit that struck (-1 for none), its weapon, the unit it struck at (-1 for terrain or
/// nobody), the voxel of the strike, the tile part a terrain attack named (0 for a unit) and whether it hit. Raised before the damage of a hit.
/// </summary>
public sealed record UnitMeleeAttacked(int UnitId, int ItemId, int TargetUnitId, BattlePosition Voxel, int TerrainPart, bool Hit) : ICampaignEvent;
