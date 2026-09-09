using System.Collections.ObjectModel;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed record SoldierCommendation(string RuleId, string Noun, int DecorationLevel);

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
    public int Missions { get; init; }
    public int Kills { get; init; }
    public int Stuns { get; init; }
    public int ManaMissing { get; init; }
    public int HealthMissing { get; init; }
    public int Improvement { get; init; }
    public int PsiStrImprovement { get; init; }
    public bool AllowAutoCombat { get; init; } = true;
    public bool IsLeeroyJenkins { get; init; } = true;
    public bool CorpseRecovered { get; init; }
    public string ReplacedArmor { get; init; } = string.Empty;
    public string TransformedArmor { get; init; } = string.Empty;
    public string PersonalEquipmentArmor { get; init; } = string.Empty;
    public float Recovery { get; init; }
    public IReadOnlyDictionary<string, int> PreviousTransformations { get; init; } = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
    public IReadOnlyDictionary<string, int> TransformationBonuses { get; init; } = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
    public IReadOnlyList<SoldierCommendation> Commendations { get; init; } = [];
}

/// <summary>Initial state from Soldier::Soldier and Mod::genSoldier at reference 4df3a5e.</summary>
public static class SoldierGeneration
{
    public static string? GenerationRestriction(RuntimeSoldierRule rule)
    {
        if (rule.MinimumStats.Any(p => p.Key != "psiSkill" && (p.Key == "bravery"
            ? p.Value / 10 > rule.MaximumStats.GetValueOrDefault(p.Key) / 10
            : p.Value > rule.MaximumStats.GetValueOrDefault(p.Key)))) return "Soldier generation has inverted stat bounds.";
        if (rule.NamePools.Sum(p => (long)p.GlobalWeight) > int.MaxValue)
            return "Soldier nationality weights exceed the random range.";
        foreach (var pool in rule.NamePools)
        {
            if (pool.LookWeights.Take(4).Sum(w => (long)w) + Math.Max(0, 4 - pool.LookWeights.Count) * 2L > int.MaxValue)
                return "Soldier look weights exceed the random range.";
            var malePossible = (pool.FemaleFrequency > -1 ? pool.FemaleFrequency : rule.FemaleFrequency) < 100;
            var callsignPool = pool.FemaleCallsign.Count == 0 ? rule.NamePools[0] : pool;
            if (malePossible && callsignPool.FemaleCallsign.Count != 0 && callsignPool.MaleCallsign.Count == 0)
                return "A possible male recruit has no enabled callsign pool.";
        }
        return null;
    }

    private static readonly string[] GeneratedStats = ["tu", "stamina", "health", "mana", "bravery", "reactions",
        "firing", "throwing", "strength", "psiStrength", "melee"];

    public static SoldierPersonalState LoadStarting(RuntimeSoldierRule rule, RuntimeSoldierTemplate? template,
        RuntimeRuleCatalog rules, IRandomSource random, bool autoCombatDefault = true)
    {
        var zero = new ReadOnlyDictionary<string, short>(GeneratedStats.Append("psiSkill")
            .ToDictionary(key => key, _ => (short)0, StringComparer.Ordinal));
        var state = new SoldierPersonalState("", "", 0, 0, 0, random.NextExclusive(64), "", zero, zero)
        { AllowAutoCombat = autoCombatDefault };
        template ??= new RuntimeSoldierTemplate(new Dictionary<string, int>(), new Dictionary<string, bool>(),
            new Dictionary<string, string>(), zero, zero, null, []);
        return ApplyTemplate(state, template, rule, rules, random, mergeStats: false);
    }

    public static SoldierPersonalState Generate(RuntimeSoldierRule rule, string armor, int nationality,
        IReadOnlySet<string> existingNames, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentNullException.ThrowIfNull(random);
        if (GenerationRestriction(rule) is { } restriction) throw new InvalidDataException(restriction);
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

    public static SoldierPersonalState NormalizeNationality(SoldierPersonalState state, RuntimeSoldierRule rule, IRandomSource random)
    {
        if (rule.NamePools.Count == 0) return state with { Nationality = 0 };
        return (uint)state.Nationality < (uint)rule.NamePools.Count
            ? state
            : state with { Nationality = random.NextExclusive(rule.NamePools.Count) };
    }

    public static SoldierPersonalState ApplyTemplate(SoldierPersonalState state, RuntimeSoldierTemplate template,
        RuntimeSoldierRule rule, RuntimeRuleCatalog rules, IRandomSource random, bool mergeStats = true)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.UnsupportedFields.Count != 0) throw new InvalidOperationException("Soldier template was not preflighted.");
        var updated = state with
        {
            Nationality = template.Integers.GetValueOrDefault("nationality", state.Nationality),
            Rank = template.Integers.GetValueOrDefault("rank", state.Rank),
            Gender = template.Integers.GetValueOrDefault("gender", state.Gender),
            Look = template.Integers.GetValueOrDefault("look", state.Look),
            LookVariant = template.Integers.GetValueOrDefault("lookVariant", state.LookVariant),
            Missions = template.Integers.GetValueOrDefault("missions", state.Missions),
            Kills = template.Integers.GetValueOrDefault("kills", state.Kills),
            Stuns = template.Integers.GetValueOrDefault("stuns", state.Stuns),
            ManaMissing = template.Integers.GetValueOrDefault("manaMissing", state.ManaMissing),
            HealthMissing = template.Integers.GetValueOrDefault("healthMissing", state.HealthMissing),
            Improvement = template.Integers.GetValueOrDefault("improvement", state.Improvement),
            PsiStrImprovement = template.Integers.GetValueOrDefault("psiStrImprovement", state.PsiStrImprovement),
            PsiTraining = template.Booleans.GetValueOrDefault("psiTraining", state.PsiTraining),
            Training = template.Booleans.GetValueOrDefault("training", state.Training),
            ReturnToTrainingWhenHealed = template.Booleans.GetValueOrDefault("returnToTrainingWhenHealed", state.ReturnToTrainingWhenHealed),
            AllowAutoCombat = template.Booleans.GetValueOrDefault("allowAutoCombat", state.AllowAutoCombat),
            IsLeeroyJenkins = template.Booleans.GetValueOrDefault("isLeeroyJenkins", state.IsLeeroyJenkins),
            CorpseRecovered = template.Booleans.GetValueOrDefault("corpseRecovered", state.CorpseRecovered),
            Name = template.Strings.GetValueOrDefault("name", state.Name),
            Callsign = template.Strings.GetValueOrDefault("callsign", state.Callsign),
            Armor = template.Strings.GetValueOrDefault("armor", state.Armor),
            ReplacedArmor = template.Strings.GetValueOrDefault("replacedArmor", state.ReplacedArmor),
            TransformedArmor = template.Strings.GetValueOrDefault("transformedArmor", state.TransformedArmor),
            PersonalEquipmentArmor = template.Strings.GetValueOrDefault("personalEquipmentArmor", state.PersonalEquipmentArmor),
            Recovery = template.Recovery ?? state.Recovery,
            InitialStats = Merge(state.InitialStats, template.InitialStats),
            CurrentStats = Merge(state.CurrentStats, template.CurrentStats),
            PreviousTransformations = template.PreviousTransformations ?? state.PreviousTransformations,
            TransformationBonuses = template.TransformationBonuses ?? state.TransformationBonuses,
        };
        if (!rules.Armors.TryGet(updated.Armor, out _))
            updated = updated with { Armor = rules.Armors.GetExternalId(rules.Soldiers.Rules[0].Value.Armor) };
        if (updated.CurrentStats.GetValueOrDefault("mana") == 0 && rule.MaximumStats.GetValueOrDefault("mana") > 0)
        {
            var mana = checked((short)random.NextInclusive(rule.MinimumStats.GetValueOrDefault("mana"), rule.MaximumStats.GetValueOrDefault("mana")));
            var initial = new Dictionary<string, short>(updated.InitialStats, StringComparer.Ordinal) { ["mana"] = mana };
            var current = new Dictionary<string, short>(updated.CurrentStats, StringComparer.Ordinal) { ["mana"] = mana };
            updated = updated with { InitialStats = new ReadOnlyDictionary<string, short>(initial), CurrentStats = new ReadOnlyDictionary<string, short>(current) };
        }
        if (template.RandomTransformationBonuses is { Count: > 0 } weights && template.TransformationBonusesCount > 0)
        {
            var choices = new SortedDictionary<string, int>(weights.Where(p => p.Value > 0).ToDictionary(), StringComparer.Ordinal);
            var bonuses = new Dictionary<string, int>(updated.TransformationBonuses, StringComparer.Ordinal);
            for (var i = 0; i < template.TransformationBonusesCount && choices.Count > 0; i++)
            {
                var choice = random.NextInclusive(1, choices.Values.Sum());
                string? selected = null;
                foreach (var pair in choices)
                {
                    if (choice <= pair.Value) { selected = pair.Key; break; }
                    choice -= pair.Value;
                }
                if (selected is null) throw new InvalidOperationException("Random source returned an invalid weighted choice.");
                choices.Remove(selected);
                if (selected.Length != 0 && selected != "\0") bonuses[selected] = checked(bonuses.GetValueOrDefault(selected) + 1);
            }
            updated = updated with { TransformationBonuses = new ReadOnlyDictionary<string, int>(bonuses) };
        }
        return updated;

        ReadOnlyDictionary<string, short> Merge(IReadOnlyDictionary<string, short> original, IReadOnlyDictionary<string, short> overlay)
        {
            var merged = new Dictionary<string, short>(original, StringComparer.Ordinal);
            foreach (var key in original.Keys)
                if (overlay.TryGetValue(key, out var value) && (!mergeStats || value != 0))
                    merged[key] = mergeStats && value == -1 ? (short)0 : value;
            return new(merged);
        }
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
