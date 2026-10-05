using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    string? ICampaignProductionPreflight.ForecastStocks(
        IReadOnlyDictionary<int, List<CraftState>> crafts,
        IReadOnlyDictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stocks,
        int? nextResearchTransferId, bool uncertainResearch, ref long work)
    {
        var rules = _content.RuntimeRules;
        List<(BaseState Owner, ProductionState Project)> planned = [];
        long transfers = 0;
        foreach (var owner in _bases)
        {
            var sources = crafts[owner.Id];
            foreach (var project in owner.Productions)
            {
                if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                var rule = rules.Manufacture[project.Rule].Value;
                if ((long)project.Spent + project.Assigned > int.MaxValue) return "Production progress exceeds the supported range.";
                if (ProductionRuleInvalid(rule) is { } invalid) return invalid;
                var requested = ProductionProgress.Requested(rule.Time, project.Spent, project.Assigned, project.Amount, project.Infinite);
                if (!project.IsFallback && requested == 0) continue;
                work += rule.RequiredMaterials.Count * (long)sources.Count + rule.ProducedMaterials.Count + sources.Count;
                if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                // The final unit was paid and its inputs consumed when it started.
                // No subsequent start can change funds, materials or craft in this hour.
                var final = !project.IsFallback && !project.Infinite && requested == 1 && Produced(project) + 1L >= project.Amount;
                var supported = final && !project.Sell && rule.RandomProducedItems.Count == 0 && rule.SpawnedPersonType.Length == 0 &&
                    rule.ProducedMaterials.All(material => material.Craft is null);
                if (!supported)
                {
                    if (rule.RequiredMaterials.Any(material => material.Quantity > 0 && material.Craft is { } type &&
                        sources.Any(craft => craft.Rule == type && craft.Logistics?.Status != "STR_OUT")))
                        return "UFO retargeting requires production craft-consumption forecasts.";
                    if (sources.Any(craft => rules.Crafts[craft.Rule].Value.RefuelItem is not null))
                        return "UFO retargeting requires production fuel-stock forecasts.";
                    if (sources.Any(craft => craft.Logistics is { Status: "STR_REFUELLING" or "STR_READY" } state &&
                        state.Weapons.Any(weapon => weapon is { Disabled: false } &&
                            rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value is { Clip.Length: > 0 } weaponRule &&
                            weapon.Ammo < weaponRule.AmmoMaximum)))
                        return "UFO retargeting requires same-tick craft ammunition reuse forecasts.";
                    continue;
                }
                planned.Add((owner, project));
                if (!project.Sell && rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0)
                    transfers += rule.ProducedMaterials.Count(material => material.Item is not null);
            }
        }
        if (transfers != 0)
        {
            if (uncertainResearch) return "Production arrivals require daily research transfer-identity forecasts.";
            long next = nextResearchTransferId ?? _nextIds.GetValueOrDefault("oxcePortTransfer", 1);
            foreach (var owner in _bases)
            {
                work += owner.Transfers.Count;
                if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                var removesDelivered = owner.Transfers.Any(transfer => !transfer.Delivered && transfer.Hours <= 1);
                foreach (var transfer in owner.Transfers)
                {
                    if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    if (removesDelivered && (transfer.Delivered || transfer.Hours <= 1)) continue;
                    next = Math.Max(next, transfer.Id == int.MaxValue ? int.MaxValue : (long)transfer.Id + 1);
                }
            }
            if (next + transfers > int.MaxValue) return "Production arrivals exhaust the transfer identity range.";
        }
        foreach (var (owner, project) in planned)
        {
            var rule = rules.Manufacture[project.Rule].Value;
            if (project.Sell || rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0) continue;
            foreach (var material in rule.ProducedMaterials)
            {
                if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                if (material.Item is not { } item) continue;
                var quantity = (long)stocks[owner.Id].GetValueOrDefault(item) + material.Quantity;
                if (quantity > int.MaxValue) return "Production outputs exceed the supported stock range.";
                stocks[owner.Id][item] = (int)quantity;
                if (rules.Items[item].Value.BattleType != 0) continue;
                var sources = crafts[owner.Id];
                work += sources.Count;
                if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                for (var index = 0; index < sources.Count; index++)
                {
                    var craft = sources[index];
                    var state = CraftServicing.ReuseItem(craft.Logistics!, rules.Crafts[craft.Rule].Value, rules, item);
                    if (!ReferenceEquals(state, craft.Logistics)) sources[index] = craft with { Logistics = state };
                }
            }
        }
        return null;
    }
}
