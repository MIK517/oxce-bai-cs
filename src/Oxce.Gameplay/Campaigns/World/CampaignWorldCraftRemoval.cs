using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal interface ICampaignCraftRemoval
{
    string? PlanReason(IReadOnlyList<(string TypeId, int Id)> targets);
    void Removed(string typeId, int id, CampaignState.TimeEffects? effects);
    string? ForecastFollowers(IReadOnlyDictionary<int, List<CampaignState.CraftState>> crafts,
        string typeId, int id, ref long work);
}

internal sealed partial class CampaignWorld : ICampaignCraftRemoval
{
    private void RegisterCraftRemoval(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignCraftRemoval>(this);
        registry.Preflight(CampaignPreflightOrder.ProductionCraftTargets, "craft target removal", (_, highest, _) =>
        {
            long work = 0;
            if (campaign.ProductionPreflight.CraftRemovalWork(_ufos.Count, highest, ref work, out var types) is { } reason) return reason;
            if (types.Count == 0) return null;
            List<(string TypeId, int Id)> targets = [];
            foreach (var owner in campaign.BaseStates)
            {
                foreach (var craft in owner.Crafts)
                {
                    var type = campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule);
                    if (types.Contains(type)) targets.Add((type, craft.Id));
                }
                foreach (var transfer in owner.Transfers)
                    if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Craft is { } craft && types.Contains(craft.RuleId))
                        targets.Add((craft.RuleId, craft.Id));
            }
            return ((ICampaignCraftRemoval)this).PlanReason(targets);
        });
    }

    string? ICampaignCraftRemoval.PlanReason(IReadOnlyList<(string TypeId, int Id)> targets)
    {
        if (targets.Count == 0) return null;
        long work = targets.Count + (long)_ufos.Count;
        foreach (var owner in campaign.BaseStates) work += owner.Crafts.Count + (long)owner.Transfers.Count;
        if (work > MaximumAlienBaseChecks) return "Craft target removal exceeds the supported work limit.";
        var keys = targets.ToHashSet();
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var craft in owner.Crafts)
                if (craft.Logistics is { Destination: { Kind: WorldTargetKind.Craft } target } state && keys.Contains((target.TypeId, target.Id)) &&
                    RemovedFollowerReason(owner, campaign.Content.RuntimeRules.Crafts[craft.Rule].Value, state) is { } reason)
                    return reason;
            foreach (var transfer in owner.Transfers)
                if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Craft is { Logistics: { Destination: { Kind: WorldTargetKind.Craft } target } state } arriving &&
                    keys.Contains((target.TypeId, target.Id)) && RemovedFollowerReason(owner,
                        campaign.Content.RuntimeRules.Crafts[campaign.Content.RuntimeRules.Crafts.GetRequired(arriving.RuleId)].Value, state) is { } reason)
                    return reason;
        }
        foreach (var ufo in _ufos)
            if (ufo.Destination is { Kind: WorldTargetKind.Craft } target && keys.Contains((target.TypeId, target.Id)) &&
                (ufo.Hunting || ufo.Escorting) && ufo.OriginalWaypoint is not { IsNormalized: true })
                return HuntStateReason;
        return null;
    }

    void ICampaignCraftRemoval.Removed(string typeId, int id, CampaignState.TimeEffects? effects)
    {
        foreach (var owner in campaign.BaseStates)
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { Destination: { Kind: WorldTargetKind.Craft } target } state ||
                    target.TypeId != typeId || target.Id != id) continue;
                var returned = ReturnRemovedCraftFollower(owner, craft, state);
                owner.Crafts[index] = craft with { Logistics = returned };
                effects?.Notify(new CraftTargetLost(owner.Id, campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id, target, true));
            }
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Destination is { Kind: WorldTargetKind.Craft } target && target.TypeId == typeId && target.Id == id &&
                (ufo.Hunting || ufo.Escorting)) _ufos[index] = WorldHunting.Restore(ufo);
        }
    }

    string? ICampaignCraftRemoval.ForecastFollowers(IReadOnlyDictionary<int, List<CampaignState.CraftState>> crafts,
        string typeId, int id, ref long work)
    {
        foreach (var owner in campaign.BaseStates)
        {
            var sources = crafts[owner.Id];
            work += sources.Count;
            if (work > MaximumAlienBaseChecks) return "Craft target removal exceeds the supported work limit.";
            for (var index = 0; index < sources.Count; index++)
            {
                var craft = sources[index];
                if (craft.Logistics is not { Destination: { Kind: WorldTargetKind.Craft } target } state ||
                    target.TypeId != typeId || target.Id != id) continue;
                if (!owner.IsPlaced || !new WorldPosition(owner.Longitude, owner.Latitude).IsNormalized)
                    return "Craft target removal requires placed follower bases.";
                if (!CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                    state.Weapons, campaign.Content.RuntimeRules, out _)) return SpeedRangeReason;
                sources[index] = craft with { Logistics = ReturnRemovedCraftFollower(owner, craft, state) };
            }
        }
        return null;
    }

    private string? RemovedFollowerReason(CampaignState.BaseState owner, RuntimeCraftRule rule, CraftLogisticsState state)
    {
        if (!owner.IsPlaced || !new WorldPosition(owner.Longitude, owner.Latitude).IsNormalized)
            return "Craft target removal requires placed follower bases.";
        return CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, campaign.Content.RuntimeRules, out _) ? null : SpeedRangeReason;
    }

    private CraftLogisticsState ReturnRemovedCraftFollower(CampaignState.BaseState owner, CampaignState.CraftState craft, CraftLogisticsState state)
    {
        if (!CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
            state.Weapons, campaign.Content.RuntimeRules, out var maximum))
            throw new InvalidOperationException(SpeedRangeReason);
        return SetDestination(state, maximum, BaseReference(owner)) with { Status = state.Status };
    }
}
