using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyBaseResearchTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 2)]
    public void DailyResearchSelectsQuietBaseDeletionBeforeActivityAndPreservesOnlySurvivingSidecars(bool cache, bool visible, int noEffect)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-activity");
        var content = Content(cache, activity: noEffect == 1 ? 1 : 0, pacts: noEffect == 2, emptyChoices: noEffect == 1);
        var deletedContent = Content(cache, "CORE", noEffect == 1 ? 1 : 0, noEffect == 2, noEffect == 1);
        var survivedContent = Content(cache, "", noEffect == 1 ? 1 : 0, noEffect == 2, noEffect == 1);
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, visible);
            if (noEffect == 2) initial = initial with
            {
                Countries = [initial.Countries[0] with { Pact = true, CancelPact = true, NewPact = false }],
                World = initial.World with { AlienBases = [initial.World.AlienBases[0] with { PactCountryId = initial.Countries[0].RuleId }, initial.World.AlienBases[1]] },
            };
            var yaml = OxceSaveAdapter.EmitNewCampaign(initial);
            var start = yaml.IndexOf("alienBases:\n", StringComparison.Ordinal);
            Assert.True(start >= 0);
            var firstLineEnd = yaml.IndexOf('\n', start + "alienBases:\n".Length);
            yaml = yaml.Insert(firstLineEnd + 1, "    futureResearchBase: retained\n");
            var loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed, "daily-base-research-before.sav");
            AdvanceUnblocked(loaded.Campaign, 1);
            var after = loaded.Campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(deleted ? 1 : 2, after.World.AlienBases.Count);
            Assert.Equal(deleted ? 2 : 9, after.Regions.Sum(region => (long)region.ActivityAlien[^1]) -
                initial.Regions.Sum(region => (long)region.ActivityAlien[^1]));
            Assert.Equal(9UL, after.World.AlienBaseBirthSequence);
            Assert.Equal(70, after.NextIds["QUIET_RESEARCH_MARKER"]);
            Assert.Equal(visible && !deleted, loaded.Campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld().Targets.Any(target =>
                target.Kind == WorldTargetKind.AlienBase && target.Id == 6));
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
            {
                var written = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
                Assert.Equal(!deleted, written.Contains("futureResearchBase: retained", StringComparison.Ordinal));
                loaded = TestFixtures.LoadLogisticsSave(written, content, seed, "daily-base-research-rewrite.sav");
                Assert.Equivalent(after, loaded.Campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(loaded.Campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), loaded.Campaign.Capture(), strict: true);
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PossibleDeletionStillRejectsAmbiguousMarkerIdentities(bool cache)
    {
        var content = Content(cache);
        var initial = Snapshot(content, 17, false);
        initial = initial with
        {
            World = initial.World with
            {
                AlienBases = [initial.World.AlienBases[0], initial.World.AlienBases[0] with
                { DeploymentId = "QUIET_OTHER_BASE", PreservationKey = "alien-base:generated:9" }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction cannot resolve an ambiguous marker identity.");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool visible)
    {
        var initial = CreateSnapshot(content, "Daily quiet base research").WithNextId("QUIET_RESEARCH_MARKER", 70);
        return initial with
        {
            RandomState = seed,
            Time = new(2, 2, 1, 1999, 23, 59, 55),
            Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)] }],
            World = initial.World with
            {
                AlienBaseBirthSequence = 9,
                AlienBases = [new(6, "QUIET_RESEARCH_BASE", "RACE_A", 0.2, 0.1)
                { Discovered = visible, PreservationKey = "alien-base:generated:8" },
                    new(7, "QUIET_SURVIVOR_BASE", "RACE_A", 0.3, 0.1) { PreservationKey = "alien-base:generated:9" }],
            },
        };
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public void UnprojectedDeletionEffectsHavePreciseAtomicBoundaries(bool cache, int effect)
    {
        var content = Content(cache, pacts: effect == 4);
        var initial = Snapshot(content, 17, false);
        var target = new WorldTargetReference(WorldTargetKind.AlienBase, "QUIET_RESEARCH_MARKER", 6, 0.2, 0.1);
        if (effect == 1) initial = initial.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Destination = target } });
        if (effect == 2) initial = initial with
        {
            Bases = [initial.Bases[0] with
            {
                Transfers = [new(101, 1, CampaignTransferKind.Craft, "SHIP", 1, Craft:
                    initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP") with { Id = 999, Logistics =
                        initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics! with { Destination = target } })],
            }],
        };
        if (effect == 4) initial = initial with
        {
            Countries = [initial.Countries[0] with { Pact = true, NewPact = true }],
            World = initial.World with { AlienBases = [initial.World.AlienBases[0] with { PactCountryId = initial.Countries[0].RuleId }, initial.World.AlienBases[1]] },
        };
        var reason = effect switch
        {
            4 => "Alien base self-destruction requires uncertain pact-cancellation forecasts.",
            _ => "Alien base self-destruction requires linked-target alternatives for uncertain research.",
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryUfoCoordinatesDoNotCreateAnAlienBaseFollower(bool cache)
    {
        var content = Content(cache);
        var initial = Snapshot(content, 17, false);
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [new(4, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 1, -1)],
                Ufos = [new(9, "UFO_SCOUT", 4, "TRAJ_PATROL", 0, 0.4, 0.2, UfoStatus.Flying, "STR_HIGH_UC")
                { Id = 3, Speed = 2200, MissionWaveNumber = 0, Destination = new(WorldTargetKind.AlienBase, "QUIET_RESEARCH_MARKER", 6, 0.2, 0.1) }],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Equal(WorldTargetKind.Waypoint, Assert.Single(campaign.Capture().World.Ufos).Destination!.Kind);
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var controlContent = Content(cache, after.CompletedResearch.Contains("BONUS") ? "CORE" : "");
        var control = CampaignState.Restore(initial, controlContent, new SplitMix64RandomSource(17));
        AdvanceUnblocked(control, 1);
        Assert.Equivalent(control.Capture(), after, strict: true);
        Reload(content, campaign, 17, "quiet-base-anonymous-ufo.sav");
    }

    [Theory]
    [InlineData(false, -5)]
    [InlineData(true, -5)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void OptionalBaseActivityCannotMaskLaterScoreOverflow(bool cache, int score)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-activity");
        var content = Content(cache, points: score < 0 ? 7 : -7, followingScore: score);
        var initial = ActivitySnapshot(content, score < 0 ? int.MinValue + 3 : int.MaxValue - 3);
        for (ulong seed = 1; seed <= 32; seed++)
            AssertTimeBlocked(CampaignState.Restore(initial with { RandomState = seed }, content, new SplitMix64RandomSource(seed)),
                "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false, -5)]
    [InlineData(true, -5)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void SafeOptionalBaseActivityMatchesSelectedControlsAtBothScoreLimits(bool cache, int score)
    {
        var points = score < 0 ? 7 : -7;
        var content = Content(cache, points: points, followingScore: score);
        var deletedContent = Content(cache, "CORE", points: points, followingScore: score);
        var survivedContent = Content(cache, "", points: points, followingScore: score);
        var initial = ActivitySnapshot(content, score < 0 ? int.MinValue + 5 : int.MaxValue - 5);
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var selected = initial with { RandomState = seed };
            var campaign = CampaignState.Restore(selected, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + score + (deleted ? 0 : points), after.Regions[0].ActivityAlien[^1]);
            var control = CampaignState.Restore(selected, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "optional-base-activity-limit.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    private static CampaignSnapshot ActivitySnapshot(RuntimeContent content, int activity)
    {
        var initial = Snapshot(content, 17, false);
        initial = initial with
        {
            Regions = [.. initial.Regions.Select(region => region with { ActivityAlien = [activity] })],
            World = initial.World with
            {
                AlienBases = [initial.World.AlienBases[0]],
                Missions = [new(4, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 1, -1)],
                Ufos = [new(9, "UFO_OPTIONAL_ACTIVITY", 4, "TRAJ_PATROL", 0, 0.2, 0.1, UfoStatus.Flying, "STR_HIGH_UC")
                { Id = 3, Speed = 2200, MissionWaveNumber = 0, Destination = WorldTargetReference.ForWaypoint(0, new(0.6, 0.2)) }],
            },
        };
        return initial;
    }

    internal static RuntimeContent Content(bool cache, string code = "BONUS", int activity = 0, bool pacts = false, bool emptyChoices = false,
        int points = 7, int? followingScore = null) =>
        StrategicReadinessTestContent.Load(pacts ? ["strategic-world.rul", "strategic-alien-base-cleanup-pacts.rul"] : ["strategic-world.rul"], cache, $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
          - {name: BONUS, cost: 1}
          - {name: OTHER, cost: 1}
        alienDeployments:
          - type: QUIET_RESEARCH_BASE
            markerName: QUIET_RESEARCH_MARKER
            points: {{{points}}}
            baseSelfDestructCode: {{{code}}}
            genMission: {{{(emptyChoices ? "{}" : "{MISSION_SCOUT: 1}")}}}
            genMissionFreq: {{{(activity == 1 ? 100 : 0)}}}
            baseDetectionRange: {{{(activity == 2 ? 100 : 0)}}}
            huntMissionMaxFrequency: 0
            huntMissionWeights: {0: {MISSION_SCOUT: 1}}
          - {type: QUIET_SURVIVOR_BASE, markerName: QUIET_SURVIVOR_MARKER, points: 2}
          - {type: QUIET_OTHER_BASE, markerName: QUIET_RESEARCH_MARKER}
        {{{(followingScore is { } score ? "ufos:\n  - {type: UFO_OPTIONAL_ACTIVITY, speedMax: 2200, damageMax: 50, missionScore: " + score + "}" : "")}}}
        """);
}
