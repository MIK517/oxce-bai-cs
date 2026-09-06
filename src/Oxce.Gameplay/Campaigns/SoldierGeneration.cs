using System.Collections.ObjectModel;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed record SoldierPersonalState(
    string Name, string Callsign, int Nationality, int Gender, int Look, int LookVariant,
    string Armor, IReadOnlyDictionary<string, short> InitialStats, IReadOnlyDictionary<string, short> CurrentStats)
{
    public int Rank { get; init; }
    public bool PsiTraining { get; init; }
    public bool Training { get; init; }
    public bool ReturnToTrainingWhenHealed { get; init; }
    public string CraftType { get; init; } = string.Empty;
    public int CraftId { get; init; }
}

/// <summary>Initial state from Soldier::Soldier and Mod::genSoldier at reference 4df3a5e.</summary>
public static class SoldierGeneration
{
    private static readonly string[] GeneratedStats = ["tu", "stamina", "health", "mana", "bravery", "reactions",
        "firing", "throwing", "strength", "psiStrength", "melee"];

    public static SoldierPersonalState Generate(RuntimeSoldierRule rule, string armor, int nationality,
        IReadOnlySet<string> existingNames, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentNullException.ThrowIfNull(random);
        SoldierPersonalState result;
        var tries = 0;
        do
        {
            var stats = new Dictionary<string, short>(StringComparer.Ordinal);
            foreach (var key in GeneratedStats)
            {
                var minimum = rule.MinimumStats.GetValueOrDefault(key);
                var maximum = rule.MaximumStats.GetValueOrDefault(key);
                var value = key == "bravery" ? random.NextInclusive(minimum / 10, maximum / 10) * 10 : random.NextInclusive(minimum, maximum);
                stats.Add(key, checked((short)value));
            }
            stats.Add("psiSkill", rule.MinimumStats.GetValueOrDefault("psiSkill"));
            var frozen = new ReadOnlyDictionary<string, short>(stats);
            var identity = GenerateName(rule, nationality, random);
            result = new(identity.Name, identity.Callsign, identity.Nationality, identity.Gender,
                identity.Look, random.NextExclusive(64), armor, frozen, frozen);
        } while (++tries < 10 && existingNames.Contains(result.Name));
        return result;
    }

    public static SoldierPersonalState RegenerateName(SoldierPersonalState state, RuntimeSoldierRule rule, IRandomSource random)
    {
        if (rule.NamePools.Count == 0) return state with { Nationality = 0 };
        var nationality = (uint)state.Nationality >= (uint)rule.NamePools.Count ? random.NextExclusive(rule.NamePools.Count) : state.Nationality;
        var name = GenerateName(rule, nationality, random);
        return state with { Name = name.Name, Callsign = name.Callsign, Nationality = name.Nationality, Gender = name.Gender, Look = name.Look };
    }

    private static (string Name, string Callsign, int Nationality, int Gender, int Look) GenerateName(
        RuntimeSoldierRule rule, int nationality, IRandomSource random)
    {
        var names = rule.NamePools;
        if (names.Count == 0)
        {
            var female = Percent(rule.FemaleFrequency, random);
            return (female ? "Jane Doe" : "John Doe", "", 0, female ? 1 : 0, random.NextExclusive(4));
        }
        if (nationality < 0)
        {
            var choice = random.NextInclusive(1, names.Sum(n => n.GlobalWeight));
            nationality = 0;
            while (choice > names[nationality].GlobalWeight) choice -= names[nationality++].GlobalWeight;
        }
        if (nationality >= names.Count) nationality = random.NextExclusive(names.Count);
        var pool = names[nationality];
        var gender = Percent(pool.FemaleFrequency > -1 ? pool.FemaleFrequency : rule.FemaleFrequency, random) ? 1 : 0;
        var first = gender == 1 ? pool.FemaleFirst : pool.MaleFirst;
        var last = gender == 1 ? pool.FemaleLast : pool.MaleLast;
        var name = first[random.NextExclusive(first.Count)];
        if (last.Count != 0) name += " " + last[random.NextExclusive(last.Count)];
        var callsign = Callsign(pool);
        if (callsign.Length == 0) callsign = Callsign(names[0]);
        Span<int> weights = stackalloc int[4];
        var total = 0;
        for (var i = 0; i < weights.Length; i++) total = checked(total + (weights[i] = i < pool.LookWeights.Count ? pool.LookWeights[i] : 2));
        var look = 0;
        if (total <= 0) look = random.NextExclusive(4);
        else
        {
            var choice = random.NextInclusive(1, total);
            while (choice > weights[look]) choice -= weights[look++];
        }
        return (name, callsign, nationality, gender, look);

        string Callsign(RuntimeSoldierNamePool source)
        {
            if (source.FemaleCallsign.Count == 0) return string.Empty;
            var choices = gender == 1 ? source.FemaleCallsign : source.MaleCallsign;
            if (choices.Count == 0) throw new InvalidDataException("Male callsign pool is empty while female callsigns are enabled.");
            return choices[random.NextExclusive(choices.Count)];
        }
    }

    private static bool Percent(int chance, IRandomSource random) => random.NextInclusive(0, 99) < chance;
}
