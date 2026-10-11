using System.Globalization;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// The craft and target screens: GeoscapeCraftState, InterceptState, SelectDestinationState,
/// MultipleTargetsState, ConfirmDestinationState and TargetInfoState. Screens are popups pushed
/// above the queued notifications; screens opened from one another share a group so a successful
/// launch closes them together, as the reference pops its state stack.
/// </summary>
public sealed partial class GeoscapeClient
{
    private const uint ShiftLeft = 0x400000E1;
    private const uint ShiftRight = 0x400000E5;
    private const int InterceptTop = SpeedTop + 6 * SpeedHeight + 40;
    private const int CancelTop = SpeedTop;

    private sealed record SelectionState(IReadOnlyList<CraftKey> Wing, int Group);

    private sealed record ConfirmState(IReadOnlyList<CraftKey> Wing, GeoscapeDestination Destination, bool FollowLeader, int Group);

    private sealed record InterceptState(List<CraftKey> Wing, WorldTargetReference? Target, int? BaseId, int Page, int Group);

    private readonly record struct PopupEntry(GeoscapePopup Popup, Action<int>? Chosen, int Group);

    private bool _shift;
    private int _lastGroup;
    private SelectionState? _selecting;
    private ConfirmState? _confirm;
    private InterceptState? _intercept;

    /// <summary>The wing choosing a destination on the globe (SelectDestinationState), if any.</summary>
    public IReadOnlyList<CraftKey>? SelectingWing => _selecting?.Wing;

    /// <summary>
    /// A popup takes the input: while a destination is being chosen only that choice's own screens
    /// show; queued notifications wait, as the reference shows them only when the geoscape is on top.
    /// </summary>
    private bool PopupShown => _popups.Count != 0 && (_selecting is null || _popups[0].Group == _selecting.Group);

    private int NewGroup() => ++_lastGroup;

    /// <summary>Pushes a screen above everything else.</summary>
    private void Push(GeoscapePopup popup, Action<int>? chosen = null, int group = 0)
    {
        _popups.Insert(0, new(popup, chosen, group));
        _rotateLongitude = _rotateLatitude = 0;
        _dragging = false;
        _time.Interrupt();
    }

    /// <summary>MultipleTargetsState::popupTarget without a wing: the screen for one clicked target.</summary>
    private void OpenTarget(GlobeTarget target)
    {
        var group = NewGroup();
        switch (target.Kind)
        {
            case WorldTargetKind.Base:
                OpenIntercept(new InterceptIntent(null, target.Reference.Id), group);
                break;
            case WorldTargetKind.Craft when target.Craft is { } craft:
                Push(GeoscapeScreens.Craft(craft, _world, _setup.Rules, _setup.Localize), group: group);
                break;
            case WorldTargetKind.Ufo when target.Target is { } ufo:
                // UfoDetectedState without the detection sound or text.
                Push(new GeoscapePopup(ufo.Label, Details(ufo),
                [
                    new(_setup.Localize("STR_INTERCEPT"), true, new WorldPosition(ufo.Longitude, ufo.Latitude))
                    {
                        Intent = new InterceptIntent(ufo.Reference),
                    },
                    new(_setup.Localize("STR_CENTER_ON_UFO_TIME_5_SECONDS"), true, new WorldPosition(ufo.Longitude, ufo.Latitude)),
                    new(_setup.Localize("STR_CANCEL_UC"), CancelKey: true),
                ]), group: group);
                break;
            case WorldTargetKind.Waypoint or WorldTargetKind.MissionSite or WorldTargetKind.AlienBase when target.Target is { } info:
                Push(GeoscapeScreens.TargetInfo(info, _setup.Localize), group: group);
                break;
        }
    }

    private List<string> Details(CampaignWorldTarget target)
    {
        var lines = new List<string>();
        if (target.Status.Length != 0) lines.Add(target.Status);
        if (target.Altitude.Length != 0) lines.Add(_setup.Localize(target.Altitude));
        if (target.UnavailableActionReason is { } reason) lines.Add(reason);
        return lines;
    }

    /// <summary>Opens the screen a button leads to.</summary>
    private void Open(GeoscapeIntent intent, PopupEntry source, bool shift)
    {
        var group = source.Group != 0 ? source.Group : NewGroup();
        switch (intent)
        {
            case InterceptIntent intercept:
                // UfoDetectedState, MissionDetectedState and TargetInfoState stay below InterceptState.
                if (source.Popup.Actions.Any(action => action.Intent is InterceptIntent))
                    _popups.Insert(0, source with { Group = group });
                OpenIntercept(intercept, group);
                break;
            case InterceptRowIntent row when _intercept is { } state:
                ChooseInterceptRow(state, row.Craft, shift);
                break;
            case InterceptPageIntent page when _intercept is { } state:
                ShowIntercept(state with { Page = page.Page });
                break;
            case CraftIntent craft when FindCraft(craft.Craft) is { } found:
                Push(GeoscapeScreens.Craft(found, _world, _setup.Rules, _setup.Localize), group: group);
                break;
            case SelectDestinationIntent select:
                BeginSelection(select.Wing, group);
                break;
            case FollowLeaderIntent when _confirm is { } confirm:
                ShowConfirm(confirm with { FollowLeader = !confirm.FollowLeader });
                break;
            case BaseIntent owner:
                OpenBases(owner.BaseId);
                break;
            case TargetInfoIntent info when _targets.Find(info.Target)?.Target is { } target:
                Push(GeoscapeScreens.TargetInfo(target, _setup.Localize), group: group);
                break;
        }
    }

    private void OpenIntercept(InterceptIntent intent, int group) =>
        ShowIntercept(new InterceptState([], intent.Target, intent.BaseId, 0, group));

    private void ShowIntercept(InterceptState state)
    {
        _intercept = state;
        Push(GeoscapeScreens.Intercept(_world, state.Wing, state.Target, state.Page, _setup.Localize, state.BaseId),
            group: state.Group);
    }

    /// <summary>InterceptState::lstCraftsLeftClick.</summary>
    private void ChooseInterceptRow(InterceptState state, CraftKey key, bool shift)
    {
        var craft = FindCraft(key);
        var canStart = craft is not null && GeoscapeScreens.CanStart(craft);
        if (shift)
        {
            if (!state.Wing.Remove(key) && canStart && state.Wing.Count < GeoscapeScreens.MaximumWingSelection)
                state.Wing.Add(key);
            ShowIntercept(state);
            return;
        }
        if (!canStart)
        {
            ShowIntercept(state);
            return;
        }
        _intercept = null;
        var wing = new List<CraftKey> { key };
        wing.AddRange(state.Wing.Where(member => member != key));
        if (state.Target is null) BeginSelection(wing, state.Group);
        else
        {
            var target = _targets.Find(state.Target);
            ShowConfirm(new ConfirmState(wing, new GeoscapeDestination(state.Target, null,
                target?.Label ?? GeoscapeScreens.TargetName(_world, state.Target, _setup.Localize)),
                GeoscapeScreens.DefaultFollowLeader(_world, state.Target), state.Group));
        }
    }

    private void ShowConfirm(ConfirmState state)
    {
        _confirm = state;
        Push(GeoscapeScreens.Confirm(_world, state.Wing, state.Destination, state.FollowLeader, _setup.Localize),
            group: state.Group);
    }

    private void BeginSelection(IReadOnlyList<CraftKey> wing, int group)
    {
        _selecting = new SelectionState(wing, group);
        _rotateLongitude = _rotateLatitude = 0;
        _dragging = false;
        _time.Interrupt();
        _globeDirty = true;
    }

    private void CancelSelection()
    {
        if (_selecting is null) return;
        CloseGroup(_selecting.Group);
        _globeDirty = true;
    }

    private void HandleSelectionKey(uint key)
    {
        switch (key)
        {
            case 27:
                CancelSelection();
                break;
            case '+' or '=' or 0x40000057:
                View.ZoomIn();
                _globeDirty = true;
                break;
            case '-' or 0x40000056:
                View.ZoomOut();
                _globeDirty = true;
                break;
        }
    }

    /// <summary>SelectDestinationState::globeClick: the clicked targets, or a new waypoint there.</summary>
    private void SelectDestinationAt(int x, int y, double longitude, double latitude)
    {
        var selection = _selecting!;
        var leader = selection.Wing[0];
        var near = _targets.Near(View, x, y,
            new WorldTargetReference(WorldTargetKind.Craft, leader.CraftTypeId, leader.CraftId, 0, 0));
        var follow = true;
        if (near.Count == 0)
        {
            ShowConfirm(new ConfirmState(selection.Wing, new GeoscapeDestination(null, new WorldPosition(longitude, latitude),
                _setup.Localize("STR_WAY_POINT")), follow, selection.Group));
            return;
        }
        void Confirm(GlobeTarget target) => ShowConfirm(new ConfirmState(selection.Wing,
            new GeoscapeDestination(target.Reference, null, target.Label),
            GeoscapeScreens.DefaultFollowLeader(_world, target.Reference), selection.Group));
        if (near.Count == 1) Confirm(near[0]);
        else ChooseTarget(near, Confirm, selection.Group);
    }

    /// <summary>
    /// Executes a button's command. A refusal closes the screens it came from and shows the reason
    /// (CraftErrorState, CraftNotEnoughPilotsState); success closes them too.
    /// </summary>
    private void Execute(ICampaignCommand command, int group)
    {
        CampaignCommandResult result;
        try
        {
            result = _session.Commands.Execute(command);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or InvalidDataException or
                                          OverflowException or KeyNotFoundException)
        {
            CloseGroup(group);
            _status = error.Message;
            return;
        }
        var blocked = result.Events.OfType<CampaignActionBlocked>().FirstOrDefault();
        var world = _world;
        Refresh();
        CloseGroup(group);
        if (blocked is not null)
            Push(new GeoscapePopup(RefusalText(blocked, command, world), [],
                [new(_setup.Localize("STR_OK"), OkKey: true, CancelKey: true)]));
        else Notify(result.Events);
    }

    /// <summary>The refusal in the reference's words when it has them.</summary>
    private string RefusalText(CampaignActionBlocked blocked, ICampaignCommand command, CampaignWorldOverview world)
    {
        if (blocked.TextKey is not { } key) return blocked.Reason;
        var text = _setup.Localize(key);
        if (key == "STR_NOT_ENOUGH_PILOTS")
        {
            // CraftNotEnoughPilotsState names the first craft lacking pilots by its pilot requirement.
            var wing = command is DispatchCraftWing dispatch ? dispatch.Crafts : [];
            var craft = wing.Select(member => world.Crafts.FirstOrDefault(entry => GeoscapeScreens.Key(entry) == member))
                .FirstOrDefault(entry => entry?.DispatchUnavailable?.Code == "craft.no-pilots");
            if (craft is not null && _setup.Rules.Crafts.TryGet(craft.TypeId, out var handle))
                text = GeoscapeNotifications.Format(text, _setup.Rules.Crafts[handle].Value.Pilots);
        }
        return text;
    }

    /// <summary>Closes every screen of a group and the destination choice or dialogs that belong to it.</summary>
    private void CloseGroup(int group)
    {
        if (group == 0) return;
        _popups.RemoveAll(entry => entry.Group == group);
        if (_selecting?.Group == group) _selecting = null;
        if (_confirm?.Group == group) _confirm = null;
        if (_intercept?.Group == group) _intercept = null;
        _globeDirty = true;
    }

    private CampaignWorldCraft? FindCraft(CraftKey key) =>
        _world.Crafts.FirstOrDefault(craft => GeoscapeScreens.Key(craft) == key);

    /// <summary>Globe::setCraftRange: a grounded leader's return range while it picks a destination.</summary>
    private GlobeCircle? CraftRange()
    {
        if (_selecting is null || FindCraft(_selecting.Wing[0]) is not { } leader || leader.Status == "STR_OUT") return null;
        return new GlobeCircle(leader.Longitude, leader.Latitude, leader.BaseRange, 64);
    }

    /// <summary>Repairs the screens after the world changed: a wing whose leader is gone stops choosing.</summary>
    private void RepairScreens()
    {
        if (_selecting is { } selection && FindCraft(selection.Wing[0]) is null) CloseGroup(selection.Group);
    }

    private void DrawSelectionSidebar(int y)
    {
        _frame.FillRectangle(GlobeWidth + 2, CancelTop, Width - GlobeWidth - 4, SpeedHeight - 2, ButtonColor);
        Text(_setup.Localize("STR_CANCEL_UC"), SidebarLeft, CancelTop + 4, 0);
        var leader = FindCraft(_selecting!.Wing[0]);
        Wrap(_setup.Localize("STR_SELECT_DESTINATION"), SidebarLeft, y, Width - SidebarLeft - 2, HighlightColor);
        if (leader is not null) Text(GeoscapeScreens.CraftName(leader, _setup.Localize), SidebarLeft, y + 28);
        if (_selecting.Wing.Count > 1)
            Text("+" + (_selecting.Wing.Count - 1).ToString(CultureInfo.InvariantCulture), SidebarLeft, y + 40);
    }
}
