using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.BaseDefenseMapTests;
using static Oxce.CompatibilityTests.BattleTestTerrain;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 1 S6: <c>BattlescapeGenerator::deployXCOM</c> for a base defense: the vehicles Base::setupDefenses
/// collected, the base's defenders, the dummy unit when nobody can be placed, and the stores and craft items on the
/// inventory tile, taken out of the base when the battle starts.
/// </summary>
public sealed class BaseDefenseDeploymentTests
{
    private const string Deployment = """
        alienItemLevels: [[0, 0, 0, 0, 0, 0, 0, 0, 0, 0]]
        invs:
          - id: STR_GROUND
            type: 2
          - id: STR_RIGHT_HAND
            type: 1
          - id: STR_LEFT_HAND
            type: 1
        armors:
          - type: VEHICLE_ARMOR
            size: 2
            allowInv: false
            corpseBattle: [CORPSE, CORPSE, CORPSE, CORPSE]
            corpseGeo: CORPSE
            loftempsSet: [0, 0, 0, 0]
          - type: SMALL_VEHICLE_ARMOR
            allowInv: false
            corpseBattle: [CORPSE]
            corpseGeo: CORPSE
            loftempsSet: [0]
          - type: SHINY_ARMOR
            allowInv: false
            stats: {health: 7}
            corpseBattle: [CORPSE]
            corpseGeo: CORPSE
            loftempsSet: [0]
          - type: BIG_SUIT
            size: 2
            corpseBattle: [CORPSE, CORPSE, CORPSE, CORPSE]
            corpseGeo: CORPSE
            loftempsSet: [0, 0, 0, 0]
        enviroEffects:
          - type: ENV_SHINY
            armorTransformations: {SMALL_VEHICLE_ARMOR: SHINY_ARMOR}
        items:
          - type: VEHICLE
            fixedWeapon: true
            battleType: 1
            clipSize: 60
            compatibleAmmo: [VEHICLE_AMMO]
          - type: VEHICLE_AMMO
            battleType: 2
            clipSize: 30
          - type: VEHICLE_B
            fixedWeapon: true
          - type: VEHICLE_D
            fixedWeapon: true
            battleType: 1
            compatibleAmmo: [VEHICLE_AMMO]
          - type: GUN
            battleType: 1
            clipSize: -1
            bigSprite: 1
          - type: KNIFE
            battleType: 3
            clipSize: -1
            bigSprite: 2
          - type: GRENADE
            battleType: 4
            bigSprite: 3
            ignoreInBaseDefense: true
          - type: LOCKED
            battleType: 3
            clipSize: -1
            bigSprite: 4
          - type: TROPHY
            bigSprite: 5
          - type: KIT
            bigSprite: 6
            ignoreInCraftEquip: false
        units:
          - type: VEHICLE
            armor: VEHICLE_ARMOR
            stats: {tu: 60, health: 50}
          - type: VEHICLE_B
            armor: SMALL_VEHICLE_ARMOR
          - type: VEHICLE_D
            armor: SMALL_VEHICLE_ARMOR
          - type: SECTOID
            armor: SMALL_VEHICLE_ARMOR
            stats: {tu: 40, health: 20}

        """;

    // A one-room base (XB_00) whose routes have eight X-COM spawn nodes.
    private static readonly FacilitySnapshot[] Room = [Facility("XB_ROOM", 0, 0)];

    // The dirt blocks hold the aliens' spawn nodes.
    private static void AlienNodes(Dictionary<string, byte[]> files)
    {
        foreach (var block in new[] { "DIRT_A", "DIRT_B" })
            files[$"ROUTES/{block}.RMP"] = [.. RouteNode(5, 5, 0, [], rank: 0, priority: 1), .. RouteNode(2, 5, 0, [], rank: 4, priority: 1)];
    }

    private static void SpawnNodes(Dictionary<string, byte[]> files)
    {
        AlienNodes(files);
        files["ROUTES/XB_00.RMP"] = [.. new[] { (1, 1), (4, 1), (7, 1), (1, 4), (4, 4), (7, 4), (1, 7), (4, 7) }
            .SelectMany(static node => RouteNode(node.Item1, node.Item2, 0, [], rank: 1, priority: 1))];
    }

    // With oxceWoundedDefendBaseIf at 50: the first soldier is crew of the ship (at the base) and wounded beyond defending
    // it (10 of 30 health left), the second is off the carrier and wounded but fit (25 of 30), the third is crew of the
    // carrier, which is out, and the fourth wears a 2x2 armor. The ship carries a small vehicle, the carrier another and
    // a kit; the base stores a large vehicle with three clips (two load it).
    private static CampaignSnapshot Fitness(CampaignSnapshot snapshot) =>
        snapshot with { Options = snapshot.Options with { WoundedDefendBaseIf = 50 } };

    private static BaseSnapshot Defenders(BaseSnapshot owner) => owner with
    {
        Soldiers =
        [
            owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = 1, Recovery = 20 } },
            owner.Soldiers[1] with { Personal = owner.Soldiers[1].Personal! with { CraftType = "", CraftId = 0, Recovery = 5 } },
            owner.Soldiers[2] with { Personal = owner.Soldiers[2].Personal! with { CraftType = "CARRIER", CraftId = 1 } },
            owner.Soldiers[3] with { Personal = owner.Soldiers[3].Personal! with { Armor = "BIG_SUIT" } },
        ],
        Crafts = [.. owner.Crafts.Select(static craft => craft.Logistics is null ? craft
            : craft.RuleId == "SHIP" ? craft with { Logistics = craft.Logistics with { Vehicles = [new CraftVehicleSnapshot("VEHICLE_B", 0)] } }
            : craft.RuleId == "CARRIER" ? craft with
            {
                Logistics = craft.Logistics with
                {
                    Status = "STR_OUT",
                    Vehicles = [new CraftVehicleSnapshot("VEHICLE_B", 0)],
                    Items = new Dictionary<string, int> { ["KIT"] = 1 },
                },
            }
            : craft)],
        Items = new Dictionary<string, int>(owner.Items, StringComparer.Ordinal)
        {
            ["VEHICLE"] = 1,
            ["VEHICLE_AMMO"] = 3,
            ["GUN"] = 2,
            ["KNIFE"] = 1,
            ["GRENADE"] = 1,
            ["LOCKED"] = 1,
            ["TROPHY"] = 1,
            ["KIT"] = 1,
        },
    };

    private static BattleState Battle(CampaignState campaign) =>
        campaign.GetQuery<IBattleQuery>() is CampaignBattles battles ? battles.Battle! : throw new InvalidOperationException();

    private static void Start(CampaignState campaign)
    {
        var events = campaign.Execute(new StartBattle()).Events;
        Assert.Equal("", events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason.ToString() ?? "");
    }

    [Fact]
    public void TheBasesVehiclesComeFirstThenItsFitSoldiersAndTheUsableStoresLeaveTheBase()
    {
        var (campaign, request) = Requested(Room, Deployment, files: SpawnNodes, adjustBase: Defenders, adjust: Fitness);
        var owner = campaign.Capture().Bases[0];
        var soldiers = owner.Soldiers;
        // Base::getAvailableSoldiers counts the ship's crew whatever its health; deployXCOM does not deploy it.
        Assert.Equal([soldiers[0].Id, soldiers[1].Id, soldiers[3].Id], request.Soldiers);
        Start(campaign);
        var battle = Battle(campaign);
        var units = battle.Units.Where(static unit => unit.Faction == BattleFaction.Player).ToArray();

        // The ship's vehicle with its own ammo (not the carrier's), then the stored one loaded full, then the defenders:
        // the 2x2 soldier before the fit wounded one; the ship's unfit crew and the carrier's crew stay out.
        Assert.Equal(["VEHICLE_B", "VEHICLE", "RECRUIT", "RECRUIT"], units.Select(static unit => unit.UnitType));
        Assert.Equal([1_000_000, 1_000_001, soldiers[3].Id, soldiers[1].Id], units.Select(static unit => unit.Id));
        Assert.Equal(2, units[2].ArmorSize);
        Assert.All(units, static unit => Assert.NotNull(unit.Position));
        var clip = Assert.Single(battle.Items, static item => item.ItemType == "VEHICLE_AMMO");
        Assert.Equal(60, clip.AmmoQuantity);

        // The stores a defender can use (inventory items, useful in battle or craft equipment, researched) and the ship's
        // items (not the carrier's kit) start on the inventory tile, all X-COM property; the rest stays in the base.
        var items = battle.Items.Where(static item => item.XcomProperty && item.ItemType is not ("VEHICLE" or "VEHICLE_B" or "VEHICLE_AMMO"))
            .GroupBy(static item => item.ItemType).ToDictionary(static group => group.Key, static group => group.Count());
        var ship = owner.Crafts.Single(static craft => craft.RuleId == "SHIP").Logistics!.Items;
        Assert.Equal(new Dictionary<string, int> { ["GUN"] = 2, ["KNIFE"] = 1, ["KIT"] = 1, ["SUPPLY"] = ship["SUPPLY"] }, items);

        // The debriefing's record is the storage after setupDefenses took the stored vehicle and its two clips.
        var stores = new Dictionary<string, int>(owner.Items, StringComparer.Ordinal) { ["VEHICLE_AMMO"] = 1 };
        stores.Remove("VEHICLE");
        Assert.Equal(stores.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
            battle.BaseStorageItems.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
        // The battle took those and the equipped stores out of the base.
        var left = campaign.Capture().Bases[0].Items;
        foreach (var gone in new[] { "VEHICLE", "GUN", "KNIFE", "KIT" }) Assert.False(left.ContainsKey(gone), gone);
        Assert.Equal((1, 1, 1, 1, owner.Items["SUPPLY"]),
            (left["VEHICLE_AMMO"], left["GRENADE"], left["LOCKED"], left["TROPHY"], left["SUPPLY"]));
    }

    [Fact]
    public void WithoutASpawnPointASummonedDummyOfTheFirstRaceStandsOnTheFirstTile()
    {
        // No X-COM spawn node: the reference force-spawns a dummy so that the battle is lost at once.
        var (campaign, _) = Requested(Room, Deployment, files: AlienNodes);
        Start(campaign);
        var battle = Battle(campaign);
        var dummy = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal(("SECTOID", true, new BattlePosition(0, 0, 0), 20), (dummy.UnitType, dummy.SummonedPlayerUnit, dummy.Position, dummy.Health));
        Assert.Equal(dummy.Id, battle.SelectedUnitId);
        Assert.Equal(new BattlePosition(0, 0, 0), battle.CraftInventoryTile);
    }

    [Fact]
    public void ADummyThatIsNoUnitRefusesTheBattle()
    {
        var rules = Deployment.Replace("type: SECTOID", "type: NOT_SECTOID", StringComparison.Ordinal);
        var (campaign, _) = Requested(Room, rules, files: AlienNodes);
        var before = campaign.Capture();
        Assert.Equal("The base defense's dummy unit SECTOID is no unit with an armor.", Refusal(campaign));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void InDebugModeTheStoresNeedNoResearch()
    {
        var (campaign, _) = Requested(Room, Deployment, files: SpawnNodes, adjustBase: Defenders,
            adjust: static snapshot => Fitness(snapshot) with { DebugMode = true });
        Start(campaign);
        Assert.Single(Battle(campaign).Items, static item => item.ItemType == "LOCKED");
        Assert.False(campaign.Capture().Bases[0].Items.ContainsKey("LOCKED"));
    }

    [Fact]
    public void AStoredVehicleWithoutClipsAndOneWhoseWeaponHasNoClipSizeLoadAsTheirRulesSay()
    {
        // getVehicleClipSize: VEHICLE_B loads no clip; VEHICLE_D's weapon has no clip size, so it loads its clip's 30
        // rounds, and getVehicleClipsLoaded takes the clip's 30 clips out of the stores.
        var (campaign, _) = Requested(Room, Deployment, files: SpawnNodes, adjustBase: static owner => owner with
        {
            Items = new Dictionary<string, int>(owner.Items, StringComparer.Ordinal) { ["VEHICLE_B"] = 1, ["VEHICLE_D"] = 1, ["VEHICLE_AMMO"] = 31 },
        });
        Start(campaign);
        var battle = Battle(campaign);
        Assert.Equal(["VEHICLE_B", "VEHICLE_D"], battle.Units.Where(static unit => unit.Faction == BattleFaction.Player).Take(2)
            .Select(static unit => unit.UnitType).Order(StringComparer.Ordinal));
        Assert.Equal(30, Assert.Single(battle.Items, static item => item.ItemType == "VEHICLE_AMMO").AmmoQuantity);
        Assert.Equal(1, campaign.Capture().Bases[0].Items["VEHICLE_AMMO"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheEnvironmentTransformsTheArmorOfUnitsMadeFromUnitRules(bool spawnPoints)
    {
        // createTempUnit: the vehicles, the dummy and the aliens wear the environment's armor for their unit's.
        var (campaign, _) = Requested(Room, Deployment, files: spawnPoints ? SpawnNodes : AlienNodes, adjustBase: Defenders, adjust: Fitness,
            rules: static rules => rules.Replace("    terrains: [XBASE_T]\n", "    terrains: [XBASE_T]\n    enviroEffects: ENV_SHINY\n",
                StringComparison.Ordinal));
        Start(campaign);
        var fromRules = Battle(campaign).Units.Where(static unit => unit.SoldierId is null && unit.UnitType != "VEHICLE").ToArray();
        Assert.Contains(fromRules, static unit => unit.Faction == BattleFaction.Hostile);
        Assert.Contains(fromRules, static unit => unit.Faction == BattleFaction.Player);
        Assert.All(fromRules, static unit => Assert.Equal("SHINY_ARMOR", unit.ArmorId));
        // The sectoids' 20 health and the armor's 7.
        Assert.All(fromRules.Where(static unit => unit.UnitType == "SECTOID"), static unit => Assert.Equal(27, unit.Health));
    }

    [Fact]
    public void WithoutAGroundInventoryTheBaseDefenseIsRefused()
    {
        var (campaign, _) = Requested(Room, Deployment.Replace("id: STR_GROUND", "id: STR_FLOOR", StringComparison.Ordinal), files: SpawnNodes);
        Assert.Equal("The mod has no STR_GROUND inventory.", Refusal(campaign));
    }
}
