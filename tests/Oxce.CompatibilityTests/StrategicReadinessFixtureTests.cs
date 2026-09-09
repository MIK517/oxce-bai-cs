using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Mods.Bootstrap;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicReadinessFixtureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BasePlacementFacilityConstructionAndSaveReloadFormACompatibilitySlice(bool fromCache)
    {
        var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var content = StrategicReadinessTestContent.Load(fromCache: fromCache);
                var original = CampaignFactory.Create(content,
                    new(new(Guid.NewGuid()), "Bases", "logistics", ["logistics"], CampaignDifficulty.Beginner),
                    new SplitMix64RandomSource(42), SystemCampaignClock.Instance);
                var funded = original.Capture() with { Funds = [20_000], Incomes = [0], Expenditures = [0] };
                var campaign = CampaignState.Restore(funded, content, new SplitMix64RandomSource(42));
                Assert.IsType<StartingBasePlaced>(Assert.Single(campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0)).Events));
                var site = campaign.QueryBaseSites(false).First(candidate => !candidate.FakeUnderwater);
                Assert.Equal(5_000, site.Cost);
                var beforeInvalidLift = campaign.Capture();
                Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
                    new CreateCampaignBase("Locked", site.Longitude, site.Latitude, "LOCKED_LIFT", 2, 2)).Events));
                Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
                    new CreateCampaignBase("Upgrade", site.Longitude, site.Latitude, "UPGRADE_LIFT", 2, 2)).Events));
                Assert.Equivalent(beforeInvalidLift, campaign.Capture(), strict: true);
                Assert.Equal("WATER_LIFT", Assert.Single(campaign.QueryAccessLifts(true),
                    lift => lift.UnavailableReason is null).RuleId);

                var unlockedCampaign = CampaignState.Restore(funded with { CompletedResearch = ["UNLOCK"] }, content,
                    new SplitMix64RandomSource(42));
                unlockedCampaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));
                var client = new CampaignLogisticsClient(new(unlockedCampaign, unlockedCampaign));
                client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, 'n', InputKeyModifiers.None));
                var underwaterBase = Assert.Single(unlockedCampaign.Capture().Bases, b => b.Id != 0);
                Assert.True(underwaterBase.FakeUnderwater);
                Assert.Equal("WATER_LIFT", Assert.Single(underwaterBase.Facilities).RuleId);

                var created = Assert.IsType<CampaignBaseCreated>(Assert.Single(campaign.Execute(
                    new CreateCampaignBase("Beta", site.Longitude, site.Latitude, "LIFT", 2, 2)).Events));
                Assert.Equal(5_000, created.Cost);
                Assert.IsType<CampaignFacilityChanged>(Assert.Single(campaign.Execute(
                    new BuildCampaignFacility(created.BaseId, "ROOM", 3, 2)).Events));
                var constructing = campaign.Capture();
                Assert.Equal(2, constructing.Bases.Single(b => b.Id == created.BaseId).Facilities.Single(f => f.RuleId == "ROOM").BuildTime);
                var reload = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(constructing), "bases.sav", content,
                    new SplitMix64RandomSource(42), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
                var midnight = constructing.Time with { Hour = 23, Minute = 59, Second = 55 };
                campaign = CampaignState.Restore(reload.Campaign.Capture() with { Time = midnight }, content, new SplitMix64RandomSource(42));
                campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Equal(1, campaign.Capture().Bases.Single(b => b.Id == created.BaseId).Facilities.Single(f => f.RuleId == "ROOM").BuildTime);

                var facilityState = campaign.Capture();
                var facilityBase = facilityState.Bases.Single(b => b.Id == created.BaseId);
                var upgradeBase = facilityBase with
                {
                    Facilities =
                    [
                        new("LIFT", 2, 2, 0, 0, false, false, false) { PreservationKey = "lift" },
                        new("ROOM", 3, 2, 0, 0, false, false, false) { PreservationKey = "room-left" },
                        new("ROOM", 4, 2, 0, 0, false, false, false) { PreservationKey = "room-right" },
                    ]
                };
                var upgradeCampaign = CampaignState.Restore(facilityState with
                {
                    Funds = [0],
                    Incomes = [0],
                    Expenditures = [0],
                    Bases = facilityState.Bases.Select(b => b.Id == upgradeBase.Id ? upgradeBase : b).ToArray(),
                }, content, new SplitMix64RandomSource(42));
                using var readinessOracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
                    "fixtures/expected/savegames/strategic-readiness.expected.json")));
                Assert.Equal(0, readinessOracle.RootElement.GetProperty("facilityAffordability")[0][4].GetInt32());
                var beforeUpgrade = upgradeCampaign.Capture();
                Assert.IsType<CampaignActionBlocked>(Assert.Single(upgradeCampaign.Execute(
                    new BuildCampaignFacility(upgradeBase.Id, "UPGRADE", 3, 2)).Events));
                Assert.Equivalent(beforeUpgrade, upgradeCampaign.Capture(), strict: true);

                var queueBase = facilityBase with
                {
                    Facilities =
                    [
                        new("LIFT", 2, 2, 0, 0, false, false, false) { PreservationKey = "lift" },
                        new("ROOM", 3, 2, 1, 0, false, false, false) { PreservationKey = "queue-source" },
                        new("ROOM", 4, 2, int.MaxValue, 0, false, false, false) { PreservationKey = "queue-middle" },
                        new("ROOM", 5, 2, int.MaxValue, 0, false, false, false) { PreservationKey = "queue-tail" },
                    ]
                };
                var queueSnapshot = facilityState with
                {
                    Options = facilityState.Options with { AllowBuildingQueue = true },
                    Bases = facilityState.Bases.Select(b => b.Id == queueBase.Id ? queueBase : b).ToArray(),
                };
                var queueYaml = OxceSaveAdapter.EmitNewCampaign(queueSnapshot).Replace(
                    "oxcePortEntityKey: queue-middle", "futureFacility: retained\n        oxcePortEntityKey: queue-middle", StringComparison.Ordinal);
                Assert.Contains("futureFacility: retained", queueYaml, StringComparison.Ordinal);
                var queueLoaded = OxceSaveAdapter.Load(queueYaml, "queue.sav", content, new SplitMix64RandomSource(42),
                    new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
                Assert.IsType<CampaignFacilityChanged>(Assert.Single(queueLoaded.Campaign.Execute(
                    new DismantleCampaignFacility(queueBase.Id, 5, 2)).Events));
                var recalculated = queueLoaded.Campaign.Capture().Bases.Single(b => b.Id == queueBase.Id).Facilities.Single(f => f.X == 4);
                Assert.Equal("queue-middle", recalculated.PreservationKey);
                Assert.Equal(3, recalculated.BuildTime);
                Assert.Contains("futureFacility: retained", OxceSaveAdapter.EmitLoadedCampaign(queueLoaded.Campaign.Capture(), queueLoaded.Source),
                    StringComparison.Ordinal);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoldierTrainingEligibilityFormsAnIndependentCompatibilitySlice(bool fromCache)
    {
                var (content, _, facilityState, facilityBase, _) = CreateConstructedBaseScenario(fromCache);

                var recruit = content.RuntimeRules.Soldiers[content.RuntimeRules.Soldiers.GetRequired("RECRUIT")].Value;
                var stats = recruit.MinimumStats.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                var undertrainedStats = recruit.TrainingStatCaps.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                foreach (var key in new[] { "firing", "health", "melee", "throwing", "strength", "tu", "stamina" })
                    undertrainedStats[key] = unchecked((short)(undertrainedStats.GetValueOrDefault(key) - 1));
                var trainee = new SoldierPersonalState("Trainee", "", 0, 0, 0, 0, "ARMOR", undertrainedStats, undertrainedStats);
                var trainingBase = facilityBase with
                {
                    Facilities =
                    [
                        new("LIFT", 2, 2, 0, 0, false, false, false),
                        new("ROOM", 3, 2, 0, 0, false, false, false),
                    ],
                    Soldiers =
                    [
                        new("RECRUIT", 1) { Personal = trainee with { Training = true } },
                        new("RECRUIT", 2) { Personal = trainee with { Training = true } },
                        new("RECRUIT", 3) { Personal = trainee with { Recovery = 1 } },
                    ]
                };
                var trainingCampaign = CampaignState.Restore(facilityState with
                {
                    Bases = facilityState.Bases.Select(b => b.Id == trainingBase.Id ? trainingBase : b).ToArray()
                }, content, new SplitMix64RandomSource(42));
                Assert.IsType<CampaignPersonnelChanged>(Assert.Single(trainingCampaign.Execute(
                    new SetSoldierTraining(trainingBase.Id, 3, true, false)).Events));
                var queuedTrainee = trainingCampaign.Capture().Bases.Single(b => b.Id == trainingBase.Id).Soldiers.Single(s => s.Id == 3).Personal!;
                Assert.False(queuedTrainee.Training);
                Assert.True(queuedTrainee.ReturnToTrainingWhenHealed);

                var fullyTrained = trainee with { CurrentStats = recruit.TrainingStatCaps };
                var completedState = trainingCampaign.Capture();
                var completedBase = completedState.Bases.Single(b => b.Id == trainingBase.Id) with
                { Soldiers = [new("RECRUIT", 4) { Personal = fullyTrained }] };
                trainingCampaign = CampaignState.Restore(completedState with
                { Bases = completedState.Bases.Select(b => b.Id == completedBase.Id ? completedBase : b).ToArray() },
                    content, new SplitMix64RandomSource(42));
                var beforeTraining = trainingCampaign.Capture();
                Assert.IsType<CampaignActionBlocked>(Assert.Single(trainingCampaign.Execute(
                    new SetSoldierTraining(completedBase.Id, 4, true, false)).Events));
                Assert.Equivalent(beforeTraining, trainingCampaign.Capture(), strict: true);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftCapacityRulesFormAnIndependentCompatibilitySlice(bool fromCache)
    {
                var (content, _, facilityState, facilityBase, _) = CreateConstructedBaseScenario(fromCache);
                var recruit = content.RuntimeRules.Soldiers[content.RuntimeRules.Soldiers.GetRequired("RECRUIT")].Value;
                var undertrainedStats = recruit.TrainingStatCaps.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                foreach (var key in new[] { "firing", "health", "melee", "throwing", "strength", "tu", "stamina" })
                    undertrainedStats[key] = unchecked((short)(undertrainedStats.GetValueOrDefault(key) - 1));
                var trainee = new SoldierPersonalState("Trainee", "", 0, 0, 0, 0, "ARMOR", undertrainedStats, undertrainedStats);

                var capacityRule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("CAPACITY_SHIP")].Value;
                var noBonusCraft = CraftLogistics.Purchase(capacityRule, content.RuntimeRules, 0, 0);
                var noBonusBase = facilityBase with
                {
                    Soldiers = [new("RECRUIT", 5) { Personal = trainee with
                        { Armor = "ARMOR", CraftType = "CAPACITY_SHIP", CraftId = 9 } }],
                    Crafts = [new("CAPACITY_SHIP", 9) { Logistics = noBonusCraft }],
                    Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUPPLY"] = 1 }
                };
                var noBonusCampaign = CampaignState.Restore(facilityState with
                { Bases = facilityState.Bases.Select(b => b.Id == noBonusBase.Id ? noBonusBase : b).ToArray() },
                    content, new SplitMix64RandomSource(42));
                var beforeNoBonusArmor = noBonusCampaign.Capture();
                Assert.IsType<CampaignActionBlocked>(Assert.Single(noBonusCampaign.Execute(
                    new EquipSoldierArmor(noBonusBase.Id, 5, "LARGE_ARMOR")).Events));
                Assert.Equivalent(beforeNoBonusArmor, noBonusCampaign.Capture(), strict: true);

                var capacityCraft = CraftLogistics.Purchase(capacityRule, content.RuntimeRules, 0, 0) with
                { Weapons = [new("CAPACITY_WEAPON", 0)] };
                var capacitySoldier = new SoldierSnapshot("RECRUIT", 5)
                { Personal = trainee with { Armor = "ARMOR", CraftType = "CAPACITY_SHIP", CraftId = 9 } };
                var capacityBase = facilityBase with
                {
                    Soldiers = [capacitySoldier],
                    Crafts = [new("CAPACITY_SHIP", 9) { Logistics = capacityCraft }],
                    Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUPPLY"] = 1 }
                };
                var capacityCampaign = CampaignState.Restore(facilityState with
                {
                    Options = facilityState.Options with { StorageLimitsEnforced = true },
                    Bases = facilityState.Bases.Select(b => b.Id == capacityBase.Id ? capacityBase : b).ToArray()
                },
                    content, new SplitMix64RandomSource(42));
                Assert.IsType<CampaignPersonnelChanged>(Assert.Single(capacityCampaign.Execute(
                    new EquipSoldierArmor(capacityBase.Id, 5, "LARGE_ARMOR")).Events));
                var beforeWeaponRemoval = capacityCampaign.Capture();
                Assert.IsType<CampaignActionBlocked>(Assert.Single(capacityCampaign.Execute(
                    new EquipCraftWeapon(capacityBase.Id, "CAPACITY_SHIP", 9, 0, "")).Events));
                Assert.Equivalent(beforeWeaponRemoval, capacityCampaign.Capture(), strict: true);

                var vehicleShipRule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value;
                var overflowWeaponState = facilityState;
                var overflowBaseState = overflowWeaponState.Bases.Single(b => b.Id == facilityBase.Id) with
                {
                    Crafts = [new("SHIP", 12) { Logistics = CraftLogistics.Purchase(vehicleShipRule, content.RuntimeRules, 0, 0) }],
                    Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUPPLY"] = 1 }
                };
                var overflowWeaponCampaign = CampaignState.Restore(overflowWeaponState with
                { Bases = overflowWeaponState.Bases.Select(b => b.Id == overflowBaseState.Id ? overflowBaseState : b).ToArray() },
                    content, new SplitMix64RandomSource(42));
                var beforeOverflowWeapon = overflowWeaponCampaign.Capture();
                Assert.IsType<CampaignActionBlocked>(Assert.Single(overflowWeaponCampaign.Execute(
                    new EquipCraftWeapon(overflowBaseState.Id, "SHIP", 12, 1, "OVERFLOW_WEAPON")).Events));
                Assert.Equivalent(beforeOverflowWeapon, overflowWeaponCampaign.Capture(), strict: true);
                var saleQuote = Assert.IsType<LogisticsQuoted>(Assert.Single(capacityCampaign.Execute(
                    new PrepareLogisticsQuote(capacityBase.Id, LogisticsOperation.Sell)).Events)).Quote;
                var launcher = saleQuote.Rows.Single(row => row.RuleId == "SUPPLY");
                Assert.Equal(2, launcher.Owned);
                Assert.IsType<CampaignActionBlocked>(Assert.Single(capacityCampaign.Execute(
                    new SubmitLogisticsOrder(saleQuote.Id, [new(launcher.Id, 2)])).Events));
                Assert.Equivalent(beforeWeaponRemoval, capacityCampaign.Capture(), strict: true);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftVehicleManagementFormsAnIndependentCompatibilitySlice(bool fromCache)
    {
                var (content, _, facilityState, facilityBase, _) = CreateConstructedBaseScenario(fromCache);
                var vehicleShipRule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value;

                var vehicleCraft = CraftLogistics.Purchase(vehicleShipRule, content.RuntimeRules, 0, 0);
                var vehicleBase = facilityBase with
                {
                    Crafts = [new("SHIP", 10) { Logistics = vehicleCraft }],
                    Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["VEHICLE"] = 1 }
                };
                var vehicleCampaign = CampaignState.Restore(facilityState with
                { Bases = facilityState.Bases.Select(b => b.Id == vehicleBase.Id ? vehicleBase : b).ToArray() },
                    content, new SplitMix64RandomSource(42));
                Assert.IsType<CampaignPersonnelChanged>(Assert.Single(vehicleCampaign.Execute(
                    new ChangeCraftVehicle(vehicleBase.Id, "SHIP", 10, "VEHICLE", true)).Events));
                var vehicleLoaded = vehicleCampaign.Capture().Bases.Single(b => b.Id == vehicleBase.Id);
                Assert.False(vehicleLoaded.Items.ContainsKey("VEHICLE"));
                Assert.Single(vehicleLoaded.Crafts[0].Logistics!.Vehicles);
                Assert.IsType<CampaignPersonnelChanged>(Assert.Single(vehicleCampaign.Execute(
                    new ChangeCraftVehicle(vehicleBase.Id, "SHIP", 10, "VEHICLE", false)).Events));
                var vehicleRemoved = vehicleCampaign.Capture().Bases.Single(b => b.Id == vehicleBase.Id);
                Assert.Equal(1, vehicleRemoved.Items["VEHICLE"]);
                Assert.Empty(vehicleRemoved.Crafts[0].Logistics!.Vehicles);

                var identityState = facilityState;
                var identityPrefix = $"{identityState.Identity.Id}:vehicle:SHIP:11:";
                var identityCraft = CraftLogistics.Purchase(vehicleShipRule, content.RuntimeRules, 0, 0) with
                {
                    Vehicles =
                    [
                        new("VEHICLE", 0) { Size = 1, SpaceOccupied = 0, PreservationKey = identityPrefix + "0" },
                        new("VEHICLE", 0) { Size = 1, SpaceOccupied = 0, PreservationKey = identityPrefix + "1" },
                        new("VEHICLE_B", 0) { Size = 1, SpaceOccupied = 0, PreservationKey = identityPrefix + "2" },
                        new("VEHICLE_B", 0) { Size = 1, SpaceOccupied = 0, PreservationKey = identityPrefix + "3" },
                    ]
                };
                var identityBase = identityState.Bases.Single(b => b.Id == facilityBase.Id) with
                {
                    Crafts = [new("SHIP", 11) { Logistics = identityCraft }],
                    Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["VEHICLE_B"] = 1 }
                };
                var identityCampaign = CampaignState.Restore(identityState with
                { Bases = identityState.Bases.Select(b => b.Id == identityBase.Id ? identityBase : b).ToArray() },
                    content, new SplitMix64RandomSource(42));
                Assert.IsType<CampaignPersonnelChanged>(Assert.Single(identityCampaign.Execute(
                    new ChangeCraftVehicle(identityBase.Id, "SHIP", 11, "VEHICLE", false)).Events));
                Assert.IsType<CampaignPersonnelChanged>(Assert.Single(identityCampaign.Execute(
                    new ChangeCraftVehicle(identityBase.Id, "SHIP", 11, "VEHICLE_B", true)).Events));
                var vehicleKeys = identityCampaign.Capture().Bases.Single(b => b.Id == identityBase.Id).Crafts[0].Logistics!.Vehicles
                    .Select(vehicle => vehicle.PreservationKey).ToArray();
                Assert.Equal(vehicleKeys.Length, vehicleKeys.Distinct(StringComparer.Ordinal).Count());
                var identityYaml = OxceSaveAdapter.EmitNewCampaign(identityCampaign.Capture());
                var identityLoaded = OxceSaveAdapter.Load(identityYaml, "vehicle-identities.sav", content,
                    new SplitMix64RandomSource(42), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
                _ = OxceSaveAdapter.EmitLoadedCampaign(identityLoaded.Campaign.Capture(), identityLoaded.Source);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SoldierTransformationsFormAnIndependentCompatibilitySlice(bool fromCache)
    {
                var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
                var (content, campaign, facilityState, _, created) = CreateConstructedBaseScenario(fromCache);
                using var readinessOracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
                    "fixtures/expected/savegames/strategic-readiness.expected.json")));
                var recruit = content.RuntimeRules.Soldiers[content.RuntimeRules.Soldiers.GetRequired("RECRUIT")].Value;
                var stats = recruit.MinimumStats.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

                var soldier = new SoldierSnapshot("RECRUIT", 77)
                {
                    PreservationKey = "fixture:soldier:77",
                    Personal = new("Transform Me", "", 0, 0, 0, 0, "ARMOR", stats, stats),
                };
                var state = campaign.Capture();
                var shipRule = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value;
                var beta = state.Bases.Single(b => b.Id == created.BaseId) with
                {
                    Soldiers = [soldier],
                    Crafts = [new CraftSnapshot("SHIP", 1) { Logistics = CraftLogistics.Purchase(shipRule, content.RuntimeRules, 0, 0) }],
                    Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUPPLY"] = 2 }
                };
                campaign = CampaignState.Restore(state with { Bases = state.Bases.Select(b => b.Id == beta.Id ? beta : b).ToArray() },
                    content, new SplitMix64RandomSource(42));
                Assert.IsType<CampaignPersonnelChanged>(Assert.Single(campaign.Execute(
                    new AssignSoldierToCraft(beta.Id, 77, "SHIP", 1)).Events));
                Assert.IsType<CampaignPersonnelChanged>(Assert.Single(campaign.Execute(
                    new EquipSoldierArmor(beta.Id, 77, "LARGE_ARMOR")).Events));
                Assert.Equal("SHIP", campaign.Capture().Bases.Single(b => b.Id == beta.Id).Soldiers.Single().Personal!.CraftType);
                Assert.IsType<CampaignPersonnelChanged>(Assert.Single(campaign.Execute(
                    new EquipSoldierArmor(beta.Id, 77, "ARMOR")).Events));

                var softState = campaign.Capture();
                var softBase = softState.Bases.Single(b => b.Id == beta.Id);
                var softSoldier = softBase.Soldiers.Single();
                var softStats = softSoldier.Personal!.CurrentStats.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                softStats["tu"] = checked((short)(recruit.MaximumStats["tu"] + 5));
                var softPersonal = softSoldier.Personal with { CurrentStats = softStats };
                campaign = CampaignState.Restore(softState with
                {
                    Bases = softState.Bases.Select(b => b.Id == beta.Id ? b with
                    { Soldiers = [softSoldier with { Personal = softPersonal }] } : b).ToArray()
                }, content, new SplitMix64RandomSource(42));
                Assert.IsType<CampaignSoldierTransformed>(Assert.Single(campaign.Execute(
                    new TransformCampaignSoldier(beta.Id, 77, "SOFT_LIMIT_TRANSFORMATION")).Events));
                Assert.Equal(softStats["tu"] - 1,
                    campaign.Capture().Bases.Single(b => b.Id == beta.Id).Soldiers.Single().Personal!.CurrentStats["tu"]);

                var postSoftState = campaign.Capture();
                var rerollState = postSoftState;
                var rerollBase = rerollState.Bases.Single(b => b.Id == beta.Id);
                var rerollSoldier = rerollBase.Soldiers.Single();
                var rerollStats = rerollSoldier.Personal!.CurrentStats.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                rerollStats["tu"] = 99;
                campaign = CampaignState.Restore(rerollState with
                {
                    Bases = rerollState.Bases.Select(b => b.Id == beta.Id ? b with
                    {
                        Soldiers = [rerollSoldier with { Personal = rerollSoldier.Personal with { CurrentStats = rerollStats } }]
                    } : b).ToArray()
                }, content, new SplitMix64RandomSource(42));
                Assert.IsType<CampaignSoldierTransformed>(Assert.Single(campaign.Execute(
                    new TransformCampaignSoldier(beta.Id, 77, "REROLL_TRANSFORMATION")).Events));
                Assert.Equal(checked((short)readinessOracle.RootElement.GetProperty("transformationCombine")[1][3].GetInt32()),
                    campaign.Capture().Bases.Single(b => b.Id == beta.Id).Soldiers.Single().Personal!.CurrentStats["tu"]);

                var overflowState = campaign.Capture();
                var overflowBase = overflowState.Bases.Single(b => b.Id == beta.Id);
                var overflowSoldier = overflowBase.Soldiers.Single();
                campaign = CampaignState.Restore(overflowState with
                {
                    Bases = overflowState.Bases.Select(b => b.Id == beta.Id ? b with
                    {
                        Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUPPLY"] = int.MaxValue },
                        Soldiers = [overflowSoldier with { Personal = overflowSoldier.Personal! with { Armor = "LARGE_ARMOR" } }]
                    } : b).ToArray()
                }, content, new SplitMix64RandomSource(42));
                var beforeOverflow = campaign.Capture();
                Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
                    new TransformCampaignSoldier(beta.Id, 77, "ARMOR_RETURN_TRANSFORMATION")).Events));
                Assert.Equivalent(beforeOverflow, campaign.Capture(), strict: true);

                var combinationBase = postSoftState.Bases.Single(b => b.Id == beta.Id);
                var combinationSoldier = combinationBase.Soldiers.Single();
                var combinationPersonal = combinationSoldier.Personal! with
                {
                    Rank = 3,
                    CraftType = "SHIP",
                    CraftId = 1,
                    PreviousTransformations = new Dictionary<string, int>(StringComparer.Ordinal) { ["LEGACY"] = 2 },
                    TransformationBonuses = new Dictionary<string, int>(StringComparer.Ordinal) { ["LEGACY_BONUS"] = 1 }
                };
                var combinationState = postSoftState with
                {
                    Bases = postSoftState.Bases.Select(b => b.Id == beta.Id ? b with
                    { Soldiers = [combinationSoldier with { Personal = combinationPersonal }] } : b).ToArray()
                };
                var cloneCampaign = CampaignState.Restore(combinationState, content, new SplitMix64RandomSource(42));
                var cloned = Assert.IsType<CampaignSoldierTransformed>(Assert.Single(cloneCampaign.Execute(
                    new TransformCampaignSoldier(beta.Id, 77, "CLONE_RESET_TRANSFORMATION")).Events));
                Assert.Equal(24, cloned.TransferHours);
                var cloneBase = cloneCampaign.Capture().Bases.Single(b => b.Id == beta.Id);
                var cloneSource = Assert.Single(cloneBase.Soldiers).Personal!;
                Assert.Equal(2, cloneSource.PreviousTransformations["LEGACY"]);
                Assert.Equal(1, cloneSource.PreviousTransformations["CLONE_RESET_TRANSFORMATION"]);
                Assert.Equal("", cloneSource.CraftType);
                Assert.Equal(0, cloneSource.CraftId);
                var clone = Assert.Single(cloneBase.Transfers).Soldier!.Personal!;
                Assert.Equal(0, clone.Rank);
                Assert.Empty(clone.PreviousTransformations);
                Assert.Empty(clone.TransformationBonuses);
                Assert.Equal(clone.InitialStats, clone.CurrentStats);

                var resetCampaign = CampaignState.Restore(combinationState, content, new SplitMix64RandomSource(42));
                Assert.IsType<CampaignSoldierTransformed>(Assert.Single(resetCampaign.Execute(
                    new TransformCampaignSoldier(beta.Id, 77, "RESET_TRANSFORMATION")).Events));
                var resetPersonal = resetCampaign.Capture().Bases.Single(b => b.Id == beta.Id).Soldiers.Single().Personal!;
                Assert.Equal(1, Assert.Single(resetPersonal.PreviousTransformations).Value);
                Assert.True(resetPersonal.PreviousTransformations.ContainsKey("RESET_TRANSFORMATION"));
                Assert.Empty(resetPersonal.TransformationBonuses);

                var immediateCampaign = CampaignState.Restore(postSoftState with
                {
                    Bases = postSoftState.Bases.Select(b => b.Id == beta.Id ? b with
                    {
                        Soldiers = [b.Soldiers.Single() with { Personal = b.Soldiers.Single().Personal! with
                            { Training = true, PsiTraining = true } }]
                    } : b).ToArray()
                }, content, new SplitMix64RandomSource(42));
                Assert.IsType<CampaignSoldierTransformed>(Assert.Single(immediateCampaign.Execute(
                    new TransformCampaignSoldier(beta.Id, 77, "IMMEDIATE_TYPE_TRANSFORMATION")).Events));
                var immediate = immediateCampaign.Capture().Bases.Single(b => b.Id == beta.Id).Soldiers.Single();
                Assert.Equal("NO_PSI", immediate.RuleId);
                Assert.False(immediate.Personal!.Training);
                Assert.True(immediate.Personal.ReturnToTrainingWhenHealed);
                Assert.False(immediate.Personal.PsiTraining);
                campaign = CampaignState.Restore(postSoftState, content, new SplitMix64RandomSource(42));

                var transformed = Assert.IsType<CampaignSoldierTransformed>(Assert.Single(campaign.Execute(
                    new TransformCampaignSoldier(beta.Id, 77, "READINESS_TRANSFORMATION")).Events));
                Assert.Equal(2, transformed.TransferHours);
                var pending = campaign.Capture().Bases.Single(b => b.Id == beta.Id);
                Assert.Empty(pending.Soldiers);
                Assert.Equal(1, pending.Items["SUPPLY"]);
                var transformedPersonal = Assert.Single(pending.Transfers).Soldier!.Personal!;
                Assert.Equal(softStats["tu"], transformedPersonal.CurrentStats["tu"]);
                Assert.Equal(1, transformedPersonal.PreviousTransformations["READINESS_TRANSFORMATION"]);
                campaign.Execute(new AdvanceCampaignTime(1_440));
                Assert.Equal(77, Assert.Single(campaign.Capture().Bases.Single(b => b.Id == beta.Id).Soldiers).Id);
    }

    private static (RuntimeContent Content, CampaignState Campaign, CampaignSnapshot State,
        BaseSnapshot Base, CampaignBaseCreated Created) CreateConstructedBaseScenario(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(fromCache: fromCache);
        var original = CampaignFactory.Create(content,
            new(new(Guid.NewGuid()), "Bases", "logistics", ["logistics"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(42), SystemCampaignClock.Instance);
        var funded = original.Capture() with { Funds = [20_000], Incomes = [0], Expenditures = [0] };
        var campaign = CampaignState.Restore(funded, content, new SplitMix64RandomSource(42));
        _ = campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));
        var site = campaign.QueryBaseSites(false).First(candidate => !candidate.FakeUnderwater);
        var created = Assert.IsType<CampaignBaseCreated>(Assert.Single(campaign.Execute(
            new CreateCampaignBase("Beta", site.Longitude, site.Latitude, "LIFT", 2, 2)).Events));
        _ = campaign.Execute(new BuildCampaignFacility(created.BaseId, "ROOM", 3, 2));
        var constructing = campaign.Capture();
        var reload = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(constructing), "bases.sav", content,
            new SplitMix64RandomSource(42), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        var midnight = constructing.Time with { Hour = 23, Minute = 59, Second = 55 };
        campaign = CampaignState.Restore(reload.Campaign.Capture() with { Time = midnight }, content,
            new SplitMix64RandomSource(42));
        _ = campaign.Execute(new AdvanceCampaignTime(1));
        var state = campaign.Capture();
        return (content, campaign, state, state.Bases.Single(b => b.Id == created.BaseId), created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshAndCachedServicingPreserveShieldAndFacilityAmmunitionThroughSaveOverlays(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load("strategic-servicing.rul", fromCache);
                var campaign = CampaignFactory.Create(content, new(new(Guid.NewGuid()), "Service", "logistics", ["logistics"], CampaignDifficulty.Beginner),
                    new SplitMix64RandomSource(42), SystemCampaignClock.Instance);
                Assert.Equal(2, campaign.QueryReadiness(0).Crafts[0].Shield);
                Assert.Equal(15, campaign.QueryReadiness(0).Crafts[0].ShieldMaximum);
                campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0));
                Assert.Equal(3, campaign.Capture().Bases[0].Items["SERVICE_AMMO"]);
                campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Equal(1, campaign.QueryReadiness(0).Crafts[0].Damage);
                Assert.Equal(6, campaign.QueryReadiness(0).Crafts[0].Shield);
                Assert.Equal(2, campaign.QueryReadiness(0).Defenses[0].Ammo);
                Assert.True(campaign.QueryReadiness(0).Defenses[0].Disabled); // Reference rearm does not test disabled.
                Assert.Equal(1, campaign.Capture().Bases[0].Items["SERVICE_AMMO"]);
                var snapshot = campaign.Capture();
                var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(snapshot), "readiness.sav", content,
                    new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
                Assert.Equivalent(snapshot, loaded.Campaign.Capture(), strict: true);
                campaign = loaded.Campaign;
                var shortage = campaign.Execute(new AdvanceCampaignTime(720));
                Assert.Single(shortage.Events.OfType<FacilityServiceMessage>());
                Assert.Equal(3, campaign.QueryReadiness(0).Defenses[0].Ammo);
                Assert.Equal(10, campaign.QueryReadiness(0).Crafts[0].Shield);
                Assert.Equal("STR_REARMING", campaign.QueryReadiness(0).Crafts[0].Status);
                var next = campaign.Execute(new AdvanceCampaignTime(720));
                Assert.Empty(next.Events.OfType<FacilityServiceMessage>()); // One shortage notification, persisted.
                Assert.Equal(10, campaign.QueryReadiness(0).Crafts[0].Weapons[0]!.Ammo);
                campaign.Execute(new AdvanceCampaignTime(720));
                var ready = campaign.QueryReadiness(0).Crafts[0];
                Assert.Equal("STR_READY", ready.Status);
                Assert.Equal(100, ready.Fuel);
                Assert.Equal(15, ready.Shield);
                var final = campaign.Capture();
                var reload = OxceSaveAdapter.Load(OxceSaveAdapter.EmitLoadedCampaign(final, loaded.Source), "readiness.sav", content,
                    new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
                Assert.Equivalent(final, reload.Campaign.Capture(), strict: true);
    }

    [Fact]
    public void RearmingMatchesExtractedCppIncludingStatisticalBulletSaving()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Oxce.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "fixtures/expected/savegames/strategic-readiness.expected.json")));
        Assert.Equal("4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15", expected.RootElement.GetProperty("referenceCommit").GetString());
        foreach (var row in expected.RootElement.GetProperty("weapons").EnumerateArray())
        {
            var rule = new RuntimeCraftWeaponRule(10, row[0].GetInt32(), "", "", new Dictionary<string, int>())
            { StatisticalBulletSaving = true };
            var result = CraftServicing.RearmWeapon(new("fixture", row[1].GetInt32(), true), rule,
                row[3].GetInt32(), row[2].GetInt32(), new ChoiceRandom(row[4].GetInt32()));
            Assert.Equal(row[5].GetInt32(), result.ClipsUsed);
            Assert.Equal(row[6].GetInt32(), result.Weapon.Ammo);
            Assert.Equal(row[7].GetInt32() != 0, result.Weapon.Rearming);
        }
    }

    [Fact]
    public void RecoveryMatchesExtractedCppOrderingAndClamps()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Oxce.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "fixtures/expected/savegames/strategic-readiness.expected.json")));
        foreach (var row in expected.RootElement.GetProperty("recovery").EnumerateArray())
        {
            var stats = new Dictionary<string, short>(StringComparer.Ordinal) { ["health"] = 40, ["mana"] = 30 };
            var soldier = new SoldierPersonalState("Fixture", "", 0, 0, 0, 0, "", stats, stats)
            { Recovery = row[0].GetSingle(), ManaMissing = 10, HealthMissing = 12 };
            var actual = SoldierReadiness.Recover(soldier, new(row[1].GetInt32(), 4, 0.5f, 2.5f));
            Assert.Equal(row[2].GetSingle(), actual.Recovery, 3);
            Assert.Equal(row[3].GetInt32(), actual.ManaMissing);
            Assert.Equal(row[4].GetInt32(), actual.HealthMissing);
        }
    }

    [Fact]
    public void PhysicalTrainingCompletionMatchesExtractedCppStatComparison()
    {
        var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository,
            "fixtures/expected/savegames/strategic-readiness.expected.json")));
        var discovery = Oxce.Mods.Discovery.ModDiscovery.ScanDirectory(Path.Combine(repository,
            "fixtures/public/mods/strategic-logistics"));
        var plan = Oxce.Mods.Loading.ModLoadPlanner.Create(Oxce.Mods.Loading.ModCatalog.Create(discovery.Mods),
            [new("logistics", true)], "logistics", new("Extended", "8.6.1.0"));
        var content = Oxce.Mods.Rulesets.Content.ContentSnapshotBuilder.Build(plan).Content;
        var rule = content.RuntimeRules.Soldiers[content.RuntimeRules.Soldiers.GetRequired("RECRUIT")].Value;
        foreach (var row in expected.RootElement.GetProperty("training").EnumerateArray())
        {
            var stats = rule.TrainingStatCaps.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            stats["firing"] = checked((short)(stats.GetValueOrDefault("firing") + row[0].GetInt32()));
            var soldier = new SoldierPersonalState("Oracle", "", 0, 0, 0, 0, "ARMOR", stats, stats);
            Assert.Equal(row[1].GetInt32() != 0, SoldierReadiness.IsFullyTrained(soldier, rule));
        }
    }

    private sealed class ChoiceRandom(int choice) : IRandomSource
    {
        public int NextInclusive(int minimum, int maximum) { Assert.InRange(choice, minimum, maximum); return choice; }
        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException();
        public double NextUnit() => throw new InvalidOperationException();
    }

}
