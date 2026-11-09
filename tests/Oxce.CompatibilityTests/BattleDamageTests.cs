using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S3: damage to units (<c>BattleUnit::damage</c>, <c>TileEngine::hitUnit</c>, the unit part of <c>TileEngine::hit</c> and
/// <c>awardExperience</c>) on the flat arena of the sight tests. The alien (100 health, facing west) is the victim and the soldier the attacker
/// with a loaded GUN. <see cref="Plain"/> is the base armor piercing type with its random stun and wounds turned off, so a hit's numbers follow its
/// damage; a <see cref="ScriptedDraws"/> source orders the draws the rules make (the percent rolls at the end of every damage: the zombie
/// chance, then the self-destruct chance).
/// </summary>
public sealed class BattleDamageTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";
    private const string ClipText = "- type: CLIP\n    battleType: 2\n    clipSize: 12\n    weight: 1";
    private const string GrenadeText = "- type: GRENADE\n    battleType: 4\n    weight: 1";

    private static readonly BattlePosition Front = new(-6, 0, 10);

    /// <summary>The base armor piercing type: health from the damage, a quarter to stun, a tenth to armor, no random stun or wounds.</summary>
    internal static RuntimeItemDamageRule Plain { get; } = RuntimeItemDamageRule.Predefined(1) with
    {
        RandomStun = false,
        RandomWound = false,
        IgnoreOverKill = true,
        Conversions = RuntimeItemDamageRule.Predefined(1).Conversions with { Wound = 0 },
    };

    private static RuntimeItemDamageRule With(Func<RuntimeItemDamageRule, RuntimeItemDamageRule> change) => change(Plain);

    private static RuntimeItemDamageRule WithConversions(Func<RuntimeDamageConversions, RuntimeDamageConversions> change) =>
        Plain with { Conversions = change(Plain.Conversions) };

    internal static Arena Duel(string gun = "", string clip = "", string grenade = "", string global = "", string alienExtra = "", string soldierExtra = "",
        string unitExtra = "", Func<string, string>? more = null)
    {
        var arena = Flat(alienExtra: alienExtra, soldierExtra: soldierExtra, unitExtra: unitExtra.Length == 0 ? null : unitExtra, edit: rules =>
        {
            if (gun.Length > 0) rules = Replaced(rules, GunText, GunText + "\n    " + gun.Replace("\n", "\n    ", StringComparison.Ordinal));
            if (clip.Length > 0) rules = Replaced(rules, ClipText, ClipText + "\n    " + clip.Replace("\n", "\n    ", StringComparison.Ordinal));
            if (grenade.Length > 0) rules = Replaced(rules, GrenadeText, GrenadeText + "\n    " + grenade.Replace("\n", "\n    ", StringComparison.Ordinal));
            rules = global.Length > 0 ? rules + "\n" + global + "\n" : rules;
            return more is null ? rules : more(rules);
        });
        arena.Alien.Stats = arena.Alien.Stats with { Health = 100, Bravery = 0 };
        arena.Alien.Health = 100;
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 100, Bravery = 40 };
        arena.Soldier.Health = 100;
        // The first turn's sight spotted both; start from nothing.
        foreach (var unit in arena.Battle.Units)
        {
            Array.Fill(unit.TurnsSinceSpotted, (byte)255);
            Array.Fill(unit.TurnsLeftSpottedForSnipers, (byte)0);
        }
        return arena;
    }

    internal static (BattleAttack Attack, BattleItem Gun, BattleItem Clip) Shot(Arena arena, BattleUnit? attacker = null, BattleActionType type = BattleActionType.AimedShot)
    {
        var owner = attacker ?? arena.Soldier;
        foreach (var item in arena.Battle.InventoryOf(owner).ToList()) arena.Battle.RemoveItem(item);
        var gun = GunFor(arena.Battle, owner);
        var clip = arena.Battle.Items.First(item => item.Id == gun.AmmoSlots[0]);
        return (new BattleAttack(type, owner, gun, clip), gun, clip);
    }

    internal static BattleDamageResult Hurt(Arena arena, int damage, RuntimeItemDamageRule type, BattlePosition? relative = null, BattleAttack? attack = null,
        BattleUnit? victim = null, int side = -1, int part = -1) =>
        arena.Battle.DamageUnit(arena.Rules, victim ?? arena.Alien, relative ?? Front, damage, type, attack ?? new BattleAttack(BattleActionType.None), side, part);

    // ---- the side and the body part ----

    [Fact]
    public void TheSideFollowsTheHitAgainstTheFacingAndTheBodyPartItsHeight()
    {
        // The alien faces west (direction 6): a hit from the west lands on its front, from the north on its right, from the east on its rear.
        (BattlePosition Relative, int Side, int Part)[] cases =
        [
            (new(-6, 0, 10), BattleUnit.SideFront, BattleUnit.BodyPartTorso),
            (new(0, -6, 10), BattleUnit.SideRight, BattleUnit.BodyPartRightArm),
            (new(0, 6, 10), BattleUnit.SideLeft, BattleUnit.BodyPartLeftArm),
            (new(6, 0, 10), BattleUnit.SideRear, BattleUnit.BodyPartTorso),
            (new(-6, 0, 22), BattleUnit.SideFront, BattleUnit.BodyPartHead),
            (new(0, -6, 2), BattleUnit.SideRight, BattleUnit.BodyPartRightLeg),
            (new(0, 6, 2), BattleUnit.SideLeft, BattleUnit.BodyPartLeftLeg),
            // Straight from below, or from the unit's own voxel at its feet: the side under, the torso.
            (new(0, 0, 0), BattleUnit.SideUnder, BattleUnit.BodyPartTorso),
            (new(0, 0, -5), BattleUnit.SideUnder, BattleUnit.BodyPartTorso),
        ];
        foreach (var (relative, side, part) in cases)
        {
            var arena = Duel();
            Hurt(arena, 10, Plain, relative);
            Assert.Equal((side, part), (arena.Alien.FatalShotSide, arena.Alien.FatalShotBodyPart));
        }
    }

    [Fact]
    public void ADiagonalHitRollsBetweenTheTwoSidesAndALowFrontHitRollsALeg()
    {
        // From the north-west (relative direction 15, one past the facing 6 + 8): the draw below 2 keeps the front, 2 turns to the right.
        var arena = Duel();
        arena.Battle.Random = new ScriptedDraws(0);
        Hurt(arena, 10, Plain, new(-6, -6, 10));
        Assert.Equal(BattleUnit.SideFront, arena.Alien.FatalShotSide);
        arena = Duel();
        var draws = new ScriptedDraws(2);
        arena.Battle.Random = draws;
        Hurt(arena, 10, Plain, new(-6, -6, 10));
        Assert.Equal((BattleUnit.SideRight, BattleUnit.BodyPartRightArm, (0, 2)), (arena.Alien.FatalShotSide, arena.Alien.FatalShotBodyPart, (draws.Calls[0].Minimum, draws.Calls[0].Maximum)));
        // The other three diagonals: south-west (13) turns between the front and the left... relative direction 13 less 6 is 7 (front or left); 11 less 6 is 5 (rear or left);
        // 9 less 6 is 3 (rear or right).
        foreach (var (relative, low, high) in new[]
        {
            (new BattlePosition(-6, 6, 10), BattleUnit.SideFront, BattleUnit.SideLeft),
            (new BattlePosition(6, 6, 10), BattleUnit.SideRear, BattleUnit.SideLeft),
            (new BattlePosition(6, -6, 10), BattleUnit.SideRear, BattleUnit.SideRight),
        })
        {
            foreach (var (draw, expected) in new[] { (0, low), (2, high) })
            {
                arena = Duel();
                arena.Battle.Random = new ScriptedDraws(draw);
                Hurt(arena, 10, Plain, relative);
                Assert.Equal(expected, arena.Alien.FatalShotSide);
            }
        }
        // A front or rear hit low on the body rolls a leg: right (4) or left (5).
        foreach (var leg in new[] { BattleUnit.BodyPartRightLeg, BattleUnit.BodyPartLeftLeg })
        {
            arena = Duel();
            arena.Battle.Random = new ScriptedDraws(leg);
            Hurt(arena, 10, Plain, new(-6, 0, 2));
            Assert.Equal(leg, arena.Alien.FatalShotBodyPart);
        }
    }

    [Fact]
    public void ADamageTypeThatIgnoresDirectionAlwaysHitsTheFrontTorsoAndTheEnvironmentOverridesBoth()
    {
        var arena = Duel();
        Hurt(arena, 10, With(static type => type with { IgnoreDirection = true }), new(0, -6, 2));
        Assert.Equal((BattleUnit.SideFront, BattleUnit.BodyPartTorso), (arena.Alien.FatalShotSide, arena.Alien.FatalShotBodyPart));
        Hurt(arena, 10, Plain, Front, side: BattleUnit.SideRear, part: BattleUnit.BodyPartLeftLeg);
        Assert.Equal((BattleUnit.SideRear, BattleUnit.BodyPartLeftLeg), (arena.Alien.FatalShotSide, arena.Alien.FatalShotBodyPart));
    }

    [Fact]
    public void ATallerUnitIsHitOnTheHeadHigherUpAndAKneelingOneLower()
    {
        var arena = Duel();
        // 22 standing, 14 kneeling: a hit at 14 is a torso standing and a head kneeling.
        Hurt(arena, 10, Plain, new(-6, 0, 14));
        Assert.Equal(BattleUnit.BodyPartTorso, arena.Alien.FatalShotBodyPart);
        arena.Alien.Kneeled = true;
        Hurt(arena, 10, Plain, new(-6, 0, 14));
        Assert.Equal(BattleUnit.BodyPartHead, arena.Alien.FatalShotBodyPart);
    }

    // ---- armor, resistance, conversions ----

    [Fact]
    public void ArmorOfTheSideHitReducesTheDamageAndIsWornDownByIt()
    {
        var arena = Duel();
        arena.Alien.CurrentArmor[BattleUnit.SideFront] = 20;
        arena.Alien.MaxArmor[BattleUnit.SideFront] = 20;
        var result = Hurt(arena, 50, Plain);
        // 50 - 20 = 30 to health, a tenth of it (3) to the armor; the other sides keep theirs.
        Assert.Equal((30, 70, 17), (result.Damage, arena.Alien.Health, arena.Alien.CurrentArmor[BattleUnit.SideFront]));
        Assert.Equal(0, arena.Alien.CurrentArmor[BattleUnit.SideRear]);
    }

    [Fact]
    public void ArmorIgnoreAndEffectivenessShapeWhatTheArmorTakesOffAndNeverMakeTheArmorNegative()
    {
        var arena = Duel();
        arena.Alien.CurrentArmor[BattleUnit.SideFront] = arena.Alien.MaxArmor[BattleUnit.SideFront] = 20;
        // Ignoring 5 leaves 15; half effectiveness takes off 7.5, truncated from the float result.
        Assert.Equal(35, Hurt(arena, 50, With(static type => type with { ArmorIgnore = 5 })).Damage);
        arena = Duel();
        arena.Alien.CurrentArmor[BattleUnit.SideFront] = arena.Alien.MaxArmor[BattleUnit.SideFront] = 20;
        Assert.Equal(40, Hurt(arena, 50, With(static type => type with { ArmorEffectiveness = 0.5f })).Damage);
        arena = Duel();
        arena.Alien.CurrentArmor[BattleUnit.SideFront] = arena.Alien.MaxArmor[BattleUnit.SideFront] = 20;
        // No effectiveness: the armor does not reduce (and ArmorIgnore, past the armor value, never goes below zero).
        Assert.Equal(50, Hurt(arena, 50, With(static type => type with { ArmorEffectiveness = 0 })).Damage);
        arena = Duel();
        arena.Alien.CurrentArmor[BattleUnit.SideFront] = arena.Alien.MaxArmor[BattleUnit.SideFront] = 20;
        Assert.Equal(50, Hurt(arena, 50, With(static type => type with { ArmorIgnore = 99 })).Damage);
        // Armor that cannot absorb the damage reaches zero and no lower.
        arena = Duel();
        arena.Alien.CurrentArmor[BattleUnit.SideFront] = arena.Alien.MaxArmor[BattleUnit.SideFront] = 2;
        Hurt(arena, 300, Plain);
        Assert.Equal(0, arena.Alien.CurrentArmor[BattleUnit.SideFront]);
    }

    [Fact]
    public void TheArmorsPreDamageIsTakenBeforeItReducesTheShotAndTheCapIsTheMaximum()
    {
        var arena = Duel();
        arena.Alien.CurrentArmor[BattleUnit.SideFront] = arena.Alien.MaxArmor[BattleUnit.SideFront] = 20;
        // ToArmorPre 1: 40 off the armor (to zero) from the shot's damage before the armor reduces it; the reduction uses the armor as it was.
        var result = Hurt(arena, 40, WithConversions(static conversions => conversions with { ArmorPre = 1, Armor = 0 }));
        Assert.Equal((20, 0), (result.Damage, arena.Alien.CurrentArmor[BattleUnit.SideFront]));
        // A negative armor conversion would mend the armor, but never past the maximum.
        arena = Duel();
        arena.Alien.CurrentArmor[BattleUnit.SideFront] = 5;
        arena.Alien.MaxArmor[BattleUnit.SideFront] = 8;
        Hurt(arena, 100, WithConversions(static conversions => conversions with { Armor = -1 }) with { ArmorEffectiveness = 0 });
        Assert.Equal(8, arena.Alien.CurrentArmor[BattleUnit.SideFront]);
    }

    [Fact]
    public void TheArmorsDamageModifierForTheTypeScalesTheDamageDownToAWholeNumber()
    {
        var arena = Duel(alienExtra: "    damageModifier: [1, 0.5]\n");
        // Base type 1 is the armor piercing one: 25 * 0.5 = 12.5 rounds down.
        Assert.Equal((12, 88), (Hurt(arena, 25, Plain).Damage, arena.Alien.Health));
        arena = Duel(alienExtra: "    damageModifier: [1, 0]\n");
        Assert.Equal((0, 100), (Hurt(arena, 25, Plain).Damage, arena.Alien.Health));
    }

    [Fact]
    public void EachConversionGoesToItsOwnStatisticAndRandomizedOnesDrawInTheReferencesOrder()
    {
        var arena = Duel();
        arena.Alien.TimeUnits = 50;
        arena.Alien.Energy = 40;
        arena.Alien.Mana = 30;
        arena.Alien.Stats = arena.Alien.Stats with { TimeUnits = 50, Stamina = 40, Mana = 30 };
        var type = WithConversions(static conversions => conversions with { Time = 0.5f, Energy = 0.25f, Mana = 1, Health = 0.5f, Stun = 1 });
        Hurt(arena, 20, type);
        Assert.Equal((90, 40, 35, 10, 20), (arena.Alien.Health, arena.Alien.TimeUnits, arena.Alien.Energy, arena.Alien.Mana, arena.Alien.StunLevel));
        // Random conversions each roll 0 to the damage; with the draws scripted: stun, morale, time, health, mana, energy, wound, armor (the reference's order).
        arena = Duel();
        var draws = new ScriptedDraws(7, 6, 5, 4, 3, 2, 1, 0);
        arena.Battle.Random = draws;
        Hurt(arena, 10, Plain with
        {
            RandomStun = true,
            RandomMorale = true,
            RandomTime = true,
            RandomHealth = true,
            RandomMana = true,
            RandomEnergy = true,
            RandomArmor = true,
        });
        Assert.Equal(new[] { (0, 10), (0, 10), (0, 10), (0, 10), (0, 10), (0, 10), (0, 10) },
            draws.Calls.Take(7).Select(static call => (call.Minimum, call.Maximum)).ToArray());
        Assert.Equal([7, 6, 5, 4, 3, 2, 1], draws.Calls.Take(7).Select(static call => call.Result).ToArray());
    }

    [Fact]
    public void ANegativeAmountOfDamageAfterTheArmorDoesNothingToTheUnit()
    {
        var arena = Duel();
        arena.Alien.CurrentArmor[BattleUnit.SideFront] = arena.Alien.MaxArmor[BattleUnit.SideFront] = 90;
        var result = Hurt(arena, 50, Plain);
        Assert.Equal((0, 100, 0, 100), (result.Damage, arena.Alien.Health, arena.Alien.StunLevel, arena.Alien.Morale));
        // The armor still takes the piercing type's armor share only through the final conversions, which a spent shot does not reach.
        Assert.Equal(90, arena.Alien.CurrentArmor[BattleUnit.SideFront]);
    }

    // ---- pain, overkill, wounds, morale ----

    [Fact]
    public void ArmorThatFeelsNoPainTakesNoStunUnlessTheTypeIgnoresIt()
    {
        var arena = Duel(alienExtra: "    painImmune: true\n");
        Hurt(arena, 40, Plain);
        Assert.Equal((60, 0), (arena.Alien.Health, arena.Alien.StunLevel));
        Hurt(arena, 40, With(static type => type with { IgnorePainImmunity = true }));
        Assert.Equal((20, 10), (arena.Alien.Health, arena.Alien.StunLevel));
        // Stun is capped at four times the maximum health.
        arena = Duel();
        arena.Alien.StunLevel = 395;
        Hurt(arena, 100, WithConversions(static conversions => conversions with { Stun = 1, Health = 0 }));
        Assert.Equal(400, arena.Alien.StunLevel);
    }

    [Fact]
    public void HealthGoesBelowZeroByTheArmorsOverkillUnlessTheTypeIgnoresIt()
    {
        var arena = Duel();
        var refusal = Hurt(arena, 1000, With(static type => type with { IgnoreOverKill = false })).Refusal;
        // Four times the maximum health below zero is the floor; the unit is past the threshold, which the casualty check (S4) resolves.
        Assert.Equal((-400, null), (arena.Alien.Health, refusal?.Code));
        Assert.Equal(350, BattleState.OverKillDamage(arena.Rules, arena.Alien));
        arena = Duel();
        Hurt(arena, 1000, Plain);
        Assert.Equal(0, arena.Alien.Health);
        // A health below the minimum (an earlier script) is not raised to it.
        arena = Duel();
        arena.Alien.Health = -500;
        Assert.Equal(0, Hurt(arena, 10, Plain).Damage);
        Assert.Equal(-500, arena.Alien.Health);
    }

    [Fact]
    public void WoundsFallOnTheBodyPartOfTheHitForAUnitThatBleedsAndTakeMoraleWithThem()
    {
        var wounding = WithConversions(static conversions => conversions with { Wound = 0.1f });
        // An alien does not bleed by default; the soldier does.
        var arena = Duel();
        Hurt(arena, 50, wounding);
        Assert.Equal(0, arena.Alien.FatalWoundCount);
        Hurt(arena, 50, wounding, victim: arena.Soldier);
        // Front, 10 voxels up: the torso. 5 wounds; morale -5, and -(110 - 40) * 10 / 100 = 7 per ten damage: 50 * 7 / 10 = 35, over the leader's modifier.
        Assert.Equal(5, arena.Soldier.FatalWounds[BattleUnit.BodyPartTorso]);
        Assert.Equal(100 - 5 - 35, arena.Soldier.Morale);
        // An armor that is not bleed immune wounds an alien too.
        arena = Duel(alienExtra: "    bleedImmune: false\n");
        Hurt(arena, 50, wounding, new(0, -6, 10));
        Assert.Equal(5, arena.Alien.FatalWounds[BattleUnit.BodyPartRightArm]);
        // The random wound types: vanilla wounds one to three with the chance of the potential out of ten, spread rolls up to the potential.
        arena = Duel(alienExtra: "    bleedImmune: false\n");
        var draws = new ScriptedDraws(3, 2);
        arena.Battle.Random = draws;
        Hurt(arena, 50, wounding with { RandomWound = true });
        Assert.Equal(2, arena.Alien.FatalWoundCount);
        Assert.Equal(new[] { (0, 10), (1, 3) }, draws.Calls.Take(2).Select(static call => (call.Minimum, call.Maximum)).ToArray());
        arena = Duel(alienExtra: "    bleedImmune: false\n");
        arena.Battle.Random = new ScriptedDraws(4);
        Hurt(arena, 50, wounding with { RandomWound = true, RandomWoundType = 1 });
        Assert.Equal(4, arena.Alien.FatalWoundCount);
    }

    [Fact]
    public void MoraleLossFromHealthDamageFollowsBraveryTheLeadersRankAndTheTypeAndFearImmunity()
    {
        // Alien: bravery 0 so 10 -> 11; 20 damage -> 20 * 11 / 10 = 22 at modifier 100.
        var arena = Duel();
        Hurt(arena, 20, Plain);
        Assert.Equal(78, arena.Alien.Morale);
        // The soldier (bravery 40): 7 per ten; with a commander (rank 5) alive the player's modifier is 150: 100 * (30 * 7 / 10) / 150 = 14.
        arena = Duel();
        arena.Soldier.Rank = 5;
        Hurt(arena, 30, Plain, victim: arena.Soldier);
        Assert.Equal(100 - 100 * (30 * 7 / 10) / 150, arena.Soldier.Morale);
        Assert.Equal(150, arena.Battle.PlayerMoraleModifier());
        // The leader's ladder: sergeant 110, captain 115, colonel 125, a lieutenant or rookie 100 (a unit that is out does not lead).
        foreach (var (rank, modifier) in new[] { (0, 100), (1, 100), (2, 110), (3, 115), (4, 125), (5, 150) })
        {
            arena.Soldier.Rank = rank;
            Assert.Equal(modifier, arena.Battle.PlayerMoraleModifier());
        }
        arena.Soldier.Status = BattleUnitStatus.Unconscious;
        Assert.Equal(100, arena.Battle.PlayerMoraleModifier());
        // The type may leave the morale alone, and a fear immune armor has none to lose.
        arena = Duel();
        Hurt(arena, 20, With(static type => type with { IgnoreNormalMoraleLose = true }));
        Assert.Equal(100, arena.Alien.Morale);
        arena = Duel(alienExtra: "    fearImmune: true\n");
        Hurt(arena, 20, Plain);
        Assert.Equal(100, arena.Alien.Morale);
        // Morale from the type's own conversion goes down by bravery too.
        arena = Duel();
        Hurt(arena, 20, WithConversions(static conversions => conversions with { Morale = 1 }) with { IgnoreNormalMoraleLose = true });
        Assert.Equal(100 - 110 * 20 / 100, arena.Alien.Morale);
    }

    // ---- fire ----

    [Fact]
    public void ADamageAtTheFireThresholdSetsTheUnitAlightForTheRolledTurnsIfItIsNotAlreadyBurningLonger()
    {
        var burning = With(static type => type with { FireThreshold = 10 });
        // The burn time is rolled up to five times the armor's modifier for the type.
        var arena = Duel();
        var draws = new ScriptedDraws(4);
        arena.Battle.Random = draws;
        Hurt(arena, 20, burning);
        Assert.Equal((4, (0, 5)), (arena.Alien.Fire, (draws.Calls[0].Minimum, draws.Calls[0].Maximum)));
        // A longer fire keeps burning.
        arena = Duel();
        arena.Alien.Fire = 6;
        arena.Battle.Random = new ScriptedDraws(4);
        Hurt(arena, 20, burning);
        Assert.Equal(6, arena.Alien.Fire);
        // Below the threshold nothing catches (and nothing is rolled).
        arena = Duel();
        draws = new ScriptedDraws(4);
        arena.Battle.Random = draws;
        Hurt(arena, 9, burning);
        Assert.Equal((0, 2), (arena.Alien.Fire, draws.Calls.Count));
        // An armor that resists the type completely never burns.
        arena = Duel(alienExtra: "    damageModifier: [1, 0]\n");
        arena.Battle.Random = new ScriptedDraws(4);
        Hurt(arena, 20, With(static type => type with { FireThreshold = 0 }));
        Assert.Equal(0, arena.Alien.Fire);
        // A modifier of two lets the burn time reach ten.
        arena = Duel(alienExtra: "    damageModifier: [1, 2]\n");
        draws = new ScriptedDraws(9);
        arena.Battle.Random = draws;
        Hurt(arena, 20, burning);
        Assert.Equal((9, 10), (arena.Alien.Fire, draws.Calls[0].Maximum));
    }

    [Fact]
    public void AFireExtinguisherInTheWeaponOrTheAmmunitionPutsTheUnitOut()
    {
        var burning = With(static type => type with { FireThreshold = 10 });
        foreach (var (gun, clip) in new[] { ("isFireExtinguisher: true", ""), ("", "isFireExtinguisher: true") })
        {
            var arena = Duel(gun: gun, clip: clip);
            var (attack, _, _) = Shot(arena);
            arena.Alien.Fire = 3;
            arena.Battle.Random = new ScriptedDraws(4);
            Hurt(arena, 20, burning, attack: attack);
            Assert.Equal(0, arena.Alien.Fire);
        }
        // Without one it keeps burning.
        var plain = Duel();
        var (shot, _, _) = Shot(plain);
        plain.Alien.Fire = 3;
        plain.Battle.Random = new ScriptedDraws(4);
        Hurt(plain, 20, burning, attack: shot);
        Assert.Equal(4, plain.Alien.Fire);
    }

    [Fact]
    public void AUnitThatBurnsFloorsCannotCatchFire()
    {
        var arena = Duel(alienExtra: "    specab: 2\n");
        arena.Battle.Random = new ScriptedDraws(4);
        Hurt(arena, 20, With(static type => type with { FireThreshold = 10 }));
        Assert.Equal(0, arena.Alien.Fire);
    }

    // ---- infection ----

    [Fact]
    public void AmmunitionThatInfectsSetsTheVictimToRespawnWithTheItsChance()
    {
        var arena = Duel(clip: "zombieUnit: ALIEN\nzombieUnitChance: 50", global: "");
        var (attack, _, _) = Shot(arena, arena.Alien);
        // The zombie roll is the first percent after the damage: below 50 converts, 50 or more does not.
        arena.Battle.Random = new ScriptedDraws(60);
        var result = Hurt(arena, 10, Plain, attack: attack, victim: arena.Soldier);
        Assert.False(arena.Soldier.Respawn);
        Assert.Null(result.Refusal);
        arena.Battle.Random = new ScriptedDraws(10);
        result = Hurt(arena, 10, Plain, attack: attack, victim: arena.Soldier);
        Assert.Equal((true, "ALIEN", (int)BattleFaction.Hostile, null),
            (arena.Soldier.Respawn, arena.Soldier.SpawnUnit, arena.Soldier.SpawnUnitFaction, result.Refusal?.Code));
        // A victim that already has a unit to spawn is not given another.
        var again = Duel(clip: "zombieUnit: ALIEN\nzombieUnitChance: 100");
        var (second, _, _) = Shot(again, again.Alien);
        again.Soldier.SpawnUnit = "VEHICLE";
        Hurt(again, 10, Plain, attack: second, victim: again.Soldier);
        Assert.Equal("VEHICLE", again.Soldier.SpawnUnit);
    }

    [Fact]
    public void TheInfectionIsSkippedForAZombieImmuneArmorAHostileVictimOfAHostileAttackerAndAUnitBiggerThanTheVictim()
    {
        // Zombie immune.
        var arena = Duel(clip: "zombieUnit: ALIEN\nzombieUnitChance: 100", soldierExtra: "    zombiImmune: true\n");
        var (attack, _, _) = Shot(arena, arena.Alien);
        Hurt(arena, 10, Plain, attack: attack, victim: arena.Soldier);
        Assert.False(arena.Soldier.Respawn);
        // An alien (hostile by birth) hit by a hostile attacker, or by no attacker.
        arena = Duel(clip: "zombieUnit: ALIEN\nzombieUnitChance: 100");
        (attack, _, _) = Shot(arena, arena.Alien);
        Hurt(arena, 10, Plain, attack: attack, victim: arena.Alien);
        Assert.False(arena.Alien.Respawn);
        // The same alien hit by an X-COM soldier converts.
        (attack, _, _) = Shot(arena, arena.Soldier);
        Hurt(arena, 10, Plain, attack: attack, victim: arena.Alien);
        Assert.True(arena.Alien.Respawn);
        // The unit it would become is bigger than the victim (VEHICLE wears a 2x2 armor).
        arena = Duel(clip: "zombieUnit: VEHICLE\nzombieUnitChance: 100");
        (attack, _, _) = Shot(arena, arena.Alien);
        Hurt(arena, 10, Plain, attack: attack, victim: arena.Soldier);
        Assert.Equal((false, ""), (arena.Soldier.Respawn, arena.Soldier.SpawnUnit));
    }

    [Fact]
    public void TheWeaponsActionConfigOverridesTheChanceAndTheFactionDefaultsToTheAttackers()
    {
        var arena = Duel(gun: "confAimed: {ammoZombieUnitChanceOverride: 0}", clip: "zombieUnit: ALIEN\nzombieUnitChance: 100\nzombieUnitFaction: -1");
        var (attack, _, _) = Shot(arena, arena.Alien);
        arena.Battle.Random = new ScriptedDraws(0);
        Hurt(arena, 10, Plain, attack: attack, victim: arena.Soldier);
        Assert.False(arena.Soldier.Respawn);
        // The auto shot has no override: it keeps the ammunition's chance, and the spawned unit sides with the attacker.
        arena.Battle.Random = new ScriptedDraws(0);
        Hurt(arena, 10, Plain, attack: attack with { Type = BattleActionType.AutoShot }, victim: arena.Soldier);
        Assert.Equal((true, (int)BattleFaction.Hostile), (arena.Soldier.Respawn, arena.Soldier.SpawnUnitFaction));
        // The unit by the victim's armor or type beats the item's own.
        var byType = Duel(clip: "zombieUnit: ALIEN\nzombieUnitChance: 100\nzombieUnitByType: {RECRUIT: CIVILIAN}\nzombieUnitByArmorFemale: {SUIT: VEHICLE_B}");
        var (typed, _, _) = Shot(byType, byType.Alien);
        byType.Soldier.Gender = 0;
        Hurt(byType, 10, Plain, attack: typed, victim: byType.Soldier);
        // The soldier is male by default: the armor-by-female map is not used, the type map is.
        Assert.Equal("CIVILIAN", byType.Soldier.SpawnUnit);
        var female = Duel(clip: "zombieUnit: ALIEN\nzombieUnitChance: 100\nzombieUnitByType: {RECRUIT: CIVILIAN}\nzombieUnitByArmorFemale: {SUIT: VEHICLE_B}");
        var (femaleShot, _, _) = Shot(female, female.Alien);
        female.Soldier.Gender = 1;
        Hurt(female, 10, Plain, attack: femaleShot, victim: female.Soldier);
        Assert.Equal("VEHICLE_B", female.Soldier.SpawnUnit);
    }

    // ---- the attacker's spotting and the AI ----

    [Fact]
    public void ADirectShotLetsTheVictimSideSpotTheAttackerAndTheVictimsAiRemembersIt()
    {
        var arena = Duel(unitExtra: "    spotter: 3\n");
        var (attack, _, _) = Shot(arena);
        Hurt(arena, 10, Plain, attack: attack);
        // The soldier was spotted by the hostile faction just now, and the alien's AI was hit by it (once).
        Assert.Equal((byte)0, arena.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile]);
        Assert.Equal([arena.Soldier.Id], arena.Alien.Ai!.WasHitBy);
        Hurt(arena, 10, Plain, attack: attack);
        Assert.Equal([arena.Soldier.Id], arena.Alien.Ai!.WasHitBy);
        // The other factions' entries are as they were; with the extended option off a sniper's memory is not set.
        Assert.Equal((byte)255, arena.Soldier.TurnsSinceSpotted[(int)BattleFaction.Neutral]);
        Assert.Equal((byte)0, arena.Soldier.TurnsLeftSpottedForSnipers[(int)BattleFaction.Hostile]);
    }

    [Fact]
    public void OnlyAShotOfAnotherSideThatIsDirectOrOnTheVictimSpotsTheAttacker()
    {
        // A throw, or a hit on a unit of the attacker's own side, spots nothing.
        var arena = Duel();
        var (attack, _, _) = Shot(arena, type: BattleActionType.Throw);
        Hurt(arena, 10, Plain, attack: attack);
        Assert.Equal((byte)255, arena.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile]);
        Assert.Empty(arena.Alien.Ai!.WasHitBy);
        var (aimed, _, _) = Shot(arena);
        arena.Alien.Faction = BattleFaction.Player;
        Hurt(arena, 10, Plain, attack: aimed);
        Assert.Equal((byte)255, arena.Soldier.TurnsSinceSpotted[(int)BattleFaction.Player]);
        arena.Alien.Faction = BattleFaction.Hostile;
        // An exploding shell away from the target does not spot, but one landing on it does (the relative position is zero).
        var shell = Duel(clip: "blastRadius: 3");
        var (shellShot, _, _) = Shot(shell);
        Hurt(shell, 10, Plain, attack: shellShot);
        Assert.Equal((byte)255, shell.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile]);
        Hurt(shell, 10, Plain, new BattlePosition(0, 0, 0), shellShot);
        Assert.Equal((byte)0, shell.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile]);
    }

    [Fact]
    public void SniperSpottingOnAHitFollowsTheExtendedOptionAndWhetherTheVictimIsLeftStanding()
    {
        // 0: never; 1: only when the victim is not past the out threshold; 2: always. The turns are the victim's spotter duration.
        foreach (var (option, damage, expected) in new[] { (0, 10, 0), (1, 10, 3), (1, 500, 0), (2, 500, 3), (2, 10, 3) })
        {
            var arena = Duel(unitExtra: "    spotter: 3\n", global: "constants:\n  extendedSpotOnHitForSniping: " + option);
            var (attack, _, _) = Shot(arena);
            arena.Soldier.TurnsLeftSpottedForSnipers[(int)BattleFaction.Hostile] = 1;
            Hurt(arena, damage, Plain, attack: attack);
            Assert.True(arena.Soldier.TurnsLeftSpottedForSnipers[(int)BattleFaction.Hostile] == Math.Max(expected, 1), $"option {option}, damage {damage}");
        }
    }

    // ---- self-destruct and casualties ----

    [Fact]
    public void AUnitThatExplodesWhenItDiesPushesItsExplosionOnceAndNotWhenTheTypeIgnoresIt()
    {
        var arena = Duel(alienExtra: "    selfDestructItem: GRENADE\n", unitExtra: "    specab: 1\n");
        arena.Battle.Random = new ScriptedDraws(99, 0);
        // Not lethal: nothing.
        Assert.Null(Hurt(arena, 10, Plain).Refusal);
        Assert.False(arena.Alien.AlreadyExploded);
        // Lethal: the explosion is pushed on the list of states (its damage is dealt at once; the rest waits for the list to run).
        Assert.Null(Hurt(arena, 500, Plain).Refusal);
        Assert.True(arena.Battle.HasPendingStates);
        // (ExplosionBState::init clears the flag again once the explosion starts; with a list that is not empty it waits for its turn, with the flag set.)
        Assert.False(arena.Alien.AlreadyExploded);
        arena.Battle.RunStates();
        Assert.False(arena.Battle.HasPendingStates);
        // A type that ignores self-destruct, or a unit that exploded already, goes on to the casualty check.
        var ignoring = Duel(alienExtra: "    selfDestructItem: GRENADE\n", unitExtra: "    specab: 1\n");
        Assert.Null(Hurt(ignoring, 500, With(static type => type with { IgnoreSelfDestruct = true })).Refusal);
        var done = Duel(alienExtra: "    selfDestructItem: GRENADE\n", unitExtra: "    specab: 1\n");
        done.Alien.AlreadyExploded = true;
        Assert.Null(Hurt(done, 500, Plain).Refusal);
        // A unit with the ability but no such special weapon does not explode either.
        var bare = Duel(unitExtra: "    specab: 1\n");
        Assert.Null(Hurt(bare, 500, Plain).Refusal);
    }

    [Fact]
    public void AHitThatTakesAUnitOutByHealthOrStunLeavesItToTheCasualtyCheck()
    {
        var arena = Duel();
        Assert.Null(Hurt(arena, 10, Plain).Refusal);
        // Stun reaching the health.
        var stun = WithConversions(static conversions => conversions with { Stun = 1, Health = 0 });
        Assert.Null(Hurt(arena, 200, stun).Refusal);
        Assert.Equal(90, arena.Alien.Health);
        Assert.True(BattleState.IsOutThresholdExceeded(arena.Alien));
        // A hit on a unit with no health left changes nothing but the hit flag.
        arena = Duel();
        arena.Alien.Health = 0;
        arena.Alien.HitState = false;
        var result = Hurt(arena, 10, Plain);
        Assert.Equal((0, null, true, 0), (result.Damage, result.Refusal, arena.Alien.HitState, arena.Alien.Health));
    }

    // ---- hitUnit, murderer ----

    [Fact]
    public void AnUnconsciousBodyThatKeepsItsHealthIsNotACasualtyButOneThatLosesAllIs()
    {
        var arena = Duel();
        arena.Alien.Status = BattleUnitStatus.Unconscious;
        arena.Alien.StunLevel = 100;
        Assert.Null(Hurt(arena, 10, Plain).Refusal);
        Assert.Equal(90, arena.Alien.Health);
        Assert.Empty(arena.Battle.CheckForCasualties(arena.Rules, null, default));
        Hurt(arena, 500, Plain);
        Assert.Equal(BattleUnitStatus.Dead, Assert.Single(arena.Battle.CheckForCasualties(arena.Rules, null, default)).Status);
    }

    [Fact]
    public void AnInfectionFromNoAttackerSpawnsHostileUnitsAndAMissingUnitConvertsNothing()
    {
        var arena = Duel(clip: "zombieUnit: ALIEN\nzombieUnitChance: 100\nzombieUnitFaction: -1");
        var (attack, _, clip) = Shot(arena, arena.Alien);
        // No attacker: the damage item alone is known, and the faction falls to hostile (an X-COM victim, so the hostile rule does not stop it).
        Hurt(arena, 10, Plain, attack: new BattleAttack(BattleActionType.None, null, null, clip), victim: arena.Soldier);
        Assert.Equal((true, (int)BattleFaction.Hostile), (arena.Soldier.Respawn, arena.Soldier.SpawnUnitFaction));
        // An attacker of the player's side (hitting a hostile victim) gives the spawned unit the attacker's faction.
        var other = Duel(clip: "zombieUnit: ALIEN\nzombieUnitChance: 100\nzombieUnitFaction: -1");
        var (soldierShot, _, _) = Shot(other);
        Hurt(other, 10, Plain, attack: soldierShot);
        Assert.Equal((true, (int)BattleFaction.Player), (other.Alien.Respawn, other.Alien.SpawnUnitFaction));
        // A unit rule that does not exist.
        var missing = Duel(clip: "zombieUnit: NOPE\nzombieUnitChance: 100");
        var (missingShot, _, _) = Shot(missing, missing.Alien);
        Hurt(missing, 10, Plain, attack: missingShot, victim: missing.Soldier);
        Assert.Equal((false, ""), (missing.Soldier.Respawn, missing.Soldier.SpawnUnit));
    }

    [Fact]
    public void AGlowingBodyTakesTheHitThroughTheEffectPath()
    {
        // A flare (battle type 10 with no fuse) lying as a body glows: the hit counts as an effect and the lighting runs without a terrain change.
        var arena = Duel(more: static rules => Replaced(rules, GrenadeText, "- type: GRENADE\n    battleType: 10\n    weight: 1"));
        var (attack, _, _) = Shot(arena);
        arena.Put(12, 12, 0, BattleTilePart.Floor, BurnFloor);
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "GRENADE") { CorpseUnitId = arena.Alien.Id }, new BattlePosition(12, 12, 0));
        // The effect itself (gravity, light and sight refresh) has no observable result on this map; the path must run and spare the floor.
        Assert.Null(arena.Battle.Hit(arena.Rules, attack, Voxel(12, 12, 0), 30, Flat100, false));
        Assert.Equal(70, arena.Alien.Health);
    }

    [Fact]
    public void HitUnitReportsWhetherAUnitWasHitAndRemembersTheLastAttackerWithItsWeapon()
    {
        var arena = Duel();
        var (attack, _, _) = Shot(arena);
        Assert.False(arena.Battle.HitUnit(arena.Rules, attack, null, Front, 10, Plain, true).Hit);
        arena.Alien.Health = 0;
        Assert.False(arena.Battle.HitUnit(arena.Rules, attack, arena.Alien, Front, 10, Plain, true).Hit);
        Assert.Equal(0, arena.Alien.MurdererId);
        arena.Alien.Health = 100;
        Assert.True(arena.Battle.HitUnit(arena.Rules, attack, arena.Alien, Front, 10, Plain, true).Hit);
        Assert.Equal((arena.Soldier.Id, "GUN", "CLIP"), (arena.Alien.MurdererId, arena.Alien.MurdererWeapon, arena.Alien.MurdererWeaponAmmo));
        // A hit with no weapon names the unknown one.
        arena.Battle.HitUnit(arena.Rules, new BattleAttack(BattleActionType.None, arena.Soldier), arena.Alien, Front, 10, Plain, false);
        Assert.Equal(("STR_WEAPON_UNKNOWN", "STR_WEAPON_UNKNOWN"), (arena.Alien.MurdererWeapon, arena.Alien.MurdererWeaponAmmo));
        // A hit with no attacker (the environment) leaves the murderer.
        arena.Battle.HitUnit(arena.Rules, new BattleAttack(BattleActionType.None), arena.Alien, Front, 10, Plain, false);
        Assert.Equal(arena.Soldier.Id, arena.Alien.MurdererId);
    }

    [Fact]
    public void AHarmlessHitDoesNotStealTheCreditFromAVictimThatIsAlreadyDying()
    {
        var arena = Duel();
        var (attack, _, _) = Shot(arena);
        arena.Alien.MurdererId = 77;
        arena.Alien.MurdererWeapon = "OLD";
        arena.Alien.Fire = 2;
        // No health lost (a smoke grenade): a burning victim keeps its murderer; wounds also keep it.
        arena.Battle.HitUnit(arena.Rules, attack, arena.Alien, Front, 0, Plain, true);
        Assert.Equal((77, "OLD"), (arena.Alien.MurdererId, arena.Alien.MurdererWeapon));
        arena.Alien.Fire = 0;
        arena.Alien.FatalWounds[BattleUnit.BodyPartTorso] = 1;
        arena.Battle.HitUnit(arena.Rules, attack, arena.Alien, Front, 0, Plain, true);
        Assert.Equal(77, arena.Alien.MurdererId);
        // A hit that does hurt takes the credit, and so does a harmless one on a healthy victim.
        arena.Battle.HitUnit(arena.Rules, attack, arena.Alien, Front, 5, Plain, true);
        Assert.Equal(arena.Soldier.Id, arena.Alien.MurdererId);
        arena.Alien.MurdererId = 77;
        arena.Alien.FatalWounds[BattleUnit.BodyPartTorso] = 0;
        arena.Battle.HitUnit(arena.Rules, attack, arena.Alien, Front, 0, Plain, true);
        Assert.Equal(arena.Soldier.Id, arena.Alien.MurdererId);
    }

    [Fact]
    public void EveryHitMarksTheUnitHitSinceItWasLastReset()
    {
        var arena = Duel();
        Assert.False(arena.Alien.HitState);
        Hurt(arena, 1, Plain);
        Assert.True(arena.Alien.HitState);
    }

    // ---- experience ----

    private static (Arena Arena, BattleAttack Attack) Trained(string gun = "", string clip = "", string global = "", string grenade = "", Func<string, string>? more = null)
    {
        var arena = Duel(gun: gun, clip: clip, global: global, grenade: grenade, more: more);
        var (attack, _, _) = Shot(arena);
        arena.Battle.Random = new ScriptedDraws(99, 99, 99, 99, 99);
        return (arena, attack);
    }

    private static string Exp(BattleUnit unit) =>
        $"b{unit.Experience.Bravery} r{unit.Experience.Reactions} f{unit.Experience.Firing} t{unit.Experience.Throwing} s{unit.Experience.PsiSkill} p{unit.Experience.PsiStrength} m{unit.Experience.Melee} a{unit.Experience.Mana}";

    private static void Strike(Arena arena, BattleAttack attack, bool range = true, BattleUnit? target = null) =>
        arena.Battle.HitUnit(arena.Rules, attack, target ?? arena.Alien, Front, 10, Plain, range);

    [Fact]
    public void AnXcomShooterEarnsFiringExperienceForAHitOnAnAlienAndAGunButtMeleeExperience()
    {
        var (arena, attack) = Trained();
        Strike(arena, attack);
        Assert.Equal("b0 r0 f1 t0 s0 p0 m0 a0", Exp(arena.Soldier));
        Strike(arena, attack, range: false);
        Assert.Equal("b0 r0 f1 t0 s0 p0 m1 a0", Exp(arena.Soldier));
        // An alien attacker earns nothing (only X-COM trains).
        var (alienArena, _) = Trained();
        var (alienShot, _, _) = Shot(alienArena, alienArena.Alien);
        Strike(alienArena, alienShot, target: alienArena.Soldier);
        Assert.Equal("b0 r0 f0 t0 s0 p0 m0 a0", Exp(alienArena.Alien));
    }

    [Fact]
    public void FriendsAndNeutralsAreNotTrainedOnAndMindControlOnlyCountsInTheVanillaSystem()
    {
        var (arena, attack) = Trained();
        arena.Alien.OriginalFaction = BattleFaction.Neutral;
        Strike(arena, attack);
        Assert.Equal("b0 r0 f0 t0 s0 p0 m0 a0", Exp(arena.Soldier));
        // A mind controlled alien (hostile by birth, now ours) counts for the vanilla rules...
        arena.Alien.OriginalFaction = BattleFaction.Hostile;
        arena.Alien.Faction = BattleFaction.Player;
        Strike(arena, attack);
        Assert.Equal("b0 r0 f1 t0 s0 p0 m0 a0", Exp(arena.Soldier));
        // ...and not for the extended ones.
        var (extended, extendedAttack) = Trained(global: "constants:\n  extendedExperienceAwardSystem: true");
        extended.Alien.Faction = BattleFaction.Player;
        Strike(extended, extendedAttack);
        Assert.Equal("b0 r0 f0 t0 s0 p0 m0 a0", Exp(extended.Soldier));
    }

    [Fact]
    public void GrenadesMeleeWeaponsAndMedikitsTrainByTheirKindAndTheExtendedSystemSplitsFirearmsByRange()
    {
        // A grenade: firing in vanilla, throwing in the extended system. A bare hit with a grenade as weapon.
        var (arena, _) = Trained();
        var grenade = new BattleItem(arena.Battle.TakeItemId(), "GRENADE");
        arena.Battle.AddItem(grenade);
        Strike(arena, new BattleAttack(BattleActionType.Throw, arena.Soldier, grenade, grenade));
        Assert.Equal("b0 r0 f1 t0 s0 p0 m0 a0", Exp(arena.Soldier));
        var (extended, _) = Trained(global: "constants:\n  extendedExperienceAwardSystem: true");
        var thrown = new BattleItem(extended.Battle.TakeItemId(), "GRENADE");
        extended.Battle.AddItem(thrown);
        Strike(extended, new BattleAttack(BattleActionType.Throw, extended.Soldier, thrown, thrown));
        Assert.Equal("b0 r0 f0 t1 s0 p0 m0 a0", Exp(extended.Soldier));
        // The extended firearm: an arcing shot throws, a long range fires, a short one throws, a range of one is melee, no range trains nothing.
        foreach (var (gun, expected) in new[]
        {
            ("arcingShot: true", "b0 r0 f0 t1 s0 p0 m0 a0"),
            ("maxRange: 20", "b0 r0 f1 t0 s0 p0 m0 a0"),
            ("maxRange: 5", "b0 r0 f0 t1 s0 p0 m0 a0"),
            ("maxRange: 1", "b0 r0 f0 t0 s0 p0 m1 a0"),
            ("maxRange: 0", "b0 r0 f0 t0 s0 p0 m0 a0"),
        })
        {
            var (shooter, shot) = Trained(gun: gun,
                global: "constants:\n  extendedExperienceAwardSystem: true");
            Strike(shooter, shot);
            Assert.True(expected == Exp(shooter.Soldier), $"{gun}: {Exp(shooter.Soldier)}");
        }
        // A medikit trains nothing and ignores the target's side.
        var (medic, _) = Trained(more: static rules => Replaced(rules, GrenadeText, "- type: GRENADE\n    battleType: 6\n    weight: 1"));
        var kit = new BattleItem(medic.Battle.TakeItemId(), "GRENADE");
        medic.Battle.AddItem(kit);
        Assert.True(medic.Rules.Items.TryGet("GRENADE", out var kitHandle));
        Assert.Equal(6, medic.Rules.Items[kitHandle].Value.BattleType);
        Strike(medic, new BattleAttack(BattleActionType.Use, medic.Soldier, kit, kit));
        Assert.Equal("b0 r0 f0 t0 s0 p0 m0 a0", Exp(medic.Soldier));
    }

    [Fact]
    public void ARangedMeleeItemTrainsMeleeAndTheWeaponsOwnModeOverridesTheKind()
    {
        var (arena, attack) = Trained(gun: "experienceTrainingMode: 12");
        Strike(arena, attack);
        Assert.Equal("b0 r1 f0 t0 s0 p0 m0 a0", Exp(arena.Soldier));
        // Firing and throwing together; reactions and melee together; bravery twice.
        foreach (var (mode, expected) in new[]
        {
            (10, "b0 r0 f1 t1 s0 p0 m0 a0"),
            (13, "b0 r1 f0 t0 s0 p0 m1 a0"),
            (14, "b0 r1 f1 t0 s0 p0 m0 a0"),
            (15, "b0 r1 f0 t1 s0 p0 m0 a0"),
            (19, "b1 r0 f0 t0 s0 p0 m0 a0"),
            (20, "b2 r0 f0 t0 s0 p0 m0 a0"),
            (21, "b1 r1 f0 t0 s0 p0 m0 a0"),
            (1, "b0 r0 f0 t0 s0 p0 m1 a0"),
            (4, "b0 r0 f1 t0 s0 p0 m0 a0"),
            (7, "b0 r0 f0 t1 s0 p0 m0 a0"),
            (32, "b0 r0 f0 t0 s0 p0 m0 a0"),
        })
        {
            var (trained, shot) = Trained(gun: "experienceTrainingMode: " + mode);
            Strike(trained, shot);
            Assert.True(expected == Exp(trained.Soldier), $"mode {mode}: {Exp(trained.Soldier)}");
        }
    }

    [Fact]
    public void FractionalModesAndTheEitherOrModesRollTheirPercentAfterTheDamage()
    {
        // The damage's own percent rolls (zombie, self-destruct) come first; the experience's follow. 50%: a draw below 50 trains, else not.
        foreach (var (mode, draws, expected) in new[]
        {
            (2, new[] { 99, 99, 10 }, "b0 r0 f0 t0 s0 p0 m1 a0"),
            (2, new[] { 99, 99, 60 }, "b0 r0 f0 t0 s0 p0 m0 a0"),
            (3, new[] { 99, 99, 10 }, "b0 r0 f0 t0 s0 p0 m1 a0"),
            (3, new[] { 99, 99, 40 }, "b0 r0 f0 t0 s0 p0 m0 a0"),
            (5, new[] { 99, 99, 49 }, "b0 r0 f1 t0 s0 p0 m0 a0"),
            // Firing or throwing: the first draw below 50 picks firing, else throwing; then the whole (100%) one runs and the 0% remainder rolls.
            (11, new[] { 99, 99, 10, 99 }, "b0 r0 f1 t0 s0 p0 m0 a0"),
            (11, new[] { 99, 99, 90, 99 }, "b0 r0 f0 t1 s0 p0 m0 a0"),
            (16, new[] { 99, 99, 10, 99 }, "b0 r1 f0 t0 s0 p0 m0 a0"),
            (17, new[] { 99, 99, 90, 99 }, "b0 r0 f1 t0 s0 p0 m0 a0"),
            (18, new[] { 99, 99, 90, 99 }, "b0 r0 f0 t1 s0 p0 m0 a0"),
            (22, new[] { 99, 99, 90, 99 }, "b0 r1 f0 t0 s0 p0 m0 a0"),
            (23, new[] { 99, 99, 10, 99 }, "b2 r0 f0 t0 s0 p0 m0 a0"),
        })
        {
            var (arena, attack) = Trained(gun: "experienceTrainingMode: " + mode);
            arena.Battle.Random = new ScriptedDraws(draws);
            Strike(arena, attack);
            Assert.True(expected == Exp(arena.Soldier), $"mode {mode}, draws {string.Join(',', draws)}: {Exp(arena.Soldier)}");
        }
    }

    [Fact]
    public void PsiModesNeedASoldierWithPsiSkillAndTheManaExperienceOfTheWeaponIsAddedFirst()
    {
        foreach (var (mode, expected) in new[]
        {
            (24, "b0 r0 f0 t0 s0 p1 m0 a0"), (25, "b0 r0 f0 t0 s0 p2 m0 a0"), (26, "b0 r0 f0 t0 s1 p0 m0 a0"), (27, "b0 r0 f0 t0 s2 p0 m0 a0"),
            (28, "b0 r0 f0 t0 s1 p1 m0 a0"), (29, "b0 r0 f0 t0 s2 p2 m0 a0"), (30, "b0 r0 f0 t0 s0 p1 m0 a0"), (31, "b0 r0 f0 t0 s0 p2 m0 a0"),
        })
        {
            var (without, shot) = Trained(gun: "experienceTrainingMode: " + mode);
            without.Soldier.SoldierPsiSkill = 0;
            Strike(without, shot);
            Assert.Equal("b0 r0 f0 t0 s0 p0 m0 a0", Exp(without.Soldier));
            var (trained, trainedShot) = Trained(gun: "experienceTrainingMode: " + mode);
            trained.Soldier.SoldierPsiSkill = 40;
            trained.Battle.Random = new ScriptedDraws(99, 99, 10, 99);
            Strike(trained, trainedShot);
            Assert.True(expected == Exp(trained.Soldier), $"mode {mode}: {Exp(trained.Soldier)}");
        }
        // Mana experience of 150: one whole, and one more with a draw below 50.
        var (mana, manaShot) = Trained(gun: "manaExperience: 150");
        mana.Battle.Random = new ScriptedDraws(99, 99, 10, 99, 99);
        Strike(mana, manaShot);
        Assert.Equal(2, mana.Soldier.Experience.Mana);
        Assert.Equal(1, mana.Soldier.Experience.Firing);
    }

    // ---- TileEngine::hit ----

    private static BattlePosition Voxel(int x, int y, int z) => new(x * 16 + 8, y * 16 + 8, z);

    private static RuntimeItemDamageRule Flat100 { get; } = Plain with { RandomType = RuntimeItemDamageRule.RandomFlat };

    [Fact]
    public void AHitInAUnitsVoxelDamagesItAtWhereTheVoxelLiesAgainstItsBottomCenter()
    {
        var arena = Duel();
        var (attack, _, _) = Shot(arena);
        // The alien is at (15, 10) facing west; the voxel is 6 west of its centre and 6 up: the front, the torso.
        Assert.Null(arena.Battle.Hit(arena.Rules, attack, new BattlePosition(15 * 16 + 6, 10 * 16 + 8, 6), 30, Flat100, true));
        Assert.Equal((70, BattleUnit.SideFront, BattleUnit.BodyPartTorso), (arena.Alien.Health, arena.Alien.FatalShotSide, arena.Alien.FatalShotBodyPart));
        Assert.Equal(arena.Soldier.Id, arena.Alien.MurdererId);
        // From the north, near the top: the right side, the arm (the head is above the unit's 22 voxels).
        arena.Battle.Hit(arena.Rules, attack, new BattlePosition(15 * 16 + 8, 10 * 16 + 6, 21), 30, Flat100, true);
        Assert.Equal((BattleUnit.SideRight, BattleUnit.BodyPartRightArm), (arena.Alien.FatalShotSide, arena.Alien.FatalShotBodyPart));
        // The attacker itself in the voxel is not hit.
        var own = arena.Soldier.Health;
        arena.Battle.Hit(arena.Rules, attack, new BattlePosition(10 * 16 + 8, 10 * 16 + 8, 6), 30, Flat100, true);
        Assert.Equal(own, arena.Soldier.Health);
    }

    [Fact]
    public void AUnitStandingBelowTheVoxelsTileIsHitTwentyFourVoxelsLower()
    {
        // The soldier is 30 tall, so the tile above its own has voxels of it; that tile has no floor, so the unit below is the one it holds.
        var arena = Flat(soldierHeight: 30);
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 100 };
        arena.Soldier.Health = 100;
        var alienAttack = new BattleAttack(BattleActionType.None, arena.Alien);
        arena.Battle.Random = new ScriptedDraws(4);
        // 2 west of its centre and 27 up (3 into the upper tile): relative z 27 - 24 = 3, low on the body; from the west it is its rear (it faces east).
        arena.Battle.Hit(arena.Rules, alienAttack, new BattlePosition(10 * 16 + 6, 10 * 16 + 8, 27), 30, Flat100, true);
        Assert.Equal((70, BattleUnit.SideRear, BattleUnit.BodyPartRightLeg), (arena.Soldier.Health, arena.Soldier.FatalShotSide, arena.Soldier.FatalShotBodyPart));
    }

    [Fact]
    public void ABigUnitIsMeasuredFromTheCenterOfItsFourTiles()
    {
        var arena = Flat(bigAlien: true);
        var big = arena.Alien;
        big.Stats = big.Stats with { Health = 100 };
        big.Health = 100;
        // Its origin tile is at (15, 10): the center is 16 voxels into it. The voxel is 5 west and 5 north of that, in the pillar of the first tile: a diagonal
        // hit (measured from a tile's own center it would come from the south-east), which the draw below 2 puts on the front.
        arena.Battle.Random = new ScriptedDraws(0);
        arena.Battle.Hit(arena.Rules, new BattleAttack(BattleActionType.None, arena.Soldier), new BattlePosition(15 * 16 + 11, 10 * 16 + 11, 10), 30, Flat100, true);
        Assert.Equal((70, BattleUnit.SideFront), (big.Health, big.FatalShotSide));
    }

    [Fact]
    public void ABodyLyingOnAFloorTakesTheHitInsteadOfTheTerrainWhileItIsAlive()
    {
        var arena = Duel();
        var (attack, _, _) = Shot(arena);
        arena.Put(12, 12, 0, BattleTilePart.Floor, BurnFloor);
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = arena.Alien.Id }, new BattlePosition(12, 12, 0));
        var tile = arena.Tile(12, 12);
        Assert.Null(arena.Battle.Hit(arena.Rules, attack, Voxel(12, 12, 0), 30, Flat100, false));
        // The body was hit from straight below (relative 0): the under side; the floor neither burns nor breaks.
        Assert.Equal((70, BattleUnit.SideUnder), (arena.Alien.Health, arena.Alien.FatalShotSide));
        Assert.Null(arena.Battle.Hit(arena.Rules, attack, Voxel(12, 12, 0), 30, BattleDamageTypes.Fire, false));
        Assert.Equal(0, arena.Map.Fire(tile));
        Assert.NotNull(arena.Map.MapData(tile, BattleTilePart.Floor));
        // A dead body is not hit: the terrain is.
        arena.Alien.Health = 0;
        Assert.Null(arena.Battle.Hit(arena.Rules, attack, Voxel(12, 12, 0), 30, BattleDamageTypes.Fire, false));
        Assert.True(arena.Map.Fire(tile) > 0);
    }

    [Fact]
    public void AHitThatTakesTheUnitOutLeavesItToTheCasualtyCheck()
    {
        var arena = Duel();
        var (attack, _, _) = Shot(arena);
        var voxel = new BattlePosition(15 * 16 + 6, 10 * 16 + 8, 6);
        Assert.Null(arena.Battle.Hit(arena.Rules, attack, voxel, 500, Flat100, true));
        Assert.Equal((0, BattleUnitStatus.Standing), (arena.Alien.Health, arena.Alien.Status));
    }

    [Fact]
    public void AFlameOnAUnitBurnsItAndItsLightIsUpdated()
    {
        var arena = Duel();
        var (attack, _, _) = Shot(arena);
        var voxel = new BattlePosition(15 * 16 + 6, 10 * 16 + 8, 6);
        var revision = arena.Battle.Revision;
        // DT_IN: a fire threshold of zero; whatever the burn roll, a unit that is not resistant catches fire for 0 to 5 turns, and the effect refreshes the light.
        arena.Battle.Random = new ScriptedDraws(0, 4);
        Assert.Null(arena.Battle.Hit(arena.Rules, attack, voxel, 30, BattleDamageTypes.Fire, false));
        Assert.True(arena.Alien.Health < 100);
        Assert.True(arena.Battle.Revision >= revision);
    }

    [Fact]
    public void AFloorBurnedBeneathAUnitHurtsItNow()
    {
        // Branch 4's refused case: the burn-floor hit reaching a unit in the voxel.
        var arena = Duel();
        arena.Battle.SetTile(arena.Alien, new BattlePosition(14, 12, 0));
        arena.Put(14, 12, 0, BattleTilePart.Floor, Floor);
        var voxel = new BattlePosition(14 * 16 + 8, 12 * 16 + 8, 6);
        Assert.Null(arena.Battle.HitTerrain(arena.Rules, arena.Soldier, voxel, 30, BattleDamageTypes.Fire));
        Assert.True(arena.Alien.Health < 100);
        Assert.Equal(arena.Soldier.Id, arena.Alien.MurdererId);
        // The soldier who burns is an X-COM attacker with no weapon: no experience, and the murderer's weapon is unknown.
        Assert.Equal("b0 r0 f0 t0 s0 p0 m0 a0", Exp(arena.Soldier));
        Assert.Equal("STR_WEAPON_UNKNOWN", arena.Alien.MurdererWeapon);
    }
}
