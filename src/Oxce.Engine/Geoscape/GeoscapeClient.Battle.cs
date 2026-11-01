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
    private bool _battleShown;
    private (string Target, string CraftOrBase) _briefingNames = ("", "");

    /// <summary>The screen stack the geoscape shares with the base and battle screens.</summary>
    public ScreenNavigator Screens => _screens;

    /// <summary>The battle screens' shared context while a battle is open, for tests and the host.</summary>
    public BattleUiContext? BattleContext => _battle;

    private bool CanHostBattle => _session is { Battle: not null, BattleView: not null, BattleInventory: not null, BattleBriefing: not null };

    /// <summary>
    /// Watches the session's battle and opens its screens once when the stack is free. The turn-1 save is requested by the
    /// pre-battle inventory screen that starts the first turn (<see cref="BattleUiContext.FirstTurnStarted"/>), as the reference
    /// sets it in <c>NextTurnState::close</c>, so it does not depend on when a tick sees the phase (a battle loaded already
    /// running makes no save).
    /// </summary>
    private void PollBattle()
    {
        if (!CanHostBattle) return;
        var overview = _session.Battle!.QueryBattle(BattleViewer.LocalPlayer);
        if (overview is null)
        {
            _battleShown = false;
            _battle = null;
            return;
        }
        SyncScreens();
        if (_battleShown || ForeignScreens != 0) return;
        OpenBattle();
    }

    /// <summary>Pushes the screen of the battle's phase. The briefing names are the client's; this client has none yet.</summary>
    private void OpenBattle()
    {
        _battle = new BattleUiContext(_session.Commands, _session.Battle!, _session.BattleView!, _session.BattleInventory!,
            _session.BattleBriefing!, BattleViewer.LocalPlayer, _screens, _setup.Font, _setup.Localize);
        _battle.FirstTurnStarted = RequestTurnOneSave;
        if (!_battle.Open(_briefingNames.Target, _briefingNames.CraftOrBase)) return;
        // The lines are the briefing's of this battle only: a battle loaded later is named by its save, not by these.
        _briefingNames = ("", "");
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
        // BriefingState: tr("STR_CRAFT_").arg(craft name), or tr("STR_BASE_UC_").arg(base name) for a base defense.
        var owner = craft is not null
            ? GeoscapeNotifications.Format(_setup.Localize("STR_CRAFT_"), GeoscapeScreens.CraftName(craft, _setup.Localize))
            : _overview.Bases.FirstOrDefault(entry => entry.Id == request.BaseId) is { } home
                ? GeoscapeNotifications.Format(_setup.Localize("STR_BASE_UC_"), home.Name)
                : "";
        _briefingNames = (target, owner);
    }

    private void RequestTurnOneSave()
    {
        if (_setup.SaveRequested is { } requested) Guard(() => requested(CampaignSaveKind.BattleTurnOne));
    }

    private void ForgetBattle()
    {
        _battle = null;
        _battleShown = false;
        _briefingNames = ("", "");
    }
}
