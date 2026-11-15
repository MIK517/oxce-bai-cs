using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;
using Xunit;
using static Oxce.CompatibilityTests.BattleStartTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S8 (the third part): the inventory's ammunition and fuse commands. <c>Inventory::mouseClick</c>'s "put item in weapon" (a load and the Shift quick
/// swap), <c>Inventory::unload</c> (the button and the Shift quick unload), and the pre-battle right click's priming (<c>src/Battlescape/Inventory.cpp</c>). The suited soldier of
/// <see cref="BattleInventoryTests"/> holds a GUN in its right hand, loaded with the CLIP 2; its hands cost 1 to each other and 5 to the ground, the back pack 8 to the right
/// hand, 2 to the left and 3 to the ground, and a load costs 15 (an unload 8) time units.
/// </summary>
public sealed class BattleInventoryAmmoTests
{
    private const int Gun = 0;
    private const int Grenade = 1;
    private const int Clip = 2;

    private static readonly string Costs = BattleInventoryTests.WithCosts(Rules);

    private const string GrenadeText = "  - type: GRENADE\n    battleType: 4\n    weight: 1";
    private const string FuseKeys = "\n    costPrime: {time: 10}\n    costUnprime: {time: 5}\n    unprimeActionName: STR_UNPRIME_GRENADE";

    private static string WithGrenade(string keys)
    {
        var rules = Costs.Replace(GrenadeText, GrenadeText + (keys.Length == 0 ? "" : "\n    " + keys) + FuseKeys, StringComparison.Ordinal);
        Assert.NotEqual(Costs, rules);
        return rules;
    }

    /// <summary>The BOMB is a grenade (battle type 4) the GUN takes as ammunition, so a live one dropped to the ground would set the danger zone.</summary>
    private static readonly string BombRules = Costs
        .Replace(GrenadeText, GrenadeText + "\n  - type: BOMB\n    battleType: 4\n    weight: 1\n    damageType: 3\n    power: 40", StringComparison.Ordinal)
        .Replace("compatibleAmmo: [CLIP]", "compatibleAmmo: [CLIP, BOMB]", StringComparison.Ordinal);

    /// <summary>An extra weapon that is fixed (the reference never picks it up).</summary>
    private static readonly string FixedGunRules = Costs.Replace(GrenadeText, GrenadeText + "\n  - type: FIXED_GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1\n    fixedWeapon: true", StringComparison.Ordinal);

    static BattleInventoryAmmoTests()
    {
        Assert.NotEqual(Costs, BombRules);
        Assert.NotEqual(Costs, FixedGunRules);
    }

    private static (CampaignState Campaign, BattleState Battle, BattleUnit Soldier) Open(string? rules = null, bool firstTurn = false)
    {
        var (campaign, _) = Requested(rules ?? Costs, "UFO_SUITED");
        Started(campaign);
        Assert.IsType<BriefingOpened>(Assert.Single(campaign.Execute(new BeginBriefing("T", "C")).Events));
        Assert.IsType<BriefingConfirmed>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));
        var battle = Battle(campaign);
        var soldier = battle.Units.Single(static unit => unit.Faction == BattleFaction.Player);
        if (firstTurn) Assert.IsType<BattleFirstTurnStarted>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
        return (campaign, battle, soldier);
    }

    private static BattleItem Item(BattleState battle, int id) => battle.Items.Single(item => item.Id == id);

    private static BattleItem Carry(BattleState battle, BattleUnit soldier, string type, string slot)
    {
        var item = new BattleItem(battle.TakeItemId(), type) { OwnerUnitId = soldier.Id, Slot = slot, SlotX = 3 };
        if (type == "CLIP") item.AmmoQuantity = 12;
        battle.AddItem(item);
        return item;
    }

    /// <summary>The gun with no clip (the clip is gone), and a fresh clip in the given section.</summary>
    private static BattleItem EmptyGun(BattleState battle)
    {
        var gun = Item(battle, Gun);
        battle.RemoveItem(Item(battle, Clip));
        gun.AmmoSlots[0] = null;
        return gun;
    }

    private static IReadOnlyList<ICampaignEvent> Run(CampaignState campaign, ICampaignCommand command)
    {
        var events = campaign.Execute(command).Events;
        Assert.Equal("", events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Code ?? "");
        return events;
    }

    /// <summary>The gun's clip replaced by a live BOMB (a grenade as ammunition) in the gun.</summary>
    private static BattleItem LiveBomb(BattleState battle, BattleUnit soldier)
    {
        var gun = EmptyGun(battle);
        var bomb = Carry(battle, soldier, "BOMB", "STR_BELT");
        bomb.Slot = "";
        bomb.OwnerUnitId = null;
        bomb.IsAmmo = true;
        bomb.FuseEnabled = true;
        gun.AmmoSlots[0] = bomb.Id;
        return bomb;
    }

    /// <summary>The item lies on the unit's tile.</summary>
    private static BattleItem OnTheGround(BattleState battle, BattleUnit soldier, BattleItem item)
    {
        item.OwnerUnitId = null;
        item.Slot = "STR_GROUND";
        item.Tile = BattleState.TileOf(soldier);
        return item;
    }

    private static string Refusal(CampaignState campaign, ICampaignCommand command) =>
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(command).Events)).Code;

    private static (string Slot, int? Owner, bool IsAmmo) State(BattleState battle, int id)
    {
        var item = Item(battle, id);
        return (item.Slot, item.OwnerUnitId, item.IsAmmo);
    }

    // ---- loading ----

    [Fact]
    public void BeforeTheBattleADropOnAnEmptyWeaponLoadsItForNothing()
    {
        var (campaign, battle, soldier) = Open();
        var gun = EmptyGun(battle);
        var fresh = Carry(battle, soldier, "CLIP", "STR_LEFT_HAND");
        Assert.Equal(new InventoryAmmoLoaded(soldier.Id, Gun, fresh.Id, 0, -1, "", 0),
            Assert.Single(Run(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_RIGHT_HAND", 0, 0))));
        Assert.Equal(fresh.Id, gun.AmmoSlots[0]);
        Assert.Equal(("", (int?)null, true), State(battle, fresh.Id));
        Assert.Equal(60, soldier.TimeUnits);
    }

    [Fact]
    public void ALoadedWeaponRefusesAPlainDropAndTheOtherRefusalsChangeNothing()
    {
        var (campaign, battle, soldier) = Open();
        var fresh = Carry(battle, soldier, "CLIP", "STR_LEFT_HAND");
        var revision = battle.Revision;
        Assert.Equal("battle.inventory-ammo-already-loaded", Refusal(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_RIGHT_HAND", 0, 0)));
        Assert.Equal("battle.inventory-ammo-wrong", Refusal(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_RIGHT_HAND", 0, 0)));
        Assert.Equal(revision, battle.Revision);
        Assert.Equal(Clip, Item(battle, Gun).AmmoSlots[0]);
        Assert.Equal("STR_LEFT_HAND", fresh.Slot);
    }

    [Fact]
    public void BeforeTheBattleAShiftDropSwapsTheClipsAndTheOldOneAlwaysGoesToTheGround()
    {
        var (campaign, battle, soldier) = Open();
        var fresh = Carry(battle, soldier, "CLIP", "STR_BACK_PACK");
        var events = Run(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_RIGHT_HAND", 0, 0, Shift: true));
        Assert.Equal(new InventoryAmmoLoaded(soldier.Id, Gun, fresh.Id, 0, Clip, "STR_GROUND", 0), Assert.Single(events));
        Assert.Equal(fresh.Id, Item(battle, Gun).AmmoSlots[0]);
        Assert.Equal(("STR_GROUND", (int?)null, false), State(battle, Clip));
        Assert.Equal(BattleState.TileOf(soldier), Item(battle, Clip).Tile);
        Assert.Equal(60, soldier.TimeUnits);
    }

    [Fact]
    public void InTheBattleALoadCostsTheWeaponsLoadTimeAndTheExtendedReloadCostAddsTheMoveToTheHand()
    {
        var (campaign, battle, soldier) = Open(firstTurn: true);
        EmptyGun(battle);
        var fresh = Carry(battle, soldier, "CLIP", "STR_LEFT_HAND");
        Assert.Equal(15, Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_RIGHT_HAND", 0, 0)))).TimeUnitsSpent);
        Assert.Equal(45, soldier.TimeUnits);

        // The extended cost: a clip in the back pack first moves to the right hand (8), one already in a hand does not.
        var (extended, extendedBattle, extendedSoldier) = Open(Costs + "\nconstants:\n  - extendedItemReloadCost: true\n", firstTurn: true);
        EmptyGun(extendedBattle);
        var pack = Carry(extendedBattle, extendedSoldier, "CLIP", "STR_BACK_PACK");
        Assert.Equal(23, Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(extended, new MoveInventoryItem(extendedSoldier.Id, pack.Id, "STR_RIGHT_HAND", 0, 0)))).TimeUnitsSpent);
        Assert.Equal(37, extendedSoldier.TimeUnits);
    }

    [Fact]
    public void InTheBattleAShiftSwapPaysTheUnloadAndTheOldClipGoesToTheHandTheNewOneLeftElseTheGround()
    {
        var (campaign, battle, soldier) = Open(firstTurn: true);
        var fresh = Carry(battle, soldier, "CLIP", "STR_BACK_PACK");
        // The left hand is free: the old clip goes there (load 15 + unload 8).
        var loaded = Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_RIGHT_HAND", 0, 0, Shift: true))));
        Assert.Equal((Clip, "STR_LEFT_HAND", 23), (loaded.ReplacedItemId, loaded.ReplacedTo, loaded.TimeUnitsSpent));
        Assert.Equal(("STR_LEFT_HAND", (int?)soldier.Id, false), State(battle, Clip));
        Assert.Equal(37, soldier.TimeUnits);

        // Both hands taken: the old clip drops to the ground, which costs the drop from the right hand (5) as well.
        var (second, secondBattle, secondSoldier) = Open(firstTurn: true);
        Carry(secondBattle, secondSoldier, "GRENADE", "STR_LEFT_HAND");
        var other = Carry(secondBattle, secondSoldier, "CLIP", "STR_BACK_PACK");
        var dropped = Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(second, new MoveInventoryItem(secondSoldier.Id, other.Id, "STR_RIGHT_HAND", 0, 0, Shift: true))));
        Assert.Equal((Clip, "STR_GROUND", 28), (dropped.ReplacedItemId, dropped.ReplacedTo, dropped.TimeUnitsSpent));
        Assert.Equal(BattleState.TileOf(secondSoldier), Item(secondBattle, Clip).Tile);
        Assert.Equal(32, secondSoldier.TimeUnits);
    }

    [Fact]
    public void InTheBattleTheSwapNeedsAWeaponInAHandAndAnUnloadTimeAndTheLoadNeedsTheTimeUnits()
    {
        var (campaign, battle, soldier) = Open(firstTurn: true);
        var fresh = Carry(battle, soldier, "CLIP", "STR_LEFT_HAND");
        soldier.TimeUnits = 22;
        var revision = battle.Revision;
        Assert.Equal("battle.inventory-not-enough-time-units", Refusal(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_RIGHT_HAND", 0, 0, Shift: true)));
        Assert.Equal(revision, battle.Revision);
        Assert.Equal(Clip, Item(battle, Gun).AmmoSlots[0]);

        // A weapon in the back pack takes no swap (and no plain drop).
        var (held, heldBattle, heldSoldier) = Open(firstTurn: true);
        var pack = Item(heldBattle, Gun);
        pack.Slot = "STR_BACK_PACK";
        pack.SlotX = 3;
        var spare = Carry(heldBattle, heldSoldier, "CLIP", "STR_LEFT_HAND");
        Assert.Equal("battle.inventory-ammo-swap-not-held", Refusal(held, new MoveInventoryItem(heldSoldier.Id, spare.Id, "STR_BACK_PACK", 3, 0, Shift: true)));
        Assert.Equal("battle.inventory-ammo-already-loaded", Refusal(held, new MoveInventoryItem(heldSoldier.Id, spare.Id, "STR_BACK_PACK", 3, 0)));

        // A weapon with no unload time cannot be swapped in the battle (the reference then offers only the plain refusal).
        var rules = Costs.Replace("  - type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1",
            "  - type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1\n    tuUnload: 0", StringComparison.Ordinal);
        Assert.NotEqual(Costs, rules);
        var (free, freeBattle, freeSoldier) = Open(rules, firstTurn: true);
        var clip = Carry(freeBattle, freeSoldier, "CLIP", "STR_LEFT_HAND");
        Assert.Equal("battle.inventory-ammo-already-loaded", Refusal(free, new MoveInventoryItem(freeSoldier.Id, clip.Id, "STR_RIGHT_HAND", 0, 0, Shift: true)));
    }

    // ---- unloading ----

    [Fact]
    public void BeforeTheBattleTheButtonUnloadsToTheSecondFreeHandElseTheGroundAndCostsNothing()
    {
        var (campaign, battle, soldier) = Open();
        Assert.Equal(new InventoryItemUnloaded(soldier.Id, Gun, Clip, "STR_RIGHT_HAND", "STR_LEFT_HAND", 0),
            Assert.Single(Run(campaign, new UseInventoryFeature(soldier.Id, Gun, InventoryFeature.UnloadAmmo))));
        Assert.Equal(("STR_LEFT_HAND", (int?)soldier.Id, false), State(battle, Clip));
        Assert.Null(Item(battle, Gun).AmmoSlots[0]);
        Assert.Equal(60, soldier.TimeUnits);

        var (second, secondBattle, secondSoldier) = Open();
        Carry(secondBattle, secondSoldier, "GRENADE", "STR_LEFT_HAND");
        Assert.Equal(new InventoryItemUnloaded(secondSoldier.Id, Gun, Clip, "STR_RIGHT_HAND", "STR_GROUND", 0),
            Assert.Single(Run(second, new UseInventoryFeature(secondSoldier.Id, Gun, InventoryFeature.UnloadAmmo))));
        Assert.Equal(BattleState.TileOf(secondSoldier), Item(secondBattle, Clip).Tile);
    }

    [Fact]
    public void AWeaponOutsideTheHandsMovesToTheFirstFreeHandWhenUnloadedAndNeedsOneToBeFree()
    {
        var (campaign, battle, soldier) = Open();
        var gun = Item(battle, Gun);
        gun.Slot = "STR_BACK_PACK";
        gun.SlotX = 3;
        Assert.Equal(new InventoryItemUnloaded(soldier.Id, Gun, Clip, "STR_RIGHT_HAND", "STR_LEFT_HAND", 0),
            Assert.Single(Run(campaign, new UseInventoryFeature(soldier.Id, Gun, InventoryFeature.UnloadAmmo))));
        Assert.Equal("STR_RIGHT_HAND", gun.Slot);

        var (full, fullBattle, fullSoldier) = Open();
        var held = Item(fullBattle, Gun);
        held.Slot = "STR_BACK_PACK";
        held.SlotX = 3;
        Carry(fullBattle, fullSoldier, "GRENADE", "STR_RIGHT_HAND");
        Carry(fullBattle, fullSoldier, "GRENADE", "STR_LEFT_HAND");
        var revision = fullBattle.Revision;
        Assert.Equal("battle.inventory-hands-full", Refusal(full, new UseInventoryFeature(fullSoldier.Id, Gun, InventoryFeature.UnloadAmmo)));
        Assert.Equal(revision, fullBattle.Revision);
    }

    [Fact]
    public void BeforeTheBattleAShiftClickIsTheQuickUnloadThatLeavesTheWeaponAndDropsTheClip()
    {
        var (campaign, battle, soldier) = Open();
        var gun = Item(battle, Gun);
        gun.Slot = "STR_BACK_PACK";
        gun.SlotX = 3;
        Assert.Equal(new InventoryItemUnloaded(soldier.Id, Gun, Clip, "STR_BACK_PACK", "STR_GROUND", 0),
            Assert.Single(Run(campaign, new UseInventoryFeature(soldier.Id, Gun, InventoryFeature.UnloadAmmo, Shift: true))));
        Assert.Equal("STR_BACK_PACK", gun.Slot);
        Assert.Equal(("STR_GROUND", (int?)null, false), State(battle, Clip));
        // Even with both hands taken: it needs no free hand.
        var (full, fullBattle, fullSoldier) = Open();
        Carry(fullBattle, fullSoldier, "GRENADE", "STR_LEFT_HAND");
        Assert.IsType<InventoryItemUnloaded>(Assert.Single(Run(full, new UseInventoryFeature(fullSoldier.Id, Gun, InventoryFeature.UnloadAmmo, Shift: true))));
    }

    [Fact]
    public void InTheBattleAnUnloadCostsTheUnloadTheDropAndTheMoveToTheHand()
    {
        var (campaign, battle, soldier) = Open(firstTurn: true);
        // The left hand is free: 8.
        Assert.Equal(8, Assert.IsType<InventoryItemUnloaded>(Assert.Single(Run(campaign, new UseInventoryFeature(soldier.Id, Gun, InventoryFeature.UnloadAmmo)))).TimeUnitsSpent);
        Assert.Equal(52, soldier.TimeUnits);

        // The left hand taken: the clip drops to the ground (8 + 5).
        var (second, secondBattle, secondSoldier) = Open(firstTurn: true);
        Carry(secondBattle, secondSoldier, "GRENADE", "STR_LEFT_HAND");
        Assert.Equal(13, Assert.IsType<InventoryItemUnloaded>(Assert.Single(Run(second, new UseInventoryFeature(secondSoldier.Id, Gun, InventoryFeature.UnloadAmmo)))).TimeUnitsSpent);

        // A weapon in the back pack first moves to the right hand (8 + 8, the clip to the left hand).
        var (third, thirdBattle, thirdSoldier) = Open(firstTurn: true);
        var gun = Item(thirdBattle, Gun);
        gun.Slot = "STR_BACK_PACK";
        gun.SlotX = 3;
        Assert.Equal(16, Assert.IsType<InventoryItemUnloaded>(Assert.Single(Run(third, new UseInventoryFeature(thirdSoldier.Id, Gun, InventoryFeature.UnloadAmmo)))).TimeUnitsSpent);
        Assert.Equal(("STR_RIGHT_HAND", 44), (gun.Slot, thirdSoldier.TimeUnits));

        // Too few time units: refused, nothing changes. A Shift click needs the weapon in a hand.
        var (poor, poorBattle, poorSoldier) = Open(firstTurn: true);
        poorSoldier.TimeUnits = 7;
        var revision = poorBattle.Revision;
        Assert.Equal("battle.attack-refused", Refusal(poor, new UseInventoryFeature(poorSoldier.Id, Gun, InventoryFeature.UnloadAmmo)));
        Assert.Equal(revision, poorBattle.Revision);
        var (pack, packBattle, packSoldier) = Open(firstTurn: true);
        var stowed = Item(packBattle, Gun);
        stowed.Slot = "STR_BACK_PACK";
        stowed.SlotX = 3;
        Assert.Equal("battle.inventory-unload-not-held", Refusal(pack, new UseInventoryFeature(packSoldier.Id, Gun, InventoryFeature.UnloadAmmo, Shift: true)));
        // A Shift click on a weapon in a hand is the ordinary unload in the battle.
        var (hand, handBattle, handSoldier) = Open(firstTurn: true);
        Assert.Equal(8, Assert.IsType<InventoryItemUnloaded>(Assert.Single(Run(hand, new UseInventoryFeature(handSoldier.Id, Gun, InventoryFeature.UnloadAmmo, Shift: true)))).TimeUnitsSpent);
        Assert.Equal(("STR_LEFT_HAND", (int?)handSoldier.Id, false), State(handBattle, Clip));
    }

    [Fact]
    public void AnUnloadNeedsAWeaponWithAmmunitionOrAPrimedGrenade()
    {
        var (campaign, battle, soldier) = Open();
        var supply = battle.Items.First(item => item.OwnerUnitId == soldier.Id && item.ItemType != "GUN" && item.ItemType != "GRENADE");
        Assert.Equal("battle.inventory-nothing-to-unload", Refusal(campaign, new UseInventoryFeature(soldier.Id, supply.Id, InventoryFeature.UnloadAmmo)));
        Assert.Equal("battle.inventory-nothing-to-unload", Refusal(campaign, new UseInventoryFeature(soldier.Id, Grenade, InventoryFeature.UnloadAmmo)));
        Assert.Equal("battle.inventory-item-unknown", Refusal(campaign, new UseInventoryFeature(soldier.Id, 999, InventoryFeature.UnloadAmmo)));
        EmptyGun(battle);
        Assert.Equal("battle.inventory-no-ammunition-loaded", Refusal(campaign, new UseInventoryFeature(soldier.Id, Gun, InventoryFeature.UnloadAmmo)));
    }

    [Fact]
    public void AWeaponWithNoUnloadTimeIsSkippedBeforeTheBattleAndAnActionWithNoCostDoesNothingInIt()
    {
        var rules = Costs.Replace("  - type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1",
            "  - type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1\n    tuUnload: 0", StringComparison.Ordinal);
        Assert.NotEqual(Costs, rules);
        // Before the battle a zero unload time is skipped (the slot is not unloadable); in the battle the unload is possible, and costs what is left (the drop, 5).
        var (before, _, beforeSoldier) = Open(rules);
        Assert.Equal("battle.inventory-nothing-to-unload", Refusal(before, new UseInventoryFeature(beforeSoldier.Id, Gun, InventoryFeature.UnloadAmmo)));
        var (campaign, battle, soldier) = Open(rules, firstTurn: true);
        Carry(battle, soldier, "GRENADE", "STR_LEFT_HAND");
        Assert.Equal(5, Assert.IsType<InventoryItemUnloaded>(Assert.Single(Run(campaign, new UseInventoryFeature(soldier.Id, Gun, InventoryFeature.UnloadAmmo)))).TimeUnitsSpent);
        // With the left hand free the whole unload costs nothing, and BattleActionCost::haveTU fails without a message for an action with no time.
        var (free, _, freeSoldier) = Open(rules, firstTurn: true);
        Assert.Equal("battle.inventory-nothing-to-unload", Refusal(free, new UseInventoryFeature(freeSoldier.Id, Gun, InventoryFeature.UnloadAmmo)));
    }

    // ---- fuses ----

    [Fact]
    public void AGrenadeIsUnprimedByTheUnloadWhereItLiesBeforeTheBattleAndInAHandInIt()
    {
        var rules = WithGrenade("fuseType: 2");
        var (campaign, battle, soldier) = Open(rules);
        var grenade = Item(battle, Grenade);
        grenade.SetFuseTimer(2, battle.RequiredRules.Battle.Equipment.Items["GRENADE"]);
        var events = Run(campaign, new UseInventoryFeature(soldier.Id, Grenade, InventoryFeature.UnloadAmmo, Shift: true));
        Assert.Equal([new InventoryItemUnloaded(soldier.Id, Grenade, -1, "STR_BACK_PACK", "", 0), new ItemUnprimed(soldier.Id, Grenade)], events);
        Assert.Equal(-1, grenade.FuseTimer);

        var (second, secondBattle, secondSoldier) = Open(rules, firstTurn: true);
        var primed = Item(secondBattle, Grenade);
        primed.SetFuseTimer(2, secondBattle.RequiredRules.Battle.Equipment.Items["GRENADE"]);
        // The gun holds the right hand: the grenade goes to the left (the unprime's cost, 5% of 60 is 3, and the move from the back pack, 2).
        var moved = Run(second, new UseInventoryFeature(secondSoldier.Id, Grenade, InventoryFeature.UnloadAmmo));
        Assert.Equal([new InventoryItemUnloaded(secondSoldier.Id, Grenade, -1, "STR_LEFT_HAND", "", 5), new ItemUnprimed(secondSoldier.Id, Grenade)], moved);
        Assert.Equal(("STR_LEFT_HAND", -1, 55), (primed.Slot, primed.FuseTimer, secondSoldier.TimeUnits));
    }

    [Fact]
    public void ThePreBattleRightClickPrimesAFixedFuseAndUnprimesItAgain()
    {
        var (campaign, battle, soldier) = Open(WithGrenade("fuseType: 2"));
        Assert.Equal(new ItemPrimed(soldier.Id, Grenade, 2), Assert.Single(Run(campaign, new UseInventoryFeature(soldier.Id, Grenade, InventoryFeature.Prime, Turns: 9))));
        Assert.Equal(2, Item(battle, Grenade).FuseTimer);
        Assert.Equal(new ItemUnprimed(soldier.Id, Grenade), Assert.Single(Run(campaign, new UseInventoryFeature(soldier.Id, Grenade, InventoryFeature.Prime))));
        Assert.Equal(-1, Item(battle, Grenade).FuseTimer);
        Assert.Equal(60, soldier.TimeUnits);
    }

    [Fact]
    public void ASettableFuseIsPrimedForTheChosenTurnsAndTheRefusalsChangeNothing()
    {
        var (campaign, battle, soldier) = Open(WithGrenade("fuseType: -1"));
        var revision = battle.Revision;
        Assert.Equal("battle.prime-turns-invalid", Refusal(campaign, new UseInventoryFeature(soldier.Id, Grenade, InventoryFeature.Prime, Turns: 24)));
        Assert.Equal("battle.prime-turns-invalid", Refusal(campaign, new UseInventoryFeature(soldier.Id, Grenade, InventoryFeature.Prime, Turns: -1)));
        Assert.Equal("battle.inventory-prime-not-offered", Refusal(campaign, new UseInventoryFeature(soldier.Id, Gun, InventoryFeature.Prime)));
        Assert.Equal("battle.inventory-item-unknown", Refusal(campaign, new UseInventoryFeature(soldier.Id, Clip, InventoryFeature.Prime)));
        Assert.Equal(revision, battle.Revision);
        Assert.Equal(new ItemPrimed(soldier.Id, Grenade, 5), Assert.Single(Run(campaign, new UseInventoryFeature(soldier.Id, Grenade, InventoryFeature.Prime, Turns: 5))));
        Assert.Equal(5, Item(battle, Grenade).FuseTimer);
    }

    [Fact]
    public void AGrenadeWithNoPrimeCostIsNotPrimedAndTheBattlesInventoryDoesNotPrime()
    {
        var noCost = WithGrenade("fuseType: 2").Replace("costPrime: {time: 10}", "costPrime: {time: 0}", StringComparison.Ordinal);
        var (campaign, _, soldier) = Open(noCost);
        Assert.Equal("battle.inventory-prime-not-offered", Refusal(campaign, new UseInventoryFeature(soldier.Id, Grenade, InventoryFeature.Prime)));
        var (running, _, runner) = Open(WithGrenade("fuseType: 2"), firstTurn: true);
        Assert.Equal("battle.inventory-prime-in-battle", Refusal(running, new UseInventoryFeature(runner.Id, Grenade, InventoryFeature.Prime)));
    }

    [Fact]
    public void AnItemWithAFuseTypeOutOfRangeIsNotPrimedAndAnUnprimeNeedsAFuseTypeAndACost()
    {
        // RuleItem::getFuseTimerDefault is -1 for a fuse type of 64: the reference leaves the item unprimed, the port refuses by name.
        var (campaign, battle, soldier) = Open(WithGrenade("fuseType: 64"));
        var revision = battle.Revision;
        Assert.Equal("battle.prime-fuse-type-unknown", Refusal(campaign, new UseInventoryFeature(soldier.Id, Grenade, InventoryFeature.Prime)));
        Assert.Equal((revision, -1), (battle.Revision, Item(battle, Grenade).FuseTimer));

        // A primed grenade of a rule with no fuse type is not unprimed by the unload.
        var (noType, noTypeBattle, noTypeSoldier) = Open(WithGrenade("fuseType: -3"));
        Item(noTypeBattle, Grenade).SetFuseTimer(2, noTypeBattle.RequiredRules.Battle.Equipment.Items["GRENADE"]);
        Assert.Equal("battle.inventory-nothing-to-unload", Refusal(noType, new UseInventoryFeature(noTypeSoldier.Id, Grenade, InventoryFeature.UnloadAmmo)));
        Assert.Equal("battle.inventory-prime-not-offered", Refusal(noType, new UseInventoryFeature(noTypeSoldier.Id, Grenade, InventoryFeature.Prime)));

        // A primed grenade with no unprime cost is neither unloaded nor unprimed by the pre-battle right click.
        var noCost = WithGrenade("fuseType: 2").Replace("costUnprime: {time: 5}", "costUnprime: {time: 0}", StringComparison.Ordinal);
        var (free, freeBattle, freeSoldier) = Open(noCost);
        Item(freeBattle, Grenade).SetFuseTimer(2, freeBattle.RequiredRules.Battle.Equipment.Items["GRENADE"]);
        Assert.Equal("battle.inventory-nothing-to-unload", Refusal(free, new UseInventoryFeature(freeSoldier.Id, Grenade, InventoryFeature.UnloadAmmo, Shift: true)));
        Assert.Equal("battle.inventory-prime-not-offered", Refusal(free, new UseInventoryFeature(freeSoldier.Id, Grenade, InventoryFeature.Prime)));
        Assert.Equal(2, Item(freeBattle, Grenade).FuseTimer);
    }

    // ---- what the audit found ----

    [Fact]
    public void TheUnloadButtonCannotSelectAFixedWeaponButAShiftClickUnloadsItBeforeTheBattle()
    {
        var (campaign, battle, soldier) = Open(FixedGunRules);
        var fixedGun = Carry(battle, soldier, "FIXED_GUN", "STR_BELT");
        fixedGun.IsWeaponWithAmmo = true;
        var clip = Carry(battle, soldier, "CLIP", "STR_BACK_PACK");
        clip.Slot = "";
        clip.OwnerUnitId = null;
        clip.IsAmmo = true;
        fixedGun.AmmoSlots[0] = clip.Id;
        var revision = battle.Revision;
        Assert.Equal("battle.inventory-item-fixed", Refusal(campaign, new UseInventoryFeature(soldier.Id, fixedGun.Id, InventoryFeature.UnloadAmmo)));
        Assert.Equal((revision, clip.Id), (battle.Revision, fixedGun.AmmoSlots[0]));

        Assert.Equal(new InventoryItemUnloaded(soldier.Id, fixedGun.Id, clip.Id, "STR_BELT", "STR_GROUND", 0),
            Assert.Single(Run(campaign, new UseInventoryFeature(soldier.Id, fixedGun.Id, InventoryFeature.UnloadAmmo, Shift: true))));
        Assert.Null(fixedGun.AmmoSlots[0]);
    }

    [Fact]
    public void ALiveGrenadeClipThatLandsOnTheGroundSetsTheDangerZoneOfTheSwapTheQuickUnloadAndTheUnload()
    {
        static bool Marked(BattleState battle, BattleUnit soldier) => battle.Map.Dangerous(battle.TileIndex(BattleState.TileOf(soldier)!.Value));

        // The swap: before the battle the old clip always goes to the ground.
        var (swap, swapBattle, swapSoldier) = Open(BombRules);
        var bomb = LiveBomb(swapBattle, swapSoldier);
        var fresh = Carry(swapBattle, swapSoldier, "CLIP", "STR_BACK_PACK");
        Assert.False(Marked(swapBattle, swapSoldier));
        Assert.Equal("STR_GROUND", Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(swap, new MoveInventoryItem(swapSoldier.Id, fresh.Id, "STR_RIGHT_HAND", 0, 0, Shift: true)))).ReplacedTo);
        Assert.Equal(("STR_GROUND", fresh.Id), (State(swapBattle, bomb.Id).Slot, Item(swapBattle, Gun).AmmoSlots[0]));
        Assert.True(Marked(swapBattle, swapSoldier));

        // The quick unload drops the clip to the ground.
        var (quick, quickBattle, quickSoldier) = Open(BombRules);
        LiveBomb(quickBattle, quickSoldier);
        Run(quick, new UseInventoryFeature(quickSoldier.Id, Gun, InventoryFeature.UnloadAmmo, Shift: true));
        Assert.True(Marked(quickBattle, quickSoldier));

        // The unload: the left hand taken sends the clip to the ground, a free left hand takes it and sets nothing.
        var (taken, takenBattle, takenSoldier) = Open(BombRules);
        LiveBomb(takenBattle, takenSoldier);
        Carry(takenBattle, takenSoldier, "GRENADE", "STR_LEFT_HAND");
        Assert.Equal("STR_GROUND", Assert.IsType<InventoryItemUnloaded>(Assert.Single(Run(taken, new UseInventoryFeature(takenSoldier.Id, Gun, InventoryFeature.UnloadAmmo)))).AmmoTo);
        Assert.True(Marked(takenBattle, takenSoldier));
        var (free, freeBattle, freeSoldier) = Open(BombRules);
        LiveBomb(freeBattle, freeSoldier);
        Assert.Equal("STR_LEFT_HAND", Assert.IsType<InventoryItemUnloaded>(Assert.Single(Run(free, new UseInventoryFeature(freeSoldier.Id, Gun, InventoryFeature.UnloadAmmo)))).AmmoTo);
        Assert.False(Marked(freeBattle, freeSoldier));
    }

    // ---- a weapon that lies on the ground ----

    [Fact]
    public void ADropOnAGroundWeaponLoadsItAndWithoutTheTargetItIsAPlainDrop()
    {
        // Inventory::mouseClick: the ground item under the drop, a weapon with ammunition that the clip does not stack with, is loaded ("Put item in weapon").
        var (campaign, battle, soldier) = Open(firstTurn: true);
        var gun = EmptyGun(battle);
        OnTheGround(battle, soldier, gun);
        var fresh = Carry(battle, soldier, "CLIP", "STR_LEFT_HAND");
        var revision = battle.Revision;
        var loaded = Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_GROUND", 0, 0, OntoItemId: gun.Id))));
        Assert.Equal((soldier.Id, gun.Id, fresh.Id, 0, -1, "", 15), (loaded.UnitId, loaded.WeaponId, loaded.AmmoId, loaded.AmmoSlot, loaded.ReplacedItemId, loaded.ReplacedTo, loaded.TimeUnitsSpent));
        Assert.True(battle.Revision > revision);
        Assert.Equal(fresh.Id, gun.AmmoSlots[0]);
        Assert.Equal(("", (int?)null, true), State(battle, fresh.Id));
        Assert.Equal(45, soldier.TimeUnits);
        // The weapon stays where it lay.
        Assert.Equal(("STR_GROUND", BattleState.TileOf(soldier)), (gun.Slot, gun.Tile));

        // Without a target the clip is only put down (the cost of the move to the ground, the clip on the tile, the weapon empty).
        var (plain, plainBattle, plainSoldier) = Open(firstTurn: true);
        var plainGun = EmptyGun(plainBattle);
        OnTheGround(plainBattle, plainSoldier, plainGun);
        var clip = Carry(plainBattle, plainSoldier, "CLIP", "STR_LEFT_HAND");
        var moved = Assert.IsType<InventoryItemMoved>(Assert.Single(Run(plain, new MoveInventoryItem(plainSoldier.Id, clip.Id, "STR_GROUND", 0, 0))));
        Assert.Equal(("STR_GROUND", (int?)null), (moved.ToSlot, plainGun.AmmoSlots[0]));
        Assert.Equal(("STR_GROUND", (int?)null, false), State(plainBattle, clip.Id));
    }

    [Fact]
    public void ADropOnAGroundWeaponRefusesAWrongClipALoadedWeaponAndAQuickSwapInTheBattleButSwapsBeforeIt()
    {
        var (campaign, battle, soldier) = Open(firstTurn: true);
        OnTheGround(battle, soldier, Item(battle, Gun));
        var fresh = Carry(battle, soldier, "CLIP", "STR_LEFT_HAND");
        var revision = battle.Revision;
        // The gun is loaded; the grenade is no ammunition for it; in the battle only a weapon in a hand is swapped.
        Assert.Equal("battle.inventory-ammo-already-loaded", Refusal(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_GROUND", 0, 0, OntoItemId: Gun)));
        Assert.Equal("battle.inventory-ammo-wrong", Refusal(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0, OntoItemId: Gun)));
        Assert.Equal("battle.inventory-ammo-swap-not-held", Refusal(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_GROUND", 0, 0, Shift: true, OntoItemId: Gun)));
        Assert.Equal(revision, battle.Revision);
        Assert.Equal(Clip, Item(battle, Gun).AmmoSlots[0]);

        // Before the battle the swap needs no hand, and the old clip goes to the ground.
        var (before, beforeBattle, beforeSoldier) = Open();
        OnTheGround(beforeBattle, beforeSoldier, Item(beforeBattle, Gun));
        var spare = Carry(beforeBattle, beforeSoldier, "CLIP", "STR_BACK_PACK");
        var swapped = Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(before, new MoveInventoryItem(beforeSoldier.Id, spare.Id, "STR_GROUND", 0, 0, Shift: true, OntoItemId: Gun))));
        Assert.Equal((Clip, "STR_GROUND"), (swapped.ReplacedItemId, swapped.ReplacedTo));
        Assert.Equal(spare.Id, Item(beforeBattle, Gun).AmmoSlots[0]);
    }

    [Fact]
    public void ADropOnAGroundItemThatIsNotThereOrCannotTakeItIsRefusedAndAStackableOneIsAPlainDrop()
    {
        var (campaign, battle, soldier) = Open();
        var fresh = Carry(battle, soldier, "CLIP", "STR_LEFT_HAND");
        // An item that is not on the unit's tile (the grenade in the back pack), or none at all.
        Assert.Equal("battle.inventory-item-unknown", Refusal(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_GROUND", 0, 0, OntoItemId: Grenade)));
        Assert.Equal("battle.inventory-item-unknown", Refusal(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_GROUND", 0, 0, OntoItemId: 9999)));
        // A ground item that is no weapon and does not stack with the clip: nothing happens in the reference.
        var rock = OnTheGround(battle, soldier, Carry(battle, soldier, "GRENADE", "STR_BELT"));
        Assert.Equal("battle.inventory-no-room", Refusal(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_GROUND", 0, 0, OntoItemId: rock.Id)));
        // A ground clip of the same type stacks: a plain drop.
        var other = OnTheGround(battle, soldier, Carry(battle, soldier, "CLIP", "STR_BELT"));
        Assert.IsType<InventoryItemMoved>(Assert.Single(Run(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_GROUND", 0, 0, OntoItemId: other.Id))));
        Assert.Equal("STR_GROUND", State(battle, fresh.Id).Slot);
    }

    [Fact]
    public void TheSwappedClipGoesToTheHandTheNewClipLeft()
    {
        // The new clip in the right hand, the weapon in the left: the old clip takes the right hand (load 15 + unload 8, no move).
        var (campaign, battle, soldier) = Open(firstTurn: true);
        var gun = Item(battle, Gun);
        gun.Slot = "STR_LEFT_HAND";
        gun.SlotX = 0;
        gun.SlotY = 0;
        var fresh = Carry(battle, soldier, "CLIP", "STR_RIGHT_HAND");
        fresh.SlotX = 0;
        var loaded = Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_LEFT_HAND", 0, 0, Shift: true))));
        Assert.Equal((Clip, "STR_RIGHT_HAND", 23), (loaded.ReplacedItemId, loaded.ReplacedTo, loaded.TimeUnitsSpent));
        Assert.Equal(("STR_RIGHT_HAND", (int?)soldier.Id, false), State(battle, Clip));
        Assert.Equal(37, soldier.TimeUnits);

        // The new clip in the left hand, the weapon in the right: the old clip takes the left hand.
        var (second, secondBattle, secondSoldier) = Open(firstTurn: true);
        var other = Carry(secondBattle, secondSoldier, "CLIP", "STR_LEFT_HAND");
        var swapped = Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(second, new MoveInventoryItem(secondSoldier.Id, other.Id, "STR_RIGHT_HAND", 0, 0, Shift: true))));
        Assert.Equal((Clip, "STR_LEFT_HAND", 23), (swapped.ReplacedItemId, swapped.ReplacedTo, swapped.TimeUnitsSpent));
        Assert.Equal(("STR_LEFT_HAND", (int?)secondSoldier.Id, false), State(secondBattle, Clip));
    }

    [Fact]
    public void TheExtendedReloadCostAddsNoMoveForAClipAlreadyInAHand()
    {
        var (campaign, battle, soldier) = Open(Costs + "\nconstants:\n  - extendedItemReloadCost: true\n", firstTurn: true);
        EmptyGun(battle);
        var fresh = Carry(battle, soldier, "CLIP", "STR_LEFT_HAND");
        Assert.Equal(15, Assert.IsType<InventoryAmmoLoaded>(Assert.Single(Run(campaign, new MoveInventoryItem(soldier.Id, fresh.Id, "STR_RIGHT_HAND", 0, 0)))).TimeUnitsSpent);
        Assert.Equal(45, soldier.TimeUnits);
    }

    private static string WithoutCost(string table, string entry) => Costs.Replace(table, table.Replace(entry, "", StringComparison.Ordinal), StringComparison.Ordinal);

    [Fact]
    public void AMissingEntryOfTheCostTableRefusesTheMoveByName()
    {
        // The ground has no cost to the right hand (the soldier's generation reads no cost from it): the extended load's move of a clip on the ground
        // and the unload's move of a weapon on the ground.
        const string ground = "costs: {STR_BELT: 2, STR_BACK_PACK: 3, STR_RIGHT_HAND: 4, STR_LEFT_HAND: 4}";
        var noGroundCost = WithoutCost(ground, "STR_RIGHT_HAND: 4, ");
        Assert.NotEqual(Costs, noGroundCost);
        var (load, loadBattle, loadSoldier) = Open(noGroundCost + "\nconstants:\n  - extendedItemReloadCost: true\n", firstTurn: true);
        EmptyGun(loadBattle);
        var fresh = OnTheGround(loadBattle, loadSoldier, Carry(loadBattle, loadSoldier, "CLIP", "STR_BACK_PACK"));
        var revision = loadBattle.Revision;
        Assert.Equal("battle.inventory-cost-unknown", Refusal(load, new MoveInventoryItem(loadSoldier.Id, fresh.Id, "STR_RIGHT_HAND", 0, 0)));
        Assert.Equal(revision, loadBattle.Revision);
        var (unload, unloadBattle, unloadSoldier) = Open(noGroundCost, firstTurn: true);
        OnTheGround(unloadBattle, unloadSoldier, Item(unloadBattle, Gun));
        Assert.Equal("battle.inventory-cost-unknown", Refusal(unload, new UseInventoryFeature(unloadSoldier.Id, Gun, InventoryFeature.UnloadAmmo)));

        // The right hand has no cost to the ground: the drop of a swapped or an unloaded clip.
        const string hand = "costs: {STR_BELT: 3, STR_BACK_PACK: 6, STR_GROUND: 5, STR_LEFT_HAND: 1}";
        var noDropCost = WithoutCost(hand, "STR_GROUND: 5, ");
        Assert.NotEqual(Costs, noDropCost);
        var (swap, swapBattle, swapSoldier) = Open(noDropCost, firstTurn: true);
        Carry(swapBattle, swapSoldier, "GRENADE", "STR_LEFT_HAND");
        var spare = Carry(swapBattle, swapSoldier, "CLIP", "STR_BACK_PACK");
        Assert.Equal("battle.inventory-cost-unknown", Refusal(swap, new MoveInventoryItem(swapSoldier.Id, spare.Id, "STR_RIGHT_HAND", 0, 0, Shift: true)));
        var (drop, dropBattle, dropSoldier) = Open(noDropCost, firstTurn: true);
        Carry(dropBattle, dropSoldier, "GRENADE", "STR_LEFT_HAND");
        Assert.Equal("battle.inventory-cost-unknown", Refusal(drop, new UseInventoryFeature(dropSoldier.Id, Gun, InventoryFeature.UnloadAmmo)));
    }
}
