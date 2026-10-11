using System.Globalization;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Engine.Geoscape;

/// <summary>InterceptState row: a click launches the wing led by this craft; Shift+click adds or removes it.</summary>
public sealed record InterceptRowIntent(CraftKey Craft) : GeoscapeIntent;

/// <summary>InterceptState scrolling: shows another page of the craft list.</summary>
public sealed record InterceptPageIntent(int Page) : GeoscapeIntent;

/// <summary>ConfirmDestinationState's follow-wing-leader toggle.</summary>
public sealed record FollowLeaderIntent : GeoscapeIntent;

/// <summary>GeoscapeCraftState's lost-target variant: the UFO's last known position and marker id.</summary>
public sealed record GeoscapeLostUfo(WorldPosition Position, int UfoId);

/// <summary>Where a wing is sent: a globe target or a new waypoint.</summary>
public sealed record GeoscapeDestination(WorldTargetReference? Target, WorldPosition? Waypoint, string Label)
{
    public WorldPosition Position => Waypoint ?? (Target is { } target
        ? new WorldPosition(target.Longitude, target.Latitude)
        : throw new InvalidOperationException("A destination needs a target or a waypoint."));
}

/// <summary>
/// The geoscape's craft and target screens as popups: GeoscapeCraftState, InterceptState,
/// ConfirmDestinationState and TargetInfoState. The texts and buttons follow the reference states;
/// the client executes the buttons' commands and intents.
/// </summary>
public static class GeoscapeScreens
{
    /// <summary>Craft rows per InterceptState page.</summary>
    public const int InterceptPageSize = 6;

    /// <summary>InterceptState: wing size limit (three selected craft plus the clicked leader).</summary>
    public const int MaximumWingSelection = 3;

    public static CraftKey Key(CampaignWorldCraft craft)
    {
        ArgumentNullException.ThrowIfNull(craft);
        return new(craft.BaseId, craft.TypeId, craft.Id);
    }

    public static string CraftName(CampaignWorldCraft craft, Func<string, string> tr) =>
        craft.Name.Length != 0 ? craft.Name : DefaultCraftName(craft.TypeId, craft.Id, tr);

    /// <summary>Craft::getDefaultName: STR_CRAFTNAME with the craft type and id ("{0}-{1}" without the string).</summary>
    public static string DefaultCraftName(string typeId, int id, Func<string, string> tr)
    {
        var pattern = tr("STR_CRAFTNAME");
        if (pattern == "STR_CRAFTNAME") pattern = "{0}-{1}";
        return Format(pattern, tr(typeId), id);
    }

    /// <summary>InterceptState::lstCraftsLeftClick allowStart.</summary>
    public static bool CanStart(CampaignWorldCraft craft) =>
        craft.Status == "STR_READY" || (craft.Status == "STR_OUT" && !craft.LowFuel && !craft.MissionComplete);

    /// <summary>GeoscapeCraftState for an airborne craft.</summary>
    /// <param name="lost">
    /// GeoscapeState::time5Seconds after a pursued UFO vanished from radar: the status names the UFO
    /// and Cancel becomes "Go to last known UFO position", which sends the craft to a new waypoint there.
    /// </param>
    public static GeoscapePopup Craft(CampaignWorldCraft craft, CampaignWorldOverview world, RuntimeRuleCatalog? rules,
        Func<string, string> tr, int etaMode = 0, GeoscapeLostUfo? lost = null)
    {
        ArgumentNullException.ThrowIfNull(craft);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tr);
        var key = Key(craft);
        var status = lost is null ? CraftStatus(craft, world, tr) : Format(tr("STR_INTERCEPTING_UFO"), lost.UfoId);
        var lines = new List<string>
        {
            Format(tr("STR_STATUS_"), status),
            Format(tr("STR_BASE_UC"), BaseName(world, craft.BaseId)),
            Format(tr("STR_SPEED_"), Number(craft.Speed)) + "  " + Format(tr("STR_MAXIMUM_SPEED_UC"), Number(craft.SpeedMaximum)),
            Format(tr("STR_FUEL"), Percent(Percentage(craft.Fuel, craft.FuelMaximum))) + "  " +
                Format(tr("STR_DAMAGE_UC_"), Percent(Percentage(craft.Damage, craft.DamageMaximum))),
        };
        if (craft.ShieldMaximum != 0)
            lines.Add(Format(tr("STR_SHIELD"), Percent(craft.Shield * 100 / craft.ShieldMaximum)));
        var rule = rules is not null && rules.Crafts.TryGet(craft.TypeId, out var handle) ? rules.Crafts[handle].Value : null;
        var slotLabels = rule?.WeaponStrings ?? [];
        for (var slot = 0; slot < craft.Weapons.Count; slot++)
        {
            var label = slot < slotLabels.Count ? slotLabels[slot] : "";
            if (label.Length == 0) continue;
            var weapon = craft.Weapons[slot];
            var text = Format(tr(label), weapon is null ? tr("STR_NONE_UC") : tr(weapon.RuleId));
            if (weapon is { AmmoMaximum: > 0 }) text += "  " + Format(tr("STR_ROUNDS_"), weapon.Ammo);
            lines.Add(text);
        }
        // The crew lines are hidden for craft that cannot carry soldiers or vehicles.
        var crew = new List<string>(2);
        if (rule is not { EffectiveMaximumUnits: 0 }) crew.Add(tr("STR_SOLDIERS_UC") + " " + Number(craft.Soldiers));
        if (rule is not { EffectiveMaximumVehiclesAndLargeSoldiers: 0 }) crew.Add(tr("STR_HWPS") + " " + Number(craft.Vehicles));
        if (crew.Count != 0) lines.Add(string.Join("  ", crew));
        if (lost is null && craft.Destination is { } destination && Eta(craft, destination, world, tr, etaMode) is { } eta)
            lines.Add(eta);
        var actions = new List<GeoscapePopupAction>();
        // GeoscapeCraftState hides its commands for a craft returning on low fuel or after its mission.
        if (!craft.LowFuel && !craft.MissionComplete)
        {
            var refused = craft.PatrolRecallUnavailable?.Text;
            actions.Add(new(tr("STR_RETURN_TO_BASE"))
            {
                Command = new RecallCraft(craft.BaseId, craft.TypeId, craft.Id),
                Disabled = refused,
            });
            actions.Add(new(tr("STR_SELECT_NEW_TARGET")) { Intent = new SelectDestinationIntent([key]), Disabled = refused });
            actions.Add(new(tr(craft.CanAutoPatrol ? "STR_AUTO_PATROL" : "STR_PATROL"))
            {
                Command = new PatrolCraft(craft.BaseId, craft.TypeId, craft.Id),
                Disabled = refused,
            });
        }
        actions.Add(lost is null
            ? new(tr("STR_CANCEL_UC"), CancelKey: true)
            : new(tr("STR_GO_TO_LAST_KNOWN_UFO_POSITION"), CancelKey: true)
            {
                Command = new DispatchCraftToWaypoint(craft.BaseId, craft.TypeId, craft.Id, lost.Position.Longitude,
                    lost.Position.Latitude),
            });
        return new(CraftName(craft, tr), lines, actions);
    }

    /// <summary>
    /// UfoDetectedState: the UFO's size, altitude, heading and speed, then with hyper-wave decoding
    /// its craft type, race, mission and zone. Intercept and Centre reset the speed and centre on it;
    /// Ignore (the reference's Ctrl+Cancel) stops further alerts for this contact until reload.
    /// </summary>
    public static GeoscapePopup Ufo(CampaignWorldTarget ufo, bool detected, bool hyperwave, Func<string, string> tr)
    {
        ArgumentNullException.ThrowIfNull(ufo);
        ArgumentNullException.ThrowIfNull(tr);
        var lines = new List<string>();
        if (detected) lines.Add(tr("STR_DETECTED"));
        var details = ufo.Ufo;
        if (details is not null)
        {
            lines.Add(Row(tr("STR_SIZE_UC"), tr(details.Size)));
            lines.Add(Row(tr("STR_ALTITUDE"), tr(ufo.Altitude == "STR_GROUND" ? "STR_GROUNDED" : ufo.Altitude)));
            lines.Add(Row(tr("STR_HEADING"), tr(details.Heading)));
            lines.Add(Row(tr("STR_SPEED"), Number(details.Speed)));
        }
        if (hyperwave)
        {
            lines.Add(tr("STR_HYPER_WAVE_TRANSMISSIONS_ARE_DECODED"));
            if (details is not null)
            {
                lines.Add(Row(tr("STR_CRAFT_TYPE"), tr(ufo.TypeId)));
                lines.Add(Row(tr("STR_RACE"), tr(details.Race)));
                lines.Add(Row(tr("STR_MISSION"), tr(details.Mission)));
                lines.Add(Row(tr("STR_ZONE"), tr(details.Region)));
            }
        }
        var position = new WorldPosition(ufo.Longitude, ufo.Latitude);
        var actions = new List<GeoscapePopupAction>
        {
            new(tr("STR_INTERCEPT"), true, position) { Intent = new InterceptIntent(ufo.Reference) },
            new(tr("STR_CENTER_ON_UFO_TIME_5_SECONDS"), true, position),
            new(tr("STR_CANCEL_UC"), CancelKey: true),
        };
        // IgnoreUfoContact accepts flying and landed contacts; a crash site has no alerts left to ignore.
        if (!ufo.DetectionAlertIgnored && ufo.Reference.UniqueId > 0 && ufo.Status != nameof(UfoStatus.Crashed))
            actions.Add(new(tr("STR_IGNORE_UC")) { Command = new IgnoreUfoContact(ufo.Reference.UniqueId, ufo.Reference.Id) });
        return new(TargetLabel(ufo, tr), lines, actions);
    }

    /// <summary>
    /// InterceptState: every craft of every base with its status, base and weapons/soldiers/vehicles.
    /// Rows that cannot start are listed but refuse; Shift+click builds a wing of up to four.
    /// </summary>
    public static GeoscapePopup Intercept(CampaignWorldOverview world, IReadOnlyCollection<CraftKey> wing,
        WorldTargetReference? target, int page, Func<string, string> tr, int? baseId = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(wing);
        ArgumentNullException.ThrowIfNull(tr);
        // InterceptState opened from a base lists only that base's craft.
        var crafts = baseId is { } only ? world.Crafts.Where(craft => craft.BaseId == only).ToArray() : world.Crafts;
        var pages = Math.Max(1, (crafts.Count + InterceptPageSize - 1) / InterceptPageSize);
        page = Math.Clamp(page, 0, pages - 1);
        var lines = new List<string>();
        if (target is not null) lines.Add(Format(tr("STR_TARGET"), TargetName(world, target, tr)));
        if (crafts.Count == 0) lines.Add(tr("STR_NO_CRAFT"));
        else if (wing.Count != 0) lines.Add("Shift+click adds or removes wing members; click the leader to launch.");
        var actions = new List<GeoscapePopupAction>();
        foreach (var craft in crafts.Skip(page * InterceptPageSize).Take(InterceptPageSize))
        {
            var key = Key(craft);
            var marker = wing.Contains(key) ? "* " : "";
            var crew = string.Create(CultureInfo.InvariantCulture,
                $"{craft.Weapons.Count(weapon => weapon is not null)}/{craft.Soldiers}/{craft.Vehicles}");
            actions.Add(new($"{marker}{CraftName(craft, tr)}  {InterceptStatus(craft, world, tr)}  {BaseName(world, craft.BaseId)}  {crew}")
            {
                Intent = new InterceptRowIntent(key),
                Disabled = CanStart(craft) ? null : craft.DispatchUnavailable?.Text ?? tr(craft.Status),
            });
        }
        if (pages > 1)
            actions.Add(new(string.Create(CultureInfo.InvariantCulture, $"More... ({page + 1}/{pages})"))
            {
                Intent = new InterceptPageIntent((page + 1) % pages),
            });
        // InterceptState opened from a base offers that base's screens.
        if (baseId is { } owner) actions.Add(new(tr("STR_GO_TO_BASE")) { Intent = new BaseIntent(owner) });
        actions.Add(new(tr("STR_CANCEL_UC"), CancelKey: true));
        return new(tr("STR_LAUNCH_INTERCEPTION"), lines, actions);
    }

    /// <summary>ConfirmDestinationState: the target, the leader's ETA and the follow-leader toggle.</summary>
    public static GeoscapePopup Confirm(CampaignWorldOverview world, IReadOnlyList<CraftKey> wing,
        GeoscapeDestination destination, bool followLeader, Func<string, string> tr, int etaMode = 0)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(wing);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(tr);
        if (wing.Count == 0) throw new ArgumentException("A wing needs a leader.", nameof(wing));
        var lines = new List<string>();
        var leader = world.Crafts.FirstOrDefault(craft => Key(craft) == wing[0]);
        if (leader is not null && EtaTo(leader, destination.Target, destination.Position, world, tr, etaMode) is { } eta)
            lines.Add(eta);
        var actions = new List<GeoscapePopupAction>
        {
            new(tr("STR_OK"), OkKey: true)
            {
                Command = new DispatchCraftWing(wing.ToArray(), destination.Target, destination.Waypoint, followLeader),
            },
            new(tr("STR_CANCEL_UC"), CancelKey: true),
        };
        if (wing.Count > 1)
            actions.Insert(1, new((followLeader ? "[x] " : "[ ] ") + tr("STR_FOLLOW_WING_LEADER_QUESTION"))
            {
                Intent = new FollowLeaderIntent(),
            });
        return new(Format(tr("STR_TARGET"), destination.Label), lines, actions);
    }

    /// <summary>
    /// ConfirmDestinationState's default: the wing follows its leader unless it is going after a
    /// flying UFO, when every craft heads straight for it.
    /// </summary>
    public static bool DefaultFollowLeader(CampaignWorldOverview world, WorldTargetReference? target)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (target is not { Kind: WorldTargetKind.Ufo }) return true;
        var ufo = world.Targets.FirstOrDefault(entry => entry.Kind == WorldTargetKind.Ufo &&
            entry.Reference.UniqueId == target.UniqueId);
        return ufo is null || ufo.Altitude == "STR_GROUND";
    }

    /// <summary>TargetInfoState for a UFO, mission site or alien base: Intercept, then OK.</summary>
    public static GeoscapePopup TargetInfo(CampaignWorldTarget target, Func<string, string> tr)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tr);
        var lines = new List<string>();
        if (target.Status.Length != 0) lines.Add(target.Status);
        if (target.Altitude.Length != 0) lines.Add(tr(target.Altitude));
        if (target.UnavailableActionReason is { } reason) lines.Add(reason);
        return new(TargetLabel(target, tr), lines,
        [
            new(tr("STR_INTERCEPT")) { Intent = new InterceptIntent(target.Reference) },
            new(tr("STR_OK"), CancelKey: true),
        ]);
    }

    /// <summary>
    /// GeoscapeCraftState/ConfirmDestinationState ETA: whole hours at maximum speed, shown as
    /// Options::oxceShowETAMode asks (0, the default: never; 1: not for moving targets; 2: always).
    /// </summary>
    public static string? Eta(CampaignWorldCraft craft, WorldTargetReference destination, CampaignWorldOverview world,
        Func<string, string> tr, int etaMode) =>
        EtaTo(craft, destination, new WorldPosition(destination.Longitude, destination.Latitude), world, tr, etaMode);

    private static string? EtaTo(CampaignWorldCraft craft, WorldTargetReference? target, WorldPosition position,
        CampaignWorldOverview world, Func<string, string> tr, int etaMode)
    {
        if (etaMode <= 0) return null;
        if (etaMode == 1 && IsMoving(target, world)) return null;
        if (craft.SpeedMaximum <= 0) return null;
        var radians = WorldGeometry.Distance(new WorldPosition(craft.Longitude, craft.Latitude), position);
        var distance = (long)(radians * 60.0 * (180.0 / Math.PI));
        var hoursTotal = (distance + craft.SpeedMaximum / 2) / craft.SpeedMaximum;
        var days = hoursTotal / 24;
        var hours = hoursTotal % 24;
        var text = days > 0 ? Format(tr("STR_DAY_SHORT"), days) : "";
        if (hours > 0 || days == 0) text += (days > 0 ? "/" : "") + Format(tr("STR_HOUR_SHORT"), hours);
        return Format(tr("STR_ETA"), text);
    }

    /// <summary>A moving target (MovingTarget with a speed): an airborne UFO or a craft in flight.</summary>
    private static bool IsMoving(WorldTargetReference? target, CampaignWorldOverview world) => target?.Kind switch
    {
        WorldTargetKind.Craft => world.Crafts.FirstOrDefault(craft => craft.TypeId == target.TypeId && craft.Id == target.Id)
            is { Speed: > 0 },
        WorldTargetKind.Ufo => world.Targets.FirstOrDefault(entry => entry.Kind == WorldTargetKind.Ufo &&
            entry.Reference.UniqueId == target.UniqueId) is { Altitude: not "STR_GROUND" },
        _ => false,
    };

    /// <summary>GeoscapeCraftState status text.</summary>
    public static string CraftStatus(CampaignWorldCraft craft, CampaignWorldOverview world, Func<string, string> tr)
    {
        if (craft.Status != "STR_OUT") return tr(craft.Status);
        if (craft.LowFuel) return tr("STR_LOW_FUEL_RETURNING_TO_BASE");
        if (craft.MissionComplete) return tr("STR_MISSION_COMPLETE_RETURNING_TO_BASE");
        switch (craft.Destination)
        {
            case null:
                return tr("STR_PATROLLING");
            case { Kind: WorldTargetKind.Base } home when home.Id == craft.BaseId:
                return tr("STR_RETURNING_TO_BASE");
            case { Kind: WorldTargetKind.Ufo } ufo:
                var target = world.Targets.FirstOrDefault(entry => entry.Kind == WorldTargetKind.Ufo &&
                    entry.Reference.UniqueId == ufo.UniqueId);
                return target is { Altitude: not "STR_GROUND" }
                    ? Format(tr("STR_INTERCEPTING_UFO"), target.Id)
                    : Format(tr("STR_DESTINATION_UC_"), TargetName(world, ufo, tr));
            default:
                return Format(tr("STR_DESTINATION_UC_"), TargetName(world, craft.Destination, tr));
        }
    }

    /// <summary>InterceptState's shorter status column.</summary>
    private static string InterceptStatus(CampaignWorldCraft craft, CampaignWorldOverview world, Func<string, string> tr)
    {
        if (craft.Status != "STR_OUT")
            return craft.Status == "STR_READY" && craft.DispatchUnavailable?.Code == "craft.no-pilots"
                ? tr("STR_PILOT_MISSING")
                : tr(craft.Status);
        if (craft.Destination is null) return tr("STR_PATROLLING");
        if (craft.LowFuel || craft.MissionComplete ||
            craft.Destination is { Kind: WorldTargetKind.Base } home && home.Id == craft.BaseId)
            return tr("STR_RETURNING");
        return craft.Destination.Kind switch
        {
            WorldTargetKind.Ufo => world.Targets.FirstOrDefault(entry => entry.Kind == WorldTargetKind.Ufo &&
                entry.Reference.UniqueId == craft.Destination.UniqueId) is { Altitude: not "STR_GROUND" }
                ? tr("STR_INTERCEPTING")
                : tr("STR_EN_ROUTE"),
            WorldTargetKind.Craft => tr("STR_ESCORTING"),
            WorldTargetKind.MissionSite or WorldTargetKind.AlienBase => tr("STR_EN_ROUTE"),
            _ => tr(craft.Status),
        };
    }

    /// <summary>Target::getName: a custom name, or the marker's language key with its id.</summary>
    public static string TargetLabel(CampaignWorldTarget target, Func<string, string> tr)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tr);
        return target.HasCustomName ? target.Label : Format(tr(target.Label), target.Id);
    }

    public static string TargetName(CampaignWorldOverview world, WorldTargetReference target, Func<string, string> tr)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(target);
        switch (target.Kind)
        {
            case WorldTargetKind.Base:
                return BaseName(world, target.Id);
            case WorldTargetKind.Craft:
                return world.Crafts.FirstOrDefault(craft => craft.TypeId == target.TypeId && craft.Id == target.Id) is { } craft
                    ? CraftName(craft, tr)
                    : tr(target.TypeId) + "-" + target.Id.ToString(CultureInfo.InvariantCulture);
            case WorldTargetKind.Ufo:
                return world.Targets.FirstOrDefault(entry => entry.Kind == WorldTargetKind.Ufo &&
                    entry.Reference.UniqueId == target.UniqueId) is { } ufo ? TargetLabel(ufo, tr) : tr(target.TypeId);
            default:
                // Site and alien base ids are counted per marker, so the marker type must match too.
                return world.Targets.FirstOrDefault(entry => entry.Kind == target.Kind && entry.Id == target.Id &&
                        string.Equals(entry.Reference.TypeId, target.TypeId, StringComparison.Ordinal)) is { } found
                    ? TargetLabel(found, tr)
                    : Format(tr(target.Kind is WorldTargetKind.AlienBase or WorldTargetKind.Waypoint
                        ? target.TypeId + "_" : target.TypeId), target.Id);
        }
    }

    private static string BaseName(CampaignWorldOverview world, int baseId) =>
        world.Bases.FirstOrDefault(owner => owner.Id == baseId)?.Name ?? "#" + baseId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Craft::getFuelPercentage and getDamagePercentage: floor of the share, 0 for no maximum.</summary>
    private static int Percentage(int value, int maximum) =>
        maximum <= 0 ? 0 : (int)Math.Floor((double)value / maximum * 100.0);

    private static string Percent(int value) => value.ToString(CultureInfo.InvariantCulture) + "%";

    private static string Row(string label, string value) => label + " . . " + value;

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Format(string text, params object[] arguments) => GeoscapeNotifications.Format(text, arguments);
}
