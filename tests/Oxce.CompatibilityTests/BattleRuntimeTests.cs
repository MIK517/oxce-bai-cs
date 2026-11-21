using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleDamageTests;
using static Oxce.CompatibilityTests.BattleSightTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S5b: the battle runtime (the state, the rules, the stream, the link, the hooks and the event sink set once, the battle's own
/// command registry and its fault path), the events S3 to S5's outcomes raise through the sink, and each tactical script hook reached with its default.
/// </summary>
public sealed class BattleRuntimeTests
{
    private sealed record Boom : IBattleCommand;

    private sealed record Stray : IBattleCommand;

    private sealed record Detonate : IBattleCommand;

    private sealed class Link(int difficulty) : IBattleCampaignLink
    {
        public List<int> Killed { get; } = [];

        public int MissionCount => 4;

        public int Difficulty => difficulty;

        public BattleSoldierDescription? DescribeSoldier(int soldierId) => new("Name", "STR_ROOKIE");

        public void KillSoldier(int soldierId, BattleUnitKillSnapshot cause) => Killed.Add(soldierId);
    }

    private static BattleRuntime RuntimeOf(Arena arena, IBattleCampaignLink? link = null, IBattleHooks? hooks = null, IRandomSource? random = null) =>
        new(arena.Battle, arena.Rules, random ?? new SplitMix64RandomSource(7), link, hooks);

    private static BattleUnit Victim(Arena arena, int health)
    {
        var unit = new BattleUnit(970, BattleFaction.Hostile, BattleController.Ai)
        {
            Stats = arena.Alien.Stats,
            ArmorId = arena.Alien.ArmorId,
            ArmorSize = 1,
            UnitType = arena.Alien.UnitType,
            Health = health,
            MovementType = 0,
        };
        arena.Battle.AddUnit(unit);
        arena.Battle.SetTile(unit, new BattlePosition(21, 20, 0));
        unit.SetDirection(6);
        unit.Morale = 100;
        return unit;
    }

    private static BattlePosition Center(int x, int y) => new(x * 16 + 8, y * 16 + 8, 12);

    /// <summary>A grenade of power 80 and a flat blast, lying where the explosion goes off, and the attack that throws it.</summary>
    private static BattleAttack Throw(Arena arena)
    {
        var grenade = arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "GRENADE"), new BattlePosition(20, 20, 0));
        return new BattleAttack(BattleActionType.Throw, arena.Soldier, grenade, grenade);
    }

    private static Arena Armed() => Duel(grenade: "damageType: 3\ndamageAlter: {RandomType: 3}\npower: 80");

    [Fact]
    public void TheRuntimeSetsTheStreamTheLinkAndTheHooksOnceAndItsEventsAreTheStates()
    {
        var arena = Armed();
        var random = new SplitMix64RandomSource(9);
        var link = new Link(3);
        var runtime = RuntimeOf(arena, link, random: random);
        Assert.Same(random, arena.Battle.Random);
        Assert.Same(link, arena.Battle.CampaignLink);
        Assert.Same(DefaultBattleHooks.Instance, arena.Battle.Hooks);
        Assert.Same(arena.Battle.Events, runtime.Events);
        Assert.Equal(arena.Battle.Revision, runtime.Revision);
        Assert.Null(runtime.Fault);
    }

    [Fact]
    public void TheRuntimeRefusesABattleThatHoldsAnotherCatalogThanTheCampaigns()
    {
        var arena = Armed();
        var other = Armed();
        Assert.NotSame(arena.Rules, other.Rules);
        Assert.Throws<ArgumentException>(() => RuntimeOf(arena with { Rules = other.Rules }));
        Assert.Same(arena.Rules, RuntimeOf(arena).State.Rules);
    }

    [Fact]
    public void ABattleTheCampaignStartsAndConfirmsDrawsFromTheCampaignsStreamWithItsLinkWithoutAnyAssignmentPerCommand()
    {
        var (campaign, _) = BattleStartTests.Requested(BattleStartTests.Rules, "UFO_SCOUT");
        BattleStartTests.Started(campaign);
        var battle = BattleStartTests.Battle(campaign);
        Assert.Same(campaign.Random, battle.Random);
        Assert.Equal((int)campaign.Difficulty, battle.CampaignLink!.Difficulty);
        foreach (var command in new ICampaignCommand[] { new BeginBriefing("", ""), new ConfirmBriefing(), new StartFirstTurn() })
            Assert.DoesNotContain(campaign.Execute(command).Events, static item => item is CampaignActionBlocked);
        Assert.Same(campaign.Random, battle.Random);
        // A command the battle runs through its runtime draws from that stream: the state moves with a unit's walk or kneel (a kneel draws nothing,
        // so the link and the stream are what a rule would find).
        Assert.NotNull(battle.CampaignLink);
    }

    [Fact]
    public void ATrialCopyReadsTheLiveLinkButLeavesTheMemorialAlone()
    {
        var arena = Armed();
        var link = new Link(3);
        var runtime = RuntimeOf(arena, link);
        var copy = Armed();
        runtime.Adopt(copy.Battle);
        Assert.Equal((3, 4), (copy.Battle.CampaignLink!.Difficulty, copy.Battle.CampaignLink.MissionCount));
        Assert.Equal("Name", copy.Battle.CampaignLink.DescribeSoldier(1)!.Name);
        Assert.Same(arena.Battle.Random, copy.Battle.Random);
        // A death in the copy does not reach the campaign's memorial.
        copy.Battle.CampaignLink.KillSoldier(1, default!);
        Assert.Empty(link.Killed);
    }

    [Fact]
    public void AnExceptionInACommandFaultsTheBattleAndEveryLaterCommandIsRefused()
    {
        var arena = Armed();
        var runtime = RuntimeOf(arena);
        runtime.Register<Boom>(static _ => throw new InvalidOperationException("broken rule"));
        var selected = arena.Battle.SelectedUnitId;
        var result = runtime.Execute(new Boom());
        Assert.Equal(BattleRuntime.Faulted.Code, Assert.Single(result.Events.OfType<CampaignActionBlocked>()).Code);
        Assert.Contains("broken rule", runtime.Fault);
        // The next command, one the battle knows well, is refused without running.
        var later = runtime.Execute(new SelectUnit(arena.Soldier.Id));
        Assert.Equal(BattleRuntime.Faulted.Code, Assert.Single(later.Events.OfType<CampaignActionBlocked>()).Code);
        Assert.Equal(selected, arena.Battle.SelectedUnitId);
    }

    [Fact]
    public void AnUnknownCommandIsARefusalNotAnExceptionAndDoesNotFaultTheBattle()
    {
        var arena = Armed();
        var runtime = RuntimeOf(arena);
        var result = runtime.Execute(new Stray());
        Assert.Equal(BattleRuntime.UnknownCommand.Code, Assert.Single(result.Events.OfType<CampaignActionBlocked>()).Code);
        Assert.Null(runtime.Fault);
        Assert.Empty(runtime.Execute(new SelectUnit(arena.Soldier.Id)).Events.OfType<CampaignActionBlocked>());
    }

    [Fact]
    public void ACommandReturnsTheEventsTheRulesRaisedInTheOrderTheyHappenedAndStartsWithAnEmptySink()
    {
        var arena = Armed();
        var victim = Victim(arena, 50);
        var runtime = RuntimeOf(arena);
        arena.Battle.Events.Add(new UnitKneeled(1, true, 0));
        runtime.Register<Detonate>(_ =>
        {
            arena.Battle.PushExplosion(Throw(arena), Center(20, 20));
            arena.Battle.RunStates();
            return new CampaignCommandResult([]);
        });
        var events = runtime.Execute(new Detonate()).Events;
        // The stale event is gone; the blast goes off first, then the hit it deals, then the death the casualty check finds.
        Assert.DoesNotContain(events, static raised => raised is UnitKneeled);
        Assert.Equal([typeof(BattleExploded), typeof(UnitHit), typeof(UnitDied)], events.Select(static raised => raised.GetType()));
        var hit = events.OfType<UnitHit>().Single();
        Assert.Equal((victim.Id, arena.Soldier.Id), (hit.UnitId, hit.AttackerId));
        Assert.True(hit.HealthLost >= 50 && hit.Health <= 0);
        Assert.Equal(new UnitDied(victim.Id, arena.Soldier.Id), events.OfType<UnitDied>().Single());
        // The sink was emptied by the command.
        Assert.Equal(0, runtime.Events.Count);
    }

    [Fact]
    public void ARefusalReturnsItsReasonAloneEvenWhenTheRulesRaisedEvents()
    {
        var arena = Armed();
        var runtime = RuntimeOf(arena);
        runtime.Register<Detonate>(_ =>
        {
            arena.Battle.Events.Add(new UnitKneeled(1, true, 0));
            return BattleRuntime.Blocked(BattleRuntime.UnknownCommand);
        });
        Assert.IsType<CampaignActionBlocked>(Assert.Single(runtime.Execute(new Detonate()).Events));
        Assert.Equal(0, runtime.Events.Count);
    }

    [Fact]
    public void AFireAndABurningUnitRaiseTheirEvents()
    {
        var arena = Duel();
        arena.Put(20, 20, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.Floor, BurnFloor);
        var fuel = arena.Battle.Fuel(arena.Tile(20, 20));
        Assert.True(fuel > 0);
        // A certain ignition (a power far over the flammability) sets the floor alight and says so.
        arena.Battle.Ignite(arena.Tile(20, 20), 1000);
        var ignited = arena.Battle.Events.Of<TileIgnited>().Single();
        Assert.Equal((new BattlePosition(20, 20, 0), (int)arena.Map.Fire(arena.Tile(20, 20))), (ignited.Position, ignited.Turns));
        // A hit that sets a unit alight says so once, and a unit already burning does not say it again.
        var fire = RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageIncendiary) with { RandomType = RuntimeItemDamageRule.RandomFlat, FireThreshold = 0 };
        arena.Alien.Fire = 0;
        for (var attempt = 0; attempt < 20 && arena.Alien.Fire == 0; attempt++) Hurt(arena, 40, fire);
        Assert.True(arena.Alien.Fire > 0);
        Assert.Equal(arena.Alien.Id, Assert.Single(arena.Battle.Events.Of<UnitIgnited>()).UnitId);
    }

    // ---- the hooks ----

    private sealed class Recorder : IBattleHooks
    {
        public List<string> Calls { get; } = [];

        public int DamageToSet { get; init; } = -1;

        public bool SeeNothing { get; init; }

        public Func<BattleDamageHookValues, BattleDamageHookValues>? OnDamage { get; init; }

        public Func<BattleSpecialHookValues, BattleSpecialHookValues>? OnSpecial { get; init; }

        public int? Experience { get; init; }

        public int BonusAdded { get; init; }

        public BattleHitHookValues HitUnit(BattleUnit unit, in BattleAttack attack, int originalDamage, RuntimeItemDamageRule type, BattleHitHookValues values)
        {
            Calls.Add("hit:" + unit.Id + ":" + originalDamage);
            return DamageToSet >= 0 ? values with { Damage = DamageToSet } : values;
        }

        public BattleDamageHookValues DamageUnit(BattleUnit unit, in BattleAttack attack, int damage, int originalDamage, int bodyPart, int side,
            RuntimeItemDamageRule type, BattleDamageHookValues values)
        {
            Calls.Add("damage:" + unit.Id + ":" + damage);
            return OnDamage?.Invoke(values) ?? values;
        }

        public BattleSpecialHookValues DamageSpecialUnit(BattleUnit unit, in BattleAttack attack, int damage, int originalDamage, int bodyPart, int side,
            RuntimeItemDamageRule type, BattleSpecialHookValues values)
        {
            Calls.Add("special:" + unit.Id);
            return OnSpecial?.Invoke(values) ?? values;
        }

        public int AwardExperience(BattleUnit attacker, BattleUnit target, BattleItem weapon, BattleActionType type, int multiplier, int trainingMode)
        {
            Calls.Add("experience:" + attacker.Id + ":" + target.Id + ":" + multiplier + ":" + trainingMode);
            return Experience ?? multiplier;
        }

        public int VisibilityUnit(BattleUnit viewer, BattleUnit? target, int tile, int quality)
        {
            Calls.Add("visibility:" + viewer.Id + ":" + (target?.Id ?? -1));
            return SeeNothing ? 0 : quality;
        }

        public int StatBonus(BattleStatBonusKind kind, BattleUnit? unit, BattleItem? item, int value)
        {
            Calls.Add("bonus:" + kind);
            return value + BonusAdded;
        }
    }

    [Fact]
    public void TheDefaultHooksLeaveTheRulesAloneAndARecordingHookSeesEachCallSite()
    {
        // The same blast with the default hooks and with a recording one that changes nothing ends the same, and the recorder saw every call site of a hit.
        var plain = Armed();
        var watched = Armed();
        var hooks = new Recorder();
        RuntimeOf(watched, hooks: hooks);
        plain.Battle.Random = new SplitMix64RandomSource(7);
        var plainVictim = Victim(plain, 100);
        var watchedVictim = Victim(watched, 100);
        foreach (var arena in new[] { plain, watched })
        {
            arena.Battle.PushExplosion(Throw(arena), Center(20, 20));
            arena.Battle.RunStates();
        }
        Assert.Equal((plainVictim.Health, plainVictim.StunLevel), (watchedVictim.Health, watchedVictim.StunLevel));
        Assert.Contains("hit:" + watchedVictim.Id + ":" + (100 - watchedVictim.Health), hooks.Calls, StringComparer.Ordinal);
        Assert.Contains(hooks.Calls, static call => call.StartsWith("damage:970:", StringComparison.Ordinal));
        Assert.Contains("special:970", hooks.Calls);
        // The thrower is an X-COM soldier and the victim an alien: the experience script runs, for the throw's training mode.
        Assert.Contains(hooks.Calls, static call => call.StartsWith("experience:", StringComparison.Ordinal) && call.EndsWith(":100:4", StringComparison.Ordinal));
        // The damage bonus of the grenade is read through the hook.
        Assert.Contains("bonus:Damage", hooks.Calls);
        // Order of the calls on one victim: the hit script, the damage script, the special script.
        var order = hooks.Calls.Where(static call => call.Contains(":970", StringComparison.Ordinal) && !call.StartsWith("experience", StringComparison.Ordinal)).Select(static call => call.Split(':')[0]);
        Assert.Equal(["hit", "damage", "special"], order.Take(3));
    }

    [Fact]
    public void AHitHookThatChangesTheDamageChangesTheOutcome()
    {
        var arena = Armed();
        RuntimeOf(arena, hooks: new Recorder { DamageToSet = 0 });
        var victim = Victim(arena, 100);
        arena.Battle.PushExplosion(Throw(arena), Center(20, 20));
        arena.Battle.RunStates();
        Assert.Equal(100, victim.Health);
    }

    [Fact]
    public void TheVisibilityAndAccuracyHooksAreCalledWhereTheReferenceRunsItsScripts()
    {
        var arena = Duel();
        var hooks = new Recorder();
        RuntimeOf(arena, hooks: hooks);
        arena.See();
        arena.Battle.Sight().Visible(arena.Soldier, arena.Tile(15, 10));
        Assert.Contains("visibility:" + arena.Soldier.Id + ":" + arena.Alien.Id, hooks.Calls);
        var (_, gun, _) = Shot(arena);
        arena.Battle.FiringAccuracy(arena.Soldier, gun, BattleActionType.AimedShot);
        Assert.Contains("bonus:Accuracy", hooks.Calls);
        // A script that sees nothing hides a unit that is in plain view.
        var sighted = Duel();
        sighted.See();
        Assert.True(sighted.Battle.Sight().Visible(sighted.Soldier, sighted.Tile(15, 10)));
        var blind = Duel();
        RuntimeOf(blind, hooks: new Recorder { SeeNothing = true });
        blind.See();
        Assert.False(blind.Battle.Sight().Visible(blind.Soldier, blind.Tile(15, 10)));
    }

    // ---- the hooks' return values take effect ----

    [Fact]
    public void ADamageHookThatTakesNothingLeavesTheVictimUnhurtAndASpecialHookSetsTheMoraleLossAndTheFire()
    {
        var plain = Duel();
        Hurt(plain, 40, Plain);
        Assert.True(plain.Alien.Health < 100);

        var saved = Duel();
        var hooks = new Recorder { OnDamage = static values => values with { Health = 0 } };
        RuntimeOf(saved, hooks: hooks);
        Hurt(saved, 40, Plain);
        Assert.Equal(100, saved.Alien.Health);

        // The special values: the morale loss and the fire the hook answers are the ones applied (the unit catches fire, and says so).
        var small = Duel();
        RuntimeOf(small, hooks: new Recorder { OnSpecial = static values => values with { MoraleLoss = 0, Fire = 0 } });
        Hurt(small, 40, Plain);
        var big = Duel();
        RuntimeOf(big, hooks: new Recorder { OnSpecial = static values => values with { MoraleLoss = 30, Fire = 6 } });
        Hurt(big, 40, Plain);
        Assert.Equal(30, small.Alien.Morale - big.Alien.Morale);
        Assert.Equal((0, 6), (small.Alien.Fire, big.Alien.Fire));
        Assert.Equal(new UnitIgnited(big.Alien.Id, 6), Assert.Single(big.Battle.Events.Of<UnitIgnited>()));
    }

    [Fact]
    public void ASpecialHooksTransformAndSelfDestructOutputsAreNeverReadBack()
    {
        // The reference gates the conversion on the ammunition's zombie unit and the self-destruct on its chance: a script that clears the flags
        // changes neither (only the chances it writes count).
        var arena = Duel(clip: "zombieUnit: ALIEN\nzombieUnitChance: 100");
        RuntimeOf(arena, hooks: new Recorder { OnSpecial = static values => values with { Transform = false, SelfDestruct = false } });
        var (attack, _, _) = Shot(arena, arena.Alien);
        arena.Battle.Random = new ScriptedDraws(10);
        Hurt(arena, 10, Plain, attack: attack, victim: arena.Soldier);
        Assert.True(arena.Soldier.Respawn);
        Assert.Equal("ALIEN", arena.Soldier.SpawnUnit);
    }

    [Fact]
    public void TheSpecialEffectsAndTheirHookWaitForTheBattlescapeDuringGeneration()
    {
        // The generator's explosions run with no battlescape: BattleUnit::damage skips morale, fire, conversion, self-destruct and the special scripts.
        var hooks = new Recorder();
        var arena = Duel(alienExtra: "    selfDestructItem: GRENADE\n", unitExtra: "    specab: 1\n",
            grenade: "damageType: 3\ndamageAlter: {RandomType: 3, IgnoreSelfDestruct: false}\npower: 60");
        RuntimeOf(arena, hooks: hooks);
        var junk = arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "GRENADE") { FuseTimer = -2 }, new BattlePosition(15, 10, 0));
        Assert.NotNull(junk);
        arena.Alien.Health = 30;
        arena.Alien.Morale = 100;
        arena.Battle.ExplodeOtherJunk();
        Assert.True(arena.Alien.Health < 30);
        Assert.DoesNotContain("special:" + arena.Alien.Id, hooks.Calls);
        Assert.Equal((100, 0, false), (arena.Alien.Morale, arena.Alien.Fire, arena.Alien.AlreadyExploded));
        Assert.False(arena.Battle.Generating);
        // The same blow once the battlescape exists: the special effects and their hook run.
        arena.Soldier.Morale = 100;
        Hurt(arena, 10, Plain, victim: arena.Soldier);
        Assert.Contains("special:" + arena.Soldier.Id, hooks.Calls);
        Assert.True(arena.Soldier.Morale < 100);
    }

    [Fact]
    public void AnExperienceHookSetsTheMultiplier()
    {
        foreach (var (multiplier, expected) in new[] { (0, 0), (200, 2) })
        {
            var arena = Duel();
            RuntimeOf(arena, hooks: new Recorder { Experience = multiplier });
            var (attack, _, _) = Shot(arena);
            arena.Battle.Random = new ScriptedDraws(99, 99, 99, 99, 99);
            arena.Battle.HitUnit(attack, arena.Alien, new BattlePosition(0, 0, 0), 10, Plain, true);
            Assert.Equal(expected, arena.Soldier.Experience.Firing);
        }
    }

    [Fact]
    public void TheStatBonusHooksChangeTheAccuracyOfEveryAction()
    {
        const string accuracies = "accuracyMelee: 100\naccuracyThrow: 100\naccuracyCloseQuarters: 100\naccuracySnap: 100\naccuracyAimed: 100\naccuracyAuto: 100";
        var plain = Duel(gun: accuracies);
        var boosted = Duel(gun: accuracies);
        RuntimeOf(boosted, hooks: new Recorder { BonusAdded = 20 });
        var (_, plainGun, _) = Shot(plain);
        var (_, boostedGun, _) = Shot(boosted);
        foreach (var type in new[] { BattleActionType.SnapShot, BattleActionType.AimedShot, BattleActionType.AutoShot, BattleActionType.Hit,
                     BattleActionType.Throw, BattleActionType.CloseQuarters })
        {
            var before = plain.Battle.FiringAccuracy(plain.Soldier, plainGun, type);
            var after = boosted.Battle.FiringAccuracy(boosted.Soldier, boostedGun, type);
            Assert.True(after > before, $"{type}: {before} then {after}");
        }
    }

    [Fact]
    public void ABonusHookChangesTheBlastsPowerAndAnotherChangesTheTilesVisibility()
    {
        // The damage bonus is the blast's power: the same grenade hurts the victim more with a bonus.
        var plain = Armed();
        var strong = Armed();
        RuntimeOf(strong, hooks: new Recorder { BonusAdded = 100 });
        var near = Victim(plain, 100);
        var nearStrong = Victim(strong, 100);
        foreach (var arena in new[] { plain, strong })
        {
            arena.Battle.Random = new SplitMix64RandomSource(3);
            arena.Battle.PushExplosion(Throw(arena), Center(20, 20));
            arena.Battle.RunStates();
        }
        Assert.True(nearStrong.Health < near.Health);

        // isTileInLOS asks the visibility script too: a script that sees nothing hides a tile in plain view.
        var seen = Duel();
        seen.See();
        var (_, gun, _) = Shot(seen);
        var aim = new BattleAimAction(seen.Soldier, BattleActionType.AimedShot, gun, new BattlePosition(15, 10, 0));
        Assert.True(seen.Battle.Sight().IsTileInLos(aim, seen.Tile(15, 10), false));
        var blind = Duel();
        RuntimeOf(blind, hooks: new Recorder { SeeNothing = true });
        blind.See();
        var (_, blindGun, _) = Shot(blind);
        var blindAim = new BattleAimAction(blind.Soldier, BattleActionType.AimedShot, blindGun, new BattlePosition(15, 10, 0));
        Assert.False(blind.Battle.Sight().IsTileInLos(blindAim, blind.Tile(15, 10), false));
    }

    // ---- more events ----

    [Fact]
    public void AnIncendiaryBlastAndAHitThatSetATileAlightRaiseTheirEventsAndAUnitIgnitesOnceOnly()
    {
        var arena = Duel();
        arena.Put(20, 20, 0, Oxce.Gameplay.Battles.Terrain.BattleTilePart.Floor, BurnFloor);
        var tile = arena.Tile(20, 20);
        var fire = RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageIncendiary) with { RandomType = RuntimeItemDamageRule.RandomFlat, FireThreshold = 0 };
        arena.Battle.Explode(default, new BattlePosition(20 * 16 + 8, 20 * 16 + 8, 12), 60, fire, 1, true);
        Assert.NotEqual(0, arena.Map.Fire(tile));
        Assert.Equal(new BattlePosition(20, 20, 0), Assert.Single(arena.Battle.Events.Of<TileIgnited>(), static raised => raised.Position == new BattlePosition(20, 20, 0)).Position);
        // A unit burning already does not say it again.
        arena.Alien.Fire = 0;
        for (var attempt = 0; attempt < 20 && arena.Alien.Fire == 0; attempt++) Hurt(arena, 40, fire);
        Assert.True(arena.Alien.Fire > 0);
        Hurt(arena, 40, fire);
        Hurt(arena, 40, fire);
        Assert.Single(arena.Battle.Events.Of<UnitIgnited>());
    }

    [Fact]
    public void ACasualtyCheckRaisesAKnockOutAndAConversion()
    {
        var arena = Duel();
        arena.Alien.StunLevel = arena.Alien.Health;
        arena.Alien.MurdererId = arena.Soldier.Id;
        Assert.Single(arena.Battle.CheckForCasualties(null, default));
        Assert.Equal(new UnitKnockedOut(arena.Alien.Id, arena.Soldier.Id), Assert.Single(arena.Battle.Events.Of<UnitKnockedOut>()));

        var infected = Duel();
        infected.Alien.SpawnUnit = "ALIEN";
        infected.Alien.Respawn = true;
        Assert.True(infected.Battle.ConvertInfected());
        var converted = Assert.Single(infected.Battle.Events.Of<UnitConverted>());
        Assert.Equal((infected.Alien.Id, infected.Battle.Units[^1].Id), (converted.UnitId, converted.NewUnitId));
    }

    // ---- the campaign path: the fault, the save gate, the pre-battle commands, the stream and the trial copy ----

    private static BattleRuntime RuntimeOf(CampaignState campaign) =>
        campaign.GetQuery<IBattleQuery>() is CampaignBattles { Runtime: { } runtime } ? runtime : throw new InvalidOperationException();

    [Fact]
    public void AFaultedBattleRefusesEveryLaterCommandThroughTheCampaignIncludingThePreBattleOnesAndIsNotSaved()
    {
        var arena = Duel();
        var runtime = RuntimeOf(arena.Campaign);
        Assert.Null(arena.Campaign.QuerySaveRefusal());
        runtime.Register<Boom>(static _ => throw new InvalidOperationException("broken rule"));
        runtime.Execute(new Boom());
        Assert.NotNull(runtime.Fault);
        Assert.Equal(BattleRuntime.Faulted.Text, arena.Campaign.QuerySaveRefusal());
        foreach (var command in new ICampaignCommand[] { new BeginBriefing("", ""), new ConfirmBriefing(), new StartFirstTurn(), new SelectUnit(arena.Soldier.Id) })
            Assert.Equal(BattleRuntime.Faulted.Code, Assert.Single(arena.Campaign.Execute(command).Events.OfType<CampaignActionBlocked>()).Code);
    }

    [Fact]
    public void AnExceptionInTheFirstTurnFaultsTheBattleInsteadOfThrowing()
    {
        var (campaign, _) = BattleStartTests.Requested(BattleStartTests.Rules, "UFO_SCOUT");
        BattleStartTests.Started(campaign);
        Assert.DoesNotContain(campaign.Execute(new BeginBriefing("", "")).Events, static item => item is CampaignActionBlocked);
        Assert.DoesNotContain(campaign.Execute(new ConfirmBriefing()).Events, static item => item is CampaignActionBlocked);
        // A defect: a unit that stands outside the map, which the first turn puts on its tile.
        var stray = new BattleUnit(990, BattleFaction.Hostile, BattleController.Ai) { ArmorId = BattleStartTests.Battle(campaign).Units[0].ArmorId, ArmorSize = 1, UnitType = "ALIEN", Health = 10 };
        BattleStartTests.Battle(campaign).AddUnit(stray);
        stray.Position = new BattlePosition(999, 999, 0);
        var result = campaign.Execute(new StartFirstTurn());
        Assert.Equal(BattleRuntime.Faulted.Code, Assert.Single(result.Events.OfType<CampaignActionBlocked>()).Code);
        Assert.NotNull(RuntimeOf(campaign).Fault);
        Assert.Equal(BattleRuntime.Faulted.Text, campaign.QuerySaveRefusal());
        Assert.Equal(BattleRuntime.Faulted.Code, Assert.Single(campaign.Execute(new StartFirstTurn()).Events.OfType<CampaignActionBlocked>()).Code);
    }

    [Fact]
    public void ABattleCommandNoHandlerTakesIsARefusalNotAnException()
    {
        var arena = Duel();
        Assert.Equal(BattleRuntime.UnknownCommand.Code, Assert.Single(arena.Campaign.Execute(new Stray()).Events.OfType<CampaignActionBlocked>()).Code);
        Assert.Null(RuntimeOf(arena.Campaign).Fault);
        var (campaign, _) = BattleStartTests.Requested(BattleStartTests.Rules, "UFO_SCOUT");
        Assert.Equal(BattleRuntime.UnknownCommand.Code, Assert.Single(campaign.Execute(new Stray()).Events.OfType<CampaignActionBlocked>()).Code);
    }

    [Fact]
    public void ARestoredBattleDrawsFromTheCampaignsStreamFromTheRestoreOn()
    {
        var (campaign, _) = BattleStartTests.Requested(BattleStartTests.Rules, "UFO_SCOUT");
        BattleStartTests.Started(campaign);
        var snapshot = campaign.Capture();
        // The restored battle's stream is the campaign's before any command ran (the restore's own stream would be the battle's otherwise).
        var restored = CampaignState.Restore(snapshot, campaign.Content, new SplitMix64RandomSource(23), BattleStartTests.TerrainFiles());
        var battle = BattleStartTests.Battle(restored);
        Assert.Same(restored.Random, battle.Random);
        Assert.Equal((int)restored.Difficulty, battle.CampaignLink!.Difficulty);
        Assert.Same(restored.Random, RuntimeOf(restored).Random);
    }

    [Fact]
    public void ACommandBumpsTheRevisionOnceHoweverOftenItMarksTheBattleChangedAndNotAtAllWhenItChangesNothing()
    {
        var arena = Armed();
        var runtime = RuntimeOf(arena);
        runtime.Register<Stray>(command =>
        {
            arena.Battle.MarkChanged();
            arena.Battle.MarkChanged();
            arena.Battle.MarkChanged();
            return new CampaignCommandResult([]);
        });
        runtime.Register<Detonate>(static _ => new CampaignCommandResult([]));
        var revision = runtime.Revision;
        runtime.Execute(new Stray());
        Assert.Equal(revision + 1, runtime.Revision);
        // The commit emptied the marks: a command that changes nothing leaves the revision, and the next one bumps it once more.
        runtime.Execute(new Detonate());
        Assert.Equal(revision + 1, runtime.Revision);
        runtime.Execute(new Stray());
        Assert.Equal(revision + 2, runtime.Revision);
        // A rule that ran outside a command bumps the revision itself, at each mark.
        arena.Battle.MarkChanged();
        arena.Battle.MarkChanged();
        Assert.Equal(revision + 4, runtime.Revision);
        runtime.Execute(new Detonate());
        Assert.Equal(revision + 4, runtime.Revision);
    }

    [Fact]
    public void AFaultedOrRefusedCommandStillCommitsWhatItMarkedAsOneBump()
    {
        var arena = Armed();
        var runtime = RuntimeOf(arena);
        runtime.Register<Boom>(_ =>
        {
            arena.Battle.MarkChanged();
            arena.Battle.MarkChanged();
            throw new InvalidOperationException("broken rule");
        });
        runtime.Register<Stray>(_ =>
        {
            arena.Battle.MarkChanged();
            arena.Battle.MarkChanged();
            return BattleRuntime.Blocked(BattleRuntime.UnknownCommand);
        });
        var revision = runtime.Revision;
        Assert.Equal(BattleRuntime.UnknownCommand.Code, Assert.Single(runtime.Execute(new Stray()).Events.OfType<CampaignActionBlocked>()).Code);
        Assert.Equal(revision + 1, runtime.Revision);
        Assert.Equal(BattleRuntime.Faulted.Code, Assert.Single(runtime.Execute(new Boom()).Events.OfType<CampaignActionBlocked>()).Code);
        Assert.Equal(revision + 2, runtime.Revision);
        // The depth is balanced after the fault: a refused command (the battle is faulted) changes nothing, and direct marks bump at once again.
        runtime.Execute(new Stray());
        arena.Battle.MarkChanged();
        Assert.Equal(revision + 3, runtime.Revision);
    }

    [Fact]
    public void TheBriefingAndTheFirstTurnAreOneBumpOfTheRevisionEach()
    {
        var (campaign, _) = BattleStartTests.Requested(BattleStartTests.Rules, "UFO_SCOUT");
        BattleStartTests.Started(campaign);
        var battle = BattleStartTests.Battle(campaign);
        var revision = battle.Revision;
        Assert.DoesNotContain(campaign.Execute(new ConfirmBriefing()).Events, static item => item is CampaignActionBlocked);
        Assert.Equal(revision + 1, battle.Revision);
        Assert.DoesNotContain(campaign.Execute(new StartFirstTurn()).Events, static item => item is CampaignActionBlocked);
        Assert.Equal(revision + 2, battle.Revision);
        // A refused one (not before the first turn any more) leaves it.
        Assert.Equal("battle.not-before-first-turn", Assert.Single(campaign.Execute(new StartFirstTurn()).Events.OfType<CampaignActionBlocked>()).Code);
        Assert.Equal(revision + 2, battle.Revision);
    }

    [Fact]
    public void AWalkIsOneBumpOfTheRevisionForTheWholeCommand()
    {
        var arena = Duel();
        var runtime = RuntimeOf(arena.Campaign);
        var revision = arena.Battle.Revision;
        Assert.Empty(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, new BattlePosition(11, 10, 0))).Events.OfType<CampaignActionBlocked>());
        Assert.Equal(revision + 1, arena.Battle.Revision);
        Assert.Null(runtime.Fault);
    }

    [Fact]
    public void ARefusedWalkChangesNothingAndTheBattleIsNotFaulted()
    {
        // Phase 7 branch 5 S11: with no copy to run first, a refusal comes before the first change; RunOnLiveBattle faults the battle if one ever comes after.
        var arena = Duel();
        var runtime = RuntimeOf(arena.Campaign);
        var revision = arena.Battle.Revision;
        var at = arena.Soldier.Position;
        var timeUnits = arena.Soldier.TimeUnits;
        var blocked = Assert.Single(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, new BattlePosition(-5, 10, 0))).Events.OfType<CampaignActionBlocked>());
        Assert.NotEmpty(blocked.Code);
        Assert.Equal(revision, arena.Battle.Revision);
        Assert.Equal((at, timeUnits), (arena.Soldier.Position, arena.Soldier.TimeUnits));
        Assert.Null(runtime.Fault);
    }

    private sealed class ViewerRecorder(List<BattleUnit> viewers) : IBattleHooks
    {
        public int VisibilityUnit(BattleUnit viewer, BattleUnit? target, int tile, int quality)
        {
            viewers.Add(viewer);
            return quality;
        }
    }
}
