using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed record TransformCampaignSoldier(int BaseId, int SoldierId, string TransformationRuleId, string? Name = null) : ICampaignCommand;
public sealed record CampaignSoldierTransformed(int BaseId, int SoldierId, string TransformationRuleId, int TransferHours) : ICampaignEvent;
public sealed record CampaignTransformationEventSelected(string RuleId) : ICampaignEvent;

public sealed partial class CampaignState
{
    private static readonly string[] TransformationStats =
    ["tu", "stamina", "health", "bravery", "reactions", "firing", "throwing", "strength", "psiStrength", "psiSkill", "melee", "mana"];

    private CampaignCommandResult TransformSoldier(TransformCampaignSoldier command)
    {
        var owner = FindBase(command.BaseId);
        if (FacilityActionRestriction(owner) is { } restriction) return Blocked(restriction);
        var index = owner.Soldiers.FindIndex(s => s.Id == command.SoldierId);
        if (index < 0 || owner.Soldiers[index].Personal is not { } personal) return Blocked("Soldier was not found or is unresolved.");
        var sourcePersonal = personal!;
        var transformation = _content.RuntimeRules.SoldierTransformations[
            _content.RuntimeRules.SoldierTransformations.GetRequired(command.TransformationRuleId)].Value;
        ulong eventWeight = 0;
        foreach (var pair in transformation.Events)
        {
            if (pair.Value > (ulong)int.MaxValue - eventWeight)
                return Blocked("Transformation event weights exceed the supported random range.");
            eventWeight += pair.Value;
        }
        if (!_debugMode && transformation.Requirements.Any(r => !_completedResearch.Contains(r))) return Blocked("Required research is not complete.");
        if (!HasFunctions(owner, transformation.RequiredBaseFunctions)) return Blocked("Required base functions are unavailable.");
        if (personal.Rank < transformation.Integers["minRank"]) return Blocked("Soldier rank is too low.");
        var wounded = SoldierReadiness.IsWounded(personal, _content.RuntimeRules.Campaign.ManaWoundThreshold,
            _content.RuntimeRules.Campaign.HealthWoundThreshold);
        if (wounded ? !transformation.Booleans["allowsWoundedSoldiers"] : !transformation.Booleans["allowsLiveSoldiers"])
            return Blocked("Soldier health is incompatible with this transformation.");
        var soldierType = _content.RuntimeRules.Soldiers.GetExternalId(owner.Soldiers[index].Rule);
        if (!transformation.AllowedSoldierTypes.Contains(soldierType, StringComparer.Ordinal)) return Blocked("Soldier type is not allowed.");
        if (transformation.RequiredPreviousTransformations.Any(r => !personal.PreviousTransformations.ContainsKey(r)) ||
            transformation.ForbiddenPreviousTransformations.Any(personal.PreviousTransformations.ContainsKey))
            return Blocked("Soldier transformation history is incompatible.");
        if (!StatsEligible(personal, transformation)) return Blocked("Soldier stats are outside transformation requirements.");
        if (transformation.RequiredCommendations.Any(required => !personal.Commendations.Any(c =>
            c.RuleId == required.Key && c.DecorationLevel >= required.Value))) return Blocked("Required commendation is missing.");
        if (_funds[^1] < transformation.Integers["cost"]) return Blocked("STR_NOT_ENOUGH_MONEY");
        foreach (var item in transformation.RequiredItems)
            if (item.Value < 0 || !_content.RuntimeRules.Items.TryGet(item.Key, out var handle) || owner.Items.GetValueOrDefault(handle) < item.Value)
                return Blocked("STR_NOT_ENOUGH_ITEMS");

        var producedItem = transformation.Strings["producedItem"];
        var createsClone = transformation.Booleans["createsClone"];
        var producedType = transformation.Strings["producedSoldierType"];
        var transferHours = transformation.Integers["transferTime"];
        if (transferHours < 0) return Blocked("Transformation transfer time cannot be negative.");
        if (producedItem.Length != 0) _content.RuntimeRules.Items.GetRequired(producedItem);
        if (producedType.Length != 0) _content.RuntimeRules.Soldiers.GetRequired(producedType);
        if (createsClone && producedItem.Length != 0) return Blocked("A transformation cannot both clone a soldier and produce an item.");
        var destinationRuleId = producedType.Length == 0 ? soldierType : producedType;
        var destinationHandle = _content.RuntimeRules.Soldiers.GetRequired(destinationRuleId);
        var destinationRule = _content.RuntimeRules.Soldiers[destinationHandle].Value;
        var destinationArmor = transformation.Booleans["keepSoldierArmor"] ? sourcePersonal.Armor : transformation.Strings["producedSoldierArmor"];
        if (destinationArmor.Length == 0) destinationArmor = _content.RuntimeRules.Armors.GetExternalId(destinationRule.Armor);
        _content.RuntimeRules.Armors.GetRequired(destinationArmor);
        if (sourcePersonal.PreviousTransformations.GetValueOrDefault(command.TransformationRuleId) == int.MaxValue)
            return Blocked("Transformation history exceeds the supported range.");
        var awardedBonus = transformation.Strings["soldierBonusType"];
        if (awardedBonus.Length != 0 && sourcePersonal.TransformationBonuses.GetValueOrDefault(awardedBonus) == int.MaxValue)
            return Blocked("Transformation bonuses exceed the supported range.");

        var nextTransferId = NextTransferId();
        var nextSoldierId = createsClone ? NextSoldierId() : command.SoldierId;
        if (nextTransferId == int.MaxValue || createsClone && nextSoldierId == int.MaxValue) return Blocked("Transformation identity range is exhausted.");
        var funds = _funds[^1]; var income = _incomes[^1]; var spending = _expenditures[^1];
        Account(-(long)transformation.Integers["cost"], ref funds, ref income, ref spending);
        string? selectedEvent = null;
        if (eventWeight != 0)
        {
            var choice = _random.NextInclusive(1, (int)eventWeight);
            foreach (var pair in transformation.Events)
            {
                choice -= (int)pair.Value;
                if (choice > 0) continue;
                selectedEvent = pair.Key;
                break;
            }
        }
        foreach (var item in transformation.RequiredItems)
            ChangeStock(owner.Items, _content.RuntimeRules.Items.GetRequired(item.Key), -item.Value);
        _funds[^1] = funds; _incomes[^1] = income; _expenditures[^1] = spending;

        if (producedItem.Length != 0)
        {
            owner.Soldiers.RemoveAt(index);
            var hours = transferHours > 0 ? transferHours : 1;
            owner.Transfers.Add(new(nextTransferId, hours, CampaignTransferKind.Item, producedItem, 1)
            { PreservationKey = $"{Identity.Id}:transfer:{nextTransferId}" });
            _nextIds["oxcePortTransfer"] = nextTransferId + 1;
            return TransformationEvents(command.SoldierId, hours);
        }

        var result = createsClone
            ? SoldierGeneration.Generate(destinationRule, _content.RuntimeRules.Armors.GetExternalId(destinationRule.Armor), sourcePersonal.Nationality,
                _bases.SelectMany(b => b.Soldiers).Select(s => s.Personal?.Name).OfType<string>().ToHashSet(StringComparer.Ordinal), _random)
            : sourcePersonal;
        result = ApplyTransformationStats(result, sourcePersonal, transformation, destinationRule,
            string.Equals(soldierType, destinationRuleId, StringComparison.Ordinal));
        if (createsClone) result = result with { InitialStats = result.CurrentStats };
        var history = transformation.Booleans["reset"] ? new Dictionary<string, int>(StringComparer.Ordinal) :
            new Dictionary<string, int>(result.PreviousTransformations, StringComparer.Ordinal);
        var bonuses = transformation.Booleans["reset"] ? new Dictionary<string, int>(StringComparer.Ordinal) :
            new Dictionary<string, int>(result.TransformationBonuses, StringComparer.Ordinal);
        foreach (var removed in transformation.RemovedTransformations)
        {
            var count = history.GetValueOrDefault(removed);
            history.Remove(removed);
            if (count > 0 && _content.RuntimeRules.SoldierTransformations.TryGet(removed, out var removedHandle))
            {
                var removedBonus = _content.RuntimeRules.SoldierTransformations[removedHandle].Value.Strings["soldierBonusType"];
                if (removedBonus.Length != 0 && bonuses.TryGetValue(removedBonus, out var existing))
                { if (existing > count) bonuses[removedBonus] = existing - count; else bonuses.Remove(removedBonus); }
            }
        }
        if (!createsClone) history[command.TransformationRuleId] = checked(history.GetValueOrDefault(command.TransformationRuleId) + 1);
        var bonus = transformation.Strings["soldierBonusType"];
        if (bonus.Length != 0) bonuses[bonus] = checked(bonuses.GetValueOrDefault(bonus) + 1);
        var armor = destinationArmor;
        if (!transformation.Booleans["keepSoldierArmor"])
        {
            if (!createsClone && sourcePersonal.Armor != armor && _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(sourcePersonal.Armor)].Value.StoreItem is { } oldArmor)
                ChangeServiceStock(owner, oldArmor, 1);
        }
        result = result with
        {
            Name = command.Name ?? result.Name,
            Armor = armor,
            Rank = transformation.Booleans["resetRank"] || !destinationRule.AllowPromotion ? 0 :
                destinationRule.RankCount == 0 ? result.Rank : Math.Min(result.Rank, destinationRule.RankCount - 1),
            Recovery = transformation.Integers["recoveryTime"] > 0 ? transformation.Integers["recoveryTime"] : result.Recovery,
            CraftType = "",
            CraftId = 0,
            PreviousTransformations = new ReadOnlyDictionary<string, int>(history),
            TransformationBonuses = new ReadOnlyDictionary<string, int>(bonuses),
        };
        var transformed = new SoldierState(destinationHandle, nextSoldierId)
        { Personal = result, PreservationKey = createsClone ? $"{Identity.Id}:soldier:{nextSoldierId}" : owner.Soldiers[index].PreservationKey };
        if (createsClone)
        {
            var sourceHistory = new Dictionary<string, int>(sourcePersonal.PreviousTransformations, StringComparer.Ordinal)
            { [command.TransformationRuleId] = sourcePersonal.PreviousTransformations.GetValueOrDefault(command.TransformationRuleId) + 1 };
            owner.Soldiers[index] = owner.Soldiers[index] with
            { Personal = sourcePersonal with { PreviousTransformations = new ReadOnlyDictionary<string, int>(sourceHistory) } };
        }
        if (!createsClone) owner.Soldiers.RemoveAt(index);
        if (transferHours > 0 || createsClone)
        {
            var hours = transferHours > 0 ? transferHours : 24;
            var snapshot = new SoldierSnapshot(destinationRuleId, transformed.Id)
            {
                Personal = result with
                { PsiTraining = false, Training = false, ReturnToTrainingWhenHealed = result.Training || result.ReturnToTrainingWhenHealed },
                PreservationKey = transformed.PreservationKey
            };
            owner.Transfers.Add(new(nextTransferId, hours, CampaignTransferKind.Soldier, destinationRuleId, 1, snapshot)
            { PreservationKey = $"{Identity.Id}:transfer:{nextTransferId}" });
            _nextIds["oxcePortTransfer"] = nextTransferId + 1;
            if (createsClone) _nextIds["STR_SOLDIER"] = nextSoldierId + 1;
            transferHours = hours;
        }
        else owner.Soldiers.Insert(index, transformed);
        return TransformationEvents(transformed.Id, transferHours);

        CampaignCommandResult TransformationEvents(int soldierId, int hours)
        {
            var events = new List<ICampaignEvent>
            { new CampaignSoldierTransformed(owner.Id, soldierId, command.TransformationRuleId, hours) };
            if (selectedEvent is not null) events.Add(new CampaignTransformationEventSelected(selectedEvent));
            return new(events.AsReadOnly());
        }
    }

    private bool StatsEligible(SoldierPersonalState soldier, RuntimeSoldierTransformationRule rule)
    {
        var minimum = rule.StatSets["requiredMinStats"];
        var maximum = rule.StatSets["requiredMaxStats"];
        var minimumStats = rule.Booleans["includeBonusesForMinStats"] ? StatsWithBonuses(soldier) : soldier.CurrentStats;
        var maximumStats = rule.Booleans["includeBonusesForMaxStats"] ? StatsWithBonuses(soldier) : soldier.CurrentStats;
        foreach (var key in TransformationStats)
        {
            var minimumValue = minimumStats.GetValueOrDefault(key);
            if (minimumValue < minimum.GetValueOrDefault(key) && (key != "psiSkill" || minimum.GetValueOrDefault(key) != 0) ||
                maximumStats.GetValueOrDefault(key) > maximum.GetValueOrDefault(key)) return false;
        }
        return true;
    }

    private Dictionary<string, short> StatsWithBonuses(SoldierPersonalState soldier)
    {
        var stats = new Dictionary<string, short>(soldier.CurrentStats, StringComparer.Ordinal);
        var bonuses = new HashSet<RuleHandle<SoldierBonusRuleFamily>>();
        foreach (var id in soldier.TransformationBonuses.Keys)
            if (_content.RuntimeRules.SoldierBonuses.TryGet(id, out var handle)) bonuses.Add(handle);
        foreach (var commendation in soldier.Commendations)
            if (_content.RuntimeRules.Commendations.TryGet(commendation.RuleId, out var commendationHandle))
            {
                var types = _content.RuntimeRules.Commendations[commendationHandle].Value.SoldierBonusTypes;
                if (types.Count != 0 && _content.RuntimeRules.SoldierBonuses.TryGet(types[Math.Clamp(commendation.DecorationLevel, 0, types.Count - 1)], out var bonus))
                    bonuses.Add(bonus);
            }
        foreach (var bonus in bonuses)
            foreach (var pair in _content.RuntimeRules.SoldierBonuses[bonus].Value.Stats)
                stats[pair.Key] = unchecked((short)(stats.GetValueOrDefault(pair.Key) + pair.Value));
        return stats;
    }

    private SoldierPersonalState ApplyTransformationStats(SoldierPersonalState destination, SoldierPersonalState source,
        RuntimeSoldierTransformationRule transformation, RuntimeSoldierRule destinationRule, bool isSameSoldierType)
    {
        var stats = new Dictionary<string, short>(destination.CurrentStats, StringComparer.Ordinal);
        foreach (var key in TransformationStats)
        {
            var current = source.CurrentStats.GetValueOrDefault(key);
            var gained = current - source.InitialStats.GetValueOrDefault(key);
            var change = (int)transformation.StatSets["flatOverallStatChange"].GetValueOrDefault(key);
            change += RandomBetween(transformation.StatSets["flatMin"].GetValueOrDefault(key), transformation.StatSets["flatMax"].GetValueOrDefault(key));
            change += current * transformation.StatSets["percentOverallStatChange"].GetValueOrDefault(key) / 100;
            change += current * RandomBetween(transformation.StatSets["percentMin"].GetValueOrDefault(key), transformation.StatSets["percentMax"].GetValueOrDefault(key)) / 100;
            change += gained * transformation.StatSets["percentGainedStatChange"].GetValueOrDefault(key) / 100;
            change += gained * RandomBetween(transformation.StatSets["percentGainedMin"].GetValueOrDefault(key), transformation.StatSets["percentGainedMax"].GetValueOrDefault(key)) / 100;
            if (key == "bravery") change = ((change + (change < 0 ? -5 : 5)) / 10) * 10;
            if (transformation.Booleans["lowerBoundAtMinStats"]) change = Math.Max(change, destinationRule.MinimumStats.GetValueOrDefault(key) - current);
            if (transformation.Booleans["upperBoundAtMaxStats"] || transformation.Booleans["upperBoundAtStatCaps"])
            {
                var upper = (transformation.Booleans["upperBoundAtMaxStats"] ? destinationRule.MaximumStats : destinationRule.StatCaps).GetValueOrDefault(key);
                var boundType = transformation.Integers["upperBoundType"];
                var softLimit = boundType == 1 || boundType == 0 && isSameSoldierType;
                change = softLimit
                    ? change <= 0 ? change : current <= upper ? Math.Min(change, upper - current) : 0
                    : Math.Min(change, upper - current);
            }
            stats[key] = transformation.StatSets["rerollStats"].GetValueOrDefault(key) != 0
                ? destination.CurrentStats.GetValueOrDefault(key) : unchecked((short)(current + change));
        }
        return destination with { CurrentStats = new ReadOnlyDictionary<string, short>(stats) };

        int RandomBetween(int left, int right) => left == right ? left : _random.NextInclusive(Math.Min(left, right), Math.Max(left, right));
    }
}
