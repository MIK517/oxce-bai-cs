using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldUfoShieldTests
{
    [Fact]
    public void RechargeAndRandomDrawsMatchTheExtractedFlyingBranch()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-ufo-shield");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var random = new ShieldRoll(row[3].GetInt32());
            var shield = WorldUfoShields.Recharge(row[2].GetInt32(), row[0].GetInt32(), row[1].GetInt32(), random);
            int[] actual = [row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32(), shield, random.Calls];
            TestFixtures.AssertJsonEqual(row, JsonSerializer.SerializeToUtf8Bytes(actual));
        }
    }

    [Fact]
    public void RaceBonusesRechargeInFlightAcrossSaveAndBatchBoundaries()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 1, 0),
            "UFO_SHIELD_TEST").Capture();
        snapshot = snapshot with
        {
            RandomState = 17,
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { Race = "RACE_B" }],
                Ufos = [snapshot.World.Ufos[0] with { Shield = 3 }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var batch = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var random = new SplitMix64RandomSource(17);
        var expected = 3;
        for (var tick = 0; tick < 12; tick++)
        {
            if (expected < 15) expected = Math.Min(15, expected + 1 + (random.NextInclusive(0, 99) < 50 ? 1 : 0));
            AdvanceOne(campaign);
            Assert.Equal(expected, campaign.Capture().World.Ufos[0].Shield);
            Assert.Equal(random.State, campaign.Capture().RandomState);
            campaign = Reload(content, campaign, 17, "ufo-shield.sav");
        }
        AdvanceUnblocked(batch, 12);
        Assert.Equivalent(batch.Capture(), campaign.Capture(), strict: true);
    }

    [Fact]
    public void LandedAndTakeoffTicksDoNotRecharge()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateLifecycleSnapshot(content);
        var campaign = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [snapshot.World.Ufos[0] with
                {
                    RuleId = "UFO_SHIELD_TEST", Shield = 3,
                    Destination = WorldTargetReference.ForWaypoint(0, new(0.6, 0.2)),
                }],
            },
        }, content, new SplitMix64RandomSource(17));
        AdvanceOne(campaign);
        Assert.Equal(3, campaign.Capture().World.Ufos[0].Shield);
        AdvanceOne(campaign);
        Assert.Equal(UfoStatus.Flying, campaign.Capture().World.Ufos[0].Status);
        Assert.Equal(3, campaign.Capture().World.Ufos[0].Shield);
        campaign = Reload(content, campaign, 17, "ufo-shield-takeoff.sav");
        AdvanceOne(campaign);
        Assert.InRange(campaign.Capture().World.Ufos[0].Shield, 4, 5);
    }

    [Fact]
    public void OrdinaryWaveCanSpawnShieldedUfoAndInitializeItInTheSameTick()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Shield wave");
        var campaign = CampaignState.Restore(snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 0, 29, 55),
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_SPAWN_SHIELD", "REGION", "RACE_B", 0, 0, 30, 0, -1)],
            },
        }, content, new SplitMix64RandomSource(17));
        AdvanceOne(campaign);
        Assert.Equal(15, Assert.Single(campaign.Capture().World.Ufos).Shield);
        Reload(content, campaign, 17, "shield-spawn.sav");
    }

    private sealed class ShieldRoll(int roll) : IRandomSource
    {
        public int Calls { get; private set; }
        public int NextInclusive(int minimum, int maximum)
        {
            Assert.Equal(0, minimum);
            Assert.Equal(99, maximum);
            Calls++;
            return roll;
        }
        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException();
        public double NextUnit() => throw new InvalidOperationException();
    }
}
