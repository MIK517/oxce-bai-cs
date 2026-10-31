using Oxce.Engine.Battlescape;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// The host loop's battle hand-over (Phase 7 branch 3 S6b): when the session has a battle, the screens of its phase
/// (<see cref="BattleUiContext.Open"/>) go on the stack above the geoscape, which then covers the clock; and the turn-1
/// save of <c>BattlescapeState::init</c> is requested when the first turn starts.
/// </summary>
public sealed partial class GeoscapeClient
{
    private BattleUiContext? _battle;
    private BattlePhase? _battlePhase;
    private bool _battleShown;
    private (string Target, string CraftOrBase) _briefingNames = ("", "");

    /// <summary>The screen stack the geoscape shares with the base and battle screens.</summary>
    public ScreenNavigator Screens => _screens;

    /// <summary>The battle screens' shared context while a battle is open, for tests and the host.</summary>
    public BattleUiContext? BattleContext => _battle;

    private bool CanHostBattle => _session is { Battle: not null, BattleView: not null, BattleInventory: not null, BattleBriefing: not null };

    /// <summary>
    /// Watches the session's battle: opens its screens once when the stack is free, and requests the turn-1 save when the
    /// phase moves from the pre-battle inventory to the running battle (not for a battle loaded already running).
    /// </summary>
    private void PollBattle()
    {
        if (!CanHostBattle) return;
        var overview = _session.Battle!.QueryBattle(BattleViewer.LocalPlayer);
        if (overview is null)
        {
            _battlePhase = null;
            _battleShown = false;
            _battle = null;
            return;
        }
        var previous = _battlePhase;
        _battlePhase = overview.Phase;
        if (previous == BattlePhase.Inventory && overview.Phase == BattlePhase.InProgress && _setup.SaveRequested is { } requested)
            Guard(() => requested(CampaignSaveKind.BattleTurnOne));
        SyncScreens();
        if (_battleShown || ForeignScreens != 0) return;
        OpenBattle();
    }

    /// <summary>Pushes the screen of the battle's phase. The briefing names are the client's; this client has none yet.</summary>
    private void OpenBattle()
    {
        _battle = new BattleUiContext(_session.Commands, _session.Battle!, _session.BattleView!, _session.BattleInventory!,
            _session.BattleBriefing!, BattleViewer.LocalPlayer, _screens, _setup.Font, _setup.Localize);
        if (!_battle.Open(_briefingNames.Target, _briefingNames.CraftOrBase)) return;
        _battleShown = true;
        _rotateLongitude = _rotateLatitude = 0;
        _dragging = false;
        _time.SetCovered(_session.Player, true);
        _revision++;
    }

    /// <summary>
    /// BriefingState's lines, read from the request before <see cref="StartBattle"/> consumes it: the target is the landing's
    /// destination, the second line the craft that landed (or the base that defends).
    /// </summary>
    private void CaptureBriefingNames()
    {
        _briefingNames = ("", "");
        if (_session.Landings?.QueryDeploymentRequest() is not { } request) return;
        var target = request.Target.Kind == WorldTargetKind.Base && request.CraftTypeId.Length == 0
            ? ""
            : GeoscapeScreens.TargetName(_world, request.Target, _setup.Localize);
        var craft = _world.Crafts.FirstOrDefault(entry => entry.BaseId == request.BaseId && entry.TypeId == request.CraftTypeId &&
            entry.Id == request.CraftId);
        var owner = craft is not null
            ? GeoscapeScreens.CraftName(craft, _setup.Localize)
            : _overview.Bases.FirstOrDefault(entry => entry.Id == request.BaseId)?.Name ?? "";
        _briefingNames = (target, owner);
    }

    private void ForgetBattle()
    {
        _battle = null;
        _battlePhase = null;
        _battleShown = false;
    }
}
