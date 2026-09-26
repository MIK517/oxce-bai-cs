using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>Shared setup and assertions for the strategic-world compatibility tests.</summary>
internal static class StrategicWorldTestSupport
{
    /// <summary>A veteran logistics campaign with base Alpha placed at (0.2, 0.1) and month zero.</summary>
    public static CampaignSnapshot CreateSnapshot(RuntimeContent content, string name)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, name, CampaignDifficulty.Veteran);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        return campaign.Capture() with { MonthsPassed = 0 };
    }

    /// <summary>One MISSION_LANDING mission with a landed UFO_SCOUT at the start of 1 January 1999.</summary>
    public static CampaignSnapshot CreateLifecycleSnapshot(RuntimeContent content)
    {
        var snapshot = CreateSnapshot(content, "World lifecycle");
        return snapshot with
        {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 0, 0),
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_LANDING", "REGION", "RACE_A", 1, 0, 1500, 1, -1)],
                Ufos = [new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_LANDING", 2,
                    Degrees(8), Degrees(2), UfoStatus.Landed, WorldAltitudes.Ground)
                {
                    Id = 3, MissionWaveNumber = 0, Shield = 0, SecondsRemaining = 10,
                    Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(8.01), Degrees(2))),
                }],
            },
        };
    }

    /// <summary>One flying UFO at (0.4, 0.2) heading for its saved waypoint at (1.0, 0.6);
    /// the mission's next wave is the one after the UFO's own wave.</summary>
    public static CampaignState CreateUfoTransitCampaign(RuntimeContent content, CampaignTime time,
        string ufoRuleId = "UFO_HUNTER", int speed = 3200, string missionRuleId = "MISSION_SCOUT",
        string trajectoryId = "TRAJ_PATROL", int missionWaveNumber = 1)
    {
        var snapshot = CreateSnapshot(content, "UFO transit");
        var mission = new AlienMissionSnapshot(
            4, missionRuleId, "REGION", "RACE_A", missionWaveNumber + 1, 0, 1500, 1, -1);
        var ufo = new UfoSnapshot(9, ufoRuleId, 4, trajectoryId, 0,
            0.4, 0.2, UfoStatus.Flying, "STR_HIGH_UC")
        {
            Id = 3,
            Speed = speed,
            MissionWaveNumber = missionWaveNumber,
            Destination = new WorldTargetReference(WorldTargetKind.Waypoint,
                WorldTargetReference.WaypointType, 0, 1.0, 0.6),
        };
        return CampaignState.Restore(snapshot with
        {
            Time = time,
            World = snapshot.World with { Missions = [mission], Ufos = [ufo] },
        }, content, new SplitMix64RandomSource(40));
    }

    public static double Degrees(double value) => value * Math.PI / 180;

    /// <summary>Replaces every craft of <paramref name="ruleId"/> in the first base.</summary>
    public static CampaignSnapshot WithCraft(this CampaignSnapshot snapshot, string ruleId,
        Func<CraftSnapshot, CraftSnapshot> change)
    {
        var owner = snapshot.Bases[0];
        Assert.Contains(owner.Crafts, craft => craft.RuleId == ruleId);
        return snapshot with
        {
            Bases = [owner with
            {
                Crafts = [.. owner.Crafts.Select(craft => craft.RuleId == ruleId ? change(craft) : craft)],
            }, .. snapshot.Bases.Skip(1)],
        };
    }

    /// <summary>Adds a facility at the origin of the first base.</summary>
    public static CampaignSnapshot WithFacility(this CampaignSnapshot snapshot, string ruleId,
        int buildTime = 0, bool disabled = false)
    {
        var owner = snapshot.Bases[0];
        return snapshot with
        {
            Bases = [owner with
            {
                Facilities = [.. owner.Facilities, new FacilitySnapshot(ruleId, 1, 0, buildTime, 0, false, disabled, false)],
            }, .. snapshot.Bases.Skip(1)],
        };
    }

    public static CampaignSnapshot WithNextId(this CampaignSnapshot snapshot, string key, int value)
    {
        var ids = snapshot.NextIds.ToDictionary(static pair => pair.Key, static pair => pair.Value,
            StringComparer.Ordinal);
        ids[key] = value;
        return snapshot with { NextIds = ids };
    }

    /// <summary>Advances exactly one tick and asserts that it produced no notification.</summary>
    public static void AdvanceOne(CampaignState campaign) => Assert.Equal(1,
        Assert.IsType<CampaignTimeAdvanced>(Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events))
            .Summary.TickCount);

    /// <summary>Advances <paramref name="ticks"/> ticks, resuming after pausing notifications.</summary>
    public static void AdvanceUnblocked(CampaignState campaign, int ticks)
    {
        while (ticks > 0)
        {
            var result = campaign.Execute(new AdvanceCampaignTime(ticks));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var advanced = Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount;
            Assert.InRange(advanced, 1, ticks);
            ticks -= advanced;
        }
    }

    /// <summary>Asserts that the next tick is refused with <paramref name="reason"/> and changes nothing.</summary>
    public static void AssertTimeBlocked(CampaignState campaign, string reason)
    {
        var before = campaign.Capture();
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(reason, Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    /// <summary>Saves, reloads and asserts that the loaded campaign is equivalent.</summary>
    public static CampaignState Reload(RuntimeContent content, CampaignState campaign, ulong seed, string name)
    {
        var snapshot = campaign.Capture();
        var restored = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(snapshot), content,
            seed, name).Campaign;
        Assert.Equivalent(snapshot, restored.Capture(), strict: true);
        return restored;
    }

    /// <summary>Managed bytes allocated on this thread by <paramref name="action"/>.</summary>
    public static long AllocatedBytes(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
