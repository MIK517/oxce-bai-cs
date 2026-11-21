namespace Oxce.Gameplay.Battles;

/// <summary>BattleActionOrigin: where along the shooter's tile the weapon's barrel is taken from (the centre, or a third to the left or right).</summary>
internal enum BattleActionOrigin
{
    Centre = 0,
    Left = 1,
    Right = 2,
}

/// <summary>
/// The parts of <c>BattleAction</c> the line of fire and accuracy code reads (Phase 7 branch 5 S2): the actor, the action type, the weapon, the target
/// tile, the barrel's origin, the shot counter, spray targeting and waypoints, the stored result, and the two flags the reference takes from the
/// battlescape (<c>isCtrlPressed(true)</c> with <c>Options::forceFire</c> on, and <c>getPanicHandled()</c>).
/// </summary>
internal sealed class BattleAimAction(BattleUnit actor, BattleActionType type, BattleItem weapon, BattlePosition target)
{
    public BattleUnit Actor { get; } = actor;
    public BattleActionType Type { get; set; } = type;
    public BattleItem Weapon { get; } = weapon;
    public BattlePosition Target { get; set; } = target;
    public BattleActionOrigin RelativeOrigin { get; set; } = BattleActionOrigin.Centre;

    /// <summary>BattleAction::autoShotCounter: 1 for the first shot of a burst (the reference counts it up in <c>createNewProjectile</c> before the trajectory).</summary>
    public int AutoShotCounter { get; set; }

    public bool SprayTargeting { get; set; }
    public List<BattlePosition> Waypoints { get; } = [];

    /// <summary>BattleAction::result: the reason the action stopped (empty while none).</summary>
    public string Result { get; set; } = "";

    /// <summary>The force fire flag: <c>Options::forceFire</c> (on) with Ctrl held, as the command carries it.</summary>
    public bool ForceFire { get; set; }

    /// <summary><c>BattlescapeGame::getPanicHandled</c>: false while a berserk or panicked unit acts (its shots are less accurate and aim at the tile's centre).</summary>
    public bool PanicHandled { get; set; } = true;
}
