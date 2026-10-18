using Oxce.Mods.Rulesets.Runtime;

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

/// <summary>
/// CraftErrorState after a refused starting condition: the craft returns to base and the window shows
/// <see cref="TextKey"/>.
/// </summary>
public sealed record LandingRefused(int BaseId, string CraftTypeId, int CraftId, string TextKey) : ICampaignEvent;

public interface ICampaignLandingQuery
{
    IReadOnlyList<CampaignLandingConfirmation> QueryLandingConfirmations();

    /// <summary>The pending deployment request, if a landing was confirmed.</summary>
    CampaignDeploymentRequest? QueryDeploymentRequest();
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
internal sealed partial class CampaignWorld : ICampaignLandingQuery, Battles.ICampaignDeploymentHandOver
{
    private readonly List<CampaignLandingConfirmation> _landings = [];
    private int _nextLanding = 1;
    // D4: at most one deployment request; saved, never withdrawn, consumed when its battle is generated.
    private CampaignDeploymentRequest? _deploymentRequest;

    private void RegisterLandings(CampaignCapabilityRegistry registry)
    {
        registry.Query<ICampaignLandingQuery>(this);
        // Popups pause the geoscape; time runs again once every confirmation is answered.
        registry.Preflight(CampaignPreflightOrder.Landings, "landing confirmations",
            (_, _, _) => _deploymentRequest is not null ? WorldCommandReasons.DeploymentRequestPending
                : _baseDefense is not null ? WorldCommandReasons.BaseDefenseOpen
                : _landings.Count != 0 ? WorldCommandReasons.LandingConfirmationsOpen : null);
        registry.Command<AnswerLanding>(AnswerLanding);
        registry.Operation<Battles.ICampaignDeploymentHandOver>(this);
        registry.Command<LaunchFinalMission>(LaunchFinalMission);
        // D4: the campaign waits at the launch point. Time stops in its preflight (above); every other
        // command except starting the battle refuses, so nothing changes the craft, crew or world first.
        // EnterCampaignWorld is a no-op once the world has begun, which a pending request implies.
        registry.CommandGate(command => _deploymentRequest is not null &&
            command is not (Battles.StartBattle or AdvanceCampaignTime or EnterCampaignWorld)
            ? Blocked(WorldCommandReasons.DeploymentRequestPending) : null);
    }

    public CampaignDeploymentRequest? QueryDeploymentRequest() => campaign.Read(() => _deploymentRequest);

    /// <summary>
    /// The Cydonia button (SelectDestinationState) and ConfirmCydoniaState: a single craft that can launch,
    /// fully fuelled, a spacecraft, with the final research done, a commander when the final deployment's
    /// starting condition wants one, no banned armor, its pilots and some units on board, records the
    /// battle of the first final-destination deployment with that deployment's race.
    /// </summary>
    private CampaignCommandResult LaunchFinalMission(LaunchFinalMission command)
    {
        if (!TryFindCraft(command.BaseId, command.CraftTypeId, command.CraftId, out var owner, out var index))
            return Blocked(WorldCommandReasons.FinalMissionUnavailable);
        var rules = campaign.Content.RuntimeRules;
        var craft = owner.Crafts[index];
        if (FinalMissionReason(owner, craft) is { } reason) return Blocked(reason);
        var state = craft.Logistics!;
        // SelectDestinationState::btnCydoniaClick does nothing for a craft without units.
        if (!CraftHasUnits(owner, craft, state)) return Blocked(WorldCommandReasons.FinalMissionUnavailable);
        var final = rules.AlienDeployments.Rules.FirstOrDefault(static deployment => deployment.Value.FinalDestination);
        var type = command.CraftTypeId;
        bool Aboard(CampaignState.SoldierState soldier) =>
            soldier.Personal is { } personal && personal.CraftType == type && personal.CraftId == craft.Id;
        _deploymentRequest = new CampaignDeploymentRequest(final?.Id ?? "", final?.Id ?? "", "", "", final?.Value.Race ?? "",
            -1, -1, 0, new WorldTargetReference(WorldTargetKind.Base, WorldTargetReference.BaseType, owner.Id, owner.Longitude, owner.Latitude),
            owner.Id, type, craft.Id)
        {
            Soldiers = [.. owner.Soldiers.Where(Aboard).Select(static soldier => soldier.Id)],
            Vehicles = [.. state.Vehicles.Select(static vehicle => vehicle.RuleId)],
            Items = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(new Dictionary<string, int>(state.Items, StringComparer.Ordinal)),
        };
        return new CampaignCommandResult([new DeploymentRequested(_deploymentRequest)]);
    }

    private string[]? _finalResearch;

    /// <summary>
    /// SelectDestinationState's Cydonia button visibility for a lone craft: a craft that can launch (pilots,
    /// banned armor), fully fuelled, a spacecraft, with the final research done and a commander on board
    /// when the final deployment's starting condition requires one.
    /// </summary>
    private string? FinalMissionReason(CampaignState.BaseState owner, CampaignState.CraftState craft)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Crafts[craft.Rule].Value;
        if (craft.Logistics is not { } state || DispatchReason(owner, craft) is not null) return WorldCommandReasons.FinalMissionUnavailable;
        var finals = _finalResearch ??= [.. rules.Research.Rules.Where(static research => research.Value.UnlockFinalMission)
            .Select(static research => research.Id)];
        var fuelled = rule.FuelMaximum > 0 && (int)Math.Floor((double)state.Fuel / rule.FuelMaximum * 100) >= 100;
        if (!fuelled || !rule.Spacecraft || finals.Length == 1 && !campaign.ArcResearch.IsResearched(finals[0]))
            return WorldCommandReasons.FinalMissionUnavailable;
        var final = rules.AlienDeployments.Rules.FirstOrDefault(static deployment => deployment.Value.FinalDestination);
        var type = rules.Crafts.GetExternalId(craft.Rule);
        if (final is not null && final.Value.StartingCondition.Length != 0 &&
            rules.StartingConditions.TryGet(final.Value.StartingCondition, out var condition) &&
            rules.StartingConditions[condition].Value.RequireCommanderOnboard &&
            !owner.Soldiers.Any(soldier => soldier.Personal is { } personal && personal.CraftType == type &&
                personal.CraftId == craft.Id && personal.Rank == 5))
            return WorldCommandReasons.FinalMissionUnavailable;
        return null;
    }

    CampaignDeploymentRequest? Battles.ICampaignDeploymentHandOver.PendingDeploymentRequest => _deploymentRequest;

    void Battles.ICampaignDeploymentHandOver.ConsumeDeploymentRequest() => _deploymentRequest = null;

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

    /// <summary>
    /// ConfirmLandingState::checkStartingCondition without its item destruction: the text key of the
    /// first failed check, or null. <paramref name="condition"/> is the starting condition that applied.
    /// </summary>
    private string? StartingConditionRefusal(CampaignState.BaseState owner, CampaignState.CraftState craft, CraftLogisticsState state,
        CampaignLandingConfirmation landing, out RuntimeStartingCondition? condition)
    {
        condition = null;
        if (!campaign.HasAllowedArmorsOnboard(owner, craft)) return "STR_ARMOR_NOT_ALLOWED_ONBOARD";
        var rules = campaign.Content.RuntimeRules;
        var ufo = landing.Target.Kind == WorldTargetKind.Ufo ? TargetUfo(landing.Target) : null;
        if (LandingDeployment(landing) is not { } deployment) return null;
        if (deployment.StartingCondition.Length != 0 && rules.StartingConditions.TryGet(deployment.StartingCondition, out var handle))
            condition = rules.StartingConditions[handle].Value;
        else if (rules.Campaign.Globe.Textures.TryGetValue(landing.MissionTexture, out var texture) && texture.StartingCondition.Length != 0 &&
            rules.StartingConditions.TryGet(texture.StartingCondition, out var textureHandle))
            condition = rules.StartingConditions[textureHandle].Value;
        if (condition is not { } applied) return null;
        var type = landing.CraftTypeId;
        bool Aboard(CampaignState.SoldierState soldier) =>
            soldier.Personal is { } personal && personal.CraftType == type && personal.CraftId == craft.Id;
        // Soldier rank 5 is RANK_COMMANDER; a destroyed UFO does not ask for one.
        if (applied.RequireCommanderOnboard && !owner.Soldiers.Any(soldier => Aboard(soldier) && soldier.Personal!.Rank == 5) &&
            ufo?.Status is not UfoStatus.Destroyed)
            return "STR_STARTING_CONDITION_COMMANDER";
        if (!applied.IsCraftPermitted(type)) return "STR_STARTING_CONDITION_CRAFT";
        if (owner.Soldiers.Any(soldier => Aboard(soldier) && !applied.IsSoldierTypePermitted(rules.Soldiers.GetExternalId(soldier.Rule))))
            return "STR_STARTING_CONDITION_SOLDIER_TYPE";
        foreach (var (item, quantity) in applied.RequiredItems)
            if (state.Items.GetValueOrDefault(item) < quantity) return "STR_STARTING_CONDITION_ITEM";
        return null;
    }

    /// <summary>
    /// The deployment ConfirmLandingState checks: the UFO type (with "_UNDERWATER" on a fake-underwater
    /// mission texture), the site's deployment, or an alien base race's custom mission before the base's
    /// own deployment. Null when the mod has none.
    /// </summary>
    private RuntimeAlienDeploymentRule? LandingDeployment(CampaignLandingConfirmation landing)
    {
        var rules = campaign.Content.RuntimeRules;
        string? id = null;
        switch (landing.Target.Kind)
        {
            case WorldTargetKind.Ufo when TargetUfo(landing.Target) is { } ufo:
                id = rules.Campaign.Globe.Textures.TryGetValue(landing.MissionTexture, out var texture) && texture.FakeUnderwater
                    ? ufo.RuleId + "_UNDERWATER" : ufo.RuleId;
                break;
            case WorldTargetKind.MissionSite when _sites.Find(site => site.Id == landing.Target.Id &&
                MarkerName(site.DeploymentId) == landing.Target.TypeId) is { } site:
                id = site.DeploymentId;
                break;
            case WorldTargetKind.AlienBase when TargetAlienBase(landing.Target) is { } alienBase:
                if (rules.AlienRaces.TryGet(alienBase.Race, out var race) && rules.AlienRaces[race].Value.BaseCustomMission is { Length: > 0 } custom &&
                    rules.AlienDeployments.TryGet(custom, out _))
                    id = custom;
                else id = alienBase.DeploymentId;
                break;
        }
        return id is not null && rules.AlienDeployments.TryGet(id, out var handle) ? rules.AlienDeployments[handle].Value : null;
    }

    /// <summary>
    /// ConfirmLandingState::btnYesClick's BattlescapeGenerator inputs for the shown landing: mission type,
    /// deployments, race, textures, shade, target, craft and crew. Null when the target is gone (the
    /// reference throws "No mission available!").
    /// </summary>
    private CampaignDeploymentRequest? DeploymentRequestFor(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CampaignLandingConfirmation landing)
    {
        var rules = campaign.Content.RuntimeRules;
        string missionType, deployment, customDeploy, customMission, race;
        var missionTexture = landing.MissionTexture;
        switch (landing.Target.Kind)
        {
            case WorldTargetKind.Ufo when TargetUfo(landing.Target) is { } ufo && FindMission(ufo.MissionId) is { } mission:
                {
                    missionType = ufo.Status == UfoStatus.Crashed ? "STR_UFO_CRASH_RECOVERY" : "STR_UFO_GROUND_ASSAULT";
                    var underwater = rules.Campaign.Globe.Textures.TryGetValue(missionTexture, out var texture) && texture.FakeUnderwater;
                    deployment = underwater ? ufo.RuleId + "_UNDERWATER" : ufo.RuleId;
                    var stats = rules.Ufos.GetRequiredValue(ufo.RuleId).StatsForRace(mission.Race);
                    customDeploy = stats.CraftCustomDeployment;
                    customMission = underwater ? deployment : "";
                    race = mission.Race;
                    break;
                }
            case WorldTargetKind.MissionSite when _sites.Find(site => site.Id == landing.Target.Id &&
                MarkerName(site.DeploymentId) == landing.Target.TypeId) is { } site:
                missionType = deployment = site.DeploymentId;
                customDeploy = site.CustomDeploymentId;
                customMission = "";
                race = site.Race;
                break;
            case WorldTargetKind.AlienBase when TargetAlienBase(landing.Target) is { } alienBase:
                {
                    missionType = deployment = alienBase.DeploymentId;
                    var raceRule = rules.AlienRaces.TryGet(alienBase.Race, out var handle) ? rules.AlienRaces[handle].Value : null;
                    customDeploy = raceRule?.BaseCustomDeploy ?? "";
                    customMission = raceRule?.BaseCustomMission ?? "";
                    race = alienBase.Race;
                    // BattlescapeGenerator::setWorldTexture(0, globeTexture) for alien bases.
                    missionTexture = 0;
                    break;
                }
            default:
                return null;
        }
        string Known(string id) => id.Length != 0 && rules.AlienDeployments.TryGet(id, out _) ? id : "";
        var type = landing.CraftTypeId;
        return new CampaignDeploymentRequest(missionType, Known(deployment), Known(customDeploy), Known(customMission), race,
            missionTexture, landing.GlobeTexture, landing.Shade, landing.Target, owner.Id, type, craft.Id)
        {
            Soldiers = [.. owner.Soldiers.Where(soldier => soldier.Personal is { } personal && personal.CraftType == type &&
                personal.CraftId == craft.Id).Select(static soldier => soldier.Id)],
        };
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
        // Confirmations queued behind a confirmed landing wait until its battle is over (D4).
        if (_deploymentRequest is not null) return Blocked(WorldCommandReasons.DeploymentRequestPending);
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
                if (StartingConditionRefusal(owner, craft, state, landing, out var condition) is { } refusal)
                {
                    owner.Crafts[index] = craft with { Logistics = SetDestination(state, maximum, BaseReference(owner)) };
                    _landings.RemoveAt(0);
                    CloseStaleLandings();
                    return new CampaignCommandResult([new LandingRefused(owner.Id, landing.CraftTypeId, landing.CraftId, refusal)]);
                }
                // The reference throws "No mission available!"; the port refuses Yes and leaves No and Patrol.
                if (DeploymentRequestFor(owner, craft, landing) is not { } request) return Blocked(WorldCommandReasons.LandingTargetGone);
                // Craft::destroyRequiredItems, then the request records the craft's load.
                if (condition is { DestroyRequiredItems: true })
                {
                    var items = new Dictionary<string, int>(state.Items, StringComparer.Ordinal);
                    foreach (var (item, quantity) in condition.RequiredItems)
                        if (items.TryGetValue(item, out var held) && quantity > 0)
                        {
                            if (held - quantity <= 0) items.Remove(item);
                            else items[item] = held - quantity;
                        }
                    state = state with { Items = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(items) };
                    owner.Crafts[index] = craft with { Logistics = state };
                }
                _deploymentRequest = request with
                {
                    Vehicles = [.. state.Vehicles.Select(static vehicle => vehicle.RuleId)],
                    Items = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(
                        new Dictionary<string, int>(state.Items, StringComparer.Ordinal)),
                };
                _landings.RemoveAt(0);
                return new CampaignCommandResult([new DeploymentRequested(_deploymentRequest)]);
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
