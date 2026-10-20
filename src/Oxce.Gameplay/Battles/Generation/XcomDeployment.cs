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
    internal const string EquipmentRestrictions =
        "X-COM deployment with starting-condition vehicle and item restrictions arrives with the craft's items (Phase 7 branch 1 S4b).";

    private const int InvalidMoveCost = BattlePathing.InvalidMoveCost;

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
    private readonly RuntimeStartingCondition? _startingCondition;
    private readonly IReadOnlyDictionary<string, string>? _armorTransformations;
    private readonly UnitEquipment _equipment;
    private int _unitSequence = BattleState.MaximumSoldierId;

    private XcomDeployment(BattleState battle, BattleMapResult generated, RuntimeRuleCatalog rules, IRandomSource random,
        RuntimeStartingCondition? startingCondition, IReadOnlyDictionary<string, string>? armorTransformations)
    {
        _battle = battle;
        _map = battle.Map;
        _generated = generated;
        _rules = rules;
        _random = random;
        _startingCondition = startingCondition;
        _armorTransformations = armorTransformations;
        _equipment = new UnitEquipment(battle, rules, random);
    }

    public static void Deploy(CampaignState campaign, CampaignDeploymentRequest request, BattleState battle, BattleMapResult generated,
        RuntimeStartingCondition? startingCondition, string enviroEffects)
    {
        var rules = campaign.Content.RuntimeRules;
        if (startingCondition is { RestrictsEquipment: true }) throw new MapGenerationException(EquipmentRestrictions);
        new XcomDeployment(battle, generated, rules, campaign.Random, startingCondition,
            rules.Battle.EnviroArmorTransformations.GetValueOrDefault(enviroEffects)).Run(campaign, request);
    }

    private void Run(CampaignState campaign, CampaignDeploymentRequest request)
    {
        var owner = campaign.BaseStates.Find(candidate => candidate.Id == request.BaseId)
            ?? throw new MapGenerationException($"The deployment request's base {request.BaseId} no longer exists.");
        var extended = _rules.Campaign.ExtendedHwpLoadOrder;
        var vehicles = request.Vehicles.Select((id, index) => Vehicle(id, index < request.VehicleAmmo.Count ? request.VehicleAmmo[index] : null)).ToArray();
        // 2x2 vehicles first (all vehicles without the extended order).
        foreach (var vehicle in vehicles)
            if (vehicle.Armor.Size > 1 || !extended) AddVehicle(vehicle);
        var soldiers = owner.Soldiers.Where(soldier => request.Soldiers.Contains(soldier.Id)).Select(Soldier).ToArray();
        // Soldiers in 2x2 armor, then the remaining 1x1 vehicles, then the 1x1 soldiers.
        foreach (var soldier in soldiers)
            if (soldier.ArmorSize > 1) AddSoldier(soldier);
        if (extended)
            foreach (var vehicle in vehicles)
                if (vehicle.Armor.Size == 1) AddVehicle(vehicle);
        foreach (var soldier in soldiers)
            if (soldier.ArmorSize == 1) AddSoldier(soldier);
        if (_battle.Units.Count == 0)
            throw new MapGenerationException("Map generator encountered an error: no xcom units could be placed on the map.");
        AddCraftItems(request);
        var layouts = owner.Soldiers.Where(soldier => request.Soldiers.Contains(soldier.Id) && soldier.Personal is not null)
            .ToDictionary(static soldier => soldier.Id, static soldier => soldier.Personal!.EquipmentLayout);
        XcomCargo.Distribute(_battle, _equipment, _rules, campaign.Options, layouts, request.Shade);
    }

    /// <summary>
    /// deployXCOM's craft items: each item the craft carries, one battle item per unit of quantity, on the craft inventory
    /// tile, all X-COM property. The reference walks the craft's item map in RuleItem pointer order (allocation order, an
    /// implementation detail); the port uses the item rules' order, which that follows in practice. Equipment layouts,
    /// weapon loading and auto-equip follow in a later part of S4b.
    /// </summary>
    private void AddCraftItems(CampaignDeploymentRequest request)
    {
        if (!_rules.Inventories.TryGet(BattleState.GroundSlot, out _))
            throw new MapGenerationException("The mod has no STR_GROUND inventory.");
        foreach (var rule in _rules.Items.Rules)
        {
            if (!request.Items.TryGetValue(rule.Id, out var quantity)) continue;
            for (var count = 0; count < quantity; count++)
                _equipment.CreateItemForTile(rule.Id, _battle.CraftInventoryTile).XcomProperty = _battle.CraftInventoryTile is not null;
        }
    }

    private Vehicle Vehicle(string itemId, int? ammo)
    {
        // RuleItem::getVehicleUnit: the unit of the same name as the fixed-weapon item, wearing its armor.
        if (!_rules.Items.TryGet(itemId, out var item) || _rules.Items[item].Value.VehicleArmor is not { } armor ||
            !_rules.Battle.Equipment.Units.TryGetValue(itemId, out var unit))
            throw new MapGenerationException($"Vehicle item {itemId} has no vehicle unit.");
        return new(itemId, _rules.Armors[armor].Value, unit, ammo);
    }

    private BattleUnit Soldier(CampaignState.SoldierState soldier)
    {
        var personal = soldier.Personal ?? throw new MapGenerationException($"Soldier {soldier.Id} has no personal state.");
        var own = Armor(personal.Armor) ?? throw new MapGenerationException($"Soldier {soldier.Id} wears unknown armor {personal.Armor}.");
        var soldierType = _rules.Soldiers.GetExternalId(soldier.Rule);
        var (armorId, armor) = BattleArmor(soldierType, personal.Armor, own);
        // Soldier::prepareStatsWithBonuses: current statistics, soldier bonuses and the battle armor's statistics.
        var stats = (BattleUnitStats.From(CampaignState.StatsWithBonuses(personal, _rules)) + BattleUnitStats.From(armor.Stats))
            .ObeyFixedMinimum();
        var health = Math.Max(1, stats.Health - personal.HealthMissing);
        var morale = 100;
        // Wounded soldiers (defending the base) start with lowered morale.
        if (SoldierReadiness.IsWounded(personal, _rules.Campaign.ManaWoundThreshold, _rules.Campaign.HealthWoundThreshold))
        {
            morale = 75;
            health = Math.Max(1, health - (int)Math.Ceiling(personal.Recovery));
        }
        return new BattleUnit(soldier.Id, BattleFaction.Player, BattleController.Human(0))
        {
            UnitType = soldierType,
            SoldierId = soldier.Id,
            ArmorId = armorId,
            OriginalArmorId = armorId == personal.Armor ? "" : personal.Armor,
            ArmorSize = armor.Size,
            MovementType = armor.MovementType,
            Stats = stats,
            TimeUnits = stats.TimeUnits,
            Energy = stats.Stamina,
            Health = health,
            Mana = Math.Max(0, stats.Mana - personal.ManaMissing),
            Morale = morale,
        };
    }

    private RuntimeArmorRule? Armor(string id) => id.Length != 0 && _rules.Armors.TryGet(id, out var handle) ? _rules.Armors[handle].Value : null;

    /// <summary>
    /// deployXCOM's armor pass: the environment's transformation of the soldier's armor when it is no larger, else the
    /// starting condition's replacement (RuleStartingCondition::getArmorReplacement, a weighted pick for a disallowed
    /// armor) when it is no larger. The reference changes the geoscape soldier's armor for the battle and restores it in
    /// the debriefing; the port gives the battle unit the armor and leaves the soldier as it was.
    /// </summary>
    private (string Id, RuntimeArmorRule Armor) BattleArmor(string soldierType, string armorId, RuntimeArmorRule armor)
    {
        if (_armorTransformations is not null && _armorTransformations.TryGetValue(armorId, out var transformedId))
        {
            var transformed = Armor(transformedId) ?? throw new MapGenerationException($"Armor transformation target {transformedId} does not exist.");
            if (armor.Size >= transformed.Size) return (transformedId, transformed);
        }
        if (_startingCondition is not { } condition) return (armorId, armor);
        var allowed = condition.ForbiddenArmors.Count != 0 ? !condition.ForbiddenArmors.Contains(armorId)
            : condition.AllowedArmors.Count == 0 || condition.AllowedArmors.Contains(armorId);
        if (allowed || !condition.DefaultArmor.TryGetValue(soldierType, out var weights)) return (armorId, armor);
        var pick = WorldAlienMissionGeneration.Choose(
            weights.Where(static pair => pair.Value > 0).ToDictionary(static pair => pair.Key, static pair => (ulong)pair.Value, StringComparer.Ordinal), _random);
        if (pick is "" or "noChange") return (armorId, armor);
        var replaced = Armor(pick) ?? throw new MapGenerationException($"Default armor {pick} does not exist.");
        // A soldier cannot switch into a bigger armor.
        return replaced.Size > armor.Size ? (armorId, armor) : (pick, replaced);
    }

    /// <summary>
    /// BattlescapeGenerator::addXCOMVehicle: a player unit numbered after the soldiers' ID range, with the unit rule's and
    /// its armor's statistics; once placed it takes its fixed weapon and that weapon's clip ammunition with the vehicle's
    /// ammo.
    /// </summary>
    private void AddVehicle(Vehicle vehicle)
    {
        var stats = (BattleUnitStats.From(vehicle.Unit.Stats) + BattleUnitStats.From(vehicle.Armor.Stats)).ObeyFixedMinimum();
        var unit = new BattleUnit(_unitSequence++, BattleFaction.Player, BattleController.Human(0))
        {
            UnitType = vehicle.Id,
            ArmorId = vehicle.Armor.Type,
            ArmorSize = vehicle.Armor.Size,
            MovementType = vehicle.Armor.MovementType,
            Stats = stats,
            TimeUnits = stats.TimeUnits,
            Energy = stats.Stamina,
            Health = stats.Health,
            Mana = stats.Mana,
        };
        if (!AddUnit(unit, vehicle.Unit, "")) return;
        _equipment.CreateItemForUnit(vehicle.Id, unit, fixedWeapon: true);
        if (_equipment.VehicleClipAmmo(vehicle.Id) is { } clip)
        {
            var ammo = vehicle.Ammo ?? throw new MapGenerationException($"The deployment request records no ammo for vehicle {vehicle.Id}.");
            if (_equipment.CreateItemForUnit(clip, unit) is { } item) item.AmmoQuantity = ammo;
        }
        unit.TurretType = _equipment.BattleItemRule(vehicle.Id).TurretType;
    }

    private void AddSoldier(BattleUnit unit) => AddUnit(unit, null, unit.UnitType);

    /// <summary>
    /// BattlescapeGenerator::addXCOMUnit: a placed unit joins the battle and SavedBattleGame::initUnit equips it; a unit
    /// that finds no place stays out of the battle.
    /// </summary>
    private bool AddUnit(BattleUnit unit, RuntimeBattleUnitRule? unitRule, string soldierType)
    {
        if (!Place(unit)) return false;
        _battle.AddUnit(unit);
        _battle.SelectedUnitId ??= unit.Id;
        _equipment.InitUnit(unit, unitRule, soldierType);
        return true;
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
            if (_battle.MissionType != "STR_BASE_DEFENSE" && PlaceNearFriend(unit))
            {
                _battle.CraftInventoryTile ??= unit.Position;
                unit.Direction = _random.NextInclusive(0, 7);
                return true;
            }
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
        var placed = position with { Z = position.Z + zOffset };
        // A large unit must not straddle a wall between its tiles.
        if (size > 0)
            for (var direction = 2; direction <= 4; direction++)
                if (BattlePathing.IsBlockedDirection(_map, unit.MovementType, placed, direction)) return null;
        return placed;
    }

    /// <summary>
    /// BattlescapeGenerator::placeUnitNearFriend: up to ten times, a random placed unit of the same side at least as large
    /// (100 draws at most) and a place next to it.
    /// </summary>
    private bool PlaceNearFriend(BattleUnit unit)
    {
        if (_battle.Units.Count == 0) return false;
        for (var attempt = 0; attempt != 10; attempt++)
        {
            BattlePosition? entry = null;
            var largeFriend = false;
            var tries = 100;
            while (entry is null && tries != 0)
            {
                var friend = _battle.Units[_random.NextInclusive(0, _battle.Units.Count - 1)];
                if (friend.Faction == unit.Faction && friend.Position is { } at && friend.ArmorSize >= unit.ArmorSize)
                {
                    entry = at;
                    largeFriend = friend.ArmorSize > 1;
                }
                tries--;
            }
            if (tries != 0 && PlaceNearPosition(unit, entry!.Value, largeFriend)) return true;
        }
        return false;
    }

    /// <summary>
    /// SavedBattleGame::placeUnitNearPosition: the position itself, then the eight neighbours (offset by the unit's and the
    /// friend's size) not walled off from it, then one level up for a flyer over open air.
    /// </summary>
    private bool PlaceNearPosition(BattleUnit unit, BattlePosition entry, bool largeFriend)
    {
        if (TryPosition(unit, entry) is { } here)
        {
            unit.Position = here;
            return true;
        }
        var me = -unit.ArmorSize;
        var you = largeFriend ? 2 : 1;
        ReadOnlySpan<int> xs = [0, you, you, you, 0, me, me, me];
        ReadOnlySpan<int> ys = [me, me, 0, you, you, you, 0, me];
        for (var direction = 0; direction <= 7; direction++)
        {
            var offset = new BattlePosition(entry.X + xs[direction], entry.Y + ys[direction], entry.Z);
            // Position division truncates toward zero, as C++ does.
            var half = new BattlePosition(entry.X + xs[direction] / 2, entry.Y + ys[direction] / 2, entry.Z);
            if (_map.Contains(offset) && _map.Contains(half) &&
                !BattlePathing.IsBlockedDirection(_map, unit.MovementType, half, direction) && TryPosition(unit, offset) is { } placed)
            {
                unit.Position = placed;
                return true;
            }
        }
        var above = entry with { Z = entry.Z + 1 };
        if (unit.MovementType == 1 && _map.Contains(above) && _map.HasNoFloor(above) && TryPosition(unit, above) is { } flying)
        {
            unit.Position = flying;
            return true;
        }
        return false;
    }
}

/// <summary>A vehicle on board: its item, armor, unit rule and Vehicle::getAmmo (null when the request records none).</summary>
internal sealed record Vehicle(string Id, RuntimeArmorRule Armor, RuntimeBattleUnitRule Unit, int? Ammo);
