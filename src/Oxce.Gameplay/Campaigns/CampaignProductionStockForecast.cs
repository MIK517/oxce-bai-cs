using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    string? ICampaignProductionPreflight.ForecastStocks(
        IReadOnlyDictionary<int, List<CraftState>> crafts,
        IReadOnlyDictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stocks,
        IReadOnlyDictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stockUncertainty,
        int? nextResearchTransferId, bool uncertainResearch, IReadOnlyList<CustomCounterChange>? researchCounters,
        CampaignTimeTrigger highest, ref long work)
    {
        var rules = _content.RuntimeRules;
        List<(BaseState Owner, ProductionState Project, int Units, IReadOnlyDictionary<string, int>? RandomItems)> planned = [];
        long transfers = 0;
        var uncertainTransfers = false;
        var uncertainAccounting = false;
        Dictionary<string, long>? craftCounters = null;
        HashSet<RuleHandle<CraftRuleFamily>>? uncertainCraftCounters = null;
        var accounting = new AccountingState(_funds[^1], _incomes[^1], _expenditures[^1]);
        var tracksSoldierCounter = false;
        if (rules.Crafts.TryGet("STR_SOLDIER", out var soldierCraft))
            foreach (var owner in _bases)
            {
                if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                foreach (var project in owner.Productions)
                {
                    var rule = rules.Manufacture[project.Rule].Value;
                    work += 1L + rule.ProducedMaterials.Count;
                    if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    tracksSoldierCounter |= rule.ProducedMaterials.Any(material => material.Craft == soldierCraft && material.Quantity > 0);
                }
            }
        foreach (var owner in _bases)
        {
            if (owner.Productions.Count == 0) continue;
            if (ForecastProductionAssignments(owner, highest, out var assignments, ref work) is { } staffingReason)
                return staffingReason;
            var sources = crafts[owner.Id];
            var uncertainStocks = false;
            var uncertainPersonnel = false;
            long personnelBirths = 0;
            work += owner.Facilities.Count + (long)owner.Transfers.Count + owner.Research.Count + owner.Productions.Count;
            if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
            var quarters = UsedQuartersWide(owner);
            long availableQuarters = 0;
            foreach (var facility in owner.Facilities)
                if (facility.BuildTime == 0 || highest >= CampaignTimeTrigger.OneDay && facility.BuildTime == 1)
                    availableQuarters += rules.Facilities[facility.Rule].Value.Personnel;
            var projectIndex = 0;
            foreach (var original in owner.Productions)
            {
                if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                var assigned = assignments[projectIndex++];
                var project = original.Assigned == assigned ? original : original with { Assigned = assigned };
                var rule = rules.Manufacture[project.Rule].Value;
                // Validation consumes rule edges even when this hour produces no
                // unit. Reserve those scans before calling the shared validator.
                work += 2L * (rule.RequiredMaterials.Count + (long)rule.ProducedMaterials.Count) +
                    rule.RandomProducedItems.Count + (long)rule.Events.Count;
                if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                foreach (var choice in rule.RandomProducedItems)
                {
                    work += choice.Items.Count;
                    if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                }
                if (ProductionRuleInvalid(rule) is { } invalid) return invalid;
                var requested = ProductionProgress.Requested(rule.Time, project.Spent, project.Assigned, project.Amount, project.Infinite);
                if (requested == 0) continue;
                work += rule.RequiredMaterials.Count * (long)sources.Count + rule.ProducedMaterials.Count + sources.Count;
                if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                // The final unit was paid and its inputs consumed when it started.
                // No subsequent start can change funds, materials or craft in this hour.
                var final = !project.Infinite && requested == 1 && Produced(project) + 1L >= project.Amount;
                var deterministicRandom = TryForecastRandomProduction(owner, rule, out var randomItems, out var randomTransferCount, ref work);
                if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                var personnel = rule.SpawnedPersonType.Length != 0;
                var craftDelay = rule.TransferTimes.Count > 2 && rule.TransferTimes[2] > 0;
                var supported = deterministicRandom;
                if (!supported)
                {
                    foreach (var material in rule.ProducedMaterials)
                        if (material.Craft is { } craft) (uncertainCraftCounters ??= []).Add(craft);
                    if (tracksSoldierCounter && rule.SpawnedPersonType is not "" and not "STR_SCIENTIST" and not "STR_ENGINEER")
                        (uncertainCraftCounters ??= []).Add(soldierCraft);
                    uncertainAccounting |= project.Sell || !final && rule.Cost > 0;
                    uncertainStocks |= rule.RequiredMaterials.Any(material => material.Quantity > 0) ||
                        rule.RandomProducedItems.Count != 0 || !project.Sell && (
                            (rule.TransferTimes.Count == 0 || rule.TransferTimes[0] <= 0) &&
                            rule.ProducedMaterials.Any(material => material.Item is not null));
                    var itemDelay = rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0;
                    uncertainTransfers |= rule.SpawnedPersonType.Length != 0 ||
                        itemDelay && (rule.RandomProducedItems.Count != 0 || !project.Sell && rule.ProducedMaterials.Any(material => material.Item is not null)) ||
                        craftDelay && rule.ProducedMaterials.Any(material => material.Craft is not null);
                    uncertainPersonnel |= personnel;
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
                if ((project.Sell || !final && rule.Cost > 0) && uncertainAccounting)
                    return "UFO retargeting requires shared production accounting forecasts.";
                if (!final && uncertainStocks && rule.RequiredMaterials.Any(material => material.Quantity > 0))
                    return "UFO retargeting requires shared production material forecasts.";
                if (personnel && uncertainPersonnel) return "UFO retargeting requires shared production personnel forecasts.";
                var completed = 0;
                for (; completed < requested;)
                {
                    if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    if (project.Sell)
                    {
                        if (ForecastProductionSale(rule, highest, ref accounting, ref work) is { } saleReason) return saleReason;
                    }
                    else if (ForecastProductionItemOutputs(owner.Id, rule, crafts, stocks, ref work) is { } outputReason) return outputReason;
                    foreach (var material in rule.ProducedMaterials)
                        if (material.Craft is { } craft)
                        {
                            if (uncertainCraftCounters?.Contains(craft) == true)
                                return "Craft production requires other production identity forecasts.";
                            craftCounters ??= [];
                            if (ForecastProducedCraftIdentity(craft, crafts, craftCounters, researchCounters, highest, ref work, out var identity) is { } identityReason)
                                return identityReason;
                            if (!craftDelay) sources.Add(new(craft, identity) { Logistics = ProducedCraftLogistics(owner, craft) });
                        }
                    if (randomItems is not null && (rule.TransferTimes.Count == 0 || rule.TransferTimes[0] <= 0))
                        foreach (var item in randomItems)
                            if (ForecastProductionItemOutput(owner.Id, rules.Items.GetRequired(item.Key), item.Value, crafts, stocks, ref work) is { } randomReason)
                                return randomReason;
                    if (tracksSoldierCounter && rule.SpawnedPersonType is not "" and not "STR_SCIENTIST" and not "STR_ENGINEER")
                    {
                        if (uncertainCraftCounters?.Contains(soldierCraft) == true)
                            return "Craft production requires other production identity forecasts.";
                        craftCounters ??= new(StringComparer.Ordinal);
                        if (ForecastProducedSoldierCounter(craftCounters, researchCounters, highest, ref work) is { } soldierReason)
                            return soldierReason;
                    }
                    completed++;
                    if (personnel) personnelBirths++;
                    if (!project.Infinite && Produced(project) + (long)completed >= project.Amount) break;
                    if (rule.Cost > 0 && accounting.Funds < rule.Cost ||
                        !TryStageAccounting(-rule.Cost, accounting, out var nextAccounting)) break;
                    // Native checks quarters only after the final completion of this
                    // hour; intermediate completions can exceed capacity together.
                    if (personnel && completed >= requested && availableQuarters < quarters + personnelBirths) break;
                    if (!TryPlanProductionMaterials(stocks[owner.Id], sources, rule, out var materials, out var materialReason))
                    {
                        if (materialReason != "STR_NOT_ENOUGH_MATERIALS") return materialReason;
                        break;
                    }
                    // All inputs are nonnegative. If the upper stocks cannot start
                    // another unit, neither can any smaller saving outcome. When
                    // they can, prove every uncertain input is also present at its
                    // lower bound before applying the same consumption to both.
                    if (stockUncertainty.TryGetValue(owner.Id, out var uncertainty))
                        foreach (var material in materials)
                            if (material.Item is { } item &&
                                stocks[owner.Id].GetValueOrDefault(item) - (long)uncertainty.GetValueOrDefault(item) < material.Quantity)
                                return "UFO retargeting requires stochastic production material forecasts.";
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
                planned.Add((owner, project, completed, randomItems));
                if (personnel) transfers += completed;
                if (craftDelay) transfers += completed * (long)rule.ProducedMaterials.Where(material => material.Craft is not null).Sum(material => material.Quantity);
                if (rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0)
                    transfers += completed * (long)((project.Sell ? 0 : rule.ProducedMaterials.Count(material => material.Item is not null)) + randomTransferCount);
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
            foreach (var (owner, project, units, randomItems) in planned)
            {
                var rule = rules.Manufacture[project.Rule].Value;
                if (rule.TransferTimes.Count == 0 || rule.TransferTimes[0] <= 0) continue;
                if (!project.Sell) foreach (var material in rule.ProducedMaterials)
                {
                    if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    if (material.Item is not { } item) continue;
                    var quantity = incoming[owner.Id].GetValueOrDefault(item) + units * (long)material.Quantity;
                    if (quantity > int.MaxValue) return "Production arrivals exceed the supported quantity range.";
                    incoming[owner.Id][item] = quantity;
                }
                if (randomItems is not null) foreach (var item in randomItems)
                {
                    if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
                    var handle = rules.Items.GetRequired(item.Key);
                    var quantity = incoming[owner.Id].GetValueOrDefault(handle) + units * (long)item.Value;
                    if (quantity > int.MaxValue) return "Production arrivals exceed the supported quantity range.";
                    incoming[owner.Id][handle] = quantity;
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

    private string? ForecastProductionSale(RuntimeManufactureRule rule, CampaignTimeTrigger highest, ref AccountingState accounting, ref long work)
    {
        var rules = _content.RuntimeRules;
        var daily = highest >= CampaignTimeTrigger.OneDay;
        if (daily && DaysPassed == int.MaxValue) return "Production price days exceed the supported range.";
        var forecast = new PriceForecastContext(Time.Advance(out _), DaysPassed + (daily ? 1 : 0), daily, work);
        foreach (var material in rule.ProducedMaterials)
        {
            if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
            if (material.Item is not { } item) continue;
            var itemRule = rules.Items[item].Value;
            forecast.Work = work;
            var price = ItemPrice(rules.Items.GetExternalId(item), itemRule, false, out var unavailable, forecast);
            work = forecast.Work;
            if (unavailable is not null) return unavailable;
            if (!TryStageAccounting(checked((long)price * material.Quantity), accounting, out var next))
                return "Production accounting exceeds the supported range.";
            accounting = next;
        }
        return null;
    }

    private string? ForecastProductionItemOutputs(int ownerId, RuntimeManufactureRule rule,
        IReadOnlyDictionary<int, List<CraftState>> crafts,
        IReadOnlyDictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stocks, ref long work)
    {
        if (rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0) return null;
        foreach (var material in rule.ProducedMaterials)
        {
            if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
            if (material.Item is not { } item) continue;
            if (ForecastProductionItemOutput(ownerId, item, material.Quantity, crafts, stocks, ref work) is { } reason) return reason;
        }
        return null;
    }

    private string? ForecastProductionItemOutput(int ownerId, RuleHandle<ItemRuleFamily> item, int added,
        IReadOnlyDictionary<int, List<CraftState>> crafts,
        IReadOnlyDictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stocks, ref long work)
    {
        if (++work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
        var rules = _content.RuntimeRules;
        var quantity = (long)stocks[ownerId].GetValueOrDefault(item) + added;
        if (quantity > int.MaxValue) return "Production outputs exceed the supported stock range.";
        stocks[ownerId][item] = (int)quantity;
        if (rules.Items[item].Value.BattleType != 0) return null;
        var sources = crafts[ownerId];
        work += sources.Count;
        if (work > 1_000_000) return "UFO retargeting exceeds the supported work limit.";
        for (var index = 0; index < sources.Count; index++)
        {
            var craft = sources[index];
            var state = CraftServicing.ReuseItem(craft.Logistics!, rules.Crafts[craft.Rule].Value, rules, item);
            if (!ReferenceEquals(state, craft.Logistics)) sources[index] = craft with { Logistics = state };
        }
        return null;
    }

    private bool TryForecastRandomProduction(BaseState owner, RuntimeManufactureRule rule,
        out IReadOnlyDictionary<string, int>? items, out int transferCount, ref long work)
    {
        items = null;
        transferCount = 0;
        if (rule.RandomProducedItems.Count == 0) return true;
        var rules = _content.RuntimeRules;
        HashSet<RuleHandle<ItemRuleFamily>> sensitive = [];
        foreach (var craft in rules.Crafts.Rules)
        {
            if (++work > 1_000_000) return false;
            if (craft.Value.RefuelItem is { } fuel) sensitive.Add(fuel);
        }
        foreach (var project in owner.Productions)
        {
            var product = rules.Manufacture[project.Rule].Value;
            work += 1L + product.RequiredMaterials.Count;
            if (work > 1_000_000) return false;
            foreach (var material in product.RequiredMaterials)
                if (material.Item is { } item && material.Quantity > 0) sensitive.Add(item);
        }
        var immediate = rule.TransferTimes.Count == 0 || rule.TransferTimes[0] <= 0;
        var minimumTransfers = int.MaxValue;
        foreach (var choice in rule.RandomProducedItems)
        {
            if (++work > 1_000_000) return false;
            if (choice.Weight == 0) continue;
            transferCount = Math.Max(transferCount, choice.Items.Count);
            minimumTransfers = Math.Min(minimumTransfers, choice.Items.Count);
        }
        var stableTransfers = immediate || minimumTransfers == transferCount;
        return ProductionOutputEnvelope.TryCreate(rule.RandomProducedItems, id =>
        {
            var item = rules.Items.GetRequired(id);
            var definition = rules.Items[item].Value;
            return stableTransfers && definition.BattleType != 0 && !definition.IsAlien && !sensitive.Contains(item);
        }, ref work, out items);
    }
}
