using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>
/// BattlescapeGenerator::deployXCOM for a landed craft: the craft's vehicles and soldiers become player units in the
/// reference's order and are placed on the craft's start tiles, at the craft rule's deployment points, or on X-COM spawn
/// nodes when no craft map was placed. Equipment, unit statistics and the base defense deployment follow in later slices.
/// </summary>
internal sealed class XcomDeployment
{
    internal const string LargeUnitPlacement =
        "Placing a unit larger than one tile needs the pathfinding direction checks (Phase 7 branch 1 S4b).";
    internal const string ArmorChanges =
        "X-COM deployment with armor transformations or armor and equipment restrictions arrives in Phase 7 branch 1 S4b.";
    internal const string NearFriendPlacement =
        "Placing X-COM units next to each other without spawn nodes arrives in Phase 7 branch 1 S4b.";

    /// <summary>Pathfinding::INVALID_MOVE_COST.</summary>
    private const int InvalidMoveCost = 255;

    /// <summary>SpecialTileType START_POINT.</summary>
    private const int StartPoint = 1;

    /// <summary>NodeRank NR_XCOM.</summary>
    private const int XcomNodeRank = 1;

    /// <summary>Node::TYPE_FLYING and TYPE_SMALL.</summary>
    private const int FlyingNode = 0x01;
    private const int SmallNode = 0x02;

    private readonly BattleState _battle;
    private readonly BattleMap _map;
    private readonly BattleMapResult _generated;
    private readonly RuntimeRuleCatalog _rules;
    private readonly IRandomSource _random;
    private int _unitSequence = BattleState.MaximumSoldierId;

    private XcomDeployment(BattleState battle, BattleMapResult generated, RuntimeRuleCatalog rules, IRandomSource random)
    {
        _battle = battle;
        _map = battle.Map;
        _generated = generated;
        _rules = rules;
        _random = random;
    }

    public static void Deploy(CampaignState campaign, CampaignDeploymentRequest request, BattleState battle, BattleMapResult generated,
        RuntimeStartingCondition? startingCondition, string enviroEffects)
    {
        var rules = campaign.Content.RuntimeRules;
        if (startingCondition is { RestrictsArmor: true } or { RestrictsEquipment: true } ||
            rules.Battle.EnviroArmorTransformations.TryGetValue(enviroEffects, out var transformations) && transformations.Count != 0)
            throw new MapGenerationException(ArmorChanges);
        new XcomDeployment(battle, generated, rules, campaign.Random).Run(campaign, request);
    }

    private void Run(CampaignState campaign, CampaignDeploymentRequest request)
    {
        var owner = campaign.BaseStates.Find(candidate => candidate.Id == request.BaseId)
            ?? throw new MapGenerationException($"The deployment request's base {request.BaseId} no longer exists.");
        var extended = _rules.Campaign.ExtendedHwpLoadOrder;
        var vehicles = request.Vehicles.Select(Vehicle).ToArray();
        // 2x2 vehicles first (all vehicles without the extended order).
        foreach (var vehicle in vehicles)
            if (vehicle.Armor.Size > 1 || !extended) AddVehicle(vehicle);
        var soldiers = owner.Soldiers.Where(soldier => request.Soldiers.Contains(soldier.Id)).Select(Soldier).ToArray();
        // Soldiers in 2x2 armor, then the remaining 1x1 vehicles, then the 1x1 soldiers.
        foreach (var soldier in soldiers)
            if (soldier.ArmorSize > 1) AddUnit(soldier);
        if (extended)
            foreach (var vehicle in vehicles)
                if (vehicle.Armor.Size == 1) AddVehicle(vehicle);
        foreach (var soldier in soldiers)
            if (soldier.ArmorSize == 1) AddUnit(soldier);
        if (_battle.Units.Count == 0)
            throw new MapGenerationException("Map generator encountered an error: no xcom units could be placed on the map.");
    }

    private (string Id, RuntimeArmorRule Armor) Vehicle(string itemId)
    {
        // RuleItem::getVehicleUnit: the unit of the same name as the fixed-weapon item, wearing its armor.
        if (!_rules.Items.TryGet(itemId, out var item) || _rules.Items[item].Value.VehicleArmor is not { } armor)
            throw new MapGenerationException($"Vehicle item {itemId} has no vehicle unit.");
        return (itemId, _rules.Armors[armor].Value);
    }

    private BattleUnit Soldier(CampaignState.SoldierState soldier)
    {
        var personal = soldier.Personal ?? throw new MapGenerationException($"Soldier {soldier.Id} has no personal state.");
        if (!_rules.Armors.TryGet(personal.Armor, out var handle))
            throw new MapGenerationException($"Soldier {soldier.Id} wears unknown armor {personal.Armor}.");
        var armor = _rules.Armors[handle].Value;
        return new BattleUnit(soldier.Id, BattleFaction.Player, BattleController.Human(0))
        {
            UnitType = _rules.Soldiers.GetExternalId(soldier.Rule),
            SoldierId = soldier.Id,
            ArmorId = personal.Armor,
            ArmorSize = armor.Size,
            MovementType = armor.MovementType,
        };
    }

    /// <summary>BattlescapeGenerator::addXCOMVehicle: a player unit numbered after the soldiers' ID range.</summary>
    private void AddVehicle((string Id, RuntimeArmorRule Armor) vehicle) => AddUnit(new BattleUnit(_unitSequence++, BattleFaction.Player,
        BattleController.Human(0))
    {
        UnitType = vehicle.Id,
        ArmorId = vehicle.Armor.Type,
        ArmorSize = vehicle.Armor.Size,
        MovementType = vehicle.Armor.MovementType,
    });

    /// <summary>BattlescapeGenerator::addXCOMUnit; a unit that finds no place stays out of the battle.</summary>
    private void AddUnit(BattleUnit unit)
    {
        if (!Place(unit)) return;
        _battle.AddUnit(unit);
        _battle.SelectedUnitId ??= unit.Id;
    }

    private bool Place(BattleUnit unit)
    {
        if (!_generated.CraftDeployed)
        {
            SetCustomCraftInventoryTile();
            if (SpawnNode(unit) is { } node)
            {
                unit.Position = TryPosition(unit, node.Position);
                _battle.CraftInventoryTile ??= node.Position;
                unit.Direction = _random.NextInclusive(0, 7);
                return true;
            }
            // placeUnitNearFriend finds no friend before the first unit is placed.
            if (_battle.MissionType != "STR_BASE_DEFENSE" && _battle.Units.Count != 0) throw new MapGenerationException(NearFriendPlacement);
            return false;
        }
        var craft = _rules.Crafts.GetRequiredValue(_generated.CraftRuleId);
        SetCustomCraftInventoryTile();
        if (craft.Deployment.Count != 0)
        {
            // SavedBattleGame::getCustomDeployment: the craft rule's points, relative to the craft map.
            foreach (var point in craft.Deployment)
            {
                if (point.Count < 4) throw new MapGenerationException($"Craft {_generated.CraftRuleId} has a deployment point without four values.");
                var position = new BattlePosition(point[0] + _generated.CraftX * 10, point[1] + _generated.CraftY * 10, point[2] + _generated.CraftZ);
                var free = true;
                for (var x = 0; x < unit.ArmorSize; x++)
                    for (var y = 0; y < unit.ArmorSize; y++)
                        free = free && CanPlaceXcomUnit(position with { X = position.X + x, Y = position.Y + y });
                if (free && TryPosition(unit, position) is { } placed)
                {
                    unit.Position = placed;
                    unit.Direction = point[3];
                    return true;
                }
            }
            return false;
        }
        // The first start tile, in tile order, that takes the unit.
        for (var tile = 0; tile < _map.TileCount; tile++)
        {
            var position = Position(tile);
            if (CanPlaceXcomUnit(position) && TryPosition(unit, position) is { } placed)
            {
                unit.Position = placed;
                return true;
            }
        }
        return false;
    }

    private BattlePosition Position(int tile) =>
        new(tile % _map.Width, tile / _map.Width % _map.Length, tile / (_map.Width * _map.Length));

    /// <summary>
    /// BattlescapeGenerator::canPlaceXCOMUnit: a start point floor that can be walked on, with no object; the first such
    /// tile checked becomes the craft inventory tile.
    /// </summary>
    private bool CanPlaceXcomUnit(BattlePosition position)
    {
        if (!_map.Contains(position)) return false;
        var tile = _map.TileIndex(position);
        if (_map.FloorSpecialType(tile) != StartPoint || _map.MapData(tile, BattleTilePart.ObjectPart) is not null ||
            _map.MapData(tile, BattleTilePart.Floor) is not { } floor || floor.TimeUnitsWalk == InvalidMoveCost)
            return false;
        _battle.CraftInventoryTile ??= position;
        return true;
    }

    /// <summary>
    /// BattlescapeGenerator::setCustomCraftInventoryTile: the craft rule's inventory tile (checked as a start tile, which
    /// sets it when it qualifies), else a random map block inventory tile, unchecked.
    /// </summary>
    private void SetCustomCraftInventoryTile()
    {
        if (_battle.CraftInventoryTile is null && _generated.CraftDeployed)
        {
            var coordinates = _rules.Crafts.GetRequiredValue(_generated.CraftRuleId).InventoryTile;
            if (coordinates.Count >= 3)
                CanPlaceXcomUnit(new BattlePosition(coordinates[0] + _generated.CraftX * 10, coordinates[1] + _generated.CraftY * 10,
                    coordinates[2] + _generated.CraftZ));
        }
        if (_battle.CraftInventoryTile is null && _generated.BackupInventoryTiles.Count != 0)
        {
            var pile = _generated.BackupInventoryTiles[_random.NextInclusive(0, _generated.BackupInventoryTiles.Count - 1)];
            // The reference takes the tile unchecked; one outside the map would crash it later.
            if (!_map.Contains(pile)) throw new MapGenerationException($"A map block's inventory tile {pile} is outside the map.");
            _battle.CraftInventoryTile = pile;
        }
    }

    /// <summary>
    /// SavedBattleGame::getSpawnNode: among the X-COM nodes with a positive priority that suit the unit and can take it,
    /// a random one of the highest priority.
    /// </summary>
    private BattleNode? SpawnNode(BattleUnit unit)
    {
        var highest = -1;
        var compliant = new List<BattleNode>();
        foreach (var node in _map.Nodes)
        {
            if (node.IsDummy || node.Rank != XcomNodeRank || node.Priority <= 0) continue;
            if ((node.Type & SmallNode) != 0 && unit.ArmorSize != 1) continue;
            if ((node.Type & FlyingNode) != 0 && unit.MovementType != 1) continue;
            if (TryPosition(unit, node.Position) is null) continue;
            if (node.Priority > highest)
            {
                highest = node.Priority;
                compliant.Clear();
            }
            if (node.Priority == highest) compliant.Add(node);
        }
        return compliant.Count == 0 ? null : compliant[_random.NextInclusive(0, compliant.Count - 1)];
    }

    /// <summary>
    /// SavedBattleGame::setUnitPosition's checks: every tile under the unit inside the map, free of other units, passable,
    /// with a floor unless the unit flies, and without a low big wall; a raised tile (terrain level -24) lifts the unit
    /// one level. Returns the position the unit would take, or null.
    /// </summary>
    private BattlePosition? TryPosition(BattleUnit unit, BattlePosition position)
    {
        var size = unit.ArmorSize - 1;
        var zOffset = 0;
        for (var x = size; x >= 0; x--)
            for (var y = size; y >= 0; y--)
            {
                var at = new BattlePosition(position.X + x, position.Y + y, position.Z + zOffset);
                if (!_map.Contains(at)) return null;
                var tile = _map.TileIndex(at);
                var item = _map.MapData(tile, BattleTilePart.ObjectPart);
                if (_map.UnitAt(tile) is { } other && other != unit.Id ||
                    _map.TimeUnitCost(tile, BattleTilePart.ObjectPart, unit.MovementType) == InvalidMoveCost ||
                    _map.HasNoFloor(at) && unit.MovementType != 1 ||
                    item is { BigWall: > 0 and <= 3 })
                    return null;
                if (_map.TerrainLevel(tile) == -24)
                {
                    zOffset++;
                    x = size;
                    y = size + 1;
                }
            }
        if (size > 0) throw new MapGenerationException(LargeUnitPlacement);
        return position with { Z = position.Z + zOffset };
    }
}
