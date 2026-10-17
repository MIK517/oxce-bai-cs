using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>A facility type a missile strike destroyed, and how many (Base::getDestroyedFacilitiesCache).</summary>
public sealed record DestroyedFacilityCount(string RuleId, int Count);

public sealed partial class CampaignState
{
    /// <summary>
    /// Base::damageFacilities: missiles worth <paramref name="missilePower"/> hit facilities chosen by their
    /// missile attraction (never the lift) until the power is spent or only indestructible facilities remain.
    /// A hit replaces the facility with its destroyed form (or the mod's 1x1 destroyed facility per cell) and
    /// then destroys it; without a mod destroyed facility, parts cut off from the lift are destroyed too.
    /// Returns the destroyed facilities by rule and whether the stores are now over capacity.
    /// </summary>
    internal (IReadOnlyList<DestroyedFacilityCount> Destroyed, bool StoresOverfull) DamageBaseFacilities(BaseState owner, int missilePower,
        Action<CraftState> craftDestroyed)
    {
        var rules = _content.RuntimeRules;
        var hangars = HangarCrafts(owner);
        var destroyed = new Dictionary<RuleHandle<FacilityRuleFamily>, int>();
        for (var spent = 0; spent < missilePower;)
        {
            var options = new Dictionary<string, ulong>(StringComparer.Ordinal);
            for (var index = 0; index < owner.Facilities.Count; index++)
            {
                var rule = FacilityRule(owner.Facilities[index]);
                if (rule.MissileAttraction > 0 && !rule.Lift)
                    options[index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = (ulong)rule.MissileAttraction;
            }
            // Only indestructible facilities remain.
            if (options.Count == 0) break;
            var selected = int.Parse(World.WorldAlienMissionGeneration.Choose(options, _random), System.Globalization.CultureInfo.InvariantCulture);
            spent += DamageFacility(owner, owner.Facilities[selected], hangars, destroyed, craftDestroyed);
        }
        if (rules.Campaign.DestroyedFacility is null)
        {
            var disconnected = DisconnectedFacilities(owner);
            for (var index = disconnected.Count - 1; index >= 0; index--)
                DestroyFacility(owner, disconnected[index], hangars, destroyed, craftDestroyed);
        }
        var counts = rules.Facilities.Rules.Select(rule => rules.Facilities.GetRequired(rule.Id))
            .Where(destroyed.ContainsKey).Select(handle => new DestroyedFacilityCount(rules.Facilities.GetExternalId(handle), destroyed[handle]))
            .ToArray();
        return (counts, StoresOverfull(owner, 0));
    }

    /// <summary>
    /// The crafts BaseView::draw places in each built hangar, in facility order: crafts not out, in craft
    /// order, of the hangar's type, one per distinct craft slot. The reference keeps these only from the
    /// last base view drawing; the port places them as that drawing would now (ADR 0034).
    /// </summary>
    private Dictionary<FacilityState, List<CraftState>> HangarCrafts(BaseState owner)
    {
        var result = new Dictionary<FacilityState, List<CraftState>>(ReferenceEqualityComparer.Instance);
        var assigned = new HashSet<CraftState>(ReferenceEqualityComparer.Instance);
        foreach (var facility in owner.Facilities)
        {
            var rule = FacilityRule(facility);
            if (facility.BuildTime != 0 || rule.Crafts <= 0) continue;
            var placed = new List<CraftState>();
            foreach (var craft in owner.Crafts)
            {
                if (placed.Count == rule.DrawnCraftSlots) break;
                if (craft.Logistics?.Status == "STR_OUT" || assigned.Contains(craft) ||
                    _content.RuntimeRules.Crafts[craft.Rule].Value.HangarType != rule.HangarType) continue;
                placed.Add(craft);
                assigned.Add(craft);
            }
            result[facility] = placed;
        }
        return result;
    }

    // Base::damageFacility: returns the missile power the hit spent (the facility's size).
    private int DamageFacility(BaseState owner, FacilityState target, Dictionary<FacilityState, List<CraftState>> hangars,
        Dictionary<RuleHandle<FacilityRuleFamily>, int> destroyed, Action<CraftState> craftDestroyed)
    {
        var rules = _content.RuntimeRules;
        var rule = FacilityRule(target);
        if (rule.DestroyedFacility is { } replacement)
        {
            var damaged = new FacilityState(replacement, target.X, target.Y, 0, 0, false, false, false);
            owner.Facilities.Add(damaged);
            // The crafts move from the original hangar to the damaged one.
            if (rules.Facilities[replacement].Value.Crafts > 0 && hangars.Remove(target, out var crafts)) hangars[damaged] = crafts;
        }
        else if (rules.Campaign.DestroyedFacility is { } rubble)
        {
            for (var x = 0; x < rule.SizeX; x++)
                for (var y = 0; y < rule.SizeY; y++)
                    owner.Facilities.Add(new FacilityState(rubble, target.X + x, target.Y + y, 0, 0, false, false, false));
        }
        DestroyFacility(owner, target, hangars, destroyed, craftDestroyed);
        return rule.SizeX * rule.SizeY;
    }

    // Base::destroyFacility: the facility's loss ripples through crafts, training, research and transfers.
    private void DestroyFacility(BaseState owner, FacilityState facility, Dictionary<FacilityState, List<CraftState>> hangars,
        Dictionary<RuleHandle<FacilityRuleFamily>, int> destroyed, Action<CraftState> craftDestroyed)
    {
        var rules = _content.RuntimeRules;
        var rule = FacilityRule(facility);
        if (rule.Crafts > 0)
        {
            if (hangars.Remove(facility, out var crafts) && crafts.Count != 0)
            {
                // The crafts in the hangar are lost: their crew stay at the base and their cargo goes to the stores.
                foreach (var craft in crafts)
                {
                    var type = rules.Crafts.GetExternalId(craft.Rule);
                    for (var index = 0; index < owner.Soldiers.Count; index++)
                        if (owner.Soldiers[index].Personal is { } personal && personal.CraftType == type && personal.CraftId == craft.Id)
                            owner.Soldiers[index] = owner.Soldiers[index] with { Personal = personal with { CraftType = "", CraftId = 0 } };
                    if (craft.Logistics is { } state)
                        foreach (var (item, quantity) in state.Items)
                        {
                            var handle = rules.Items.GetRequired(item);
                            owner.Items[handle] = checked(owner.Items.GetValueOrDefault(handle) + quantity);
                        }
                    owner.Crafts.Remove(craft);
                    craftDestroyed(craft);
                }
            }
            else
            {
                // No crafts in the hangar: productions, then incoming crafts, beyond the remaining hangars go.
                var remove = -(AvailableHangars(owner, rule.HangarType) - UsedHangars(owner, rule.HangarType) - rule.Crafts);
                remove = DeleteIf(owner.Productions, remove, production =>
                {
                    if (ProducedCraft(rules.Manufacture[production.Rule].Value) is not { } craft ||
                        rules.Crafts[craft].Value.HangarType != rule.HangarType) return false;
                    owner.Engineers += production.Assigned;
                    return true;
                });
                DeleteIf(owner.Transfers, remove, transfer => !transfer.Delivered && transfer.Kind == CampaignTransferKind.Craft &&
                    transfer.Craft is { } incoming && rules.Crafts[rules.Crafts.GetRequired(incoming.RuleId)].Value.HangarType == rule.HangarType);
            }
        }
        if (rule.PsiLaboratories > 0)
        {
            // Psi labs: take the soldiers beyond the remaining space out of psi training.
            var toRemove = rule.PsiLaboratories - (AvailablePsiLabs(owner) - UsedPsiLabs(owner));
            for (var index = 0; index < owner.Soldiers.Count && toRemove > 0; index++)
                if (owner.Soldiers[index].Personal is { PsiTraining: true } personal)
                {
                    owner.Soldiers[index] = owner.Soldiers[index] with { Personal = personal with { PsiTraining = false } };
                    toRemove--;
                }
        }
        if (rule.TrainingRooms > 0)
        {
            var available = owner.Facilities.Where(f => f.BuildTime == 0).Sum(f => FacilityRule(f).TrainingRooms);
            var toRemove = rule.TrainingRooms - (available - owner.Soldiers.Count(s => s.Personal?.Training == true));
            for (var index = 0; index < owner.Soldiers.Count && toRemove > 0; index++)
                if (owner.Soldiers[index].Personal is { Training: true } personal)
                {
                    owner.Soldiers[index] = owner.Soldiers[index] with { Personal = personal with { Training = false } };
                    toRemove--;
                }
        }
        if (rule.Laboratories != 0)
        {
            // Laboratories: scientists leave projects until the space fits; research is not cancelled.
            var available = owner.Facilities.Where(f => f.BuildTime == 0).Sum(f => FacilityRule(f).Laboratories);
            var toRemove = rule.Laboratories - (available - owner.Research.Sum(project => project.Assigned));
            for (var index = 0; index < owner.Research.Count && toRemove > 0; index++)
            {
                var project = owner.Research[index];
                if (project.Assigned >= toRemove)
                {
                    owner.Research[index] = project with { Assigned = project.Assigned - toRemove };
                    owner.Scientists += toRemove;
                    break;
                }
                toRemove -= project.Assigned;
                owner.Scientists += project.Assigned;
                owner.Research[index] = project with { Assigned = 0 };
            }
        }
        if (rule.Workshops != 0)
        {
            // Workshops: engineers leave productions, and a production left without them is cancelled.
            var available = owner.Facilities.Where(f => f.BuildTime == 0).Sum(f => FacilityRule(f).Workshops);
            var toRemove = rule.Workshops - (available - UsedWorkshopSpace(owner));
            for (var index = 0; index < owner.Productions.Count && toRemove > 0;)
            {
                var production = owner.Productions[index];
                if (production.Assigned > toRemove)
                {
                    owner.Productions[index] = production with { Assigned = production.Assigned - toRemove };
                    owner.Engineers += toRemove;
                    toRemove = 0;
                    break;
                }
                owner.Engineers += production.Assigned;
                toRemove -= production.Assigned;
                owner.Productions.RemoveAt(index);
            }
        }
        if (rule.Storage != 0 && StoresOverfull(owner, rule.Storage))
            // Items in transit end up at the dead letter office.
            owner.Transfers.RemoveAll(static transfer => !transfer.Delivered && transfer.Kind == CampaignTransferKind.Item);
        if (rule.Personnel != 0 && AvailableQuarters(owner) - UsedQuarters(owner) - rule.Personnel < 0)
            owner.Transfers.RemoveAll(static transfer => !transfer.Delivered &&
                transfer.Kind is CampaignTransferKind.Engineer or CampaignTransferKind.Scientist);
        destroyed[facility.Rule] = destroyed.GetValueOrDefault(facility.Rule) + 1;
        owner.Facilities.Remove(facility);
    }

    // Base::storesOverfull: compared in hundredths of a store unit, the used space truncated.
    private bool StoresOverfull(BaseState owner, double offset) =>
        (long)((UsedStores(owner) + offset) * 100) > AvailableStores(owner) * 100L;

    private int AvailablePsiLabs(BaseState owner) => owner.Facilities.Where(f => f.BuildTime == 0).Sum(f => FacilityRule(f).PsiLaboratories);

    // Base::getUsedPsiLabs counts soldiers coming home in psi training too.
    private static int UsedPsiLabs(BaseState owner) => owner.Soldiers.Count(s => s.Personal?.PsiTraining == true) +
        owner.Transfers.Count(t => !t.Delivered && t.Kind == CampaignTransferKind.Soldier && t.Soldier?.Personal?.PsiTraining == true);

    // Collections::deleteIf: removes up to count matching entries, stops testing once none are left to
    // remove, and returns how many could not be removed.
    private static int DeleteIf<T>(List<T> values, int count, Func<T, bool> remove)
    {
        if (count <= 0) return 0;
        for (var index = 0; index < values.Count && count > 0;)
        {
            if (remove(values[index]))
            {
                values.RemoveAt(index);
                count--;
            }
            else index++;
        }
        return count;
    }

    /// <summary>
    /// Base::getDisconnectedFacilities(0): the facilities not reachable from the lift through built (or
    /// replaced) facilities and queued neighbours, in facility order without repeats.
    /// </summary>
    private List<FacilityState> DisconnectedFacilities(BaseState owner)
    {
        var cells = new List<(FacilityState Facility, bool Connected)>();
        var grid = new int?[BaseGridSize, BaseGridSize];
        FacilityState? lift = null;
        foreach (var facility in owner.Facilities)
        {
            var rule = FacilityRule(facility);
            if (rule.Lift) lift = facility;
            for (var x = 0; x < rule.SizeX; x++)
                for (var y = 0; y < rule.SizeY; y++)
                {
                    cells.Add((facility, false));
                    grid[facility.X + x, facility.Y + y] = cells.Count - 1;
                }
        }
        if (lift is null) return [];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((lift.X, lift.Y));
        while (stack.Count != 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || x >= BaseGridSize || y < 0 || y >= BaseGridSize || grid[x, y] is not { } cell || cells[cell].Connected) continue;
            cells[cell] = (cells[cell].Facility, true);
            var facility = cells[cell].Facility;
            var open = facility.BuildTime == 0 || facility.HadPreviousFacility;
            if (open || Queued(x - 1, y)) stack.Push((x - 1, y));
            if (open || Queued(x + 1, y)) stack.Push((x + 1, y));
            if (open || Queued(x, y - 1)) stack.Push((x, y - 1));
            if (open || Queued(x, y + 1)) stack.Push((x, y + 1));

            bool Queued(int nx, int ny)
            {
                if (nx < 0 || nx >= BaseGridSize || ny < 0 || ny >= BaseGridSize || grid[nx, ny] is not { } neighbour) return false;
                var other = cells[neighbour].Facility;
                return ReferenceEquals(other, facility) || other.BuildTime > FacilityRule(other).BuildTime;
            }
        }
        var result = new List<FacilityState>();
        FacilityState? last = null;
        foreach (var (facility, connected) in cells)
        {
            if (!ReferenceEquals(facility, last) && !connected) result.Add(facility);
            last = facility;
        }
        return result;
    }
}
