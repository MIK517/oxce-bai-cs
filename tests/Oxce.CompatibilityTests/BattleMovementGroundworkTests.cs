using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;
using static Oxce.CompatibilityTests.BattleStartTests;
using static Oxce.CompatibilityTests.BattleTestSupport;
using static Oxce.CompatibilityTests.BattleTestTerrain;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S1: the movement groundwork. The battle constants and the armor's movement fields in the runtime rules,
/// <c>BattleActionCost</c> (<c>updateTU</c>, <c>haveTU</c>, <c>BattleUnit::getActionTUs</c> and <c>applyPercentages</c>),
/// <c>BattlescapeGame::checkReservedTU</c>, <c>BattleUnit::getMainHandWeapon</c>, and the unit's primitives (<c>lookAt</c>, <c>turn</c>,
/// <c>abortTurn</c>, <c>directionTo</c>, <c>startWalking</c> and <c>keepWalking</c> as whole steps, <c>setTile</c>,
/// <c>updateTileFloorState</c>, <c>getReactionScore</c>, the turn and kneel costs)
/// (<c>src/Mod/Mod.cpp</c>, <c>Armor.cpp</c>, <c>RuleItem.cpp</c>, <c>src/Battlescape/BattlescapeGame.cpp</c>,
/// <c>src/Savegame/BattleUnit.cpp</c> at 4df3a5e). The suited soldier carries a gun (right hand, loaded) and a grenade; the battle is past
/// its first turn.
/// </summary>
public sealed class BattleMovementGroundworkTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";
    private const string SuitText = "- type: SUIT\n    movementType: 1";

    private static string WithGun(string extra) => Replaced(Rules, GunText, GunText + "\n    " + extra);

    private static string WithSuit(string extra) => Replaced(Rules, SuitText, SuitText + "\n    " + extra);

    internal static (CampaignState Campaign, BattleState Battle, BattleUnit Soldier, RuntimeRuleCatalog Rules) Open(string? rules = null,
        Action<Dictionary<string, byte[]>>? terrain = null, string ufo = "UFO_SUITED")
    {
        var (campaign, _) = Requested(rules ?? Rules, ufo);
        if (terrain is not null) campaign.BattleTerrainFiles = TerrainFiles(terrain);
        Started(campaign);
        Assert.IsType<BriefingOpened>(Assert.Single(campaign.Execute(new BeginBriefing("T", "C")).Events));
        Assert.IsType<BriefingConfirmed>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));
        Assert.IsType<BattleFirstTurnStarted>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
        var battle = Battle(campaign);
        return (campaign, battle, battle.Units.Single(static unit => unit.Faction == BattleFaction.Player), campaign.Content.RuntimeRules);
    }

    private static BattleItem Gun(BattleState battle, BattleUnit unit) =>
        battle.InventoryOf(unit).Single(static item => item.ItemType == "GUN" && item.Slot == "STR_RIGHT_HAND");

    // ---- the rules ----

    [Fact]
    public void TheBattleConstantsDefaultAndAreReadFromTheGlobalRules()
    {
        var (_, _, _, defaults) = Open();
        Assert.Equal((16, 24, 0, 10, false, 0), (defaults.Campaign.MaximumStaticLightDistance, defaults.Campaign.MaximumDynamicLightDistance,
            defaults.Campaign.EnhancedLighting, defaults.Campaign.TooMuchSmokeThreshold, defaults.Campaign.ExtendedRunningCost,
            defaults.Campaign.ExtendedMovementCostRounding));
        var rules = Rules + "\nlighting: {maxStatic: 12, maxDynamic: 20, enhanced: 1}\ntooMuchSmokeThreshold: 7\nextendedRunningCost: true\nextendedMovementCostRounding: 2\n";
        var (_, _, _, set) = Open(rules);
        Assert.Equal((12, 20, 1, 7, true, 2), (set.Campaign.MaximumStaticLightDistance, set.Campaign.MaximumDynamicLightDistance,
            set.Campaign.EnhancedLighting, set.Campaign.TooMuchSmokeThreshold, set.Campaign.ExtendedRunningCost,
            set.Campaign.ExtendedMovementCostRounding));
        // A key of the lighting section left out keeps its default.
        var (_, _, _, partial) = Open(Rules + "\nlighting: {enhanced: 2}\n");
        Assert.Equal((16, 24, 2), (partial.Campaign.MaximumStaticLightDistance, partial.Campaign.MaximumDynamicLightDistance, partial.Campaign.EnhancedLighting));
    }

    [Fact]
    public void TheArmorsMovementFieldsDefaultAndAreRead()
    {
        var (_, _, _, defaults) = Open();
        var plain = defaults.Battle.Equipment.Armors["SUIT"];
        Assert.Equal((1, false, true, (bool?)null, (bool?)null, (bool?)null, (bool?)null), (plain.TurnCost, plain.TurnBeforeFirstStep, plain.AllowsMoving,
            plain.AllowsKneeling, plain.AllowsRunning, plain.AllowsStrafing, plain.AllowsSneaking));
        Assert.Equal(new RuntimeArmorStepCosts((100, 50), (75, 75), (100, 50), (100, 50), (100, 50), (75, 75), (100, 50), (100, 0), (100, 0), (100, 50),
            (100, 50), (100, 0)), plain.StepCosts);
        Assert.Equal((-1, -1, -1), (plain.StandHeight, plain.KneelHeight, plain.FloatHeight));

        var rules = WithSuit("turnCost: 3\n    turnBeforeFirstStep: true\n    allowsMoving: false\n    allowsKneeling: false\n    allowsRunning: true\n" +
            "    allowsStrafing: false\n    allowsSneaking: true\n    standHeight: 22\n    kneelHeight: 12\n    floatHeight: 2\n" +
            "    moveCost: {walkPercent: [90, 40], runPercent: [70, 70], strafePercent: [120, 60], " +
            "sneakPercent: [80, 30], flyWalkPercent: [95, 45], flyRunPercent: [65, 65], " +
            "flyStrafePercent: [110, 55], flyUpPercent: [130, 10], flyDownPercent: [50, 5], " +
            "climbUpPercent: [140, 20], climbDownPercent: [60, 25], gravLiftPercent: [30, 15]}");
        var (_, _, _, set) = Open(rules);
        var armor = set.Battle.Equipment.Armors["SUIT"];
        Assert.Equal((3, true, false, (bool?)false, (bool?)true, (bool?)false, (bool?)true), (armor.TurnCost, armor.TurnBeforeFirstStep, armor.AllowsMoving,
            armor.AllowsKneeling, armor.AllowsRunning, armor.AllowsStrafing, armor.AllowsSneaking));
        Assert.Equal(new RuntimeArmorStepCosts((90, 40), (70, 70), (120, 60), (80, 30), (95, 45), (65, 65), (110, 55), (130, 10), (50, 5), (140, 20),
            (60, 25), (30, 15)), armor.StepCosts);
        Assert.Equal((22, 12, 2), (armor.StandHeight, armor.KneelHeight, armor.FloatHeight));
        // The unit's own heights come from its unit rule or soldier rule when the armor sets none.
        Assert.Equal(new RuntimeUnitHeights(0, 0, 0), defaults.Battle.Equipment.Units["ALIEN"].Heights);
        var withHeights = Replaced(Rules, "- type: ALIEN\n    armor: SMALL_VEHICLE_ARMOR", "- type: ALIEN\n    standHeight: 20\n    kneelHeight: 14\n    floatHeight: 3\n    armor: SMALL_VEHICLE_ARMOR");
        Assert.Equal(new RuntimeUnitHeights(20, 14, 3), Open(withHeights).Rules.Battle.Equipment.Units["ALIEN"].Heights);
    }

    [Fact]
    public void AnItemsActionCostsResolveTheirFallbacks()
    {
        var rules = WithGun("tuAuto: 40\n    tuSnap: 25\n    tuAimed: 80\n    costAimed: {energy: 20}\n    flatSnap: true");
        var (_, _, _, set) = Open(rules);
        var gun = set.Battle.Equipment.Items["GUN"].ActionCosts;
        // Time: each action's own percentage. Energy falls back from auto and snap to aimed, then each flag to the use flags (time percent, the rest flat).
        Assert.Equal((new RuntimeUseCost(80, 20, 0, 0, 0, 0), new RuntimeUseFlat(false, true, true, true, true, true)), gun.Of(RuntimeItemActionCosts.Aimed));
        Assert.Equal((new RuntimeUseCost(40, 20, 0, 0, 0, 0), new RuntimeUseFlat(false, true, true, true, true, true)), gun.Of(RuntimeItemActionCosts.Auto));
        Assert.Equal((new RuntimeUseCost(25, 20, 0, 0, 0, 0), new RuntimeUseFlat(true, true, true, true, true, true)), gun.Of(RuntimeItemActionCosts.Snap));
        // Defaults: use, throw and unprime cost 25 percent, prime 50; mind control and panic follow the use cost.
        var grenade = set.Battle.Equipment.Items["GRENADE"].ActionCosts;
        Assert.Equal(25, grenade.Of(RuntimeItemActionCosts.Use).Cost.Time);
        Assert.Equal(25, grenade.Of(RuntimeItemActionCosts.Throw).Cost.Time);
        Assert.Equal(50, grenade.Of(RuntimeItemActionCosts.Prime).Cost.Time);
        Assert.Equal(25, grenade.Of(RuntimeItemActionCosts.Unprime).Cost.Time);
        Assert.Equal(25, grenade.Of(RuntimeItemActionCosts.MindControl).Cost.Time);
        Assert.Equal(25, grenade.Of(RuntimeItemActionCosts.Panic).Cost.Time);
        // The use cost of a psi amplifier without an attack name is none; with one it is the rule's. An item with no prime action name primes for nothing.
        var psi = set.Battle.Equipment.Items["AMP"].ActionCosts;
        Assert.Equal(0, psi.Of(RuntimeItemActionCosts.Use).Cost.Time);
        Assert.Equal(25, psi.Of(RuntimeItemActionCosts.MindControl).Cost.Time);
        var named = Replaced(Rules, "- type: AMP\n    battleType: 9", "- type: AMP\n    battleType: 9\n    psiAttackName: STR_PSI\n    primeActionName: ''\n    flatRate: true");
        var amp = Open(named).Rules.Battle.Equipment.Items["AMP"].ActionCosts;
        Assert.Equal(25, amp.Of(RuntimeItemActionCosts.Use).Cost.Time);
        Assert.Equal(0, amp.Of(RuntimeItemActionCosts.Prime).Cost.Time);
        // flatRate makes the use time flat, and the flat melee and aimed times follow it unless they say otherwise.
        Assert.True(amp.Of(RuntimeItemActionCosts.Use).Flat.Time);
        Assert.True(amp.Of(RuntimeItemActionCosts.Melee).Flat.Time);
        Assert.False(amp.Of(RuntimeItemActionCosts.Throw).Flat.Time);
    }

    // ---- the action cost ----

    [Fact]
    public void APercentageCostIsAShareOfTheUnitsStatisticAtLeastOneAndAFlatCostIsTaken()
    {
        var rules = WithGun("tuAimed: 80\n    tuAuto: 1\n    costSnap: {time: 25, energy: 10, morale: 50, health: 5, stun: 20, mana: 15}\n    flatSnap: {time: false, energy: false, morale: true, health: false, stun: false, mana: true}\n" +
            "    tuMelee: 0");
        var (_, battle, soldier, set) = Open(rules);
        var gun = Gun(battle, soldier);
        var stats = soldier.Stats;
        var aimed = BattleActionCost.For(set, BattleActionType.AimedShot, soldier, gun);
        Assert.Equal((int)Math.Floor(stats.TimeUnits * 80 / 100f), aimed.Time);
        // A percentage of 1 of the time units still costs at least 1.
        Assert.Equal(Math.Max(1, (int)Math.Floor(stats.TimeUnits * 1 / 100f)), BattleActionCost.For(set, BattleActionType.AutoShot, soldier, gun).Time);
        // Every resource: a percentage of the statistic (morale of 110 less bravery, stun of the health), or flat as written.
        var snap = BattleActionCost.For(set, BattleActionType.SnapShot, soldier, gun);
        Assert.Equal((int)Math.Floor(stats.TimeUnits * 25 / 100f), snap.Time);
        Assert.Equal((int)Math.Floor(stats.Stamina * 10 / 100f), snap.Energy);
        Assert.Equal(50, snap.Morale);
        Assert.Equal(Math.Max(1, (int)Math.Floor(stats.Health * 5 / 100f)), snap.Health);
        Assert.Equal(Math.Max(1, (int)Math.Floor(stats.Health * 20 / 100f)), snap.Stun);
        Assert.Equal(15, snap.Mana);
        // The melee time is zero and stays zero, and an action the weapon has no cost for (a kneel) is free.
        Assert.Equal(0, BattleActionCost.For(set, BattleActionType.Hit, soldier, gun).Time);
        Assert.Equal(0, BattleActionCost.For(set, BattleActionType.Kneel, soldier, gun).Time);
        Assert.Equal(0, BattleActionCost.For(set, BattleActionType.AimedShot, soldier, null).Time);
        Assert.Equal(0, BattleActionCost.For(set, BattleActionType.None, soldier, gun).Time);
    }

    [Fact]
    public void AFlatTimeCostIgnoresTheStatisticsAndForcedAbsoluteCostsStayAtTheirDefault()
    {
        var rules = WithGun("tuAimed: 37\n    flatAimed: {time: true}");
        var (_, battle, soldier, set) = Open(rules);
        Assert.Equal(37, BattleActionCost.For(set, BattleActionType.AimedShot, soldier, Gun(battle, soldier)).Time);
        // The default option converts nothing: a percentage is still a share of the statistic.
        Assert.False(BattleOptions.ForcedAbsoluteCosts);
        var grenade = battle.InventoryOf(soldier).Single(static item => item.ItemType == "GRENADE");
        Assert.Equal((int)Math.Floor(soldier.Stats.TimeUnits * 50 / 100f), BattleActionCost.For(set, BattleActionType.Prime, soldier, grenade).Time);
        Assert.Equal((int)Math.Floor(soldier.Stats.TimeUnits * 25 / 100f), BattleActionCost.For(set, BattleActionType.Throw, soldier, grenade).Time);
    }

    [Fact]
    public void HaveTimeUnitsNamesTheFirstResourceTheActorLacks()
    {
        var (_, battle, soldier, set) = Open(WithGun("tuAimed: 50"));
        var cost = BattleActionCost.For(set, BattleActionType.AimedShot, soldier, Gun(battle, soldier));
        Assert.Equal(BattleActionShortfall.None, cost.Shortfall());
        Assert.True(cost.HaveTimeUnits());
        // Nothing to pay is "no action", before any other check.
        Assert.Equal(BattleActionShortfall.NoAction, new BattleActionCost(soldier).Shortfall());
        cost.Energy = soldier.Energy + 1;
        cost.Morale = soldier.Morale + 1;
        Assert.Equal(BattleActionShortfall.Energy, cost.Shortfall());
        cost.Energy = 0;
        Assert.Equal(BattleActionShortfall.Morale, cost.Shortfall());
        cost.Morale = 0;
        cost.Health = soldier.Health;
        Assert.Equal(BattleActionShortfall.Health, cost.Shortfall());
        cost.Health = 0;
        cost.Mana = soldier.Mana + 1;
        Assert.Equal(BattleActionShortfall.Mana, cost.Shortfall());
        cost.Mana = 0;
        cost.Stun = soldier.Health - soldier.StunLevel;
        Assert.Equal(BattleActionShortfall.Stun, cost.Shortfall());
        cost.Stun = 0;
        soldier.TimeUnits = cost.Time - 1;
        Assert.Equal(BattleActionShortfall.TimeUnits, cost.Shortfall());
        var before = (soldier.TimeUnits, soldier.Energy);
        cost.Time = 3;
        soldier.TimeUnits = 10;
        soldier.SpendCost(cost);
        Assert.Equal((7, before.Energy), (soldier.TimeUnits, soldier.Energy));
    }

    // ---- the reserve ----

    private static ReservedTimeUnitsResult Check(BattleState battle, RuntimeRuleCatalog rules, BattleUnit unit, int timeUnits, int energy = 0, bool justChecking = false) =>
        battle.CheckReservedTimeUnits(rules, unit, timeUnits, energy, justChecking);

    [Fact]
    public void ReservedTimeUnitsFallBackFromAutoToSnapToAimedToNone()
    {
        // 60 time units: aimed 30, snap 15, auto 24.
        var (_, battle, soldier, set) = Open(WithGun("tuAimed: 50\n    tuSnap: 25\n    tuAuto: 40"));
        soldier.TimeUnits = 60;
        // Nothing reserved: anything the unit can pay.
        Assert.True(Check(battle, set, soldier, 60).Allowed);
        // The unit's own time units are the caller's to check: with no reserve there is nothing to refuse.
        Assert.True(Check(battle, set, soldier, 61).Allowed);
        var cases = new (BattleActionType Reserve, int Cost, ReservedTimeUnitsWarning Warning)[]
        {
            (BattleActionType.AimedShot, 30, ReservedTimeUnitsWarning.AimedShot),
            (BattleActionType.SnapShot, 15, ReservedTimeUnitsWarning.SnapShot),
            (BattleActionType.AutoShot, 24, ReservedTimeUnitsWarning.AutoShot),
        };
        foreach (var (reserve, cost, warning) in cases)
        {
            battle.TimeUnitsReserved = (int)reserve;
            Assert.Equal(new ReservedTimeUnitsResult(true), Check(battle, set, soldier, 60 - cost));
            Assert.Equal(new ReservedTimeUnitsResult(false, warning), Check(battle, set, soldier, 60 - cost + 1));
            // Just checking: the same answer, with no warning.
            Assert.Equal(new ReservedTimeUnitsResult(false, ReservedTimeUnitsWarning.Silent), Check(battle, set, soldier, 60 - cost + 1, justChecking: true));
            Assert.True(Check(battle, set, soldier, 60 - cost, justChecking: true).Allowed);
        }

        // A weapon with no auto shot reserves a snap shot; with neither, an aimed shot.
        var (_, snapBattle, snapSoldier, snapSet) = Open(WithGun("tuAimed: 50\n    tuSnap: 25\n    tuAuto: 0"));
        snapBattle.TimeUnitsReserved = (int)BattleActionType.AutoShot;
        Assert.True(Check(snapBattle, snapSet, snapSoldier, 60 - 15).Allowed);
        Assert.False(Check(snapBattle, snapSet, snapSoldier, 60 - 15 + 1).Allowed);
        var (_, aimedBattle, aimedSoldier, aimedSet) = Open(WithGun("tuAimed: 50\n    tuSnap: 0\n    tuAuto: 0"));
        aimedBattle.TimeUnitsReserved = (int)BattleActionType.AutoShot;
        Assert.True(Check(aimedBattle, aimedSet, aimedSoldier, 60 - 30).Allowed);
        Assert.Equal(ReservedTimeUnitsWarning.AutoShot, Check(aimedBattle, aimedSet, aimedSoldier, 60 - 30 + 1).Warning);
        // No aimed shot either: nothing is reserved.
        var (_, noneBattle, noneSoldier, noneSet) = Open(WithGun("tuAimed: 0\n    tuSnap: 0\n    tuAuto: 0"));
        noneBattle.TimeUnitsReserved = (int)BattleActionType.AutoShot;
        Assert.True(Check(noneBattle, noneSet, noneSoldier, 60).Allowed);
    }

    [Fact]
    public void AUnitWithTooFewTimeUnitsForTheReserveCanSpendAnyway()
    {
        var (_, battle, soldier, set) = Open(WithGun("tuAimed: 50"));
        battle.TimeUnitsReserved = (int)BattleActionType.AimedShot;
        soldier.TimeUnits = 29;
        // The reserve of 30 is more than it has: nothing can be reserved, unless it is only a check.
        Assert.Equal(new ReservedTimeUnitsResult(true), Check(battle, set, soldier, 29));
        Assert.Equal(new ReservedTimeUnitsResult(false, ReservedTimeUnitsWarning.Silent), Check(battle, set, soldier, 1, justChecking: true));
    }

    [Fact]
    public void TheKneelReserveAddsTheKneelCostForAStandingUnitWhoseArmorAllowsIt()
    {
        var (_, battle, soldier, set) = Open(WithGun("tuAimed: 50"));
        soldier.TimeUnits = 60;
        battle.KneelReserved = true;
        // Kneel alone: a standing soldier keeps 4. The type is none, so the warning is the combined one.
        Assert.Equal(new ReservedTimeUnitsResult(true), Check(battle, set, soldier, 56));
        Assert.Equal(new ReservedTimeUnitsResult(false, ReservedTimeUnitsWarning.KneelingAndFiring), Check(battle, set, soldier, 57));
        // With a shot: 30 and 4.
        battle.TimeUnitsReserved = (int)BattleActionType.AimedShot;
        Assert.True(Check(battle, set, soldier, 26).Allowed);
        Assert.Equal(ReservedTimeUnitsWarning.KneelingAndFiring, Check(battle, set, soldier, 27).Warning);
        // A kneeling unit keeps nothing for kneeling; with no shot either, just the type's check applies (none, but the kneel reserve is set).
        soldier.Kneel(true);
        Assert.True(Check(battle, set, soldier, 30).Allowed);
        Assert.False(Check(battle, set, soldier, 31).Allowed);
        // No weapon to cost: only the kneel is reserved, and its warning is the kneeling one.
        soldier.Kneel(false);
        battle.TimeUnitsReserved = (int)BattleActionType.AutoShot;
        var gun = Gun(battle, soldier);
        gun.Slot = "STR_BELT";
        Assert.Null(battle.MainHandWeapon(set, soldier, quickest: false));
        Assert.Equal(new ReservedTimeUnitsResult(true), Check(battle, set, soldier, 56));
        Assert.Equal(new ReservedTimeUnitsResult(false, ReservedTimeUnitsWarning.Kneeling), Check(battle, set, soldier, 57));
        // The armor can forbid kneeling; then nothing is reserved for it.
        var (_, forbidBattle, forbidSoldier, forbidSet) = Open(WithSuit("allowsKneeling: false"));
        forbidSoldier.TimeUnits = 60;
        forbidBattle.KneelReserved = true;
        Assert.False(BattleState.AllowsKneeling(forbidSet, forbidSoldier));
        Assert.True(Check(forbidBattle, forbidSet, forbidSoldier, 100).Allowed);
        // For an armor that allows it the same reserve keeps 4 for the kneel and refuses a spend of 100.
        battle.TimeUnitsReserved = 0;
        Assert.Equal(new ReservedTimeUnitsResult(false, ReservedTimeUnitsWarning.KneelingAndFiring), Check(battle, set, soldier, 100));
        // An armor that says nothing lets a soldier kneel and a unit that is no soldier not.
        Assert.True(BattleState.AllowsKneeling(set, soldier));
        var alien = new BattleUnit(990, BattleFaction.Hostile, BattleController.Ai) { ArmorId = soldier.ArmorId, UnitType = "ALIEN" };
        Assert.False(BattleState.AllowsKneeling(set, alien));
    }

    [Fact]
    public void AnotherSidesUnitOnlyNeedsItsTimeUnitsAndTheHostileReserveIsRefusedAsBranchSixs()
    {
        var (_, battle, soldier, set) = Open(WithGun("tuAimed: 50"));
        soldier.TimeUnits = 20;
        battle.TimeUnitsReserved = (int)BattleActionType.AimedShot;
        battle.KneelReserved = true;
        // It is the other side's turn: the reserve does not apply.
        battle.Side = BattleFaction.Hostile;
        Assert.Equal(new ReservedTimeUnitsResult(true), Check(battle, set, soldier, 20));
        Assert.Equal(new ReservedTimeUnitsResult(false), Check(battle, set, soldier, 21));
        battle.Side = BattleFaction.Neutral;
        Assert.Equal(new ReservedTimeUnitsResult(true), Check(battle, set, soldier, 20));
        // A hostile unit on the hostile side reserves by the AI's percentage, which branch 6 ports.
        var alien = battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);
        battle.Side = BattleFaction.Hostile;
        var hostile = Check(battle, set, alien, 1);
        Assert.False(hostile.Allowed);
        Assert.Equal("battle.hostile-reserve-unsupported", hostile.Unsupported!.Code);
        // A neutral unit on the neutral side has no reserve either.
        battle.Side = BattleFaction.Neutral;
        alien.Faction = BattleFaction.Neutral;
        alien.TimeUnits = 5;
        Assert.Equal(new ReservedTimeUnitsResult(true), Check(battle, set, alien, 5));
        Assert.Equal(new ReservedTimeUnitsResult(false), Check(battle, set, alien, 6));
    }

    [Fact]
    public void TheMainHandWeaponIsTheLoadedOneThenTheCheaperSnapShotThenTheActiveHand()
    {
        var (_, battle, soldier, set) = Open(WithGun("tuSnap: 30"));
        var right = Gun(battle, soldier);
        Assert.Equal(right, battle.MainHandWeapon(set, soldier));
        // A second gun in the left hand, unloaded: ignored when ammunition is needed, picked when it is not and it is the quicker.
        var left = new BattleItem(battle.TakeItemId(), "GUN") { OwnerUnitId = soldier.Id, Slot = "STR_LEFT_HAND", IsWeaponWithAmmo = true };
        battle.AddItem(left);
        Assert.Equal(right, battle.MainHandWeapon(set, soldier));
        Assert.Equal(right, battle.MainHandWeapon(set, soldier, quickest: false));
        // Load it: equal snap costs go to the right hand when the quickest is asked for, the active hand when not (a player's unit).
        var clip = new BattleItem(battle.TakeItemId(), "CLIP") { AmmoQuantity = 5, IsAmmo = true };
        battle.AddItem(clip);
        left.AmmoSlots[0] = clip.Id;
        Assert.Equal(right, battle.MainHandWeapon(set, soldier, quickest: true));
        soldier.ActiveHand = "STR_LEFT_HAND";
        Assert.Equal(left, battle.MainHandWeapon(set, soldier, quickest: false));
        soldier.ActiveHand = "STR_RIGHT_HAND";
        Assert.Equal(right, battle.MainHandWeapon(set, soldier, quickest: false));
        // The weapon without a snap shot loses to the one with.
        var (_, otherBattle, otherSoldier, otherSet) = Open(WithGun("tuSnap: 30"));
        var otherRight = Gun(otherBattle, otherSoldier);
        var otherClip = new BattleItem(otherBattle.TakeItemId(), "CLIP") { AmmoQuantity = 5, IsAmmo = true };
        otherBattle.AddItem(otherClip);
        var grenade = new BattleItem(otherBattle.TakeItemId(), "GRENADE") { OwnerUnitId = otherSoldier.Id, Slot = "STR_LEFT_HAND" };
        otherBattle.AddItem(grenade);
        Assert.Equal(otherRight, otherBattle.MainHandWeapon(otherSet, otherSoldier, quickest: false));
        // Reactions disabled for a hand drop its weapon (player units only).
        otherSoldier.ReactionsDisabledForRightHand = true;
        Assert.Equal(grenade, otherBattle.MainHandWeapon(otherSet, otherSoldier, reactions: true));
        Assert.Equal(otherRight, otherBattle.MainHandWeapon(otherSet, otherSoldier, reactions: false));
    }

    // ---- the primitives ----

    private static BattleUnit Bare(int x = 5, int y = 5) => new(1, BattleFaction.Player, BattleController.Human(0)) { Position = new BattlePosition(x, y, 0) };

    [Fact]
    public void DirectionToDividesTheCompassIntoEighthsCentredOnTheDirections()
    {
        var unit = Bare();
        // The eight neighbours: north is y - 1, east is x + 1.
        var neighbours = new[] { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) };
        for (var direction = 0; direction < 8; direction++)
            Assert.Equal(direction, unit.DirectionTo(new BattlePosition(5 + neighbours[direction].Item1 * 3, 5 + neighbours[direction].Item2 * 3, 4)));
        // Off the axis: the pie edge lies at 22.5 degrees. 2 east and 5 north is within north; 3 east and 5 north is north-east.
        Assert.Equal(0, unit.DirectionTo(new BattlePosition(7, 0, 0)));
        Assert.Equal(1, unit.DirectionTo(new BattlePosition(8, 0, 0)));
        Assert.Equal(7, unit.DirectionTo(new BattlePosition(2, 0, 0)));
        Assert.Equal(6, unit.DirectionTo(new BattlePosition(0, 3, 0)));
        Assert.Equal(5, unit.DirectionTo(new BattlePosition(0, 9, 0)));
        Assert.Equal(4, unit.DirectionTo(new BattlePosition(6, 9, 0)));
        Assert.Equal(4, unit.DirectionTo(new BattlePosition(4, 9, 0)));
        // The unit's own tile: atan2(0, -0) is a half turn, so south (the reference's quirk, which no turn command reaches).
        Assert.Equal(4, unit.DirectionTo(new BattlePosition(5, 5, 0)));
    }

    [Fact]
    public void LookAtTurnsTheShortestWayOneEighthAtATimeAndAHalfTurnGoesClockwise()
    {
        for (var from = 0; from < 8; from++)
            for (var to = 0; to < 8; to++)
            {
                var unit = Bare();
                unit.SetDirection(from);
                unit.LookAt(to, force: false);
                Assert.Equal(from == to ? BattleUnitStatus.Standing : BattleUnitStatus.Turning, unit.Status);
                var forward = (to - from + 8) % 8;
                var expectedSteps = forward <= 4 ? forward : 8 - forward;
                var clockwise = forward <= 4;
                var steps = 0;
                var direction = from;
                while (unit.Status == BattleUnitStatus.Turning)
                {
                    unit.Turn(turret: false);
                    steps++;
                    direction = (direction + (clockwise ? 1 : 7)) % 8;
                    Assert.Equal(direction, unit.Direction);
                }
                Assert.Equal((expectedSteps, to, BattleUnitStatus.Standing), (steps, unit.Direction, unit.Status));
                // Without a turret the turret stays where it was.
                Assert.Equal(from, unit.DirectionTurret);
            }
    }

    [Fact]
    public void ATurretUnitTurnsItsTurretWithTheBodyAndTheTurretAloneOnRequest()
    {
        var unit = Bare();
        unit.TurretType = 2;
        unit.SetDirection(7);
        unit.LookAt(1, force: false);
        unit.Turn(turret: false);
        Assert.Equal((0, 0), (unit.Direction, unit.DirectionTurret));
        unit.Turn(turret: false);
        Assert.Equal((1, 1, BattleUnitStatus.Standing), (unit.Direction, unit.DirectionTurret, unit.Status));
        // The turret alone, towards a point: the body keeps its facing.
        unit.LookAt(new BattlePosition(5, 9, 0), turret: true);
        Assert.Equal((4, BattleUnitStatus.Turning), (unit.ToDirectionTurret, unit.Status));
        for (var step = 0; step < 3; step++) unit.Turn(turret: true);
        Assert.Equal((1, 4, BattleUnitStatus.Standing), (unit.Direction, unit.DirectionTurret, unit.Status));
        // Turning when already there stands the unit.
        unit.Status = BattleUnitStatus.Turning;
        unit.Turn(turret: true);
        Assert.Equal(BattleUnitStatus.Standing, unit.Status);
    }

    [Fact]
    public void LookAtIgnoresADirectionOutsideTheCompassUnlessForcedAndAbortTurnStopsTheTurn()
    {
        var unit = Bare();
        unit.SetDirection(2);
        unit.LookAt(9, force: false);
        unit.LookAt(-1, force: false);
        Assert.Equal((2, 2, BattleUnitStatus.Standing), (unit.ToDirection, unit.Direction, unit.Status));
        unit.LookAt(new BattlePosition(5, 9, 0), turret: false);
        Assert.Equal((4, BattleUnitStatus.Turning), (unit.ToDirection, unit.Status));
        unit.AbortTurn();
        Assert.Equal((2, BattleUnitStatus.Standing), (unit.Direction, unit.Status));
        unit.LookAt(6, force: true);
        Assert.Equal((6, 6, BattleUnitStatus.Standing), (unit.ToDirection, unit.Direction, unit.Status));
        // An unconscious unit finishes its turn at once.
        unit.Status = BattleUnitStatus.Unconscious;
        unit.ToDirection = 0;
        unit.Turn(turret: false);
        Assert.Equal((7, BattleUnitStatus.Standing), (unit.Direction, unit.Status));
    }

    [Fact]
    public void TheReactionScoreScalesTheReactionsByTheTimeUnitsLeft()
    {
        var unit = Bare();
        unit.Stats = new BattleUnitStats(60, 0, 0, 0, 50, 0, 0, 0, 0, 0, 0, 0);
        unit.TimeUnits = 30;
        Assert.Equal(25.0, unit.ReactionScore);
        unit.TimeUnits = 0;
        Assert.Equal(0.0, unit.ReactionScore);
        unit.TimeUnits = 45;
        Assert.Equal(37.5, unit.ReactionScore);
        // No time units statistic: the reference divides by zero.
        unit.Stats = new BattleUnitStats(0, 0, 0, 0, 50, 0, 0, 0, 0, 0, 0, 0);
        Assert.True(double.IsInfinity(unit.ReactionScore));
    }

    [Fact]
    public void TheTurnAndKneelCostsAreTheArmorsAndTheFixedOnes()
    {
        var (_, _, soldier, set) = Open();
        Assert.Equal(1, soldier.TurnCost(set));
        Assert.Equal((8, 4), (BattleUnit.KneelUpCost, BattleUnit.KneelDownCost));
        Assert.Equal(4, soldier.KneelChangeCost);
        soldier.Kneel(true);
        Assert.Equal(8, soldier.KneelChangeCost);
        var (_, _, heavy, heavySet) = Open(WithSuit("turnCost: 3"));
        Assert.Equal(3, heavy.TurnCost(heavySet));
    }

    // ---- steps ----

    /// <summary>TESTSET's records 3 and 4 (MAP IDs 5 and 6) are a hole (no floor) and a ladder (a grav lift object).</summary>
    private static void HoleAndLadder(Dictionary<string, byte[]> files) =>
        files["TERRAIN/TESTSET.MCD"] = Mcd(Record((42, 2)), Record((42, 3)), Record((42, 4), (59, 2)), Record((32, 1)), Record((34, 1)));

    internal static int Set(BattleState battle, string name) => battle.Map.DataSets.ToList().FindIndex(set => set.Name == name);

    internal static void Place(BattleState battle, BattlePosition position, BattleTilePart part, int id) =>
        battle.Map.SetPart(battle.Map.TileIndex(position), part, id, Set(battle, "TESTSET"));

    internal static BattlePosition East(BattlePosition position, int distance = 1) => position with { X = position.X + distance };

    [Fact]
    public void AWholeStepMovesTheUnitsTileSetsItsFacingAndGainsMotionPoints()
    {
        var (_, battle, soldier, set) = Open(WithSuit("standHeight: 22"), HoleAndLadder);
        var start = soldier.Position!.Value;
        var target = East(start);
        soldier.Kneel(true);
        soldier.MotionPoints = 10;
        var revision = battle.Revision;
        battle.WalkStep(set, soldier, 2, target);
        Assert.Equal(target, soldier.Position);
        Assert.Equal(start, soldier.LastPosition);
        Assert.Null(battle.Map.UnitAt(battle.Map.TileIndex(start)));
        Assert.Equal(soldier.Id, battle.Map.UnitAt(battle.Map.TileIndex(target)));
        Assert.Equal((2, false, false, BattleUnitStatus.Standing, 0, 14), (soldier.Direction, soldier.Kneeled, soldier.Floating, soldier.Status,
            soldier.VerticalDirection, soldier.MotionPoints));
        Assert.True(battle.Revision > revision);
        battle.Validate(placement: false);
        // A shorter unit gains 3: the soldier rule's own height is not above 16.
        var (_, shortBattle, shortSoldier, shortSet) = Open();
        shortBattle.WalkStep(shortSet, shortSoldier, 4, shortSoldier.Position!.Value with { Y = shortSoldier.Position!.Value.Y + 1 });
        Assert.Equal(3, shortSoldier.MotionPoints);
        // A strafing step finishes facing its face direction.
        soldier.FaceDirection = 6;
        battle.WalkStep(set, soldier, 2, East(target));
        Assert.Equal((6, -1), (soldier.Direction, soldier.FaceDirection));
    }

    [Fact]
    public void AStepOntoATileWithNoFloorTakesAFlyingUnitIntoTheAirAndAStepBackLandsIt()
    {
        var (_, battle, soldier, set) = Open(terrain: HoleAndLadder);
        Assert.Equal(1, soldier.MovementType);
        var start = soldier.Position!.Value;
        var hole = East(start);
        Place(battle, hole, BattleTilePart.Floor, 3);
        Assert.False(battle.Map.HasNoFloor(start));
        Assert.True(battle.Map.HasNoFloor(hole));
        battle.WalkStep(set, soldier, 2, hole);
        Assert.Equal((true, true, BattleUnitStatus.Standing), (soldier.HaveNoFloorBelow, soldier.Floating, soldier.Status));
        // Back onto the floor: the unit lands.
        battle.WalkStep(set, soldier, 6, start);
        Assert.Equal((false, false), (soldier.HaveNoFloorBelow, soldier.Floating));
        // Up a level where there is no floor, and down again: the vertical direction is cleared and the facing is kept.
        var above = start with { Z = start.Z + 1 };
        soldier.Direction = 3;
        battle.WalkStep(set, soldier, BattleUnit.DirectionUp, above);
        Assert.Equal((above, 3, true, true, 0), (soldier.Position!.Value, soldier.Direction, soldier.HaveNoFloorBelow, soldier.Floating, soldier.VerticalDirection));
        battle.WalkStep(set, soldier, BattleUnit.DirectionDown, start);
        Assert.Equal((start, 3, false, false), (soldier.Position!.Value, soldier.Direction, soldier.HaveNoFloorBelow, soldier.Floating));
        battle.Validate(placement: false);
    }

    [Fact]
    public void ALadderHoldsASmallUnitUpAndABigUnitNeedsFloorUnderAnyOfItsTiles()
    {
        var (_, battle, soldier, set) = Open(terrain: HoleAndLadder);
        var start = soldier.Position!.Value;
        var upstairs = start with { Z = start.Z + 1 };
        Assert.True(battle.Map.HasNoFloor(upstairs));
        // A floating tile without a ladder has no floor; with a ladder object the unit stands on it.
        battle.UpdateTileFloorState(soldier);
        Assert.False(soldier.HaveNoFloorBelow);
        battle.SetTile(soldier, upstairs);
        Assert.True(soldier.HaveNoFloorBelow);
        Place(battle, upstairs, BattleTilePart.ObjectPart, 4);
        Assert.True(battle.Map.HasLadder(battle.Map.TileIndex(upstairs)));
        battle.UpdateTileFloorState(soldier);
        Assert.False(soldier.HaveNoFloorBelow);
        // A ladder on a wall counts too.
        Place(battle, upstairs, BattleTilePart.ObjectPart, 0);
        Assert.False(battle.Map.HasLadder(battle.Map.TileIndex(upstairs)));
        Place(battle, upstairs, BattleTilePart.NorthWall, 4);
        Assert.True(battle.Map.HasLadder(battle.Map.TileIndex(upstairs)));
        Place(battle, upstairs, BattleTilePart.NorthWall, 0);
        battle.SetTile(soldier, start);

        // A 2x2 unit: floor under any one of its four tiles holds it.
        var origin = East(start, 4) with { Y = start.Y + 4 };
        var vehicle = new BattleUnit(901, BattleFaction.Player, BattleController.Human(0))
        {
            ArmorId = "VEHICLE_ARMOR",
            ArmorSize = 2,
            UnitType = "VEHICLE",
            MovementType = 1,
            Position = origin,
            Stats = soldier.Stats,
        };
        battle.AddUnit(vehicle);
        battle.UpdateTileFloorState(vehicle);
        Assert.False(vehicle.HaveNoFloorBelow);
        var tiles = BattleMap.Footprint(origin, 2).ToArray();
        foreach (var tile in tiles.Skip(1)) Place(battle, tile, BattleTilePart.Floor, 3);
        battle.UpdateTileFloorState(vehicle);
        Assert.False(vehicle.HaveNoFloorBelow);
        Place(battle, tiles[0], BattleTilePart.Floor, 3);
        battle.UpdateTileFloorState(vehicle);
        Assert.True(vehicle.HaveNoFloorBelow);
        // A unit on no tile has floor.
        var nowhere = new BattleUnit(902, BattleFaction.Player, BattleController.Human(0)) { HaveNoFloorBelow = true };
        battle.UpdateTileFloorState(nowhere);
        Assert.False(nowhere.HaveNoFloorBelow);
    }

    [Fact]
    public void ABigUnitsStepMovesAllFourTilesAndGainsThirtyMotionPoints()
    {
        var (_, battle, soldier, set) = Open();
        var start = soldier.Position!.Value;
        var origin = East(start, 5) with { Y = start.Y + 5 };
        var vehicle = new BattleUnit(901, BattleFaction.Player, BattleController.Human(0))
        {
            ArmorId = "VEHICLE_ARMOR",
            ArmorSize = 2,
            UnitType = "VEHICLE",
            Position = origin,
            Stats = soldier.Stats,
            TurretType = 2,
        };
        battle.AddUnit(vehicle);
        battle.Validate(placement: false);
        Assert.True(vehicle.IsBigUnit);
        var target = East(origin);
        battle.WalkStep(set, vehicle, 2, target);
        Assert.Equal(30, vehicle.MotionPoints);
        foreach (var covered in BattleMap.Footprint(origin, 2))
            Assert.Equal(covered.X == origin.X ? (int?)null : vehicle.Id, battle.Map.UnitAt(battle.Map.TileIndex(covered)));
        foreach (var covered in BattleMap.Footprint(target, 2)) Assert.Equal(vehicle.Id, battle.Map.UnitAt(battle.Map.TileIndex(covered)));
        Assert.Equal(origin, vehicle.LastPosition);
        battle.Validate(placement: false);
        // Stepping onto the tile it is on changes nothing; a tile outside the map or a unit not in the battle is a defect.
        var revision = battle.Revision;
        battle.SetTile(vehicle, target);
        Assert.Equal(revision, battle.Revision);
        Assert.Throws<InvalidOperationException>(() => battle.SetTile(vehicle, new BattlePosition(-1, 0, 0)));
        Assert.Throws<InvalidOperationException>(() => battle.SetTile(Bare(), target));
        Assert.Throws<ArgumentOutOfRangeException>(() => battle.WalkStep(set, vehicle, 10, target));
        // Heights: the armor's own, else the soldier's or the unit rule's.
        Assert.Equal(new RuntimeUnitHeights(0, 0, 0), BattleState.HeightsOf(set, vehicle));
        Assert.Equal(new RuntimeUnitHeights(0, 0, 0), BattleState.HeightsOf(set, soldier));
    }

    private static string TwoGuns(string gunExtra) => Replaced(WithGun(gunExtra), "- type: GRENADE\n    battleType: 4",
        "- type: GUN2\n    battleType: 1\n    clipSize: -1\n    tuSnap: 10\n  - type: GRENADE\n    battleType: 4");

    private static BattleItem Hold(BattleState battle, BattleUnit unit, string type, string slot, bool special = false)
    {
        var item = new BattleItem(battle.TakeItemId(), type) { OwnerUnitId = unit.Id, Slot = slot, IsSpecialWeapon = special };
        battle.AddItem(item);
        return item;
    }

    [Fact]
    public void TheMainHandPickCoversUnloadedSpecialBlasterAndFactionCases()
    {
        var (_, battle, soldier, set) = Open(TwoGuns("tuSnap: 30\n    waypoints: 3"));
        var right = Gun(battle, soldier);
        // Ammunition not required: an unloaded left gun competes (equal snap costs: the quickest is the right, a player's pick the active hand).
        var left = Hold(battle, soldier, "GUN", "STR_LEFT_HAND");
        left.IsWeaponWithAmmo = true;
        Assert.Equal(right, battle.MainHandWeapon(set, soldier, needAmmo: true));
        Assert.Equal(right, battle.MainHandWeapon(set, soldier, quickest: true, needAmmo: false));
        soldier.ActiveHand = "STR_LEFT_HAND";
        Assert.Equal(left, battle.MainHandWeapon(set, soldier, quickest: false, needAmmo: false));
        soldier.ActiveHand = "STR_RIGHT_HAND";
        // A cheaper snap shot in the left hand wins for the quickest; a player's non-quickest pick is the active hand.
        left.Slot = "";
        var quick = Hold(battle, soldier, "GUN2", "STR_LEFT_HAND");
        Assert.Equal(quick, battle.MainHandWeapon(set, soldier, quickest: true));
        Assert.Equal(right, battle.MainHandWeapon(set, soldier, quickest: false));
        soldier.ActiveHand = "STR_LEFT_HAND";
        Assert.Equal(quick, battle.MainHandWeapon(set, soldier, quickest: false));
        soldier.ActiveHand = "STR_RIGHT_HAND";

        // A hostile unit: not the active hand but the right when the left is cheaper, the left when it is not; a blaster first.
        var alien = battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);
        var alienRight = Hold(battle, alien, "GUN2", "STR_RIGHT_HAND");
        var alienLeft = Hold(battle, alien, "GUN", "STR_LEFT_HAND");
        Assert.Equal(alienLeft, battle.MainHandWeapon(set, alien, quickest: false));
        Assert.Equal(alienRight, battle.MainHandWeapon(set, alien, quickest: true));
        // GUN has waypoints in this rule: the blaster comes first, whichever hand and whatever the cost.
        Assert.Equal(alienLeft, battle.MainHandWeapon(set, alien, quickest: false));
        alienRight.Slot = "";
        alienLeft.Slot = "";
        var gunRight = Hold(battle, alien, "GUN", "STR_RIGHT_HAND");
        var cheapLeft = Hold(battle, alien, "GUN2", "STR_LEFT_HAND");
        Assert.Equal(gunRight, battle.MainHandWeapon(set, alien, quickest: false));
        gunRight.Slot = "";
        cheapLeft.Slot = "";
        // Two blasters at equal cost: the cost rule would pick the left for a hostile unit, the blaster priority takes the right first (the blaster
        // priority is the only thing that separates the two here).
        var blasterRight = Hold(battle, alien, "GUN", "STR_RIGHT_HAND");
        var blasterLeft = Hold(battle, alien, "GUN", "STR_LEFT_HAND");
        Assert.Equal(blasterRight, battle.MainHandWeapon(set, alien, quickest: false));
        blasterRight.Slot = "";
        blasterLeft.Slot = "";
        // Without waypoints the cost decides: the left is cheaper, so a hostile unit takes the right.
        var (_, plainBattle, _, plainSet) = Open(TwoGuns("tuSnap: 30"));
        var plainAlien = plainBattle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);
        var plainRight = Hold(plainBattle, plainAlien, "GUN", "STR_RIGHT_HAND");
        Hold(plainBattle, plainAlien, "GUN2", "STR_LEFT_HAND");
        Assert.Equal(plainRight, plainBattle.MainHandWeapon(plainSet, plainAlien, quickest: false));
        // Empty hands: the AI may use a special firearm.
        var (_, specialBattle, specialSoldier, specialSet) = Open();
        Gun(specialBattle, specialSoldier).Slot = "STR_BELT";
        Assert.Null(specialBattle.MainHandWeapon(specialSet, specialSoldier));
        var special = Hold(specialBattle, specialSoldier, "GUN", "", special: true);
        specialSoldier.SpecialWeaponIds.Add(special.Id);
        Assert.Equal(special, specialBattle.MainHandWeapon(specialSet, specialSoldier));
    }

    [Fact]
    public void AmmoChecksCoverMeleeAndASpentClip()
    {
        var (_, battle, soldier, set) = Open();
        var gun = Gun(battle, soldier);
        Assert.True(battle.HaveAnyAmmo(set, gun));
        var clip = battle.Items.Single(item => item.Id == gun.AmmoSlots[0]);
        clip.AmmoQuantity = 0;
        Assert.False(battle.HaveAnyAmmo(set, gun));
        clip.AmmoQuantity = 1;
        Assert.True(battle.HaveAnyAmmo(set, gun));
        // A melee weapon needs its hit loaded; one that takes no ammunition always is.
        var ram = new BattleItem(battle.TakeItemId(), "RAM") { IsWeaponWithAmmo = true };
        battle.AddItem(ram);
        ram.AmmoSlots[0] = ram.Id;
        ram.AmmoQuantity = 1;
        Assert.True(battle.HaveAnyAmmo(set, ram));
        ram.AmmoQuantity = 0;
        Assert.False(battle.HaveAnyAmmo(set, ram));
        // A weapon that is no weapon with ammunition (a grenade) counts as loaded.
        Assert.True(battle.HaveAnyAmmo(set, new BattleItem(900, "GRENADE")));
    }

    [Fact]
    public void AnUnconsciousUnitFloatsOnlyWhenItFliesAndHasNoFloor()
    {
        var (_, battle, soldier, _) = Open(terrain: HoleAndLadder);
        var start = soldier.Position!.Value;
        var hole = East(start);
        Place(battle, hole, BattleTilePart.Floor, 3);
        soldier.Status = BattleUnitStatus.Unconscious;
        battle.SetTile(soldier, hole);
        Assert.True(soldier.Floating);
        battle.SetTile(soldier, start);
        Assert.False(soldier.Floating);
        soldier.MovementType = 0;
        battle.SetTile(soldier, hole);
        Assert.False(soldier.Floating);
    }

    [Fact]
    public void GeneratedAndLoadedUnitsFaceTheSameWayTheyTurnTo()
    {
        var (_, battle, soldier, _) = Open();
        Assert.All(battle.Units, unit => Assert.Equal((unit.Direction, unit.Direction), (unit.DirectionTurret, unit.ToDirection)));
        Assert.All(battle.Units, unit => Assert.Equal(unit.DirectionTurret, unit.ToDirectionTurret));
        Assert.NotNull(soldier);
    }

    [Fact]
    public void ARestoredUnitTurnsToTheDirectionsItWasSavedFacing()
    {
        // BattleUnit::load sets _toDirection and _toDirectionTurret from the loaded directions: a unit whose turret faces another way than its body
        // does not start a turn when it is next moved.
        var (campaign, _, soldier, _) = Open();
        soldier.Direction = 2;
        soldier.DirectionTurret = 5;
        var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new SplitMix64RandomSource(3), TerrainFiles());
        var loaded = Battle(restored).Units.Single(unit => unit.Id == soldier.Id);
        Assert.Equal((2, 5, 2, 5), (loaded.Direction, loaded.DirectionTurret, loaded.ToDirection, loaded.ToDirectionTurret));
        Assert.All(Battle(restored).Units, unit => Assert.Equal((unit.Direction, unit.DirectionTurret), (unit.ToDirection, unit.ToDirectionTurret)));
    }
}
