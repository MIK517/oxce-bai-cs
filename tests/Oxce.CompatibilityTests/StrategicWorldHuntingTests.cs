using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Gameplay.Campaigns;
using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Mods.Rulesets.Content;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldHuntingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HunterCandidateScansChargeEmptyBasesBeforeMutation(bool newborn)
    {
        var content = Load(newborn ? "alienMissions: [{type: EMPTY_BASE_HUNTERS, objective: 0, waves: [{ufo: SPAWN_HUNTER_DEFAULTS, count: 1001, trajectory: TRAJ_SPAWN_FIXED, timer: 0}]}]\n" : null);
        var initial = HuntingSnapshot(content);
        var owner = initial.Bases[0];
        initial = initial with
        {
            Time = newborn ? new(1, 1, 1, 1999, 1, 29, 55) : initial.Time,
            Bases = Enumerable.Range(0, 1001).Select(index => owner with
            {
                Id = index,
                Facilities = [],
                Crafts = [],
                Soldiers = [],
                Research = [],
                Transfers = [],
                Items = new Dictionary<string, int>(),
            }).ToArray(),
            World = initial.World with
            {
                Missions = newborn ? [new(4, "EMPTY_BASE_HUNTERS", "REGION", "RACE_A", 0, 0, 0, 0, -1)]
                    : [initial.World.Missions[0] with { LiveUfos = 1001 }],
                Ufos = newborn ? [] : Enumerable.Range(0, 1001).Select(index => initial.World.Ufos[0] with { UniqueId = 9 + index, Id = 3 + index }).ToArray(),
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            newborn ? "New UFO retargeting exceeds the supported work limit." : "UFO retargeting exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void DelayedProductionBirthsDoNotEnterTheCreationTickHunterPopulation(int delay)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "productionCraftBirthRows"), row => row[2].GetInt32() == delay && row[5].GetInt32() == (delay == 0 ? 0 : 1));
        var content = Load($"alienMissions: [{{type: DELAYED_POPULATION_HUNTERS, objective: 0, waves: [{{ufo: SPAWN_HUNTER_DEFAULTS, count: 1001, trajectory: TRAJ_SPAWN_FIXED, timer: 0}}]}}]\nmanufacture: [{{name: DELAYED_POPULATION_PRODUCT, category: STR_CRAFT, time: 1, transferTimes: [0, 0, {delay}], producedItems: {{SHIP: 1}}}}]\nfacilities: [{{type: DELAYED_POPULATION_WORKSHOP, mapName: LOGISTICS, workshops: 1100}}]\n");
        var initial = CreateSnapshot(content, "Delayed craft population") with
        {
            Time = new(1, 1, 1, 1999, 1, 59, 55),
            World = new() { Missions = [new(4, "DELAYED_POPULATION_HUNTERS", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
        };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Research = [], Transfers = [],
            Facilities = [.. initial.Bases[0].Facilities, new("DELAYED_POPULATION_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("DELAYED_POPULATION_PRODUCT", 1001, 0, 1001, false, false, false, new Dictionary<string, int>())],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "delayed-population-before.sav");
        if (delay == 0)
        {
            AssertTimeBlocked(campaign, "New UFO retargeting exceeds the supported work limit.");
            return;
        }
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Equal(initial.Bases[0].Crafts.Count, after.Bases[0].Crafts.Count);
        Assert.Equal(1001, after.Bases[0].Transfers.Count);
        Assert.All(after.Bases[0].Transfers, transfer => Assert.Equal(delay, transfer.Hours));
        Assert.Equal(1001, after.World.Ufos.Count);
        Assert.All(after.World.Ufos, ufo => Assert.False(ufo.Hunting));
        campaign = Reload(content, campaign, 81, "delayed-population-after.sav");
        AdvanceOne(campaign);
        Assert.Equal(1001, campaign.Capture().Bases[0].Transfers.Count);
        Reload(content, campaign, 81, "delayed-population-following-tick.sav");
    }

    [Fact]
    public void NewHunterPopulationBoundsUseNativeHourlyProgressIncludingInfiniteQueues()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("manufacture: [{name: POPULATION_FOUR, category: STR_CRAFT, time: 4, producedItems: {SHIP: 1}}, {name: POPULATION_HUNDRED, category: STR_CRAFT, time: 100, producedItems: {SHIP: 1}}]\nfacilities: [{type: POPULATION_WORKSHOP, mapName: LOGISTICS, workshops: 10}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionProgressRows").Where(row => row[0].GetInt32() > 0))
        {
            var initial = CreateSnapshot(content, "Native hourly craft population") with
            {
                Time = new(1, 1, 1, 1999, 1, 59, 55),
                World = new() { Missions = [new(4, "MISSION_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
            };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Research = [],
                Facilities = [.. initial.Bases[0].Facilities, new("POPULATION_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new(row[0].GetInt32() == 4 ? "POPULATION_FOUR" : "POPULATION_HUNDRED",
                    row[2].GetInt32(), row[1].GetInt32(), 20, row[3].GetBoolean(), false, false, new Dictionary<string, int>())],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "hourly-population-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            Assert.Equal(1, Assert.Single(result.Events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
            var after = campaign.Capture();
            Assert.Equal(initial.Bases[0].Crafts.Count + row[4].GetInt32(), after.Bases[0].Crafts.Count);
            Assert.False(Assert.Single(after.World.Ufos).Hunting);
            Reload(content, campaign, 81, "hourly-population-after.sav");
        }
    }

    [Fact]
    public void EscortPrefixesChargeMissionLookupsBeforeMutation()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Missions = Enumerable.Range(0, 5001).Select(index => initial.World.Missions[0] with
                { Id = 4 + index, LiveUfos = index == 5000 ? 200 : 0 }).ToArray(),
                Ufos = Enumerable.Range(0, 200).Select(index => initial.World.Ufos[0] with
                {
                    UniqueId = 9 + index,
                    Id = 3 + index,
                    MissionId = 5004,
                    HunterKiller = false,
                    Status = index == 199 ? UfoStatus.Flying : UfoStatus.Landed,
                    Altitude = index == 199 ? WorldAltitudes.High : WorldAltitudes.Ground,
                    SecondsRemaining = 5,
                    Escorting = index == 199,
                    OriginalWaypoint = index == 199 ? new(1, 0.6) : null,
                    Destination = index == 199 ? new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 201, 0.4, 0.2) { UniqueId = 207 }
                        : initial.World.Ufos[0].Destination,
                }).ToArray(),
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO escort movement exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalArrivalDefersLaterHunterCombatWithoutMutatingTheHunter(bool terminalFirst)
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        var hunter = initial.World.Ufos[0] with
        {
            HunterKiller = false,
            Hunting = true,
            OriginalWaypoint = new(1, 0.6),
            Longitude = 0.45,
            Latitude = 0.21,
            Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21),
        };
        var terminal = initial.World.Ufos[0] with
        {
            UniqueId = 10,
            Id = 4,
            HunterKiller = false,
            TrajectoryPoint = content.RuntimeRules.UfoTrajectories[content.RuntimeRules.UfoTrajectories.GetRequired(hunter.TrajectoryId)].Value.Waypoints.Count - 1,
            Destination = WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2)),
        };
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = terminalFirst ? [terminal, hunter] : [hunter, terminal],
                Missions = [initial.World.Missions[0] with { LiveUfos = 2 }],
            }
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "deferred-hunter-before.sav");
        if (terminalFirst)
        {
            var before = campaign.Capture();
            AdvanceOne(campaign);
            var after = campaign.Capture();
            Assert.Equal(before.World.Ufos[1], after.World.Ufos[1]);
            Assert.Equal(UfoStatus.Destroyed, after.World.Ufos[0].Status);
            campaign = Reload(content, campaign, 81, "deferred-hunter-after.sav");
        }
        AssertInterceptionStarts(campaign);
    }

    [Fact]
    public void LargeEscortGraphsRefuseBeforePositionForecasts()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 501 }],
                Ufos = Enumerable.Range(0, 501).Select(index => initial.World.Ufos[0] with
                {
                    UniqueId = 9 + index,
                    Id = 3 + index,
                    HunterKiller = false,
                    Escorting = true,
                    OriginalWaypoint = new(1, 0.6),
                    Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 },
                }).ToArray(),
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO escort movement exceeds the supported work limit.");
    }

    [Fact]
    public void EscortChainsRingsAndSelfTargetsMatchNativeMovement()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        var content = Load();
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "escortMovement").GroupBy(row => (Shape: row[0].GetInt32(), Order: row[1].GetInt32())))
        {
            var initial = HuntingSnapshot(content);
            var actors = Enumerable.Range(0, 3).Select(index => initial.World.Ufos[0] with
            {
                UniqueId = 9 + index,
                Id = 3 + index,
                HunterKiller = false,
                Escort = false,
                Longitude = 0.4 + index * 0.1,
                Latitude = 0.2 + index * 0.05,
                Escorting = group.Key.Shape is not (0 or 3) || index != 0,
                OriginalWaypoint = group.Key.Shape is not (0 or 3) || index != 0 ? new(1, 0.6) : null,
                TrajectoryPoint = group.Key.Shape == 3 && index == 0 ? content.RuntimeRules.UfoTrajectories[
                    content.RuntimeRules.UfoTrajectories.GetRequired(initial.World.Ufos[0].TrajectoryId)].Value.Waypoints.Count - 1 : initial.World.Ufos[0].TrajectoryPoint,
                Destination = group.Key.Shape is 0 or 3 && index == 0
                    ? new(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0,
                        group.Key.Shape == 3 ? 0.400001 : 1, group.Key.Shape == 3 ? 0.2 : 0.6)
                    : new(WorldTargetKind.Ufo, WorldTargetReference.UfoType,
                        3 + (group.Key.Shape == 2 ? index : index == 0 ? 2 : index - 1), 0, 0)
                    { UniqueId = 9 + (group.Key.Shape == 2 ? index : index == 0 ? 2 : index - 1) },
            }).ToArray();
            initial = initial with
            {
                World = initial.World with
                {
                    Ufos = orders[group.Key.Order].Select(index => actors[index]).ToArray(),
                    Missions = [initial.World.Missions[0] with { LiveUfos = 3 }],
                }
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "escort-chain-before.sav");
            foreach (var row in group)
            {
                AdvanceOne(campaign);
                var after = campaign.Capture();
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { group.Key.Shape, group.Key.Order, row[2].GetInt32() }
                    .Concat(after.World.Ufos.OrderBy(ufo => ufo.UniqueId).SelectMany(ufo => new object[]
                    {
                        decimal.Parse(ufo.Longitude.ToString("F12", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture),
                        decimal.Parse(ufo.Latitude.ToString("F12", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture),
                    })).ToArray());
                TestFixtures.AssertJsonEqual(row, actual);
                campaign = Reload(content, campaign, 81, "escort-chain-after.sav");
            }
        }
    }

    [Fact]
    public void SoldierNameChecksIncludeTransfersQueuedByOtherProduction()
    {
        var content = Load("manufacture: [{name: PENDING_STAFF, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: STR_SCIENTIST}, {name: PENDING_SOLDIER, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: RECRUIT}]\nfacilities: [{type: PENDING_WORKSHOP, mapName: LOGISTICS, workshops: 2500, personnel: 10000}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = WorldSnapshot.Empty };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Soldiers = [], Crafts = [], Research = [], Transfers = [],
            Facilities = [new("PENDING_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("PENDING_STAFF", 1111, 0, 1111, false, false, false, new Dictionary<string, int>()), new("PENDING_SOLDIER", 1000, 0, 1000, false, false, false, new Dictionary<string, int>())]
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), "Soldier production exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoldierProductionBoundsGrowthOfPendingNameChecks(bool split)
    {
        var content = Load("manufacture: [{name: NAME_WORK_A, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: RECRUIT}, {name: NAME_WORK_B, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: RECRUIT}]\nfacilities: [{type: NAME_WORK_SHOP, mapName: LOGISTICS, workshops: 1600, personnel: 10000}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = WorldSnapshot.Empty };
        ProductionSnapshot first = new("NAME_WORK_A", split ? 800 : 1600, 0, split ? 800 : 1600, false, false, false, new Dictionary<string, int>());
        initial = initial with { Bases = [initial.Bases[0] with { Soldiers = [], Crafts = [], Transfers = [], Research = [], Facilities = [new("NAME_WORK_SHOP", 1, 1, 0, 0, false, false, false)], Productions = split ? [first, first with { RuleId = "NAME_WORK_B" }] : [first] }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), "Soldier production exceeds the supported work limit.");
    }

    [Fact]
    public void SoldierAndCraftProductionShareCounterInNativeCallerOrder()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: STR_SOLDIER, speedMax: 2000, damageMax: 100, fuelMax: 0}]\nmanufacture: [{name: SHARED_PERSON, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: RECRUIT}, {name: SHARED_CRAFT, category: STR_CRAFT, time: 1, producedItems: {STR_SOLDIER: 1}}, {name: SHARED_BOTH, category: STR_CRAFT, time: 1, producedItems: {STR_SOLDIER: 1}, spawnedPersonType: RECRUIT}]\nfacilities: [{type: SHARED_WORKSHOP, mapName: LOGISTICS, workshops: 2}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionSharedSoldierCraftCounterRows"))
        {
            var order = row[0].GetInt32();
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            ProductionSnapshot person = new("SHARED_PERSON", 1, 0, 1, false, false, false, new Dictionary<string, int>());
            ProductionSnapshot craft = new("SHARED_CRAFT", 1, 0, 1, false, false, false, new Dictionary<string, int>());
            initial = initial with
            {
                NextIds = new Dictionary<string, int>(initial.NextIds) { ["STR_SOLDIER"] = 20 },
                Bases = [initial.Bases[0] with
                {
                    Soldiers = [], Research = [], Transfers = [],
                    Facilities = [.. initial.Bases[0].Facilities, new("SHARED_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = order == 0 ? [person, craft] : order == 1 ? [craft, person] : [person with { RuleId = "SHARED_BOTH" }]
                }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "shared-birth-before.sav");
            Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var born = Assert.Single(after.Bases[0].Crafts, value => value.RuleId == "STR_SOLDIER");
            var soldier = Assert.Single(after.Bases[0].Transfers).Soldier!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { order, born.Id, soldier.Id, after.NextIds["STR_SOLDIER"] });
            TestFixtures.AssertJsonEqual(row, actual);
            Assert.Equal(1, after.World.Ufos[0].Destination!.Id);
            Reload(content, campaign, 81, "shared-birth-after.sav");
        }
    }

    [Fact]
    public void ManufacturedSoldiersMatchNativeCallerAndPendingNameRetries()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionSoldierRows"))
        {
            var delay = row[0].GetInt32();
            var fixedName = row[1].GetBoolean();
            var overlay = row[2].GetBoolean();
            var pending = row[3].GetBoolean();
            var content = Load($"hireByCountryOdds: 100\nsoldiers: [{{type: COUNTRY_PRODUCT_SOLDIER, armor: ARMOR, soldierNames: [SoldierName/test.nam, SoldierName/test.nam], minStats: {{health: 30}}, maxStats: {{health: 30}}}}]\nmanufacture: [{{name: SOLDIER_PRODUCT, time: 1, producedItems: {{SUPPLY: 0}}, spawnedPersonType: COUNTRY_PRODUCT_SOLDIER, transferTimes: [{(delay < 0 ? "0" : $"0, {delay}")}], spawnedPersonName: '{(fixedName ? "FORCED" : "")}', spawnedSoldier: {{{(overlay ? "name: TEMPLATE NAME, currentStats: {health: 45}" : "")}}}}}]\nfacilities: [{{type: SOLDIER_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = WorldSnapshot.Empty };
            var owner = initial.Bases[0];
            var prior = owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "", CraftId = 0, Name = "Alex Example" } };
            initial = initial with
            {
                NextIds = new Dictionary<string, int>(initial.NextIds) { ["STR_SOLDIER"] = row[5].GetInt32() },
                Bases = [owner with
                {
                    Longitude = 0.45, Latitude = 0.21, Crafts = [], Soldiers = [], Research = [],
                    Transfers = pending ? [new(1, 2, CampaignTransferKind.Soldier, "RECRUIT", 1, prior)] : [],
                    Facilities = [.. owner.Facilities, new("SOLDIER_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("SOLDIER_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())]
                }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "soldier-production-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var transfer = after.Bases[0].Transfers[^1];
            var soldier = transfer.Soldier!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { transfer.Hours, soldier.Id, after.NextIds["STR_SOLDIER"], soldier.Personal!.Nationality, soldier.Personal.Name });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(4).Take(5).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Assert.Equal(overlay ? 45 : 30, soldier.Personal.CurrentStats["health"]);
            // The pinned Mod caller retries duplicates before applying the template
            // and final name. Pending-transfer names participate in all ten tries.
            var random = new SplitMix64RandomSource(81);
            random.Restore(initial.RandomState);
            random.NextInclusive(0, 99); // Guaranteed country selection still draws.
            var definition = content.RuntimeRules.Soldiers[content.RuntimeRules.Soldiers.GetRequired("COUNTRY_PRODUCT_SOLDIER")].Value;
            for (var attempt = 0; attempt < row[9].GetInt32(); attempt++)
                SoldierGeneration.Generate(definition, "ARMOR", 0, new HashSet<string>(), random);
            var template = content.RuntimeRules.Manufacture[content.RuntimeRules.Manufacture.GetRequired("SOLDIER_PRODUCT")].Value.SpawnedSoldierTemplate!;
            var personal = SoldierGeneration.ApplyTemplate(soldier.Personal, template, definition, content.RuntimeRules, random);
            if (!fixedName) SoldierGeneration.RegenerateName(personal, definition, random);
            Assert.Equal(random.State, after.RandomState);
            Reload(content, campaign, 81, "soldier-production-after.sav");
            AdvanceUnblocked(campaign, transfer.Hours * 720);
            Assert.Contains(campaign.Capture().Bases[0].Soldiers, value => value.Id == soldier.Id && value.Personal!.Name == soldier.Personal.Name);
            Reload(content, campaign, 81, "soldier-production-arrived.sav");
        }
    }

    [Theory]
    [InlineData("counter")]
    [InlineData("soldier")]
    [InlineData("transfer")]
    [InlineData("daily")]
    public void SoldierProductionIdentityBoundsRefuseBeforeMutation(string source)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var native = TestFixtures.Rows(oracle.RootElement, "productionSoldierRows").First();
        Assert.Equal(1, native[6].GetInt32() - native[5].GetInt32());
        var content = Load("manufacture: [{name: SOLDIER_LIMIT, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: RECRUIT}]\nresearch: [{name: SOLDIER_COUNTER_TOPIC, cost: 1, increaseCounter: [STR_SOLDIER]}]\nfacilities: [{type: SOLDIER_LIMIT_WORKSHOP, mapName: LOGISTICS, workshops: 1, labs: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, source == "daily" ? 23 : 1, 59, 55), World = WorldSnapshot.Empty };
        var owner = initial.Bases[0];
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["STR_SOLDIER"] = source == "counter" ? int.MaxValue : source == "daily" ? int.MaxValue - 1 : 1 },
            Bases = [owner with
            {
                Soldiers = source == "soldier" ? [owner.Soldiers[0] with { Id = int.MaxValue - 1 }] : [],
                Transfers = source == "transfer" ? [new(1, 2, CampaignTransferKind.Soldier, "RECRUIT", 1, owner.Soldiers[0] with { Id = int.MaxValue - 1, Personal = owner.Soldiers[0].Personal! with { CraftType = "", CraftId = 0 } })] : [],
                Research = source == "daily" ? [new("SOLDIER_COUNTER_TOPIC", 1, 0, 1)] : [],
                Facilities = [.. owner.Facilities, new("SOLDIER_LIMIT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("SOLDIER_LIMIT", 1, 0, 1, false, false, false, new Dictionary<string, int>())]
            }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "soldier-limit-before.sav");
        AssertTimeBlocked(campaign, "Soldier production exhausts the soldier identity range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoldierProductionGenerationDefectsRefuseBeforeMutation(bool bonus)
    {
        var extra = bonus ? "spawnedSoldier: {transformationBonuses: {BONUS: 2147483647}, randomTransformationBonuses: {BONUS: 1}}" : "";
        var stats = bonus ? "" : "soldiers: [{type: BAD_PRODUCTION_SOLDIER, armor: ARMOR, minStats: {health: 50}, maxStats: {health: 30}}]\n";
        var content = Load($"{stats}manufacture: [{{name: SOLDIER_DEFECT, time: 1, producedItems: {{SUPPLY: 0}}, spawnedPersonType: {(bonus ? "RECRUIT" : "BAD_PRODUCTION_SOLDIER")}, {extra}}}]\nfacilities: [{{type: SOLDIER_DEFECT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
        if (!bonus) Assert.Equal(50, content.RuntimeRules.Soldiers[content.RuntimeRules.Soldiers.GetRequired("BAD_PRODUCTION_SOLDIER")].Value.MinimumStats["health"]);
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = WorldSnapshot.Empty };
        initial = initial with { Bases = [initial.Bases[0] with { Productions = [new("SOLDIER_DEFECT", 1, 0, 1, false, false, false, new Dictionary<string, int>())], Facilities = [.. initial.Bases[0].Facilities, new("SOLDIER_DEFECT_WORKSHOP", 1, 1, 0, 0, false, false, false)] }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), bonus ? "A possible soldier production template bonus exceeds the counter range." : "Soldier generation has inverted stat bounds.");
    }

    [Fact]
    public void UnknownRewardsDoNotReserveLookupOnlyCounterEffects()
    {
        var content = Load("research: [{name: SAFE_COUNTER_SOURCE, cost: 1, lookup: LOOKUP_COUNTER_TOPIC, getOneFree: [SAFE_FREE_A, SAFE_FREE_B], increaseCounter: [SAFE_COUNTER]}, {name: SAFE_FREE_A, cost: 1}, {name: SAFE_FREE_B, cost: 1}, {name: LOOKUP_COUNTER_TOPIC, cost: 1, increaseCounter: [UNUSED_COUNTER]}]\nfacilities: [{type: SAFE_COUNTER_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), World = WorldSnapshot.Empty };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["UNUSED_COUNTER"] = int.MaxValue },
            Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("SAFE_COUNTER_LAB", 1, 1, 0, 0, false, false, false)],
                Research = [new("SAFE_COUNTER_SOURCE", 1, 0, 1)]
            }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(2, campaign.Capture().NextIds["SAFE_COUNTER"]);
        Assert.Equal(int.MaxValue, campaign.Capture().NextIds["UNUSED_COUNTER"]);
        Assert.Contains("LOOKUP_COUNTER_TOPIC", campaign.Capture().CompletedResearch);
        Reload(content, campaign, 81, "safe-lookup-counters.sav");
    }

    [Fact]
    public void UnknownResearchCounterWorkRefusesRepeatedlyWithoutMutation()
    {
        var counters = string.Join(", ", Enumerable.Repeat("WORK_COUNTER", 1001));
        var content = Load($"research: [{{name: COUNTER_WORK_TOPIC, cost: 1, getOneFree: [COUNTER_WORK_A, COUNTER_WORK_B], increaseCounter: [{counters}]}}, {{name: COUNTER_WORK_A, cost: 1, repeatable: true}}, {{name: COUNTER_WORK_B, cost: 1, repeatable: true}}]\nfacilities: [{{type: COUNTER_WORK_LAB, mapName: LOGISTICS, labs: 1}}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), World = WorldSnapshot.Empty };
        var owner = initial.Bases[0] with
        {
            Crafts = [],
            Soldiers = [],
            Facilities = [.. initial.Bases[0].Facilities, new("COUNTER_WORK_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("COUNTER_WORK_TOPIC", 1, 0, 1)]
        };
        initial = initial with { Bases = Enumerable.Range(0, 1001).Select(index => owner with { Id = index }).ToArray() };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        for (var attempt = 0; attempt < 2; attempt++)
            // The bounded reward search now runs in the earlier diary forecast.
            AssertTimeBlocked(campaign, "Research diary forecasts exceed the supported work limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResearchOnlyCounterOverflowRefusesBeforeDailyMutation(bool randomReward)
    {
        var counters = randomReward ? ", getOneFree: [OVERFLOW_REWARD, SAFE_REWARD]" : ", increaseCounter: [OTHER_COUNTER]";
        var content = Load($"research: [{{name: COUNTER_OVERFLOW_TOPIC, cost: 1{counters}}}, {{name: OVERFLOW_REWARD, cost: 1, increaseCounter: [OTHER_COUNTER]}}, {{name: SAFE_REWARD, cost: 1}}]\nfacilities: [{{type: COUNTER_OVERFLOW_LAB, mapName: LOGISTICS, labs: 1}}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), World = WorldSnapshot.Empty };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["OTHER_COUNTER"] = int.MaxValue },
            Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("COUNTER_OVERFLOW_LAB", 1, 1, 0, 0, false, false, false)],
                Research = [new("COUNTER_OVERFLOW_TOPIC", 1, 0, 1)]
            }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Research custom counters exceed the supported range.");
    }

    [Fact]
    public void DecreasedResearchCounterStillReservesSavedCraftIdentityFloor()
    {
        var content = Load("research: [{name: FLOOR_TOPIC, cost: 1, decreaseCounter: [FLOOR_CRAFT]}]\ncrafts: [{type: FLOOR_CRAFT, fuelMax: 100, refuelRate: 40}]\nmanufacture: [{name: FLOOR_BIRTH, category: STR_CRAFT, time: 1, producedItems: {FLOOR_CRAFT: 1}}]\nfacilities: [{type: FLOOR_LAB_WORKSHOP, mapName: LOGISTICS, labs: 1, workshops: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), World = WorldSnapshot.Empty };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["FLOOR_CRAFT"] = int.MaxValue - 1 },
            Bases = [initial.Bases[0] with
            {
                Crafts = [new("FLOOR_CRAFT", int.MaxValue - 1) { Logistics = CraftLogistics.LoadStarting(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("FLOOR_CRAFT")].Value, null) }], Soldiers = [],
                Facilities = [.. initial.Bases[0].Facilities, new("FLOOR_LAB_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Research = [new("FLOOR_TOPIC", 1, 0, 1)],
                Productions = [new("FLOOR_BIRTH", 1, 0, 1, false, false, false, new Dictionary<string, int>())]
            }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft production exhausts the craft identity range.");
    }

    [Fact]
    public void DifferentResearchRewardsKeepAtomicCraftIdentityForecastGuard()
    {
        var content = Load("research: [{name: RANDOM_COUNTER_SOURCE, cost: 1, getOneFree: [RANDOM_COUNTER_A, RANDOM_COUNTER_B]}, {name: RANDOM_COUNTER_A, cost: 1, increaseCounter: [RANDOM_COUNTER_CRAFT]}, {name: RANDOM_COUNTER_B, cost: 1}]\ncrafts: [{type: RANDOM_COUNTER_CRAFT, fuelMax: 100, refuelRate: 40}]\nmanufacture: [{name: RANDOM_COUNTER_BIRTH, category: STR_CRAFT, time: 1, producedItems: {RANDOM_COUNTER_CRAFT: 1}}]\nfacilities: [{type: RANDOM_COUNTER_LAB_WORKSHOP, mapName: LOGISTICS, labs: 1, workshops: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("RANDOM_COUNTER_LAB_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Research = [new("RANDOM_COUNTER_SOURCE", 1, 0, 1)],
                Productions = [new("RANDOM_COUNTER_BIRTH", 1, 0, 1, false, false, false, new Dictionary<string, int>())]
            }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft production requires daily research identity forecasts.");
    }

    [Theory]
    [InlineData("single")]
    [InlineData("sequential")]
    [InlineData("identical")]
    public void DeterministicResearchCountersSelectNativeProducedCraftIdentity(string choice)
    {
        var free = choice == "single" ? "B_COUNTER_REWARD" : choice == "sequential" ? "B_COUNTER_REWARD, C_COUNTER_OTHER" : "B_COUNTER_REWARD, B_COUNTER_REWARD";
        var content = Load($"research: [{{name: A_COUNTER_SOURCE, cost: 1, sequentialGetOneFree: {(choice == "sequential" ? "true" : "false")}, getOneFree: [{free}], increaseCounter: [COUNTER_TRACE], decreaseCounter: [COUNTER_TRACE, COUNTER_TRACE]}}, {{name: B_COUNTER_REWARD, cost: 1, increaseCounter: [COUNTER_TRACE, COUNTER_TRACE], decreaseCounter: [COUNTER_TRACE]}}, {{name: C_COUNTER_OTHER, cost: 1}}]\ncrafts: [{{type: COUNTER_TRACE, fuelMax: 100, refuelRate: 40}}]\nmanufacture: [{{name: TRACE_BIRTH, category: STR_CRAFT, time: 1, producedItems: {{COUNTER_TRACE: 1}}}}]\nfacilities: [{{type: TRACE_LAB_WORKSHOP, mapName: LOGISTICS, labs: 1, workshops: 1}}]\n");
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var sequential = TestFixtures.Rows(oracle.RootElement, "researchCounterSequentialBaseRows").ToArray();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchOrderedCounterRows").Where(row => row[1].GetBoolean()))
        {
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55) };
            var ids = new Dictionary<string, int>(initial.NextIds);
            if (row[0].GetInt32() != -1) ids["COUNTER_TRACE"] = row[0].GetInt32();
            initial = initial with
            {
                NextIds = ids,
                Bases = [initial.Bases[0] with
                {
                    Facilities = [.. initial.Bases[0].Facilities, new("TRACE_LAB_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Research = [new("A_COUNTER_SOURCE", 1, 0, 1)],
                    Productions = [new("TRACE_BIRTH", 1, 0, 1, false, false, false, new Dictionary<string, int>())]
                }]
            };
            if (row[2].GetBoolean()) initial = initial with { Bases = [initial.Bases[0], initial.Bases[0] with { Id = 1, Crafts = [], Soldiers = [], Productions = [] }] };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "counter-birth-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var completingBases = choice == "sequential" && row[2].GetBoolean() ? 2 : 1;
            Assert.Equal(completingBases, result.Events.OfType<CampaignResearchCompleted>().Count());
            var born = Assert.Single(campaign.Capture().Bases[0].Crafts, craft => craft.RuleId == "COUNTER_TRACE");
            var expected = choice == "sequential" ? sequential.Single(value => value[0].GetInt32() == row[0].GetInt32() && value[1].GetInt32() == completingBases)[2].GetInt32() : row[3].GetInt32();
            Assert.Equal(expected, born.Id);
            Assert.Equal(born.Id + 1, campaign.Capture().NextIds["COUNTER_TRACE"]);
            Reload(content, campaign, 81, "counter-birth-after.sav");
        }
    }

    [Fact]
    public void DailyResearchCounterProjectionMatchesNativePrimaryOrdering()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var bonus in new[] { false, true })
        {
            var content = Load($"research: [{{name: A_COUNTER_SOURCE, cost: 1, increaseCounter: [COUNTER_TRACE], decreaseCounter: [COUNTER_TRACE, COUNTER_TRACE]{(bonus ? ", getOneFree: [B_COUNTER_REWARD]" : "")}}}, {{name: B_COUNTER_REWARD, cost: 1, increaseCounter: [COUNTER_TRACE, COUNTER_TRACE], decreaseCounter: [COUNTER_TRACE]}}]\nfacilities: [{{type: COUNTER_TRACE_LAB, mapName: LOGISTICS, labs: 2}}]\n");
            foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchOrderedCounterRows").Where(row => row[1].GetBoolean() == bonus))
            {
                var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), World = WorldSnapshot.Empty };
                var ids = new Dictionary<string, int>(initial.NextIds);
                if (row[0].GetInt32() != -1) ids["COUNTER_TRACE"] = row[0].GetInt32();
                initial = initial with
                {
                    NextIds = ids,
                    Bases = [initial.Bases[0] with
                    {
                        Facilities = [.. initial.Bases[0].Facilities, new("COUNTER_TRACE_LAB", 1, 1, 0, 0, false, false, false)],
                        Research = [new("A_COUNTER_SOURCE", 1, 0, 1)]
                    }]
                };
                if (row[2].GetBoolean()) initial = initial with { Bases = [initial.Bases[0], initial.Bases[0] with { Id = 1, Crafts = [], Soldiers = [] }] };
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
                var before = campaign.Capture();
                long work = 0;
                Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast));
                Assert.Equivalent(before, campaign.Capture(), strict: true);
                Assert.All(forecast.CounterChanges, change => Assert.Equal("COUNTER_TRACE", change.Name));
                TestFixtures.AssertJsonEqual(row[4], JsonSerializer.SerializeToUtf8Bytes(forecast.CounterChanges.Select(change => change.Increase)));
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                Assert.Single(result.Events.OfType<CampaignResearchCompleted>());
                Assert.Equal(row[3].GetInt32(), campaign.Capture().NextIds["COUNTER_TRACE"]);
                Reload(content, campaign, 81, "counter-trace-after.sav");
            }
        }
    }

    [Fact]
    public void DailyResearchCounterCannotExhaustLaterCraftBirthIdentity()
    {
        var content = Load("research: [{name: BIRTH_COUNTER_TOPIC, cost: 1, increaseCounter: [COUNTER_CRAFT]}]\ncrafts: [{type: COUNTER_CRAFT, fuelMax: 100, refuelRate: 40}]\nmanufacture: [{name: COUNTER_BIRTH, category: STR_CRAFT, time: 1, producedItems: {COUNTER_CRAFT: 1}}]\nfacilities: [{type: COUNTER_LAB_WORKSHOP, mapName: LOGISTICS, labs: 1, workshops: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), World = WorldSnapshot.Empty };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["COUNTER_CRAFT"] = int.MaxValue - 1 },
            Bases = [initial.Bases[0] with
            {
                Crafts = [], Soldiers = [],
                Facilities = [.. initial.Bases[0].Facilities, new("COUNTER_LAB_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Research = [new("BIRTH_COUNTER_TOPIC", 1, 0, 1)],
                Productions = [new("COUNTER_BIRTH", 1, 0, 1, false, false, false, new Dictionary<string, int>())]
            }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft production exhausts the craft identity range.");
    }

    [Fact]
    public void CraftProductionIdentityScansChargeEmptyBasesAgainstWorkLimit()
    {
        var craftRows = string.Join(", ", Enumerable.Range(0, 1001).Select(index => $"{{type: EMPTY_SCAN_{index}}}"));
        var productionRows = string.Join(", ", Enumerable.Range(0, 1001).Select(index => $"{{name: EMPTY_BIRTH_{index}, category: STR_CRAFT, time: 1, producedItems: {{EMPTY_SCAN_{index}: 1}}}}"));
        var content = Load($"crafts: [{craftRows}]\nmanufacture: [{productionRows}]\nfacilities: [{{type: EMPTY_SCAN_WORKSHOP, mapName: LOGISTICS, workshops: 1001}}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = WorldSnapshot.Empty };
        var owner = initial.Bases[0] with { Crafts = [], Soldiers = [], Transfers = [], Research = [], Productions = [] };
        initial = initial with
        {
            Bases = Enumerable.Range(0, 1001).Select(index => owner with
            {
                Id = index,
                Facilities = index == 0 ? [.. owner.Facilities, new("EMPTY_SCAN_WORKSHOP", 1, 1, 0, 0, false, false, false)] : [],
                Productions = index == 0 ? Enumerable.Range(0, 1001).Select(project => new ProductionSnapshot(
                    $"EMPTY_BIRTH_{project}", 1, 0, 1, false, false, false, new Dictionary<string, int>())).ToArray() : []
            }).ToArray()
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft production exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EarlierBornCraftSuppliesLaterProductionInputBeforeHunting(int delay)
    {
        var content = Load($"crafts: [{{type: CHAIN_HUNT, fuelMax: 100, refuelRate: 40, speedMax: 2000, damageMax: 100, autoPatrol: true, refuelItem: SUPPLY}}, {{type: CHAIN_BORN, fuelMax: 100, refuelRate: 40}}]\nmanufacture: [{{name: CHAIN_BIRTH, category: STR_CRAFT, time: 1, transferTimes: [0, 0, {delay}], producedItems: {{CHAIN_BORN: 1}}}}, {{name: CHAIN_USE, time: 1, requiredItems: {{CHAIN_BORN: 1}}, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: CHAIN_WORKSHOP, mapName: LOGISTICS, workshops: 3}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        { RuleId = "CHAIN_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 } });
        initial = initial with
        {
            Time = new(1, 1, 1, 1999, 1, 59, 55),
            Bases = [initial.Bases[0] with
            {
                Items = new Dictionary<string, int>(), Soldiers = [],
                Facilities = [.. initial.Bases[0].Facilities, new("CHAIN_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("CHAIN_BIRTH", 1, 0, 1, false, false, false, new Dictionary<string, int>()),
                    new("CHAIN_USE", 2, 0, 2, false, false, false, new Dictionary<string, int>())]
            }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "birth-chain-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>(), value => value.RuleId == "CHAIN_USE");
        Assert.Equal(delay == 0 ? 2 : 1, progress.Produced);
        Assert.Equal(delay == 0 ? "STR_PRODUCTION_COMPLETE" : "STR_NOT_ENOUGH_MATERIALS", progress.StopReason);
        var owner = campaign.Capture().Bases[0];
        Assert.DoesNotContain(owner.Crafts, craft => craft.RuleId == "CHAIN_BORN");
        Assert.Equal(delay, owner.Transfers.Count);
        Assert.Equal(delay == 0 ? 1 : 0, owner.Items.GetValueOrDefault("SUPPLY"));
        Assert.Equal(1, campaign.Capture().World.Ufos[0].Destination!.Id);
        Reload(content, campaign, 81, "birth-chain-after.sav");
    }

    [Fact]
    public void ProducedCraftContinuationAndFuelMatchNativeHuntingOrder()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionCraftHuntingRows")
            .GroupBy(row => (Delay: row[0].GetInt32(), Fixed: row[1].GetBoolean(), Cost: row[4].GetInt32())))
        {
            var content = Load($"craftWeapons: [{{type: BIRTH_FIXED, launcher: SUPPLY, ammoMax: 10, rearmRate: 4}}]\ncrafts: [{{type: BIRTH_HUNT, fuelMax: 100, refuelRate: 40, speedMax: 2000, damageMax: 100, autoPatrol: true, refuelItem: SUPPLY}}, {{type: PRODUCED_CRAFT, weapons: 1, fixedWeapons: [{(group.Key.Fixed ? "BIRTH_FIXED" : "''")}], fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY}}]\nmanufacture: [{{name: BIRTH_PRODUCT, category: STR_CRAFT, time: 2, cost: {group.Key.Cost}, requiredItems: {{SUPPLY: 1}}, transferTimes: [0, 0, {group.Key.Delay}], producedItems: {{PRODUCED_CRAFT: 1}}}}]\nfacilities: [{{type: BIRTH_WORKSHOP, mapName: LOGISTICS, workshops: 4}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                { RuleId = "BIRTH_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 } });
                initial = initial with
                {
                    Time = new(1, 1, 1, 1999, 1, 59, 55),
                    Funds = [5],
                    NextIds = new Dictionary<string, int>(initial.NextIds) { ["PRODUCED_CRAFT"] = 10 },
                    Bases = [initial.Bases[0] with
                    {
                        Longitude = 0.45, Latitude = 0.21, Soldiers = [],
                        Items = row[2].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[2].GetInt32() },
                        Facilities = [.. initial.Bases[0].Facilities, new("BIRTH_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                        Productions = [new("BIRTH_PRODUCT", row[3].GetInt32() * 2, 1, 2, false, false, false, new Dictionary<string, int>())]
                    }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "craft-composed-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                var owner = campaign.Capture().Bases[0];
                var born = owner.Crafts.Where(craft => craft.RuleId == "PRODUCED_CRAFT").Concat(owner.Transfers.Select(transfer => transfer.Craft!)).ToArray();
                var existing = Assert.Single(owner.Crafts, craft => craft.RuleId == "BIRTH_HUNT").Logistics!;
                var stop = progress.StopReason == "STR_PRODUCTION_COMPLETE" ? 1 : progress.StopReason == "STR_NOT_ENOUGH_MONEY" ? 2 : progress.StopReason == "STR_NOT_ENOUGH_MATERIALS" ? 3 : 0;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                    progress.Produced, owner.Items.GetValueOrDefault("SUPPLY"), campaign.Capture().Funds[^1], stop,
                    owner.Crafts.Count(craft => craft.RuleId == "PRODUCED_CRAFT"), owner.Transfers.Count,
                    born.Select(craft => new object[] { craft.Id, craft.Logistics!.Status, craft.Logistics.Fuel, craft.Logistics.Weapons[0]?.Rearming ?? false }).ToArray(),
                    existing.Status, existing.Fuel, campaign.Capture().World.Ufos[0].Hunting ? campaign.Capture().World.Ufos[0].Destination!.Id : 0
                });
                TestFixtures.AssertJsonEqual(row.EnumerateArray().Skip(5).ToArray(), actual);
                Reload(content, campaign, 81, "craft-composed-after.sav");
            }
        }
    }

    [Theory]
    [InlineData("counter")]
    [InlineData("existing")]
    [InlineData("transfer")]
    [InlineData("arrivals")]
    [InlineData("population")]
    [InlineData("service")]
    public void CraftProductionCapacityRefusesBeforeOrdinaryWorldMutation(string failure)
    {
        var fuel = failure == "service" ? -1 : 100;
        var units = failure == "population" ? 100_001 : 1;
        var content = Load($"crafts: [{{type: CAPACITY_CRAFT, fuelMax: {fuel}, refuelRate: 40}}]\nmanufacture: [{{name: CAPACITY_BIRTH, category: STR_CRAFT, time: 1, transferTimes: [0, 0, 1], producedItems: {{CAPACITY_CRAFT: 1}}}}]\nfacilities: [{{type: CAPACITY_WORKSHOP, mapName: LOGISTICS, workshops: {units}}}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = WorldSnapshot.Empty };
        var existing = new CraftSnapshot("CAPACITY_CRAFT", int.MaxValue)
        { Logistics = CraftLogistics.LoadStarting(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("CAPACITY_CRAFT")].Value, null) };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds)
            { ["CAPACITY_CRAFT"] = failure == "counter" ? int.MaxValue : 1, ["oxcePortTransfer"] = failure == "arrivals" ? int.MaxValue : 1 },
            Bases = [initial.Bases[0] with
            {
                Crafts = failure == "existing" ? [existing] : [], Soldiers = [],
                Transfers = failure == "transfer" ? [new TransferSnapshot(2, 2, CampaignTransferKind.Craft, "CAPACITY_CRAFT", 1, Craft: existing)] : [],
                Facilities = [.. initial.Bases[0].Facilities, new("CAPACITY_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("CAPACITY_BIRTH", units, 0, units, false, false, false, new Dictionary<string, int>())]
            }]
        };
        var reason = failure switch
        {
            "service" => "Produced craft service values exceed the supported range.",
            "population" => "Craft production exceeds the supported population limit.",
            "arrivals" => "Production arrivals exhaust the transfer identity range.",
            _ => "Craft production exhausts the craft identity range."
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProducedCraftInitializationMatchesNativeCallerAndReload(bool hunting)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionCraftBirthRows"))
        {
            var fixedWeapon = row[0].GetBoolean();
            var fuel = row[1].GetInt32();
            var delay = row[2].GetInt32();
            var content = Load($"craftWeapons: [{{type: BIRTH_FIXED, launcher: SUPPLY, ammoMax: 10, rearmRate: 4}}]\ncrafts: [{{type: BIRTH_HUNT, fuelMax: 100, refuelRate: 40, speedMax: 2000, damageMax: 100, autoPatrol: true, refuelItem: SUPPLY}}, {{type: PRODUCED_CRAFT, weapons: 1, fixedWeapons: [{(fixedWeapon ? "BIRTH_FIXED" : "''")}], fuelMax: {fuel}, refuelRate: 40}}]\nmanufacture: [{{name: BIRTH_PRODUCT, category: STR_CRAFT, time: 1, transferTimes: [0, 0, {delay}], producedItems: {{PRODUCED_CRAFT: 1, ZZ_IGNORED_MISSING: 99}}}}]\nfacilities: [{{type: BIRTH_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            Assert.Single(content.RuntimeRules.Manufacture[content.RuntimeRules.Manufacture.GetRequired("BIRTH_PRODUCT")].Value.ProducedMaterials);
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            { RuleId = "BIRTH_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 } });
            initial = initial with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = hunting ? initial.World : WorldSnapshot.Empty };
            initial = initial with
            {
                NextIds = new Dictionary<string, int>(initial.NextIds) { ["PRODUCED_CRAFT"] = 10 },
                Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Crafts = hunting ? initial.Bases[0].Crafts : [], Soldiers = [],
                    Items = hunting ? new Dictionary<string, int> { ["SUPPLY"] = 1 } : new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("BIRTH_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("BIRTH_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())]
                }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "craft-birth-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var owner = campaign.Capture().Bases[0];
            var craft = delay > 0 ? Assert.Single(owner.Transfers).Craft! : Assert.Single(owner.Crafts, craft => craft.RuleId == "PRODUCED_CRAFT");
            var state = craft.Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            { craft.Id, owner.Crafts.Count(craft => craft.RuleId == "PRODUCED_CRAFT"), owner.Transfers.Count, owner.Transfers.Count == 0 ? 0 : owner.Transfers[0].Hours,
                state.Status, state.Fuel, state.Longitude, state.Latitude, state.Weapons[0]?.Ammo ?? 0,
                state.Weapons[0]?.Rearming ?? false, state.IsAutoPatrolling });
            TestFixtures.AssertJsonEqual(row.EnumerateArray().Skip(3).ToArray(), actual);
            Assert.Equal(11, campaign.Capture().NextIds["PRODUCED_CRAFT"]);
            if (hunting)
            {
                Assert.Equal(0, owner.Items.GetValueOrDefault("SUPPLY"));
                Assert.Equal(1, Assert.Single(campaign.Capture().World.Ufos).Destination!.Id);
                Assert.Equal(WorldTargetKind.Craft, campaign.Capture().World.Ufos[0].Destination!.Kind);
            }
            Reload(content, campaign, 81, "craft-birth-after.sav");
        }
    }

    [Fact]
    public void HunterInitializationMatchesPinnedConditionalDraws()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "constructorRows"))
        {
            var random = new ConstructorRandom(row[3].GetInt32());
            var result = WorldHunting.Initialize(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), random);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32(),
                result.HunterKiller, result.Mode, result.Behavior, random.Calls,
            });
            TestFixtures.AssertJsonEqual(row, actual);
        }
    }

    private sealed class ConstructorRandom(int roll) : IRandomSource
    {
        public int Calls { get; private set; }
        public int NextInclusive(int minimum, int maximum)
        {
            Assert.Equal(0, minimum);
            Assert.True(maximum is 1 or 99);
            Calls++;
            return maximum == 99 ? roll : Calls % 2 == 0 ? 1 : 0;
        }
        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException();
        public double NextUnit() => throw new InvalidOperationException();
    }

    [Fact]
    public void AttractionMatchesPinnedMethods()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var actual = new List<int>();
        var expected = new List<int>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "attractionRows"))
        {
            Assert.True(WorldHunting.TryAttraction(row[0].GetInt32(), row[1].GetBoolean(), row[2].GetInt32(),
                row[3].GetInt32(), row[4].GetInt32(), 100, out var attraction));
            actual.Add(attraction);
            expected.Add(row[5].GetInt32());
        }
        TestFixtures.AssertJsonEqual(expected, JsonSerializer.SerializeToUtf8Bytes(actual));
    }

    [Fact]
    public void SelectionMatchesPinnedSavedOrderAndOriginalTargetRules()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "selectionRows"))
        {
            var mode = row[0].GetInt32();
            var variant = row[1].GetInt32();
            var a = new WorldTargetReference(WorldTargetKind.Craft, "A", 1, 0, WorldGeometry.Nautical(100));
            var b = new WorldTargetReference(WorldTargetKind.Craft, "B", 2, 0, WorldGeometry.Nautical(100));
            if (variant == 4)
            {
                a = a with { Latitude = WorldGeometry.Nautical(301) };
                b = b with { Latitude = WorldGeometry.Nautical(300) };
            }
            if (variant == 7)
            {
                a = a with { Latitude = WorldGeometry.Nautical(301) };
                b = b with { Latitude = WorldGeometry.Nautical(302) };
            }
            if (variant == 8) { a = a with { Latitude = 0 }; b = b with { Latitude = 0 }; }
            Assert.True(WorldHunting.TryAttraction(mode, false, 0, 2000, 0, 100, out var attractionA));
            Assert.True(WorldHunting.TryAttraction(mode, false, 0, variant == 1 ? 2000 : 6000, 0, 100, out var attractionB));
            var ufo = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0, 0, UfoStatus.Flying, WorldAltitudes.Low)
            { Hunting = variant is 1 or 2 or 6, Destination = a };
            WorldHuntCraft[] crafts =
            [
                new(a, a.Position, attractionA, variant is 3 or 6, variant == 2, true),
                new(b, b.Position, attractionB, variant == 2, variant == 3, true),
            ];
            Assert.Equal(row[2].GetInt32(), WorldHunting.Select(ufo, variant == 5 ? 0 : variant == 8 ? -1 : 300, crafts)?.Id ?? 0);
        }
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 100, 2147483647)]
    [InlineData(1, 100, 2147483647)]
    public void MalformedAttractionArithmeticIsRejected(int mode, int damageMaximum, int units) =>
        Assert.False(WorldHunting.TryAttraction(mode, true, units, 6000, 50, damageMaximum, out _));
    [Fact]
    public void RetargetTransitionsMatchPinnedBackupSpeedAndRestore()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "retargetRows"))
        {
            var ufo = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0, 0, UfoStatus.Flying, WorldAltitudes.Low)
            { Speed = 900, Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)) };
            var target = new WorldTargetReference(WorldTargetKind.Craft, "SHIP", 1, 0.8, 0.3);
            Assert.True(WorldHunting.TryHunt(ufo, target, 2200, row[0].GetInt32(), out var hunted));
            var escort = WorldHunting.Escort(hunted, new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 4, 1, 0.5) { UniqueId = 10 }, 2200);
            var restored = WorldHunting.Restore(escort);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                row[0].GetInt32(), hunted.Speed, escort.Speed, restored.Destination!.Longitude,
                restored.Destination.Latitude, restored.Hunting, restored.Escorting,
            });
            TestFixtures.AssertJsonEqual(row, actual);
            Assert.Null(restored.OriginalWaypoint);
            Assert.Equal(2200, restored.Speed);
        }
    }

    [Fact]
    public void ExplicitSpeedContextMatchesPinnedFastCondition()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "fastRows"))
        {
            var speed = (CampaignTimeSpeed)row[1].GetInt32();
            var fast = row[0].GetBoolean();
            Assert.Equal(row[2].GetBoolean(), WorldHunting.RetargetDue(CampaignTimeTrigger.FiveSeconds, speed, fast));
            Assert.True(WorldHunting.RetargetDue(CampaignTimeTrigger.TenMinutes, speed, fast));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetargetOptionsKeepReferenceDefaultsCacheAndReload(bool disabled)
    {
        var overlay = disabled ? "hunterKillerFastRetarget: false\nfixedUserOptions:\n  oxceGeoSuppressRedundantHKAlert: 'false'\n" : null;
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], extraRules: overlay);
        Assert.Equal(!disabled, content.RuntimeRules.Campaign.HunterKillerFastRetarget);
        var campaign = CampaignState.Restore(CreateSnapshot(content, "Hunt settings"), content, new SplitMix64RandomSource(81));
        Assert.Equal(!disabled, campaign.Options.SuppressRedundantHunterAlert);
        Reload(content, campaign, 81, "hunting-settings.sav");
    }

    [Fact]
    public void GroundedHunterWavesInitializeBeforeGeometryAndReload()
    {
        var content = Load();
        var snapshot = CreateSnapshot(content, "Ground hunter") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_GROUND", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var random = new SplitMix64RandomSource(snapshot.RandomState);
        var expected = WorldHunting.Initialize(100, 2, 2, random);
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState));
        AdvanceOne(campaign);
        var spawned = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(spawned.HunterKiller);
        Assert.Equal(expected.Mode, spawned.HuntMode);
        Assert.Equal(expected.Behavior, spawned.HuntBehavior);
        Assert.Equal(UfoStatus.Landed, spawned.Status);
        Assert.Equal(55, spawned.SecondsRemaining);
        Reload(content, campaign, 81, "ground-hunter-spawn.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(12));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(UfoStatus.Flying, Assert.Single(campaign.Capture().World.Ufos).Status);
        Reload(content, campaign, 81, "ground-hunter-lift.sav");
    }

    [Fact]
    public void AirborneHunterSpawnsWithoutCandidatesAndFindsLaterAirborneCraft()
    {
        var content = Load();
        var snapshot = CreateSnapshot(content, "Air hunter") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState));
        AdvanceOne(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(ufo.HunterKiller);
        Assert.Equal(1, ufo.HuntMode);
        Assert.Equal(1, ufo.HuntBehavior);
        Assert.False(ufo.Hunting);
        Assert.False(ufo.Escort);
        Reload(content, campaign, 81, "air-hunter-no-target.sav");
        var craft = snapshot.Bases[0].Crafts.Single(item => item.RuleId == "SHIP");
        var active = campaign.Capture().WithCraft("SHIP", item => item with
        {
            Logistics = item.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = ufo.Longitude + 0.02,
                Latitude = ufo.Latitude,
                Destination = null,
            },
        });
        campaign = CampaignState.Restore(active, content, new SplitMix64RandomSource(active.RandomState));
        var hunt = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(hunt.Events[0]).Summary.TickCount);
        Assert.Single(hunt.Events.OfType<UfoStartedHunting>());
        Assert.DoesNotContain(hunt.Events, item => item is CampaignActionBlocked);
        Assert.Equal(craft.Id, Assert.Single(campaign.Capture().World.Ufos).Destination!.Id);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Reload(content, campaign, 81, "air-hunter-later-target.sav");
    }

    [Fact]
    public void AnAirborneHunterBornNearACraftCanAttackItInItsBirthTick()
    {
        var content = Load();
        var snapshot = HuntingSnapshot(content);
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with
            {
                Ufos = [],
                Missions = [snapshot.World.Missions[0] with
                    { RuleId = "MISSION_HUNTER_AIR", NextWave = 0, LiveUfos = 0, SpawnCountdown = 0 }],
            },
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Longitude = Degrees(10), Latitude = Degrees(8), Fuel = 1000 } });
        snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Longitude = Degrees(10), Latitude = Degrees(8) }] };
        AssertNewbornHuntRuns(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81)));
        // A hunter born within reach of the ship selects it and attacks in the same tick on some seed.
        var attacked = false;
        for (ulong seed = 1; seed <= 64 && !attacked; seed++)
        {
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.DoesNotContain(result.Events, static evt => evt is CampaignActionBlocked);
            if (result.Events.OfType<InterceptionStarted>().SingleOrDefault() is not { UfoAttacking: true } started) continue;
            attacked = true;
            Assert.Equal("SHIP", started.CraftTypeId);
            Assert.True(Assert.Single(campaign.GetQuery<ICampaignInterceptionQuery>()!.QueryInterceptions()).UfoAttacking);
        }
        Assert.True(attacked);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[[4, 0, 100]]")]
    public void HunterSpawnRejectsIncompleteTrajectoriesBeforeIndexing(string waypoints)
    {
        var content = Load(overlay: $"ufoTrajectories:\n  - id: HUNTER_INCOMPLETE\n    waypoints: {waypoints}\nalienMissions:\n  - type: HUNTER_INCOMPLETE_MISSION\n    objective: 0\n    waves:\n      - {{ufo: SPAWN_HUNTER_DEFAULTS, count: 1, trajectory: HUNTER_INCOMPLETE, timer: 9000}}\n");
        var snapshot = CreateSnapshot(content, "Incomplete hunter trajectory") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "HUNTER_INCOMPLETE_MISSION", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81)),
            "Alien mission wave spawning requires world simulation.");
    }

    [Fact]
    public void AirborneHunterSpawnIncludesRelaunchedAutoPatrolTargets()
    {
        var content = Load();
        var snapshot = CreateSnapshot(content, "Auto patrol hunter boundary");
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_REFUELLING", IsAutoPatrolling = true },
        }) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81));
        AdvanceUnblocked(campaign, 1);
        var born = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(born.Hunting);
        Assert.Equal("STR_OUT", campaign.Capture().Bases[0].Crafts.Single(craft =>
            craft.Id == born.Destination!.Id && craft.RuleId == born.Destination.TypeId).Logistics!.Status);
        Reload(content, campaign, 81, "auto-patrol-hunter-birth.sav");
    }

    [Fact]
    public void IgnoredHunterTargetsMatchThePinnedCraftMethod()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "ignoredRows"))
        {
            var actual = JsonSerializer.SerializeToUtf8Bytes(new[]
                { row[0].GetBoolean(), row[1].GetBoolean(), WorldHunting.IsIgnored(row[0].GetBoolean(), row[1].GetBoolean()) });
            TestFixtures.AssertJsonEqual(row, actual);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AirborneHunterSpawnsWhileExistingCraftAreIgnored(bool complete)
    {
        var content = Load();
        var snapshot = HuntingSnapshot(content);
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { LowFuel = !complete, MissionComplete = complete },
        }) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(ufo.HunterKiller);
        Assert.False(ufo.Hunting);
        Reload(content, campaign, 81, "ignored-target-hunter-spawn.sav");
    }

    [Fact]
    public void UndetectableAirborneCraftDoNotBlockNewHunterWaves()
    {
        var content = Load("crafts:\n  - type: SHIP\n    undetectable: true\n");
        Assert.True(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value.Undetectable);
        var snapshot = HuntingSnapshot(content) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(ufo.HunterKiller);
        Assert.False(ufo.Hunting);
        Reload(content, campaign, 81, "undetectable-target-hunter-spawn.sav");
    }

    [Fact]
    public void NewHunterRetargetWorkIsReservedBeforeTheCreationTickMutates()
    {
        var content = Load(overlay: "alienMissions:\n  - type: MASS_HUNTER_AIR\n    objective: 0\n    waves:\n      - {ufo: SPAWN_HUNTER_DEFAULTS, count: 1001, trajectory: TRAJ_SPAWN_FIXED, timer: 0}\n");
        var snapshot = CreateSnapshot(content, "Hunter work reservation");
        var craft = snapshot.Bases[0].Crafts.Single(item => item.RuleId == "SHIP");
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Bases = [snapshot.Bases[0] with
            {
                Soldiers = [],
                Crafts = Enumerable.Range(1, 1001).Select(id => craft with { Id = id }).ToArray(),
            }],
            World = new WorldSnapshot
            {
                Missions = [new(4, "MASS_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81)),
            "New UFO retargeting exceeds the supported work limit.");
    }

    [Fact]
    public void NewHunterWorkReservesFiniteCraftProductionBeforeSpawning()
    {
        var content = Load(overlay: $"alienMissions:\n  - type: PRODUCTION_HUNTER_WAVE\n    objective: 0\n    waves:\n      - {{ufo: SPAWN_HUNTER_DEFAULTS, count: 1001, trajectory: TRAJ_SPAWN_FIXED, timer: 0}}\nmanufacture:\n  - name: HUNTER_CRAFT_PRODUCT\n    category: STR_CRAFT\n    time: 1\n    producedItems: {{SHIP: 1}}\nfacilities:\n  - type: HUNTER_WORKSHOP\n    mapName: LOGISTICS\n    size: 1\n    workshops: 2000\n");
        var snapshot = CreateSnapshot(content, "Produced craft hunter work");
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 59, 55),
            Bases = [snapshot.Bases[0] with
            {
                Facilities = [.. snapshot.Bases[0].Facilities, new("HUNTER_WORKSHOP", 5, 5, 0, 0, false, false, false)],
                Productions = [new("HUNTER_CRAFT_PRODUCT", 1001, 0, 1001, false, false, false,
                    new Dictionary<string, int>())],
            }],
            World = new WorldSnapshot
            {
                Missions = [new(4, "PRODUCTION_HUNTER_WAVE", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81)),
            "New UFO retargeting exceeds the supported work limit.");
    }

    private static RuntimeContent Load(string? overlay = null) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], extraRules: overlay);

    private static CampaignSnapshot HuntingSnapshot(RuntimeContent content, bool escort = false)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 0, 0),
            ufoRuleId: escort ? "ESCORT_HUNTER" : "HUNTER", speed: 2200).Capture();
        initial = initial with
        {
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { HunterKiller = true, Escort = escort }] },
        };
        return initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = 0.45,
                Latitude = 0.21,
                Destination = null,
            },
        });
    }

    [Fact]
    public void HalfHourRefuellingAndRelaunchPrecedeHunterSelection()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: REFUEL_HUNT, speedMax: 6000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "maintenanceRows").Where(row => !row[0].GetBoolean() && !row[4].GetBoolean()))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "REFUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = row[1].GetInt32(),
                    LowFuel = row[3].GetBoolean(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, 29, 55) };
            initial = initial with { Bases = [initial.Bases[0] with { Longitude = 0.45, Latitude = 0.21 }] };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            campaign = Reload(content, campaign, 81, "refuel-hunting-before.sav");
            Assert.Empty(campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "REFUEL_HUNT").Logistics!.Weapons);
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var ship = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "REFUEL_HUNT").Logistics!;
            Assert.Equal(row[6].GetString(), ship.Status);
            Assert.Equal(row[5].GetInt32() - (ship.Status == "STR_OUT" ? 60 : 0), ship.Fuel);
            Assert.Equal(Math.Max(0, row[10].GetInt32() - 1), ship.Takeoff);
            Assert.Equal(row[11].GetInt32() != 0, Assert.Single(after.World.Ufos).Hunting);
            Assert.Equal(row[11].GetInt32() != 0 ? 1 : 0, result.Events.OfType<UfoStartedHunting>().Count());
            Reload(content, campaign, 81, "refuel-hunting-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HalfHourItemRefuellingPrecedesHunterSelection(bool hourly)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: ITEM_REFUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "maintenanceRows").Where(row => row[0].GetBoolean() && !row[4].GetBoolean()))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "ITEM_REFUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = row[1].GetInt32(),
                    LowFuel = row[3].GetBoolean(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, hourly ? 59 : 29, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            { Longitude = 0.45, Latitude = 0.21, Items = row[2].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[2].GetInt32() } }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "item-refuel-hunt-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "ITEM_REFUEL_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                craft.Fuel + (craft.Status == "STR_OUT" ? 1 : 0), craft.Status,
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.LowFuel,
                result.Events.OfType<CraftArrivalServiceMessage>().Any(message => message.Message == "STR_NOT_ENOUGH_ITEM_TO_REFUEL_CRAFT_AT_BASE"),
                craft.Takeoff + (craft.Status == "STR_OUT" ? 1 : 0),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "item-refuel-hunt-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedFuelStocksPreserveCraftOrderBeforeHunting(bool hourly)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: SHARED_REFUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "sharedFuelRows"))
        {
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, hourly ? 59 : 29, 55) };
            var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
            CraftSnapshot Craft(int id, int fuel, bool auto) => template with
            {
                RuleId = "SHARED_REFUEL_HUNT",
                Id = id,
                Logistics = template.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = fuel,
                    IsAutoPatrolling = auto,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21,
                Items = row[1].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[1].GetInt32() },
                Crafts = [Craft(1, row[0].GetInt32(), row[2].GetBoolean()), Craft(2, 60, true)],
                Soldiers = [],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "shared-refuel-hunt-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var first = after.Bases[0].Crafts[0].Logistics!;
            var second = after.Bases[0].Crafts[1].Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                first.Fuel + (first.Status == "STR_OUT" ? 1 : 0), first.Status,
                second.Fuel + (second.Status == "STR_OUT" ? 1 : 0), second.Status,
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "shared-refuel-hunt-after.sav");
        }
    }

    [Fact]
    public void HourlyMaintenancePreservesRepairAndRearmDelayBeforeHunting()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: HOURLY_REFUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, repairRate: 10, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "hourlyMaintenanceRows"))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "HOURLY_REFUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = row[0].GetString()!,
                    Fuel = row[1].GetInt32(),
                    Damage = row[2].GetInt32(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            initial = initial with { Bases = [initial.Bases[0] with { Longitude = 0.45, Latitude = 0.21 }] };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "hourly-hunt-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HOURLY_REFUEL_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                craft.Fuel + (craft.Status == "STR_OUT" ? 20 : 0), craft.Status, craft.Damage,
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "hourly-hunt-after.sav");
        }
    }

    [Fact]
    public void GroundedIncomingAutoPatrolRelaunchesBeforeHunting()
    {
        var content = Load("crafts: [{type: INCOMING_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var incoming = template with
        {
            RuleId = "INCOMING_HUNT",
            Id = 2,
            Logistics = template.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = [],
            },
        };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21,
            Crafts = initial.Bases[0].Crafts.Where(craft => craft.RuleId != "SHIP").ToArray(),
            Transfers = [new(1, 1, CampaignTransferKind.Craft, "INCOMING_HUNT", 1, Craft: incoming)],
        }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "INCOMING_HUNT").Logistics!;
        Assert.Equal("STR_OUT", craft.Status);
        Assert.Equal(80, craft.Fuel);
        Assert.Equal(59, craft.Takeoff);
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("INCOMING_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "incoming-hunt.sav");
    }

    [Fact]
    public void IncomingCraftCheckupAndRelaunchMatchNative()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: NATIVE_INCOMING_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "incomingCraftRows"))
        {
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
            var incoming = template with
            {
                RuleId = "NATIVE_INCOMING_HUNT",
                Id = 2,
                Logistics = template.Logistics! with
                {
                    Status = row[1].GetString()!,
                    Fuel = row[2].GetInt32(),
                    Damage = row[3].GetInt32(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Longitude = 0.9,
                    Latitude = 0.3,
                    Weapons = [],
                },
            };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Crafts = [], Soldiers = [],
                Transfers = [new(1, row[0].GetInt32(), CampaignTransferKind.Craft, "NATIVE_INCOMING_HUNT", 1, Craft: incoming)],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "native-incoming-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var delivered = after.Bases[0].Transfers.Count == 0;
            var craft = delivered ? Assert.Single(after.Bases[0].Crafts).Logistics! : Assert.Single(after.Bases[0].Transfers).Craft!.Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                delivered, craft.Status, craft.Fuel, craft.Damage, craft.Longitude, craft.Latitude,
                craft.Takeoff + (delivered && craft.Status == "STR_OUT" ? 1 : 0),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(4).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "native-incoming-after.sav");
        }
    }

    [Fact]
    public void HunterCombatWithIncomingCraftIsGuardedBeforeDelivery()
    {
        var content = Load(overlay: "crafts: [{type: CLOSE_INCOMING_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var incoming = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP") with
        {
            RuleId = "CLOSE_INCOMING_HUNT",
            Id = 2,
            Logistics = new(60, 0, "STR_REFUELLING", [], new Dictionary<string, int>(), [])
            { IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 },
        };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.4, Latitude = 0.2, Crafts = [], Soldiers = [],
            Transfers = [new(1, 1, CampaignTransferKind.Craft, "CLOSE_INCOMING_HUNT", 1, Craft: incoming)],
        }]
        };
        AssertInterceptionStarts(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)));
    }

    [Fact]
    public void SuppliedFuelAndAmmunitionPrecedeHalfHourHunterSelection()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: SUPPLIED_FUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true, weapons: 1}]\ncraftWeapons: [{type: HUNT_TRANSFER_GUN, ammoMax: 10, launcher: SUPPLY, clip: SUPPLY}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "suppliedFuelRows"))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "SUPPLIED_FUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = row[1].GetString()!,
                    Fuel = row[2].GetInt32(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = row[4].GetBoolean() ? [new("HUNT_TRANSFER_GUN", 0)] : [],
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21,
                Items = row[3].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[3].GetInt32() },
                Transfers = [new(1, row[0].GetInt32(), CampaignTransferKind.Item, "SUPPLY", 1)],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "supplied-fuel-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SUPPLIED_FUEL_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                after.Bases[0].Transfers.Count == 0, craft.Status, craft.Fuel,
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "supplied-fuel-after.sav");
        }
    }

    [Fact]
    public void ArrivalItemReuseWorkIsBoundedWithoutUfos()
    {
        var content = Load();
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = new() };
        var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Soldiers = [],
            Crafts = Enumerable.Range(1, 1001).Select(id => template with
            { Id = id, Logistics = template.Logistics! with { Status = "STR_READY", Weapons = [] } }).ToArray(),
            Transfers = Enumerable.Range(1, 1001).Select(id =>
                new TransferSnapshot(id, 1, CampaignTransferKind.Item, "SUPPLY", 1)).ToArray(),
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Arrival item reuse exceeds the supported work limit.");
    }

    [Fact]
    public void CombinedCraftAndItemTransfersReuseAfterAllArrivals()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: COMBINED_TRANSFER_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true, weapons: 1}]\ncraftWeapons: [{type: HUNT_COMBINED_GUN, ammoMax: 10, launcher: SUPPLY, clip: SUPPLY}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "combinedTransferRows"))
        {
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            var incoming = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP") with
            {
                RuleId = "COMBINED_TRANSFER_HUNT",
                Id = 2,
                Logistics = new(row[2].GetInt32(), 0, "STR_REFUELLING",
                    row[1].GetBoolean() ? [new("HUNT_COMBINED_GUN", 0)] : [], new Dictionary<string, int>(), [])
                { IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 },
            };
            var itemTransfer = new TransferSnapshot(1, 1, CampaignTransferKind.Item, "SUPPLY", 1);
            var craftTransfer = new TransferSnapshot(2, 1, CampaignTransferKind.Craft, "COMBINED_TRANSFER_HUNT", 1, Craft: incoming);
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(), Crafts = [], Soldiers = [],
                Transfers = row[0].GetBoolean() ? [itemTransfer, craftTransfer] : [craftTransfer, itemTransfer],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "combined-transfer-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            Assert.Empty(after.Bases[0].Transfers);
            var craft = Assert.Single(after.Bases[0].Crafts).Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                craft.Status, craft.Fuel, after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                craft.Weapons.Any(weapon => weapon is { Rearming: true }),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "combined-transfer-after.sav");
        }
    }

    [Fact]
    public void CraftAmmunitionSharesFuelStocksWithoutForecastRandomDraws()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "ammunitionFuelRows")
            .GroupBy(row => (Clip: row[0].GetInt32(), Saving: row[1].GetBoolean())))
        {
            var content = Load($"items: [{{type: HUNT_SHARED_AMMO, clipSize: {group.Key.Clip}}}]\ncraftWeapons: [{{type: HUNT_AMMO_GUN, ammoMax: 10, rearmRate: 4, bulletSaving: {group.Key.Saving.ToString().ToLowerInvariant()}, launcher: SUPPLY, clip: HUNT_SHARED_AMMO}}]\ncrafts: [{{type: HUNT_AMMO_CONSUMER, speedMax: 2000, damageMax: 100, fuelMax: 100, weapons: 1}}, {{type: HUNT_AMMO_FUEL, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: HUNT_SHARED_AMMO, autoPatrol: true}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
                var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
                var gun = template with
                {
                    RuleId = "HUNT_AMMO_CONSUMER",
                    Id = 2,
                    Logistics = template.Logistics! with
                    {
                        Status = "STR_REARMING",
                        Fuel = 100,
                        Weapons = [new("HUNT_AMMO_GUN", row[2].GetInt32(), Rearming: true)],
                    },
                };
                var fuel = template with
                {
                    RuleId = "HUNT_AMMO_FUEL",
                    Id = 1,
                    Logistics = template.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3,
                    },
                };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Crafts = [gun, fuel], Soldiers = [],
                    Items = row[3].GetInt32() == 0 ? new Dictionary<string, int>() :
                        new Dictionary<string, int> { ["HUNT_SHARED_AMMO"] = row[3].GetInt32() },
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "ammo-fuel-before.sav");
                if (row[11].GetInt32() != 0 && row[3].GetInt32() == 1)
                {
                    AssertTimeBlocked(campaign, "UFO retargeting requires stochastic craft ammunition stock forecasts.");
                    continue;
                }
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var weapon = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_AMMO_CONSUMER").Logistics!.Weapons[0]!;
                var refuelled = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_AMMO_FUEL").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                    weapon.Ammo, weapon.Rearming, after.Bases[0].Items.GetValueOrDefault("HUNT_SHARED_AMMO"),
                    refuelled.Status, refuelled.Fuel,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
                });
                var native = row[11].GetInt32() == 0 ? row : group.Single(candidate =>
                    candidate[2].GetInt32() == row[2].GetInt32() && candidate[3].GetInt32() == row[3].GetInt32() &&
                    candidate[7].GetInt32() == after.Bases[0].Items.GetValueOrDefault("HUNT_SHARED_AMMO"));
                var expected = JsonSerializer.SerializeToUtf8Bytes(native.EnumerateArray().Skip(5).Take(6).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "ammo-fuel-after.sav");
            }
        }
    }

    [Fact]
    public void FacilityAmmunitionAndCraftFuelShareNativeStockOrder()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: FACILITY_FUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nfacilities: [{type: SHARED_HUNT_FACILITY, mapName: LOGISTICS, ammoMax: 2, rearmRate: 1, ammoItem: SUPPLY}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "facilityFuelRows"))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "FACILITY_FUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = 60,
                    Weapons = [],
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21,
                Items = row[0].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[0].GetInt32() },
                Facilities = [.. initial.Bases[0].Facilities, new("SHARED_HUNT_FACILITY", 1, 1,
                    row[2].GetInt32(), row[1].GetInt32(), row[3].GetBoolean(), true, false)],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "facility-fuel-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var facility = after.Bases[0].Facilities.Single(facility => facility.RuleId == "SHARED_HUNT_FACILITY");
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "FACILITY_FUEL_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                facility.Ammo, facility.AmmoMissingReported, result.Events.OfType<FacilityServiceMessage>().Any(),
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(4).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "facility-fuel-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverdueUndeliveredTransfersAreRefusedBeforeUnsafeNativeArrival(bool hunter)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "invalidArrivalRows"))
        {
            Assert.True(row[2].GetBoolean());
            Assert.False(row[3].GetBoolean());
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            initial = initial with
            {
                World = hunter ? initial.World : new(),
                Bases = [initial.Bases[0] with
                { Transfers = [new(1, row[0].GetInt32(), CampaignTransferKind.Item, "SUPPLY", 1)] }],
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "overdue-arrival.sav");
            AssertTimeBlocked(campaign, "Undelivered transfer arrival requires positive remaining hours.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StockFreeDailyResearchAllowsFuelAndAmmunitionHunterTargets(bool ammunition)
    {
        var fuelItem = ammunition ? "" : ", refuelItem: SUPPLY";
        var content = Load($"crafts: [{{type: STOCK_FREE_RESEARCH_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuelItem}}}]\ncraftWeapons: [{{type: STOCK_FREE_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nresearch: [{{name: STOCK_FREE_TOPIC, cost: 1, getOneFree: [STOCK_FREE_BONUS]}}, {{name: STOCK_FREE_BONUS, cost: 1}}]\nfacilities: [{{type: STOCK_FREE_LAB, mapName: LOGISTICS, labs: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "STOCK_FREE_RESEARCH_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = ammunition ? [new("STOCK_FREE_GUN", 0)] : [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21,
            Items = new Dictionary<string, int> { ["SUPPLY"] = 1 },
            Facilities = [.. initial.Bases[0].Facilities, new("STOCK_FREE_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("STOCK_FREE_TOPIC", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "stock-free-research-before.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Contains("STOCK_FREE_TOPIC", after.CompletedResearch);
        Assert.Contains("STOCK_FREE_BONUS", after.CompletedResearch);
        Assert.Equal(ammunition ? 1 : 0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("STOCK_FREE_RESEARCH_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "stock-free-research-after.sav");
    }

    [Fact]
    public void DailyResearchAmmunitionReusePrecedesHuntingWithoutItemFuel()
    {
        var content = Load("crafts: [{type: RESEARCH_AMMO_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1}]\ncraftWeapons: [{type: RESEARCH_AMMO_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}]\nresearch: [{name: RESEARCH_AMMO_DELIVERY, cost: 1, spawnedItem: SUPPLY}]\nfacilities: [{type: RESEARCH_AMMO_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "RESEARCH_AMMO_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [new("RESEARCH_AMMO_GUN", 0)],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("RESEARCH_AMMO_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("RESEARCH_AMMO_DELIVERY", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "research-ammo-hunt.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "RESEARCH_AMMO_HUNT").Logistics!;
        Assert.Equal("STR_REARMING", craft.Status);
        Assert.Equal(60, craft.Fuel);
        Assert.True(craft.Weapons[0]!.Rearming);
        Assert.False(after.World.Ufos[0].Hunting);
        Assert.Equal(1, after.Bases[0].Items["SUPPLY"]);
        Reload(content, campaign, 81, "research-ammo-hunt-after.sav");
    }

    [Fact]
    public void OtherBaseResearchCompletionCanRefundFuelBeforeHunting()
    {
        var content = Load("crafts: [{type: RESEARCH_FUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nresearch: [{name: RESEARCH_FUEL_RETURN, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY}]\nfacilities: [{type: RESEARCH_FUEL_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "RESEARCH_FUEL_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        var owner = initial.Bases[0] with
        {
            Longitude = 0.45,
            Latitude = 0.21,
            Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("RESEARCH_FUEL_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("RESEARCH_FUEL_RETURN", 1, 0, 100)],
        };
        initial = initial with
        {
            Bases = [owner, owner with
        {
            Id = 1, Name = "Research headquarters", Crafts = [], Soldiers = [], Transfers = [],
            Research = [new("RESEARCH_FUEL_RETURN", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "cross-base-research-fuel.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var predicted = campaign.Capture();
        Assert.True(predicted.World.Ufos[0].Hunting);
        Assert.Equal("RESEARCH_FUEL_HUNT", predicted.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "cross-base-research-fuel-after.sav");
        var control = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { HunterKiller = false }] } };
        campaign = CampaignState.Restore(control, content, new SplitMix64RandomSource(81));
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Empty(after.Bases[0].Research);
        Assert.Equal("STR_OUT", after.Bases[0].Crafts.Single(craft => craft.RuleId == "RESEARCH_FUEL_HUNT").Logistics!.Status);
        Assert.Equal(99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "RESEARCH_FUEL_HUNT").Logistics!.Fuel);
    }

    [Fact]
    public void ResearchCancellationRefundMatchesNativeFinishedAndHeldState()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchRefundRows")
            .GroupBy(row => (Holding: row[0].GetBoolean(), Returns: row[1].GetBoolean())))
        {
            var content = Load($"research: [{{name: NATIVE_HELD_RETURN, cost: 1, needItem: {group.Key.Holding.ToString().ToLowerInvariant()}, destroyItem: {(group.Key.Holding && !group.Key.Returns).ToString().ToLowerInvariant()}, returnsItem: {group.Key.Returns.ToString().ToLowerInvariant()}, neededItem: SUPPLY}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content) with { World = new() };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Items = row[3].GetInt32() == 0 ? new Dictionary<string, int>() :
                        new Dictionary<string, int> { ["SUPPLY"] = row[3].GetInt32() },
                    Research = [new("NATIVE_HELD_RETURN", 0, row[2].GetBoolean() ? 1 : 0, 1)],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "native-held-before.sav");
                Assert.IsType<CampaignResearchChanged>(Assert.Single(campaign.Execute(
                    new ConfigureResearchProject(0, "NATIVE_HELD_RETURN", 0, Cancel: true)).Events));
                var after = campaign.Capture();
                Assert.Empty(after.Bases[0].Research);
                Assert.Equal(row[4].GetInt32(), after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
                Reload(content, campaign, 81, "native-held-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void CompletingHeldResearchRefundFeedsHalfHourHunterSelection(int stock)
    {
        var content = Load("crafts: [{type: DIRECT_REFUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nresearch: [{name: DIRECT_REFUND_TOPIC, cost: 1, needItem: true, returnsItem: true, neededItem: SUPPLY}]\nfacilities: [{type: DIRECT_REFUND_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "DIRECT_REFUND_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Items = stock == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = stock },
            Facilities = [.. initial.Bases[0].Facilities, new("DIRECT_REFUND_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("DIRECT_REFUND_TOPIC", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "direct-refund-before.sav");
        if (stock == int.MaxValue)
        {
            AssertTimeBlocked(campaign, "Research refunds exceed the supported stock range.");
            return;
        }
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Contains("DIRECT_REFUND_TOPIC", after.CompletedResearch);
        Assert.Equal(0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
        Assert.Equal(99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "DIRECT_REFUND_HUNT").Logistics!.Fuel);
        Assert.Equal("DIRECT_REFUND_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "direct-refund-after.sav");
    }

    [Fact]
    public void FixedResearchDeliveriesMatchNativeStockAndReuseOrder()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchSpawnRows")
            .GroupBy(row => (Fuel: row[0].GetBoolean(), Main: row[1].GetBoolean(), Count: row[2].GetInt32(), Duplicates: row[3].GetInt32())))
        {
            var fuel = group.Key.Fuel ? ", refuelItem: SUPPLY" : "";
            var items = "MISSING" + string.Concat(Enumerable.Repeat(", SUPPLY", group.Key.Duplicates));
            var content = Load($"crafts: [{{type: FIXED_RESEARCH_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuel}}}]\ncraftWeapons: [{{type: FIXED_RESEARCH_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nresearch: [{{name: FIXED_RESEARCH_DELIVERY, cost: 1, spawnedItem: {(group.Key.Main ? "SUPPLY" : "MISSING")}, spawnedItemCount: {group.Key.Count}, spawnedItemList: [{items}]}}]\nfacilities: [{{type: FIXED_RESEARCH_LAB, mapName: LOGISTICS, labs: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "FIXED_RESEARCH_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = group.Key.Fuel ? [] : [new("FIXED_RESEARCH_GUN", 0)],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3,
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 23, 59, 55) };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21,
                    Items = row[4].GetInt32() == 0 ? new Dictionary<string, int>() :
                        new Dictionary<string, int> { ["SUPPLY"] = row[4].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("FIXED_RESEARCH_LAB", 1, 1, 0, 0, false, false, false)],
                    Research = [new("FIXED_RESEARCH_DELIVERY", 1, 0, 1)],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "fixed-research-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "FIXED_RESEARCH_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                    result.Events.OfType<SuppliesArrived>().Sum(arrived => arrived.TransferIds.Count),
                    after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    craft.Weapons.Count == 0 ? false : craft.Weapons[0]?.Rearming ?? false,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
                });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.Empty(after.Bases[0].Transfers);
                Reload(content, campaign, 81, "fixed-research-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResearchDeliveryLimitsStopBeforeAnyTickMutation(bool identities)
    {
        var content = Load("crafts: [{type: LIMITED_RESEARCH_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nresearch: [{name: LIMITED_RESEARCH_DELIVERY, cost: 1, spawnedItem: SUPPLY}]\nfacilities: [{type: LIMITED_RESEARCH_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "LIMITED_RESEARCH_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21,
            Items = identities ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue },
            Facilities = [.. initial.Bases[0].Facilities, new("LIMITED_RESEARCH_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("LIMITED_RESEARCH_DELIVERY", 1, 0, 1)],
        }]
        };
        if (identities) initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = int.MaxValue },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "research-delivery-limit.sav");
        AssertTimeBlocked(campaign, identities ? "Research arrivals exhaust the transfer identity range." :
            "Research arrivals exceed the supported stock range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledHeldProjectCannotFundAnUnforecastHunterInterception(bool close)
    {
        var content = Load("crafts: [{type: DISABLED_REFUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nresearch: [{name: DISABLED_REFUND_TOPIC, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "DISABLED_REFUND_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            ResearchRuleStatus = new Dictionary<string, int> { ["DISABLED_REFUND_TOPIC"] = 2 },
            Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                Research = [new("DISABLED_REFUND_TOPIC", 0, 0, 100)],
            }],
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { Longitude = close ? 0.45 : 0.4, Latitude = close ? 0.21 : 0.2 }] },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "disabled-refund-hunt.sav");
        if (close)
        {
            AssertInterceptionStarts(campaign);
            return;
        }
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Empty(after.Bases[0].Research);
        Assert.DoesNotContain("DISABLED_REFUND_TOPIC", after.CompletedResearch);
        Assert.Equal(99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "DISABLED_REFUND_HUNT").Logistics!.Fuel);
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("DISABLED_REFUND_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "disabled-refund-hunt-after.sav");
    }

    [Fact]
    public void LookupDiscoveryDoesNotSpawnOrCancelPrimaryStockEffects()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchPrimaryRows").Where(row => !row[1].GetBoolean()))
        {
            var direct = row[0].GetBoolean();
            var content = Load($"crafts: [{{type: LOOKUP_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: LOOKUP_SOURCE, cost: 1, lookup: LOOKUP_TOPIC, spawnedItem: {(direct ? "SUPPLY" : "")}}}, {{name: LOOKUP_TOPIC, cost: 100, spawnedItem: SUPPLY, spawnedItemCount: 100, needItem: true, returnsItem: true, neededItem: SUPPLY}}]\nfacilities: [{{type: LOOKUP_LAB, mapName: LOGISTICS, labs: 2}}]\n");
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "LOOKUP_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = 60,
                    Weapons = [],
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3
                },
            }) with
            { Time = new(1, 1, 1, 1999, 23, 59, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                Facilities = [.. initial.Bases[0].Facilities, new("LOOKUP_LAB", 1, 1, 0, 0, false, false, false)],
                Research = [new("LOOKUP_SOURCE", 1, 0, 1), new("LOOKUP_TOPIC", 1, 0, 100)],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "lookup-stocks-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            Assert.Contains("LOOKUP_TOPIC", after.CompletedResearch);
            Assert.Equal("LOOKUP_TOPIC", Assert.Single(after.Bases[0].Research).RuleId);
            Assert.Equal(row[2].GetInt32(), result.Events.OfType<SuppliesArrived>().Sum(arrived => arrived.TransferIds.Count));
            Assert.Equal(0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
            Assert.Equal(direct, after.World.Ufos[0].Hunting);
            Assert.Equal(direct ? 99 : 60, after.Bases[0].Crafts.Single(craft => craft.RuleId == "LOOKUP_HUNT").Logistics!.Fuel);
            Reload(content, campaign, 81, "lookup-stocks-after.sav");
        }
    }

    [Fact]
    public void DailyResearchDiscoveryForecastBypassesTheStockOnlyShortcut()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var row = TestFixtures.Rows(oracle.RootElement, "researchRewardRows").First(value =>
            !value[0].GetBoolean() && !value[1].GetBoolean() && value[2].GetInt32() == 0 &&
            value[3].GetInt32() == 0 && !value[4].GetBoolean());
        Assert.Equal(1, row[5].GetInt32());
        var content = Load("research: [{name: STOCK_FREE_SOURCE, cost: 1, getOneFree: [STOCK_FREE_REWARD]}, {name: STOCK_FREE_REWARD, cost: 1}]\nfacilities: [{type: STOCK_FREE_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            World = new(),
            Bases = [initial.Bases[0] with
            {
                Research = [new("STOCK_FREE_SOURCE", 1, 0, 1)],
                Facilities = [.. initial.Bases[0].Facilities, new("STOCK_FREE_LAB", 1, 1, 0, 0, false, false, false)],
            }],
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "stock-free-before.sav");
        var before = campaign.Capture();
        long work = 0;
        Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var stockOnly));
        Assert.Null(stockOnly.CompletedResearch);
        long limitedWork = 1_000_000;
        Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref limitedWork, out var refused, includeDiscoveries: true));
        Assert.Null(refused.CompletedResearch);
        Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var discovery, includeDiscoveries: true));
        Assert.NotNull(discovery.CompletedResearch);
        Assert.Contains("STOCK_FREE_REWARD", discovery.CompletedResearch);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(campaign.Capture().CompletedResearch.Order(StringComparer.Ordinal), discovery.CompletedResearch.Order(StringComparer.Ordinal));
        Reload(content, campaign, 81, "stock-free-after.sav");
    }

    [Theory]
    [InlineData()]
    [InlineData(true)]
    public void ResearchDiscoveryAndDisabledRefundsMatchNativeBeforeHunting(bool priced = false)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchDiscoveryRows").GroupBy(row => row[0].GetInt32()))
        {
            var mode = group.Key;
            var source = mode is 0 or 3 ? ", disables: [DISCOVERY_HELD]" : mode == 1 ? ", lookup: DISCOVERY_LOOKUP" : "";
            var lookup = mode == 1 ? ", disables: [DISCOVERY_HELD]" : "";
            var automatic = mode == 2 ? ", disables: [DISCOVERY_HELD]" : mode == 3 ? ", reenables: [DISCOVERY_HELD]" : "";
            var priceOverlay = priced ? "items: [{type: DISCOVERY_PRICE_ITEM, scripts: {sellCostItem: 'var ptr RuleResearch topic; var int known; var int held; rules.getRuleResearch topic \"DISCOVERY_AUTO\"; geoscape_game.isResearched known topic; rules.getRuleResearch topic \"DISCOVERY_HELD\"; geoscape_game.isResearched held topic; mul held 10; add known held; return known;'}}]\nmanufacture: [{name: DISCOVERY_PRICE_PRODUCT, time: 1, producedItems: {DISCOVERY_PRICE_ITEM: 1}}]\n" : "";
            var content = Load($"crafts: [{{type: DISCOVERY_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: DISCOVERY_SOURCE, cost: 1{source}}}, {{name: DISCOVERY_LOOKUP, cost: 1{lookup}}}, {{name: DISCOVERY_AUTO, cost: 0, dependencies: [DISCOVERY_SOURCE]{automatic}}}, {{name: DISCOVERY_HELD, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY}}]\nfacilities: [{{type: DISCOVERY_LAB, mapName: LOGISTICS, labs: 1, workshops: 1}}]\n" + priceOverlay);
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "DISCOVERY_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 23, 59, 55), CompletedResearch = row[1].GetBoolean() ? ["DISCOVERY_SOURCE"] : [] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("DISCOVERY_LAB", 1, 1, 0, 0, false, false, false)],
                    Research = [new("DISCOVERY_SOURCE", 1, 0, 1), new("DISCOVERY_HELD", 0, row[2].GetBoolean() ? 100 : 0, 100)],
                    Productions = priced ? [new("DISCOVERY_PRICE_PRODUCT", 1, 0, 1, false, true, false, new Dictionary<string, int>())] : [],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "discovery-refund-before.sav");
                var before = campaign.Capture();
                long work = 0;
                Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast, includeDiscoveries: true));
                Assert.NotNull(forecast.CompletedResearch);
                Assert.Equivalent(before, campaign.Capture(), strict: true);
                Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                Assert.Equal(after.CompletedResearch.Order(StringComparer.Ordinal), forecast.CompletedResearch.Order(StringComparer.Ordinal));
                var fuel = after.Bases[0].Crafts.Single(craft => craft.RuleId == "DISCOVERY_HUNT").Logistics!.Fuel;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                    fuel == 99 ? 1 : 0, after.ResearchRuleStatus.GetValueOrDefault("DISCOVERY_HELD"),
                    after.CompletedResearch.Contains("DISCOVERY_AUTO"),
                });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.Equal(row[3].GetInt32() != 0, after.World.Ufos[0].Hunting);
                if (priced)
                    Assert.Equal(initial.Funds[^1] + (row[5].GetBoolean() ? 1 : 0) +
                        (row[2].GetBoolean() && row[4].GetInt32() != 2 ? 10 : 0), after.Funds[^1]);
                Reload(content, campaign, 81, "discovery-refund-after.sav");
            }
        }
    }

    [Fact]
    public void DeterministicFreeResearchStocksAndCancellationMatchNative()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchRewardRows")
            .GroupBy(row => (Sequential: row[0].GetBoolean(), Duplicate: row[1].GetBoolean())))
        {
            var content = Load($"crafts: [{{type: REWARD_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: REWARD_SOURCE, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY, sequentialGetOneFree: {group.Key.Sequential.ToString().ToLowerInvariant()}, getOneFree: [{(group.Key.Duplicate ? "REWARD_A, REWARD_A" : "REWARD_A")}], getOneFreeProtected: {{REWARD_GATE: [REWARD_B]}}}}, {{name: REWARD_A, cost: 1, spawnedItem: SUPPLY}}, {{name: REWARD_B, cost: 1, spawnedItem: SUPPLY, spawnedItemCount: 2}}, {{name: REWARD_GATE, cost: 1}}]\nfacilities: [{{type: REWARD_LAB, mapName: LOGISTICS, labs: 1}}]\n");
            foreach (var row in group)
            {
                List<string> completed = [];
                if (row[2].GetInt32() == 1) completed.Add("REWARD_A");
                if (row[3].GetInt32() == 1) completed.Add("REWARD_B");
                if (row[4].GetBoolean()) completed.Add("REWARD_GATE");
                Dictionary<string, int> statuses = new(StringComparer.Ordinal);
                if (row[2].GetInt32() == 2) statuses.Add("REWARD_A", 2);
                if (row[3].GetInt32() == 2) statuses.Add("REWARD_B", 2);
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "REWARD_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 23, 59, 55), CompletedResearch = completed, ResearchRuleStatus = statuses };
                var owner = initial.Bases[0] with
                {
                    Longitude = 0.45,
                    Latitude = 0.21,
                    Items = new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("REWARD_LAB", 1, 1, 0, 0, false, false, false)],
                    Research = [new("REWARD_SOURCE", 1, 99, 100)],
                };
                initial = initial with
                {
                    Bases = [owner, owner with { Id = 1, Crafts = [], Soldiers = [],
                    Research = [new("REWARD_SOURCE", 0, 0, 100)] }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "reward-stocks-before.sav");
                var before = campaign.Capture();
                long work = 0;
                var supported = campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast, includeDiscoveries: true);
                Assert.Equivalent(before, campaign.Capture(), strict: true);
                if (!group.Key.Sequential && row[2].GetInt32() == 0 && row[3].GetInt32() == 0 && row[4].GetBoolean())
                {
                    Assert.False(supported);
                    long worldWork = 0;
                    Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref worldWork, out var worldForecast));
                    Assert.Equal(2, worldForecast.DiscoveryAlternatives.Count);
                    Assert.Equal(1, worldForecast.ArrivalUncertainty[owner.Id]["SUPPLY"]);
                    Assert.Equivalent(before, campaign.Capture(), strict: true);
                    AdvanceUnblocked(campaign, 1);
                    var selected = campaign.Capture();
                    var chosen = selected.CompletedResearch.Contains("REWARD_A") ? "REWARD_A" : "REWARD_B";
                    var branch = Assert.Single(worldForecast.DiscoveryAlternatives, alternative => alternative.CompletedResearch!.Contains(chosen));
                    Assert.Equal(selected.CompletedResearch.Order(StringComparer.Ordinal), branch.CompletedResearch!.Order(StringComparer.Ordinal));
                    Assert.Equal(branch.Arrivals[owner.Id].Sum(arrival => arrival.Value) - 1, selected.Bases[0].Items.GetValueOrDefault("SUPPLY"));
                    Assert.Equal(99, selected.Bases[0].Crafts.Single(craft => craft.RuleId == "REWARD_HUNT").Logistics!.Fuel);
                    Assert.True(selected.World.Ufos[0].Hunting);
                    Assert.Single(selected.Bases[1].Research);
                    Assert.Equal(0, selected.Bases[1].Items.GetValueOrDefault("SUPPLY"));
                    if (chosen == "REWARD_A") Assert.Equal(row[6].GetInt32() - 1, selected.Bases[0].Items.GetValueOrDefault("SUPPLY"));
                    campaign = ReloadRepeatedly(content, campaign, 81, "reward-variable-stocks.sav", 3, selected);
                    continue;
                }
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                Assert.True(supported);
                Assert.NotNull(forecast.CompletedResearch);
                Assert.Equal(after.CompletedResearch.Order(StringComparer.Ordinal), forecast.CompletedResearch.Order(StringComparer.Ordinal));
                var discoveries = Assert.Single(result.Events.OfType<CampaignResearchCompleted>()).Discoveries;
                var bonus = row[5].GetInt32();
                Assert.Equal(bonus != 0, after.World.Ufos[0].Hunting);
                if (bonus != 0) Assert.Contains(bonus == 1 ? "REWARD_A" : "REWARD_B", discoveries);
                Assert.Equal(row[6].GetInt32() - (bonus == 0 ? 0 : 1), after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
                Assert.Equal(row[7].GetBoolean(), after.Bases[1].Research.Count != 0);
                Assert.Equal(row[7].GetBoolean() ? 0 : 1, after.Bases[1].Items.GetValueOrDefault("SUPPLY"));
                Assert.Equal(bonus == 0 ? 60 : 99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "REWARD_HUNT").Logistics!.Fuel);
                Reload(content, campaign, 81, "reward-stocks-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedAutomaticDiscoveryKeepsNativeOrderAndAtomicHuntBoundary(bool close)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchQueuedRows"))
        {
            var lateId = row[0].GetBoolean() ? "QUEUED_A_LATE" : "QUEUED_Z_LATE";
            var content = Load($"crafts: [{{type: QUEUED_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: QUEUED_SOURCE, cost: 1}}, {{name: QUEUED_DISABLER, cost: 0, dependencies: [QUEUED_SOURCE], disables: [{lateId}], listOrder: 901}}, {{name: {lateId}, cost: 0, dependencies: [QUEUED_SOURCE], disables: [QUEUED_HELD], listOrder: 900}}, {{name: QUEUED_HELD, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY}}]\nfacilities: [{{type: QUEUED_LAB, mapName: LOGISTICS, labs: 1}}]\n");
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "QUEUED_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = 60,
                    Weapons = [],
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3
                },
            }) with
            { Time = new(1, 1, 1, 1999, 23, 59, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("QUEUED_LAB", 1, 1, 0, 0, false, false, false)],
                    Research = [new("QUEUED_SOURCE", 1, 0, 1), new("QUEUED_HELD", 0, 0, 100)],
                }],
                World = initial.World with { Ufos = [initial.World.Ufos[0] with { Longitude = close ? 0.45 : 0.4, Latitude = close ? 0.21 : 0.2 }] },
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "queued-stocks-before.sav");
            if (close)
            {
                AssertInterceptionStarts(campaign);
                continue;
            }
            Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            Assert.Equal(row[1].GetBoolean(), after.CompletedResearch.Contains(lateId));
            Assert.Equal(row[2].GetInt32(), after.ResearchRuleStatus.GetValueOrDefault(lateId));
            Assert.Equal(row[3].GetInt32() != 0, after.World.Ufos[0].Hunting);
            Assert.Equal(99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "QUEUED_HUNT").Logistics!.Fuel);
            Assert.Empty(after.Bases[0].Research);
            Reload(content, campaign, 81, "queued-stocks-after.sav");
        }
    }

    [Fact]
    public void AutomaticDiscoveryUsesNativeUtf8MapOrderIndependentOfListOrder()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        const string first = "\uE000";
        const string second = "\U00010000";
        var content = Load($"research: [{{name: UNICODE_SOURCE, cost: 1}}, {{name: '{second}', cost: 0, dependencies: [UNICODE_SOURCE], listOrder: 1}}, {{name: '{first}', cost: 0, dependencies: [UNICODE_SOURCE], disables: ['{second}'], listOrder: 999}}]\nfacilities: [{{type: UNICODE_LAB, mapName: LOGISTICS, labs: 1}}]\n");
        var initial = HuntingSnapshot(content) with { World = new(), Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Facilities = [.. initial.Bases[0].Facilities, new("UNICODE_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("UNICODE_SOURCE", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "unicode-research-before.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchUnicodeRows"))
        {
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { after.CompletedResearch.Contains(second), after.ResearchRuleStatus.GetValueOrDefault(second) });
            TestFixtures.AssertJsonEqual(row, actual);
        }
        Reload(content, campaign, 81, "unicode-research-after.sav");
    }

    [Fact]
    public void ProtectedDiscoveryWorkLimitStopsBeforeHunterTickMutation()
    {
        var topics = Enumerable.Range(0, 1100).Select(index => $"BOUNDED_CHILD_{index}").ToArray();
        var automatic = string.Join(", ", topics.Select(id => $"{{name: {id}, cost: 0, requires: [BOUNDED_GATE]}}"));
        var content = Load($"crafts: [{{type: BOUNDED_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: BOUNDED_SOURCE, cost: 1, spawnedItem: SUPPLY, unlocks: [{string.Join(", ", topics)}]}}, {{name: BOUNDED_GATE, cost: 1}}, {automatic}]\nfacilities: [{{type: BOUNDED_LAB, mapName: LOGISTICS, labs: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "BOUNDED_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("BOUNDED_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("BOUNDED_SOURCE", 1, 0, 1)],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO retargeting exceeds the supported work limit.");
    }

    [Fact]
    public void ZeroQuantityProductionStillReusesCraftItemsAndPersistsTransfers()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionDeliveryRows").Where(row => row[3].GetInt32() == 0)
            .GroupBy(row => (Fuel: row[0].GetBoolean(), Armed: row[1].GetBoolean(), Delay: row[2].GetInt32())))
        {
            var fuel = group.Key.Fuel ? ", refuelItem: SUPPLY" : "";
            var content = Load($"crafts: [{{type: ZERO_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuel}}}]\ncraftWeapons: [{{type: ZERO_PRODUCT_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nmanufacture: [{{name: ZERO_PRODUCT, time: 1, producedItems: {{SUPPLY: 0}}, transferTimes: [{group.Key.Delay}]}}]\nfacilities: [{{type: ZERO_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "ZERO_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = group.Key.Armed ? [new("ZERO_PRODUCT_GUN", 0)] : [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21,
                    Items = row[4].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[4].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("ZERO_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("ZERO_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "zero-product-before.sav");
                Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "ZERO_PRODUCT_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                    craft.Status, craft.Fuel, group.Key.Armed && craft.Weapons[0]!.Rearming, after.Bases[0].Transfers.Count });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).Take(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.All(after.Bases[0].Transfers, transfer => Assert.Equal(0, transfer.Quantity));
                Reload(content, campaign, 81, "zero-product-after.sav");
            }
        }
    }

    [Fact]
    public void ReloadedZeroItemTransferArrivesOnceAndReusesCraftAmmunition()
    {
        var content = Load("craftWeapons: [{type: ZERO_ARRIVAL_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_READY",
                Fuel = 300,
                Weapons = [new("ZERO_ARRIVAL_GUN", 0)],
                IsAutoPatrolling = false
            },
        }) with
        { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int>(), Transfers = [new(17, 1, CampaignTransferKind.Item, "SUPPLY", 0)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "zero-arrival-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(17, Assert.Single(Assert.Single(result.Events.OfType<SuppliesArrived>()).TransferIds));
        var after = campaign.Capture();
        Assert.Empty(after.Bases[0].Transfers);
        var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal("STR_REARMING", craft.Status);
        Assert.True(craft.Weapons[0]!.Rearming);
        campaign = Reload(content, campaign, 81, "zero-arrival-after.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<SuppliesArrived>());
        Assert.Empty(campaign.Capture().Bases[0].Transfers);
    }

    [Theory]
    [InlineData(CampaignTransferKind.Item, -1)]
    [InlineData(CampaignTransferKind.Engineer, 0)]
    [InlineData(CampaignTransferKind.Scientist, 0)]
    public void ZeroItemTransferCompatibilityDoesNotPermitNegativeOrEmptyStaff(CampaignTransferKind kind, int quantity)
    {
        var content = Load();
        var initial = HuntingSnapshot(content) with { World = new() };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        { Transfers = [new(17, 1, kind, kind == CampaignTransferKind.Item ? "SUPPLY" : "", quantity)] }]
        };
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)));
    }

    [Fact]
    public void FinalPrepaidProductionOutputsMatchNativeBeforeHunting()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionDeliveryRows")
            .GroupBy(row => (Fuel: row[0].GetBoolean(), Armed: row[1].GetBoolean(), Delay: row[2].GetInt32(), Quantity: row[3].GetInt32())))
        {
            var fuel = group.Key.Fuel ? ", refuelItem: SUPPLY" : "";
            var content = Load($"crafts: [{{type: FINAL_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuel}}}]\ncraftWeapons: [{{type: FINAL_PRODUCT_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nmanufacture: [{{name: FINAL_PRODUCT, time: 1, cost: 777, requiredItems: {{SUPPLY: 10, FINAL_PRODUCT_HUNT: 1}}, producedItems: {{SUPPLY: {group.Key.Quantity}}}, transferTimes: [{group.Key.Delay}]}}]\nfacilities: [{{type: FINAL_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "FINAL_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = group.Key.Armed ? [new("FINAL_PRODUCT_GUN", 0)] : [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [0] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21,
                    Items = row[4].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[4].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("FINAL_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("FINAL_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "final-product-before.sav");
                Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "FINAL_PRODUCT_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                    craft.Status, craft.Fuel, group.Key.Armed && craft.Weapons[0]!.Rearming, after.Bases[0].Transfers.Count,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.Equal(0, after.Funds[^1]);
                Assert.Empty(after.Bases[0].Productions);
                Reload(content, campaign, 81, "final-product-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalProductionStockAndTransferLimitsStopBeforeMutation(bool transfers)
    {
        var content = Load($"crafts: [{{type: FINAL_LIMIT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nmanufacture: [{{name: FINAL_LIMIT_PRODUCT, time: 1, producedItems: {{SUPPLY: 1}}, transferTimes: [{(transfers ? 1 : 0)}]}}]\nfacilities: [{{type: FINAL_LIMIT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "FINAL_LIMIT_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            },
        }) with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = transfers ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue },
            Facilities = [.. initial.Bases[0].Facilities, new("FINAL_LIMIT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("FINAL_LIMIT_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
        }]
        };
        if (transfers) initial = initial with { NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = int.MaxValue } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), transfers ?
            "Production arrivals exhaust the transfer identity range." : "Production outputs exceed the supported stock range.");
    }

    [Fact]
    public void FreeRecurringProductionStocksMatchNativeUnitCapsAndReuse()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionFreeRows")
            .GroupBy(row => (Armed: row[0].GetBoolean(), Quantity: row[4].GetInt32(), Delay: row[5].GetInt32())))
        {
            var content = Load($"crafts: [{{type: FREE_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1, refuelItem: SUPPLY}}]\ncraftWeapons: [{{type: FREE_PRODUCT_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nmanufacture: [{{name: FREE_PRODUCT, time: 2, cost: 0, producedItems: {{SUPPLY: {group.Key.Quantity}}}, transferTimes: [{group.Key.Delay}]}}]\nfacilities: [{{type: FREE_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 6}}]\n");
            foreach (var row in group)
            {
                var units = row[1].GetInt32();
                var amount = row[2].GetInt32();
                var infinite = row[3].GetBoolean();
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "FREE_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = group.Key.Armed ? [new("FREE_PRODUCT_GUN", 0)] : [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [-1] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Engineers = 0,
                    Items = row[6].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[6].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("FREE_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("FREE_PRODUCT", units * 2, 1, amount, infinite, false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "free-product-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "FREE_PRODUCT_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { result.Events.OfType<CampaignProductionProgress>().Sum(progress => progress.Produced),
                    after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    group.Key.Armed && craft.Weapons[0]!.Rearming, after.Bases[0].Transfers.Count,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(7).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.Equal(-1, after.Funds[^1]);
                Assert.Equal(infinite || units < amount ? 1 : 0, after.Bases[0].Productions.Count);
                Reload(content, campaign, 81, "free-product-after.sav");
            }
        }
    }

    [Fact]
    public void ExcessiveFreeProductionWorkStopsBeforeClockInventoryAndRandomChanges()
    {
        var content = Load("crafts: [{type: FREE_BOUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nmanufacture: [{name: FREE_BOUND_PRODUCT, time: 1, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: FREE_BOUND_WORKSHOP, mapName: LOGISTICS, workshops: 500001}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        { RuleId = "FREE_BOUND_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [] } })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Engineers = 0, Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("FREE_BOUND_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("FREE_BOUND_PRODUCT", 500001, 0, 1, true, false, false, new Dictionary<string, int>())],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO retargeting exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FreeProductionFutureQuantitiesAreBoundedBeforeMutation(int units)
    {
        var content = Load($"crafts: [{{type: FUTURE_BOUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nmanufacture: [{{name: FUTURE_BOUND_PRODUCT, time: 1, producedItems: {{SUPPLY: {int.MaxValue}}}, transferTimes: [1]}}]\nfacilities: [{{type: FUTURE_BOUND_WORKSHOP, mapName: LOGISTICS, workshops: 2}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        { RuleId = "FUTURE_BOUND_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [] } })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int>(),
            Transfers = units == 1 ? [new(17, 2, CampaignTransferKind.Item, "SUPPLY", 1)] : [],
            Facilities = [.. initial.Bases[0].Facilities, new("FUTURE_BOUND_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("FUTURE_BOUND_PRODUCT", units, 0, units, false, false, false, new Dictionary<string, int>())],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Production arrivals exceed the supported quantity range.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ProductionCaptivePopulationLimitsIncludeStocksAndFutureArrivals(int delay)
    {
        var content = Load($"items: [{{type: CAPTIVE_PRODUCT_A, liveAlien: true, prisonType: 0}}, {{type: CAPTIVE_PRODUCT_B, liveAlien: true, prisonType: 0}}]\ncrafts: [{{type: CAPTIVE_BOUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nmanufacture: [{{name: CAPTIVE_BOUND_PRODUCT, time: 1, producedItems: {{CAPTIVE_PRODUCT_B: 1}}, transferTimes: [{delay}]}}]\nfacilities: [{{type: CAPTIVE_BOUND_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        { RuleId = "CAPTIVE_BOUND_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [] } })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int> { ["CAPTIVE_PRODUCT_A"] = int.MaxValue },
            Facilities = [.. initial.Bases[0].Facilities, new("CAPTIVE_BOUND_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("CAPTIVE_BOUND_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Production captive populations exceed the supported range.");
    }

    [Fact]
    public void MixedFutureProductionCannotUseIncompleteIdentityForecasts()
    {
        var content = Load("crafts: [{type: MIXED_ID_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\nmanufacture: [{name: MIXED_UNFORECAST_PRODUCT, time: 1, producedItems: {SUPPLY: 0}, randomProducedItems: [[1, {SUPPLY: 1}], [1, {BULKY: 1}]], transferTimes: [1]}, {name: MIXED_FIXED_PRODUCT, time: 1, producedItems: {SUPPLY: 0}, transferTimes: [1]}]\nfacilities: [{type: MIXED_ID_WORKSHOP, mapName: LOGISTICS, workshops: 2}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "MIXED_ID_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            }
        })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = int.MaxValue - 1 },
            Bases = [initial.Bases[0] with
            {
                Items = new Dictionary<string, int>(),
                Facilities = [.. initial.Bases[0].Facilities, new("MIXED_ID_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("MIXED_UNFORECAST_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>()),
                    new("MIXED_FIXED_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
            }],
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Production arrivals require other production effect forecasts.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionFutureIdentitiesFollowDailyResearchAndHourlyCleanup(bool research)
    {
        var content = Load("crafts: [{type: ORDERED_ID_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\nresearch: [{name: ORDERED_ID_RESEARCH, cost: 1, spawnedItem: SUPPLY}]\nmanufacture: [{name: ORDERED_ID_PRODUCT, time: 1, producedItems: {SUPPLY: 0}, transferTimes: [1]}]\nfacilities: [{type: ORDERED_ID_WORKSHOP, mapName: LOGISTICS, workshops: 1, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "ORDERED_ID_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            }
        })
            with
        { Time = new(1, 1, 1, 1999, research ? 23 : 1, 59, 55) };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = research ? int.MaxValue - 1 : 1 },
            Bases = [initial.Bases[0] with
            {
                Items = new Dictionary<string, int>(), Transfers = research ? [] : [new(int.MaxValue, 1, CampaignTransferKind.Item, "SUPPLY", 0)],
                Facilities = [.. initial.Bases[0].Facilities, new("ORDERED_ID_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Research = research ? [new("ORDERED_ID_RESEARCH", 1, 0, 1)] : [],
                Productions = [new("ORDERED_ID_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
            }],
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "ordered-id-before.sav");
        if (research)
        {
            AssertTimeBlocked(campaign, "Production arrivals exhaust the transfer identity range.");
            return;
        }
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Equal(1, Assert.Single(after.Bases[0].Transfers).Id);
        Assert.Equal(2, after.NextIds["oxcePortTransfer"]);
        Assert.True(after.World.Ufos[0].Hunting);
        Reload(content, campaign, 81, "ordered-id-after.sav");
    }

    [Fact]
    public void ProductionFundedRelaunchStopsBeforeSameTickHunterInterception()
    {
        var content = Load("crafts: [{type: PRODUCTION_COMBAT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nmanufacture: [{name: PRODUCTION_COMBAT_PRODUCT, time: 1, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: PRODUCTION_COMBAT_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "PRODUCTION_COMBAT_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                Longitude = 0.45,
                Latitude = 0.21,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            }
        })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                Facilities = [.. initial.Bases[0].Facilities, new("PRODUCTION_COMBAT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("PRODUCTION_COMBAT_PRODUCT", 1, 0, 2, false, false, false, new Dictionary<string, int>())],
            }],
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { Longitude = 0.45, Latitude = 0.21 }] },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "production-combat-before.sav");
        AssertInterceptionStarts(campaign);
    }

    [Fact]
    public void ManyCraftInputsRetainNativeRemovalAndFollowerNotificationOrder()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var expected = TestFixtures.Rows(oracle.RootElement, "productionCraftOrderRows").Single();
        var types = Enumerable.Range(0, 18).Select(index => $"ORDER_INPUT_{index:D2}").ToArray();
        var overlay = "crafts: [" + string.Join(",", types.Select(type => $"{{type: {type}, speedMax: 2000, damageMax: 100, fuelMax: 300}}")) +
            ",{type: ORDER_FOLLOWER, speedMax: 2000, damageMax: 100, fuelMax: 300}]\nmanufacture: [{name: ORDER_CONSUME, time: 1, requiredItems: {" +
            string.Join(",", types.Select(type => $"{type}: 0")) + "}, producedItems: {SUPPLY: 0}}]\nfacilities: [{type: ORDER_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n";
        var content = Load(overlay);
        var initial = HuntingSnapshot(content) with { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var source = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var crafts = types.Select((type, index) => source with
        {
            RuleId = type,
            Id = index + 1,
            Logistics = source.Logistics! with
            { Status = "STR_READY", Weapons = [], Items = new Dictionary<string, int>(), Vehicles = [], IsAutoPatrolling = false }
        }).ToList();
        crafts.AddRange(types.Select((type, index) => source with
        {
            RuleId = "ORDER_FOLLOWER",
            Id = 100 + index,
            Logistics = source.Logistics! with
            {
                Fuel = 300,
                Speed = 1000,
                Longitude = 0.5,
                Latitude = 0.21,
                Weapons = [],
                Items = new Dictionary<string, int>(),
                Vehicles = [],
                IsAutoPatrolling = false,
                Destination = new(WorldTargetKind.Craft, type, index + 1, source.Logistics!.Longitude, source.Logistics.Latitude)
            }
        }));
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Crafts = crafts, Soldiers = [], Engineers = 0,
            Facilities = [.. initial.Bases[0].Facilities, new("ORDER_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("ORDER_CONSUME", 1, 0, 2, false, false, false, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "ordered-input-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var actual = JsonSerializer.SerializeToUtf8Bytes(result.Events.OfType<CraftTargetLost>().Select(item => item.Target.Id).ToArray());
        TestFixtures.AssertJsonEqual(expected, actual);
        Assert.All(campaign.Capture().Bases[0].Crafts, craft => Assert.Equal("ORDER_FOLLOWER", craft.RuleId));
        Reload(content, campaign, 81, "ordered-input-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumedCraftFollowersMatchNativeImmediateReturnAndUfoRestore(bool hourly)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts: [{type: DELETED_FOLLOWER, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40}]\nmanufacture: [{name: DELETE_LEADER, time: 1, requiredItems: {SHIP: 0}, producedItems: {SUPPLY: 0}}]\nfacilities: [{type: DELETE_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "deletedCraftFollowerRows"))
        {
            if (hourly && (!row[0].GetBoolean() || !row[1].GetBoolean())) continue;
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            var source = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
            var leader = source with { Logistics = source.Logistics! with { Longitude = 0.4, Latitude = 0.2, Weapons = [], Items = new Dictionary<string, int>(), Vehicles = [] } };
            var follower = source with
            {
                RuleId = "DELETED_FOLLOWER",
                Id = 2,
                Logistics = source.Logistics! with
                {
                    Status = row[0].GetBoolean() ? "STR_OUT" : "STR_REFUELLING",
                    Speed = row[0].GetBoolean() ? 1000 : 0,
                    Fuel = 100,
                    Longitude = 0.5,
                    Latitude = 0.21,
                    Weapons = [],
                    Items = new Dictionary<string, int>(),
                    Vehicles = [],
                    IsAutoPatrolling = false,
                    Destination = row[0].GetBoolean() ? new(WorldTargetKind.Craft, "SHIP", 1, 0.4, 0.2) : null
                }
            };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Crafts = [leader, follower], Soldiers = [], Engineers = hourly ? 0 : 1,
                Items = new Dictionary<string, int>(),
                Facilities = [.. initial.Bases[0].Facilities, new("DELETE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = hourly ? [new("DELETE_LEADER", 1, 0, 2, false, false, false, new Dictionary<string, int>())] : []
            }],
                World = initial.World with
                {
                    Ufos = [initial.World.Ufos[0] with
            {
                Longitude = 0.40001, Latitude = 0.2, HunterKiller = false, Hunting = row[1].GetBoolean(), Speed = row[1].GetBoolean() ? 2200 : 1000,
                Destination = row[1].GetBoolean() ? new(WorldTargetKind.Craft, "SHIP", 1, 0.4, 0.2) : new(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0, 0.9, 0.5),
                OriginalWaypoint = row[1].GetBoolean() ? new(0.9, 0.5) : null
            }]
                }
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "deleted-leader-before.sav");
            var result = hourly ? campaign.Execute(new AdvanceCampaignTime(1)) : campaign.Execute(new ConfigureProductionProject(initial.Bases[0].Id, "DELETE_LEADER", 0, 1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture(); Assert.DoesNotContain(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP");
            var craft = Assert.Single(after.Bases[0].Crafts).Logistics!; var ufo = Assert.Single(after.World.Ufos);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { craft.Status, craft.Speed, craft.Destination?.Kind == WorldTargetKind.Base,
                ufo.Hunting, ufo.Escorting, ufo.OriginalWaypoint is not null, ufo.Destination!.Longitude, ufo.Destination.Latitude, ufo.Speed });
            // Ordinary movement follows the restored waypoint during the hourly case; its target remains unchanged.
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(2).Take(9).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            if (hourly)
            {
                Assert.Equal(row[11].GetInt32(), craft.Fuel);
                Assert.Single(result.Events.OfType<CraftTargetLost>());
            }
            Reload(content, campaign, 81, "deleted-leader-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidGroundedFollowerBaseStopsBeforePaidProductionOutput(bool arriving)
    {
        var content = Load("crafts: [{type: UNPLACED_FOLLOWER, speedMax: 2000, damageMax: 100, fuelMax: 100}]\nmanufacture: [{name: UNPLACED_DELETE, time: 1, requiredItems: {SHIP: 0}, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: UNPLACED_DELETE_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n");
        var initial = HuntingSnapshot(content) with { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var source = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var leader = source with { Logistics = source.Logistics! with { Status = "STR_READY", Weapons = [], Items = new Dictionary<string, int>(), Vehicles = [], IsAutoPatrolling = false } };
        var follower = source with
        {
            RuleId = "UNPLACED_FOLLOWER",
            Id = 2,
            Logistics = source.Logistics! with
            {
                Status = "STR_READY",
                Fuel = 100,
                Speed = 0,
                Weapons = [],
                Items = new Dictionary<string, int>(),
                Vehicles = [],
                IsAutoPatrolling = false,
                Destination = new(WorldTargetKind.Craft, "SHIP", 1, source.Logistics!.Longitude, source.Logistics.Latitude)
            }
        };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Name = "", Crafts = arriving ? [leader] : [leader, follower],
            Transfers = arriving ? [new(17, 1, CampaignTransferKind.Craft, "UNPLACED_FOLLOWER", 1, Craft: follower)] : [], Soldiers = [], Engineers = 0, Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("UNPLACED_DELETE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("UNPLACED_DELETE", 1, 0, 2, false, false, false, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "unplaced-follower-before.sav");
        AssertTimeBlocked(campaign, "Craft target removal requires placed follower bases.");
    }

    [Fact]
    public void CraftRemovalFollowerWorkStopsBeforeAnUnboundedHourlyScan()
    {
        var content = Load("manufacture: [{name: DELETE_MANY, time: 1, requiredItems: {SHIP: 0}, producedItems: {SUPPLY: 0}}]\nfacilities: [{type: DELETE_MANY_WORKSHOP, mapName: LOGISTICS, workshops: 1001}]\n");
        var initial = HuntingSnapshot(content) with { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var source = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Soldiers = [], Engineers = 0,
            Crafts = Enumerable.Range(1, 1001).Select(id => source with { Id = id, Logistics = source.Logistics! with
                { Status = "STR_READY", Weapons = [], Items = new Dictionary<string, int>(), Vehicles = [], IsAutoPatrolling = false } }).ToArray(),
            Facilities = [.. initial.Bases[0].Facilities, new("DELETE_MANY_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("DELETE_MANY", 1001, 0, 1, true, false, false, new Dictionary<string, int>())]
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), "Craft target removal exceeds the supported work limit.");
    }

    [Fact]
    public void ProductionCraftUnloadStockOverflowStopsBeforeConsumption()
    {
        var content = Load("manufacture: [{name: UNLOAD_LIMIT, time: 1, requiredItems: {SHIP: 1}, producedItems: {SUPPLY: 0}}]\nfacilities: [{type: UNLOAD_LIMIT_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_READY", Weapons = [], Items = new Dictionary<string, int> { ["SUPPLY"] = 1 }, Vehicles = [], IsAutoPatrolling = false }
        });
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue },
            Facilities = [.. initial.Bases[0].Facilities, new("UNLOAD_LIMIT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("UNLOAD_LIMIT", 1, 0, 2, false, false, false, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "unload-limit-before.sav");
        AssertTimeBlocked(campaign, "Production craft unload stocks exceed the supported range.");
    }

    [Fact]
    public void ProductionCraftUnloadFeedsNativeRefuellingAndHunterSelection()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionCraftUnloadRows").GroupBy(row => row[1].GetInt32()))
        {
            var content = Load($"crafts: [{{type: UNLOAD_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: UNLOAD_PRODUCT, time: 1, requiredItems: {{SHIP: 1, SUPPLY: {group.Key}}}, producedItems: {{SUPPLY: 0}}}}]\nfacilities: [{{type: UNLOAD_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
                var source = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
                var hunterTarget = source with
                {
                    RuleId = "UNLOAD_HUNT",
                    Logistics = source.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        Items = new Dictionary<string, int>(),
                        Vehicles = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                };
                var consumed = source with
                {
                    Id = 2,
                    Logistics = source.Logistics! with
                    {
                        Status = "STR_READY",
                        Weapons = [],
                        Items = row[2].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[2].GetInt32() },
                        Vehicles = [],
                        IsAutoPatrolling = false
                    }
                };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Crafts = [hunterTarget, consumed], Soldiers = [], Engineers = 0,
                    Items = row[0].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[0].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("UNLOAD_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("UNLOAD_PRODUCT", 1, 0, 2, false, false, false, new Dictionary<string, int>())]
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "craft-unload-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                Assert.Equal(row[3].GetBoolean(), Assert.Single(result.Events.OfType<CampaignProductionProgress>()).StopReason is null);
                var after = campaign.Capture(); var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "UNLOAD_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    after.Bases[0].Crafts.Count, after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(4).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "craft-unload-after.sav");
            }
        }
    }

    [Fact]
    public void ProductionCraftInputSelectionMatchesNativeQuantityAndFirstCraft()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionCraftInputRows").GroupBy(row => row[0].GetInt32()))
        {
            var content = Load($"manufacture: [{{name: CRAFT_INPUT, time: 1, requiredItems: {{SHIP: {group.Key}}}, producedItems: {{SUPPLY: 0}}}}]\nfacilities: [{{type: CRAFT_INPUT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content) with { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
                var source = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
                var craftIds = row[2].GetBoolean() ? new List<int> { 1 } : [];
                craftIds.AddRange(Enumerable.Range(2, row[1].GetInt32()));
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Soldiers = [], Engineers = 0, Items = new Dictionary<string, int>(),
                    Crafts = craftIds.Select(id => source with { Id = id, Logistics = source.Logistics! with
                    { Status = id == 1 ? "STR_OUT" : "STR_READY", Weapons = [], Items = new Dictionary<string, int>(), Vehicles = [], IsAutoPatrolling = false } }).ToArray(),
                    Facilities = [.. initial.Bases[0].Facilities, new("CRAFT_INPUT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("CRAFT_INPUT", 1, 0, 2, false, false, false, new Dictionary<string, int>())]
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "craft-input-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                Assert.Equal(row[3].GetBoolean(), progress.StopReason is null);
                var actual = JsonSerializer.SerializeToUtf8Bytes(campaign.Capture().Bases[0].Crafts.Select(craft => craft.Id).ToArray());
                var expected = JsonSerializer.SerializeToUtf8Bytes(row[4]);
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "craft-input-after.sav");
            }
        }
    }

    [Fact]
    public void FallbackProgressOverflowBlocksWithoutAnyHunter()
    {
        var content = Load("manufacture: [{name: FALLBACK_OVERFLOW, time: 100, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: FALLBACK_OVERFLOW_WORKSHOP, mapName: LOGISTICS, workshops: 2}]\n");
        var initial = HuntingSnapshot(content) with { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Engineers = 2,
            Facilities = [.. initial.Bases[0].Facilities, new("FALLBACK_OVERFLOW_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("FALLBACK_OVERFLOW", 0, int.MaxValue - 1, 1, true, false, true, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "fallback-overflow-before.sav");
        AssertTimeBlocked(campaign, "Production progress exceeds the supported range.");
    }

    [Fact]
    public void ProductionStaffReleaseWaitsForTheWholeNativeBasePass()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionStaffReleaseRows"))
        {
            var fails = row[0].GetBoolean();
            var content = Load($"crafts: [{{type: RELEASE_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: RELEASE_FIRST, time: 1, cost: {(fails ? 1000 : 0)}, producedItems: {{SUPPLY: 0}}}}, {{name: RELEASE_FALLBACK, time: 1, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: RELEASE_WORKSHOP, mapName: LOGISTICS, workshops: 3}}]\n");
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "RELEASE_HUNT",
                Logistics = craft.Logistics! with
                { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 }
            }) with
            { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [100] };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Engineers = row[1].GetInt32(), Items = new Dictionary<string, int>(),
                Facilities = [.. initial.Bases[0].Facilities, new("RELEASE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("RELEASE_FIRST", 2, 0, fails ? 3 : 1, false, false, false, new Dictionary<string, int>()),
                    new("RELEASE_FALLBACK", 0, 0, 4, false, false, true, new Dictionary<string, int>())]
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "release-before.sav");
            Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture(); var project = Assert.Single(after.Bases[0].Productions);
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "RELEASE_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { project.Spent, project.Assigned, after.Bases[0].Engineers,
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(2).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "release-after.sav");
        }
    }

    [Fact]
    public void FallbackUsesWorkshopConstructionCompletedEarlierInTheDailyTick()
    {
        var content = Load("crafts: [{type: BUILD_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}]\nmanufacture: [{name: BUILD_PRODUCT, time: 2, space: 2, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: BUILD_WORKSHOP, mapName: LOGISTICS, workshops: 4}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "BUILD_HUNT",
            Logistics = craft.Logistics! with
            { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 }
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Engineers = 4, Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("BUILD_WORKSHOP", 1, 1, 1, 0, false, false, false)],
            Productions = [new("BUILD_PRODUCT", 0, 0, 4, false, false, true, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "build-before.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture(); var project = Assert.Single(after.Bases[0].Productions);
        Assert.Equal(2, project.Assigned); Assert.Equal(2, project.Spent); Assert.Equal(2, after.Bases[0].Engineers);
        Assert.Equal("STR_OUT", after.Bases[0].Crafts.Single(craft => craft.RuleId == "BUILD_HUNT").Logistics!.Status);
        Assert.True(after.World.Ufos[0].Hunting);
        Reload(content, campaign, 81, "build-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackProductionStaffingMatchesNativeBeforeHunting(bool arrivingEngineers)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionFallbackRows").GroupBy(row => row[2].GetInt32()))
        {
            foreach (var row in group)
            {
                var capacity = row[1].GetInt32() + row[4].GetInt32() + (row[3].GetInt32() != 0 || row[4].GetInt32() != 0 ? group.Key : 0);
                var rowContent = Load($"crafts: [{{type: MONEY_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: MONEY_PRODUCT, time: 2, space: {group.Key}, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: MONEY_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: {capacity}}}]\n");
                var initial = HuntingSnapshot(rowContent).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "MONEY_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                })
                    with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [12] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Engineers = arrivingEngineers ? 0 : row[0].GetInt32(), Items = new Dictionary<string, int>(),
                    Transfers = arrivingEngineers && row[0].GetInt32() > 0 ? [new(17, 1, CampaignTransferKind.Engineer, "", row[0].GetInt32())] : [],
                    Facilities = [.. initial.Bases[0].Facilities, new("MONEY_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("MONEY_PRODUCT", row[4].GetInt32(), row[3].GetInt32(), 4, false, false, true, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(rowContent, CampaignState.Restore(initial, rowContent, new SplitMix64RandomSource(81)), 81, "money-product-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = result.Events.OfType<CampaignProductionProgress>().SingleOrDefault();
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "MONEY_PRODUCT_HUNT").Logistics!;
                var project = Assert.Single(after.Bases[0].Productions);
                Assert.Equal(row[5].GetInt32() - row[4].GetInt32(), ProductionProgress.FallbackAdditional(row[3].GetInt32(), row[4].GetInt32(), group.Key, row[0].GetInt32(), row[1].GetInt32()));
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { project.Assigned, after.Bases[0].Engineers,
                    project.Spent, progress?.Produced ?? 0, 0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(rowContent, campaign, 81, "money-product-after.sav");
            }
        }
    }

    [Fact]
    public void ProductionMaterialContinuationMatchesNativeOutputThenInputOrder()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionMaterialRows").GroupBy(row => (Input: row[0].GetInt32(), Output: row[1].GetInt32(), Delay: row[3].GetInt32())))
        {
            var content = Load($"crafts: [{{type: MONEY_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: MONEY_PRODUCT, time: 2, cost: 3, requiredItems: {{SUPPLY: {group.Key.Input}}}, producedItems: {{SUPPLY: {group.Key.Output}}}, transferTimes: [{group.Key.Delay}]}}]\nfacilities: [{{type: MONEY_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 6}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "MONEY_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                })
                    with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [12] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Engineers = 0, Items = row[2].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[2].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("MONEY_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("MONEY_PRODUCT", row[4].GetInt32() * 2, 1, 4, false, false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "money-product-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "MONEY_PRODUCT_HUNT").Logistics!;
                var stop = progress.StopReason == "STR_PRODUCTION_COMPLETE" ? 1 : progress.StopReason == "STR_NOT_ENOUGH_MONEY" ? 2 : progress.StopReason == "STR_NOT_ENOUGH_MATERIALS" ? 3 : 0;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { progress.Produced, after.Funds[^1], stop,
                    after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0, after.Bases[0].Transfers.Count });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "money-product-after.sav");
            }
        }
    }

    [Fact]
    public void ProductionCostContinuationMatchesNativePaidUnitAndStopOrder()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionMoneyRows").GroupBy(row => row[0].GetInt32()))
        {
            var content = Load($"crafts: [{{type: MONEY_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: MONEY_PRODUCT, time: 2, cost: {group.Key}, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: MONEY_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 6}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "MONEY_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                })
                    with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [row[1].GetInt32()] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Engineers = 0, Items = new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("MONEY_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("MONEY_PRODUCT", row[2].GetInt32() * 2, 1, row[3].GetInt32(), row[4].GetBoolean(), false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "money-product-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "MONEY_PRODUCT_HUNT").Logistics!;
                var stop = progress.StopReason == "STR_PRODUCTION_COMPLETE" ? 1 : progress.StopReason == "STR_NOT_ENOUGH_MONEY" ? 2 : 0;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { progress.Produced, after.Funds[^1], stop,
                    after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "money-product-after.sav");
            }
        }
    }

    [Fact]
    public void EarlierFixedSaleFundsLaterProductionBeforeHunting()
    {
        var content = Load("sellPriceCoefficient: [100, 100, 100, 100, 100]\nitems: [{type: SHARED_SALE_ITEM, costSell: 10}]\ncrafts: [{type: SHARED_MONEY_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}]\nmanufacture: [{name: EARLIER_SALE, time: 1, producedItems: {SHARED_SALE_ITEM: 1}}, {name: LATER_MONEY_PRODUCT, time: 1, cost: 3, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: SHARED_MONEY_WORKSHOP, mapName: LOGISTICS, workshops: 2}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        { RuleId = "SHARED_MONEY_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [] } })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [0] };
        Assert.Equal(10, content.RuntimeRules.Items[content.RuntimeRules.Items.GetRequired("SHARED_SALE_ITEM")].Value.CostSell);
        var owner = initial.Bases[0] with
        {
            Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("SHARED_MONEY_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("LATER_MONEY_PRODUCT", 2, 0, 3, false, false, false, new Dictionary<string, int>())],
        };
        initial = initial with
        {
            Bases = [owner with { Crafts = [], Soldiers = [],
            Productions = [new("EARLIER_SALE", 1, 0, 1, false, true, false, new Dictionary<string, int>())] },
            owner with { Id = 1 }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "shared-sale-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(2, Assert.Single(result.Events.OfType<CampaignProductionProgress>(), progress => progress.RuleId == "LATER_MONEY_PRODUCT").Produced);
        Assert.Equal(4, campaign.Capture().Funds[^1]);
        Reload(content, campaign, 81, "shared-sale-after.sav");
    }

    [Fact]
    public void ScoredProductionMatchesNativeContinuationBeforeHunting()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionScoreRows").GroupBy(row => row[0].GetInt32()))
        {
            var content = Load($"crafts: [{{type: SCORE_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: SCORE_PRODUCT, time: 2, points: {group.Key}, requiredItems: {{SUPPLY: 2}}, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: SCORE_WORKSHOP, mapName: LOGISTICS, workshops: 6}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "SCORE_HUNT",
                    Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 }
                }) with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), ResearchScores = [13] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                    {
                        Longitude = 0.45, Latitude = 0.21, Soldiers = [],
                        Items = row[3].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[3].GetInt32() },
                        Facilities = [.. initial.Bases[0].Facilities, new("SCORE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                        Productions = [new("SCORE_PRODUCT", row[1].GetInt32(), 1, 2, row[2].GetBoolean(), false, false, new Dictionary<string, int>())]
                    }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "score-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                var after = campaign.Capture();
                var state = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SCORE_HUNT").Logistics!;
                var stop = progress.StopReason == "STR_PRODUCTION_COMPLETE" ? 1 : progress.StopReason == "STR_NOT_ENOUGH_MATERIALS" ? 3 : 0;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { progress.Produced, after.ResearchScores[^1], after.Bases[0].Items.GetValueOrDefault("SUPPLY"), stop, state.Status, state.Fuel, after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                TestFixtures.AssertJsonEqual(row.EnumerateArray().Skip(4).ToArray(), actual);
                Reload(content, campaign, 81, "score-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void CombinedDailyResearchAndProductionScoreCapacityStopsBeforeMutation(int sign)
    {
        var content = Load($"research: [{{name: SCORE_TOPIC, cost: 1, points: {sign}, getOneFree: [SCORE_BONUS]}}, {{name: SCORE_BONUS, cost: 1, points: {sign}, lookup: SCORE_LOOKUP}}, {{name: SCORE_LOOKUP, cost: 1, points: {sign}}}]\nmanufacture: [{{name: SCORE_PRODUCT, time: 1, points: {sign}, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: SCORE_LAB_WORKSHOP, mapName: LOGISTICS, labs: 1, workshops: 1}}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), ResearchScores = [sign > 0 ? int.MaxValue - 3 : int.MinValue + 3] };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            {
                Research = [new("SCORE_TOPIC", 1, 0, 1)],
                Productions = [new("SCORE_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
                Facilities = [.. initial.Bases[0].Facilities, new("SCORE_LAB_WORKSHOP", 1, 1, 0, 0, false, false, false)]
            }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "score-limit.sav");
        AssertTimeBlocked(campaign, "Research and production scores exceed the supported range.");
        // Enough room for the reward, its lookup, the primary and the hourly output.
        initial = initial with { ResearchScores = [sign > 0 ? int.MaxValue - 4 : int.MinValue + 4] };
        var safe = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        Assert.Empty(safe.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        Assert.Equal(sign > 0 ? int.MaxValue : int.MinValue, safe.Capture().ResearchScores[^1]);
        Reload(content, safe, 81, "score-limit-after.sav");
    }

    [Fact]
    public void DeterministicRandomProductionMatchesNativeOrderingAndHistory()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionDeterministicRandomRows")
            .GroupBy(row => (Delay: row[1].GetInt32(), Quantity: row[2].GetInt32(), Duplicate: row[3].GetBoolean())))
        {
            var choices = $"[1, {{SUPPLY: {group.Key.Quantity}}}]";
            if (group.Key.Duplicate) choices += $", [1, {{SUPPLY: {group.Key.Quantity}}}]";
            var content = Load($"crafts: [{{type: RANDOM_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: RANDOM_PRODUCT, time: 2, requiredItems: {{SUPPLY: 2}}, producedItems: {{SUPPLY: 1}}, randomProducedItems: [{choices}], transferTimes: [{group.Key.Delay}]}}]\nfacilities: [{{type: RANDOM_WORKSHOP, mapName: LOGISTICS, workshops: 6}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "RANDOM_HUNT",
                    Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 }
                }) with
                { Time = new(1, 1, 1, 1999, 1, 59, 55) };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                    {
                        Longitude = 0.45, Latitude = 0.21, Soldiers = [],
                        Items = new Dictionary<string, int> { ["SUPPLY"] = 1 },
                        Facilities = [.. initial.Bases[0].Facilities, new("RANDOM_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                        Productions = [new("RANDOM_PRODUCT", row[4].GetInt32(), 1, 2, row[5].GetBoolean(), row[0].GetBoolean(), false, new Dictionary<string, int>())]
                    }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "random-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                var after = campaign.Capture();
                var state = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "RANDOM_HUNT").Logistics!;
                var stop = progress.StopReason == "STR_PRODUCTION_COMPLETE" ? 1 : progress.StopReason == "STR_NOT_ENOUGH_MATERIALS" ? 3 : 0;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { progress.Produced, stop, after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.Bases[0].Transfers.Count, state.Status, state.Fuel, after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(6).Where((_, index) => index != 4).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                if (after.Bases[0].Productions.Count != 0)
                    Assert.Equal(row[10].GetInt32(), after.Bases[0].Productions[0].RandomProductionInfo["SUPPLY"]);
                Reload(content, campaign, 81, "random-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomProductionHistoryOverflowStopsBeforeTickMutation(bool hunting)
    {
        var content = Load("manufacture: [{name: HISTORY_PRODUCT, time: 1, producedItems: {SUPPLY: 1}, randomProducedItems: [[0, {SUPPLY: 2147483647}], [1, {SUPPLY: 2}]]}]\nfacilities: [{type: HISTORY_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            World = hunting ? initial.World : new(),
            Bases = [initial.Bases[0] with
            {
                Productions = [new("HISTORY_PRODUCT", 1, 0, 2, true, false, false, new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue - 2 })],
                Facilities = [.. initial.Bases[0].Facilities, new("HISTORY_WORKSHOP", 1, 1, 0, 0, false, false, false)]
            }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "random-limit.sav");
        AssertTimeBlocked(campaign, "Random production history exceeds the supported range.");
        var owner = initial.Bases[0];
        initial = initial with { Bases = [owner with { Productions = [owner.Productions[0] with { RandomProductionInfo = new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue - 3 } }] }] };
        var safe = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        Assert.Empty(safe.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        Assert.Equal(int.MaxValue, safe.Capture().Bases[0].Productions[0].RandomProductionInfo["SUPPLY"]);
        Reload(content, safe, 81, "random-limit-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersonnelProductionMatchesNativeQuartersAndHunterFallthrough(bool soldier)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, soldier ? "productionSoldierHuntingRows" : "productionPersonnelRows")
            .GroupBy(row => (Scientist: row[0].GetBoolean(), Delay: row[1].GetInt32(), Assigned: row[2].GetInt32(), Headroom: row[3].GetInt32())))
        {
            var quarters = group.Key.Assigned + 1 + group.Key.Headroom;
            var times = group.Key.Delay < 0 ? "0" : $"0, {group.Key.Delay}";
            var content = Load($"crafts: [{{type: PERSONNEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: PERSONNEL_PRODUCT, time: 2, cost: 3, producedItems: {{SUPPLY: 1}}, spawnedPersonType: {(soldier ? "RECRUIT" : group.Key.Scientist ? "STR_SCIENTIST" : "STR_ENGINEER")}, transferTimes: [{times}]}}]\nfacilities: [{{type: PERSONNEL_WORKSHOP, mapName: LOGISTICS, workshops: 6, personnel: {quarters}}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "PERSONNEL_HUNT",
                    Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 }
                }) with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [10], Incomes = [0], Expenditures = [0] };
                initial = initial with
                {
                    NextIds = new Dictionary<string, int>(initial.NextIds) { ["STR_SOLDIER"] = 20 },
                    Bases = [initial.Bases[0] with
                    {
                        Longitude = 0.45, Latitude = 0.21, Soldiers = [], Engineers = 0, Scientists = 0, Research = [], Transfers = [],
                        Items = new Dictionary<string, int>(),
                        Facilities = [new("PERSONNEL_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                        Productions = [new("PERSONNEL_PRODUCT", group.Key.Assigned, 1, 2, row[4].GetBoolean(), false, false, new Dictionary<string, int>())]
                    }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "personnel-before.sav");
                var usedBefore = campaign.QueryStores(0).UsedQuarters;
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                var stop = progress.StopReason == "STR_PRODUCTION_COMPLETE" ? 1 : progress.StopReason == "STR_NOT_ENOUGH_LIVING_SPACE" ? 4 : 0;
                var after = campaign.Capture();
                var state = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "PERSONNEL_HUNT").Logistics!;
                var transfers = after.Bases[0].Transfers;
                Assert.All(transfers, transfer => Assert.Equal(soldier ? CampaignTransferKind.Soldier : group.Key.Scientist ? CampaignTransferKind.Scientist : CampaignTransferKind.Engineer, transfer.Kind));
                if (soldier)
                {
                    Assert.Equal(row[16].GetInt32(), after.NextIds["STR_SOLDIER"]);
                    Assert.Equal(Enumerable.Range(20, progress.Produced), transfers.Select(transfer => transfer.Soldier!.Id));
                }
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { progress.Produced, stop, usedBefore, campaign.QueryStores(0).UsedQuarters, transfers.Count, transfers[^1].Hours, after.Funds[^1], after.Bases[0].Items.GetValueOrDefault("SUPPLY"), state.Status, state.Fuel, after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                // The native trace captures before time1Hour's deferred removal.
                // Removing a stopped project releases its single reserved quarter.
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).Take(11)
                    .Select((value, index) => index == 3 ? (object)(value.GetInt32() - (stop == 0 ? 0 : 1)) : value).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "personnel-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersonnelProductionTransferLimitsStopBeforeMutation(bool population)
    {
        var content = Load("manufacture: [{name: PERSONNEL_LIMIT, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: STR_ENGINEER}]\nfacilities: [{type: PERSONNEL_LIMIT_WORKSHOP, mapName: LOGISTICS, workshops: 100001, personnel: 200000}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = new() };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = population ? 1 : int.MaxValue },
            Bases = [initial.Bases[0] with
            {
                Productions = [new("PERSONNEL_LIMIT", population ? 100001 : 1, 0, 1, true, false, false, new Dictionary<string, int>())],
                Facilities = [new("PERSONNEL_LIMIT_WORKSHOP", 1, 1, 0, 0, false, false, false)]
            }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "personnel-limit.sav");
        AssertTimeBlocked(campaign, population ? "Production personnel arrivals exceed the supported population limit." : "Production arrivals exhaust the transfer identity range.");
    }

    [Fact]
    public void QueuedPersonnelProjectReservesOneQuarterUntilCancellation()
    {
        var content = Load("manufacture: [{name: HELD_PERSONNEL, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: STR_SCIENTIST}]\n");
        var initial = HuntingSnapshot(content);
        initial = initial with { Bases = [initial.Bases[0] with { Soldiers = [], Engineers = 0, Scientists = 0, Research = [], Transfers = [], Productions = [new("HELD_PERSONNEL", 0, 0, 1, false, false, false, new Dictionary<string, int>())] }] };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "personnel-held.sav");
        Assert.Equal(1, campaign.QueryStores(0).UsedQuarters);
        Assert.Empty(campaign.Execute(new ConfigureProductionProject(0, "HELD_PERSONNEL", 0, 1, Cancel: true)).Events.OfType<CampaignActionBlocked>());
        Assert.Equal(0, campaign.QueryStores(0).UsedQuarters);
        Reload(content, campaign, 81, "personnel-cancelled.sav");
    }

    [Fact]
    public void EmptyProtectedRewardGroupsCountTowardScoreForecastWork()
    {
        var groups = string.Join(", ", Enumerable.Range(0, 1001).Select(index => $"EMPTY_SCORE_GATE_{index}: []"));
        var gates = string.Join(", ", Enumerable.Range(0, 1001).Select(index => $"{{name: EMPTY_SCORE_GATE_{index}, cost: 1}}"));
        var content = Load(overlay: $"research: [{{name: EMPTY_SCORE_SOURCE, cost: 1, getOneFreeProtected: {{{groups}}}}}, {gates}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), World = new() };
        var owner = initial.Bases[0] with { Crafts = [], Soldiers = [], Transfers = [], Productions = [], Scientists = 0, Engineers = 0, Research = [new("EMPTY_SCORE_SOURCE", 0, 1, 1)] };
        initial = initial with { Bases = Enumerable.Range(0, 1001).Select(id => owner with { Id = id }).ToArray() };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), "Research score forecasts exceed the supported work limit.");
    }

    [Fact]
    public void ExistingTransfersConsumePersonnelProductionPopulationCapacity()
    {
        var content = Load(overlay: "manufacture: [{name: POPULATED_PERSONNEL, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: STR_ENGINEER}]\nfacilities: [{type: POPULATED_PERSONNEL_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = new() };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            {
                Transfers = Enumerable.Range(1, 100000).Select(id => new TransferSnapshot(id, 2, CampaignTransferKind.Item, "SUPPLY", 1)).ToArray(),
                Facilities = [new("POPULATED_PERSONNEL_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("POPULATED_PERSONNEL", 1, 0, 1, false, false, false, new Dictionary<string, int>())]
            }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), "Production personnel arrivals exceed the supported population limit.");
    }

    [Fact]
    public void ImportedPersonnelReservationsCannotOverflowQuarters()
    {
        var content = Load("manufacture: [{name: IMPORTED_PERSONNEL, time: 1, producedItems: {SUPPLY: 0}, spawnedPersonType: STR_ENGINEER}]\n");
        var initial = HuntingSnapshot(content);
        initial = initial with { Bases = [initial.Bases[0] with { Soldiers = [], Scientists = 0, Engineers = int.MaxValue, Research = [], Transfers = [], Productions = [new("IMPORTED_PERSONNEL", 0, 0, 1, false, false, false, new Dictionary<string, int>())] }] };
        var error = Assert.Throws<InvalidDataException>(() => CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)));
        Assert.Equal("Base personnel including incoming transfers exceed the supported range.", error.Message);
    }

    [Fact]
    public void CorpseRuntimeProjectionsAndSavedOptionKeepFreshCacheSemantics()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchCorpseRows").GroupBy(row => (Recover: row[6].GetBoolean(), Corpse: row[7].GetBoolean())))
        {
            var content = Load($"items: [{{type: CORPSE_OUTPUT, recover: {group.Key.Recover.ToString().ToLowerInvariant()}, recoverCorpse: {group.Key.Corpse.ToString().ToLowerInvariant()}}}]\narmors: [{{type: CORPSE_ARMOR, corpseItem: CORPSE_OUTPUT, loftempsSet: [0]}}]\nunits: [{{type: CORPSE_TOPIC, armor: CORPSE_ARMOR}}]\n");
            var rules = content.RuntimeRules;
            var armor = rules.Armors[rules.Units[rules.Units.GetRequired("CORPSE_TOPIC")].Value.Armor!.Value].Value;
            var item = rules.Items[armor.CorpseGeoscape!.Value].Value;
            Assert.Equal("CORPSE_OUTPUT", item.Type);
            Assert.Equal(group.Key.Recover, item.Recoverable);
            Assert.Equal(group.Key.Corpse, item.CorpseRecoverable);
            var initial = HuntingSnapshot(content);
            Assert.False(initial.Options.RetainCorpses);
            foreach (var retain in new[] { false, true })
            {
                var campaign = CampaignState.Restore(initial with { Options = initial.Options with { RetainCorpses = retain } }, content, new SplitMix64RandomSource(81));
                Assert.Equal(retain, Reload(content, campaign, 81, "corpse-option.sav").Options.RetainCorpses);
            }
        }
    }

    [Fact]
    public void FixedRetainCorpsesOverridesImportedAndSavedSessionOptions()
    {
        var content = Load("fixedUserOptions: {retainCorpses: 'true'}\n");
        var initial = HuntingSnapshot(content) with { Options = new(RetainCorpses: false) };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        Assert.True(campaign.Options.RetainCorpses);
        Assert.True(Reload(content, campaign, 81, "fixed-corpse-option.sav").Options.RetainCorpses);
    }

    [Fact]
    public void LiveResearchRecoveryMatchesNativeRemovalAndDailyCallers()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchCorpseRows")
            .GroupBy(row => (Need: row[1].GetBoolean(), Destroy: row[2].GetBoolean(), Returns: row[3].GetBoolean(), Unit: row[5].GetBoolean(), Recover: row[6].GetBoolean(), Corpse: row[7].GetBoolean())))
        {
            var unit = group.Key.Unit ? "units: [{type: CORPSE_TOPIC, armor: CORPSE_ARMOR}]\n" : "";
            var content = Load($"items: [{{type: CORPSE_OUTPUT, recover: {Bool(group.Key.Recover)}, recoverCorpse: {Bool(group.Key.Corpse)}}}]\narmors: [{{type: CORPSE_ARMOR, corpseItem: CORPSE_OUTPUT, loftempsSet: [0]}}]\nresearch: [{{name: CORPSE_TOPIC, cost: 1, needItem: {Bool(group.Key.Need)}, destroyItem: {Bool(group.Key.Destroy)}, returnsItem: {Bool(group.Key.Returns)}, neededItem: SUPPLY}}]\n{unit}");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), World = new() };
                initial = initial with
                {
                    Options = initial.Options with { RetainCorpses = row[0].GetBoolean() },
                    Bases = [initial.Bases[0] with { Research = [new("CORPSE_TOPIC", 0, row[4].GetBoolean() ? 1 : 0, 1)], Items = new Dictionary<string, int>() }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "corpse-live-before.sav");
                var result = row[8].GetBoolean() && row[4].GetBoolean()
                    ? campaign.Execute(new AdvanceCampaignTime(1))
                    : campaign.Execute(new ConfigureResearchProject(0, "CORPSE_TOPIC", 0, Cancel: true));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                Assert.Empty(after.Bases[0].Research);
                var actual = JsonSerializer.SerializeToUtf8Bytes(new[] { after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.Bases[0].Items.GetValueOrDefault("CORPSE_OUTPUT") });
                TestFixtures.AssertJsonEqual(row.EnumerateArray().Skip(9).ToArray(), actual);
                Reload(content, campaign, 81, "corpse-live-after.sav");
            }
        }
        static string Bool(bool value) => value.ToString().ToLowerInvariant();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResearchCorpseOverflowStopsBeforeDailyOrCancellationMutation(bool cancel)
    {
        var content = Load("items: [{type: RECOVERY_OUTPUT}]\narmors: [{type: RECOVERY_ARMOR, corpseItem: RECOVERY_OUTPUT, loftempsSet: [0]}]\nunits: [{type: RECOVERY_TOPIC, armor: RECOVERY_ARMOR}]\nresearch: [{name: RECOVERY_TOPIC, cost: 1, needItem: true, destroyItem: true, neededItem: SUPPLY}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), World = new() };
        initial = initial with
        {
            Options = initial.Options with { RetainCorpses = true },
            Bases = [initial.Bases[0] with { Research = [new("RECOVERY_TOPIC", 0, 1, 1)], Items = new Dictionary<string, int> { ["RECOVERY_OUTPUT"] = int.MaxValue - (cancel ? 0 : 1) } }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "corpse-limit.sav");
        if (!cancel) AssertTimeBlocked(campaign, "Research refunds exceed the supported stock range.");
        else
        {
            var before = JsonSerializer.SerializeToUtf8Bytes(campaign.Capture());
            var result = campaign.Execute(new ConfigureResearchProject(0, "RECOVERY_TOPIC", 0, Cancel: true));
            Assert.Equal("Research refunds exceed the supported stock range.", Assert.Single(result.Events.OfType<CampaignActionBlocked>()).Reason);
            Assert.Equal(CanonicalJson.Normalize(before), CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(campaign.Capture())));
        }
    }

    [Fact]
    public void RecoveredCorpseStocksMatchNativeRefuelAndHunting()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchCorpseHuntingRows")
            .GroupBy(row => (Need: row[1].GetBoolean(), Destroy: row[2].GetBoolean(), Returns: row[3].GetBoolean(), Unit: row[4].GetBoolean(), Recover: row[5].GetBoolean(), Corpse: row[6].GetBoolean())))
        {
            var unit = group.Key.Unit ? "units: [{type: CORPSE_TOPIC, armor: CORPSE_ARMOR}]\n" : "";
            var content = Load($"items: [{{type: CORPSE_OUTPUT, recover: {Bool(group.Key.Recover)}, recoverCorpse: {Bool(group.Key.Corpse)}}}]\narmors: [{{type: CORPSE_ARMOR, corpseItem: CORPSE_OUTPUT, loftempsSet: [0]}}]\nresearch: [{{name: CORPSE_TOPIC, cost: 1, needItem: {Bool(group.Key.Need)}, destroyItem: {Bool(group.Key.Destroy)}, returnsItem: {Bool(group.Key.Returns)}, neededItem: SUPPLY}}]\n{unit}crafts: [{{type: CORPSE_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: CORPSE_OUTPUT}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "CORPSE_HUNT",
                    Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 }
                }) with
                { Time = new(1, 1, 1, 1999, 23, 59, 55) };
                initial = initial with
                {
                    Options = initial.Options with { RetainCorpses = row[0].GetBoolean() },
                    Bases = [initial.Bases[0] with { Longitude = 0.45, Latitude = 0.21, Soldiers = [], Research = [new("CORPSE_TOPIC", 0, 1, 1)], Items = new Dictionary<string, int>() }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "corpse-hunt-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var state = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "CORPSE_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.Bases[0].Items.GetValueOrDefault("CORPSE_OUTPUT"), state.Status, state.Fuel, after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                TestFixtures.AssertJsonEqual(row.EnumerateArray().Skip(7).ToArray(), actual);
                Reload(content, campaign, 81, "corpse-hunt-after.sav");
            }
        }
        static string Bool(bool value) => value.ToString().ToLowerInvariant();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlierCompletionCancelsFinishedCorpseProjectBeforeItsDailyCaller(bool returns)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "researchCorpseRows").Where(row => row[1].GetBoolean() && row[2].GetBoolean() && row[3].GetBoolean() == returns && row[4].GetBoolean() && row[5].GetBoolean() && row[6].GetBoolean() && row[7].GetBoolean() && !row[8].GetBoolean()).ToArray();
        Assert.Equal(2, rows.Length);
        var content = Load($"items: [{{type: CROSS_CORPSE}}]\narmors: [{{type: CROSS_CORPSE_ARMOR, corpseItem: CROSS_CORPSE, loftempsSet: [0]}}]\nunits: [{{type: CROSS_CORPSE_TOPIC, armor: CROSS_CORPSE_ARMOR}}]\nresearch: [{{name: CROSS_CORPSE_TOPIC, cost: 1, needItem: true, destroyItem: true, returnsItem: {returns.ToString().ToLowerInvariant()}, neededItem: SUPPLY}}]\ncrafts: [{{type: CROSS_CORPSE_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: CROSS_CORPSE}}]\n");
        foreach (var row in rows)
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "CROSS_CORPSE_HUNT",
                Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 }
            }) with
            { Time = new(1, 1, 1, 1999, 23, 59, 55) };
            var owner = initial.Bases[0] with { Longitude = 0.45, Latitude = 0.21, Soldiers = [], Research = [new("CROSS_CORPSE_TOPIC", 0, 1, 1)], Items = new Dictionary<string, int>() };
            initial = initial with { Options = initial.Options with { RetainCorpses = row[0].GetBoolean() }, Bases = [owner with { Crafts = [] }, owner with { Id = 1 }] };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "cross-corpse-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            Assert.Single(result.Events.OfType<CampaignResearchCompleted>());
            var after = campaign.Capture();
            Assert.All(after.Bases, baseState => Assert.Empty(baseState.Research));
            Assert.Equal(row[9].GetInt32(), after.Bases[1].Items.GetValueOrDefault("SUPPLY"));
            Assert.Equal(row[10].GetInt32() != 0, after.World.Ufos[0].Hunting);
            Assert.Equal(0, after.Bases[1].Items.GetValueOrDefault("CROSS_CORPSE"));
            Reload(content, campaign, 81, "cross-corpse-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FixedProductionSalesMatchNativeContinuationAndSharedFunds(bool crossBase, bool scripted = false)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionSaleRows")
            .GroupBy(row => (Cost: row[0].GetInt32(), Quantity: row[4].GetInt32(), Coefficient: row[5].GetInt32(), Delay: row[6].GetInt32())))
        {
            var coefficient = group.Key.Coefficient;
            var itemScript = scripted ? ", scripts: {sellCostItem: 'mul cost_current 2; return cost_current;'}" : "";
            var events = scripted ? "extended: {scripts: {sellCostItem: [{new: before-production-price, offset: -1, code: 'add cost_current 1; return cost_current;'}, {new: after-production-price, offset: 1, code: 'div cost_current 2; sub cost_current 1; return cost_current;'}]}}\n" : "";
            var caseContent = Load($"sellPriceCoefficient: [{coefficient}, {coefficient}, {coefficient}, {coefficient}, {coefficient}]\nitems: [{{type: SALE_OUTPUT, costSell: 7{itemScript}}}]\ncrafts: [{{type: SALE_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: SALE_PRODUCT, time: 2, cost: {group.Key.Cost}, producedItems: {{SALE_OUTPUT: {group.Key.Quantity}}}, transferTimes: [{group.Key.Delay}]}}, {{name: SALE_LATER, time: 1, cost: 3, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: SALE_WORKSHOP, mapName: LOGISTICS, workshops: 8}}]\n" + events);
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(caseContent).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "SALE_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                }) with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [row[1].GetInt32()], Incomes = [0], Expenditures = [0] };
                var owner = initial.Bases[0] with
                {
                    Longitude = 0.45,
                    Latitude = 0.21,
                    Engineers = 0,
                    Soldiers = [],
                    Items = new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("SALE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("SALE_PRODUCT", row[2].GetInt32() * 2, 1, 2, row[3].GetBoolean(), true, false, new Dictionary<string, int>()),
                            new("SALE_LATER", 2, 0, 3, false, false, false, new Dictionary<string, int>())]
                };
                initial = initial with
                {
                    Bases = crossBase
                        ? [owner with { Crafts = [], Productions = [owner.Productions[0]] }, owner with { Id = 1, Productions = [owner.Productions[1]] }]
                        : [owner]
                };
                var campaign = Reload(caseContent, CampaignState.Restore(initial, caseContent, new SplitMix64RandomSource(81)), 81, "sale-product-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var sale = Assert.Single(result.Events.OfType<CampaignProductionProgress>(), progress => progress.RuleId == "SALE_PRODUCT");
                var later = Assert.Single(result.Events.OfType<CampaignProductionProgress>(), progress => progress.RuleId == "SALE_LATER");
                var after = campaign.Capture();
                var targetBase = after.Bases[^1];
                var state = Assert.Single(targetBase.Crafts, craft => craft.RuleId == "SALE_PRODUCT_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                        sale.Produced, Stop(sale), later.Produced, Stop(later), after.Funds[^1], after.Incomes[^1], after.Expenditures[^1],
                        targetBase.Items.GetValueOrDefault("SUPPLY"), after.Bases.Sum(item => item.Transfers.Count), state.Status, state.Fuel,
                        after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0
                });
                TestFixtures.AssertJsonEqual(row.EnumerateArray().Skip(7).ToArray(), actual);
                Reload(caseContent, campaign, 81, "sale-product-after.sav");
            }
        }
        static int Stop(CampaignProductionProgress progress) => progress.StopReason == "STR_PRODUCTION_COMPLETE" ? 1 : progress.StopReason == "STR_NOT_ENOUGH_MONEY" ? 2 : 0;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedProductionSaleValuesMatchNativePerItemAccounting(bool hunting)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionMixedSaleRows"))
        {
            var content = Load($"sellPriceCoefficient: [100, 100, 100, 100, 100]\nitems: [{{type: MIXED_FIRST, costSell: {row[0].GetInt32()}}}, {{type: MIXED_LAST, costSell: {row[1].GetInt32()}}}]\nmanufacture: [{{name: MIXED_SALE, time: 1, producedItems: {{MIXED_FIRST: 1, MIXED_LAST: 1}}}}]\nfacilities: [{{type: MIXED_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [10], Incomes = [0], Expenditures = [0] };
            initial = initial with
            {
                World = hunting ? initial.World : new(),
                Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("MIXED_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("MIXED_SALE", 1, 0, 1, false, true, false, new Dictionary<string, int>())]
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "mixed-sale-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var actual = JsonSerializer.SerializeToUtf8Bytes(new[] { after.Funds[^1], after.Incomes[^1], after.Expenditures[^1] });
            TestFixtures.AssertJsonEqual(row.EnumerateArray().Skip(2).ToArray(), actual);
            Reload(content, campaign, 81, "mixed-sale-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptedProductionSalePriceUsesTheActualHourlyContextDuringHunting(bool global)
    {
        var script = "var ptr Time moment; var int second; geoscape_game.getTime moment; moment.getSecond second; return second;";
        var overlay = "items: [{type: SCRIPTED_SALE_ITEM, costSell: 7" + (global ? "" : $", scripts: {{sellCostItem: '{script}'}}") +
            "}]\nmanufacture: [{name: SCRIPTED_SALE, time: 1, producedItems: {SCRIPTED_SALE_ITEM: 1}}]\nfacilities: [{type: SCRIPTED_SALE_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n" +
            (global ? $"extended: {{scripts: {{sellCostItem: [{{new: future-sale-price, offset: 1, code: '{script}'}}]}}}}\n" : "");
        var content = Load(overlay);
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [0] };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Facilities = [.. initial.Bases[0].Facilities, new("SCRIPTED_SALE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("SCRIPTED_SALE", 1, 0, 1, false, true, false, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "scripted-sale-before.sav");
        AdvanceUnblocked(campaign, 1);
        var ordinary = CampaignState.Restore(initial with { World = new() }, content, new SplitMix64RandomSource(81));
        var result = ordinary.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(0, ordinary.Capture().Funds[^1]); // The actual caller runs at 02:00:00.
        Assert.Equal(ordinary.Capture().Funds, campaign.Capture().Funds);
        Reload(content, campaign, 81, "scripted-hunting-sale-after.sav");
        Reload(content, ordinary, 81, "scripted-sale-after.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ProductionFuturePriceFailuresAndAggregateWorkAreAtomic(bool hunting, bool workLimit)
    {
        var script = workLimit ? "loop var i 200000; end; return cost_current;" :
            "var ptr Time moment; var int second; geoscape_game.getTime moment; moment.getSecond second; div cost_current second; return cost_current;";
        var content = Load($"items: [{{type: PRICE_FAILURE_ITEM, costSell: 7, scripts: {{sellCostItem: '{script}'}}}}]\nmanufacture: [{{name: PRICE_FAILURE_PRODUCT, time: 1, producedItems: {{PRICE_FAILURE_ITEM: 1}}}}]\nfacilities: [{{type: PRICE_FAILURE_WORKSHOP, mapName: LOGISTICS, workshops: 4}}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [0] };
        initial = initial with
        {
            World = hunting ? initial.World : new(),
            Bases = [initial.Bases[0] with
        {
            Facilities = [.. initial.Bases[0].Facilities, new("PRICE_FAILURE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("PRICE_FAILURE_PRODUCT", workLimit ? 4 : 1, 0, workLimit ? 4 : 1, false, true, false, new Dictionary<string, int>())],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "price-failure-before.sav");
        var before = campaign.Capture();
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var reason = Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason;
        Assert.Contains(workLimit ? "limit" : "operation 'div' failed", reason, StringComparison.Ordinal);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Reload(content, campaign, 81, "price-failure-blocked.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownScriptedProductionPricesReserveTheirActualAccountingRange(bool hunting)
    {
        foreach (var price in new[] { -7, 7 })
        {
            var content = Load($"items: [{{type: EXACT_PRICE_ITEM, costSell: 1, scripts: {{sellCostItem: 'return {price};'}}}}]\nmanufacture: [{{name: EXACT_PRICE_PRODUCT, time: 1, producedItems: {{EXACT_PRICE_ITEM: 1}}}}]\nfacilities: [{{type: EXACT_PRICE_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            var funds = price > 0 ? long.MaxValue - 10 : long.MinValue + 10;
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [funds], Incomes = [0], Expenditures = [0] };
            initial = initial with
            {
                World = hunting ? initial.World : new(),
                Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("EXACT_PRICE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("EXACT_PRICE_PRODUCT", 1, 0, 1, false, true, false, new Dictionary<string, int>())],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "exact-price-before.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(funds + price, campaign.Capture().Funds[^1]);
            Assert.Equal(Math.Max(0, price), campaign.Capture().Incomes[^1]);
            Assert.Equal(Math.Max(0, -price), campaign.Capture().Expenditures[^1]);
            Reload(content, campaign, 81, "exact-price-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionPricePreflightUsesOnlyTheDueFutureCaller(bool completes)
    {
        var script = completes ? "var int days; geoscape_game.getDaysPassed days; if eq days 12; div cost_current 0; end; return days;" : "div cost_current 0; return cost_current;";
        var content = Load($"items: [{{type: DUE_PRICE_ITEM, costSell: 7, scripts: {{sellCostItem: '{script}'}}}}]\nmanufacture: [{{name: DUE_PRICE_PRODUCT, time: 1, producedItems: {{DUE_PRICE_ITEM: 1}}}}]\nfacilities: [{{type: DUE_PRICE_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), DaysPassed = 12, Funds = [0] };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Research = [],
            Facilities = [.. initial.Bases[0].Facilities, new("DUE_PRICE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("DUE_PRICE_PRODUCT", completes ? 1 : 0, 0, 1, false, true, false, new Dictionary<string, int>())],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "due-price-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(completes ? 13 : 0, campaign.Capture().Funds[^1]);
        Assert.Equal(completes ? 0 : 1, campaign.Capture().Bases[0].Productions.Count);
        var control = CampaignState.Restore(initial with { World = new() }, content, new SplitMix64RandomSource(81));
        AdvanceUnblocked(control, 1);
        Assert.Equal(control.Capture().Funds, campaign.Capture().Funds);
        Reload(content, campaign, 81, "due-price-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionPriceVmMatchesNativeCallerContextAndAccounting(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionScriptSaleContextRows")
                     .Where(row => row[0].GetInt32() == 4 || row[2].GetInt32() == 0))
        {
            var mode = row[0].GetInt32();
            var daily = row[1].GetInt32() != 0;
            var researched = row[2].GetInt32() != 0;
            var script = mode switch
            {
                0 => "var ptr Time moment; var int value; geoscape_game.getTime moment; moment.getSecond value; return value;",
                1 => "var ptr Time moment; var int value; var int days; geoscape_game.getTime moment; moment.getHour value; geoscape_game.getDaysPassed days; add value days; return value;",
                2 => "var int value; geoscape_game.getDaysPassed value; return value;",
                3 => "var int value; var int months; geoscape_game.difficultyLevel value; geoscape_game.getMonthsPassed months; add value months; return value;",
                _ => "var ptr RuleResearch topic; var ptr Time moment; var int known; var int days; var int hour; rules.getRuleResearch topic \"PRICE_TOPIC\"; geoscape_game.isResearched known topic; if eq known 1; geoscape_game.getDaysPassed days; return days; end; geoscape_game.getTime moment; moment.getHour hour; return hour;",
            };
            script = script.Replace("'", "''", StringComparison.Ordinal);
            var overlay = "research: [{name: PRICE_TOPIC, cost: 1}]\nitems: [{type: CONTEXT_SALE_ITEM, costSell: 7" +
                (global ? "" : $", scripts: {{sellCostItem: '{script}'}}") +
                "}]\nmanufacture: [{name: CONTEXT_SALE, time: 1, producedItems: {CONTEXT_SALE_ITEM: 2}}]\nfacilities: [{type: CONTEXT_WORKSHOP, mapName: LOGISTICS, workshops: 1, labs: 1}]\n" +
                (global ? $"extended: {{scripts: {{sellCostItem: [{{new: context-sale-price, offset: 1, code: '{script}'}}]}}}}\n" : "");
            var content = Load(overlay);
            var initial = HuntingSnapshot(content) with
            {
                Time = new(1, 1, 1, 1999, daily ? 23 : 1, 59, 55),
                DaysPassed = 12,
                MonthsPassed = 3,
                Difficulty = CampaignDifficulty.Superhuman,
                Funds = [0],
                DebugMode = true,
                CompletedResearch = researched && !daily ? ["PRICE_TOPIC"] : [],
            };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("CONTEXT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Research = researched && daily ? [new("PRICE_TOPIC", 1, 0, 1)] : [],
                Productions = [new("CONTEXT_SALE", 1, 0, 1, false, true, false, new Dictionary<string, int>())],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "context-sale-before.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(row[9].GetInt64(), campaign.Capture().Funds[^1]);
            Assert.Equal(row[5].GetInt32(), campaign.Capture().DaysPassed);
            Assert.Equal(researched, campaign.Capture().CompletedResearch.Contains("PRICE_TOPIC"));
            var control = CampaignState.Restore(initial with { World = new() }, content, new SplitMix64RandomSource(81));
            AdvanceUnblocked(control, 1);
            Assert.Equal(control.Capture().Funds, campaign.Capture().Funds);
            campaign = Reload(content, campaign, 81, "context-sale-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "context-sale-following.sav");
        }
    }

    [Theory]
    [InlineData("uncertain")]
    [InlineData("unrelated")]
    [InlineData("dead")]
    [InlineData("null")]
    public void DailyProductionPriceQueriesOnlyGuardConsumedUncertainResearch(string query)
    {
        var script = query switch
        {
            "dead" => "var ptr RuleResearch topic; var int known; if eq 0 1; rules.getRuleResearch topic \"PRICE_REWARD_A\"; geoscape_game.isResearched known topic; return known; end; return 7;",
            "null" => "var ptr RuleResearch topic; var int known; geoscape_game.isResearched known topic; return known;",
            _ => $"var ptr RuleResearch topic; var int known; rules.getRuleResearch topic \"{(query == "uncertain" ? "PRICE_REWARD_A" : "PRICE_UNRELATED")}\"; geoscape_game.isResearched known topic; return known;",
        };
        var overlay = "research: [{name: PRICE_RANDOM_SOURCE, cost: 1, getOneFree: [PRICE_REWARD_A, PRICE_REWARD_B]}, {name: PRICE_REWARD_A, cost: 1}, {name: PRICE_REWARD_B, cost: 1}, {name: PRICE_UNRELATED, cost: 1}]\n" +
            $"items: [{{type: QUERY_PRICE_ITEM, costSell: 7, scripts: {{sellCostItem: '{script}'}}}}]\n" +
            "manufacture: [{name: QUERY_PRICE_PRODUCT, time: 1, producedItems: {QUERY_PRICE_ITEM: 1}}]\nfacilities: [{type: QUERY_PRICE_LAB, mapName: LOGISTICS, labs: 1, workshops: 1}]\n";
        var content = Load(overlay);
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), Funds = [0] };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Research = [new("PRICE_RANDOM_SOURCE", 1, 0, 1)],
            Facilities = [.. initial.Bases[0].Facilities, new("QUERY_PRICE_LAB", 1, 1, 0, 0, false, false, false)],
            Productions = [new("QUERY_PRICE_PRODUCT", 1, 0, 1, false, true, false, new Dictionary<string, int>())],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "query-price-before.sav");
        if (query == "uncertain")
            AssertTimeBlocked(campaign, "Price script failed: Script binding 'GeoscapeGame.isResearched' failed: Production prices require uncertain daily research forecasts..");
        else
        {
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(query == "dead" ? 7 : 0, campaign.Capture().Funds[^1]);
            Assert.Contains("PRICE_RANDOM_SOURCE", campaign.Capture().CompletedResearch);
            var controlContent = Load(overlay.Replace(script, query == "dead" ? "return 7;" : "return 0;", StringComparison.Ordinal));
            var control = CampaignState.Restore(initial, controlContent, new SplitMix64RandomSource(81));
            AdvanceUnblocked(control, 1);
            Assert.Equal(control.Capture().RandomState, campaign.Capture().RandomState);
            Assert.Equal(control.Capture().CompletedResearch, campaign.Capture().CompletedResearch);
            Reload(content, campaign, 81, "query-price-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionPriceClockPointersMatchNativeIdentity(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "priceTimePointerRows"))
        {
            var script = "var ptr GeoscapeGame absent null; var ptr Time a; var ptr Time b; var int second; " +
                $"{(row[0].GetBoolean() ? "geoscape_game" : "absent")}.getTime a; " +
                $"{(row[0].GetBoolean() ? "geoscape_game" : "absent")}.getTime b; " +
                $"if {(row[2].GetBoolean() ? "neq" : "eq")} a b; div cost_current 0; end; " +
                $"if {(row[1].GetBoolean() ? "eq" : "neq")} a null; div cost_current 0; end; " +
                "a.getSecond second; add second 7; return second;";
            var content = Load("manufacture: [{name: CLOCK_PRICE_PRODUCT, time: 1, producedItems: {CLOCK_PRICE_ITEM: 1}}]\nfacilities: [{type: CLOCK_PRICE_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n" +
                (global ? $"items: [{{type: CLOCK_PRICE_ITEM}}]\nextended: {{scripts: {{sellCostItem: [{{new: clock-identity-price, offset: 1, code: '{script}'}}]}}}}\n" :
                    $"items: [{{type: CLOCK_PRICE_ITEM, scripts: {{sellCostItem: '{script}'}}}}]\n"));
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), Funds = [0] };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
                {
                    Facilities = [.. initial.Bases[0].Facilities, new("CLOCK_PRICE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("CLOCK_PRICE_PRODUCT", 1, 0, 1, false, true, false, new Dictionary<string, int>())],
                }],
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "clock-pointer-before.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(7, campaign.Capture().Funds[^1]);
            Reload(content, campaign, 81, "clock-pointer-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionPriceRulePointersKeepNativeDefaultsAndSentinels(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        Assert.Equal(0, oracle.RootElement.GetProperty("noneItem").GetInt32());
        const string script = "var ptr RuleMod absent null; var ptr RuleResearch topic; var ptr RuleResearch lookup; var ptr RuleItem item; var ptr RuleItem wanted; var int value; " +
            "rules.getRuleItem item \"STR_NONE\"; if neq item null; div cost_current 0; end; " +
            "rules.getRuleResearch topic \"SUPPLY\"; topic.getNeededItem item; rules.getRuleItem wanted \"SUPPLY\"; if neq item wanted; div cost_current 0; end; " +
            "rules.getRuleResearch topic \"SELF_PRICE_TOPIC\"; topic.getLookup lookup; if neq lookup null; div cost_current 0; end; " +
            "absent.getRuleResearch topic \"SUPPLY\"; topic.getCost value; if neq value 0; div cost_current 0; end; " +
            "rules.getRuleItem item \"RULE_POINTER_PRICE_ITEM\"; if neq item item_rule; div cost_current 0; end; return 7;";
        var content = Load("research: [{name: SUPPLY, cost: 1, needItem: true}, {name: SELF_PRICE_TOPIC, cost: 1, lookup: SELF_PRICE_TOPIC}]\nmanufacture: [{name: RULE_POINTER_PRICE_PRODUCT, time: 1, producedItems: {RULE_POINTER_PRICE_ITEM: 1}}]\nfacilities: [{type: RULE_POINTER_PRICE_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n" +
            (global ? $"items: [{{type: STR_NONE}}, {{type: RULE_POINTER_PRICE_ITEM}}]\nextended: {{scripts: {{sellCostItem: [{{new: rule-pointer-price, offset: 1, code: '{script}'}}]}}}}\n" :
                $"items: [{{type: STR_NONE}}, {{type: RULE_POINTER_PRICE_ITEM, scripts: {{sellCostItem: '{script}'}}}}]\n"));
        Assert.True(content.RuntimeRules.Items.TryGet("STR_NONE", out _));
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [0] };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("RULE_POINTER_PRICE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("RULE_POINTER_PRICE_PRODUCT", 1, 0, 1, false, true, false, new Dictionary<string, int>())],
            }],
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "rule-pointer-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(7, campaign.Capture().Funds[^1]);
        Reload(content, campaign, 81, "rule-pointer-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionPriceResearchGettersMatchNativeMetadataAndNulls(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        using var api = TestFixtures.ReadVerifiedExpected("script-api-catalog");
        foreach (var name in new[] { "RuleResearch.getCost", "RuleResearch.getPoints", "RuleResearch.getLookup", "RuleResearch.getNeededItem" })
            Assert.Contains(TestFixtures.Rows(api.RootElement, "bindings"), binding =>
                binding.GetProperty("name").GetString() == name &&
                binding.GetProperty("parsers").EnumerateArray().Any(parser => parser.GetString() == "sellCostItem"));
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchPriceGetterRows"))
            for (var mode = 0; mode < 4; mode++)
            {
                var present = row[0].GetBoolean();
                var linked = row[1].GetBoolean();
                var needed = row[2].GetBoolean();
                var script = "var ptr RuleResearch topic; var ptr RuleResearch lookup; var ptr RuleResearch again; var ptr RuleItem item; var ptr RuleItem wanted; var int value; " +
                    $"rules.getRuleResearch topic \"{(present ? "PRICE_META_SOURCE" : "MISSING")}\"; " + (mode switch
                    {
                        0 => "topic.getCost value; return value;",
                        1 => "topic.getPoints value; return value;",
                        2 => "topic.getLookup lookup; rules.getRuleResearch again \"PRICE_META_LOOKUP\"; if neq lookup null; if neq lookup again; div cost_current 0; end; end; lookup.getPoints value; return value;",
                        _ => "topic.getNeededItem item; rules.getRuleItem wanted \"SUPPLY\"; if neq item null; if neq item wanted; div cost_current 0; end; return 1; end; return 0;",
                    });
                var overlay = $"research: [{{name: PRICE_META_SOURCE, cost: {row[3].GetInt32()}, points: {row[4].GetInt32()}, needItem: {needed.ToString().ToLowerInvariant()}, neededItem: SUPPLY{(linked ? ", lookup: PRICE_META_LOOKUP" : "")}}}, {{name: PRICE_META_LOOKUP, cost: 1, points: 23}}]\n" +
                    (global ? $"extended: {{scripts: {{sellCostItem: [{{new: research-metadata-price, offset: 1, code: '{script}'}}]}}}}\nitems: [{{type: PRICE_META_ITEM}}]\n" : $"items: [{{type: PRICE_META_ITEM, scripts: {{sellCostItem: '{script}'}}}}]\n") +
                    "manufacture: [{name: PRICE_META_PRODUCT, time: 1, producedItems: {PRICE_META_ITEM: 1}}]\nfacilities: [{type: PRICE_META_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n";
                var content = Load(overlay);
                var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [0] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                    {
                        Facilities = [.. initial.Bases[0].Facilities, new("PRICE_META_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                        Productions = [new("PRICE_META_PRODUCT", 1, 0, 1, false, true, false, new Dictionary<string, int>())],
                    }],
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "research-metadata-before.sav");
                AdvanceUnblocked(campaign, 1);
                Assert.Equal(row[5 + mode].GetInt32(), campaign.Capture().Funds[^1]);
                Reload(content, campaign, 81, "research-metadata-after.sav");
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyProductionPricesObserveNativeDeterministicResearchRewards(bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        const string script = "var ptr RuleResearch topic; var int a; var int b; var int source; rules.getRuleResearch topic \"PRICE_REWARD_A\"; geoscape_game.isResearched a topic; rules.getRuleResearch topic \"PRICE_REWARD_B\"; geoscape_game.isResearched b topic; rules.getRuleResearch topic \"PRICE_REWARD_SOURCE\"; geoscape_game.isResearched source topic; mul b 10; mul source 100; add a b; add a source; return a;";
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchRewardRows")
                     .GroupBy(row => (Sequential: row[0].GetBoolean(), Duplicate: row[1].GetBoolean())))
        {
            var overlay = $"research: [{{name: PRICE_REWARD_SOURCE, cost: 1, sequentialGetOneFree: {group.Key.Sequential.ToString().ToLowerInvariant()}, getOneFree: [{(group.Key.Duplicate ? "PRICE_REWARD_A, PRICE_REWARD_A" : "PRICE_REWARD_A")}], getOneFreeProtected: {{PRICE_REWARD_GATE: [PRICE_REWARD_B]}}}}, {{name: PRICE_REWARD_A, cost: 1}}, {{name: PRICE_REWARD_B, cost: 1}}, {{name: PRICE_REWARD_GATE, cost: 1}}]\n" +
                (global ? $"extended: {{scripts: {{sellCostItem: [{{new: price-reward, offset: 1, code: '{script}'}}]}}}}\nitems: [{{type: PRICE_REWARD_ITEM}}]\n" : $"items: [{{type: PRICE_REWARD_ITEM, scripts: {{sellCostItem: '{script}'}}}}]\n") +
                "manufacture: [{name: PRICE_REWARD_PRODUCT, time: 1, producedItems: {PRICE_REWARD_ITEM: 1}}]\nfacilities: [{type: PRICE_REWARD_LAB, mapName: LOGISTICS, labs: 1, workshops: 1}]\n";
            var content = Load(overlay);
            foreach (var row in group)
            {
                List<string> completed = [];
                if (row[2].GetInt32() == 1) completed.Add("PRICE_REWARD_A");
                if (row[3].GetInt32() == 1) completed.Add("PRICE_REWARD_B");
                if (row[4].GetBoolean()) completed.Add("PRICE_REWARD_GATE");
                Dictionary<string, int> statuses = new(StringComparer.Ordinal);
                if (row[2].GetInt32() == 2) statuses.Add("PRICE_REWARD_A", 2);
                if (row[3].GetInt32() == 2) statuses.Add("PRICE_REWARD_B", 2);
                var initial = HuntingSnapshot(content) with
                {
                    Time = new(1, 1, 1, 1999, 23, 59, 55),
                    Funds = [0],
                    World = new(),
                    CompletedResearch = completed,
                    ResearchRuleStatus = statuses,
                };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                    {
                        Research = [new("PRICE_REWARD_SOURCE", 1, 0, 1)],
                        Facilities = [.. initial.Bases[0].Facilities, new("PRICE_REWARD_LAB", 1, 1, 0, 0, false, false, false)],
                        Productions = [new("PRICE_REWARD_PRODUCT", 1, 0, 1, false, true, false, new Dictionary<string, int>())],
                    }],
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "price-reward-before.sav");
                if (!group.Key.Sequential && row[2].GetInt32() == 0 && row[3].GetInt32() == 0 && row[4].GetBoolean())
                {
                    AssertTimeBlocked(campaign, $"Price {(global ? "event" : "script")} failed: Script binding 'GeoscapeGame.isResearched' failed: Production prices require uncertain daily research forecasts..");
                    continue;
                }
                AdvanceUnblocked(campaign, 1);
                var expected = 100 + (row[2].GetInt32() == 1 || row[5].GetInt32() == 1 ? 1 : 0) +
                    (row[3].GetInt32() == 1 || row[5].GetInt32() == 2 ? 10 : 0);
                Assert.Equal(expected, campaign.Capture().Funds[^1]);
                var controlContent = Load(overlay.Replace(script, $"return {expected};", StringComparison.Ordinal));
                var control = CampaignState.Restore(initial, controlContent, new SplitMix64RandomSource(81));
                AdvanceUnblocked(control, 1);
                Assert.Equal(control.Capture().RandomState, campaign.Capture().RandomState);
                Assert.Equal(control.Capture().CompletedResearch, campaign.Capture().CompletedResearch);
                Reload(content, campaign, 81, "price-reward-after.sav");
            }
        }
    }

    [Theory]
    [InlineData("funds", true)]
    [InlineData("income", true)]
    [InlineData("total", true)]
    [InlineData("mixedfunds", true)]
    [InlineData("mixedincome", true)]
    [InlineData("mixedfunds", false)]
    [InlineData("mixedincome", false)]
    public void ProductionSaleAccountingOverflowStopsBeforeTickMutation(string field, bool hunting)
    {
        var mixed = field.StartsWith("mixed", StringComparison.Ordinal);
        var outputs = field == "total" ? "SALE_LIMIT_A: 2147483647, SALE_LIMIT_B: 2147483647, SALE_LIMIT_C: 2147483647" : mixed ? "SALE_LIMIT_A: 1, SALE_LIMIT_B: 1" : "SALE_LIMIT_A: 1";
        var content = Load($"sellPriceCoefficient: [100, 100, 100, 100, 100]\nitems: [{{type: SALE_LIMIT_A, costSell: {(mixed ? 7 : int.MaxValue)}}}, {{type: SALE_LIMIT_B, costSell: {(mixed ? -7 : int.MaxValue)}}}, {{type: SALE_LIMIT_C, costSell: 2147483647}}]\nmanufacture: [{{name: SALE_LIMIT, time: 1, producedItems: {{{outputs}}}}}]\nfacilities: [{{type: SALE_LIMIT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
        var initial = HuntingSnapshot(content) with
        {
            Time = new(1, 1, 1, 1999, 1, 59, 55),
            Funds = [field.EndsWith("funds", StringComparison.Ordinal) ? long.MaxValue : 0],
            Incomes = [field.EndsWith("income", StringComparison.Ordinal) ? long.MaxValue : 0]
        };
        initial = initial with
        {
            World = hunting ? initial.World : new(),
            Bases = [initial.Bases[0] with
        {
            Facilities = [.. initial.Bases[0].Facilities, new("SALE_LIMIT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("SALE_LIMIT", 1, 0, 1, false, true, false, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "sale-limit-before.sav");
        AssertTimeBlocked(campaign, "Production accounting exceeds the supported range.");
    }

    [Fact]
    public void ProductionCompletionCountMatchesPinnedCaller()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionProgressRows"))
            Assert.Equal(row[4].GetInt32(), ProductionProgress.Requested(row[0].GetInt32(),
                row[1].GetInt32(), row[2].GetInt32(), 20, row[3].GetBoolean()));
    }

    [Theory]
    [InlineData("fuel")]
    [InlineData("craft")]
    [InlineData("ammunition")]
    [InlineData("overflow")]
    [InlineData("fallback")]
    public void IncompleteProductionDoesNotChangeHunterTargetsOrStocks(string source)
    {
        var clip = source == "ammunition" ? "clip: SUPPLY" : "clip: ''";
        var fuel = source == "ammunition" ? "" : ", refuelItem: SUPPLY";
        var required = source == "craft" ? "requiredItems: {UNFINISHED_HUNT: 1}, " : "";
        var content = Load($"crafts: [{{type: UNFINISHED_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuel}}}]\ncraftWeapons: [{{type: UNFINISHED_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, {clip}}}]\nmanufacture: [{{name: UNFINISHED_PRODUCT, time: 100, {required}producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: UNFINISHED_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "UNFINISHED_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = source == "ammunition" ? [new("UNFINISHED_GUN", 0, Rearming: false)] : [],
            },
        }) with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21,
            Items = new Dictionary<string, int> { ["SUPPLY"] = 1 },
            Facilities = [.. initial.Bases[0].Facilities, new("UNFINISHED_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("UNFINISHED_PRODUCT", 1, source == "overflow" ? int.MaxValue : 0, 1, false, false, source == "fallback", new Dictionary<string, int>())],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "unfinished-production-before.sav");
        if (source == "overflow")
        {
            AssertTimeBlocked(campaign, "Production progress exceeds the supported range.");
            return;
        }
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Equal(1, after.Bases[0].Productions.Single().Spent);
        Assert.Equal(source == "ammunition" ? 1 : 0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("UNFINISHED_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "unfinished-production-after.sav");
    }

    [Theory]
    [InlineData("production", "UFO retargeting requires production fuel-stock forecasts.")]
    [InlineData("craft", "UFO retargeting requires production craft-consumption forecasts.")]
    public void UnforecastHourlyStockInputsStopBeforeMutation(string source, string reason)
    {
        var overlay = "crafts: [{type: STOCK_GUARD_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true, weapons: 1}]\n";
        overlay += source switch
        {
            "production" => "manufacture: [{name: HUNT_STOCK_PRODUCT, time: 1, producedItems: {SUPPLY: 1}, randomProducedItems: [[1, {SUPPLY: 1}], [1, {BULKY: 1}]]}]\n",
            _ => "manufacture: [{name: HUNT_STOCK_PRODUCT, time: 1, requiredItems: {STOCK_GUARD_HUNT: 1}, producedItems: {SUPPLY: 1}}]\n",
        };
        var content = Load(overlay: overlay);
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "STOCK_GUARD_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = [],
            },
        }) with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var owner = initial.Bases[0];
        if (source is "production" or "craft") owner = owner with
        {
            Facilities = [.. owner.Facilities, new("FUEL_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("HUNT_STOCK_PRODUCT", 1, 0, source == "craft" ? 2 : 1, false, false, false, new Dictionary<string, int>())],
        };
        initial = initial with { Bases = [owner] };
        if (source == "craft")
        {
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
            Assert.DoesNotContain(campaign.Capture().Bases[0].Crafts, craft => craft.RuleId == "STOCK_GUARD_HUNT");
            Assert.False(campaign.Capture().World.Ufos[0].Hunting);
            Reload(content, campaign, 81, "consumption-forecast-after.sav");
            return;
        }
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), reason);
    }

    [Fact]
    public void RelaunchingLeaderSpeedFeedsEscortFuelBeforeHunting()
    {
        var content = Load(overlay: "crafts: [{type: RELAUNCH_LEADER, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 29, 55) };
        var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var leader = template with
        {
            RuleId = "RELAUNCH_LEADER",
            Id = 2,
            Logistics = template.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = [],
            },
        };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Fuel = 50,
                Speed = 6000,
                Weapons = [],
                Destination = new(WorldTargetKind.Craft, "RELAUNCH_LEADER", 2, 0.45, 0.21),
            },
        });
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Crafts = [.. initial.Bases[0].Crafts, leader],
        }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var after = campaign.Capture();
        var escort = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(20, escort.Fuel);
        Assert.False(escort.LowFuel);
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("SHIP", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "relaunched-leader-escort.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlierLeaderFuelReturnChangesLaterEscortConsumption(bool leaderFirst)
    {
        var content = Load(overlay: "crafts: [{type: FUEL_RETURN_LEADER, speedMax: 2000, damageMax: 100, fuelMax: 100}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 9, 55) };
        var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var leader = template with
        {
            RuleId = "FUEL_RETURN_LEADER",
            Id = 2,
            Logistics = template.Logistics! with { Fuel = 0, Speed = 0, Weapons = [] },
        };
        var escort = template with
        {
            Logistics = template.Logistics! with
            {
                Fuel = 50,
                Speed = 6000,
                Weapons = [],
                Destination = new(WorldTargetKind.Craft, "FUEL_RETURN_LEADER", 2, 0.45, 0.21),
            }
        };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Soldiers = [],
            Crafts = leaderFirst ? [leader, escort] : [escort, leader],
        }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var after = campaign.Capture();
        var state = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(leaderFirst ? 20 : 0, state.Fuel);
        // At the base, the return arrival clears low-fuel after the hunt caller.
        Assert.Equal(leaderFirst, after.World.Ufos[0].Hunting);
        Reload(content, campaign, 81, "ordered-escort-fuel.sav");
    }

    [Fact]
    public void ComposedEscortFuelAndHuntingMatchNativeOrder()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load("crafts:\n  - {type: HUNT_FUEL_ESCORT, speedMax: 6000, damageMax: 100, fuelMax: 100, allowLanding: false}\n  - {type: HUNT_FUEL_LEADER, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, allowLanding: false}\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "escortFuelRows"))
        {
            var relaunch = row[0].GetBoolean();
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, relaunch ? 29 : 9, 55) };
            var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
            var leader = template with
            {
                RuleId = "HUNT_FUEL_LEADER",
                Id = 2,
                Logistics = template.Logistics! with
                {
                    Status = relaunch ? "STR_REFUELLING" : "STR_OUT",
                    Fuel = relaunch ? 60 : 0,
                    IsAutoPatrolling = relaunch,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            };
            var escort = template with
            {
                RuleId = "HUNT_FUEL_ESCORT",
                Logistics = template.Logistics! with
                {
                    Fuel = 50,
                    Speed = 6000,
                    Weapons = [],
                    Destination = new(WorldTargetKind.Craft, "HUNT_FUEL_LEADER", 2, 0.45, 0.21),
                },
            };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Soldiers = [],
                Crafts = row[1].GetBoolean() ? [leader, escort] : [escort, leader],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "ordered-fuel-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var state = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_FUEL_ESCORT").Logistics!;
            var leaderState = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_FUEL_LEADER").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                state.Fuel, leaderState.Fuel,
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(new object[]
            { row[2].GetInt32(), row[4].GetInt32(), row[5].GetInt32() });
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Assert.Equal(row[3].GetBoolean(), result.Events.OfType<CraftLowFuel>().Any(message => message.CraftTypeId == "HUNT_FUEL_ESCORT"));
            Reload(content, campaign, 81, "ordered-fuel-after.sav");
        }
    }

    [Theory]
    [InlineData(-1, 40)]
    [InlineData(100, int.MaxValue)]
    public void MalformedRefuellingForecastIsRefusedBeforeMutation(int maximum, int rate)
    {
        var content = Load(overlay: $"crafts: [{{type: INVALID_REFUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: {maximum}, refuelRate: {rate}, autoPatrol: true}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "INVALID_REFUEL_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = [],
            },
        }) with
        { Time = new(1, 1, 1, 1999, 1, 29, 55) };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft service capacity exceeds the supported range.");
    }

    [Fact]
    public void LiveHuntersSelectRevealMoveAndReload()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(10));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var alert = Assert.Single(result.Events.OfType<UfoStartedHunting>());
        Assert.Equal(9, alert.UniqueId);
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.True(ufo.Hunting);
        Assert.True(ufo.Detected);
        Assert.False(ufo.Escorting);
        Assert.Equal(new WorldPosition(1, 0.6), ufo.OriginalWaypoint);
        Assert.Equal(WorldTargetKind.Craft, ufo.Destination!.Kind);
        Assert.Equal(3300, ufo.Speed);
        Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, new(0.45, 0.21), WorldGeometry.RadianSpeed(3300)), ufo.Position);
        var restored = Reload(content, campaign, 81, "live-hunter.sav");
        AdvanceOne(restored);
        Assert.True(Assert.Single(restored.Capture().World.Ufos).Hunting);
    }

    [Theory]
    [InlineData(CampaignTimeSpeed.FiveSeconds, true, false, true)]
    [InlineData(CampaignTimeSpeed.OneMinute, true, false, true)]
    [InlineData(CampaignTimeSpeed.FiveMinutes, true, false, false)]
    [InlineData(CampaignTimeSpeed.FiveSeconds, false, false, false)]
    [InlineData(CampaignTimeSpeed.OneDay, false, true, true)]
    public void LiveRetargetingUsesSelectedSpeedAndTenMinuteCaller(CampaignTimeSpeed speed, bool fast, bool tenMinutes, bool hunts)
    {
        var content = Load(overlay: fast ? null : "hunterKillerFastRetarget: false\n");
        var initial = HuntingSnapshot(content);
        if (tenMinutes) initial = initial with { Time = new(1, 1, 1, 1999, 1, 9, 55) };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(hunts ? 10 : 3) { SelectedSpeed = speed });
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(hunts ? 1 : 3, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(hunts, Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Assert.Equal(hunts ? 1 : 0, result.Events.OfType<UfoStartedHunting>().Count());
        Reload(content, campaign, 81, "speed-selected-hunter.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscortsFollowUfoListOrderAndRestoreAfterDeletion(bool targetFirst)
    {
        var content = Load();
        var initial = HuntingSnapshot(content, escort: true);
        var hunter = initial.World.Ufos[0];
        var target = hunter with
        { UniqueId = 10, Id = 4, RuleId = "UFO_SCOUT", HunterKiller = false, Escort = false, Longitude = 0.5, Latitude = 0.25 };
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 2 }],
                Ufos = targetFirst ? [target, hunter] : [hunter, target],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var after = campaign.Capture();
        var escorted = after.World.Ufos.Single(ufo => ufo.UniqueId == 9);
        var targetPosition = targetFirst ? after.World.Ufos.Single(ufo => ufo.UniqueId == 10).Position : target.Position;
        Assert.True(escorted.Escorting);
        Assert.False(escorted.Hunting);
        Assert.Equal(10, escorted.Destination!.UniqueId);
        Assert.Equal(WorldGeometry.Move(hunter.Position, targetPosition, WorldGeometry.RadianSpeed(2200)), escorted.Position);
        var restored = Reload(content, campaign, 81, "live-escort.sav");
        var deleted = restored.Capture();
        deleted = deleted with
        {
            World = deleted.World with
            {
                Ufos =
            deleted.World.Ufos.Select(ufo => ufo.UniqueId == 10 ? ufo with { Status = UfoStatus.Destroyed } : ufo).ToArray()
            }
        };
        var cleanup = CampaignState.Restore(deleted, content, new SplitMix64RandomSource(81));
        AdvanceOne(cleanup);
        var survivor = Assert.Single(cleanup.Capture().World.Ufos);
        Assert.False(survivor.Escorting);
        Assert.Equal(new WorldPosition(1, 0.6), survivor.Destination!.Position);
        Assert.Null(survivor.OriginalWaypoint);
        Reload(content, cleanup, 81, "deleted-escort-target.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HunterInterceptionAndMarkerExhaustionAreAtomic(bool marker)
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with { Id = marker ? 0 : 3, Longitude = marker ? 0.4 : 0.45, Latitude = marker ? 0.2 : 0.21 }]
            }
        };
        if (marker) initial = initial.WithNextId("STR_UFO", int.MaxValue);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        if (marker) AssertTimeBlocked(campaign, "UFO contact marker IDs are exhausted or collide with a saved UFO.");
        else AssertInterceptionStarts(campaign, ufoAttacking: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RedundantHunterAlertsFollowTheOption(bool suppress)
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with { Options = initial.Options with { SuppressRedundantHunterAlert = suppress } };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 }
            }
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(suppress ? 0 : 1, result.Events.OfType<UfoStartedHunting>().Count());
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Reload(content, campaign, 81, "redundant-hunt-alert.sav");
    }

    [Fact]
    public void IgnoredOriginalCraftRestoresThePreparedWaypoint()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { LowFuel = true } });
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
                {
                    Hunting = true,
                    OriginalWaypoint = new(1, 0.6),
                    Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21),
                }],
            }
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.False(ufo.Hunting);
        Assert.Null(ufo.OriginalWaypoint);
        Assert.Equal(new WorldPosition(1, 0.6), ufo.Destination!.Position);
        Reload(content, campaign, 81, "ignored-hunt-target.sav");
    }

    [Fact]
    public void DestroyedFollowerCleanupIsBoundedBeforeItsNestedScan()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 1001 }],
                Ufos = Enumerable.Range(0, 1001).Select(index => initial.World.Ufos[0] with
                { UniqueId = 9 + index, Id = 3 + index, HunterKiller = false, Status = UfoStatus.Destroyed }).ToArray(),
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Destroyed UFO follower cleanup exceeds the supported work limit.");
    }

    [Fact]
    public void HunterRevelationCannotBypassAnImportedCraftInterceptionGuard()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Longitude = 0.403,
                Latitude = 0.2,
                Speed = 6000,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 },
            }
        });
        AssertInterceptionStarts(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)));
    }

    [Theory]
    [InlineData(2147000, 2000000000, 0, 100)]
    [InlineData(0, 2000, 21474837, 2147483647)]
    public void UndefinedIntermediateAttractionOverflowIsRejected(int units, int speed, int damage, int damageMaximum) =>
        Assert.False(WorldHunting.TryAttraction(0, true, units, speed, damage, damageMaximum, out _));


    [Fact]
    public void AHunterKillerReachingItsCraftOpensAnAttackWindowAndStopsTheClock()
    {
        // Branch 5 S4: GeoscapeState::time5Seconds starts a hunter-killer dogfight when the hunting UFO
        // reaches its craft; the window offers only the hunter-killer modes and cannot be minimized.
        var content = Load();
        var initial = HuntingSnapshot(content);
        var hunter = initial.World.Ufos[0] with
        {
            HunterKiller = true,
            Hunting = true,
            OriginalWaypoint = new(1, 0.6),
            Longitude = 0.45,
            Latitude = 0.21,
            Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21),
        };
        initial = initial with { World = initial.World with { Ufos = [hunter] } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var started = AssertInterceptionStarts(campaign, ufoAttacking: true);
        Assert.Equal((1, "SHIP", 1, hunter.UniqueId), (started.Interception, started.CraftTypeId, started.CraftId, started.UfoUniqueId));
        var window = Assert.Single(campaign.GetQuery<ICampaignInterceptionQuery>()!.QueryInterceptions());
        Assert.True(window.UfoAttacking);
        Assert.DoesNotContain(InterceptionMode.Standoff, window.AvailableModes);
        Assert.DoesNotContain(InterceptionMode.Standard, window.AvailableModes);
        Assert.Equal(InterceptionMode.Aggressive, window.Mode);
        Assert.NotEmpty(campaign.Execute(new MinimizeInterception(window.Number)).Events);
        AssertTimeBlocked(campaign, WorldCommandReasons.InterceptionsOpen);
    }

    [Fact]
    public void TheGeoscapeShowsTheHunterKillerAttackWindowWhichCannotBeMinimized()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        var hunter = initial.World.Ufos[0] with
        {
            HunterKiller = true,
            Hunting = true,
            OriginalWaypoint = new(1, 0.6),
            Longitude = 0.45,
            Latitude = 0.21,
            Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21),
        };
        var campaign = CampaignState.Restore(initial with { World = initial.World with { Ufos = [hunter] } }, content,
            new SplitMix64RandomSource(81));
        var client = StrategicWorldGeoscapeScenarioTests.Client(content, CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            save: static () => { }, load: static () => throw new InvalidOperationException("No save to load."));
        client.Tick(GeoscapeTimeController.GeoClock);
        while (client.Popups.Count != 0)
            client.Choose(client.Popups[0].Actions.ToList().FindIndex(static action => action.CancelKey || action.OkKey));
        var window = Assert.Single(client.Interceptions);
        Assert.True(window.UfoAttacking);
        Assert.False(client.TimeCanRun);

        // Standoff is not offered, so its button does nothing; Minimise is refused with the reason.
        void Click(int x, int y) =>
            client.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, 1, 1));
        Click(165, 36 + 5);
        Assert.Equal(InterceptionMode.Aggressive, client.Interceptions[0].Mode);
        Click(165, 36 + 5 * 16 + 6 + 5);
        Assert.False(client.Interceptions[0].Minimized);
        client.Choose(Assert.Single(client.Popups).Actions.ToList().FindIndex(static action => action.OkKey));

        // The dogfight timer steps the attack while game time waits.
        var time = campaign.Capture().Time;
        for (var step = 0; step < 20 && client.Popups.Count == 0 && client.Interceptions.Count != 0; step++)
            client.Tick(GeoscapeClient.DogfightClock);
        Assert.Equal(time, campaign.Capture().Time);
    }

    [Fact]
    public void AnArmedCraftBesideTheHuntedOneJoinsTheHunterKillersFight()
    {
        // GeoscapeState::time5Seconds with escortsJoinFightAgainstHK: crafts within escortRange that have a
        // loaded weapon and are not returning join the attack in their own windows.
        var content = Load("""
            crafts:
              - type: INTERCEPTOR
                weapons: 1
                damageMax: 100
              - type: SHIP
                damageMax: 100
            craftWeapons:
              - type: ESCORT_CANNON
                launcher: SUPPLY
                ammoMax: 50
                damage: 10
                range: 10
                accuracy: 25
                reloadCautious: 24
                reloadStandard: 16
                reloadAggressive: 8
                projectileType: 2
                projectileSpeed: 8

            """);
        var initial = HuntingSnapshot(content);
        var hunter = initial.World.Ufos[0] with
        {
            HunterKiller = true,
            Hunting = true,
            OriginalWaypoint = new(1, 0.6),
            Longitude = 0.45,
            Latitude = 0.21,
            Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21),
        };
        initial = (initial with { World = initial.World with { Ufos = [hunter] } }).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Weapons = [new CraftWeaponSnapshot("ESCORT_CANNON", 50, false, false), null] },
        }).WithCraft("INTERCEPTOR", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 300,
                Longitude = 0.4501,
                Latitude = 0.2101,
                Destination = null,
                Weapons = [new CraftWeaponSnapshot("ESCORT_CANNON", 50, false, false)],
            },
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, static evt => evt is CampaignActionBlocked);
        var started = result.Events.OfType<InterceptionStarted>().ToArray();
        // The hunter may retarget the armed interceptor first; either way the other craft joins the fight.
        Assert.Equal(["INTERCEPTOR", "SHIP"], started.Select(static evt => evt.CraftTypeId).Order());
        Assert.All(started, static evt => Assert.True(evt.UfoAttacking));
        Assert.Equal(2, campaign.GetQuery<ICampaignInterceptionQuery>()!.QueryInterceptions().Count);
    }

    [Fact]
    public void EscortsFollowALeaderThatCrashesOnLandingUntilItsSiteExpires()
    {
        // A damaged leader reaching its ground waypoint crashes (AlienMission::ufoReachedWaypoint). Its escort
        // keeps following the stationary wreck; the crash site's removal resets the escort to its own
        // waypoint (Target::~Target, Ufo::resetOriginalDestination).
        var content = Load();
        var initial = CreateLifecycleSnapshot(content);
        var landingPoint = new WorldPosition(Degrees(8), Degrees(2));
        var leader = initial.World.Ufos[0] with
        {
            TrajectoryId = "TRAJ_LANDING",
            TrajectoryPoint = 1,
            Status = UfoStatus.Flying,
            Altitude = "STR_HIGH_UC",
            Speed = 1000,
            Damage = 30,
            SecondsRemaining = 0,
            Longitude = landingPoint.Longitude,
            Latitude = landingPoint.Latitude,
            Destination = WorldTargetReference.ForWaypoint(0, landingPoint),
        };
        var own = new WorldPosition(1.0, 0.6);
        var escort = new UfoSnapshot(10, "ESCORT_HUNTER", 4, "TRAJ_LANDING", 1, landingPoint.Longitude + 0.001, landingPoint.Latitude,
            UfoStatus.Flying, "STR_HIGH_UC")
        {
            Id = 4,
            MissionWaveNumber = 0,
            Speed = 2200,
            HunterKiller = true,
            Escort = true,
            Escorting = true,
            OriginalWaypoint = own,
            Destination = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, leader.Id,
                leader.Longitude, leader.Latitude)
            { UniqueId = leader.UniqueId },
        };
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 2 }],
                Ufos = [leader, escort],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceUnblocked(campaign, 1);
        var crashed = campaign.Capture().World.Ufos;
        Assert.Equal(UfoStatus.Crashed, crashed[0].Status);
        Assert.True(crashed[1].Escorting);
        Assert.Equal(leader.UniqueId, crashed[1].Destination!.UniqueId);

        // The escort reaches the wreck and waits over it; the site expires within 96 hours.
        for (var hour = 0; hour < 100 && campaign.Capture().World.Ufos.Any(ufo => ufo.UniqueId == leader.UniqueId); hour++)
        {
            var result = campaign.Execute(new AdvanceCampaignTime(720));
            Assert.DoesNotContain(result.Events, static evt => evt is CampaignActionBlocked);
        }
        var survivor = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal(escort.UniqueId, survivor.UniqueId);
        Assert.False(survivor.Escorting);
    }
}
