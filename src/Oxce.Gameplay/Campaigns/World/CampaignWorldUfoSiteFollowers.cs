using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private static bool FollowsUfo(WorldTargetReference? target, UfoSnapshot ufo) =>
        target is { Kind: WorldTargetKind.Ufo } reference &&
        (reference.UniqueId > 0 ? reference.UniqueId == ufo.UniqueId : reference.Id == ufo.Id);

    // Craft::getNumTotalUnits counts assigned soldiers plus vehicle instances.
    // AllowLanding and the equipment option belong to the later arrival caller.
    private bool CraftHasUnits(CampaignState.BaseState owner, CampaignState.CraftState craft, CraftLogisticsState state)
    {
        if (state.Vehicles.Count != 0) return true;
        var type = campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule);
        return owner.Soldiers.Any(soldier => soldier.Personal is { } personal &&
            personal.CraftType == type && personal.CraftId == craft.Id);
    }

    private string? UfoSiteFollowersReason(UfoSnapshot ufo, RuntimeAlienMissionRule mission, MissionArea area,
        CampaignTimeTrigger highest, ref long work)
    {
        if (!TrySpawnArea(area, out var positions)) return "UFO site conversion coordinates require an unsupported latitude domain.";
        foreach (var owner in campaign.BaseStates)
            foreach (var craft in owner.Crafts)
            {
                work += 1L + owner.Soldiers.Count;
                if (work > MaximumAlienBaseChecks) return "UFO site conversion exceeds the supported work limit.";
                if (craft.Logistics is not { } state || !FollowsUfo(state.Destination, ufo)) continue;
                if (state.Status != "STR_OUT" || !owner.IsPlaced)
                    return "UFO site follower craft cannot change destination.";
                if (!CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                        state.Weapons, campaign.Content.RuntimeRules, out var maximum)) return SpeedRangeReason;
                if (highest >= CampaignTimeTrigger.TenMinutes)
                    state = AdvanceCraftFuel(owner, craft, state);
                if (!FollowsUfo(state.Destination, ufo)) continue;
                if (!mission.RespawnUfoAfterSiteDespawn || !CraftHasUnits(owner, craft, state) || !CanLand(owner, craft, state)) continue;
                // Conversion resets destination speed before the craft movement pass.
                // The spherical rectangle radius bounds every possible random site.
                var from = new WorldPosition(state.Longitude, state.Latitude);
                var step = state.Takeoff > 0 ? 0 : WorldGeometry.RadianSpeed(maximum);
                if (WorldGeometry.Distance(from, positions.Center) <= positions.Radius + step)
                    return LandingReason;
            }
        return null;
    }

    private void TransferUfoSiteFollowers(UfoSnapshot ufo, MissionSiteSnapshot site, bool dormant)
    {
        var rules = campaign.Content.RuntimeRules;
        var target = new WorldTargetReference(WorldTargetKind.MissionSite, MarkerName(site.DeploymentId),
            site.Id, site.Longitude, site.Latitude);
        foreach (var owner in campaign.BaseStates)
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { } state || !FollowsUfo(state.Destination, ufo)) continue;
                var units = CraftHasUnits(owner, craft, state);
                if (!units && !dormant) continue;
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rules.Crafts[craft.Rule].Value,
                        state.Weapons, rules, out var maximum))
                    throw new InvalidOperationException("UFO site follower speed changed after world preflight.");
                owner.Crafts[index] = craft with
                { Logistics = SetDestination(state, maximum, units ? target : BaseReference(owner)) };
            }
    }
}
