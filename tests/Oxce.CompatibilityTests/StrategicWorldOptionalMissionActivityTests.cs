using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalMissionActivityTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, -1)]
    [InlineData(true, 1)]
    [InlineData(true, -1)]
    public void OptionalDailyWaveAwardCannotMaskLaterSiteOverflow(bool direct, int sign)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        var content = Content(direct, sign);
        var initial = Snapshot(content, direct, sign, safe: false);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, -1)]
    [InlineData(true, 1)]
    [InlineData(true, -1)]
    public void SafeOptionalWaveAwardsMatchSelectedControlsAndReloadAtBothLimits(bool direct, int sign)
    {
        var content = Content(direct, sign);
        var interruptedContent = Content(direct, sign, "CORE");
        var continuedContent = Content(direct, sign, "");
        var initial = Snapshot(content, direct, sign, safe: true);
        var interruptedSeen = false;
        var continuedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var selected = initial with { RandomState = seed };
            var campaign = CampaignState.Restore(selected, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var interrupted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] - 5 * sign + (interrupted ? 0 : 7 * sign), after.Regions[0].ActivityAlien[^1]);
            var control = CampaignState.Restore(selected, interrupted ? interruptedContent : continuedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "optional-mission-activity-limit.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            interruptedSeen |= interrupted;
            continuedSeen |= !interrupted;
        }
        Assert.True(interruptedSeen);
        Assert.True(continuedSeen);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, -1)]
    [InlineData(true, 1)]
    [InlineData(true, -1)]
    public void OptionalEarthSourceInterruptionCannotMaskLaterSiteOverflow(bool direct, int sign)
    {
        var content = Content(direct, sign, interrupt: "", source: true);
        var initial = Snapshot(content, direct, sign, safe: false);
        initial = initial with
        {
            World = initial.World with
            {
                AlienBases = [new(6, "OPTIONAL_ACTIVITY_BASE", "RACE_A", 0.2, 0.1)],
                Missions = [.. initial.World.Missions.Select(mission => mission.Id == 4 ? mission with
                { AlienBase = new(WorldTargetKind.AlienBase, "OPTIONAL_ACTIVITY_SOURCE", 6, 0.2, 0.1) } : mission)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO alien activity exceeds the supported range.");
    }

    private static RuntimeContent Content(bool direct, int sign, string interrupt = "BONUS", bool source = false) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
        research:
          - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
          - {name: BONUS, cost: 1}
          - {name: OTHER, cost: 1}
        ufos:
          - {type: UFO_OPTIONAL_AWARD, speedMax: 2200, damageMax: 50, missionScore: {{{7 * sign}}}}
        ufoTrajectories:
          - {id: OPTIONAL_AIR, waypoints: [[4, 3, 100], [5, 2, 80], [4, 3, 100]]}
        alienDeployments:
          - {type: OPTIONAL_AWARD_SITE, markerName: OPTIONAL_AWARD_MARKER, duration: [1, 1], points: {{{7 * sign}}}}
          - {type: LATER_AWARD_SITE, markerName: LATER_AWARD_MARKER, duration: [1, 1], points: {{{-5 * sign}}}}
        {{{(source ? "  - {type: OPTIONAL_ACTIVITY_BASE, markerName: OPTIONAL_ACTIVITY_SOURCE, baseSelfDestructCode: BONUS}" : "")}}}
        alienMissions:
          - type: OPTIONAL_AWARD_MISSION
            interruptResearch: {{{interrupt}}}
            objective: {{{(direct ? 3 : 0)}}}
            operationType: {{{(source ? 1 : 0)}}}
            operationBaseType: {{{(source ? "OPTIONAL_ACTIVITY_BASE" : "")}}}
            spawnZone: 5
            waves: [{ufo: {{{(direct ? "OPTIONAL_AWARD_SITE" : "UFO_OPTIONAL_AWARD")}}}, count: 1, trajectory: OPTIONAL_AIR, timer: 9000}]
          - type: LATER_AWARD_MISSION
            objective: 3
            spawnZone: 5
            waves: [{ufo: LATER_AWARD_SITE, count: 1, trajectory: OPTIONAL_AIR, timer: 9000}]
        """);

    private static CampaignSnapshot Snapshot(RuntimeContent content, bool direct, int sign, bool safe)
    {
        var initial = CreateSnapshot(content, "Optional mission activity");
        var margin = safe ? 5 : 3;
        return initial.WithNextId("OPTIONAL_AWARD_MARKER", 70).WithNextId("LATER_AWARD_MARKER", 70) with
        {
            RandomState = 17,
            Time = new(2, 2, 1, 1999, 23, 59, 55),
            Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)] }],
            Regions = [.. initial.Regions.Select(region => region with { ActivityAlien = [sign > 0 ? int.MinValue + margin : int.MaxValue - margin] })],
            World = initial.World with
            {
                Missions = direct ? [new(4, "OPTIONAL_AWARD_MISSION", "REGION", "RACE_A", 0, 0, 0, 0, -1),
                    new(5, "LATER_AWARD_MISSION", "REGION", "RACE_A", 0, 0, 0, 0, -1)] :
                    [new(4, "OPTIONAL_AWARD_MISSION", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
                MissionSites = direct ? [] : [new(12, "MISSION_SITE", "LATER_AWARD_SITE", "RACE_A", 0.2, 0.1, 3600) { Detected = true }],
            },
        };
    }
}
