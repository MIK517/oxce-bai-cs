using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;
using static Oxce.CompatibilityTests.BattleSaveRootTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 2 S4: the battle's items through the save module (<c>SavedBattleGame::load</c>'s item passes and the
/// units' second pass, <c>save</c>'s item keys, <c>BattleItem::load</c>, <c>save</c>, the constructor, <c>setFuseTimer</c>,
/// <c>setAmmoForSlot</c> and <c>moveToOwner</c>; <c>BattleUnit::setSpecialWeapon</c> and <c>addLoadedSpecialWeapon</c>;
/// <c>src/Savegame/SavedBattleGame.cpp</c>, <c>BattleItem.cpp</c> and <c>BattleUnit.cpp</c> at 4df3a5e).
/// </summary>
public sealed class BattleSaveItemTests
{
    // Items for each save condition, an armor with a self-destruct item and item tags, added to the battle rules' lists.
    private static readonly string ItemRules = BattleStartTests.Rules
        .Replace("\nitems:\n", "\nitems:\n" +
            "  - type: MEDKIT\n    battleType: 6\n    heal: 3\n    painKiller: 4\n    stimulant: 5\n" +
            "  - type: BATTERY\n    battleType: 2\n    clipSize: 10\n    isAmmoRechargeable: true\n" +
            "  - type: MINE\n    battleType: 5\n    fuseTriggerEvents: {proximityTrigger: true}\n" +
            "  - type: HEAVY\n    inventoryMoveCost: {basePercent: 50}\n" +
            "  - type: BOMB\n    battleType: 4\n" +
            "  - type: THROWN\n    battleType: 4\n    fuseTriggerEvents: {throwTrigger: true}\n" +
            "  - type: INERT\n    battleType: 4\n    fuseTriggerEvents: {defaultBehavior: false}\n" +
            "", StringComparison.Ordinal)
        .Replace("\narmors:\n", "\narmors:\n" +
            "  - type: BOMB_ARMOR\n    size: 1\n    allowInv: false\n    specialWeapon: RAM\n    selfDestructItem: BOMB\n" +
            "    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]\n", StringComparison.Ordinal)
        .Replace("\nunits:\n", "\nunits:\n  - type: BOMBER\n    armor: BOMB_ARMOR\n" +
            "  - type: RAMMER\n    armor: SMALL_VEHICLE_ARMOR\n    meleeWeapon: RAM\n    stats: {tu: 50}\n" +
            // A psionic with a psi weapon, and a unit whose special built-in firearm has no clip size.
            "  - type: PSIONIC\n    armor: SMALL_VEHICLE_ARMOR\n    psiWeapon: AMP\n    stats: {psiSkill: 50}\n" +
            "  - type: BADMELEE\n    armor: SMALL_VEHICLE_ARMOR\n    meleeWeapon: GUN\n", StringComparison.Ordinal)
        // The generated aliens are rammers, with a special built-in melee weapon.
        .Replace("  - id: RACE_TEST\n    members: [ALIEN, ALIEN, ALIEN, ALIEN, ALIEN, ALIEN, ALIEN, ALIEN]",
            "  - id: RACE_TEST\n    members: [RAMMER, RAMMER, RAMMER, RAMMER, RAMMER, RAMMER, RAMMER, RAMMER]", StringComparison.Ordinal) +
        "\nextended:\n  tags:\n    BattleItem: {ITEM_TAG: int, ITEM_LIST: RuleList}\n" +
        // The soldiers' special built-in weapon (RuleSoldier::getSpecialWeapon).
        "soldiers:\n  - type: RECRUIT\n    specialWeapon: RAM\n";

    private static readonly Lazy<(CampaignState Campaign, BattleState Battle)> Fixture = new(() => Generated(ItemRules));

    private static RuntimeContent Content => Fixture.Value.Campaign.Content;

    private static BattleRestoreCampaign Campaign => BattleRestoreCampaign.From(Fixture.Value.Campaign);

    private const string Root = "selectedUnit: -1\nwidth: 4\nlength: 4\nheight: 2\n";

    // A standing alien (no special built-in items from its rules), a dead one, and a unit whose rule is gone.
    private const string Units = "units:\n" +
        "  - {id: 1000000, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, status: 0, position: [1, 1, 0]}\n" +
        "  - {id: 1000001, genUnitType: ALIEN, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, status: 6}\n" +
        "  - {id: 1000002, genUnitType: GONE, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, status: 0}\n";

    // Every key BattleItem::save writes, in its order.
    private const string AllKeys = "items:\n" +
        "  - {id: 3, type: MEDKIT, owner: 1000000, previousOwner: 1000000, inventoryMoveCost: {basePercent: 70}, inventoryslot: STR_BELT," +
        " inventoryX: 1, inventoryY: 0, painKiller: 1, heal: 2, stimulant: 0, droppedOnAlienTurn: true, XCOMProperty: true, tags: {ITEM_TAG: 5}}\n" +
        "  - {id: 5, type: MINE, unit: 1000001, inventoryslot: STR_GROUND, position: [3, 2, 1], fuseTimer: 1, fuseEnabed: true}\n" +
        "  - {id: 6, type: GUN, owner: 1000000, inventoryslot: STR_RIGHT_HAND, ammoItem: 7, ammoItemSlots: [7, 6, 6, 6]}\n" +
        "  - {id: 7, type: CLIP, ammoqty: 5}\n" +
        "  - {id: 8, type: BATTERY, inventoryslot: STR_GROUND, position: [0, 0, 0], ammoqty: 0}\n" +
        "itemsSpecial:\n" +
        "  - {id: 9, type: RAM, owner: 1000000, ammoqty: -1, ammoItem: 9, ammoItemSlots: [9, 9, 9, 9]}\n";

    private static BattleSnapshot Read(string yaml, IDiagnosticSink? diagnostics = null) =>
        OxceBattleSave.Read(Parse(Root + yaml), Content, diagnostics);

    private static BattleState Restore(string yaml) => BattleState.Restore(Read(yaml), Content, Terrain(), campaign: Campaign);

    private static BattleItem Item(BattleState battle, int id) => battle.FindItem(id) ?? throw new KeyNotFoundException($"item {id}");

    private static YamlMappingNode Written(BattleState battle) => OxceBattleSave.Write(battle.Capture());

    private static YamlMappingNode WrittenItem(BattleState battle, int id, string key = "items") =>
        ((YamlSequenceNode)Value(Written(battle), key)).Items.Cast<YamlMappingNode>()
            .Single(item => ((YamlScalarNode)Value(item, "id")).Value == id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static YamlNode Value(YamlMappingNode map, string key) => map.TryGet(key, out var node) ? node! : throw new KeyNotFoundException(key);

    private static string Text(YamlMappingNode map, string key) => ((YamlScalarNode)Value(map, key)).Value;

    private static string[] Ints(YamlMappingNode map, string key) =>
        [.. ((YamlSequenceNode)Value(map, key)).Items.Select(static node => ((YamlScalarNode)node).Value)];

    private static string[] Keys(YamlMappingNode map) => [.. map.Entries.Select(static entry => entry.ScalarKey!)];

    private static string Failure(Action action) => Assert.Throws<InvalidDataException>(action).Message;

    private static YamlMappingNode Only(YamlMappingNode map, params string[] keys) =>
        new(map.Span, map.Entries.Where(entry => keys.Contains(entry.ScalarKey)), null, null);

    /// <summary>A snapshot without its units and items, which read as saved and take the constructors' values on restore.</summary>
    internal static BattleSnapshot WithoutEntities(BattleSnapshot snapshot) => snapshot with
    {
        Units = [],
        Items = [],
        ItemsSpecial = [],
        RecoverConditional = [],
        RecoverGuaranteed = [],
    };

    [Theory]
    [InlineData("UFO_SCOUT")]
    [InlineData("UFO_ALIENS")]
    [InlineData("UFO_ARMORY")]
    [InlineData("UFO_POWER")]
    public void GeneratedItemsWriteReloadAndConform(string deployment)
    {
        var (campaign, _) = BattleStartTests.Requested(ItemRules, deployment);
        BattleStartTests.Started(campaign);
        var battle = BattleStartTests.Battle(campaign);
        var snapshot = battle.Capture();
        Assert.NotEmpty(snapshot.Items);
        var written = OxceBattleSave.Write(snapshot);
        BattleSaveConformance.Check(written);
        var text = Emit(written);
        var content = campaign.Content;
        var restored = BattleState.Restore(OxceBattleSave.Read(Parse(text), content), content, Terrain(), campaign: BattleRestoreCampaign.From(campaign));
        Assert.Equivalent(BattleSaveUnitTests.Loaded(snapshot), restored.Capture(), strict: true);
        // The item counter: the highest ID plus one (the generator's gaps are not kept).
        var ids = battle.Items.Concat(battle.GuaranteedRecoveredItems).Select(static item => item.Id).ToArray();
        Assert.Equal(ids.Max() + 1, restored.NextItemId);
        Assert.Equal(battle.Units.Select(static unit => unit.SpecialWeaponIds.ToArray()), restored.Units.Select(static unit => unit.SpecialWeaponIds.ToArray()));
        BattleSaveUnitTests.AssertLoadedRewriteIsStable(restored, text, content, BattleRestoreCampaign.From(campaign));
    }

    [Fact]
    public void GeneratedBattlesHoldWhatTheReferenceGeneratorLeaves()
    {
        // UFO_ALIENS: aliens carry a gun loaded with a clip (setAmmoForSlot: the clip leaves its owner and section).
        var aliens = Battle("UFO_ALIENS");
        var gun = aliens.Items.First(static item => item.ItemType == "GUN" && item.AmmoSlots[0] is not null && item.AmmoSlots[0] != item.Id);
        var clip = Item(aliens, gun.AmmoSlots[0]!.Value);
        Assert.Equal((null, "", true), (clip.OwnerUnitId, clip.Slot, clip.IsAmmo));
        Assert.Equal([clip.Id, gun.Id, gun.Id, gun.Id], gun.AmmoSlots.Select(static slot => slot!.Value));
        // A rammer's RAM: owned, in no section, written to itemsSpecial (self-loaded, ammo -1).
        var scout = Fixture.Value.Battle;
        var ram = scout.Items.First(static item => item.ItemType == "RAM");
        Assert.Contains(ram.Id, scout.Units.Single(unit => unit.Id == ram.OwnerUnitId).SpecialWeaponIds);
        var writtenRam = WrittenItem(scout, ram.Id, "itemsSpecial");
        Assert.Equal(["id", "type", "owner", "ammoqty", "ammoItem", "ammoItemSlots"], Keys(writtenRam));
        // UFO_ARMORY: loadMAP's fuse timers go through setFuseTimer (a grenade's default trigger enables the fuse).
        var armory = Battle("UFO_ARMORY");
        Assert.All(armory.Items.Where(static item => item.ItemType == "GRENADE"), static grenade => Assert.Equal((3, true), (grenade.FuseTimer, grenade.FuseEnabled)));
        // UFO_POWER (noWeaponPile): the craft's pile goes to guaranteed recovery, written after the ambience.
        var power = Battle("UFO_POWER");
        Assert.NotEmpty(power.GuaranteedRecoveredItems);
        var keys = Keys(Written(power));
        Assert.Equal(Array.IndexOf(keys, "currentAmbienceDelay") + 1, Array.IndexOf(keys, "recoverGuaranteed"));
        Assert.DoesNotContain("recoverConditional", keys);
        Assert.Equal(Array.IndexOf(keys, "units") + 1, Array.IndexOf(keys, "items"));
        Assert.Equal(Array.IndexOf(keys, "items") + 1, Array.IndexOf(keys, "itemsSpecial"));

        static BattleState Battle(string deployment)
        {
            var (campaign, _) = BattleStartTests.Requested(ItemRules, deployment);
            BattleStartTests.Started(campaign);
            return BattleStartTests.Battle(campaign);
        }
    }

    [Fact]
    public void EveryKeyRoundTripsInSavesOrder()
    {
        var source = Parse(Root + Units + AllKeys);
        var battle = BattleState.Restore(OxceBattleSave.Read(source, Content), Content, Terrain(), campaign: Campaign);
        var written = OxceBattleSave.Write(battle.Capture(), source);
        BattleSaveConformance.Check(written);
        Assert.Equal(Normalized(Only(source, "items", "itemsSpecial")), Normalized(Only(written, "items", "itemsSpecial")));
        var medikit = Item(battle, 3);
        Assert.Equal((1000000, 1000000, 70, "STR_BELT", 1, 0, 1, 2, 0, true, true), (medikit.OwnerUnitId, medikit.PreviousOwnerUnitId,
            medikit.InventoryMoveCostPercent, medikit.Slot, medikit.SlotX, medikit.SlotY, medikit.PainKillerQuantity, medikit.HealQuantity,
            medikit.StimulantQuantity, medikit.DroppedOnAlienTurn, medikit.XcomProperty));
        Assert.Equal([new Oxce.Scripting.Globals.ScriptValueEntry("ITEM_TAG", "int", 5)], medikit.ScriptValues!.Capture());
        var mine = Item(battle, 5);
        Assert.Equal((1000001, (BattlePosition?)new BattlePosition(3, 2, 1), 1, true), (mine.CorpseUnitId, mine.Tile, mine.FuseTimer, mine.FuseEnabled));
        // The unit's inventory and special built-in item; the clip loaded into the gun.
        var alien = battle.Units.Single(static unit => unit.Id == 1000000);
        Assert.Equal([3, 6], battle.InventoryOf(alien).Select(static item => item.Id));
        Assert.Equal([9], alien.SpecialWeaponIds);
        Assert.True(Item(battle, 9).IsSpecialWeapon);
        Assert.True(Item(battle, 7).IsAmmo);
        Assert.Equal(10, battle.NextItemId);
        AssertRewriteIsStable(battle, Emit(written));
    }

    private static void AssertRewriteIsStable(BattleState restored, string text) =>
        BattleSaveUnitTests.AssertLoadedRewriteIsStable(restored, text, Content, Campaign);

    [Fact]
    public void TheConstructorGivesWhatTheSaveLeavesOut()
    {
        var battle = Restore(Units + "items:\n" +
            "  - {id: 1, type: CLIP}\n  - {id: 2, type: MEDKIT}\n  - {id: 3, type: HEAVY}\n  - {id: 4, type: GUN}\n  - {id: 5, type: BATTERY, ammoqty: 0}\n" +
            "  - {id: 6, type: HEAVY, inventoryMoveCost: {basePercent: 25}}\n  - {id: 7, type: HEAVY, inventoryMoveCost: 25}\n");
        // The clip size as the ammo, a medikit's quantities, the rule's inventory move cost, the slots without ammo self-filled.
        Assert.Equal(12, Item(battle, 1).AmmoQuantity);
        Assert.Equal((3, 4, 5), (Item(battle, 2).HealQuantity, Item(battle, 2).PainKillerQuantity, Item(battle, 2).StimulantQuantity));
        Assert.Equal((50, 25, 50), (Item(battle, 3).InventoryMoveCostPercent, Item(battle, 6).InventoryMoveCostPercent, Item(battle, 7).InventoryMoveCostPercent));
        Assert.Equal([null, 4, 4, 4], Item(battle, 4).AmmoSlots);
        Assert.Equal((-1, false, "", (BattlePosition?)null), (Item(battle, 1).FuseTimer, Item(battle, 1).FuseEnabled, Item(battle, 1).Slot, Item(battle, 1).Tile));
        // save: ammo when non-zero or rechargeable, a medikit's quantities always, the move cost when it differs from the rule's.
        Assert.Equal("12", Text(WrittenItem(battle, 1), "ammoqty"));
        Assert.Equal(["id", "type", "painKiller", "heal", "stimulant"], Keys(WrittenItem(battle, 2)));
        Assert.Equal(["id", "type"], Keys(WrittenItem(battle, 3)));
        Assert.Equal(["id", "type", "ammoItemSlots"], Keys(WrittenItem(battle, 4)));
        Assert.Equal(["-1", "4", "4", "4"], Ints(WrittenItem(battle, 4), "ammoItemSlots"));
        Assert.Equal("0", Text(WrittenItem(battle, 5), "ammoqty"));
        Assert.Equal("25", Text((YamlMappingNode)Value(WrittenItem(battle, 6), "inventoryMoveCost"), "basePercent"));
    }

    [Fact]
    public void SlotsAndGroundPositionsFollowTheLoad()
    {
        var diagnostics = new DiagnosticCollector(10);
        var snapshot = Read(Units + "items:\n" +
            "  - {id: 1, type: CLIP, inventoryslot: STR_POCKET, position: [2, 2, 0]}\n" +
            "  - {id: 2, type: CLIP, inventoryslot: STR_GROUND}\n" +
            "  - {id: 3, type: CLIP, inventoryslot: STR_GROUND, position: [-1, 2, 0]}\n" +
            "  - {id: 4, type: CLIP, inventoryslot: STR_BELT, inventoryX: 1, position: [9, 9, 9]}\n" +
            "  - {id: 5, type: CLIP, owner: 1000000, inventoryslot: STR_LEFT_HAND, inventoryX: 3, inventoryY: 4, position: oops}\n" +
            "  - {id: 6, type: CLIP, inventoryslot: NULL, position: [1, 1, 1]}\n", diagnostics);
        // An unknown section is the ground (getInventoryGround), with the port's warning.
        Assert.Contains(diagnostics.Snapshot(), static item => item.Code == OxceBattleSave.SkippedCode && item.Message.Contains("STR_POCKET", StringComparison.Ordinal));
        var battle = BattleState.Restore(snapshot, Content, Terrain(), campaign: Campaign);
        Assert.Equal(("STR_GROUND", (BattlePosition?)new BattlePosition(2, 2, 0)), (Item(battle, 1).Slot, Item(battle, 1).Tile));
        // A ground item without a position, or at x -1, lies nowhere; held by nothing, which a restored battle allows.
        Assert.Equal(("STR_GROUND", (BattlePosition?)null), (Item(battle, 2).Slot, Item(battle, 2).Tile));
        Assert.Null(Item(battle, 3).Tile);
        // Only a ground item's position is read (not even checked elsewhere).
        Assert.Equal(("STR_BELT", 1, (BattlePosition?)null), (Item(battle, 4).Slot, Item(battle, 4).SlotX, Item(battle, 4).Tile));
        Assert.Equal(("STR_LEFT_HAND", 3, 4), (Item(battle, 5).Slot, Item(battle, 5).SlotX, Item(battle, 5).SlotY));
        Assert.Equal(("", (BattlePosition?)null), (Item(battle, 6).Slot, Item(battle, 6).Tile));
        // save: the grid position only in an inventory grid, the position only on a tile.
        Assert.Equal(["id", "type", "inventoryslot", "position", "ammoqty"], Keys(WrittenItem(battle, 1)));
        Assert.Equal(["id", "type", "inventoryslot", "inventoryX", "inventoryY", "ammoqty"], Keys(WrittenItem(battle, 4)));
        Assert.Equal(["id", "type", "owner", "inventoryslot", "ammoqty"], Keys(WrittenItem(battle, 5)));
        Assert.Equal(["id", "type", "ammoqty"], Keys(WrittenItem(battle, 6)));
        // getTile of a position outside the map is null, which the reference dereferences.
        Assert.Contains("lies at", Failure(() => Restore(Units + "items:\n  - {id: 1, type: CLIP, inventoryslot: STR_GROUND, position: [4, 0, 0]}\n")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void OwnersAndCorpseUnitsAreFoundAmongTheLoadedUnits()
    {
        var battle = Restore(Units + "items:\n" +
            "  - {id: 1, type: CLIP, owner: 1000002, previousOwner: 1000002, unit: 1000002, inventoryslot: STR_BELT}\n" +
            "  - {id: 2, type: CLIP, owner: 77, previousOwner: 1000001, unit: 1000000, inventoryslot: STR_BELT}\n" +
            "  - {id: 3, type: CLIP, owner: 1000000, inventoryslot: STR_BELT}\n");
        // findUnitById: a unit the load left out, or no unit at all, is none; the items of a skipped unit are held by nothing.
        Assert.Equal(((int?)null, (int?)null, (int?)null), (Item(battle, 1).OwnerUnitId, Item(battle, 1).PreviousOwnerUnitId, Item(battle, 1).CorpseUnitId));
        Assert.Equal(((int?)null, (int?)1000001, (int?)1000000), (Item(battle, 2).OwnerUnitId, Item(battle, 2).PreviousOwnerUnitId, Item(battle, 2).CorpseUnitId));
        // setOwner then setPreviousOwner: the saved previous owner, none when absent.
        Assert.Equal((1000000, (int?)null), (Item(battle, 3).OwnerUnitId, Item(battle, 3).PreviousOwnerUnitId));
        Assert.Equal(["id", "type", "inventoryslot", "inventoryX", "inventoryY", "ammoqty"], Keys(WrittenItem(battle, 1)));
    }

    [Fact]
    public void SpecialBuiltInItemsLoadAndTheUnitsSecondPassAddsWhatTheyLack()
    {
        // addLoadedSpecialWeapon: four slots; a fifth stays owned in no section (the reference logs it).
        var five = string.Concat(Enumerable.Range(1, 5).Select(static id => $"  - {{id: {id}, type: CLIP, owner: 1000000}}\n"));
        var battle = Restore(Units + "items: []\nitemsSpecial:\n" + five);
        var alien = battle.Units.Single(static unit => unit.Id == 1000000);
        Assert.Equal([1, 2, 3, 4], alien.SpecialWeaponIds);
        Assert.True(Item(battle, 5).IsSpecialWeapon);
        Assert.Equal(5, ((YamlSequenceNode)Value(Written(battle), "itemsSpecial")).Items.Count);
        // An owned item in no section listed under items is a special built-in item, written to itemsSpecial.
        var moved = Restore(Units + "items:\n  - {id: 1, type: CLIP, owner: 1000000}\n");
        Assert.Equal([1], moved.Units.Single(static unit => unit.Id == 1000000).SpecialWeaponIds);
        Assert.Empty(((YamlSequenceNode)Value(Written(moved), "items")).Items);
        Assert.Single(((YamlSequenceNode)Value(Written(moved), "itemsSpecial")).Items);

        const string bombers = "units:\n" +
            "  - {id: 1000000, genUnitType: BOMBER, genUnitArmor: BOMB_ARMOR, faction: 1, status: 0, position: [1, 1, 0]}\n" +
            "  - {id: 1000001, genUnitType: BOMBER, genUnitArmor: BOMB_ARMOR, faction: 1, status: 0, position: [2, 2, 0]}\n" +
            "  - {id: 1000002, genUnitType: BOMBER, genUnitArmor: BOMB_ARMOR, faction: 1, status: 6}\n";
        // setSpecialWeapon(save, true): a unit saved with special items gets the armor's self-destruct item it lacks, in the
        // first slot (the item there stays owned); one saved without gets the new unit's (older saves); a dead unit none.
        var loaded = Restore(bombers + "items: []\nitemsSpecial:\n  - {id: 7, type: RAM, owner: 1000000, ammoqty: -1}\n");
        var (first, second, dead) = (loaded.Units[0], loaded.Units[1], loaded.Units[2]);
        Assert.Equal([8], first.SpecialWeaponIds);
        Assert.Equal(("BOMB", 1000000), (Item(loaded, 8).ItemType, Item(loaded, 8).OwnerUnitId!.Value));
        Assert.True(Item(loaded, 7).IsSpecialWeapon);
        Assert.Equal([9, 10], second.SpecialWeaponIds);
        Assert.Equal(["RAM", "BOMB"], second.SpecialWeaponIds.Select(id => Item(loaded, id).ItemType));
        Assert.Empty(dead.SpecialWeaponIds);
        Assert.Equal(11, loaded.NextItemId);
        Assert.Equal([7, 8, 9, 10], loaded.Items.Select(static item => item.Id));
        // A unit saved with its self-destruct item gets nothing more.
        var complete = Restore(bombers.Replace("status: 6", "status: 10", StringComparison.Ordinal) + "itemsSpecial:\n" +
            "  - {id: 7, type: RAM, owner: 1000000, ammoqty: -1}\n  - {id: 8, type: BOMB, owner: 1000000}\n" +
            "  - {id: 9, type: RAM, owner: 1000001, ammoqty: -1}\n  - {id: 10, type: BOMB, owner: 1000001}\n");
        Assert.Equal([[7, 8], [9, 10], []], complete.Units.Select(static unit => unit.SpecialWeaponIds.ToArray()));
        Assert.Equal(11, complete.NextItemId);
    }

    [Fact]
    public void AmmoSlotsAndTheOlderAmmoItemLoadThroughSetAmmoForSlot()
    {
        var battle = Restore(Units + "items:\n" +
            // ammoItemSlots wins over ammoItem; an ID that names no item is ignored.
            "  - {id: 1, type: GUN, owner: 1000000, inventoryslot: STR_RIGHT_HAND, ammoItem: 99, ammoItemSlots: [2]}\n" +
            "  - {id: 2, type: CLIP, owner: 1000000, inventoryslot: STR_BELT}\n" +
            "  - {id: 3, type: GUN, ammoItemSlots: [42, 4, 4]}\n" +
            "  - {id: 4, type: CLIP, inventoryslot: STR_GROUND, position: [0, 1, 0]}\n" +
            // The older ammoItem without ammoItemSlots loads slot 0.
            "  - {id: 5, type: GUN, ammoItem: 6}\n" +
            "  - {id: 6, type: CLIP}\n" +
            // A slot the weapon fills itself, and an item that is no weapon with ammo, take nothing.
            "  - {id: 7, type: CLIP, ammoItemSlots: [8]}\n" +
            "  - {id: 8, type: CLIP, inventoryslot: STR_GROUND, position: [0, 2, 0]}\n");
        // setAmmoForSlot: moveToOwner(nullptr) (the owner becomes the previous owner), no section, ammo.
        var clip = Item(battle, 2);
        Assert.Equal(((int?)null, (int?)1000000, "", true), (clip.OwnerUnitId, clip.PreviousOwnerUnitId, clip.Slot, clip.IsAmmo));
        Assert.Equal([2, 1, 1, 1], Item(battle, 1).AmmoSlots.Select(static slot => slot!.Value));
        Assert.Equal([null, 3, 3, 3], Item(battle, 3).AmmoSlots);
        Assert.False(Item(battle, 4).IsAmmo);
        Assert.Equal((BattlePosition?)new BattlePosition(0, 1, 0), Item(battle, 4).Tile);
        Assert.Equal(6, Item(battle, 5).AmmoSlots[0]);
        Assert.Equal(((int?)null, (BattlePosition?)new BattlePosition(0, 2, 0)), (Item(battle, 7).AmmoSlots[0], Item(battle, 8).Tile));
        // save writes ammoItem and the slots up to the last filled one.
        Assert.Equal("6", Text(WrittenItem(battle, 5), "ammoItem"));
        Assert.Equal(["6", "5", "5", "5"], Ints(WrittenItem(battle, 5), "ammoItemSlots"));
        // itemIndex.at(ammoItem): an older ammoItem that names no item throws.
        Assert.Contains("'ammoItem' names item 9", Failure(() => Restore(Units + "items:\n  - {id: 1, type: GUN, ammoItem: 9}\n")), StringComparison.Ordinal);
    }

    [Fact]
    public void OxcFuseTimersGoThroughSetFuseTimer()
    {
        var battle = Restore("items:\n" +
            "  - {id: 1, type: GRENADE, fuseTimer: 2}\n  - {id: 2, type: MINE, fuseTimer: 2}\n  - {id: 3, type: GRENADE, fuseTimer: 2, fuseEnabed: false}\n" +
            "  - {id: 4, type: GRENADE, fuseEnabed: true}\n  - {id: 5, type: GRENADE, fuseTimer: -1}\n");
        // The default trigger enables a primed fuse; a proximity trigger does not; fuseEnabed is read after it.
        Assert.Equal([(2, true), (2, false), (2, false), (-1, true), (-1, false)],
            battle.Items.Select(static item => (item.FuseTimer, item.FuseEnabled)));
        Assert.Equal(["id", "type", "fuseTimer", "fuseEnabed"], Keys(WrittenItem(battle, 1)));
        Assert.Equal(["id", "type", "fuseEnabed"], Keys(WrittenItem(battle, 4)));
        Assert.Equal(["id", "type"], Keys(WrittenItem(battle, 5)));
    }

    [Fact]
    public void RecoveryListsAndTheItemOrderFollowTheLoad()
    {
        var battle = Restore(Units + "items:\n  - {id: 9, type: CLIP}\n  - {id: 2, type: CLIP}\n" +
            "itemsSpecial:\n  - {id: 4, type: RAM, owner: 1000000}\n" +
            "recoverConditional:\n  - {id: 7, type: CLIP}\n  - {id: 3, type: GUN, ammoItemSlots: [11]}\n" +
            "recoverGuaranteed:\n  - {id: 12, type: CLIP}\n  - {id: 11, type: CLIP}\n");
        // Each list sorted by ID (items and itemsSpecial share the battle's); ammo is found across the lists.
        Assert.Equal([2, 4, 9], battle.Items.Select(static item => item.Id));
        Assert.Equal([3, 7], battle.ConditionalRecoveredItems.Select(static item => item.Id));
        Assert.Equal([11, 12], battle.GuaranteedRecoveredItems.Select(static item => item.Id));
        Assert.Equal(11, Item(battle, 3).AmmoSlots[0]);
        Assert.Equal(13, battle.NextItemId);
        var keys = Keys(Written(battle));
        Assert.Equal(Array.IndexOf(keys, "recoverGuaranteed") + 1, Array.IndexOf(keys, "recoverConditional"));
        // Without items, neither items nor itemsSpecial is written, and the counter is 1; negative IDs keep it at 1.
        var empty = Restore(Units);
        Assert.Equal(1, empty.NextItemId);
        Assert.DoesNotContain("items", Keys(Written(empty)));
        Assert.DoesNotContain("itemsSpecial", Keys(Written(empty)));
        Assert.Equal(1, Restore("items:\n  - {id: -5, type: CLIP}\n").NextItemId);
        // A repeated ID, in one list or across them, is a corrupted save.
        Assert.Contains("Battle item ID 2 is saved twice", Failure(() => Restore("items:\n  - {id: 2, type: CLIP}\nrecoverGuaranteed:\n  - {id: 2, type: CLIP}\n")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemWhoseRuleIsGoneIsLeftOut()
    {
        var diagnostics = new DiagnosticCollector(10);
        // The type is checked before the ID is read.
        var snapshot = Read("items:\n  - {type: GONE}\n  - {id: 2, type: CLIP, ammoItem: 1}\nitemsSpecial:\n  - {id: 1, type: GONE}\n", diagnostics);
        Assert.Equal([2], snapshot.Items.Select(static item => item.Id));
        Assert.Empty(snapshot.ItemsSpecial);
        Assert.Equal(2, diagnostics.Snapshot().Count(static item => item.Code == OxceBattleSave.SkippedCode));
        // A legacy ammoItem naming the skipped item is then a corrupted save, as itemIndex.at throws.
        Assert.Contains("names item 1", Failure(() => BattleState.Restore(Read("items:\n  - {id: 2, type: GUN, ammoItem: 1}\n  - {id: 1, type: GONE}\n"),
            Content, Terrain(), campaign: Campaign)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("items:\n  - 3\n", "'items' holds an entry that is not a mapping")]
    [InlineData("items:\n  - {id: 1}\n", "'type' is missing")]
    [InlineData("items:\n  - {type: CLIP}\n", "'id' is missing")]
    [InlineData("recoverGuaranteed:\n  - {type: [CLIP], id: 1}\n", "'type'")]
    [InlineData("items:\n  - {id: x, type: CLIP}\n", "'id'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, owner: x}\n", "'owner'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, previousOwner: x}\n", "'previousOwner'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, unit: x}\n", "'unit'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, inventoryslot: [a]}\n", "'inventoryslot'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, inventoryX: x}\n", "'inventoryX'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, inventoryY: x}\n", "'inventoryY'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, inventoryMoveCost: {basePercent: x}}\n", "'inventoryMoveCost'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, inventoryslot: STR_GROUND, position: [1, 1]}\n", "'position'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, ammoqty: x}\n", "'ammoqty'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, ammoItem: x}\n", "'ammoItem'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, ammoItemSlots: [1, x]}\n", "'ammoItemSlots'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, painKiller: x}\n", "'painKiller'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, heal: x}\n", "'heal'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, stimulant: x}\n", "'stimulant'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, fuseTimer: x}\n", "'fuseTimer'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, fuseEnabed: x}\n", "'fuseEnabed'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, droppedOnAlienTurn: x}\n", "'droppedOnAlienTurn'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, XCOMProperty: x}\n", "'XCOMProperty'")]
    [InlineData("items:\n  - {id: 1, type: CLIP, tags: {ITEM_TAG: x}}\n", "'tags'")]
    public void UnreadableItemsNameTheirKey(string items, string expected) =>
        Assert.Contains(expected, Failure(() => Read(items)), StringComparison.Ordinal);

    [Fact]
    public void ItemTagsAreReadSkippedOrRefusedAsTheRootsAre()
    {
        var diagnostics = new DiagnosticCollector(10);
        var item = Read("items:\n  - {id: 1, type: CLIP, tags: {ITEM_TAG: 3, GONE_TAG: 1}}\n", diagnostics).Items[0];
        Assert.Equal([new Oxce.Scripting.Globals.ScriptValueEntry("ITEM_TAG", "int", 3)], item.ScriptValues);
        Assert.Contains(diagnostics.Snapshot(), static entry => entry.Message.Contains("GONE_TAG", StringComparison.Ordinal));
        Assert.Equal(OxceBattleSave.UnsupportedTagType("ITEM_LIST", "RuleList"), Failure(() => Read("items:\n  - {id: 1, type: CLIP, tags: {ITEM_LIST: 1}}\n")));
    }

    [Fact]
    public void UnknownItemKeysSurviveALoadedRewrite()
    {
        const string items = "items:\n  - {id: 1, type: CLIP, portFuture: [1], ammoqty: 3, painKiller: 9}\n";
        var source = Parse(Root + items);
        var battle = BattleState.Restore(OxceBattleSave.Read(source, Content), Content, Terrain(), campaign: Campaign);
        var item = (YamlMappingNode)((YamlSequenceNode)Value(OxceBattleSave.Write(battle.Capture(), source), "items")).Items[0];
        // The unknown key stays in place; a key save does not write for this item (a clip's painKiller) is removed.
        Assert.Equal(["id", "type", "portFuture", "ammoqty"], Keys(item));
    }

    [Fact]
    public void ARestoredBattleValidatesItsItemsHolders()
    {
        // Two weapons loaded with one clip give it two holders.
        Assert.Contains("has 2 holders", Failure(() => Restore("items:\n  - {id: 1, type: GUN, ammoItemSlots: [3]}\n  - {id: 2, type: GUN, ammoItemSlots: [3]}\n" +
            "  - {id: 3, type: CLIP}\n")), StringComparison.Ordinal);
        // An owned item that also lies on a tile (the reference puts it in both places; ADR 0039).
        Assert.Contains("has 2 holders", Failure(() => Restore(Units +
            "items:\n  - {id: 1, type: CLIP, owner: 1000000, inventoryslot: STR_GROUND, position: [0, 0, 0]}\n")), StringComparison.Ordinal);
        // A weapon of the battle loaded with an item of a recovery list.
        Assert.Contains("which is no loaded ammo of the battle", Failure(() => Restore("items:\n  - {id: 1, type: GUN, ammoItemSlots: [2]}\n" +
            "recoverGuaranteed:\n  - {id: 2, type: CLIP}\n")), StringComparison.Ordinal);
        // An ID of INT_MAX leaves the counter no next ID.
        Assert.Contains("leaves no ID for the next item", Failure(() => Restore("items:\n  - {id: 2147483647, type: CLIP}\n")), StringComparison.Ordinal);
        // setSpecialWeapon throws for a special built-in firearm without a clip size: a corrupted save.
        Assert.Contains("give it a clipSize", Failure(() => Restore(
            "units:\n  - {id: 1000000, genUnitType: BADMELEE, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, status: 0, position: [1, 1, 0]}\n")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryListItemsKeepTheirTilesAndOwners()
    {
        var battle = Restore(Units + "items:\n  - {id: 1, type: CLIP}\n" +
            "recoverGuaranteed:\n  - {id: 2, type: CLIP, inventoryslot: STR_GROUND, position: [3, 3, 1]}\n" +
            "recoverConditional:\n  - {id: 3, type: CLIP, owner: 1000000}\n  - {id: 4, type: CLIP, owner: 1000000, inventoryslot: STR_BELT}\n");
        // getTile(pos)->addItem: the tile holds the item, so it is not void and is saved.
        Assert.Equal((BattlePosition?)new BattlePosition(3, 3, 1), Item(battle, 2).Tile);
        Assert.Contains(battle.Capture().Tiles, static tile => tile.Index == 3 + 3 * 4 + 1 * 16);
        // An owned item of a recovery list keeps its owner; in no section it is the owner's special item (ADR 0039: it
        // does not join the inventory, which is the battle's items).
        var alien = battle.Units.Single(static unit => unit.Id == 1000000);
        Assert.Equal((1000000, true), (Item(battle, 3).OwnerUnitId!.Value, Item(battle, 3).IsSpecialWeapon));
        Assert.Equal([3], alien.SpecialWeaponIds);
        Assert.Equal(1000000, Item(battle, 4).OwnerUnitId);
        Assert.Empty(battle.InventoryOf(alien));
    }

    [Fact]
    public void SetFuseTimerFollowsTheRulesFuseTriggers()
    {
        var battle = Restore("items:\n  - {id: 1, type: THROWN, fuseTimer: 2}\n  - {id: 2, type: INERT, fuseTimer: 2}\n  - {id: 3, type: GRENADE, fuseTimer: 0}\n");
        // A throw trigger, or no default behavior, leaves a primed fuse off; the default enables it from 0.
        Assert.Equal([(2, false), (2, false), (0, true)], battle.Items.Select(static item => (item.FuseTimer, item.FuseEnabled)));
    }

    [Fact]
    public void TheUnitsSecondPassGivesPsiAndSoldierSpecialWeapons()
    {
        // A mind-controlled psionic (originally hostile) gets its psi weapon; one originally neutral does not.
        var battle = Restore("units:\n" +
            "  - {id: 1000000, genUnitType: PSIONIC, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 0, originalFaction: 1, status: 0, position: [1, 1, 0]}\n" +
            "  - {id: 1000001, genUnitType: PSIONIC, genUnitArmor: SMALL_VEHICLE_ARMOR, faction: 1, originalFaction: 2, status: 0, position: [2, 2, 0]}\n");
        Assert.Equal(["AMP"], battle.Units[0].SpecialWeaponIds.Select(id => Item(battle, id).ItemType));
        Assert.Empty(battle.Units[1].SpecialWeaponIds);
        // A soldier saved without special items gets the soldier rule's (an older save).
        var (campaign, generated) = Fixture.Value;
        var soldier = generated.Units.First(static unit => unit.SoldierId is not null);
        Assert.Equal(["RAM"], soldier.SpecialWeaponIds.Select(id => generated.FindItem(id)!.ItemType));
        var snapshot = generated.Capture();
        var stripped = snapshot with { ItemsSpecial = [.. snapshot.ItemsSpecial.Where(item => item.OwnerId != soldier.Id)] };
        var restored = BattleState.Restore(stripped, campaign.Content, Terrain(), campaign: BattleRestoreCampaign.From(campaign));
        var created = Assert.Single(restored.Units.Single(unit => unit.Id == soldier.Id).SpecialWeaponIds);
        Assert.Equal("RAM", Item(restored, created).ItemType);
        Assert.True(created >= snapshot.Items.Concat(snapshot.ItemsSpecial).Concat(snapshot.RecoverGuaranteed).Max(static item => item.Id));
    }
}
