namespace Oxce.Gameplay.Campaigns.World;

/// <summary>The answers of <c>ConfirmLandingState</c>: Yes, No, or Patrol (No with Ctrl held).</summary>
public enum LandingAnswer
{
    Yes,
    No,
    Patrol,
}

/// <summary>Answers the landing confirmation that is currently shown.</summary>
public sealed record AnswerLanding(int Confirmation, LandingAnswer Answer) : ICampaignCommand;

/// <summary>
/// A craft is ready to land near its destination (<c>ConfirmLandingState</c>). Textures are globe
/// texture ids (-1 for none); the shade is the landing site's battlescape shade (0-15). It pauses time.
/// </summary>
public sealed record LandingConfirmationRequested(int Confirmation, int BaseId, string CraftTypeId, int CraftId,
    WorldTargetReference Target, int MissionTexture, int GlobeTexture, int Shade) : ICampaignEvent;

/// <summary>A queued landing confirmation as the UI shows it; the first one is the open window.</summary>
public sealed record CampaignLandingConfirmation(int Confirmation, int BaseId, string CraftTypeId, int CraftId,
    WorldTargetReference Target, int MissionTexture, int GlobeTexture, int Shade);

public interface ICampaignLandingQuery
{
    IReadOnlyList<CampaignLandingConfirmation> QueryLandingConfirmations();
}

/// <summary>
/// Landing confirmations from the craft pass of <c>GeoscapeState::time5Seconds</c>: a craft with units
/// that may land reaching a landed, crashed or expiring UFO (outside an interception), a mission site or
/// a discovered alien base queues a <c>ConfirmLandingState</c> with the site's texture and shade. The
/// queue is the geoscape's popup list: it pauses time, shows one window at a time and is not saved, so
/// after a reload a craft still at its destination asks again on the next tick. Each window first
/// applies <c>ConfirmLandingState::init</c>: a craft now heading for its own base closes it, and (an
/// intentional difference, ADR 0032) so does a craft left without a destination, which would make the
/// reference throw on Yes. No sends the craft home and Patrol leaves it circling.
/// </summary>
internal sealed partial class CampaignWorld : ICampaignLandingQuery
{
    private readonly List<CampaignLandingConfirmation> _landings = [];
    private int _nextLanding = 1;

    private void RegisterLandings(CampaignCapabilityRegistry registry)
    {
        registry.Query<ICampaignLandingQuery>(this);
        // Popups pause the geoscape; time runs again once every confirmation is answered.
        registry.Preflight(CampaignPreflightOrder.Landings, "landing confirmations",
            (_, _, _) => _landings.Count != 0 ? WorldCommandReasons.LandingConfirmationsOpen : null);
        registry.Command<AnswerLanding>(AnswerLanding);
    }

    public IReadOnlyList<CampaignLandingConfirmation> QueryLandingConfirmations() => campaign.Read(() =>
        (IReadOnlyList<CampaignLandingConfirmation>)[.. _landings.SkipWhile(IsStaleLanding)]);

    /// <summary>
    /// The craft pass reached a landing target with a craft that may land: queue its confirmation with
    /// the texture and shade of Globe::getPolygonTextureAndShade at the target.
    /// </summary>
    private void QueueLanding(CampaignState.BaseState owner, CampaignState.CraftState craft, WorldTargetReference target,
        WorldPosition position, int missionTexture, CampaignState.TimeEffects effects)
    {
        var rules = campaign.Content.RuntimeRules;
        var globeTexture = StrategicGeography.TextureAt(rules.Campaign.Globe, position.Longitude, position.Latitude) ?? -1;
        if (target.Kind == WorldTargetKind.MissionSite)
        {
            // Mission sites may lie on the ocean; a cosmetic ocean texture becomes texture -1.
            if (rules.Campaign.Globe.Textures.TryGetValue(globeTexture, out var texture) && texture.IsOcean) globeTexture = -1;
            if (missionTexture < 0 || !rules.Campaign.Globe.Textures.ContainsKey(missionTexture)) missionTexture = globeTexture;
        }
        else
        {
            missionTexture = globeTexture;
        }
        var shade = WorldShading.LandingShade(campaign.Time, position.Longitude, position.Latitude, campaign.Options.GlobeSeasons);
        var landing = new CampaignLandingConfirmation(_nextLanding++, owner.Id, rules.Crafts.GetExternalId(craft.Rule), craft.Id,
            target, missionTexture, globeTexture, shade);
        _landings.Add(landing);
        effects.Notify(new LandingConfirmationRequested(landing.Confirmation, landing.BaseId, landing.CraftTypeId, landing.CraftId,
            landing.Target, landing.MissionTexture, landing.GlobeTexture, landing.Shade));
    }

    // ConfirmLandingState::init for the window about to be shown; earlier windows closed in order.
    private void CloseStaleLandings()
    {
        while (_landings.Count != 0 && IsStaleLanding(_landings[0])) _landings.RemoveAt(0);
    }

    private bool IsStaleLanding(CampaignLandingConfirmation landing) =>
        !TryFindCraft(landing.BaseId, landing.CraftTypeId, landing.CraftId, out var owner, out var index) ||
        owner.Crafts[index].Logistics?.Destination is null or { Kind: WorldTargetKind.Base };

    private CampaignCommandResult AnswerLanding(AnswerLanding command)
    {
        CloseStaleLandings();
        if (_landings.Count == 0 || _landings[0].Confirmation != command.Confirmation || !Enum.IsDefined(command.Answer))
            return Blocked(WorldCommandReasons.NoLandingConfirmation);
        var landing = _landings[0];
        if (!TryFindCraft(landing.BaseId, landing.CraftTypeId, landing.CraftId, out var owner, out var index))
            return Blocked(WorldCommandReasons.NoLandingConfirmation);
        var craft = owner.Crafts[index];
        var state = craft.Logistics!;
        if (!CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value, state.Weapons,
                campaign.Content.RuntimeRules, out var maximum))
            return Blocked(SpeedRangeReason);
        switch (command.Answer)
        {
            case LandingAnswer.Yes:
                // The starting-condition check and the deployment request come with the rest of S6.
                return Blocked(LandingReason);
            case LandingAnswer.No:
                owner.Crafts[index] = craft with { Logistics = SetDestination(state, maximum, BaseReference(owner)) };
                break;
            default:
                // Craft::setDestination(0): the craft patrols at half speed where it is.
                owner.Crafts[index] = craft with
                {
                    Logistics = state with
                    {
                        Destination = null,
                        Speed = maximum / 2,
                        SpeedRadian = WorldGeometry.RadianSpeed(maximum / 2),
                        SpeedLongitude = 0,
                        SpeedLatitude = 0,
                    },
                };
                break;
        }
        _landings.RemoveAt(0);
        CloseStaleLandings();
        return new CampaignCommandResult([new CraftDestinationChanged(owner.Id, landing.CraftTypeId, landing.CraftId,
            owner.Crafts[index].Logistics!.Destination)]);
    }
}
