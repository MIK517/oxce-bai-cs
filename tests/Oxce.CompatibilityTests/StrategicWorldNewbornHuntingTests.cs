using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornHuntingTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    public void ZeroRadarHuntersAndEscortsSpawnAlongsideActiveCraft(bool cache, bool escort, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "selectionRows"), row =>
            row[0].GetInt32() == mode && row[1].GetInt32() == 5 && row[2].GetInt32() == 0);
        var content = Load(cache, escort, mode);
        var initial = Snapshot(content, escort);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "zero-radar-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var born = Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER");
        Assert.True(born.HunterKiller);
        Assert.False(born.Hunting);
        Assert.Equal(mode, born.HuntMode);
        Assert.Equal(escort, born.Escorting);
        Assert.Equal(escort ? 2 : 1, Assert.Single(after.World.Missions).LiveUfos);
        if (escort)
        {
            var leader = after.World.Ufos.Single(ufo => ufo.UniqueId == 9);
            Assert.Equal(leader.UniqueId, born.Destination!.UniqueId);
            Assert.Equal(leader.Position, born.Position);
            Assert.NotNull(born.OriginalWaypoint);
        }
        else Assert.Equal(WorldTargetKind.Waypoint, born.Destination!.Kind);
        campaign = Reload(content, campaign, 81, "zero-radar-after.sav");
        AdvanceOne(campaign);
        Assert.False(campaign.Capture().World.Ufos.Single(ufo => ufo.UniqueId == born.UniqueId).Hunting);
        Reload(content, campaign, 81, "zero-radar-following.sav");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void ZeroRadarNewbornsRejectMalformedAttractionBeforeBirth(bool cache, int mode)
    {
        var content = Load(cache, false, mode, "crafts: [{type: SHIP, soldiers: 2147483647}]\n");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, false), content, new SplitMix64RandomSource(81)),
            "Craft hunter attraction exceeds the supported range.");
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    public void PendingCraftJoinsNewbornCandidatesOnlyAtItsHourlyArrival(bool cache, bool hourly, int hours)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "incomingCraftRows"), row => row[0].GetInt32() == hours);
        foreach (var escort in new[] { false, true })
        {
            var content = Load(cache, escort, 0, radar: 300);
            var initial = Snapshot(content, escort);
            var owner = initial.Bases[0];
            var incoming = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
            incoming = incoming with
            {
                Logistics = incoming.Logistics! with
                { IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3, Weapons = [] }
            };
            initial = initial with
            {
                Time = new(1, 1, 1, 1999, 1, hourly ? 59 : 29, 55),
                Bases = [owner with { Crafts = [], Soldiers = [],
                    Transfers = [new(1, hours, CampaignTransferKind.Craft, "SHIP", 1, Craft: incoming)] }],
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "pending-newborn-before.sav");
            if (hourly && hours == 1)
            {
                AssertTimeBlocked(campaign, escort ? "New UFO escorts require craft retarget forecasts." :
                    "Alien mission wave spawning requires world simulation.");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.Bases[0].Crafts);
            Assert.Equal(hours - (hourly ? 1 : 0), Assert.Single(after.Bases[0].Transfers).Hours);
            Assert.False(Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER").Hunting);
            campaign = Reload(content, campaign, 81, "pending-newborn-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "pending-newborn-following.sav");
        }
    }

    private static RuntimeContent Load(bool cache, bool escort, int mode, string extra = "", int radar = 0) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, $$"""
            ufos:
              - type: ZERO_RADAR_HUNTER
                speedMax: 2200
                damageMax: 50
                radarRange: {{radar}}
                hunterKillerPercentage: 100
                huntMode: {{mode}}
            alienMissions:
              - type: ZERO_RADAR_MISSION
                objective: 0
                waves:
                  - {ufo: ZERO_RADAR_HUNTER, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 9000, escort: {{escort.ToString().ToLowerInvariant()}}}
            {{extra}}
            """);

    private static CampaignSnapshot Snapshot(RuntimeContent content, bool escort)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 29, 55), "UFO_SCOUT", 900).Capture();
        return initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_OUT", Fuel = 300, Speed = 0, Destination = null }
        }) with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RuleId = "ZERO_RADAR_MISSION", NextWave = 0, SpawnCountdown = 0, LiveUfos = escort ? 1 : 0 }],
                Ufos = escort ? initial.World.Ufos : [],
            },
        };
    }
}
