using System.Globalization;
using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldLifecycleTraceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LifecycleMatchesCapturedCppTransitionsAcrossReloadsAndBatches(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul", fromCache);
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        foreach (var scenario in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var name = scenario.GetProperty("name").GetString()!;
            var initial = Initial(content, name);
            var campaign = Restore(content, initial);
            foreach (var expected in TestFixtures.Rows(scenario, "rows"))
            {
                var tick = expected[0].GetInt32();
                if (name == "interrupted" && tick == 2) campaign = Interrupt(content, campaign);
                var before = campaign.Capture();
                if (before.World.Ufos.Any(ufo => ufo.SecondsRemaining is 5 or 10 ||
                    ufo.Status == UfoStatus.Flying && WorldGeometry.Distance(ufo.Position, ufo.Destination!.Position)
                        <= WorldGeometry.RadianSpeed(ufo.Speed)) || before.Time.Second == 55)
                    campaign = Reload(content, campaign);
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var mission = after.World.Missions.SingleOrDefault();
                object[] actual = [tick, mission?.NextWave ?? -1, mission?.NextUfoCounter ?? -1,
                    mission?.SpawnCountdown ?? -1, mission?.LiveUfos ?? -1, after.Regions[0].ActivityAlien[^1],
                    result.Events.Any(item => item is UfoContactDetected or UfoLanded) ? 1 : 0, after.RandomState,
                    after.World.Ufos.Select(ufo => new object[] { ufo.UniqueId, (int)ufo.Status, ufo.TrajectoryPoint,
                        ufo.Altitude, Fixed(ufo.Longitude), Fixed(ufo.Latitude), Fixed(ufo.Destination!.Longitude),
                        Fixed(ufo.Destination.Latitude), ufo.Speed, ufo.SecondsRemaining, ufo.Id, ufo.LandId,
                        ufo.Detected ? 1 : 0 }).ToArray()];
                Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(expected.GetRawText())),
                    CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
                if (!before.World.Ufos.Select(ufo => (ufo.Status, ufo.TrajectoryPoint))
                    .SequenceEqual(after.World.Ufos.Select(ufo => (ufo.Status, ufo.TrajectoryPoint))) ||
                    before.World.Missions.Count != after.World.Missions.Count)
                    campaign = Reload(content, campaign);
            }
            var batched = Restore(content, initial);
            var remaining = 721;
            if (name == "interrupted")
            {
                batched.Execute(new AdvanceCampaignTime(1));
                batched = Interrupt(content, batched);
                remaining--;
            }
            while (remaining > 0)
            {
                var result = batched.Execute(new AdvanceCampaignTime(remaining));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var count = Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount;
                Assert.InRange(count, 1, remaining);
                remaining -= count;
            }
            Assert.Equivalent(campaign.Capture(), batched.Capture(), strict: true);
            Assert.Empty(batched.Capture().World.Ufos);
            Assert.Empty(batched.Capture().World.Missions);
        }
    }

    private static CampaignSnapshot Initial(RuntimeContent content, string name)
    {
        var snapshot = TestFixtures.CreateWorldLifecycleSnapshot(content);
        var multiple = name == "multiple";
        var mission = snapshot.World.Missions[0] with
        {
            RuleId = name == "long" ? "MISSION_LONG_LANDING" : "MISSION_LANDING",
            NextWave = multiple ? 1 : 0,
            LiveUfos = multiple ? 2 : 0,
            SpawnCountdown = 30
        };
        var ufo = snapshot.World.Ufos[0] with
        {
            Id = 0,
            Status = UfoStatus.Flying,
            Altitude = WorldAltitudes.High,
            Speed = 2200,
            Longitude = 10 * Math.PI / 180,
            Latitude = 8 * Math.PI / 180,
            TrajectoryPoint = 0,
            SecondsRemaining = 0,
            Destination = WorldTargetReference.ForWaypoint(0, new(8 * Math.PI / 180, 2 * Math.PI / 180)),
        };
        return snapshot with
        {
            RandomState = multiple ? 12UL : 0,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Options = snapshot.Options with { AggressiveRetaliation = false, UfoLandingAlert = true },
            NextIds = new Dictionary<string, int>(snapshot.NextIds)
            { ["STR_UFO_UNIQUE"] = multiple ? 11 : 9, ["STR_UFO"] = 11, ["STR_LANDING_SITE"] = 40 },
            Bases = [snapshot.Bases[0] with { Crafts = [], Soldiers = [], Facilities = [.. snapshot.Bases[0].Facilities,
                new FacilitySnapshot("RADAR_HYPER_TEST", 1, 0, 0, 0, false, false, false)] }],
            World = snapshot.World with { Missions = [mission], Ufos = multiple ? [ufo, ufo with { UniqueId = 10 }] : [] },
        };
    }

    private static CampaignState Interrupt(RuntimeContent content, CampaignState campaign)
    {
        var snapshot = campaign.Capture();
        return Restore(content, snapshot with
        {
            World = snapshot.World with
            { Missions = [snapshot.World.Missions[0] with { Interrupted = true }] }
        });
    }

    private static CampaignState Reload(RuntimeContent content, CampaignState campaign)
    {
        var snapshot = campaign.Capture();
        var parsed = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(snapshot), content,
            seed: 17, name: "lifecycle-trace.sav").Campaign.Capture();
        Assert.Equivalent(snapshot, parsed, strict: true);
        return Restore(content, parsed);
    }

    private static CampaignState Restore(RuntimeContent content, CampaignSnapshot snapshot) =>
        CampaignState.Restore(snapshot, content, new MinimumChoices());

    private static string Fixed(double value) => value.ToString("F9", CultureInfo.InvariantCulture);

    // Controlled choices, not C++ RNG stream emulation. State is the number of choices,
    // so complete snapshot comparisons detect rerolls or changed consumption on reload.
    private sealed class MinimumChoices : IStatefulRandomSource
    {
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextExclusive(int maximum) { Assert.True(maximum > 0); State++; return 0; }
        public int NextInclusive(int minimum, int maximum) { Assert.True(minimum <= maximum); State++; return minimum; }
        public double NextUnit() { State++; return 0; }
    }
}
