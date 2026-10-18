using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldAlienBaseMonthlyTests
{
    [Fact]
    public void MonthlyUpgradesAndEvolutionMatchExtractedReference()
    {
        var content = Load();
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-monthly");
        var rows = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var name = row[0].GetString()!;
            var snapshot = Snapshot(content, "MONTHLY_" + name, row[1].GetInt32());
            var start = name switch { "timelineEarly" => 2, "futureStart" => 4, _ => 1 };
            snapshot = snapshot with
            {
                World = snapshot.World with
                { AlienBases = [snapshot.World.AlienBases[0] with { StartMonth = start }] }
            };
            var random = new MinimumChoices();
            var campaign = CampaignState.Restore(snapshot, content, random);
            var result = campaign.Execute(new AdvanceMonthlyWorldAlienBases(row[2].GetInt32() != 0));
            var error = result.Events.OfType<CampaignActionBlocked>().Any();
            if (error)
            {
                Assert.Equal("Alien race evolution reaches the reference 100-transition limit.",
                    Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Reason);
                Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
            }
            else Assert.Empty(result.Events);
            var after = campaign.Capture();
            var alienBase = Assert.Single(after.World.AlienBases);
            rows.Add([name, row[1].GetInt32(), row[2].GetInt32(), alienBase.DeploymentId, alienBase.Race,
                alienBase.StartMonth, alienBase.MinutesSinceLastHuntMissionGeneration, error ? 1 : 0, random.Calls]);
            Assert.Equal(7, alienBase.GenMissionCount);
            Assert.Equal(snapshot.Time, after.Time);
            Assert.Equal(snapshot.World.AlienBases[0].PreservationKey, alienBase.PreservationKey);
            Reload(content, campaign, 40, "monthly-base-oracle.sav");
        }
        TestFixtures.AssertJsonEqual(oracle.RootElement.GetProperty("rows"), JsonSerializer.SerializeToUtf8Bytes(rows));
    }

    [Fact]
    public void EvolutionLimitsMatchReferenceIncludingFiniteHundredTransitionChain()
    {
        var content = Load();
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-monthly");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "evolutionLimitRows"))
        {
            var snapshot = Snapshot(content, "MONTHLY_chain" + row[0].GetInt32());
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
            var result = campaign.Execute(new AdvanceMonthlyWorldAlienBases());
            Assert.Equal(row[1].GetInt32() != 0, result.Events.OfType<CampaignActionBlocked>().Any());
            if (row[1].GetInt32() != 0)
                Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
            else Assert.Equal(row[2].GetString(), Assert.Single(campaign.Capture().World.AlienBases).Race);
            Reload(content, campaign, 40, "monthly-base-evolution-limit.sav");
        }
    }

    [Fact]
    public void DeploymentChangesRepairMissionAndCraftReferencesAndPersistAllBaseState()
    {
        var content = Load();
        var snapshot = Snapshot(content, "MONTHLY_upgrade");
        var link = new WorldTargetReference(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 2, 0.2, 0.1);
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Status = "STR_OUT", Destination = link } });
        snapshot = snapshot with
        {
            World = snapshot.World with
            { Missions = [new(4, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 0, -1) { AlienBase = link }] }
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
        Assert.Empty(campaign.Execute(new AdvanceMonthlyWorldAlienBases()).Events);
        var after = campaign.Capture();
        var alienBase = Assert.Single(after.World.AlienBases);
        Assert.Equal((2, "MONTHLY_NEW", "RACE_A", "Custom base", true, 7, "COUNTRY"),
            (alienBase.Id, alienBase.DeploymentId, alienBase.Race, alienBase.Name,
                alienBase.Discovered, alienBase.GenMissionCount, alienBase.PactCountryId));
        Assert.Equal("STR_ALIEN_BASE_NEW", Assert.Single(after.World.Missions).AlienBase!.TypeId);
        Assert.Equal("RACE_A", Assert.Single(after.World.Missions).Race);
        Assert.Equal("STR_ALIEN_BASE_NEW", Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP")
            .Logistics!.Destination!.TypeId);
        var target = Assert.Single(campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld().Targets,
            target => target.Kind == WorldTargetKind.AlienBase);
        Assert.Equal("STR_ALIEN_BASE_NEW", target.Reference!.TypeId);
        Reload(content, campaign, 40, "monthly-upgraded-base-links.sav");
    }

    [Fact]
    public void UnknownSidecarsFollowUpgradesAcrossRepeatedLoadedRewritesAndDoNotResurrectDeletedBases()
    {
        var content = Load();
        var yaml = OxceSaveAdapter.EmitNewCampaign(Snapshot(content, "MONTHLY_upgrade"));
        var start = yaml.IndexOf("alienBases:\n", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var line = yaml.IndexOf('\n', start + "alienBases:\n".Length);
        yaml = yaml.Insert(line + 1, "    futureBase: preserved\n");
        // Exercise a legacy source that predates the new sidecar key.
        yaml = string.Join('\n', yaml.Split('\n').Where(value => !value.Contains("oxcePortEntityKey:", StringComparison.Ordinal)));
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 40, "monthly-base-legacy.sav");
        Assert.Empty(loaded.Campaign.Execute(new AdvanceMonthlyWorldAlienBases()).Events);
        for (var rewrite = 0; rewrite < 3; rewrite++)
        {
            yaml = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
            Assert.Contains("futureBase: preserved", yaml, StringComparison.Ordinal);
            var snapshot = loaded.Campaign.Capture();
            loaded = TestFixtures.LoadLogisticsSave(yaml, content, 40, "monthly-base-rewrite.sav");
            Assert.Equivalent(snapshot, loaded.Campaign.Capture(), strict: true);
            Assert.Empty(loaded.Campaign.Execute(new AdvanceMonthlyWorldAlienBases()).Events);
        }
        var empty = loaded.Campaign.Capture();
        yaml = OxceSaveAdapter.EmitLoadedCampaign(empty with { World = empty.World with { AlienBases = [] } }, loaded.Source);
        Assert.DoesNotContain("futureBase", yaml, StringComparison.Ordinal);
        Assert.Empty(TestFixtures.LoadLogisticsSave(yaml, content, 40).Campaign.Capture().World.AlienBases);
    }

    [Theory]
    [InlineData("MONTHLY_futureTimeline", "Alien base upgrade timeline has no entry for its current age.")]
    [InlineData("MONTHLY_badUpgrade", "Alien base upgrade hunting timer cannot be negative.")]
    [InlineData("MONTHLY_badEvolutionUpgrade", "Alien race evolution reaches the reference 100-transition limit.")]
    [InlineData("MONTHLY_overflowWeights", "Alien base mission weights exceed the supported range.")]
    public void EveryReachableOutcomeIsCheckedBeforeAnyBaseOrRandomnessChanges(string deployment, string reason)
    {
        var content = Load();
        var snapshot = Snapshot(content, "MONTHLY_upgrade");
        snapshot = snapshot with
        {
            World = snapshot.World with
            { AlienBases = [snapshot.World.AlienBases[0], new(3, deployment, "RACE_A", 0.3, 0.1) { StartMonth = 1 }] }
        };
        AssertBlocked(content, snapshot, reason);
    }

    [Fact]
    public void PotentialMarkerCollisionStopsBeforeReferenceRewiring()
    {
        var content = Load();
        var snapshot = Snapshot(content, "MONTHLY_upgrade");
        snapshot = snapshot with
        {
            World = snapshot.World with
            { AlienBases = [snapshot.World.AlienBases[0], new(2, "MONTHLY_NEW", "RACE_B", 0.3, 0.1)] }
        };
        AssertBlocked(content, snapshot, "Alien base upgrades can collide with another world target's marker identity.");
    }

    [Fact]
    public void PotentialSiteMarkerCollisionStopsBeforeSaveTargetsCanBeReclassified()
    {
        var content = Load();
        var snapshot = Snapshot(content, "MONTHLY_upgrade");
        snapshot = snapshot with
        {
            World = snapshot.World with
            { MissionSites = [new(2, "MISSION_SITE", "MONTHLY_NEW", "RACE_A", 0.3, 0.1, 3600)] }
        };
        AssertBlocked(content, snapshot, "Alien base upgrades can collide with another world target's marker identity.");
    }

    [Theory]
    [InlineData("MONTHLY_craftMarkerUpgrade")]
    [InlineData("MONTHLY_legacyMarkerUpgrade")]
    public void UpgradeMarkerPreservesFollowerOrStopsBeforeAmbiguousReload(string deployment)
    {
        var content = Load();
        var snapshot = Snapshot(content, deployment);
        var destination = new WorldTargetReference(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 2, 0.2, 0.1);
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Status = "STR_OUT", Destination = destination } });
        if (deployment == "MONTHLY_legacyMarkerUpgrade")
        {
            AssertBlocked(content, snapshot, "Alien base upgrades can collide with another world target's marker identity.");
            return;
        }
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
        Assert.Empty(campaign.Execute(new AdvanceMonthlyWorldAlienBases()).Events);
        var before = campaign.Capture();
        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(before), content, 40).Campaign.Capture();
        Assert.Equal(Assert.Single(before.Bases[0].Crafts, craft => craft.RuleId == "SHIP").Logistics!.Destination,
            Assert.Single(reloaded.Bases[0].Crafts, craft => craft.RuleId == "SHIP").Logistics!.Destination);
    }

    [Fact]
    public void ActualCraftIdentityCollisionStopsBeforeUpgradeAndFollowerRewiring()
    {
        var content = Load();
        var snapshot = Snapshot(content, "MONTHLY_craftMarkerUpgrade");
        var owner = snapshot.Bases[0];
        var craft = Assert.Single(owner.Crafts, craft => craft.RuleId == "SHIP");
        snapshot = snapshot with { Bases = [owner with { Crafts = [.. owner.Crafts, craft with { Id = 2 }] }] };
        AssertBlocked(content, snapshot, "Alien base upgrades can collide with another world target's marker identity.");
    }

    [Fact]
    public void SavedMarkerClassificationMatchesCraftLoadAndFinishLoadingReference()
    {
        var content = Load();
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-monthly");
        var rows = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "markerRows"))
        {
            var name = row[0].GetString()!;
            var snapshot = Snapshot(content, name == "legacyAlias" ? "MONTHLY_legacyMarker" : "MONTHLY_craftMarker");
            var marker = name == "legacyAlias" ? "STR_ALIEN_TERROR" : "SHIP";
            snapshot = snapshot.WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                { Status = "STR_OUT", Destination = new(WorldTargetKind.AlienBase, marker, 2, 0.2, 0.1) }
            });
            if (name == "craftIdentity")
            {
                var owner = snapshot.Bases[0];
                var craft = Assert.Single(owner.Crafts, craft => craft.RuleId == "SHIP");
                snapshot = snapshot with
                {
                    Bases = [owner with { Crafts = [.. owner.Crafts,
                    craft with { Id = 2, Logistics = craft.Logistics! with { Destination = null, Status = "STR_READY" } }] }]
                };
            }
            var after = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(snapshot), content, 40).Campaign.Capture();
            var destination = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP" && craft.Id == 1)
                .Logistics!.Destination;
            rows.Add([name, destination?.Kind switch { WorldTargetKind.AlienBase => 1, WorldTargetKind.Craft => 2, null => 0,
                _ => throw new InvalidOperationException("Unexpected target kind.") }]);
            Reload(content, CampaignState.Restore(after, content, new SplitMix64RandomSource(40)), 40, "monthly-marker-classification.sav");
        }
        TestFixtures.AssertJsonEqual(oracle.RootElement.GetProperty("markerRows"), JsonSerializer.SerializeToUtf8Bytes(rows));
    }

    [Fact]
    public void ExcessiveBasePopulationIsRejectedBeforeUpgradeSelection()
    {
        var content = Load();
        var snapshot = Snapshot(content, "MONTHLY_noWeights");
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                AlienBases = Enumerable.Range(1, 100_001).Select(id => new AlienBaseSnapshot(id,
                "MONTHLY_noWeights", "RACE_A", 0.2, 0.1)).ToArray()
            }
        };
        AssertBlocked(content, snapshot, "Alien base monthly processing exceeds the supported work limit.");
    }

    [Fact]
    public void ExcessiveReferenceRepairWorkStopsBeforeAnyUpgrade()
    {
        var content = Load();
        var snapshot = Snapshot(content, "MONTHLY_upgrade");
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                AlienBases = Enumerable.Range(1, 100).Select(id => new AlienBaseSnapshot(id,
                    "MONTHLY_upgrade", "RACE_A", 0.2, 0.1)).ToArray(),
                Missions = Enumerable.Range(1, 10_001).Select(id => new AlienMissionSnapshot(id,
                    "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 0, -1)).ToArray(),
            }
        };
        AssertBlocked(content, snapshot, "Alien base monthly processing exceeds the supported work limit.");
    }

    [Fact]
    public void DuplicateSidecarKeysAreRejectedDuringRestore()
    {
        var content = Load();
        var snapshot = Snapshot(content, "MONTHLY_upgrade");
        var alienBase = snapshot.World.AlienBases[0];
        snapshot = snapshot with { World = snapshot.World with { AlienBases = [alienBase, alienBase with { Id = 3 }] } };
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40)));
    }

    [Fact]
    public void TheCampaignMonthRunsTheMonthlyOperation()
    {
        // Branch 6 S3: the month tick runs determineAlienMissions, whose last phase is this operation.
        var content = Load();
        var snapshot = Snapshot(content, "MONTHLY_noWeights") with { Time = new(1, 31, 1, 1999, 23, 59, 55) };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Single(result.Events.OfType<MonthlyReportReady>());
        Assert.Equal(snapshot.MonthsPassed + 1, campaign.MonthsPassed);
        Reload(content, campaign, 40, "monthly-operation-in-month.sav");
    }

    private static void AssertBlocked(RuntimeContent content, CampaignSnapshot snapshot, string reason)
    {
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
        var before = campaign.Capture();
        Assert.Equal(reason, Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new AdvanceMonthlyWorldAlienBases()).Events)).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static RuntimeContent Load()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-monthly");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-monthly.rul"]);
    }

    private static CampaignSnapshot Snapshot(RuntimeContent content, string deployment, int month = 3) =>
        CreateSnapshot(content, "Monthly alien bases") with
        {
            RandomState = 0,
            MonthsPassed = month,
            World = new()
            {
                AlienBases = [new(2, deployment, "RACE_A", 0.2, 0.1)
            { StartMonth = 1, GenMissionCount = 7, MinutesSinceLastHuntMissionGeneration = 17,
                Name = "Custom base", Discovered = true, PactCountryId = "COUNTRY" }]
            },
        };

    private sealed class MinimumChoices : IStatefulRandomSource
    {
        public List<int[]> Calls { get; } = [];
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextInclusive(int minimum, int maximum)
        {
            Assert.True(minimum <= maximum);
            Calls.Add([minimum, maximum]);
            State++;
            return minimum;
        }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public double NextUnit() => throw new InvalidOperationException("No floating draw is expected during monthly upgrades.");
    }
}
