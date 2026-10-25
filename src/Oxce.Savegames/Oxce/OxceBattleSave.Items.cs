using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Battles;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using static Oxce.Savegames.Oxce.OxceSaveAdapter;

namespace Oxce.Savegames.Oxce;

/// <summary>
/// The battle's items (Phase 7 branch 2 S4): <c>SavedBattleGame::load</c>'s first item pass over <c>items</c>,
/// <c>recoverConditional</c>, <c>recoverGuaranteed</c> and <c>itemsSpecial</c> (the type, the ID and the keys the passes
/// read: <c>owner</c>, <c>previousOwner</c>, <c>unit</c>, a ground item's <c>position</c>, <c>ammoItemSlots</c> or
/// <c>ammoItem</c>), <c>BattleItem::load</c> and <c>save</c>.
/// </summary>
public static partial class OxceBattleSave
{
    // RuleItem's BT_MEDIKIT and AmmoSlotMax.
    private const int MedikitBattleType = 6;
    private const int AmmoSlotCount = 4;

    private static ReadOnlyCollection<BattleItemSnapshot> ReadItems(YamlMappingNode battle, string key, RuntimeContent content, IDiagnosticSink? diagnostics)
    {
        var rules = content.RuntimeRules;
        var items = new List<BattleItemSnapshot>();
        foreach (var node in Children(battle, key))
        {
            // itemReader["type"].readVal<std::string>() and ["id"].readVal<int>(): required, as is a mapping to hold them.
            var map = node as YamlMappingNode ?? throw new InvalidDataException($"Battle save '{key}' holds an entry that is not a mapping.");
            var type = map.TryGet("type", out var typeNode) ? Value(typeNode!, "type", YamlValueReader.ReadString)
                : throw new InvalidDataException($"Battle save '{key}' item 'type' is missing.");
            // if (!mod->getItem(type)) { Log(LOG_ERROR) << "Failed to load item " << type; continue; }: before the ID is read.
            if (!rules.Items.TryGet(type, out var handle) || !rules.Battle.Equipment.Items.TryGetValue(type, out var battleRule))
            {
                Skipped(diagnostics, map, $"Battle save '{key}' item '{type}' is not an item of the mods; it is left out.");
                continue;
            }
            var id = map.TryGet("id", out var idNode) ? Value(idNode!, "id", YamlValueReader.ReadInt32)
                : throw new InvalidDataException($"Battle save '{key}' item '{type}' 'id' is missing.");
            items.Add(ReadItem(map, id, type, rules.Items[handle].Value, battleRule, content, diagnostics));
        }
        return Array.AsReadOnly(items.ToArray());
    }

    /// <summary>BattleItem::load (useIndex: the first of repeated keys) and the keys the load's passes read.</summary>
    private static BattleItemSnapshot ReadItem(YamlMappingNode map, int id, string type, RuntimeItemRule rule, RuntimeBattleItem battleRule,
        RuntimeContent content, IDiagnosticSink? diagnostics)
    {
        var inventories = content.RuntimeRules.Battle.Equipment.Inventories;
        // readVal<std::string>("NULL"): none for NULL, getInventory(slot) else, and the ground for a section the mods lack.
        var slotName = String(map, "inventoryslot", "NULL");
        var slot = slotName == "NULL" ? null : inventories.FirstOrDefault(inventory => inventory.Id == slotName);
        if (slotName != "NULL" && slot is null)
        {
            slot = inventories.FirstOrDefault(static inventory => inventory.Id == GroundInventory)
                ?? throw new InvalidDataException($"Battle save item {id} is in unknown inventory section '{slotName}', and the mods have no {GroundInventory}.");
            Skipped(diagnostics, map, $"Battle save item {id} is in unknown inventory section '{slotName}'; it goes to the ground.");
        }
        // The first pass reads a ground item's position by readVal(Position(-1, -1, -1)); x -1 is none.
        BattlePosition? position = null;
        if (slot?.Type == RuntimeBattleInventory.Ground && map.TryGet("position", out var positionNode) &&
            Position(positionNode!, "position") is var saved && saved.X != -1)
            position = saved;
        // inventoryMoveCost: cost["basePercent"].readVal(rule's): none unless the node is a mapping holding the key.
        int? moveCost = map.TryGet("inventoryMoveCost", out var costNode) && costNode is YamlMappingNode cost && cost.TryGet("basePercent", out var percent)
            ? Value(percent!, "inventoryMoveCost", YamlValueReader.ReadInt32) : null;
        // The second pass: ammoItemSlots (reader[i].readVal(-1) for the four slots), else ammoItem (readVal(0)).
        IReadOnlyList<int>? ammoSlots = null;
        int? ammoItem;
        if (map.TryGet("ammoItemSlots", out var slotsNode))
        {
            ammoSlots = Array.AsReadOnly(Children(slotsNode).Take(AmmoSlotCount)
                .Select(static node => Value(node, "ammoItemSlots", YamlValueReader.ReadInt32)).ToArray());
            // Not read beside ammoItemSlots; kept for the rewrite when it is an integer.
            ammoItem = map.TryGet("ammoItem", out var ignored) && YamlValueReader.TryReadInt32(ignored!, out var value) ? value : null;
        }
        else
        {
            ammoItem = map.TryGet("ammoItem", out var ammoNode) ? Value(ammoNode!, "ammoItem", YamlValueReader.ReadInt32) : null;
        }
        return new BattleItemSnapshot(id, type)
        {
            OwnerId = Int(map, "owner", -1),
            PreviousOwnerId = Int(map, "previousOwner", -1),
            UnitId = Int(map, "unit", -1),
            InventoryMoveCostPercent = moveCost,
            RuleInventoryMoveCostPercent = battleRule.InventoryMoveCostPercent,
            Slot = slot?.Id ?? "",
            SlotIsGrid = slot?.Type == RuntimeBattleInventory.Slot,
            SlotX = Int(map, "inventoryX", 0),
            SlotY = Int(map, "inventoryY", 0),
            Position = position,
            AmmoQuantity = OptionalInt(map, "ammoqty"),
            AmmoRechargeable = battleRule.AmmoRechargeable,
            AmmoItem = ammoItem,
            AmmoItemSlots = ammoSlots,
            Medikit = rule.BattleType == MedikitBattleType,
            PainKiller = OptionalInt(map, "painKiller"),
            Heal = OptionalInt(map, "heal"),
            Stimulant = OptionalInt(map, "stimulant"),
            FuseTimer = OptionalInt(map, "fuseTimer"),
            FuseEnabled = OptionalBool(map, "fuseEnabed"),
            DroppedOnAlienTurn = Bool(map, "droppedOnAlienTurn", false),
            XcomProperty = Bool(map, "XCOMProperty", false),
            ScriptValues = Tags(map, content, diagnostics, BattleItemSnapshot.ScriptOwner, $"Battle save item {id} tag"),
        };
    }

    // Mod::getInventoryGround.
    private const string GroundInventory = "STR_GROUND";

    /// <summary>
    /// The items, each over its source node (the first source item of its ID in any of the four lists) so keys no reader
    /// reads survive.
    /// </summary>
    private static YamlSequenceNode WriteItems(IReadOnlyList<BattleItemSnapshot> items, Dictionary<int, YamlMappingNode> sources) =>
        Sequence(items.Select(item => (YamlNode)WriteItem(item, sources.GetValueOrDefault(item.Id))));

    private static Dictionary<int, YamlMappingNode> ItemSources(YamlMappingNode? source)
    {
        var sources = new Dictionary<int, YamlMappingNode>();
        if (source is null) return sources;
        foreach (var key in ItemKeys)
            foreach (var node in Children(source, key))
                if (node is YamlMappingNode map && map.TryGet("id", out var id) && YamlValueReader.TryReadInt32(id!, out var value))
                    sources.TryAdd(value, map);
        return sources;
    }

    private static readonly string[] ItemKeys = ["items", "recoverConditional", "recoverGuaranteed", "itemsSpecial"];

    /// <summary>
    /// BattleItem::save, in its order: the units only when set, the inventory move cost only when it differs from the rule's,
    /// the grid position only in an inventory grid, the position only on a tile, the ammo quantity when non-zero or
    /// rechargeable, the ammo slots up to the last filled one, a medikit's quantities, the fuse only when primed or enabled.
    /// </summary>
    private static YamlMappingNode WriteItem(BattleItemSnapshot item, YamlMappingNode? source)
    {
        static YamlNode? Flag(bool value) => value ? Boolean(true) : null;
        static YamlNode? Unit(int id) => id != -1 ? Integer(id) : null;
        var slots = item.AmmoItemSlots ?? [];
        var last = slots.Count - 1;
        while (last >= 0 && slots[last] == -1) last--;
        var costSource = source is not null && source.TryGet("inventoryMoveCost", out var cost) ? cost as YamlMappingNode : null;
        return Overlay(source,
        [
            Pair("id", Integer(item.Id)),
            Pair("type", Scalar(item.ItemType)),
            Pair("owner", Unit(item.OwnerId)),
            Pair("previousOwner", Unit(item.PreviousOwnerId)),
            Pair("unit", Unit(item.UnitId)),
            Pair("inventoryMoveCost", item.InventoryMoveCostPercent is { } percent && percent != item.RuleInventoryMoveCostPercent
                ? Overlay(costSource, [Pair("basePercent", Integer(percent))]) : null),
            Pair("inventoryslot", item.Slot.Length != 0 ? Scalar(item.Slot) : null),
            Pair("inventoryX", item.Slot.Length != 0 && item.SlotIsGrid ? Integer(item.SlotX) : null),
            Pair("inventoryY", item.Slot.Length != 0 && item.SlotIsGrid ? Integer(item.SlotY) : null),
            Pair("position", item.Position is { } position ? Sequence([Integer(position.X), Integer(position.Y), Integer(position.Z)]) : null),
            Pair("ammoqty", item.AmmoQuantity is { } quantity && (quantity != 0 || item.AmmoRechargeable) ? Integer(quantity) : null),
            Pair("ammoItem", item.AmmoItem is { } ammo ? Integer(ammo) : null),
            Pair("ammoItemSlots", last >= 0 ? Sequence(slots.Take(last + 1).Select(static slot => (YamlNode)Integer(slot))) : null),
            Pair("painKiller", item.Medikit && item.PainKiller is { } painKiller ? Integer(painKiller) : null),
            Pair("heal", item.Medikit && item.Heal is { } heal ? Integer(heal) : null),
            Pair("stimulant", item.Medikit && item.Stimulant is { } stimulant ? Integer(stimulant) : null),
            Pair("fuseTimer", item.FuseTimer is { } fuse && fuse != -1 ? Integer(fuse) : null),
            Pair("fuseEnabed", Flag(item.FuseEnabled == true)),
            Pair("droppedOnAlienTurn", Flag(item.DroppedOnAlienTurn)),
            Pair("XCOMProperty", Flag(item.XcomProperty)),
            Pair("tags", ScriptValues(Tags(item.ScriptValues))),
        ]);
    }
}
