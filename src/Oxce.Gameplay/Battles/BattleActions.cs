using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>BattleActionType, with the reference's values (the saved <c>tuReserved</c> is one).</summary>
internal enum BattleActionType
{
    None = 0,
    Turn = 1,
    Walk = 2,
    Kneel = 3,
    Prime = 4,
    Unprime = 5,
    Throw = 6,
    AutoShot = 7,
    SnapShot = 8,
    AimedShot = 9,
    Hit = 10,
    Use = 11,
    Launch = 12,
    MindControl = 13,
    Panic = 14,
    Rethink = 15,
    CloseQuarters = 16,
    TriggerTimedGrenade = 17,
    TriggerProximityGrenade = 18,
    SelfDestruct = 19,
    Wait = 20,
}

/// <summary>
/// The user options the battle rules read, at their defaults (Phase 7 plan, "User options are at their defaults"); they
/// become settings with Phase 8's options screen.
/// </summary>
internal static class BattleOptions
{
    /// <summary><c>Options::forcedAbsoluteCosts</c>: converts every percentage action cost to its flat value (off).</summary>
    public static bool ForcedAbsoluteCosts => false;

    /// <summary>The options pathfinding reads, at their defaults.</summary>
    public static BattlePathingOptions Pathing { get; } = new();
}

/// <summary>
/// The user options pathfinding reads (<c>Options::strictBlockedChecking</c>, <c>sneakyAI</c>, <c>brutalAI</c>, <c>brutalCivilians</c>,
/// <c>autoCombat</c>, <c>autoCombatControlPerUnit</c>, <c>aiPerformanceOptimization</c>), at their defaults. A pathfinder takes them as a
/// value, so a test chooses another set without a process-wide switch.
/// </summary>
internal sealed record BattlePathingOptions
{
    public bool StrictBlockedChecking { get; init; }
    public bool SneakyAi { get; init; }
    public bool BrutalAi { get; init; } = true;
    public bool BrutalCivilians { get; init; }
    public bool AutoCombat { get; init; }
    public bool AutoCombatControlPerUnit { get; init; } = true;
    public bool AiPerformanceOptimization { get; init; }
}

/// <summary>Why <see cref="BattleActionCost.Shortfall"/> refused: BattleActionCost::haveTU's messages.</summary>
internal enum BattleActionShortfall
{
    None,

    /// <summary>No time units to pay: "no action, no message".</summary>
    NoAction,
    TimeUnits,
    Energy,
    Morale,
    Health,
    Mana,
    Stun,
}

/// <summary>
/// BattleActionCost: the cost of an action by a unit with a weapon (<c>updateTU</c>), and whether the unit can pay it
/// (<c>haveTU</c>). The skill form (<c>skillRules</c>) belongs to the skill actions of Phase 7 branch 5.
/// </summary>
internal sealed class BattleActionCost(BattleUnit? actor = null)
{
    public BattleActionType Type { get; set; }
    public BattleUnit? Actor { get; set; } = actor;
    public BattleItem? Weapon { get; set; }

    /// <summary>RuleItemUseCost: the resources the action costs.</summary>
    public int Time { get; set; }
    public int Energy { get; set; }
    public int Morale { get; set; }
    public int Health { get; set; }
    public int Stun { get; set; }
    public int Mana { get; set; }

    /// <summary>BattleActionCost(type, unit, item): the cost with <see cref="UpdateTimeUnits"/> applied.</summary>
    public static BattleActionCost For(RuntimeRuleCatalog rules, BattleActionType type, BattleUnit actor, BattleItem? weapon)
    {
        var cost = new BattleActionCost(actor) { Type = type, Weapon = weapon };
        cost.UpdateTimeUnits(rules);
        return cost;
    }

    /// <summary>BattleActionCost::updateTU: the actor's cost of the action with the weapon, zero without either.</summary>
    public void UpdateTimeUnits(RuntimeRuleCatalog rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (Actor is not null && Weapon is not null)
        {
            var cost = Actor.ActionCost(rules.Battle.Equipment.Items[Weapon.ItemType].ActionCosts, Type);
            (Time, Energy, Morale, Health, Stun, Mana) = (cost.Time, cost.Energy, cost.Morale, cost.Health, cost.Stun, cost.Mana);
        }
        else ClearTimeUnits();
    }

    /// <summary>BattleActionCost::clearTU.</summary>
    public void ClearTimeUnits() => (Time, Energy, Morale, Health, Stun, Mana) = (0, 0, 0, 0, 0, 0);

    /// <summary>BattleActionCost::haveTU: <see cref="BattleActionShortfall.None"/> when the actor can pay every resource, else the first it lacks.</summary>
    public BattleActionShortfall Shortfall()
    {
        var actor = Actor ?? throw new InvalidOperationException("The action has no actor.");
        if (Time <= 0) return BattleActionShortfall.NoAction;
        if (actor.TimeUnits < Time) return BattleActionShortfall.TimeUnits;
        if (actor.Energy < Energy) return BattleActionShortfall.Energy;
        if (actor.Morale < Morale) return BattleActionShortfall.Morale;
        if (actor.Health <= Health) return BattleActionShortfall.Health;
        if (actor.Mana < Mana) return BattleActionShortfall.Mana;
        if (actor.Health - actor.StunLevel <= Stun + Health) return BattleActionShortfall.Stun;
        return BattleActionShortfall.None;
    }

    /// <summary>BattleActionCost::haveTU.</summary>
    public bool HaveTimeUnits() => Shortfall() == BattleActionShortfall.None;
}

internal sealed partial class BattleUnit
{
    /// <summary>
    /// BattleUnit::getActionTUs(type, item rule): the item's cost of the action (<c>getCostAimed</c> and the others, resolved in
    /// <see cref="RuntimeItemActionCosts"/>), its percentages applied to this unit's statistics. An action the item has no cost
    /// for (turn, walk, kneel...) is free.
    /// </summary>
    public RuntimeUseCost ActionCost(RuntimeItemActionCosts costs, BattleActionType type)
    {
        ArgumentNullException.ThrowIfNull(costs);
        var key = type switch
        {
            BattleActionType.Prime => RuntimeItemActionCosts.Prime,
            BattleActionType.Unprime => RuntimeItemActionCosts.Unprime,
            BattleActionType.Throw => RuntimeItemActionCosts.Throw,
            BattleActionType.AutoShot => RuntimeItemActionCosts.Auto,
            BattleActionType.SnapShot => RuntimeItemActionCosts.Snap,
            BattleActionType.Hit => RuntimeItemActionCosts.Melee,
            BattleActionType.Launch or BattleActionType.AimedShot => RuntimeItemActionCosts.Aimed,
            BattleActionType.Use => RuntimeItemActionCosts.Use,
            BattleActionType.MindControl => RuntimeItemActionCosts.MindControl,
            BattleActionType.Panic => RuntimeItemActionCosts.Panic,
            _ => null,
        };
        // Any other action pays nothing, with the use flags as the default.
        var (cost, flat) = key is null ? (RuntimeUseCost.Zero, costs.Of(RuntimeItemActionCosts.Use).Flat) : costs.Of(key);
        return ApplyPercentages(cost, flat);
    }

    /// <summary>BattleUnit::applyPercentages: a percentage cost becomes a share of the unit's statistic (at least 1), unless it is flat.</summary>
    private RuntimeUseCost ApplyPercentages(RuntimeUseCost cost, RuntimeUseFlat flat)
    {
        if (BattleOptions.ForcedAbsoluteCosts) return cost;
        static int Share(int stat, int percent) => Math.Max(1, (int)MathF.Floor(stat * percent / 100.0f));
        return new RuntimeUseCost(
            !flat.Time && cost.Time != 0 ? Share(Stats.TimeUnits, cost.Time) : cost.Time,
            !flat.Energy && cost.Energy != 0 ? Share(Stats.Stamina, cost.Energy) : cost.Energy,
            !flat.Morale && cost.Morale != 0 ? Share(110 - Stats.Bravery, cost.Morale) : cost.Morale,
            !flat.Health && cost.Health != 0 ? Share(Stats.Health, cost.Health) : cost.Health,
            !flat.Stun && cost.Stun != 0 ? Share(Stats.Health, cost.Stun) : cost.Stun,
            !flat.Mana && cost.Mana != 0 ? Share(Stats.Mana, cost.Mana) : cost.Mana);
    }

    /// <summary>BattleUnit::spendCost: the resources, without checking.</summary>
    public void SpendCost(BattleActionCost cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        TimeUnits -= cost.Time;
        Energy -= cost.Energy;
        Morale -= cost.Morale;
        Health -= cost.Health;
        StunLevel += cost.Stun;
        Mana -= cost.Mana;
    }
}
