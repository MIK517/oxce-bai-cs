using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldEscortEnduranceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EscortRingsComposeSeventyTwoHoursAcrossBatchesAndReloads(bool cache, bool reverse)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache);
        var initial = Ring(content, 3, reverse);
        var native = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "escortMovement")
            .Where(row => row[0].GetInt32() == 1 && row[1].GetInt32() == (reverse ? 5 : 0)))
        {
            AdvanceOne(native);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { 1, reverse ? 5 : 0, row[2].GetInt32() }
                .Concat(native.Capture().World.Ufos.OrderBy(ufo => ufo.UniqueId).SelectMany(ufo => new object[]
                {
                    decimal.Parse(ufo.Longitude.ToString("F12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                    decimal.Parse(ufo.Latitude.ToString("F12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                })).ToArray());
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
        }
        var stepped = Run(1, false);
        Assert.Equivalent(stepped, Run(120, false), strict: true);
        Assert.Equivalent(stepped, Run(120, true), strict: true);

        CampaignSnapshot Run(int batch, bool reload)
        {
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            var ticks = 0;
            while (ticks < 72 * 720)
            {
                var result = campaign.Execute(new AdvanceCampaignTime(Math.Min(batch, 72 * 720 - ticks)));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var advanced = Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount;
                Assert.InRange(advanced, 1, batch);
                ticks += advanced;
                if (ticks % 720 != 0) continue;
                var current = campaign.Capture();
                Assert.Equal(3, current.World.Ufos.Count);
                Assert.Equal(3, Assert.Single(current.World.Missions).LiveUfos);
                Assert.All(current.World.Ufos, ufo =>
                {
                    Assert.True(ufo.Escorting);
                    Assert.Equal(UfoStatus.Flying, ufo.Status);
                    Assert.Contains(current.World.Ufos, target => target.UniqueId == ufo.Destination!.UniqueId);
                });
                if (reload) campaign = Reload(content, campaign, 81, "escort-endurance.sav");
            }
            var after = campaign.Capture();
            Assert.Equal(initial.DaysPassed + 3, after.DaysPassed);
            Assert.Equal(initial.World.Ufos.Select(ufo => ufo.UniqueId), after.World.Ufos.Select(ufo => ufo.UniqueId));
            return after;
        }
    }

    private static CampaignSnapshot Ring(RuntimeContent content, int count, bool reverse = false)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 0, 0), "HUNTER", 2200).Capture();
        var actors = Enumerable.Range(0, count).Select(index => initial.World.Ufos[0] with
        {
            UniqueId = 9 + index,
            Id = 3 + index,
            HunterKiller = false,
            Escorting = true,
            OriginalWaypoint = new(1, 0.6),
            Longitude = 0.4 + index * 0.1 / (count / 3.0),
            Latitude = 0.2 + index * 0.05 / (count / 3.0),
            Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType,
                3 + (index == 0 ? count - 1 : index - 1), 0, 0)
            { UniqueId = 9 + (index == 0 ? count - 1 : index - 1) },
        }).ToArray();
        return initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [], Soldiers = [], Research = [], Transfers = [], Productions = [] }],
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = count }],
                Ufos = reverse ? actors.Reverse().ToArray() : actors,
            },
        };
    }
}
