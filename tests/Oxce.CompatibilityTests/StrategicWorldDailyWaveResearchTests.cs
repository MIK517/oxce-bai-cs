using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyWaveResearchTests
{
    [Fact]
    public void MissionThinkRevelationMatchesPinnedCallerIncludingLastBirthOnly()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-objective-base-spawn");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "revelationRows"))
        {
            var initial = row[0].GetInt32();
            var births = row[1].GetInt32();
            var discovered = row[2].GetInt32() != 0;
            var show = row[3].GetInt32() != 0;
            var reveal = WorldObjectiveBaseSpawn.RevealAfterMissionThink(births != 0, discovered, show);
            var actual = Enumerable.Repeat(discovered, initial + births).ToArray();
            if (reveal) actual[^1] = true;
            Assert.Equal(row[4].GetInt32(), reveal ? 1 : 0);
            Assert.Equal(row[5].EnumerateArray().Select(value => value.GetInt32() != 0), actual);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void DailyInterruptionPrecedesDueWavesAndFinalObjectives(int kind)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-region");
        var content = Content(kind);
        var interruptedContent = Content(kind, "CORE");
        var continuedContent = Content(kind, "");
        var interruptedSeen = false;
        var continuedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, kind);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var interrupted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(kind == 1 && !interrupted ? 1 : 0, after.World.Ufos.Count);
            Assert.Equal(kind is 3 or 4 && !interrupted ? 1 : 0, after.World.AlienBases.Count);
            Assert.Equal(kind == 5 && !interrupted ? 1 : 0, after.World.MissionSites.Count);
            Assert.Equal(kind == 4 && !interrupted, after.Countries.Any(country => country.NewPact));
            Assert.Equal(kind is 3 or 4 && !interrupted, result.Events.OfType<AlienBaseDiscovered>().Any());
            if (kind is 3 or 4 && !interrupted)
            {
                var alienBase = Assert.Single(after.World.AlienBases);
                Assert.True(alienBase.Discovered);
                Assert.Equal((70, "alien-base:generated:1"), (alienBase.Id, alienBase.PreservationKey));
                Assert.Single(campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld().Targets, target => target.Kind == WorldTargetKind.AlienBase);
            }
            var control = CampaignState.Restore(initial, interrupted ? interruptedContent : continuedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++) campaign = Reload(content, campaign, seed, "daily-wave-research-selected.sav");
            AssertContinuesLike(control, campaign, 1);
            interruptedSeen |= interrupted;
            continuedSeen |= !interrupted;
        }
        Assert.True(interruptedSeen);
        Assert.True(continuedSeen);
    }

    [Fact]
    public void PossibleDailyBirthsStillReserveBaseAndSiteMarkers()
    {
        var content = Content(3);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17, 3).WithNextId("DAILY_BASE", int.MaxValue), content, new SplitMix64RandomSource(17)),
            "Base objective IDs are exhausted or collide.");
        content = Content(5);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 17, 5).WithNextId("DAILY_SITE", int.MaxValue), content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int kind)
    {
        var initial = CreateSnapshot(content, "Daily wave research") with { RandomState = seed, Time = new(2, 2, 1, 1999, 23, 59, 55) };
        return initial.WithNextId("DAILY_BASE", 70).WithNextId("DAILY_SITE", 70) with
        {
            Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)], Soldiers = [] }],
            World = initial.World with
            {
                Missions = [new(4, "DAILY_RESEARCH_WAVE", "REGION", "RACE_A", 0, 0, kind == 0 ? 9000 : 0, 0, -1)],
            },
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedBaseRevelationBelongsToHalfHourThinkAndSkipsLateHunting(bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        CampaignSnapshot? hidden = null;
        foreach (var show in new[] { false, true })
        {
            var content = StrategicWorldSiteResearchGeneratedObjectiveTests.Content(2, hunting, prefix: false, interrupt: "", showBase: show);
            var initial = StrategicWorldSiteResearchGeneratedObjectiveTests.Snapshot(content, hunting);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            Assert.Equal(2, after.World.AlienBases.Count);
            Assert.Equal(show && !hunting, after.World.AlienBases[^1].Discovered);
            Assert.Equal(show && !hunting, result.Events.OfType<AlienBaseDiscovered>().Any());
            hidden ??= after;
            var expected = hidden with
            {
                World = hidden.World with { AlienBases = [hidden.World.AlienBases[0], hidden.World.AlienBases[1] with { Discovered = show && !hunting }] },
            };
            Assert.Equivalent(expected, after, strict: true);
            for (var pass = 0; pass < 3; pass++) campaign = Reload(content, campaign, 17, "generated-base-revelation.sav");
        }
    }

    internal static RuntimeContent Content(int kind, string interrupt = "BONUS", int operation = 0, string sourceCode = "BONUS",
        int generation = 0, bool immediateResearch = false, bool includeSource = false)
    {
        var wave = kind switch
        {
            0 => "{ufo: UFO_SCOUT, count: 1, trajectory: DAILY_AIR, timer: 9000}",
            1 => "{ufo: UFO_SCOUT, count: 2, trajectory: DAILY_AIR, timer: 60}",
            2 => "{count: 2, trajectory: TRAJ_PATROL, timer: 0}",
            5 => "{ufo: DEPLOY_DAILY_SITE, count: 1, trajectory: TRAJ_PATROL, timer: 0}",
            _ => "{count: 1, trajectory: TRAJ_PATROL, timer: 0}",
        };
        return StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
            research:
              - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            alienDeployments:
              - {type: DEPLOY_DAILY_BASE, markerName: DAILY_BASE}
              - {type: DEPLOY_DAILY_SITE, markerName: DAILY_SITE, duration: [{{{(immediateResearch ? 0 : 1)}}}, {{{(immediateResearch ? 0 : 1)}}}], unlockedResearchOnDespawn: {{{(immediateResearch ? "BONUS" : "")}}}}
            {{{(operation == 0 && !includeSource ? "" : "  - type: OPTIONAL_OPERATION_BASE\n    markerName: OPTIONAL_SOURCE\n    points: 7\n    baseSelfDestructCode: " + sourceCode + (generation == 0 ? "" : "\n    genMission: {DAILY_RESEARCH_WAVE: 1}\n    genMissionFreq: " + (generation is 1 or 3 ? 100 : 0) + "\n    genMissionLimit: 1\n    baseDetectionRange: " + (generation >= 2 ? 100 : 0) + "\n    baseDetectionChance: 100\n    huntMissionMaxFrequency: 60\n    huntMissionWeights: {0: {DAILY_RESEARCH_WAVE: 1}}"))}}}
            ufoTrajectories:
              - {id: DAILY_AIR, waypoints: [[4, 3, 100], [5, 2, 80], [4, 3, 100]]}
            alienMissions:
              - type: DAILY_RESEARCH_WAVE
                interruptResearch: {{{interrupt}}}
                objective: {{{(kind == 3 ? 2 : kind == 4 ? 1 : kind == 5 ? 3 : 0)}}}
                spawnZone: 5
                points: 5
                endlessInfiltration: false
                showAlienBase: true
                operationType: {{{operation}}}
                operationBaseType: {{{(operation == 0 ? "" : "OPTIONAL_OPERATION_BASE")}}}
                siteType: {{{(kind == 5 ? "DEPLOY_DAILY_SITE" : "DEPLOY_DAILY_BASE")}}}
                waves:
                  - {{{wave}}}
            {{{(kind == 2 ? "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}" : "")}}}
            """);
    }
}
