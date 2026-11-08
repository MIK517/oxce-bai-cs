using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>Helpers the battle movement tests share: rule text edits, command results, the walk's last event and a loaded gun.</summary>
internal static class BattleTestSupport
{
    /// <summary>The rules text with <paramref name="from"/> replaced, failing when it does not occur (so a stale edit cannot pass silently).</summary>
    public static string Replaced(string rules, string from, string to)
    {
        var result = rules.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(rules, result);
        return result;
    }

    /// <summary>The code of a command's refusal, which must be its only event.</summary>
    public static string Refusal(CampaignCommandResult result) => Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Code;

    /// <summary>The events of a command that was not refused.</summary>
    public static IReadOnlyList<ICampaignEvent> Events(CampaignCommandResult result)
    {
        Assert.DoesNotContain(result.Events, static @event => @event is CampaignActionBlocked);
        return result.Events;
    }

    /// <summary>The <see cref="UnitWalkEnded"/> a walk's events end with.</summary>
    public static UnitWalkEnded End(IReadOnlyList<ICampaignEvent> events) => Assert.IsType<UnitWalkEnded>(events[^1]);

    /// <summary>A GUN in the unit's right hand, loaded with a CLIP of five rounds.</summary>
    public static BattleItem GunFor(BattleState battle, BattleUnit unit)
    {
        var gun = new BattleItem(battle.TakeItemId(), "GUN") { OwnerUnitId = unit.Id, Slot = "STR_RIGHT_HAND", IsWeaponWithAmmo = true };
        battle.AddItem(gun);
        var clip = new BattleItem(battle.TakeItemId(), "CLIP") { AmmoQuantity = 5, IsAmmo = true };
        battle.AddItem(clip);
        gun.AmmoSlots[0] = clip.Id;
        return gun;
    }
}
