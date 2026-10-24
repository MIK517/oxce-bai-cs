using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>
/// BattlescapeGenerator::deployXCOM: for a landed craft, the craft's vehicles and soldiers become player units in the
/// reference's order and are placed on the craft's start tiles, at the craft rule's deployment points, or on X-COM spawn
/// nodes when no craft map was placed; for a base defense, the base's vehicles, defenders and stores.
/// </summary>
internal sealed class XcomDeployment
{
    /// <summary>SpecialTileType START_POINT.</summary>
    private const int StartPoint = 1;

    /// <summary>NodeRank NR_XCOM.</summary>
    private const int XcomNodeRank = 1;

    private readonly BattleState _battle;
    private readonly BattleMap _map;
    private readonly BattleMapResult _generated;
    private readonly RuntimeRuleCatalog _rules;
    private readonly IRandomSource _random;
    private readonly RuntimeStartingCondition? _startingCondition;
    private readonly IReadOnlyDictionary<string, string>? _armorTransformations;
    private readonly UnitEquipment _equipment;
    private readonly BattlePlacement _placement;
    private int _unitSequence = BattleState.MaximumSoldierId;
    private string _craftType = "";
    private CraftLogisticsState? _craft;
    private bool[] _vehicleDeploymentUsed = [];
    private IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<int>>> _customRuleCraftDeployments = CampaignSnapshot.EmptyCraftDeployments;

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
        _placement = new BattlePlacement(battle, random);
    }

    /// <summary>Deploys the X-COM units and returns the next unit ID (vehicles and later units count up from MAX_SOLDIER_ID).</summary>
    public static int Deploy(CampaignState campaign, CampaignDeploymentRequest request, BattleState battle, BattleMapResult generated,
        RuntimeStartingCondition? startingCondition, string enviroEffects)
    {
        var rules = campaign.Content.RuntimeRules;
        var deployment = new XcomDeployment(battle, generated, rules, campaign.Random, startingCondition,
            rules.Battle.EnviroArmorTransformations.GetValueOrDefault(enviroEffects));
        deployment.Run(campaign, request);
        return deployment._unitSequence;
    }

    /// <summary>deployXCOM's dummy takes <c>getAlienRacesList().front()</c>, undefined for a mod without races.</summary>
    internal const string NoRaceForDummy = "A base defense without a placed defender needs an alien race for its dummy unit.";

    private void Run(CampaignState campaign, CampaignDeploymentRequest request)
    {
        var owner = campaign.BaseStates.Find(candidate => candidate.Id == request.BaseId)
            ?? throw new MapGenerationException($"The deployment request's base {request.BaseId} no longer exists.");
        if (request.MissionType == "STR_BASE_DEFENSE" && request.CraftTypeId.Length == 0)
        {
            RunBaseDefense(campaign, owner);
            return;
        }
        // The base's storage, for the debriefing (before disabled vehicles and items go back to it).
        _battle.BaseStorageItems = _rules.Items.Rules.Select(rule => (rule.Id, Quantity: owner.Items.GetValueOrDefault(_rules.Items.GetRequired(rule.Id))))
            .Where(static pair => pair.Quantity > 0).ToDictionary(static pair => pair.Id, static pair => pair.Quantity, StringComparer.Ordinal).AsReadOnly();
        _craftType = request.CraftTypeId;
        _craft = owner.Crafts.Find(craft => craft.Id == request.CraftId && _rules.Crafts.GetExternalId(craft.Rule) == request.CraftTypeId)?.Logistics;
        // Craft::resetTemporaryCustomVehicleDeploymentFlags.
        _vehicleDeploymentUsed = new bool[_craft?.CustomVehicleDeployment.Count ?? 0];
        _customRuleCraftDeployments = campaign.CustomRuleCraftDeployments;
        var extended = _rules.Campaign.ExtendedHwpLoadOrder;
        var vehicles = new List<Vehicle>();
        for (var index = 0; index < request.Vehicles.Count; index++)
        {
            var vehicle = Vehicle(request.Vehicles[index], index < request.VehicleAmmo.Count ? request.VehicleAmmo[index] : null);
            var clip = _equipment.VehicleClipAmmo(vehicle.Id);
            if (_startingCondition is { } condition &&
                (!condition.IsVehiclePermitted(vehicle.Id) || clip is not null && !ItemPermitted(condition, clip, request)))
            {
                // A disabled vehicle goes back to base storage, with the clips it loads.
                _battle.ReturnToBase(vehicle.Id, 1);
                if (clip is not null) _battle.ReturnToBase(clip, VehicleClipsLoaded(vehicle.Id, clip));
                continue;
            }
            vehicles.Add(vehicle);
        }
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
        XcomCargo.Distribute(_battle, _equipment, _rules, campaign.Options, layouts, _battle.GlobalShade);
    }

    /// <summary>
    /// deployXCOM for a base defense (a base and no craft): the base's storage is recorded for the debriefing; the
    /// vehicles Base::setupDefenses collected (those of crafts that are not out, with their ammo, then those left in the
    /// stores, loaded full) come first; then the base's soldiers that are unwounded or fit to defend and not on a craft
    /// that is out, 2x2 before 1x1. With nobody placed, a summoned dummy of the first race's first member stands on the
    /// first tile so the battle is lost at once. The stores the defenders can use and the items of the crafts that are not
    /// out then start on the inventory tile, and the stores taken leave the base when the battle starts.
    /// </summary>
    private void RunBaseDefense(CampaignState campaign, CampaignState.BaseState owner)
    {
        var (storedVehicles, taken) = CampaignWorld.StoredDefenders(_rules, owner);
        var storage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in _rules.Items.Rules)
        {
            var handle = _rules.Items.GetRequired(rule.Id);
            var quantity = owner.Items.GetValueOrDefault(handle) - taken.GetValueOrDefault(handle);
            if (quantity > 0) storage[rule.Id] = quantity;
        }
        _battle.BaseStorageItems = storage.AsReadOnly();
        foreach (var (handle, quantity) in taken) _battle.TakeFromBase(_rules.Items.GetExternalId(handle), quantity);

        var crafts = owner.Crafts.Where(static craft => craft.Logistics is { Status: not "STR_OUT" }).Select(static craft => craft.Logistics!).ToArray();
        var vehicles = crafts.SelectMany(static craft => craft.Vehicles).Select(vehicle => Vehicle(vehicle.RuleId, vehicle.Ammo))
            .Concat(storedVehicles.Select(id => Vehicle(id, VehicleClipSize(id)))).ToArray();
        foreach (var vehicle in vehicles) AddVehicle(vehicle);
        var defenders = owner.Soldiers.Where(soldier => DefendsBase(owner, soldier, campaign.Options)).ToArray();
        var soldiers = defenders.Select(Soldier).ToArray();
        foreach (var soldier in soldiers)
            if (soldier.ArmorSize > 1) AddSoldier(soldier);
        foreach (var soldier in soldiers)
            if (soldier.ArmorSize == 1) AddSoldier(soldier);
        // A base with soldiers but nowhere to spawn them (a previous defense destroyed everything but the lift).
        if (_battle.Units.Count == 0) AddDummy();
        if (!_rules.Inventories.TryGet(BattleState.GroundSlot, out _))
            throw new MapGenerationException("The mod has no STR_GROUND inventory.");
        // The base's stores the defenders can use. The reference walks its stores in RuleItem address order; the port
        // uses the item rules' order, as for the craft's items.
        foreach (var (type, quantity) in storage)
        {
            if (!UsableInBaseDefense(campaign, type)) continue;
            for (var count = 0; count < quantity; count++)
                _equipment.CreateItemForTile(type, _battle.CraftInventoryTile).XcomProperty = _battle.CraftInventoryTile is not null;
            _battle.TakeFromBase(type, quantity);
        }
        foreach (var craft in crafts)
            foreach (var rule in _rules.Items.Rules)
                for (var count = 0; count < craft.Items.GetValueOrDefault(rule.Id); count++)
                    _equipment.CreateItemForTile(rule.Id, _battle.CraftInventoryTile).XcomProperty = _battle.CraftInventoryTile is not null;
        var layouts = defenders.Where(static soldier => soldier.Personal is not null)
            .ToDictionary(static soldier => soldier.Id, static soldier => soldier.Personal!.EquipmentLayout);
        XcomCargo.Distribute(_battle, _equipment, _rules, campaign.Options, layouts, _battle.GlobalShade);
    }

    /// <summary>
    /// deployXCOM's base defender: a soldier with full health or fit to defend the base (Soldier::canDefendBase), not on a
    /// craft that is out. Unlike Base::getAvailableSoldiers, a craft's crew needs the health too.
    /// </summary>
    private bool DefendsBase(CampaignState.BaseState owner, CampaignState.SoldierState soldier, CampaignOptions options)
    {
        if (soldier.Personal is not { } personal) return false;
        if (SoldierReadiness.IsWounded(personal, _rules.Campaign.ManaWoundThreshold, _rules.Campaign.HealthWoundThreshold) &&
            !CampaignWorld.CanDefendBase(personal, options)) return false;
        if (personal.CraftType.Length == 0) return true;
        var craft = owner.Crafts.FirstOrDefault(candidate => candidate.Id == personal.CraftId &&
            _rules.Crafts.GetExternalId(candidate.Rule) == personal.CraftType);
        return craft?.Logistics?.Status != "STR_OUT";
    }

    /// <summary>
    /// deployXCOM's stores for a base defense: an item allowed before a base defense (not <c>ignoreInBaseDefense</c>),
    /// an inventory item (a big sprite and no fixed weapon), useful in battle (not a corpse or battle type 0) or
    /// equippable on a craft, whose requirements are researched (SavedGame::isResearched, true in debug mode).
    /// </summary>
    private bool UsableInBaseDefense(CampaignState campaign, string type)
    {
        var item = _rules.Items.GetRequiredValue(type);
        if (!_rules.Battle.Equipment.Items.TryGetValue(type, out var battle)) return false;
        if (battle.IgnoreInBaseDefense || !battle.IsInventoryItem) return false;
        if (item.BattleType is 0 or 11 && battle.IgnoreInCraftEquip) return false;
        var research = campaign.ArcResearch;
        return item.Requirements.All(requirement => research.IsResearched(_rules.Research.GetExternalId(requirement)));
    }

    /// <summary>RuleItem::getVehicleClipSize: the vehicle weapon's clip size when both it and its clip have one, else the clip's.</summary>
    private int? VehicleClipSize(string vehicleItem)
    {
        var weapon = _rules.Items.GetRequiredValue(vehicleItem).ClipSize;
        if (_equipment.VehicleClipAmmo(vehicleItem) is not { } clip) return weapon;
        var ammo = _rules.Items.GetRequiredValue(clip).ClipSize;
        return ammo > 0 && weapon > 0 ? weapon : ammo;
    }

    /// <summary>
    /// deployXCOM's dummy: a summoned player unit of the first race's first member (BattleUnit from the unit rule) placed
    /// on the map's first tile with setUnitPosition's checks (a raised tile lifts it), which also becomes the inventory
    /// tile. When it cannot stand there the reference keeps the unit at (0, 0, 0) without a tile; the port leaves it off
    /// the map. The reference takes the front of empty lists there.
    /// </summary>
    private void AddDummy()
    {
        if (_rules.AlienRaces.Rules.Count == 0) throw new MapGenerationException(NoRaceForDummy);
        var race = _rules.AlienRaces.Rules[0];
        var name = AlienRaces.Member(race.Id, race.Value, 0, _random);
        if (!_rules.Units.TryGet(name, out var handle) || _rules.Units[handle].Value.Armor is not { } armorHandle ||
            !_rules.Battle.Equipment.Units.TryGetValue(name, out var unitRule))
            throw new MapGenerationException($"The base defense's dummy unit {name} is no unit with an armor.");
        var armor = _battle.TempUnitArmor(_rules, _rules.Armors[armorHandle].Value);
        var stats = (BattleUnitStats.From(unitRule.Stats) + BattleUnitStats.From(armor.Stats)).ObeyFixedMinimum();
        var unit = new BattleUnit(_unitSequence++, BattleFaction.Player, BattleController.Human(0))
        {
            UnitType = name,
            ArmorId = armor.Type,
            ArmorSize = armor.Size,
            MovementType = armor.MovementType,
            Stats = stats,
            TimeUnits = stats.TimeUnits,
            Energy = stats.Stamina,
            Health = stats.Health,
            Mana = stats.Mana,
            SummonedPlayerUnit = true,
        };
        var firstTile = new BattlePosition(0, 0, 0);
        unit.Position = _placement.TryPosition(unit, firstTile);
        _battle.AddUnit(unit);
        _battle.SelectedUnitId = unit.Id;
        _battle.CraftInventoryTile = firstTile;
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
            if (_startingCondition is { } condition && !ItemPermitted(condition, rule.Id, request))
            {
                // A disabled item goes back to base storage.
                _battle.ReturnToBase(rule.Id, quantity);
                continue;
            }
            for (var count = 0; count < quantity; count++)
                _equipment.CreateItemForTile(rule.Id, _battle.CraftInventoryTile).XcomProperty = _battle.CraftInventoryTile is not null;
        }
    }

    /// <summary>RuleStartingCondition::isItemPermitted with the item's categories and, when ammo shares categories, those of the craft's primary ammo for a firearm.</summary>
    private bool ItemPermitted(RuntimeStartingCondition condition, string itemType, CampaignDeploymentRequest request)
    {
        if (!_rules.Items.TryGet(itemType, out var handle)) return condition.IsItemPermitted(itemType, null);
        var item = _rules.Items[handle].Value;
        IEnumerable<string> categories = item.Categories;
        if (_rules.Campaign.ShareAmmoCategories && item.BattleType == 1 && item.CompatibleAmmo.Count != 0)
            foreach (var ammo in item.CompatibleAmmo[0])
                if (request.Items.GetValueOrDefault(_rules.Items.GetExternalId(ammo)) > 0)
                    categories = categories.Concat(_rules.Items[ammo].Value.Categories);
        return condition.IsItemPermitted(itemType, categories);
    }

    /// <summary>RuleItem::getVehicleClipsLoaded: the clips that fill the vehicle (the weapon's capacity over the clip's), or the clip size.</summary>
    private int VehicleClipsLoaded(string vehicleItem, string clip)
    {
        var weapon = _rules.Items.GetRequiredValue(vehicleItem).ClipSize;
        var ammo = _rules.Items.GetRequiredValue(clip).ClipSize;
        return ammo > 0 && weapon > 0 ? weapon / ammo : ammo;
    }

    private Vehicle Vehicle(string itemId, int? ammo)
    {
        // RuleItem::getVehicleUnit: the unit of the same name as the fixed-weapon item, wearing its armor.
        if (!_rules.Items.TryGet(itemId, out var item) || _rules.Items[item].Value.VehicleArmor is not { } armor ||
            !_rules.Battle.Equipment.Units.TryGetValue(itemId, out var unit))
            throw new MapGenerationException($"Vehicle item {itemId} has no vehicle unit.");
        // createTempUnit: the environment transforms the vehicle unit's armor.
        return new(itemId, _battle.TempUnitArmor(_rules, _rules.Armors[armor].Value), unit, ammo);
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
            if (_placement.SpawnNode(XcomNodeRank, unit) is { } node)
            {
                unit.Position = _placement.TryPosition(unit, node.Position);
                _battle.CraftInventoryTile ??= node.Position;
                unit.Direction = _random.NextInclusive(0, 7);
                return true;
            }
            if (_battle.MissionType != "STR_BASE_DEFENSE" && _placement.PlaceNearFriend(unit))
            {
                _battle.CraftInventoryTile ??= unit.Position;
                unit.Direction = _random.NextInclusive(0, 7);
                return true;
            }
            return false;
        }
        var craft = _rules.Crafts.GetRequiredValue(_generated.CraftRuleId);
        SetCustomCraftInventoryTile();
        if (_craft is { HasCustomDeployment: true } custom && _craftType == _generated.CraftRuleId)
            return PlaceCustom(unit, custom);
        // SavedBattleGame::hasCustomDeployment / getCustomDeployment: the save's override for the craft type, even an
        // empty one, else the craft rule's points.
        var deployment = _customRuleCraftDeployments.TryGetValue(_generated.CraftRuleId, out var overridden) ? overridden
            : craft.Deployment.Count != 0 ? craft.Deployment : null;
        if (deployment is not null)
        {
            // Points relative to the craft map.
            foreach (var point in deployment)
            {
                if (point.Count < 4) throw new MapGenerationException($"Craft {_generated.CraftRuleId} has a deployment point without four values.");
                var position = CraftPosition(point[0], point[1], point[2]);
                var free = true;
                for (var x = 0; x < unit.ArmorSize; x++)
                    for (var y = 0; y < unit.ArmorSize; y++)
                        free = free && CanPlaceXcomUnit(position with { X = position.X + x, Y = position.Y + y });
                if (free && _placement.TryPosition(unit, position) is { } placed)
                {
                    unit.Position = placed;
                    unit.Direction = CustomDirection(point[3]);
                    return true;
                }
            }
            return false;
        }
        // The first start tile, in tile order, that takes the unit.
        for (var tile = 0; tile < _map.TileCount; tile++)
        {
            var position = Position(tile);
            if (CanPlaceXcomUnit(position) && _placement.TryPosition(unit, position) is { } placed)
            {
                unit.Position = placed;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// addXCOMUnit with the craft's own custom deployment (the battle's craft rules are the craft's): a soldier takes its
    /// saved point, a vehicle the first unused point of its type. The unit is placed there only when every tile it covers
    /// is a start tile and the position takes it; otherwise it stays out of the battle.
    /// </summary>
    private bool PlaceCustom(BattleUnit unit, CraftLogisticsState craft)
    {
        CraftDeploymentPoint? point = null;
        if (unit.SoldierId is { } soldier)
        {
            // The reference's map lookup crashes for a soldier without a point.
            point = craft.CustomSoldierDeployment.GetValueOrDefault(soldier)
                ?? throw new MapGenerationException($"Craft {_craftType} has no custom deployment point for soldier {soldier}.");
        }
        else
        {
            for (var index = 0; index < craft.CustomVehicleDeployment.Count; index++)
            {
                var vehicle = craft.CustomVehicleDeployment[index];
                if (vehicle.Type != unit.UnitType || _vehicleDeploymentUsed[index]) continue;
                point = vehicle.Point;
                _vehicleDeploymentUsed[index] = true;
                break;
            }
        }
        if (point is null) throw new MapGenerationException("Unit generator encountered an error: custom craft deployment failed.");
        var position = CraftPosition(point.X, point.Y, point.Z);
        var free = true;
        for (var x = 0; x < unit.ArmorSize; x++)
            for (var y = 0; y < unit.ArmorSize; y++)
                free = free && CanPlaceXcomUnit(position with { X = position.X + x, Y = position.Y + y });
        if (!free || _placement.TryPosition(unit, position) is not { } placed) return false;
        unit.Position = placed;
        unit.Direction = CustomDirection(point.Direction);
        return true;
    }

    /// <summary>A point relative to the craft map as the reference's Position holds it: Sint16 coordinates.</summary>
    private BattlePosition CraftPosition(int x, int y, int z) =>
        new((short)(x + _generated.CraftX * 10), (short)(y + _generated.CraftY * 10), (short)(z + _generated.CraftZ));

    /// <summary>
    /// BattleUnit::setDirection stores a saved direction unchecked and later lookups index by it; the port refuses one
    /// outside 0-7.
    /// </summary>
    private static int CustomDirection(int direction) => direction is >= 0 and <= 7 ? direction
        : throw new MapGenerationException($"A custom craft deployment faces direction {direction}, outside 0-7.");

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
            _map.MapData(tile, BattleTilePart.Floor) is not { } floor || floor.TimeUnitsWalk == BattlePathing.InvalidMoveCost)
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
}

/// <summary>A vehicle on board: its item, armor, unit rule and Vehicle::getAmmo (null when the request records none).</summary>
internal sealed record Vehicle(string Id, RuntimeArmorRule Armor, RuntimeBattleUnitRule Unit, int? Ammo);
