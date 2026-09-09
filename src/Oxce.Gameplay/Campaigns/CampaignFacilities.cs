using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed record BuildCampaignFacility(int BaseId, string RuleId, int X, int Y) : ICampaignCommand;
public sealed record DismantleCampaignFacility(int BaseId, int X, int Y) : ICampaignCommand;
public sealed record CampaignFacilityChanged(int BaseId, int X, int Y, string RuleId, int BuildTime) : ICampaignEvent;

public sealed partial class CampaignState
{
    private RuntimeFacilityRule FacilityRule(FacilityState facility) => _content.RuntimeRules.Facilities[facility.Rule].Value;
    private static bool Overlaps(int x, int y, RuntimeFacilityRule a, int bx, int by, RuntimeFacilityRule b) =>
        x < bx + b.SizeX && bx < x + a.SizeX && y < by + b.SizeY && by < y + a.SizeY;
    private bool Adjacent(FacilityState a, FacilityState b)
    {
        var ar = FacilityRule(a); var br = FacilityRule(b);
        return (a.X + ar.SizeX == b.X || b.X + br.SizeX == a.X) && a.Y < b.Y + br.SizeY && b.Y < a.Y + ar.SizeY ||
            (a.Y + ar.SizeY == b.Y || b.Y + br.SizeY == a.Y) && a.X < b.X + br.SizeX && b.X < a.X + ar.SizeX;
    }
    private string? FacilityActionRestriction(BaseState owner) =>
        !owner.IsPlaced ? "Place the starting base first." : _restrictions.FirstOrDefault(r =>
            r.BlocksLogistics && (r.BaseId is null || r.BaseId == owner.Id))?.Feature ?? MissingCraftInventory(owner);

    private CampaignCommandResult BuildFacility(BuildCampaignFacility command)
    {
        var owner = FindBase(command.BaseId);
        if (FacilityActionRestriction(owner) is { } restriction) return Blocked(restriction);
        var handle = _content.RuntimeRules.Facilities.GetRequired(command.RuleId);
        var rule = _content.RuntimeRules.Facilities[handle].Value;
        if (!FacilityFits(rule, command.X, command.Y)) return Blocked("STR_CANNOT_BUILD_HERE");
        if (rule.Lift && !rule.UpgradeOnly || !FacilityAllowed(rule, owner.FakeUnderwater)) return Blocked("Facility is not available for this base type.");
        if (!_debugMode && rule.Requirements.Any(r => !_completedResearch.Contains(r.Id))) return Blocked("Required research is not complete.");
        if (!HasFunctions(owner, rule.RequiredBaseFunctions)) return Blocked("Required base functions are unavailable.");
        if (rule.MaximumAllowedPerBase > 0 && owner.Facilities.Count(f => f.Rule == handle) >= rule.MaximumAllowedPerBase)
            return Blocked("Maximum facility count reached.");
        var removed = owner.Facilities.Where(f => Overlaps(command.X, command.Y, rule, f.X, f.Y, FacilityRule(f))).ToArray();
        var remaining = owner.Facilities.Except(removed).ToList();
        if (rule.UpgradeOnly && removed.Sum(f => FacilityRule(f).SizeX * FacilityRule(f).SizeY) != rule.SizeX * rule.SizeY)
            return Blocked("STR_CANNOT_BUILD_UPGRADE_ONLY");
        foreach (var old in removed)
        {
            var oldRule = FacilityRule(old);
            if (!oldRule.CanBeBuiltOver && !rule.BuildOverFacilities.Contains(old.Rule)) return Blocked("STR_CANNOT_UPGRADE_FACILITY_DISALLOWED");
            if (old.X < command.X || old.Y < command.Y || old.X + oldRule.SizeX > command.X + rule.SizeX || old.Y + oldRule.SizeY > command.Y + rule.SizeY)
                return Blocked("STR_CANNOT_UPGRADE_FACILITY_WRONG_SIZE");
            if (old.HadPreviousFacility && old.BuildTime != 0) return Blocked("STR_CANNOT_UPGRADE_FACILITY_ALREADY_UPGRADING");
            if (oldRule.Lift && !rule.Lift) return Blocked("Access lift is in use.");
        }
        if (FacilityUsageRestriction(owner, removed, rule) is { } usage) return Blocked(usage);
        var placed = new FacilityState(handle, command.X, command.Y, rule.BuildTime, 0, false, false, false);
        var adjacent = remaining.Where(f => Adjacent(placed, f)).ToArray();
        if (!adjacent.Any(f => f.BuildTime == 0 || f.HadPreviousFacility) && (!Options.AllowBuildingQueue || adjacent.Length == 0))
            return Blocked("STR_CANNOT_BUILD_HERE");
        var stock = new Dictionary<RuleHandle<ItemRuleFamily>, int>(owner.Items);
        long funds = _funds[^1], income = _incomes[^1], spending = _expenditures[^1];
        double reduction = 0;
        var upgrading = false;
        foreach (var old in removed.Reverse())
        {
            var oldRule = FacilityRule(old);
            Refund(old, stock, ref funds, ref income, ref spending);
            if (old.BuildTime <= oldRule.BuildTime)
            {
                reduction += (oldRule.BuildTime - (double)old.BuildTime) * oldRule.SizeX * oldRule.SizeY / (rule.SizeX * rule.SizeY);
                upgrading |= old.BuildTime == 0;
            }
        }
        if (funds < rule.BuildCost) return Blocked("STR_NOT_ENOUGH_MONEY");
        if (rule.BuildTime < 0) return Blocked("Facility construction time cannot be negative.");
        foreach (var item in rule.BuildCostItems)
        {
            if (item.Item is not { } id || item.Build < 0 || stock.GetValueOrDefault(id) < item.Build) return Blocked("STR_NOT_ENOUGH_ITEMS");
            ChangeStock(stock, id, -item.Build);
        }
        Account(-(long)rule.BuildCost, ref funds, ref income, ref spending);
        if (upgrading)
        {
            var scaled = Math.Round(reduction * _content.RuntimeRules.Campaign.BuildTimeReductionScaling / 100, MidpointRounding.AwayFromZero);
            if (scaled < int.MinValue || scaled > int.MaxValue) return Blocked("Construction reduction exceeds the supported range.");
            placed = placed with { BuildTime = Math.Max(1, checked(rule.BuildTime - (int)scaled)), HadPreviousFacility = true };
        }
        remaining.Add(placed);
        if (Options.AllowBuildingQueue && !adjacent.Any(f => f.BuildTime == 0 || f.HadPreviousFacility))
        {
            remaining[^1] = remaining[^1] with { BuildTime = int.MaxValue };
            RecalculateQueuedBuildings(remaining);
            placed = remaining[^1];
        }
        PublishFacilityChange(owner, remaining, stock, funds, income, spending);
        return new([new CampaignFacilityChanged(owner.Id, command.X, command.Y, command.RuleId, placed.BuildTime)]);
    }

    private CampaignCommandResult DismantleFacility(DismantleCampaignFacility command)
    {
        var owner = FindBase(command.BaseId);
        if (FacilityActionRestriction(owner) is { } restriction) return Blocked(restriction);
        var target = owner.Facilities.FirstOrDefault(f => f.X == command.X && f.Y == command.Y);
        if (target is null) return Blocked("No facility at that position.");
        var rule = FacilityRule(target);
        if (target.BuildTime == 0 && FacilityUsageRestriction(owner, [target], null) is { } usage) return Blocked(usage);
        var remaining = owner.Facilities.Where(f => f != target).ToList();
        if (rule.Lift) return Blocked("Access lift removal requires base abandonment.");
        if (rule.LeavesBehindOnSell.Count == 0 && !FacilitiesConnected(remaining)) return Blocked("STR_CANNOT_DISMANTLE_FACILITY");
        var stock = new Dictionary<RuleHandle<ItemRuleFamily>, int>(owner.Items);
        long funds = _funds[^1], income = _incomes[^1], spending = _expenditures[^1];
        Refund(target, stock, ref funds, ref income, ref spending);
        if (target.BuildTime == 0 && rule.LeavesBehindOnSell.Count > 0)
        {
            var first = _content.RuntimeRules.Facilities[rule.LeavesBehindOnSell[0]].Value;
            if (first.SizeX == rule.SizeX && first.SizeY == rule.SizeY) AddRemainder(rule.LeavesBehindOnSell[0], target.X, target.Y);
            else
            {
                var slot = 0;
                for (var y = target.Y; y < target.Y + rule.SizeY; y++)
                    for (var x = target.X; x < target.X + rule.SizeX; x++)
                    {
                        var type = rule.LeavesBehindOnSell[slot++ % rule.LeavesBehindOnSell.Count];
                        var child = _content.RuntimeRules.Facilities[type].Value;
                        if (child.SizeX != 1 || child.SizeY != 1) return Blocked("Dismantling remainder facilities do not fit the vacated area.");
                        AddRemainder(type, x, y);
                    }
            }
        }
        if (Options.AllowBuildingQueue) RecalculateQueuedBuildings(remaining);
        PublishFacilityChange(owner, remaining, stock, funds, income, spending);
        return new([new CampaignFacilityChanged(owner.Id, command.X, command.Y, "", 0)]);

        void AddRemainder(RuleHandle<FacilityRuleFamily> type, int x, int y)
        {
            var time = rule.RemovalTime <= -1 ? _content.RuntimeRules.Facilities[type].Value.BuildTime : rule.RemovalTime;
            if (time < 0) throw new InvalidDataException("Dismantling construction time cannot be negative.");
            remaining.Add(new(type, x, y, time, 0, false, false, time != 0));
        }
    }

    private bool FacilitiesConnected(List<FacilityState> facilities)
    {
        var lift = facilities.LastOrDefault(f => FacilityRule(f).Lift);
        if (lift is null) return true; // Reference returns no disconnected facilities when no lift exists.
        var visited = new HashSet<FacilityState> { lift };
        var pending = new Queue<FacilityState>(); pending.Enqueue(lift);
        while (pending.TryDequeue(out var current))
            foreach (var next in facilities)
                if (!visited.Contains(next) && Adjacent(current, next) &&
                    (current.BuildTime == 0 || current.HadPreviousFacility || next.BuildTime > FacilityRule(next).BuildTime))
                { visited.Add(next); pending.Enqueue(next); }
        return visited.Count == facilities.Count;
    }

    private string? FacilityUsageRestriction(BaseState owner, FacilityState[] removed, RuntimeFacilityRule? replacement)
    {
        var remaining = owner.Facilities.Except(removed).ToArray();
        var rules = remaining.Select(FacilityRule).ToArray();
        var removedRules = removed.Select(FacilityRule).ToArray();
        var operational = remaining.Where(f => f.BuildTime == 0 || f.HadPreviousFacility).Select(FacilityRule).ToArray();
        var (geographicProvided, geographicForbidden) = GeographicFunctions(owner);
        var future = rules.SelectMany(r => r.ProvidedBaseFunctions).ToHashSet(StringComparer.Ordinal);
        future.UnionWith(geographicProvided);
        var forbidden = rules.SelectMany(r => r.ForbiddenBaseFunctions).ToHashSet(StringComparer.Ordinal);
        forbidden.UnionWith(geographicForbidden);
        if (replacement is not null && (replacement.ProvidedBaseFunctions.Any(forbidden.Contains) || replacement.ForbiddenBaseFunctions.Any(future.Contains)))
            return "Facility conflicts with another base function.";
        var missed = removedRules.SelectMany(r => r.ProvidedBaseFunctions).ToHashSet(StringComparer.Ordinal);
        var provided = remaining.Where(f => f.BuildTime == 0).SelectMany(f => FacilityRule(f).ProvidedBaseFunctions).ToHashSet(StringComparer.Ordinal);
        if (replacement is not null) provided.UnionWith(replacement.ProvidedBaseFunctions);
        if (rules.SelectMany(r => r.RequiredBaseFunctions).Any(f => missed.Contains(f) && !provided.Contains(f))) return "Required base function is in use.";
        if (removed.Length == 0) return null;
        if (removedRules.Any(r => r.Storage > 0) && operational.Sum(r => r.Storage) + (Options.StorageLimitsEnforced ? 0 : replacement?.Storage ?? 0) < UsedStores(owner))
            return "STR_FACILITY_IN_USE_STORAGE";
        if (removedRules.Any(r => r.Personnel > 0) && operational.Sum(r => r.Personnel) + (replacement?.Personnel ?? 0) < UsedQuarters(owner))
            return "STR_FACILITY_IN_USE_QUARTERS";
        foreach (var type in removedRules.Where(r => r.Crafts > 0).Select(r => r.HangarType).Distinct())
            if (rules.Where(r => r.HangarType == type).Sum(r => r.Crafts) < UsedHangars(owner, type)) return "STR_FACILITY_IN_USE_HANGARS";
        foreach (var type in removedRules.Where(r => r.Aliens > 0).Select(r => r.PrisonType).Distinct())
            if (rules.Where(r => r.PrisonType == type).Sum(r => r.Aliens) +
                (!Options.StorageLimitsEnforced && replacement?.PrisonType == type ? replacement.Aliens : 0) < UsedContainment(owner, type))
                return "STR_FACILITY_IN_USE_PRISONS";
        if (removedRules.Any(r => r.PsiLaboratories > 0) && operational.Sum(r => r.PsiLaboratories) + (replacement?.PsiLaboratories ?? 0) < owner.Soldiers.Count(s => s.Personal?.PsiTraining == true))
            return "STR_FACILITY_IN_USE_PSI_LABS";
        if (removedRules.Any(r => r.TrainingRooms > 0) && operational.Sum(r => r.TrainingRooms) + (replacement?.TrainingRooms ?? 0) < owner.Soldiers.Count(s => s.Personal?.Training == true))
            return "STR_FACILITY_IN_USE_GYMS";
        return null;
    }

    private (HashSet<string> Provided, HashSet<string> Forbidden) GeographicFunctions(BaseState owner)
    {
        var provided = new HashSet<string>(StringComparer.Ordinal);
        var forbidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var country in _countries)
        {
            var rule = _content.RuntimeRules.Countries[country.Rule].Value;
            if (!rule.Areas.Any(a => AreaContains(a, owner.Longitude, owner.Latitude))) continue;
            provided.UnionWith(rule.ProvidedBaseFunctions); forbidden.UnionWith(rule.ForbiddenBaseFunctions); break;
        }
        foreach (var region in _regions)
        {
            var rule = _content.RuntimeRules.Regions[region.Rule].Value;
            if (!rule.Areas.Any(a => AreaContains(a, owner.Longitude, owner.Latitude))) continue;
            provided.UnionWith(rule.ProvidedBaseFunctions); forbidden.UnionWith(rule.ForbiddenBaseFunctions); break;
        }
        return (provided, forbidden);
    }

    private void RecalculateQueuedBuildings(List<FacilityState> facilities)
    {
        var queued = new HashSet<int>();
        for (var index = 0; index < facilities.Count; index++)
            if (!facilities[index].HadPreviousFacility && facilities[index].BuildTime > FacilityRule(facilities[index]).BuildTime)
            { queued.Add(index); facilities[index] = facilities[index] with { BuildTime = int.MaxValue }; }
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var index in queued)
            {
                var facility = facilities[index];
                var ownTime = FacilityRule(facility).BuildTime;
                foreach (var neighbor in facilities.Where((_, candidate) => candidate != index && Adjacent(facility, facilities[candidate])).ToArray())
                {
                    var adjusted = neighbor.HadPreviousFacility ? 0 : neighbor.BuildTime;
                    if (adjusted == int.MaxValue || (long)adjusted + ownTime >= facility.BuildTime) continue;
                    facilities[index] = facility = facility with { BuildTime = adjusted + ownTime };
                    changed = true;
                }
            }
        }
    }

    private void Refund(FacilityState facility, Dictionary<RuleHandle<ItemRuleFamily>, int> stock, ref long funds, ref long income, ref long spending)
    {
        var rule = FacilityRule(facility);
        var queued = facility.BuildTime > rule.BuildTime;
        Account(queued ? rule.BuildCost : rule.RefundValue, ref funds, ref income, ref spending);
        foreach (var item in rule.BuildCostItems)
        {
            var quantity = queued ? item.Build : item.Refund;
            if (quantity < 0 || item.Item is null) throw new InvalidDataException("Invalid facility refund item.");
            ChangeStock(stock, item.Item.Value, quantity);
        }
        if (facility.Ammo > 0 && rule.AmmoItem is { } ammo) ChangeStock(stock, ammo, facility.Ammo);
    }
    private static void ChangeStock(Dictionary<RuleHandle<ItemRuleFamily>, int> stock, RuleHandle<ItemRuleFamily> item, int delta)
    {
        var value = checked(stock.GetValueOrDefault(item) + delta);
        if (value < 0) throw new InvalidDataException("Insufficient facility stock.");
        if (value == 0) stock.Remove(item); else stock[item] = value;
    }
    private static void Account(long delta, ref long funds, ref long income, ref long spending)
    {
        funds = checked(funds + delta);
        if (delta > 0) income = checked(income + delta); else spending = checked(spending - delta);
    }
    private void PublishFacilityChange(BaseState owner, List<FacilityState> facilities,
        Dictionary<RuleHandle<ItemRuleFamily>, int> stock, long funds, long income, long spending)
    {
        var next = _nextIds.GetValueOrDefault("oxcePortFacility", 1);
        for (var i = 0; i < facilities.Count; i++)
            if (!owner.Facilities.Any(previous => SameFacilityEntity(previous, facilities[i])))
            {
                var key = FormattableString.Invariant($"created:{Identity.Id}:facility:{next}");
                next = checked(next + 1);
                facilities[i] = facilities[i] with { PreservationKey = key };
            }
        _nextIds["oxcePortFacility"] = next;
        owner.Facilities.Clear(); owner.Facilities.AddRange(facilities);
        owner.Items.Clear(); foreach (var pair in stock) owner.Items.Add(pair.Key, pair.Value);
        _funds[^1] = funds; _incomes[^1] = income; _expenditures[^1] = spending;
        _logisticsQuote = null;
    }

    private static bool SameFacilityEntity(FacilityState previous, FacilityState current) =>
        ReferenceEquals(previous, current) ||
        previous.Rule == current.Rule && previous.X == current.X && previous.Y == current.Y &&
        (previous.PreservationKey.Length == 0 ||
         string.Equals(previous.PreservationKey, current.PreservationKey, StringComparison.Ordinal));
}
