using Oxce.Core.Random;

namespace Oxce.Gameplay.Campaigns.World.Interception;

/// <summary>A base defense facility as <c>BaseDefenseState</c> fires it (Base::setupDefenses order).</summary>
internal sealed class BaseDefenseWeapon
{
    public required int HitRatio { get; init; }
    public required int DefenseValue { get; init; }
    public required int AmmoNeeded { get; init; }
    /// <summary>RuleBaseFacility::getAmmoMax above 0: the facility carries its own ammunition.</summary>
    public required bool HasOwnAmmo { get; init; }
    /// <summary>The ammunition item drawn from base stores when the facility has no own ammunition.</summary>
    public string? AmmoItem { get; init; }
    public required int ShieldDamageModifier { get; init; }
    /// <summary>The ammunition item's damage type for the unified damage formula, or null for 50-150%.</summary>
    public IDogfightDamageType? UnifiedDamage { get; init; }
    public int Ammo { get; set; }
    /// <summary>The facility's index in the base, to write its ammunition back.</summary>
    public int FacilityIndex { get; init; }
}

/// <summary>The UFO attacking a base, with the Ufo fields the defense rounds read and change.</summary>
internal sealed class BaseDefenseUfo
{
    public required int DamageMaximum { get; init; }
    public required int ShieldCapacity { get; init; }
    public required int AvoidBonus2 { get; init; }
    public required int Armor { get; init; }
    public required int ShieldBleedThrough { get; init; }
    /// <summary>Ufo::isCrashed exception: kamikaze (hunt behaviour 1) or unmanned UFOs never crash.</summary>
    public required bool NeverCrashes { get; init; }
    public int Damage { get; set; }
    public int Shield { get; set; }
    public DogfightUfoStatus Status { get; set; } = DogfightUfoStatus.Flying;

    public bool IsDestroyed => Damage >= DamageMaximum;
    public bool IsCrashed => IsDestroyed || !NeverCrashes && Damage > DamageMaximum / 2;
}

/// <summary>
/// Base::setupDefenses and Base::getGravShields: the built facilities with a defense value, in facility
/// order, with their own ammunition or the stores item they draw on, and the built grav shields.
/// </summary>
internal static class BaseDefenseSetup
{
    public static (IReadOnlyList<BaseDefenseWeapon> Defenses, int GravShields) For(CampaignState.BaseState owner,
        Oxce.Mods.Rulesets.Runtime.RuntimeRuleCatalog rules)
    {
        var defenses = new List<BaseDefenseWeapon>();
        var gravShields = 0;
        for (var index = 0; index < owner.Facilities.Count; index++)
        {
            var facility = owner.Facilities[index];
            if (facility.BuildTime != 0) continue;
            var rule = rules.Facilities[facility.Rule].Value;
            if (rule.GravShield) gravShields++;
            if (rule.DefenseValue == 0) continue;
            var ammoItem = rule.AmmoItem is { } handle ? rules.Items.GetExternalId(handle) : null;
            IDogfightDamageType? unified = null;
            if (rule.UnifiedDamageFormula && rule.AmmoItem is { } item)
            {
                var damage = rules.Items[item].Value.Damage;
                unified = new DogfightItemDamage(damage.RandomType, damage.ResistType, rules.Campaign.Interception);
            }
            defenses.Add(new BaseDefenseWeapon
            {
                HitRatio = rule.HitRatio,
                DefenseValue = rule.DefenseValue,
                AmmoNeeded = rule.AmmoNeeded,
                HasOwnAmmo = rule.AmmoMaximum > 0,
                AmmoItem = ammoItem,
                ShieldDamageModifier = rule.ShieldDamageModifier,
                UnifiedDamage = unified,
                Ammo = facility.Ammo,
                FacilityIndex = index,
            });
        }
        return (defenses, gravShields);
    }
}

/// <summary>
/// The firing rounds of <c>BaseDefenseState::nextStep</c> resolved headlessly: each defense in order
/// fires once per pass when it has ammunition (own, or from base stores), rolls its hit ratio less the
/// UFO's <c>avoidBonus2</c>, applies <c>applyDamage</c> on a hit and spends ammunition on any shot;
/// each grav shield adds one more pass; a destroyed UFO ends the rounds. Presentation steps (texts,
/// sounds, explosions) draw no randomness. Pinned by the <c>strategic-base-defense</c> oracle.
/// </summary>
internal static class BaseDefense
{
    /// <param name="storage">Base stores by item id, for facilities without own ammunition.</param>
    /// <param name="waveInterrupt">The UFO wave's interrupt percentage (Ufo::setDamage), read on a crash.</param>
    /// <param name="interrupt">AlienMission::setInterrupted(true).</param>
    public static (int Passes, int Attacks) Fire(IReadOnlyList<BaseDefenseWeapon> defenses, int gravShields, BaseDefenseUfo ufo,
        Dictionary<string, int> storage, IRandomSource random, Func<int> waveInterrupt, Action interrupt)
    {
        int attacks = 0, passes = 0;
        while (true)
        {
            if (attacks == defenses.Count && passes == gravShields) return (passes, attacks);
            if (attacks == defenses.Count)
            {
                // STR_GRAV_SHIELD_REPELS_UFO: the defenses fire again.
                passes++;
                attacks = 0;
                continue;
            }
            var defense = defenses[attacks];
            var noAmmo = defense.HasOwnAmmo ? defense.Ammo < defense.AmmoNeeded
                : defense.AmmoItem is { } item && storage.GetValueOrDefault(item) < defense.AmmoNeeded;
            if (!noAmmo)
            {
                if (RandomChance.Percent(random, defense.HitRatio - ufo.AvoidBonus2)) ApplyDamage(defense, ufo, random, waveInterrupt, interrupt);
                if (defense.AmmoNeeded > 0)
                {
                    if (defense.HasOwnAmmo) defense.Ammo -= defense.AmmoNeeded;
                    else if (defense.AmmoItem is { } ammo)
                    {
                        // ItemContainer::removeItem never goes below zero.
                        var left = storage.GetValueOrDefault(ammo) - defense.AmmoNeeded;
                        if (left > 0) storage[ammo] = left;
                        else storage.Remove(ammo);
                    }
                }
            }
            attacks++;
            if (ufo.Status == DogfightUfoStatus.Destroyed) return (passes, attacks);
        }
    }

    // BaseDefenseState::applyDamage.
    private static void ApplyDamage(BaseDefenseWeapon defense, BaseDefenseUfo ufo, IRandomSource random, Func<int> waveInterrupt,
        Action interrupt)
    {
        var power = defense.DefenseValue;
        if (defense.UnifiedDamage is { } unified)
        {
            var damage = unified.RandomDamage(power, random);
            if (ufo.Shield != 0)
            {
                var shieldDamage = damage * defense.ShieldDamageModifier / 100;
                damage = defense.ShieldDamageModifier == 0 ? 0
                    : Math.Max(0, shieldDamage - ufo.Shield) * ufo.ShieldBleedThrough / defense.ShieldDamageModifier;
                SetShield(ufo, ufo.Shield - shieldDamage);
            }
            damage = Math.Max(0, damage - ufo.Armor);
            SetDamage(ufo, ufo.Damage + damage, random, waveInterrupt, interrupt);
            return;
        }
        // The vanilla formula, 50-150%; no bleed-through scaling, for backwards compatibility.
        var dmg = power / 2 + random.NextInclusive(0, power);
        if (ufo.Shield > 0)
        {
            var shieldDamage = dmg * defense.ShieldDamageModifier / 100;
            dmg = defense.ShieldDamageModifier == 0 ? 0 : Math.Max(0, shieldDamage - ufo.Shield) * 100 / defense.ShieldDamageModifier;
            SetShield(ufo, ufo.Shield - shieldDamage);
        }
        SetDamage(ufo, ufo.Damage + dmg, random, waveInterrupt, interrupt);
    }

    private static void SetShield(BaseDefenseUfo ufo, int shield) => ufo.Shield = Math.Max(0, Math.Min(ufo.ShieldCapacity, shield));

    // Ufo::setDamage: crash or destruction status, then the wave's interrupt roll.
    private static void SetDamage(BaseDefenseUfo ufo, int damage, IRandomSource random, Func<int> waveInterrupt, Action interrupt)
    {
        ufo.Damage = Math.Max(0, damage);
        if (ufo.IsDestroyed) ufo.Status = DogfightUfoStatus.Destroyed;
        else if (ufo.IsCrashed) ufo.Status = DogfightUfoStatus.Crashed;
        if (ufo.Status is not (DogfightUfoStatus.Crashed or DogfightUfoStatus.Destroyed)) return;
        var chance = waveInterrupt();
        if (chance > 0 && RandomChance.Percent(random, chance)) interrupt();
    }
}
