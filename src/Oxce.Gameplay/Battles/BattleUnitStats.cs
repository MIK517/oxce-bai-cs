namespace Oxce.Gameplay.Battles;

/// <summary>
/// UnitStats: twelve 16-bit statistics in the reference's field order. Sums wrap like the reference's Sint16 fields.
/// </summary>
internal readonly record struct BattleUnitStats(
    short TimeUnits, short Stamina, short Health, short Bravery, short Reactions, short Firing,
    short Throwing, short Strength, short PsiStrength, short PsiSkill, short Melee, short Mana)
{
    /// <summary>The rule keys, in UnitStats::fieldLoop order.</summary>
    internal static readonly string[] Keys =
        ["tu", "stamina", "health", "bravery", "reactions", "firing", "throwing", "strength", "psiStrength", "psiSkill", "melee", "mana"];

    public static BattleUnitStats From(IReadOnlyDictionary<string, short> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        short Get(string key) => values.GetValueOrDefault(key);
        return new(Get("tu"), Get("stamina"), Get("health"), Get("bravery"), Get("reactions"), Get("firing"),
            Get("throwing"), Get("strength"), Get("psiStrength"), Get("psiSkill"), Get("melee"), Get("mana"));
    }

    public static BattleUnitStats operator +(BattleUnitStats left, BattleUnitStats right) => unchecked(new(
        (short)(left.TimeUnits + right.TimeUnits), (short)(left.Stamina + right.Stamina), (short)(left.Health + right.Health),
        (short)(left.Bravery + right.Bravery), (short)(left.Reactions + right.Reactions), (short)(left.Firing + right.Firing),
        (short)(left.Throwing + right.Throwing), (short)(left.Strength + right.Strength), (short)(left.PsiStrength + right.PsiStrength),
        (short)(left.PsiSkill + right.PsiSkill), (short)(left.Melee + right.Melee), (short)(left.Mana + right.Mana)));

    public static BattleUnitStats Add(BattleUnitStats left, BattleUnitStats right) => left + right;

    /// <summary>UnitStats::obeyFixedMinimum: health at least 1, every other statistic at least 0.</summary>
    public BattleUnitStats ObeyFixedMinimum() => new(
        Math.Max(TimeUnits, (short)0), Math.Max(Stamina, (short)0), Math.Max(Health, (short)1), Math.Max(Bravery, (short)0),
        Math.Max(Reactions, (short)0), Math.Max(Firing, (short)0), Math.Max(Throwing, (short)0), Math.Max(Strength, (short)0),
        Math.Max(PsiStrength, (short)0), Math.Max(PsiSkill, (short)0), Math.Max(Melee, (short)0), Math.Max(Mana, (short)0));
}
