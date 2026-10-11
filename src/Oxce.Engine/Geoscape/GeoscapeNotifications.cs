using System.Globalization;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// A button on a geoscape popup. <see cref="ResetSpeed"/> is GeoscapeState::timerReset;
/// <see cref="OkKey"/> and <see cref="CancelKey"/> bind Options::keyOk (Enter) and keyCancel (Esc)
/// as the reference state does.
/// </summary>
public sealed record GeoscapePopupAction(string Label, bool ResetSpeed = false, WorldPosition? Center = null,
    bool OkKey = false, bool CancelKey = false)
{
    /// <summary>The screen the button opens after the popup closes.</summary>
    public GeoscapeIntent? Intent { get; init; }
    /// <summary>The campaign command the button executes; a refusal is reported in a popup.</summary>
    public ICampaignCommand? Command { get; init; }
    /// <summary>The button is shown but refuses with this reason (a hidden reference button).</summary>
    public string? Disabled { get; init; }
}

/// <summary>A geoscape screen a popup button leads to.</summary>
public abstract record GeoscapeIntent;

/// <summary>InterceptState: the craft list, optionally aimed at a target.</summary>
public sealed record InterceptIntent(WorldTargetReference? Target = null, int? BaseId = null) : GeoscapeIntent;

/// <summary>GeoscapeCraftState for an airborne craft.</summary>
public sealed record CraftIntent(CraftKey Craft) : GeoscapeIntent;

/// <summary>SelectDestinationState for a wing whose first craft leads.</summary>
public sealed record SelectDestinationIntent(IReadOnlyList<CraftKey> Wing) : GeoscapeIntent;

/// <summary>BasescapeState (or ManufactureState) for one base.</summary>
public sealed record BaseIntent(int BaseId) : GeoscapeIntent;

/// <summary>TargetInfoState for a UFO, mission site or alien base.</summary>
public sealed record TargetInfoIntent(WorldTargetReference Target) : GeoscapeIntent;

/// <summary>A queued geoscape message window and its buttons.</summary>
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
    /// <param name="rules">Labels the craft screen's weapon slots; the defaults are used without it.</param>
    public static GeoscapePopup? Describe(ICampaignEvent notification, CampaignWorldOverview world, Func<string, string> tr,
        RuntimeRuleCatalog? rules = null, int etaMode = 0)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tr);
        // Single-button states bind both keys to OK; two-button states bind Esc to OK and Enter to the
        // other button (LowFuelState, CraftErrorState, ItemsArrivingState, ...).
        GeoscapePopupAction Ok() => new(tr("STR_OK"), OkKey: true, CancelKey: true);
        GeoscapePopupAction OkCancelOnly() => new(tr("STR_OK"), CancelKey: true);
        GeoscapePopupAction OkFiveSeconds() => new(tr("STR_OK_5_SECONDS"), ResetSpeed: true, OkKey: true);
        GeoscapePopupAction Cancel() => new(tr("STR_CANCEL_UC"), CancelKey: true);
        // ItemsArrivingState and ProductionCompleteState: the base button resets the speed; only
        // ItemsArrivingState binds it to Enter.
        GeoscapePopupAction GoToBase(int baseId, string label = "STR_GO_TO_BASE", bool okKey = false) =>
            new(tr(label), ResetSpeed: true, OkKey: okKey) { Intent = new BaseIntent(baseId) };
        switch (notification)
        {
            case UfoContactDetected detected:
                {
                    // UfoDetectedState: Intercept and Centre reset the speed and centre on the UFO; Cancel does not.
                    var ufo = Ufo(detected.UniqueId);
                    if (ufo is not null) return GeoscapeScreens.Ufo(ufo, detected: true, detected.Hyperwave, tr);
                    var lines = new List<string> { tr("STR_DETECTED") };
                    if (detected.Hyperwave) lines.Add(tr("STR_HYPER_WAVE_TRANSMISSIONS_ARE_DECODED"));
                    return new(UfoName(ufo, detected.UniqueId), lines,
                    [
                        new(tr("STR_INTERCEPT"), true, Position(ufo)) { Intent = new InterceptIntent(ufo?.Reference) },
                        new(tr("STR_CENTER_ON_UFO_TIME_5_SECONDS"), true, Position(ufo)), Cancel(),
                    ]);
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
                    [
                        new(tr("STR_INTERCEPT"), true, Position(target)) { Intent = new InterceptIntent(target?.Reference) },
                        new(tr("STR_CENTER_ON_SITE_TIME_5_SECONDS"), true, Position(target)), Cancel(),
                    ]);
                }
            case AlienBaseDiscovered alienBase:
                {
                    // AlienBaseState: OK centres on the base and resets the speed.
                    // AlienBaseState: "country, region", the region alone or unknown, in the deployment's message.
                    var target = Find(WorldTargetKind.AlienBase, alienBase.Id);
                    var location = target is { CountryId.Length: > 0 }
                        ? Format(tr("STR_COUNTRIES_COMMA"), tr(target.CountryId), target.RegionId.Length == 0 ? "" : tr(target.RegionId))
                        : target is { RegionId.Length: > 0 } ? tr(target.RegionId) : tr("STR_UNKNOWN");
                    var title = target is { DiscoveredMessage.Length: > 0 }
                        ? Format(tr(target.DiscoveredMessage), location, tr(target.Reference.TypeId))
                        : Format(tr("STR_XCOM_AGENTS_HAVE_LOCATED_AN_ALIEN_BASE_IN_REGION"), location);
                    return new(title, [],
                        [new(tr("STR_OK"), true, Position(target), OkKey: true, CancelKey: true)]);
                }
            case UfoLanded landed:
                return new(Format(tr("STR_UFO_HAS_LANDED"), UfoName(Ufo(landed.UfoUniqueId), landed.UfoUniqueId)), [],
                    [OkCancelOnly(), OkFiveSeconds()]);
            case UfoStartedHunting hunting:
                return new(Format(tr("STR_UFO_STARTED_HUNTING"), UfoName(Ufo(hunting.UniqueId), hunting.UniqueId),
                    CraftName(null, hunting.CraftTypeId, hunting.CraftId)), [], [OkCancelOnly(), OkFiveSeconds()]);
            case CraftLowFuel fuel:
                return new(CraftName(fuel.BaseId, fuel.CraftTypeId, fuel.CraftId),
                    [tr("STR_IS_LOW_ON_FUEL_RETURNING_TO_BASE")], [OkCancelOnly(), OkFiveSeconds()]);
            case CraftArrivalServiceMessage service:
                {
                    // GeoscapeState::time1Hour/time30Minutes: "craft is ready" names craft and base;
                    // the refuel and rearm warnings name the item first.
                    var craft = CraftName(service.BaseId, service.CraftType, service.CraftId);
                    var text = service.ItemId.Length == 0
                        ? Format(tr(service.Message), craft, BaseName(service.BaseId))
                        : Format(tr(service.Message), tr(service.ItemId), craft, BaseName(service.BaseId));
                    return new(text, [], [OkCancelOnly(), OkFiveSeconds()]);
                }
            case FacilityServiceMessage service:
                return new(Format(tr(service.Message), tr(service.ItemId), tr(service.FacilityRuleId),
                    BaseName(service.BaseId)), [], [OkCancelOnly(), OkFiveSeconds()]);
            case CraftArrivedAtWaypoint arrived:
                return new(Format(tr("STR_CRAFT_HAS_REACHED_DESTINATION"),
                    CraftName(arrived.BaseId, arrived.CraftTypeId, arrived.CraftId),
                    Format(tr(WorldTargetReference.WaypointType), arrived.WaypointId)), [tr("STR_NOW_PATROLLING")],
                    [
                        OkCancelOnly(),
                        // CraftPatrolState::btnRedirectClick opens GeoscapeCraftState without a timer reset.
                        new(tr("STR_REDIRECT_CRAFT"), OkKey: true)
                        {
                            Intent = new CraftIntent(new(arrived.BaseId, arrived.CraftTypeId, arrived.CraftId)),
                        },
                    ]);
            case CraftTargetLost { OffersLastPosition: true, Target.Kind: WorldTargetKind.Ufo } lostTarget
                when world.Crafts.FirstOrDefault(craft => craft.BaseId == lostTarget.BaseId &&
                    craft.TypeId == lostTarget.CraftTypeId && craft.Id == lostTarget.CraftId) is { } pursuer:
                // GeoscapeState::time5Seconds: a pursued UFO left radar; the craft screen offers its last position.
                return GeoscapeScreens.Craft(pursuer, world, rules, tr, etaMode,
                    new GeoscapeLostUfo(lostTarget.Target.Position, lostTarget.Target.Id));
            case CraftTargetLost:
                // A visible destroyed or ignored UFO, or a target that no longer exists: the craft just returns or patrols.
                return null;
            case SuppliesArrived supplies:
                return new(tr("STR_ITEMS_ARRIVING"), [BaseName(supplies.BaseId)], [OkCancelOnly(), GoToBase(supplies.BaseId, okKey: true)]);
            case CampaignConstructionCompleted construction:
                return new(Format(tr("STR_CONSTRUCTION_OF_FACILITY_AT_BASE_IS_COMPLETE"), tr(construction.RuleId),
                    BaseName(construction.BaseId)), [], [OkCancelOnly(), GoToBase(construction.BaseId)]);
            case CampaignProductionProgress { StopReason: { } reason } production:
                // GeoscapeState::time1Hour opens ProductionCompleteState only when a project ends.
                return new(Format(tr(ProductionMessage(reason)), tr(production.RuleId), BaseName(production.BaseId)), [],
                    [OkCancelOnly(), GoToBase(production.BaseId, "STR_ALLOCATE_MANUFACTURE")]);
            case CampaignResearchCompleted research:
                return new(tr("STR_RESEARCH_COMPLETED"), [tr(research.RuleId)], [OkCancelOnly()]) { ResetsSpeedWhenShown = true };
            case CampaignTrainingCompleted training:
                return new(tr(training.Psi ? "STR_PSI_TRAINING" : "STR_TRAINING"),
                    [BaseName(training.BaseId), "#" + training.SoldierId.ToString(CultureInfo.InvariantCulture)], [OkCancelOnly()]);
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
                : GeoscapeScreens.DefaultCraftName(typeId, craftId, tr);

        string BaseName(int baseId) =>
            world.Bases.FirstOrDefault(owner => owner.Id == baseId)?.Name ?? "#" + baseId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>ProductionCompleteState's message for a project that ended.</summary>
    private static string ProductionMessage(string reason) => reason switch
    {
        "STR_PRODUCTION_COMPLETE" => "STR_PRODUCTION_OF_ITEM_AT_BASE_IS_COMPLETE",
        "STR_NOT_ENOUGH_MONEY" => "STR_NOT_ENOUGH_MONEY_TO_PRODUCE_ITEM_AT_BASE",
        "STR_NOT_ENOUGH_MATERIALS" => "STR_NOT_ENOUGH_SPECIAL_MATERIALS_TO_PRODUCE_ITEM_AT_BASE",
        "STR_NOT_ENOUGH_LIVING_SPACE" => "STR_NOT_ENOUGH_LIVING_SPACE_AT_BASE",
        _ => reason,
    };

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
