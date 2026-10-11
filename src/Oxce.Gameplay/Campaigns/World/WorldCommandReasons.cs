using System.Collections.Frozen;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// Codes for the refusals of world craft commands and of the guarded target actions. Reasons the
/// reference shows to the player carry its language key; port guards keep their English text.
/// </summary>
public static class WorldCommandReasons
{
    private static readonly FrozenDictionary<string, CampaignReason> Known = new CampaignReason[]
    {
        new("craft.unresolved", "Craft dispatch requires resolved craft state."),
        new("craft.base-unplaced", "A craft cannot depart from an unplaced base."),
        new("craft.not-ready", "The craft is not ready to depart."),
        new("craft.returning", "The craft is returning to base."),
        new("craft.speed-range", "Craft speed exceeds the supported range."),
        new("craft.armor-forbidden", "The craft carries armor forbidden by its rules.") { TextKey = "STR_ARMOR_NOT_ALLOWED_ONBOARD" },
        new("craft.too-many-items", "The craft carries too many items.") { TextKey = "STR_TOO_MANY_ITEMS_ONBOARD" },
        new("craft.no-pilots", "The craft does not have enough pilots.") { TextKey = "STR_NOT_ENOUGH_PILOTS" },
        new("craft.not-airborne", "The craft is not airborne."),
        new("wing.size", "A wing has one to four distinct craft."),
        new("waypoint.ids", "Waypoint IDs are exhausted or collide with a saved waypoint."),
        new("target.kind", "The destination kind is invalid."),
        new("target.ufo-identity", "UFO dispatch requires its stable unique identity."),
        new("target.gone", "The destination no longer exists or is not visible."),
        new("target.self", "A craft cannot escort itself."),
        new("target.foreign-base", "A craft can return only to its own base; use a transfer to change bases."),
        new("boundary.starting-condition", "Target starting conditions require deployment validation (Phase 6 branch 5)."),
        new("boundary.interception", "Craft interception requires strategic combat (Phase 6 branch 5)."),
        new("boundary.landing", "Craft landing requires deployment (Phase 6 branch 5)."),
    }.ToFrozenDictionary(static reason => reason.Text, StringComparer.Ordinal);

    /// <summary>The classified reason for a refusal text, or an unclassified one.</summary>
    public static CampaignReason For(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Known.TryGetValue(text, out var reason) ? reason : new CampaignReason(CampaignReason.Unclassified, text);
    }

    public static IReadOnlyCollection<CampaignReason> All => Known.Values;
}
