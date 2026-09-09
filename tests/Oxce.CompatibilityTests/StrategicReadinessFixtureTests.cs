using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Mods.Bootstrap;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicReadinessFixtureTests
{
    [Fact]
    public void BasePlacementFacilityConstructionAndSaveReloadFormACompleteSlice()
    {
        var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"oxce-bases-{Guid.NewGuid():N}");
        try
        {
            CopyFixture(Path.Combine(repository, "fixtures/public/mods/strategic-logistics"), Path.Combine(root, "standard"));
            Directory.CreateDirectory(Path.Combine(root, "user/mods"));
            File.Copy(Path.Combine(repository, "fixtures/public/savegames/strategic-bases.rul"),
                Path.Combine(root, "standard/logistics/Ruleset/aa-bases.rul"));
            var request = InstallationLoadRequest.ForMasterAndAddOn(root, "logistics", "-", new("Extended", "8.6.1.0"));
            foreach (var loadedContent in new[]
            {
                InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken),
                InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken),
            })
            {
                Assert.True(loadedContent.IsSuccess, loadedContent.DescribeFailure());
                var content = loadedContent.Content!;
                var original = CampaignFactory.Create(content,
                    new(new(Guid.NewGuid()), "Bases", "logistics", ["logistics"], CampaignDifficulty.Beginner),
                    new SplitMix64RandomSource(42), SystemCampaignClock.Instance);
                var funded = original.Capture() with { Funds = [20_000], Incomes = [0], Expenditures = [0] };
                var campaign = CampaignState.Restore(funded, content, new SplitMix64RandomSource(42));
                Assert.IsType<StartingBasePlaced>(Assert.Single(campaign.Execute(new PlaceStartingBase(0, "Alpha", 0, 0)).Events));
                var site = campaign.QueryBaseSites(false)[0];
                Assert.Equal(5_000, site.Cost);
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
                Assert.IsType<CampaignFacilityChanged>(Assert.Single(upgradeCampaign.Execute(
                    new BuildCampaignFacility(upgradeBase.Id, "UPGRADE", 3, 2)).Events));
                Assert.Equal(10, upgradeCampaign.Capture().Funds[^1]);

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
                Assert.Equal(50, campaign.Capture().Bases.Single(b => b.Id == beta.Id).Soldiers.Single().Personal!.CurrentStats["tu"]);

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
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FreshAndCachedServicingPreserveShieldAndFacilityAmmunitionThroughSaveOverlays()
    {
        var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"oxce-readiness-{Guid.NewGuid():N}");
        try
        {
            CopyFixture(Path.Combine(repository, "fixtures/public/mods/strategic-logistics"), Path.Combine(root, "standard"));
            Directory.CreateDirectory(Path.Combine(root, "user/mods"));
            File.Copy(Path.Combine(repository, "fixtures/public/savegames/strategic-servicing.rul"),
                Path.Combine(root, "standard/logistics/Ruleset/aa-servicing.rul"));
            var request = InstallationLoadRequest.ForMasterAndAddOn(root, "logistics", "-", new("Extended", "8.6.1.0"));
            var fresh = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            var cached = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(fresh.IsSuccess, fresh.DescribeFailure());
            Assert.True(cached.IsSuccess, cached.DescribeFailure());
            Assert.Equal(CompiledContentCacheStatus.Hit, cached.CacheStatus);
            foreach (var content in new[] { fresh.Content!, cached.Content! })
            {
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
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
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

    private sealed class ChoiceRandom(int choice) : IRandomSource
    {
        public int NextInclusive(int minimum, int maximum) { Assert.InRange(choice, minimum, maximum); return choice; }
        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException();
        public double NextUnit() => throw new InvalidOperationException();
    }

    private static void CopyFixture(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
