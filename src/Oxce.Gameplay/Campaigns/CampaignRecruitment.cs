using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private void AddSoldierRows(BaseState origin, BaseState destination, List<LogisticsRow> rows,
        LogisticsOperation operation, double distance, int hours)
    {
        foreach (var soldier in origin.Soldiers)
        {
            var personal = soldier.Personal;
            if (personal is { CraftType.Length: > 0 }) continue;
            string? reason = personal is null ? "Soldier personal state is required for transfer or dismissal." : null;
            if (operation == LogisticsOperation.Transfer && UsedQuarters(destination) >= AvailableQuarters(destination))
                reason ??= "No living space at the destination.";
            if (personal is not null && !_content.RuntimeRules.Armors.TryGet(personal.Armor, out _))
                reason ??= "Soldier armor must be resolved before transfer or dismissal.";
            rows.Add(new(rows.Count, CampaignTransferKind.Soldier, _content.RuntimeRules.Soldiers.GetExternalId(soldier.Rule),
                soldier.Id, personal?.Name ?? $"Soldier {soldier.Id}", 1,
                operation == LogisticsOperation.Transfer ? StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Soldier) : 0,
                reason is null ? 1 : 0, hours, reason));
        }
    }

    private void AddRecruitRows(BaseState state, List<LogisticsRow> rows)
    {
        foreach (var entry in _content.RuntimeRules.Soldiers.Rules)
        {
            var rule = entry.Value;
            if (rule.CostBuy == 0) continue;
            var reason = PurchaseRestriction(state, rule.Purchase);
            if (!_debugMode && rule.Requirements.Any(r => !_completedResearch.Contains(r.Id))) reason ??= "Required research is not complete.";
            if (rule.SpawnedTemplate is { UnsupportedFields.Count: > 0 } template)
                reason ??= $"Soldier template requires state for {string.Join(", ", template.UnsupportedFields)}.";
            if (rule.SpawnedTemplate is { TransformationBonusesCount: > 0, RandomTransformationBonuses: { } weights } spawn &&
                weights.Any(p => p.Value > 0 && p.Key.Length != 0 && p.Key != "\0" && spawn.TransformationBonuses?.GetValueOrDefault(p.Key) == int.MaxValue))
                reason ??= "A possible soldier template bonus exceeds the counter range.";
            if (rule.MinimumStats.Any(p => p.Value > rule.MaximumStats.GetValueOrDefault(p.Key))) reason ??= "Soldier generation has inverted stat bounds.";
            var max = Math.Min(MaximumLogisticsLines, Math.Max(0, AvailableQuarters(state) - UsedQuarters(state)));
            if (rule.CostBuy > 0) max = (int)Math.Min(max, Math.Max(0, _funds[^1] / rule.CostBuy));
            if (rule.Purchase.MonthlyLimit > 0) max = Math.Min(max, Math.Max(0, rule.Purchase.MonthlyLimit - _monthlyPurchaseLog.GetValueOrDefault(entry.Id)));
            if (max == 0) reason ??= "No funds, living space or monthly recruitment allowance.";
            rows.Add(new(rows.Count, CampaignTransferKind.Soldier, entry.Id, 0, entry.Id,
                state.Soldiers.Count(s => s.Rule == _content.RuntimeRules.Soldiers.GetRequired(entry.Id)),
                rule.CostBuy, reason is null ? max : 0,
                rule.TransferTime == 0 ? _content.RuntimeRules.Campaign.PersonnelTransferTime : rule.TransferTime, reason));
        }
    }

    private int MonthlyLimit(LogisticsRow row) => row.Kind switch
    {
        CampaignTransferKind.Soldier => _content.RuntimeRules.Soldiers[_content.RuntimeRules.Soldiers.GetRequired(row.RuleId)].Value.Purchase.MonthlyLimit,
        CampaignTransferKind.Item => _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(row.RuleId)].Value.Purchase.MonthlyLimit,
        CampaignTransferKind.Craft => _content.RuntimeRules.Crafts[_content.RuntimeRules.Crafts.GetRequired(row.RuleId)].Value.Purchase.MonthlyLimit,
        _ => 0,
    };

    private int NextSoldierId()
    {
        var highest = _bases.SelectMany(b => b.Soldiers.Select(s => s.Id).Concat(b.Transfers.Where(t => t.Soldier is not null).Select(t => t.Soldier!.Id)))
            .DefaultIfEmpty(0).Max();
        return Math.Max(_nextIds.GetValueOrDefault("STR_SOLDIER", 1), highest == int.MaxValue ? int.MaxValue : highest + 1);
    }

    private int SelectNationality(RuntimeSoldierRule rule, BaseState state)
    {
        var settings = _content.RuntimeRules.Campaign;
        if (settings.HireByCountryOdds > 0 && _random.NextInclusive(0, 99) < settings.HireByCountryOdds)
        {
            var country = _countries.FirstOrDefault(c => _content.RuntimeRules.Countries[c.Rule].Value.Areas.Any(a => AreaContains(a, state.Longitude, state.Latitude)));
            if (country is not null)
                for (var i = 0; i < rule.NamePools.Count; i++)
                    if (rule.NamePools[i].Country == _content.RuntimeRules.Countries.GetExternalId(country.Rule)) return i;
        }
        if (settings.HireByRegionOdds > 0 && _random.NextInclusive(0, 99) < settings.HireByRegionOdds)
        {
            var region = _regions.FirstOrDefault(r => _content.RuntimeRules.Regions[r.Rule].Value.Areas.Any(a => AreaContains(a, state.Longitude, state.Latitude)));
            if (region is null) return -1;
            var regionId = _content.RuntimeRules.Regions.GetExternalId(region.Rule);
            var weight = rule.NamePools.Where(n => n.Region == regionId).Sum(n => n.GlobalWeight);
            if (weight == 0) return -1;
            var choice = _random.NextInclusive(1, weight);
            for (var i = 0; i < rule.NamePools.Count; i++)
            {
                if (rule.NamePools[i].Region != regionId) continue;
                if (choice <= rule.NamePools[i].GlobalWeight) return i;
                choice -= rule.NamePools[i].GlobalWeight;
            }
        }
        return -1;
    }
}
