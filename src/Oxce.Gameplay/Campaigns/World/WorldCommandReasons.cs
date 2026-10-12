using System.Collections.Frozen;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// Codes for the refusals of world craft commands and of the guarded target actions. Reasons the
/// reference shows to the player carry its language key; port guards keep their English text.
/// </summary>
public static class WorldCommandReasons
{
    internal const string Unresolved = "Craft dispatch requires resolved craft state.";
    internal const string BaseUnplaced = "A craft cannot depart from an unplaced base.";
    internal const string NotReady = "The craft is not ready to depart.";
    internal const string Returning = "The craft is returning to base.";
    internal const string SpeedRange = "Craft speed exceeds the supported range.";
    internal const string ArmorForbidden = "The craft carries armor forbidden by its rules.";
    internal const string TooManyItems = "The craft carries too many items.";
    internal const string NoPilots = "The craft does not have enough pilots.";
    internal const string NotAirborne = "The craft is not airborne.";
    internal const string WingSize = "A wing has one to four distinct craft.";
    internal const string WaypointIds = "Waypoint IDs are exhausted or collide with a saved waypoint.";
    internal const string TargetKind = "The destination kind is invalid.";
    internal const string UfoIdentity = "UFO dispatch requires its stable unique identity.";
    internal const string TargetGone = "The destination no longer exists or is not visible.";
    internal const string TargetSelf = "A craft cannot escort itself.";
    internal const string ForeignBase = "A craft can return only to its own base; use a transfer to change bases.";
    internal const string InterceptionBoundary = "Craft interception requires strategic combat (Phase 6 branch 5).";
    internal const string LandingBoundary = "Craft landing requires deployment (Phase 6 branch 5).";
    internal const string NoInterception = "There is no such interception.";
    internal const string InterceptionsOpen = "Open interception windows stop the clock until they are minimized.";
    internal const string InterceptionMode = "The interception does not offer that mode.";
    internal const string LandingConfirmationsOpen = "Landing confirmations stop the clock until they are answered.";
    internal const string NoLandingConfirmation = "That landing confirmation is not the one shown.";
    internal const string ContactGone = "The UFO contact no longer exists or is not visible.";
    internal const string ContactLimit = "Ignored UFO contacts exceed the supported limit.";

    private static readonly FrozenDictionary<string, CampaignReason> Known = new CampaignReason[]
    {
        new("craft.unresolved", Unresolved),
        new("craft.base-unplaced", BaseUnplaced),
        new("craft.not-ready", NotReady),
        new("craft.returning", Returning),
        new("craft.speed-range", SpeedRange),
        new("craft.armor-forbidden", ArmorForbidden) { TextKey = "STR_ARMOR_NOT_ALLOWED_ONBOARD" },
        new("craft.too-many-items", TooManyItems) { TextKey = "STR_TOO_MANY_ITEMS_ONBOARD" },
        new("craft.no-pilots", NoPilots) { TextKey = "STR_NOT_ENOUGH_PILOTS" },
        new("craft.not-airborne", NotAirborne),
        new("wing.size", WingSize),
        new("waypoint.ids", WaypointIds),
        new("target.kind", TargetKind),
        new("target.ufo-identity", UfoIdentity),
        new("target.gone", TargetGone),
        new("target.self", TargetSelf),
        new("target.foreign-base", ForeignBase),
        new("boundary.interception", InterceptionBoundary),
        new("boundary.landing", LandingBoundary),
        new("interception.none", NoInterception),
        new("interception.open", InterceptionsOpen),
        new("interception.mode", InterceptionMode),
        new("landing.open", LandingConfirmationsOpen),
        new("landing.none", NoLandingConfirmation),
        new("contact.gone", ContactGone),
        new("contact.limit", ContactLimit),
    }.ToFrozenDictionary(static reason => reason.Text, StringComparer.Ordinal);

    /// <summary>The classified reason for a refusal text, or an unclassified one.</summary>
    public static CampaignReason For(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Known.TryGetValue(text, out var reason) ? reason : new CampaignReason(CampaignReason.Unclassified, text);
    }

    public static IReadOnlyCollection<CampaignReason> All => Known.Values;
}
