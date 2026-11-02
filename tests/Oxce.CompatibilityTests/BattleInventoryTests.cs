using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;
using Xunit;
using static Oxce.CompatibilityTests.BattleStartTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 3 S4: the inventory. <c>Inventory::mouseClick</c> (pick up, drop, Ctrl click), <c>fitItem</c>, <c>overlapItems</c>,
/// <c>RuleInventory::fitItemInSlot</c>, <c>BattleItem::getMoveToCost</c> and <c>occupiesSlot</c>, <c>TileEngine::itemMoveInventory</c> and
/// <c>InventoryState::btnOkClick</c> with <c>saveEquipmentLayout</c> (<c>src/Battlescape/Inventory.cpp</c>, <c>InventoryState.cpp</c>,
/// <c>src/Mod/RuleInventory.cpp</c>, <c>src/Savegame/BattleItem.cpp</c>, <c>src/Battlescape/TileEngine.cpp</c> at 4df3a5e).
/// The suited soldier carries a gun (right hand, loaded), a grenade (back pack) and two supplies (belt full); two supplies lie on its tile.
/// </summary>
public sealed class BattleInventoryTests
{
    private const int Gun = 0;
    private const int Grenade = 1;
    private const int BeltSupply = 3;
    private const int GroundSupply = 5;
    private const int OtherGroundSupply = 6;

    /// <summary>Costs between the sections (the base rules define only the costs out of the belt and the back pack).</summary>
    internal static string WithCosts(string rules) => rules
        .Replace("- id: STR_GROUND\n    type: 2", "- id: STR_GROUND\n    type: 2\n    costs: {STR_BELT: 2, STR_BACK_PACK: 3, STR_RIGHT_HAND: 4, STR_LEFT_HAND: 4}", StringComparison.Ordinal)
        .Replace("- id: STR_RIGHT_HAND\n    type: 1", "- id: STR_RIGHT_HAND\n    type: 1\n    costs: {STR_BELT: 3, STR_BACK_PACK: 6, STR_GROUND: 5, STR_LEFT_HAND: 1}", StringComparison.Ordinal)
        .Replace("- id: STR_LEFT_HAND\n    type: 1", "- id: STR_LEFT_HAND\n    type: 1\n    costs: {STR_BELT: 3, STR_BACK_PACK: 2, STR_GROUND: 5, STR_RIGHT_HAND: 1}", StringComparison.Ordinal)
        .Replace("costs: {STR_RIGHT_HAND: 4, STR_LEFT_HAND: 4}", "costs: {STR_RIGHT_HAND: 4, STR_LEFT_HAND: 4, STR_GROUND: 2, STR_BACK_PACK: 5}", StringComparison.Ordinal)
        .Replace("costs: {STR_RIGHT_HAND: 8, STR_LEFT_HAND: 2}", "costs: {STR_RIGHT_HAND: 8, STR_LEFT_HAND: 2, STR_GROUND: 3, STR_BELT: 5}", StringComparison.Ordinal);

    private static readonly string CostRules = WithCosts(Rules);

    static BattleInventoryTests() => Assert.NotEqual(Rules, CostRules);

    private static (CampaignState Campaign, BattleState Battle, BattleUnit Soldier) Open(string? rules = null, bool firstTurn = false,
        Func<CampaignSnapshot, CampaignSnapshot>? adjust = null)
    {
        var (campaign, _) = Requested(rules ?? CostRules, "UFO_SUITED", adjust: adjust);
        Started(campaign);
        Assert.IsType<BriefingOpened>(Assert.Single(campaign.Execute(new BeginBriefing("T", "C")).Events));
        Assert.IsType<BriefingConfirmed>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));
        var battle = Battle(campaign);
        var soldier = battle.Units.Single(static unit => unit.Faction == BattleFaction.Player);
        if (firstTurn) Assert.IsType<BattleFirstTurnStarted>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
        return (campaign, battle, soldier);
    }

    private static string Refusal(CampaignState campaign, ICampaignCommand command) =>
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(command).Events)).Code;

    private static InventoryItemMoved Moved(CampaignState campaign, ICampaignCommand command)
    {
        var events = campaign.Execute(command).Events;
        Assert.Equal("", events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason ?? "");
        return Assert.IsType<InventoryItemMoved>(Assert.Single(events));
    }

    private static (string Slot, int X, int Y, int? Owner, BattlePosition? Tile) Where(BattleState battle, int item)
    {
        var found = battle.Items.Single(candidate => candidate.Id == item);
        return (found.Slot, found.SlotX, found.SlotY, found.OwnerUnitId, found.Tile);
    }

    [Fact]
    public void BeforeTheFirstTurnAMoveCostsNothingAndTheItemTakesTheNewSectionAndPosition()
    {
        var (campaign, battle, soldier) = Open();
        Assert.Equal(("STR_RIGHT_HAND", 0, 0, (int?)soldier.Id, (BattlePosition?)null), Where(battle, Gun));
        var revision = battle.Revision;
        Assert.Equal(new InventoryItemMoved(soldier.Id, Grenade, "STR_BACK_PACK", "STR_LEFT_HAND", 0, 0, 0),
            Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0)));
        Assert.Equal(("STR_LEFT_HAND", 0, 0, (int?)soldier.Id, (BattlePosition?)null), Where(battle, Grenade));
        Assert.Equal(60, soldier.TimeUnits);
        Assert.True(battle.Revision > revision);
        // Hand to hand, and a grid position within the same section.
        Moved(campaign, new MoveInventoryItem(soldier.Id, Gun, "STR_BACK_PACK", 0, 0));
        Assert.Equal(("STR_BACK_PACK", 0, 0, (int?)soldier.Id, (BattlePosition?)null), Where(battle, Gun));
    }

    [Fact]
    public void InTheBattleAMoveCostsTheSectionsTimeUnitsAndAFreeMoveStaysFree()
    {
        var (campaign, battle, soldier) = Open(firstTurn: true);
        Assert.Equal(60, soldier.TimeUnits);
        // Back pack to the left hand: 2. Hand to hand: 1. A drop from a hand costs the section's cost as it is (5), whatever the item's percentage.
        Assert.Equal(2, Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0)).TimeUnitsSpent);
        Assert.Equal(58, soldier.TimeUnits);
        battle.Items.Single(static item => item.Id == Gun).InventoryMoveCostPercent = 50;
        Assert.Equal(5, Moved(campaign, new MoveInventoryItem(soldier.Id, Gun, "STR_GROUND", 0, 0)).TimeUnitsSpent);
        Assert.Equal(53, soldier.TimeUnits);
        // The percentage applies elsewhere, at least 1: the grenade from the left hand to the belt (3 at 20% is 1).
        battle.Items.Single(static item => item.Id == Grenade).InventoryMoveCostPercent = 20;
        Moved(campaign, new MoveInventoryItem(soldier.Id, BeltSupply, "STR_BACK_PACK", 0, 0));
        Assert.Equal(53 - 5, soldier.TimeUnits);
        Assert.Equal(1, Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_BELT", 0, 0)).TimeUnitsSpent);
        // Moving within a section is free and stays free at any percentage.
        Assert.Equal(0, Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_BELT", 0, 0)).TimeUnitsSpent);
        Assert.Equal(47 - 0, soldier.TimeUnits);
    }

    [Fact]
    public void ARefusedMoveNamesItsReasonAndChangesNothing()
    {
        var (campaign, battle, soldier) = Open();
        var before = campaign.Capture().Battle;
        var revision = battle.Revision;
        var cases = new (ICampaignCommand Command, string Code)[]
        {
            (new MoveInventoryItem(999, Grenade, "STR_BELT", 0, 0), "battle.inventory-unit-unavailable"),
            (new MoveInventoryItem(1000000, Grenade, "STR_BELT", 0, 0), "battle.inventory-unit-unavailable"),
            (new MoveInventoryItem(soldier.Id, 999, "STR_BELT", 0, 0), "battle.inventory-item-unknown"),
            // The loaded clip is in no one's inventory.
            (new MoveInventoryItem(soldier.Id, 2, "STR_BELT", 0, 0), "battle.inventory-item-unknown"),
            (new MoveInventoryItem(soldier.Id, Grenade, "STR_NOWHERE", 0, 0), "battle.inventory-slot-unknown"),
            // Belt (0, 0) holds a supply; (5, 5) is no slot; the right hand holds the gun, which would take the grenade as ammunition.
            (new MoveInventoryItem(soldier.Id, GroundSupply, "STR_BELT", 0, 0), "battle.inventory-no-room"),
            (new MoveInventoryItem(soldier.Id, Grenade, "STR_BELT", 5, 5), "battle.inventory-no-room"),
            (new MoveInventoryItem(soldier.Id, Grenade, "STR_RIGHT_HAND", 0, 0), "battle.inventory-ammo-unsupported"),
            (new MoveInventoryItem(soldier.Id, GroundSupply, "STR_BACK_PACK", 0, 0), "battle.inventory-no-room"),
        };
        foreach (var (command, code) in cases) Assert.Equal(code, Refusal(campaign, command));
        Assert.Equal(revision, battle.Revision);
        Assert.Equivalent(before, campaign.Capture().Battle, strict: true);
    }

    [Fact]
    public void TheInventoryOpensAfterTheBriefingAndTheClientCanAskBeforeMoving()
    {
        var (campaign, _) = Requested(CostRules, "UFO_SUITED");
        Started(campaign);
        var soldier = Battle(campaign).Units.Single(static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal("battle.inventory-unavailable", Refusal(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0)));
        Assert.Equal("battle.inventory-unavailable", Refusal(campaign, new AutoPlaceInventoryItem(soldier.Id, GroundSupply, true)));
        var query = campaign.GetQuery<IBattleInventoryQuery>()!;
        Assert.Null(query.QueryInventory(BattleViewer.LocalPlayer, soldier.Id));
        Assert.Equal("battle.inventory-unavailable", query.CheckInventoryMove(BattleViewer.LocalPlayer, soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0));
        campaign.Execute(new BeginBriefing("", ""));
        campaign.Execute(new ConfirmBriefing());
        Assert.Null(query.CheckInventoryMove(BattleViewer.LocalPlayer, soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0));
        Assert.Equal("battle.inventory-no-room", query.CheckInventoryMove(BattleViewer.LocalPlayer, soldier.Id, Grenade, "STR_BELT", 0, 0));
        // The check changed nothing; another faction sees no inventory.
        Assert.Equal("STR_BACK_PACK", Battle(campaign).Items.Single(static item => item.Id == Grenade).Slot);
        Assert.Null(query.QueryInventory(new BattleViewer(BattleFaction.Hostile), soldier.Id));
        Assert.Equal("battle.inventory-unit-unavailable", query.CheckInventoryMove(new BattleViewer(BattleFaction.Hostile), soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0));
    }

    [Fact]
    public void AMoveInTheBattleNeedsTheTimeUnitsAndAnUnknownCostIsRefusedOnlyWhereItIsCharged()
    {
        var (campaign, _, soldier) = Open(firstTurn: true);
        soldier.TimeUnits = 1;
        Assert.Equal("battle.inventory-not-enough-time-units", Refusal(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0)));
        soldier.TimeUnits = 2;
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0));
        Assert.Equal(0, soldier.TimeUnits);

        // The base rules give the hands no costs: the pre-battle inventory moves from a hand regardless, the battle's cannot be priced.
        var (plain, _, first) = Open(Rules);
        Moved(plain, new MoveInventoryItem(first.Id, Gun, "STR_LEFT_HAND", 0, 0));
        var (charged, _, second) = Open(Rules, firstTurn: true);
        Assert.Equal("battle.inventory-cost-unknown", Refusal(charged, new MoveInventoryItem(second.Id, Gun, "STR_LEFT_HAND", 0, 0)));
        Assert.Equal(60, second.TimeUnits);
    }

    [Fact]
    public void ASectionThatDoesNotTakeAnItemRefusesItButTheGroundTakesEverything()
    {
        var rules = CostRules.Replace("  - type: GRENADE\n    battleType: 4\n    weight: 1",
            "  - type: GRENADE\n    battleType: 4\n    weight: 1\n    supportedInventorySections: [STR_BACK_PACK, STR_BELT]", StringComparison.Ordinal);
        Assert.NotEqual(CostRules, rules);
        var (campaign, battle, soldier) = Open(rules);
        Assert.Equal("battle.inventory-section-unsupported", Refusal(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0)));
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0));
        Assert.Equal(("STR_GROUND", 0, 0, (int?)null, (BattlePosition?)soldier.Position), Where(battle, Grenade));
    }

    [Fact]
    public void ADropLeavesTheOwnerAndLiesOnTheUnitsTileLastAndAPickUpAppendsToTheInventory()
    {
        var (campaign, battle, soldier) = Open();
        Moved(campaign, new MoveInventoryItem(soldier.Id, Gun, "STR_GROUND", 3, 4));
        var gun = battle.Items.Single(static item => item.Id == Gun);
        Assert.Equal((null, soldier.Id, soldier.Position, "STR_GROUND", 0, 0), (gun.OwnerUnitId, gun.PreviousOwnerUnitId, gun.Tile, gun.Slot, gun.SlotX, gun.SlotY));
        var query = campaign.GetQuery<IBattleInventoryQuery>()!;
        // Tile::getInventory: the supplies first, the dropped gun after them.
        Assert.Equal([GroundSupply, OtherGroundSupply, Gun], query.QueryInventory(BattleViewer.LocalPlayer, soldier.Id)!.Ground.Select(static item => item.ItemId));
        // Picking a ground item up appends it to the inventory (BattleUnit::getInventory().push_back): after the grenade.
        Moved(campaign, new MoveInventoryItem(soldier.Id, OtherGroundSupply, "STR_LEFT_HAND", 0, 0));
        Assert.Equal([Grenade, BeltSupply, BeltSupply + 1, OtherGroundSupply], query.QueryInventory(BattleViewer.LocalPlayer, soldier.Id)!.Carried.Select(static item => item.ItemId));
        // A drop on the ground again moves it to the back of the tile.
        Moved(campaign, new MoveInventoryItem(soldier.Id, OtherGroundSupply, "STR_GROUND", 0, 0));
        Assert.Equal([GroundSupply, Gun, OtherGroundSupply], query.QueryInventory(BattleViewer.LocalPlayer, soldier.Id)!.Ground.Select(static item => item.ItemId));
        // An item on the ground moved to the ground is a no-op.
        Assert.Equal(0, Moved(campaign, new MoveInventoryItem(soldier.Id, GroundSupply, "STR_GROUND", 0, 0)).TimeUnitsSpent);
    }

    [Fact]
    public void BeforeTheFirstTurnAUnitOffThePileUsesTheCraftInventoryTileAsItsGround()
    {
        var (campaign, battle, soldier) = Open();
        var pile = soldier.Position!.Value;
        Assert.Equal((pile, pile), (battle.CraftInventoryTile, soldier.InventoryTile));
        // A second soldier beside the pile: BattlescapeGenerator::deployXCOM gives every X-COM unit the craft inventory tile.
        var other = new BattleUnit(901, BattleFaction.Player, BattleController.Human(0))
        {
            Stats = soldier.Stats,
            ArmorId = soldier.ArmorId,
            UnitType = soldier.UnitType,
            SoldierId = soldier.SoldierId,
            Position = pile with { X = pile.X + 1 },
            InventoryTile = pile,
        };
        battle.AddUnit(other);
        var query = campaign.GetQuery<IBattleInventoryQuery>()!;
        Assert.Equal([GroundSupply, OtherGroundSupply], query.QueryInventory(BattleViewer.LocalPlayer, other.Id)!.Ground.Select(static item => item.ItemId));
        Moved(campaign, new MoveInventoryItem(other.Id, GroundSupply, "STR_LEFT_HAND", 0, 0));
        Assert.Equal(("STR_LEFT_HAND", 0, 0, (int?)other.Id, (BattlePosition?)null), Where(battle, GroundSupply));
        // The drop lands on the pile, and the unit standing there learns the tile.
        Moved(campaign, new MoveInventoryItem(other.Id, GroundSupply, "STR_GROUND", 0, 0));
        Assert.Equal(("STR_GROUND", 0, 0, (int?)null, (BattlePosition?)pile), Where(battle, GroundSupply));
        // From the first turn each unit's ground is its own tile.
        Assert.IsType<BattleFirstTurnStarted>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
        Assert.Equal(((BattlePosition?)null, (BattlePosition?)null), (soldier.InventoryTile, other.InventoryTile));
        Assert.Empty(query.QueryInventory(BattleViewer.LocalPlayer, other.Id)!.Ground);
        Assert.Equal([GroundSupply, OtherGroundSupply], query.QueryInventory(BattleViewer.LocalPlayer, soldier.Id)!.Ground.Select(static item => item.ItemId).Order());
    }

    [Fact]
    public void AnUnconsciousUnitCarriedAsAnItemLeavesTheMapAndDroppedLiesOnTheTile()
    {
        var (campaign, battle, soldier) = Open();
        var alien = battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);
        battle.Items.Single(static item => item.Id == GroundSupply).CorpseUnitId = alien.Id;
        alien.Status = BattleUnitStatus.Unconscious;
        Moved(campaign, new MoveInventoryItem(soldier.Id, GroundSupply, "STR_LEFT_HAND", 0, 0));
        Assert.Null(alien.Position);
        Moved(campaign, new MoveInventoryItem(soldier.Id, GroundSupply, "STR_GROUND", 0, 0));
        Assert.Equal(soldier.Position, alien.Position);
        // A dead unit's body is only an item: its position stays.
        var other = battle.Units.Last(static unit => unit.Faction == BattleFaction.Hostile);
        var position = other.Position;
        other.Status = BattleUnitStatus.Dead;
        battle.Items.Single(static item => item.Id == OtherGroundSupply).CorpseUnitId = other.Id;
        Moved(campaign, new MoveInventoryItem(soldier.Id, OtherGroundSupply, "STR_LEFT_HAND", 0, 0));
        Assert.Equal(position, other.Position);
    }

    [Fact]
    public void AnEnabledGrenadeIsNotDroppedBecauseItsDangerZoneBelongsToTheAi()
    {
        var (campaign, battle, soldier) = Open();
        battle.Items.Single(static item => item.Id == Grenade).FuseEnabled = true;
        Assert.Equal("battle.inventory-danger-zone-unsupported", Refusal(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0)));
        Assert.Equal(("STR_BACK_PACK", 0, 0, (int?)soldier.Id, (BattlePosition?)null), Where(battle, Grenade));
        // Between sections it moves freely.
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0));
    }

    [Fact]
    public void AFixedItemCannotBeMoved()
    {
        var rules = CostRules.Replace("  - type: GRENADE\n    battleType: 4\n    weight: 1", "  - type: GRENADE\n    battleType: 4\n    weight: 1\n    fixedWeapon: true", StringComparison.Ordinal);
        Assert.NotEqual(CostRules, rules);
        var (campaign, battle, soldier) = Open(rules);
        var fixedItem = battle.Items.First(item => item.ItemType == "GRENADE" && item.OwnerUnitId == soldier.Id);
        Assert.Equal("battle.inventory-item-fixed", Refusal(campaign, new MoveInventoryItem(soldier.Id, fixedItem.Id, "STR_LEFT_HAND", 0, 0)));
        Assert.Equal("battle.inventory-item-fixed", Refusal(campaign, new AutoPlaceInventoryItem(soldier.Id, fixedItem.Id, false)));
    }

    [Fact]
    public void ACtrlClickDropsACarriedItemAndPlacesAGroundItemInTheVanillaOrderUntilASectionTakesIt()
    {
        var (campaign, battle, soldier) = Open();
        // Carried: dropped to the ground.
        Assert.Equal(new InventoryItemMoved(soldier.Id, Grenade, "STR_BACK_PACK", "STR_GROUND", 0, 0, 0),
            Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, Grenade, Smart: false)));
        // Ground: a supply is small, so the vanilla choice is the belt, which is full; then the sections in alphabetical order:
        // the back pack (free now), so the grenade's old home takes it.
        Assert.Equal(new InventoryItemMoved(soldier.Id, GroundSupply, "STR_GROUND", "STR_BACK_PACK", 0, 0, 0),
            Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, GroundSupply, Smart: false)));
        // Next: the left hand; then nothing is free.
        Assert.Equal("STR_LEFT_HAND", Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, OtherGroundSupply, Smart: false)).ToSlot);
        Assert.Equal("battle.inventory-no-room", Refusal(campaign, new AutoPlaceInventoryItem(soldier.Id, Grenade, Smart: false)));
        Assert.Equal("STR_GROUND", Where(battle, Grenade).Slot);
    }

    [Fact]
    public void ACtrlClickTriesTheItemsDefaultSectionBeforeTheVanillaChoice()
    {
        var (campaign, _, soldier) = Open(CostRules.Replace("  - type: GRENADE\n    battleType: 4\n    weight: 1",
            "  - type: GRENADE\n    battleType: 4\n    weight: 1\n    defaultInventorySlot: STR_LEFT_HAND", StringComparison.Ordinal));
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0));
        Assert.Equal("STR_LEFT_HAND", Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, Grenade, Smart: false)).ToSlot);
    }

    [Fact]
    public void ASmartCtrlClickPicksTheCheapestSectionToReachAHandAndChargesItsCost()
    {
        var (campaign, battle, soldier) = Open(firstTurn: true);
        // Free the back pack and the belt's first position: the sections that take a supply are the back pack (cost to a hand: 2
        // to the left), the belt (4) and the left hand (0: its cost to itself is 0, to the right hand 1, the lower counts). The left hand is the cheapest.
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0));
        Moved(campaign, new MoveInventoryItem(soldier.Id, BeltSupply, "STR_GROUND", 0, 0));
        var spent = soldier.TimeUnits;
        var placed = Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, GroundSupply, Smart: true));
        Assert.Equal(("STR_LEFT_HAND", 4), (placed.ToSlot, placed.TimeUnitsSpent));
        Assert.Equal(spent - 4, soldier.TimeUnits);
        Assert.Equal("STR_LEFT_HAND", Where(battle, GroundSupply).Slot);
        // With nothing that takes the next one (both hands are full, the back pack is taken): the next fits the back pack
        // or the belt; the cheaper reach to a hand is the back pack's 2.
        placed = Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, OtherGroundSupply, Smart: true));
        Assert.Equal("STR_BACK_PACK", placed.ToSlot);
    }

    [Fact]
    public void ASmartCtrlClickThatFindsNoSectionLeavesTheGroundItemWhereItIs()
    {
        var (campaign, battle, soldier) = Open();
        Moved(campaign, new MoveInventoryItem(soldier.Id, GroundSupply, "STR_LEFT_HAND", 0, 0));
        // Hands, belt and back pack are all full: the reference "moves" it to the ground it is on, with no warning.
        Assert.Equal(new InventoryItemMoved(soldier.Id, OtherGroundSupply, "STR_GROUND", "STR_GROUND", 0, 0, 0),
            Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, OtherGroundSupply, Smart: true)));
        Assert.Equal(("STR_GROUND", 0, 0, (int?)null, (BattlePosition?)soldier.Position), Where(battle, OtherGroundSupply));
    }

    [Fact]
    public void AnAutoPlacementNeedsTheTimeUnitsAndReportsTheWarningOfTheLastSection()
    {
        var (campaign, _, soldier) = Open(firstTurn: true);
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0));
        soldier.TimeUnits = 1;
        // Every section that is free needs more than 1 time unit from the ground; the last warning stands.
        Assert.Equal("battle.inventory-not-enough-time-units", Refusal(campaign, new AutoPlaceInventoryItem(soldier.Id, GroundSupply, Smart: false)));
        Assert.Equal(1, soldier.TimeUnits);
    }

    [Fact]
    public void TheScreenQueryListsTheSectionsTheCarriedItemsAndTheGroundForTheOwningFaction()
    {
        var (campaign, _, soldier) = Open(firstTurn: true);
        var view = campaign.GetQuery<IBattleInventoryQuery>()!.QueryInventory(BattleViewer.LocalPlayer, soldier.Id)!;
        Assert.Equal((soldier.Id, 60, true), (view.UnitId, view.TimeUnits, view.ChargesTimeUnits));
        Assert.Equal(["STR_GROUND", "STR_RIGHT_HAND", "STR_LEFT_HAND", "STR_BELT", "STR_BACK_PACK"], view.Slots.Select(static slot => slot.Id));
        Assert.Equal([new BattleInventoryCell(0, 0), new BattleInventoryCell(1, 0)], view.Slots.Single(static slot => slot.Id == "STR_BELT").Cells);
        var gun = view.Carried.Single(static item => item.ItemId == Gun);
        Assert.Equal(("GUN", "STR_RIGHT_HAND", 0, false), (gun.ItemType, gun.Slot, gun.AmmoQuantity, gun.Fixed));
        Assert.Equal(["CLIP", "", "", ""], gun.LoadedAmmo);
        Assert.Equal([GroundSupply, OtherGroundSupply], view.Ground.Select(static item => item.ItemId));
        // An item of the soldier's is not on the alien's screen.
        Assert.Null(campaign.GetQuery<IBattleInventoryQuery>()!.QueryInventory(BattleViewer.LocalPlayer, 1000000));
    }

    [Theory]
    [InlineData(InventoryFeature.LoadAmmo, "battle.inventory-ammo-unsupported")]
    [InlineData(InventoryFeature.UnloadAmmo, "battle.inventory-ammo-unsupported")]
    [InlineData(InventoryFeature.Throw, "battle.inventory-throw-unsupported")]
    [InlineData(InventoryFeature.Prime, "battle.inventory-prime-unsupported")]
    [InlineData(InventoryFeature.SaveLayout, "battle.inventory-layout-screens-unsupported")]
    [InlineData(InventoryFeature.LoadLayout, "battle.inventory-layout-screens-unsupported")]
    [InlineData(InventoryFeature.ClearInventory, "battle.inventory-clear-unsupported")]
    [InlineData(InventoryFeature.AutoEquip, "battle.inventory-autoequip-unsupported")]
    [InlineData(InventoryFeature.Template, "battle.inventory-template-unsupported")]
    [InlineData(InventoryFeature.RenameSoldier, "battle.inventory-rename-unsupported")]
    public void TheFeaturesThatArriveLaterAreRefusedByName(InventoryFeature feature, string code)
    {
        var (campaign, battle, soldier) = Open();
        var revision = battle.Revision;
        Assert.Equal(code, Refusal(campaign, new UseInventoryFeature(soldier.Id, Gun, feature)));
        Assert.Equal(revision, battle.Revision);
    }

    [Fact]
    public void ClosingThePreBattleInventorySavesEachSoldiersLayoutAndStartsTheFirstTurn()
    {
        var (campaign, battle, soldier) = Open();
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0));
        Moved(campaign, new MoveInventoryItem(soldier.Id, OtherGroundSupply, "STR_BACK_PACK", 0, 0));
        Assert.Empty(Layout(campaign));
        var started = Assert.IsType<BattleFirstTurnStarted>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
        Assert.Equal(1, started.LayoutsSaved);
        var layout = Layout(campaign);
        // The loaded gun keeps its clip's type; supplies (battle type 0) cannot go on a craft, so the layout skips them.
        Assert.Equal([("GUN", "STR_RIGHT_HAND", 0, 0, "CLIP"), ("GRENADE", "STR_LEFT_HAND", 0, 0, "")],
            layout.Select(static entry => (entry.ItemType, entry.Slot, entry.SlotX, entry.SlotY, entry.AmmoItems[0])));
        Assert.All(layout, static entry => Assert.Equal((-1, false), (entry.FuseTimer, entry.Fixed)));
        Assert.Equal(1, battle.Turn);
        // A new battle with that layout equips the soldier alike: the layout is what XcomCargo reads (S1 of branch 1), so the save is complete.
    }

    [Fact]
    public void TheAlternateCraftEquipmentManagementKeepsTheLayoutWithTheBaseScreens()
    {
        var (campaign, _, soldier) = Open(adjust: static snapshot => snapshot with { Options = snapshot.Options! with { AlternateCraftEquipmentManagement = true } });
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0));
        var started = Assert.IsType<BattleFirstTurnStarted>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
        Assert.Equal(0, started.LayoutsSaved);
        Assert.Empty(Layout(campaign));
    }

    [Fact]
    public void ARefusedFirstTurnSavesNoLayout()
    {
        // A battle whose rules bind a new-turn script refuses to start (S3); the layout is saved only when it starts.
        var (campaign, _, soldier) = Open();
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_LEFT_HAND", 0, 0));
        foreach (var unit in Battle(campaign).Units.Where(static unit => unit.Faction == BattleFaction.Player)) unit.Status = BattleUnitStatus.Dead;
        Assert.Equal("battle.no-live-soldiers", Refusal(campaign, new StartFirstTurn()));
        Assert.Empty(Layout(campaign));
    }

    private static IReadOnlyList<SoldierLayoutItem> Layout(CampaignState campaign) =>
        campaign.BaseStates.SelectMany(static owner => owner.Soldiers).Single(static soldier => soldier.Id == 1).Personal!.EquipmentLayout;

    [Fact]
    public void TheSessionMapsAHumanControlledUnitToItsPlayerAndAiUnitsToNobody()
    {
        var (campaign, _, soldier) = Open();
        var controllers = new BattleUnitControllers(campaign.GetQuery<IBattleQuery>()!);
        Assert.Equal(PlayerId.Local, controllers.ControllerOf(soldier.Id));
        Assert.Null(controllers.ControllerOf(1000000));
        Assert.Null(controllers.ControllerOf(12345));
        Assert.Equal(new PlayerId(3), new BattleUnitControllers(campaign.GetQuery<IBattleQuery>()!, static _ => new PlayerId(3)).ControllerOf(soldier.Id));
        Assert.True(new MoveInventoryItem(soldier.Id, Grenade, "STR_BELT", 0, 0) is IUnitCommand { UnitId: var id } && id == soldier.Id);
    }

    private static string WithGun(string extra) => CostRules.Replace("  - type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1",
        "  - type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1\n    " + extra, StringComparison.Ordinal);

    [Fact]
    public void ASmartCtrlClickAddsTenForATwoHandedItemInTheOtherHandAndSkipsAHandABothHandsItemBlocks()
    {
        var plain = Open(CostRules);
        Moved(plain.Campaign, new MoveInventoryItem(plain.Soldier.Id, Grenade, "STR_GROUND", 0, 0));
        // Without a rule the left hand (cost 0) beats the back pack (2).
        Assert.Equal("STR_LEFT_HAND", Moved(plain.Campaign, new AutoPlaceInventoryItem(plain.Soldier.Id, GroundSupply, Smart: true)).ToSlot);

        var rules = WithGun("twoHanded: true");
        Assert.NotEqual(CostRules, rules);
        var (campaign, _, soldier) = Open(rules);
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0));
        // The gun in the right hand is two-handed: the left hand costs 0 + 10, so the back pack (2) wins.
        Assert.Equal("STR_BACK_PACK", Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, GroundSupply, Smart: true)).ToSlot);

        // A both-hands weapon blocks the left hand outright: with the belt and back pack full nothing takes the item.
        var (blocked, battle, holder) = Open(WithGun("blockBothHands: true"));
        Assert.Equal(new InventoryItemMoved(holder.Id, GroundSupply, "STR_GROUND", "STR_GROUND", 0, 0, 0),
            Moved(blocked, new AutoPlaceInventoryItem(holder.Id, GroundSupply, Smart: true)));
        Assert.Equal("STR_GROUND", Where(battle, GroundSupply).Slot);
    }

    [Fact]
    public void ACtrlClickFollowsTheFirstCategoryWithAnInventoryOrderBeforeTheVanillaChoice()
    {
        // The belt is full and the back pack freed: the vanilla order falls back to the back pack (alphabetical).
        var (plain, _, first) = Open();
        Moved(plain, new MoveInventoryItem(first.Id, Grenade, "STR_GROUND", 0, 0));
        Assert.Equal("STR_BACK_PACK", Moved(plain, new AutoPlaceInventoryItem(first.Id, Grenade, Smart: false)).ToSlot);

        var rules = CostRules.Replace("  - type: GRENADE\n    battleType: 4\n    weight: 1", "  - type: GRENADE\n    battleType: 4\n    weight: 1\n    categories: [CAT_NONE, CAT_X]", StringComparison.Ordinal)
            + "\nitemCategories:\n  - type: CAT_X\n    invOrder: [STR_GROUND, STR_LEFT_HAND]\n";
        var (campaign, _, soldier) = Open(rules);
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0));
        Assert.Equal("STR_LEFT_HAND", Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, Grenade, Smart: false)).ToSlot);
    }

    [Fact]
    public void ASmartCtrlClickWithoutAHandCostIsRefusedAsAnUnknownCost()
    {
        var noCosts = CostRules.Replace("\n    costs: {STR_BELT: 3, STR_BACK_PACK: 2, STR_GROUND: 5, STR_RIGHT_HAND: 1}", "", StringComparison.Ordinal);
        Assert.NotEqual(CostRules, noCosts);
        var (campaign, _, soldier) = Open(noCosts, firstTurn: true);
        // The left hand has no costs: the cheapest-to-a-hand choice cannot be made.
        Assert.Equal("battle.inventory-cost-unknown", Refusal(campaign, new AutoPlaceInventoryItem(soldier.Id, GroundSupply, Smart: true)));
    }

    [Fact]
    public void TheSavedLayoutKeepsAFixedWeaponOnlyForTheAmmoItHolds()
    {
        var (campaign, _, _) = Open(WithGun("fixedWeapon: true"));
        campaign.Execute(new StartFirstTurn());
        var layout = Layout(campaign);
        var gun = Assert.Single(layout, static entry => entry.ItemType == "GUN");
        Assert.Equal((true, "CLIP"), (gun.Fixed, gun.AmmoItems[0]));

        var rules = CostRules.Replace("  - type: GRENADE\n    battleType: 4\n    weight: 1", "  - type: GRENADE\n    battleType: 4\n    weight: 1\n    fixedWeapon: true", StringComparison.Ordinal);
        var (second, _, _) = Open(rules);
        second.Execute(new StartFirstTurn());
        // A fixed grenade holds no ammo: it is not part of the layout.
        Assert.DoesNotContain(Layout(second), static entry => entry.ItemType == "GRENADE");
        Assert.Contains(Layout(second), static entry => entry.ItemType == "GUN");
    }

    [Fact]
    public void ADroppedBodySwapsPlacesWithTheFirstItemOnTheTileAndTheDropperLearnsTheTile()
    {
        var (campaign, battle, soldier) = Open();
        Assert.All(soldier.TileLastSpottedBy, static value => Assert.Equal(-1, value));
        battle.Items.Single(static item => item.Id == BeltSupply).CorpseUnitId = 1000000;
        battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile).Status = BattleUnitStatus.Dead;
        Moved(campaign, new MoveInventoryItem(soldier.Id, BeltSupply, "STR_GROUND", 0, 0));
        // Tile::addItem: [5, 6] + the body, then the front and back swap.
        Assert.Equal([BeltSupply, OtherGroundSupply, GroundSupply],
            campaign.GetQuery<IBattleInventoryQuery>()!.QueryInventory(BattleViewer.LocalPlayer, soldier.Id)!.Ground.Select(static item => item.ItemId));
        // updateEnemyKnowledge(tile, clue): every faction's last spotted tile, and the turns since seen restart.
        var tile = battle.TileIndex(soldier.Position!.Value);
        Assert.All(soldier.TileLastSpottedBy, value => Assert.Equal(tile, value));
        Assert.All(soldier.TileLastSpottedForBlindShotBy, value => Assert.Equal(tile, value));
        Assert.All(soldier.TurnsSinceSpotted, static value => Assert.Equal(0, value));
    }

    [Fact]
    public void TheInventoryIsClosedOnTheEnemysTurnAndNothingMoves()
    {
        // InventoryState opens on X-COM's turn only: in the battle with another side on turn, every inventory command and query refuses.
        var (campaign, battle, soldier) = Open(firstTurn: true);
        var queries = campaign.GetQuery<IBattleInventoryQuery>()!;
        Assert.NotNull(queries.QueryInventory(BattleViewer.LocalPlayer, soldier.Id));
        battle.Side = BattleFaction.Hostile;
        var before = Where(battle, Grenade);
        Assert.Equal("battle.inventory-unavailable", Refusal(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0)));
        Assert.Equal("battle.inventory-unavailable", Refusal(campaign, new AutoPlaceInventoryItem(soldier.Id, Grenade, Smart: true)));
        Assert.Equal("battle.inventory-unavailable", queries.CheckInventoryMove(BattleViewer.LocalPlayer, soldier.Id, Grenade, "STR_GROUND", 0, 0));
        Assert.Null(queries.QueryInventory(BattleViewer.LocalPlayer, soldier.Id));
        Assert.Equal(before, Where(battle, Grenade));
        // Back on X-COM's turn it opens again.
        battle.Side = BattleFaction.Player;
        Assert.NotNull(queries.QueryInventory(BattleViewer.LocalPlayer, soldier.Id));
    }

    [Fact]
    public void AFeatureRequestIsRefusedAsUnavailableWithoutAnOpenInventory()
    {
        var (campaign, _) = Requested(CostRules, "UFO_SUITED");
        Started(campaign);
        Assert.Equal("battle.inventory-unavailable", Refusal(campaign, new UseInventoryFeature(1, Gun, InventoryFeature.Throw)));
    }

    [Fact]
    public void AModsExtendedInventorySlotSortingMakesTheLastFallbackFollowTheListOrder()
    {
        // The belt is full; the back pack and the left hand are free. Alphabetical order reaches the back pack first, list order the left hand.
        var (plain, _, first) = Open();
        Moved(plain, new MoveInventoryItem(first.Id, Grenade, "STR_GROUND", 0, 0));
        Assert.Equal("STR_BACK_PACK", Moved(plain, new AutoPlaceInventoryItem(first.Id, Grenade, Smart: false)).ToSlot);

        var rules = CostRules + "\nextendedInventorySlotSorting: true\n";
        var (campaign, battle, soldier) = Open(rules);
        Assert.True(battle.Rules!.Campaign.ExtendedInventorySlotSorting);
        Moved(campaign, new MoveInventoryItem(soldier.Id, Grenade, "STR_GROUND", 0, 0));
        Assert.Equal("STR_LEFT_HAND", Moved(campaign, new AutoPlaceInventoryItem(soldier.Id, Grenade, Smart: false)).ToSlot);
    }
}
