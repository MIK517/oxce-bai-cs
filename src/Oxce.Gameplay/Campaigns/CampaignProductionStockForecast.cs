using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    string? ICampaignProductionPreflight.ForecastStocks(
        IReadOnlyDictionary<int, List<CraftState>> crafts,
        IReadOnlyDictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stocks,
        int? nextResearchTransferId, bool uncertainResearch, CampaignTimeTrigger highest, ref long work)
    {
        var rules = _content.RuntimeRules;
        List<(BaseState Owner, ProductionState Project, int Units)> planned = [];
        long transfers = 0;
        var uncertainTransfers = false;
        var uncertainAccounting = false;
        var accounting = new AccountingState(_funds[^1], _incomes[^1], _expenditures[^1]);
        foreach (var owner in _bases)
        {
            if (owner.Productions.Count == 0) continue;
            if (ForecastProductionAssignments(owner, highest, out var assignments, ref work) is { } staffingReason)
                return staffingReason;
            var sources = crafts[owner.Id];
            var uncertainStocks = false;
            var projectIndex = 0;
            foreach (var original in owner.Productions)
            {
                if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                var assigned = assignments[projectIndex++];
                var project = original.Assigned == assigned ? original : original with { Assigned = assigned };
                var rule = rules.Manufacture[project.Rule].Value;
                if (ProductionRuleInvalid(rule) is { } invalid) return invalid;
                var requested = ProductionProgress.Requested(rule.Time, project.Spent, project.Assigned, project.Amount, project.Infinite);
                if (requested == 0) continue;
                work += rule.RequiredMaterials.Count * (long)sources.Count + rule.ProducedMaterials.Count + sources.Count;
                if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                // The final unit was paid and its inputs consumed when it started.
                // No subsequent start can change funds, materials or craft in this hour.
                var final = !project.Infinite && requested == 1 && Produced(project) + 1L >= project.Amount;
                var supported = !project.Sell && rule.Points == 0 && rule.RandomProducedItems.Count == 0 && rule.SpawnedPersonType.Length == 0 &&
                    rule.ProducedMaterials.All(material => material.Craft is null);
                if (!supported)
                {
                    uncertainAccounting |= project.Sell || !final && rule.Cost > 0;
                    uncertainStocks |= rule.RequiredMaterials.Any(material => material.Quantity > 0) ||
                        !project.Sell && (rule.RandomProducedItems.Count != 0 ||
                            (rule.TransferTimes.Count == 0 || rule.TransferTimes[0] <= 0) &&
                            rule.ProducedMaterials.Any(material => material.Item is not null));
                    var itemDelay = rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0;
                    var craftDelay = rule.TransferTimes.Count > 2 && rule.TransferTimes[2] > 0;
                    uncertainTransfers |= rule.SpawnedPersonType.Length != 0 ||
                        itemDelay && (rule.RandomProducedItems.Count != 0 || !project.Sell && rule.ProducedMaterials.Any(material => material.Item is not null)) ||
                        craftDelay && rule.ProducedMaterials.Any(material => material.Craft is not null);
                    if (rule.RequiredMaterials.Any(material => material.Craft is { } type && sources.Any(craft => craft.Rule == type)))
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
                work += requested * (1L + rule.ProducedMaterials.Count + rule.RequiredMaterials.Count);
                if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                if (!final && rule.Cost > 0 && uncertainAccounting)
                    return "UFO retargeting requires shared production accounting forecasts.";
                if (!final && uncertainStocks && rule.RequiredMaterials.Any(material => material.Quantity > 0))
                    return "UFO retargeting requires shared production material forecasts.";
                var completed = 0;
                for (; completed < requested;)
                {
                    if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    if (ForecastProductionItemOutputs(owner.Id, rule, crafts, stocks, ref work) is { } outputReason) return outputReason;
                    completed++;
                    if (!project.Infinite && Produced(project) + (long)completed >= project.Amount) break;
                    if (rule.Cost > 0 && accounting.Funds < rule.Cost ||
                        !TryStageAccounting(-rule.Cost, accounting, out var nextAccounting)) break;
                    if (!TryPlanProductionMaterials(stocks[owner.Id], sources, rule, out var materials, out var materialReason))
                    {
                        if (materialReason != "STR_NOT_ENOUGH_MATERIALS") return materialReason;
                        break;
                    }
                    foreach (var material in materials)
                    {
                        if (material.Item is { } item) ChangeStock(stocks[owner.Id], item, -material.Quantity);
                        else if (material.Craft is { Logistics: { } state } consumed)
                        {
                            work += state.Weapons.Count + (long)state.Items.Count + state.Vehicles.Count * 2L;
                            if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                            IReadOnlyDictionary<string, int> unloaded;
                            try { unloaded = CraftLogistics.UnloadedItems(state, rules); }
                            catch (Exception error) when (error is OverflowException or InvalidDataException)
                            { return "Production craft unload quantities exceed the supported range."; }
                            foreach (var cargo in unloaded)
                            {
                                var cargoItem = rules.Items.GetRequired(cargo.Key);
                                var quantity = stocks[owner.Id].GetValueOrDefault(cargoItem) + (long)cargo.Value;
                                if (quantity > int.MaxValue) return "Production craft unload stocks exceed the supported range.";
                                ChangeStock(stocks[owner.Id], cargoItem, cargo.Value);
                            }
                            sources.Remove(consumed);
                            if (_handlers.Operation<World.ICampaignCraftRemoval>().ForecastFollowers(crafts, material.Id, consumed.Id, ref work) is { } followerReason) return followerReason;
                        }
                    }
                    accounting = nextAccounting;
                }
                planned.Add((owner, project, completed));
                if (!project.Sell && rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0)
                    transfers += completed * (long)rule.ProducedMaterials.Count(material => material.Item is not null);
            }
        }
        Dictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, long>> incoming = [];
        if (planned.Count != 0)
        {
            foreach (var owner in _bases)
            {
                Dictionary<RuleHandle<ItemRuleFamily>, long> quantities = [];
                foreach (var transfer in owner.Transfers)
                {
                    if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    if (transfer.Delivered || transfer.Hours <= 1 || transfer.Kind != CampaignTransferKind.Item) continue;
                    var item = rules.Items.GetRequired(transfer.RuleId);
                    quantities[item] = quantities.GetValueOrDefault(item) + transfer.Quantity;
                }
                incoming.Add(owner.Id, quantities);
            }
            foreach (var (owner, project, units) in planned)
            {
                var rule = rules.Manufacture[project.Rule].Value;
                if (rule.TransferTimes.Count == 0 || rule.TransferTimes[0] <= 0) continue;
                foreach (var material in rule.ProducedMaterials)
                {
                    if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    if (material.Item is not { } item) continue;
                    var quantity = incoming[owner.Id].GetValueOrDefault(item) + units * (long)material.Quantity;
                    if (quantity > int.MaxValue) return "Production arrivals exceed the supported quantity range.";
                    incoming[owner.Id][item] = quantity;
                }
            }
        }
        if (transfers != 0)
        {
            if (uncertainTransfers) return "Production arrivals require other production effect forecasts.";
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
        if (planned.Count != 0)
            foreach (var owner in _bases)
            {
                Dictionary<int, long> prisoners = [];
                foreach (var item in stocks[owner.Id])
                {
                    if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    var rule = rules.Items[item.Key].Value;
                    if (rule.IsAlien) prisoners[rule.PrisonType] = prisoners.GetValueOrDefault(rule.PrisonType) + item.Value;
                }
                foreach (var item in incoming[owner.Id])
                {
                    if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    var rule = rules.Items[item.Key].Value;
                    if (rule.IsAlien) prisoners[rule.PrisonType] = prisoners.GetValueOrDefault(rule.PrisonType) + item.Value;
                }
                if (prisoners.Values.Any(quantity => quantity > int.MaxValue))
                    return "Production captive populations exceed the supported range.";
            }
        return null;
    }

    private string? ForecastProductionItemOutputs(int ownerId, RuntimeManufactureRule rule,
        IReadOnlyDictionary<int, List<CraftState>> crafts,
        IReadOnlyDictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stocks, ref long work)
    {
        if (rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0) return null;
        var rules = _content.RuntimeRules;
        foreach (var material in rule.ProducedMaterials)
        {
            if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
            if (material.Item is not { } item) continue;
            var quantity = (long)stocks[ownerId].GetValueOrDefault(item) + material.Quantity;
            if (quantity > int.MaxValue) return "Production outputs exceed the supported stock range.";
            stocks[ownerId][item] = (int)quantity;
            if (rules.Items[item].Value.BattleType != 0) continue;
            var sources = crafts[ownerId];
            work += sources.Count;
            if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
            for (var index = 0; index < sources.Count; index++)
            {
                var craft = sources[index];
                var state = CraftServicing.ReuseItem(craft.Logistics!, rules.Crafts[craft.Rule].Value, rules, item);
                if (!ReferenceEquals(state, craft.Logistics)) sources[index] = craft with { Logistics = state };
            }
        }
        return null;
    }
}
