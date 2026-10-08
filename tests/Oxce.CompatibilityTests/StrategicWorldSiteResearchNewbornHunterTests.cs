using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchNewbornHunterTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void SavedTargetlessHuntersComposeBeforeSiteInterruption(bool cache, int mode)
    {
        using var hunting = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        using var lifecycle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        Assert.Contains(TestFixtures.Rows(hunting.RootElement, "constructorRows"), row =>
            row[0].GetInt32() == 100 && row[1].GetInt32() == mode && row[2].GetInt32() == 0 && row[4].GetBoolean());
        var row = Assert.Single(TestFixtures.Rows(lifecycle.RootElement, "spawnPathRows"), row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 0 && row[2].GetInt32() == 0);
        var content = Content(cache, mode);
        var initial = StrategicWorldSiteResearchSpawnTests.Snapshot(content);
        var random = new SplitMix64RandomSource(initial.RandomState);
        random.NextInclusive(0, 99);
        var expectedMode = mode > 1 ? random.NextInclusive(0, 1) : mode;
        for (var draw = 0; draw < row[10].GetInt32(); draw++) random.NextInclusive(0, 0);
        var countdown = WorldTrajectory.SpawnCountdown(9000, random);
        random.NextInclusive(0, 99);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Empty(result.Events.OfType<UfoStartedHunting>());
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.True(ufo.HunterKiller);
        Assert.False(ufo.Hunting);
        Assert.False(ufo.Escorting);
        Assert.Null(ufo.OriginalWaypoint);
        Assert.Equal(expectedMode, ufo.HuntMode);
        Assert.Equal(WorldTargetKind.Waypoint, ufo.Destination!.Kind);
        Assert.Equal(UfoStatus.Flying, ufo.Status);
        var position = WorldGeometry.Move(StrategicWorldSiteResearchSpawnTests.Position(row, 6),
            StrategicWorldSiteResearchSpawnTests.Position(row, 8), WorldGeometry.RadianSpeed(row[5].GetInt32()));
        Assert.InRange(WorldGeometry.Distance(position, ufo.Position), 0, 1e-8);
        var mission = Assert.Single(after.World.Missions);
        Assert.True(mission.Interrupted);
        Assert.Equal((1, 0, 1, countdown), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos, mission.SpawnCountdown));
        Assert.Equal(random.State, after.RandomState);
        Assert.Empty(after.World.MissionSites);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-research-targetless-newborn.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false, "PATH_1_0")]
    [InlineData(true, "PATH_1_0")]
    [InlineData(false, "PATH_SHORT")]
    [InlineData(true, "PATH_SHORT")]
    public void GroundedHunterBirthDefersRetargetingUntilAfterItsOwnedLift(bool cache, string path)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = Content(cache, path: path);
        var initial = StrategicWorldSiteResearchSpawnTests.Snapshot(content).WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Status = "STR_OUT", Takeoff = 0, Fuel = 300, Speed = 0, Destination = null } });
        var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(17));
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(control, 1);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Empty(result.Events.OfType<UfoStartedHunting>());
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.True(ufo.HunterKiller);
        Assert.False(ufo.Hunting);
        Assert.False(ufo.Escorting);
        Assert.Equal(WorldTargetKind.Waypoint, ufo.Destination!.Kind);
        Assert.Equal(path == "PATH_SHORT" ? UfoStatus.Flying : UfoStatus.Landed, ufo.Status);
        Assert.Equal(path == "PATH_SHORT" ? 0 : 5, ufo.SecondsRemaining);
        Assert.Equivalent(control.Capture().World.Ufos, after.World.Ufos, strict: true);
        Assert.Equal(control.Capture().RandomState, after.RandomState);
        Assert.True(Assert.Single(after.World.Missions).Interrupted);
        Assert.False(Assert.Single(control.Capture().World.Missions).Interrupted);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        Assert.Empty(after.World.MissionSites);
        Assert.Equivalent(after, Reload(content, campaign, 17, "site-research-ground-hunter.sav").Capture(), strict: true);
    }

    internal static RuntimeContent Content(bool cache, int mode = 0, string path = "PATH_0_0") =>
        StrategicWorldSiteResearchSpawnTests.Content(cache, path, extra: $$"""
        ufos:
          - type: SITE_RESEARCH_HUNTER
            speedMax: 2200
            damageMax: 50
            radarRange: 600
            noAlert: true
            hunterKillerPercentage: 100
            huntMode: {{mode}}
            huntBehavior: 0
        """, ufo: "SITE_RESEARCH_HUNTER");
}
