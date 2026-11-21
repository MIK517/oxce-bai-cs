using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets;

/// <summary>The base statistics a stat bonus term can read (<c>UnitStats</c> fields).</summary>
public enum StatBonusBaseStat
{
    TimeUnits,
    Stamina,
    Health,
    Bravery,
    Reactions,
    Firing,
    Throwing,
    Strength,
    PsiStrength,
    PsiSkill,
    Melee,
    Mana,
}

/// <summary>
/// What a stat bonus reads of a unit (the functions of <c>statDataMap</c> in <c>RuleStatBonus.cpp</c> take a <c>BattleUnit</c>): its base
/// statistics, current values, rank and basic energy regeneration.
/// </summary>
public interface IStatBonusUnit
{
    /// <summary>BattleUnit::getBaseStats.</summary>
    int BaseStat(StatBonusBaseStat stat);

    int TimeUnits { get; }
    int Health { get; }
    int Mana { get; }
    int Energy { get; }
    int Morale { get; }
    int StunLevel { get; }

    /// <summary>BattleUnit::getFatalWounds: the wounds of all body parts.</summary>
    int FatalWoundCount { get; }

    /// <summary>BattleUnit::getRankInt and getRankIntUnified.</summary>
    int RankInt { get; }
    int RankIntUnified { get; }

    /// <summary><c>basicEnergyRegeneration</c>: a soldier's initial time units over three, else the unit rule's <c>energyRecovery</c>.</summary>
    int EnergyRegeneration { get; }
}

/// <summary>One term of a stat bonus: a statistic of <c>statDataMap</c> and the coefficients of its polynomial (the first four, as the reference reads them).</summary>
public sealed record StatBonusTerm(string Stat, IReadOnlyList<float> Coefficients);

/// <summary>
/// <c>RuleStatBonus</c> (<c>RuleStatBonus.cpp</c>): a polynomial of unit statistics. A mapping lists terms (kept in the order of
/// <c>statDataMap</c>, unknown names dropped); written as script code, <see cref="Script"/> holds the source, which only the tactical script
/// bindings (Phase 7 branch 8) run, and <see cref="Evaluate"/> refuses. <see cref="Modded"/> is <c>isModded</c>: a mod wrote the key (or
/// <c>strengthApplied</c> or <c>skillApplied</c> changed the default).
/// </summary>
public sealed record RuleStatBonus(IReadOnlyList<StatBonusTerm> Terms, string? Script = null, bool Modded = false)
{
    /// <summary>The statistics of <c>statDataMap</c>, in its order, with the function that reads each.</summary>
    internal static readonly (string Name, Func<IStatBonusUnit, float> Value)[] StatDataMap =
    [
        ("flatOne", static _ => 1),
        ("flatHundred", static _ => 100),
        ("strength", Single(StatBonusBaseStat.Strength)),
        ("psi", Product(StatBonusBaseStat.PsiSkill, StatBonusBaseStat.PsiStrength, 1)),
        ("psiSkill", Single(StatBonusBaseStat.PsiSkill)),
        ("psiStrength", Single(StatBonusBaseStat.PsiStrength)),
        ("throwing", Single(StatBonusBaseStat.Throwing)),
        ("bravery", Single(StatBonusBaseStat.Bravery)),
        ("firing", Single(StatBonusBaseStat.Firing)),
        ("health", Single(StatBonusBaseStat.Health)),
        ("mana", Single(StatBonusBaseStat.Mana)),
        ("tu", Single(StatBonusBaseStat.TimeUnits)),
        ("reactions", Single(StatBonusBaseStat.Reactions)),
        ("stamina", Single(StatBonusBaseStat.Stamina)),
        ("melee", Single(StatBonusBaseStat.Melee)),
        ("strengthMelee", Product(StatBonusBaseStat.Strength, StatBonusBaseStat.Melee, 1)),
        ("strengthThrowing", Product(StatBonusBaseStat.Strength, StatBonusBaseStat.Throwing, 1)),
        ("firingReactions", Product(StatBonusBaseStat.Firing, StatBonusBaseStat.Reactions, 1)),

        ("strengthScaled", Single(StatBonusBaseStat.Strength, 100)),
        ("psiScaled", Product(StatBonusBaseStat.PsiSkill, StatBonusBaseStat.PsiStrength, 10000)),
        ("psiSkillScaled", Single(StatBonusBaseStat.PsiSkill, 100)),
        ("psiStrengthScaled", Single(StatBonusBaseStat.PsiStrength, 100)),
        ("throwingScaled", Single(StatBonusBaseStat.Throwing, 100)),
        ("braveryScaled", Single(StatBonusBaseStat.Bravery, 100)),
        ("firingScaled", Single(StatBonusBaseStat.Firing, 100)),
        ("healthScaled", Single(StatBonusBaseStat.Health, 100)),
        ("manaScaled", Single(StatBonusBaseStat.Mana, 100)),
        ("tuScaled", Single(StatBonusBaseStat.TimeUnits, 100)),
        ("reactionsScaled", Single(StatBonusBaseStat.Reactions, 100)),
        ("staminaScaled", Single(StatBonusBaseStat.Stamina, 100)),
        ("meleeScaled", Single(StatBonusBaseStat.Melee, 100)),
        ("strengthMeleeScaled", Product(StatBonusBaseStat.Strength, StatBonusBaseStat.Melee, 10000)),
        ("strengthThrowingScaled", Product(StatBonusBaseStat.Strength, StatBonusBaseStat.Throwing, 10000)),
        ("firingReactionsScaled", Product(StatBonusBaseStat.Firing, StatBonusBaseStat.Reactions, 10000)),

        ("rank", static unit => unit.RankInt),
        ("rankUnified", static unit => unit.RankIntUnified),
        ("fatalWounds", static unit => unit.FatalWoundCount),

        ("healthCurrent", static unit => unit.Health),
        ("manaCurrent", static unit => unit.Mana),
        ("tuCurrent", static unit => unit.TimeUnits),
        ("energyCurrent", static unit => unit.Energy),
        ("moraleCurrent", static unit => unit.Morale),
        ("stunCurrent", static unit => unit.StunLevel),

        ("healthNormalized", static unit => Normalized(unit.Health, unit.BaseStat(StatBonusBaseStat.Health))),
        ("manaNormalized", static unit => Normalized(unit.Mana, unit.BaseStat(StatBonusBaseStat.Mana))),
        ("tuNormalized", static unit => Normalized(unit.TimeUnits, unit.BaseStat(StatBonusBaseStat.TimeUnits))),
        ("energyNormalized", static unit => Normalized(unit.Energy, unit.BaseStat(StatBonusBaseStat.Stamina))),
        ("moraleNormalized", static unit => Normalized(unit.Morale, 100)),
        ("stunNormalized", static unit => Normalized(unit.StunLevel, unit.Health)),

        ("energyRegen", static unit => unit.EnergyRegeneration),
    ];

    private const int Multiplier = 1000;

    /// <summary>A bonus with no terms: the external bonus unchanged (<c>return bonus;</c>).</summary>
    public static RuleStatBonus Empty { get; } = new(Array.AsReadOnly(Array.Empty<StatBonusTerm>()));

    /// <summary>RuleStatBonus::setFiring.</summary>
    public static RuleStatBonus Firing { get; } = Of(("firing", 1f));

    /// <summary>RuleStatBonus::setMelee.</summary>
    public static RuleStatBonus MeleeSkill { get; } = Of(("melee", 1f));

    /// <summary>RuleStatBonus::setThrowing.</summary>
    public static RuleStatBonus Throwing { get; } = Of(("throwing", 1f));

    /// <summary>RuleStatBonus::setCloseQuarters.</summary>
    public static RuleStatBonus CloseQuarters { get; } = Of(("melee", 0.5f), ("reactions", 0.5f));

    /// <summary>RuleStatBonus::setPsiAttack.</summary>
    public static RuleStatBonus PsiAttack { get; } = Of(("psi", 0.02f));

    /// <summary>RuleStatBonus::setPsiDefense.</summary>
    public static RuleStatBonus PsiDefense { get; } = Of(("psiStrength", 1f), ("psiSkill", 0.2f));

    /// <summary>RuleStatBonus::setFlatHundred.</summary>
    public static RuleStatBonus FlatHundred { get; } = Of(("flatHundred", 1f));

    /// <summary>RuleStatBonus::setStrength.</summary>
    public static RuleStatBonus Strength { get; } = Of(("strength", 1f));

    /// <summary>Whether the bonus is written as script code, which this port's native evaluation does not run (Phase 7 branch 8).</summary>
    public bool IsScript => Script is not null;

    /// <summary>
    /// <c>RuleStatBonus::getBonus</c> through the script the reference generates: the external bonus (the item's power for a damage bonus,
    /// else 0) scaled by 1000, each term added as its polynomial of the statistic (<c>pow1 * s + pow2 * s^2 + pow3 * s^3 + pow4 * s^4</c>, the
    /// coefficients scaled by 1,000,000 and truncated) over 1000 with the running sum truncated to an integer each time, then rounded to the
    /// nearest (halves away from zero) and divided by 1000. Without a unit (<c>BattleActionAttack</c> with no attacker) the terms add nothing.
    /// A bonus written as code throws: the callers refuse such battles beforehand.
    /// </summary>
    public int Evaluate(IStatBonusUnit? unit, int externalBonuses = 0)
    {
        if (Script is not null) throw new NotSupportedException("A stat bonus written as script code needs the tactical script bindings (Phase 7 branch 8).");
        if (Terms.Count == 0) return externalBonuses;
        unchecked
        {
            var result = externalBonuses * Multiplier;
            if (unit is not null)
            {
                foreach (var term in Terms)
                {
                    var stat = Find(term.Stat)(unit);
                    float bonus = 0;
                    // pow1 .. pow4 are the coefficients as the script passes them: (int)(c * statMultiper * 1000) in single precision.
                    bonus += Power(term, 3); bonus *= stat;
                    bonus += Power(term, 2); bonus *= stat;
                    bonus += Power(term, 1); bonus *= stat;
                    bonus += Power(term, 0); bonus *= stat;
                    result = (int)(result + bonus / Multiplier);
                }
            }
            result = result >= 0 ? result + 500 : result - 500;
            return result / Multiplier;
        }
    }

    internal static Func<IStatBonusUnit, float> Find(string name)
    {
        foreach (var (statName, value) in StatDataMap)
            if (string.Equals(statName, name, StringComparison.Ordinal)) return value;
        throw new InvalidDataException($"Unknown stat multiplier term '{name}'.");
    }

    internal static bool IsKnown(string name)
    {
        foreach (var (statName, _) in StatDataMap)
            if (string.Equals(statName, name, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int Power(StatBonusTerm term, int index) =>
        index < term.Coefficients.Count ? unchecked((int)(term.Coefficients[index] * 1000f * 1000f)) : 0;

    private static RuleStatBonus Of(params (string Stat, float Coefficient)[] terms) =>
        new(Array.AsReadOnly(terms.Select(static term => new StatBonusTerm(term.Stat, Array.AsReadOnly([term.Coefficient]))).ToArray()));

    private static Func<IStatBonusUnit, float> Single(StatBonusBaseStat stat, int divisor = 1)
    {
        var scale = 1.0f / divisor;
        return unit => unit.BaseStat(stat) * scale;
    }

    private static Func<IStatBonusUnit, float> Product(StatBonusBaseStat first, StatBonusBaseStat second, int divisor)
    {
        var scale = 1.0f / divisor;
        return unit => unit.BaseStat(first) * unit.BaseStat(second) * scale;
    }

    private static float Normalized(int value, int maximum) => maximum > 0 ? 1.0f * value / maximum : 0.0f;
}

/// <summary>
/// The load state of one <c>RuleStatBonus</c> while a rule's layers apply: the <c>set*</c> defaults replace the terms (and drop a script,
/// as the refresh rebuilds it), and the key of the rule, when present, loads a mapping or a script (<c>RuleStatBonus::load</c>).
/// </summary>
internal sealed class StatBonusBuilder(RuleStatBonus initial)
{
    private IReadOnlyList<StatBonusTerm> _terms = initial.Terms;
    private string? _script = initial.Script;
    private bool _modded = initial.Modded;

    /// <summary>RuleStatBonus::setValues: the terms are replaced and the script is generated from them again.</summary>
    public void Set(RuleStatBonus defaults)
    {
        _terms = defaults.Terms;
        _script = null;
    }

    /// <summary>RuleStatBonus::setModded.</summary>
    public void SetModded(bool modded) => _modded = modded;

    /// <summary>
    /// RuleStatBonus::load of the key: a mapping lists the terms (the first four coefficients of each, unknown names dropped, in the order of
    /// <c>statDataMap</c>), a scalar is script code. Anything else (a sequence) empties the terms. The key being present marks the bonus modded.
    /// </summary>
    public void Load(YamlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        switch (node)
        {
            case YamlMappingNode mapping:
                var terms = new List<StatBonusTerm>();
                foreach (var (name, _) in RuleStatBonus.StatDataMap)
                    foreach (var entry in mapping.Entries)
                    {
                        if (!string.Equals(entry.ScalarKey, name, StringComparison.Ordinal)) continue;
                        terms.Add(new StatBonusTerm(name, Coefficients(entry.Value)));
                        break;
                    }
                _terms = terms.AsReadOnly();
                _script = null;
                break;
            case YamlScalarNode or YamlNullNode:
                _script = YamlValueReader.ReadString(node);
                break;
            default:
                // RuleStatBonus::load clears the terms whenever the key is present; a sequence leaves none and the script is rebuilt from them.
                _terms = Array.AsReadOnly(Array.Empty<StatBonusTerm>());
                _script = null;
                break;
        }
        _modded = true;
    }

    public RuleStatBonus Freeze() => new(_terms, _script, _modded);

    private static ReadOnlyCollection<float> Coefficients(YamlNode node)
    {
        switch (node)
        {
            case YamlSequenceNode sequence:
                return Array.AsReadOnly(sequence.Items.Take(4).Select(YamlValueReader.ReadSingle).ToArray());
            case YamlMappingNode mapping:
                return Array.AsReadOnly(mapping.Entries.Take(4).Select(static entry => YamlValueReader.ReadSingle(entry.Value)).ToArray());
            default:
                return Array.AsReadOnly([YamlValueReader.ReadSingle(node)]);
        }
    }
}
