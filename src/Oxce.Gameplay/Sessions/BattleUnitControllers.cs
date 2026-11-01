using Oxce.Gameplay.Battles;

namespace Oxce.Gameplay.Sessions;

/// <summary>
/// The battle's unit ownership for the session (ADR 0037 rule 2): a unit controlled by a human slot belongs to the player the
/// host maps the slot to; AI units and unknown units belong to nobody. Set it as <see cref="CampaignSession.Controllers"/>.
/// </summary>
public sealed class BattleUnitControllers(IBattleQuery battle, Func<int, PlayerId>? slotPlayer = null) : IUnitControllers
{
    private readonly IBattleQuery _battle = battle ?? throw new ArgumentNullException(nameof(battle));
    private readonly Func<int, PlayerId> _slotPlayer = slotPlayer ?? (static slot => new PlayerId(slot));

    public PlayerId? ControllerOf(int unitId)
    {
        // The units a human controls are X-COM's (or mind-controlled): the player faction's view lists them.
        var view = _battle.QueryBattle(BattleViewer.LocalPlayer)?.Units.FirstOrDefault(unit => unit.Id == unitId);
        return view is { ControllerSlot: >= 0 } ? _slotPlayer(view.ControllerSlot) : null;
    }
}
