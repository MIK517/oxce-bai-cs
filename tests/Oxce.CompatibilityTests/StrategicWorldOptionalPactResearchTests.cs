using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalPactResearchTests
{
    private static readonly int[] IneligibleFlags = [1, 2];

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void OptionalPactCancellationPreservesAllFlagsAndLaterInfiltrationCallers(bool cache, int caller)
    {
        using var cleanup = TestFixtures.ReadVerifiedExpected("strategic-alien-base-cleanup");
        using var infiltration = TestFixtures.ReadVerifiedExpected("strategic-infiltration");
        Assert.NotEmpty(TestFixtures.Rows(cleanup.RootElement, "rows"));
        Assert.NotEmpty(TestFixtures.Rows(infiltration.RootElement, "countries"));
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-region");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var content = Content(cache, caller);
        var deletedContent = Content(cache, caller, "CORE");
        var survivedContent = Content(cache, caller, "");
        for (var flags = 0; flags < 8; flags++)
        {
            var deletedSeen = false;
            var survivedSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = Snapshot(content, seed, caller, flags);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                var original = initial.Countries[0];
                var signed = caller != 0 && (deleted || WorldInfiltration.CanInfiltrate(original.Pact, original.NewPact, original.CancelPact));
                var country = Assert.Single(after.Countries);
                Assert.Equal(original.Pact, country.Pact);
                Assert.Equal(signed || !deleted && original.NewPact, country.NewPact);
                Assert.Equal(!signed && (deleted ? original.Pact : original.CancelPact), country.CancelPact);
                Assert.Equal(!deleted, after.World.AlienBases.Any(alienBase => alienBase.Id == 6));
                var pactBase = after.World.AlienBases.SingleOrDefault(alienBase => alienBase.DeploymentId == "OPTIONAL_PACT_OUTPUT");
                Assert.Equal(signed, pactBase is not null);
                if (pactBase is not null)
                {
                    Assert.Equal(100, pactBase.Id);
                    Assert.Equal(country.RuleId, pactBase.PactCountryId);
                    Assert.Equal(caller != 3, pactBase.Discovered);
                }
                Assert.Equal(signed ? 101 : 100, after.NextIds["OPTIONAL_PACT_MARKER"]);
                if (caller >= 2)
                {
                    var generator = Assert.Single(after.World.AlienBases, alienBase => alienBase.Id == 7);
                    Assert.Equal(caller == 2 ? 1 : 0, generator.GenMissionCount);
                    Assert.Equal(caller == 3 ? 0 : 50, generator.MinutesSinceLastHuntMissionGeneration);
                }
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, seed, "optional-pact-research.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
                deletedSeen |= deleted;
                survivedSeen |= !deleted;
            }
            Assert.True(deletedSeen);
            Assert.True(survivedSeen);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void PossibleNewEligibilityReservesBirthCapacityBeforeDailyPactMutation(bool cache, int caller)
    {
        var content = Content(cache, caller);
        // Both initial pact states are ineligible, but source deletion makes them eligible.
        foreach (var flags in IneligibleFlags)
        {
            var initial = Snapshot(content, 17, caller, flags).WithNextId("OPTIONAL_PACT_MARKER", int.MaxValue);
            AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
                "Base objective IDs are exhausted or collide.");
            initial = Snapshot(content, 17, caller, flags);
            initial = initial with { World = initial.World with { AlienBaseBirthSequence = ulong.MaxValue } };
            AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
                "Base objective birth sequence is exhausted.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledPactCancellationKeepsOriginalEligibilityAndUnusedMarkerCapacity(bool cache)
    {
        var content = Content(cache, 1, cancelPacts: false);
        var deletedContent = Content(cache, 1, "CORE", cancelPacts: false);
        var survivedContent = Content(cache, 1, "", cancelPacts: false);
        for (var flags = 0; flags < 8; flags++)
        {
            var deletedSeen = false;
            var survivedSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = Snapshot(content, seed, 1, flags);
                var original = initial.Countries[0];
                var signed = WorldInfiltration.CanInfiltrate(original.Pact, original.NewPact, original.CancelPact);
                if (!signed) initial = initial.WithNextId("OPTIONAL_PACT_MARKER", int.MaxValue);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var deleted = after.CompletedResearch.Contains("BONUS");
                var country = Assert.Single(after.Countries);
                Assert.Equal(original.Pact, country.Pact);
                Assert.Equal(signed || original.NewPact, country.NewPact);
                Assert.Equal(!signed && original.CancelPact, country.CancelPact);
                Assert.Equal(signed, after.World.AlienBases.Any(alienBase => alienBase.DeploymentId == "OPTIONAL_PACT_OUTPUT"));
                Assert.Equal(signed ? 101 : int.MaxValue, after.NextIds["OPTIONAL_PACT_MARKER"]);
                var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), after, strict: true);
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, seed, "disabled-optional-pact.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                deletedSeen |= deleted;
                survivedSeen |= !deleted;
            }
            Assert.True(deletedSeen);
            Assert.True(survivedSeen);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeventyTwoHoursPreserveSelectedPactsAcrossBatchesReloadsAndHuntingCleanup(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var content = Content(cache, 3);
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = Snapshot(content, seed, 3, flags: 1);
            var batched = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var reloaded = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(batched, 1);
            AdvanceUnblocked(reloaded, 1);
            var deleted = batched.Capture().CompletedResearch.Contains("BONUS");
            for (var day = 0; day < 3; day++)
            {
                AdvanceUnblocked(batched, 17_280);
                for (var hour = 0; hour < 24; hour++) AdvanceUnblocked(reloaded, 720);
                reloaded = Reload(content, reloaded, seed, "optional-pact-endurance.sav");
                var after = batched.Capture();
                Assert.Equivalent(after, reloaded.Capture(), strict: true);
                var country = Assert.Single(after.Countries);
                Assert.True(country.Pact);
                Assert.Equal(deleted, country.NewPact);
                Assert.False(country.CancelPact);
                Assert.Equal(2, after.World.AlienBases.Count);
                Assert.Equal(deleted, after.World.AlienBases.Any(alienBase => alienBase.DeploymentId == "OPTIONAL_PACT_OUTPUT"));
                Assert.Empty(after.World.Ufos);
                Assert.Empty(after.World.MissionSites);
                Assert.InRange(after.World.Missions.Count, 0, 1);
            }
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    internal static RuntimeContent Content(bool cache, int caller, string code = "BONUS", bool cancelPacts = true) =>
        StrategicReadinessTestContent.Load(cancelPacts ? ["strategic-world.rul", "strategic-alien-base-cleanup-pacts.rul"] : ["strategic-world.rul"], cache, $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
          - {name: BONUS, cost: 1}
          - {name: OTHER, cost: 1}
        alienDeployments:
          - {type: OPTIONAL_PACT_SOURCE, markerName: OPTIONAL_PACT_SOURCE_MARKER, baseSelfDestructCode: {{{code}}}}
          - {type: OPTIONAL_PACT_OUTPUT, markerName: OPTIONAL_PACT_MARKER}
          - type: OPTIONAL_PACT_GENERATOR
            markerName: OPTIONAL_PACT_GENERATOR_MARKER
            genMission: {OPTIONAL_PACT_MISSION: 1}
            genMissionFreq: {{{(caller == 2 ? 100 : 0)}}}
            genMissionLimit: 1
            baseDetectionRange: {{{(caller == 3 ? 100 : 0)}}}
            baseDetectionChance: 100
            huntMissionMaxFrequency: 60
            huntMissionWeights: {0: {OPTIONAL_PACT_MISSION: 1}}
        alienMissions:
          - type: OPTIONAL_PACT_MISSION
            objective: 1
            operationType: 0
            endlessInfiltration: false
            showAlienBase: true
            spawnZone: 5
            siteType: OPTIONAL_PACT_OUTPUT
            waves: [{count: 1, trajectory: TRAJ_PATROL, timer: 0}]
        """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int caller, int flags)
    {
        var initial = CreateSnapshot(content, "Optional pact eligibility").WithNextId("OPTIONAL_PACT_MARKER", 100);
        if (caller == 3) initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 300,
                Speed = 0,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = WorldTargetReference.ForWaypoint(1, new(1, 0.6))
            },
        });
        var source = new AlienBaseSnapshot(6, "OPTIONAL_PACT_SOURCE", "RACE_A", 0.2, 0.1)
        { PactCountryId = initial.Countries[0].RuleId, Discovered = true, PreservationKey = "alien-base:generated:8" };
        var generator = new AlienBaseSnapshot(7, "OPTIONAL_PACT_GENERATOR", "RACE_A", 0.21, 0.11)
        { Discovered = true, MinutesSinceLastHuntMissionGeneration = 50, PreservationKey = "alien-base:generated:9" };
        return initial with
        {
            RandomState = seed,
            Time = new(2, 2, 1, 1999, 23, 59, 55),
            Countries = [initial.Countries[0] with { Pact = (flags & 1) != 0, NewPact = (flags & 2) != 0, CancelPact = (flags & 4) != 0 }],
            Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)], Soldiers = [] }],
            World = initial.World with
            {
                AlienBaseBirthSequence = caller >= 2 ? 9UL : 8UL,
                AlienBases = caller >= 2 ? [source, generator] : [source],
                Missions = caller == 1 ? [new(4, "OPTIONAL_PACT_MISSION", "REGION", "RACE_A", 0, 0, 0, 0, -1)] : [],
                Waypoints = caller == 3 ? [new(1, 1, 0.6)] : [],
            },
        };
    }
}
