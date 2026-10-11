using System.Globalization;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;

namespace Oxce.Engine.Geoscape;

/// <summary>A button on a geoscape popup. <see cref="ResetSpeed"/> is GeoscapeState::timerReset.</summary>
public sealed record GeoscapePopupAction(string Label, bool ResetSpeed = false, WorldPosition? Center = null);

/// <summary>A queued geoscape message window and its buttons; the first is the default (Enter), the last cancels (Esc).</summary>
public sealed record GeoscapePopup(string Title, IReadOnlyList<string> Lines, IReadOnlyList<GeoscapePopupAction> Actions)
{
    /// <summary>The reference resets the speed when it queues this popup (research completion).</summary>
    public bool ResetsSpeedWhenShown { get; init; }
}

/// <summary>
/// Maps campaign notifications to the reference geoscape popups and their speed resets
/// (GeoscapeState::popup callers and each state's timerReset buttons). Notifications without a
/// reference popup, such as an auto-patrol relaunch, produce none.
/// </summary>
public static class GeoscapeNotifications
{
    public static GeoscapePopup? Describe(ICampaignEvent notification, CampaignWorldOverview world, Func<string, string> tr)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tr);
        GeoscapePopupAction Ok() => new(tr("STR_OK"));
        GeoscapePopupAction OkFiveSeconds() => new(tr("STR_OK_5_SECONDS"), ResetSpeed: true);
        GeoscapePopupAction Cancel() => new(tr("STR_CANCEL_UC"));
        switch (notification)
        {
            case UfoContactDetected detected:
                {
                    // UfoDetectedState: Centre and Intercept reset the speed; Cancel does not.
                    var ufo = Ufo(detected.UniqueId);
                    var lines = new List<string> { tr("STR_DETECTED") };
                    if (detected.Hyperwave) lines.Add(tr("STR_HYPER_WAVE_TRANSMISSIONS_ARE_DECODED"));
                    return new(UfoName(ufo, detected.UniqueId), lines,
                    [new(tr("STR_CENTER_ON_UFO_TIME_5_SECONDS"), true, Position(ufo)), Cancel()]);
                }
            case UfoContactLost lost:
                return new(UfoName(Ufo(lost.UniqueId), lost.UniqueId), [tr("STR_TRACKING_LOST")], [Ok()]);
            case MissionSiteLost site:
                return new(site.CustomName.Length != 0 ? site.CustomName : Format(tr(site.MarkerName), site.SiteId),
                    [tr("STR_TRACKING_LOST")], [Ok()]);
            case MissionSiteDetected site:
                {
                    var target = Find(WorldTargetKind.MissionSite, site.SiteId);
                    return new(target?.Label ?? Format(tr(site.MarkerName), site.SiteId), [],
                        [new(tr("STR_CENTER_ON_SITE_TIME_5_SECONDS"), true, Position(target)), Cancel()]);
                }
            case AlienBaseDiscovered alienBase:
                {
                    // AlienBaseState: OK centres on the base and resets the speed.
                    var target = Find(WorldTargetKind.AlienBase, alienBase.Id);
                    return new(target?.Label ?? tr(alienBase.DeploymentId),
                        [Format(tr("STR_XCOM_AGENTS_HAVE_LOCATED_AN_ALIEN_BASE_IN_REGION"), tr("STR_UNKNOWN"))],
                        [new(tr("STR_OK"), true, Position(target))]);
                }
            case UfoLanded landed:
                return new(Format(tr("STR_UFO_HAS_LANDED"), UfoName(Ufo(landed.UfoUniqueId), landed.UfoUniqueId)), [],
                    [Ok(), OkFiveSeconds()]);
            case UfoStartedHunting hunting:
                return new(Format(tr("STR_UFO_STARTED_HUNTING"), UfoName(Ufo(hunting.UniqueId), hunting.UniqueId),
                    CraftName(null, hunting.CraftTypeId, hunting.CraftId)), [], [Ok(), OkFiveSeconds()]);
            case CraftLowFuel fuel:
                return new(CraftName(fuel.BaseId, fuel.CraftTypeId, fuel.CraftId),
                    [tr("STR_IS_LOW_ON_FUEL_RETURNING_TO_BASE")], [Ok(), OkFiveSeconds()]);
            case CraftArrivalServiceMessage service:
                return new(service.Message, [], [Ok(), OkFiveSeconds()]);
            case FacilityServiceMessage service:
                return new(service.Message, [], [Ok(), OkFiveSeconds()]);
            case CraftArrivedAtWaypoint arrived:
                return new(Format(tr("STR_CRAFT_HAS_REACHED_DESTINATION"),
                    CraftName(arrived.BaseId, arrived.CraftTypeId, arrived.CraftId),
                    Format(tr(WorldTargetReference.WaypointType), arrived.WaypointId)), [tr("STR_NOW_PATROLLING")], [Ok()]);
            case CraftTargetLost lostTarget:
                return new(CraftName(lostTarget.BaseId, lostTarget.CraftTypeId, lostTarget.CraftId),
                    [tr("STR_TRACKING_LOST"), lostTarget.ReturningToBase ? tr("STR_RETURNING_TO_BASE") : tr("STR_NOW_PATROLLING")],
                    [Ok()]);
            case SuppliesArrived supplies:
                return new(tr("STR_ITEMS_ARRIVING"), [BaseName(supplies.BaseId)], [Ok()]);
            case CampaignConstructionCompleted construction:
                return new(Format(tr("STR_CONSTRUCTION_OF_FACILITY_AT_BASE_IS_COMPLETE"), tr(construction.RuleId),
                    BaseName(construction.BaseId)), [], [Ok()]);
            case CampaignProductionProgress production:
                return new(production.StopReason ?? Format(tr("STR_PRODUCTION_OF_ITEM_AT_BASE_IS_COMPLETE"),
                    tr(production.RuleId), BaseName(production.BaseId)), [], [Ok()]);
            case CampaignResearchCompleted research:
                return new(tr("STR_RESEARCH_COMPLETED"), [tr(research.RuleId)], [Ok()]) { ResetsSpeedWhenShown = true };
            case CampaignTrainingCompleted training:
                return new(tr(training.Psi ? "STR_PSI_TRAINING" : "STR_TRAINING"),
                    [BaseName(training.BaseId), "#" + training.SoldierId.ToString(CultureInfo.InvariantCulture)], [Ok()]);
            case CampaignArcUnlocked arc:
                return new(tr(arc.RuleId), [], [Ok()]);
            case CampaignStrategicEventRequested request:
                return new(tr(request.RuleId), [], [Ok()]);
            case CampaignTransformationEventSelected transformation:
                return new(tr(transformation.RuleId), [], [Ok()]);
            case CampaignActionBlocked blocked:
                return new("Time cannot advance", [blocked.Reason], [Ok()]);
            default:
                return null;
        }

        CampaignWorldTarget? Ufo(int uniqueId) =>
            world.Targets.FirstOrDefault(target => target.Kind == WorldTargetKind.Ufo && target.Reference.UniqueId == uniqueId);

        CampaignWorldTarget? Find(WorldTargetKind kind, int id) =>
            world.Targets.FirstOrDefault(target => target.Kind == kind && target.Id == id);

        static WorldPosition? Position(CampaignWorldTarget? target) =>
            target is null ? null : new WorldPosition(target.Longitude, target.Latitude);

        static string UfoName(CampaignWorldTarget? ufo, int uniqueId) =>
            ufo?.Label ?? "UFO-" + uniqueId.ToString(CultureInfo.InvariantCulture);

        string CraftName(int? baseId, string typeId, int craftId) =>
            world.Crafts.FirstOrDefault(craft => (baseId is null || craft.BaseId == baseId) && craft.TypeId == typeId &&
                craft.Id == craftId) is { Name.Length: > 0 } craft
                ? craft.Name
                : tr(typeId) + "-" + craftId.ToString(CultureInfo.InvariantCulture);

        string BaseName(int baseId) =>
            world.Bases.FirstOrDefault(owner => owner.Id == baseId)?.Name ?? "#" + baseId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>LocalizedText::arg: replaces <c>{0}</c>, <c>{1}</c>, ... in order.</summary>
    public static string Format(string text, params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var index = 0; index < arguments.Length; index++)
            text = text.Replace("{" + index.ToString(CultureInfo.InvariantCulture) + "}",
                Convert.ToString(arguments[index], CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return text;
    }
}
