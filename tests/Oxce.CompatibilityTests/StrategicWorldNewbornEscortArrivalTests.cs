using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornEscortArrivalTests
{
    private static readonly string[] ResearchDiaryRules = ["CORE", "UNLOCK"];

    [Theory]
    [InlineData(false, 50, false)]
    [InlineData(true, 50, false)]
    [InlineData(false, 100, false)]
    [InlineData(true, 100, false)]
    [InlineData(false, 50, true)]
    [InlineData(true, 50, true)]
    [InlineData(false, 100, true)]
    [InlineData(true, 100, true)]
    public void EscortsChooseTheFirstOrdinaryLeaderOrAdvanceWithoutOne(bool cache, int percentage, bool savedLeader)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, false, hunterPercentage: percentage, escort: true);
        HashSet<int> patterns = [];
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed);
            initial = initial with { Bases = [initial.Bases[0] with { Crafts = [], Soldiers = [] }] };
            if (savedLeader)
                initial = initial with
                {
                    World = initial.World with
                    {
                        Missions = [initial.World.Missions[0] with { LiveUfos = 1 }],
                        Ufos = [new(9, "FIRST_ARRIVAL_UFO", 4, "FIRST_ARRIVAL_AIR", 1,
                            Degrees(8) + WorldGeometry.Nautical(50), Degrees(2), UfoStatus.Flying, WorldAltitudes.High)
                        {
                            Id = 9, Shield = 10, MissionWaveNumber = 0,
                            Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)),
                        }],
                    },
                };
            var random = new SplitMix64RandomSource(seed);
            List<(bool Hunter, int Mode, int Behavior)> choices = [];
            var pattern = 0;
            for (var wave = 0; wave < 3; wave++)
            {
                var hunter = random.NextInclusive(0, 99) < percentage;
                choices.Add((hunter, hunter ? random.NextInclusive(0, 1) : 0, hunter ? random.NextInclusive(0, 1) : 0));
                if (hunter) pattern |= 1 << wave;
                for (var draw = 0; draw < 6; draw++) random.NextUnit();
                if (wave < 2) random.NextUnit();
            }
            var ordinaryCount = choices.Count(choice => !choice.Hunter);
            var arrivals = !savedLeader && ordinaryCount == 0 ? 3 : ordinaryCount;
            for (var draw = 0; draw < 3 + (savedLeader ? 1 : 0) + arrivals * 6; draw++) random.NextUnit();
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(random.State, after.RandomState);
            Assert.Equal(savedLeader ? 4 : 3, after.World.Ufos.Count);
            var leader = after.World.Ufos.FirstOrDefault(ufo => !ufo.HunterKiller);
            for (var wave = 0; wave < 3; wave++)
            {
                var born = after.World.Ufos[wave + (savedLeader ? 1 : 0)];
                var choice = choices[wave];
                var attaches = choice.Hunter && leader is not null;
                Assert.Equal(choice, (born.HunterKiller, born.HuntMode, born.HuntBehavior));
                Assert.Equal(choice.Hunter, born.Escort);
                Assert.Equal(attaches, born.Escorting);
                Assert.False(born.Hunting);
                Assert.Equal(attaches ? 0 : 1, born.TrajectoryPoint);
                Assert.Equal(attaches ? 2200 : 1760, born.Speed);
                Assert.Equal(10, born.Shield);
                if (attaches)
                {
                    Assert.Equal(leader!.UniqueId, born.Destination!.UniqueId);
                    Assert.Equal(WorldTargetKind.Ufo, born.Destination.Kind);
                    Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), born.OriginalWaypoint);
                }
                else
                {
                    Assert.Null(born.OriginalWaypoint);
                    Assert.Equal(WorldTargetKind.Waypoint, born.Destination!.Kind);
                }
            }
            patterns.Add(pattern);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "escort-first-arrival.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            Assert.All(campaign.Capture().World.Ufos.Where(ufo => !savedLeader || ufo.UniqueId != 9),
                ufo => Assert.Equal(ufo.Escorting ? 0 : 2, ufo.TrajectoryPoint));
        }
        Assert.Equal(percentage == 100 ? 1 : 8, patterns.Count);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void SiteResearchComposesWithNoTargetAndSafeEscortHuntOutcomes(bool cache, bool hunt, bool escort)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = ResearchContent(cache, escort, "OTHER");
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var initial = ResearchSnapshot(content, seed, hunt);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content,
                new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            var after = campaign.Capture();
            Assert.Equal(3, after.World.Ufos.Count);
            Assert.Equivalent(control.Capture().World.Ufos, after.World.Ufos, strict: true);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            Assert.Contains("CORE", after.CompletedResearch);
            Assert.Contains("UNLOCK", after.CompletedResearch);
            Assert.Equal(ResearchDiaryRules, after.ResearchDiary.Select(entry => entry.RuleId));
            Assert.Equal(3, after.ResearchDiary[0].SourceType);
            Assert.Equal("FIRST_ESCORT_SITE", after.ResearchDiary[0].SourceName);
            Assert.Equal(2, after.ResearchDiary[1].SourceType);
            Assert.Equal("CORE", after.ResearchDiary[1].SourceName);
            Assert.Empty(after.World.MissionSites);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "escort-arrival-site-research.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture().World.Ufos, campaign.Capture().World.Ufos, strict: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DeferredHuntingProofCannotHideInterruptionOrAttractionFailure(bool cache, bool malformed)
    {
        var content = ResearchContent(cache, true, malformed ? "OTHER" : "CORE",
            malformed ? "  - {type: SHIP, soldiers: 2147483647}\n" : "");
        AssertTimeBlocked(CampaignState.Restore(ResearchSnapshot(content, 17, false), content, new SplitMix64RandomSource(17)),
            malformed ? "Craft hunter attraction exceeds the supported range." : "Alien mission wave spawning requires world simulation.");
    }

    private static RuntimeContent ResearchContent(bool cache, bool escort, string interrupt, string crafts = "") =>
        StrategicWorldNewbornFirstArrivalTests.Content(cache, false, interrupt, hunterPercentage: 50, escort: escort,
            radarRange: 300, extraRules: $$$"""
            research:
              - {name: CORE, cost: 1}
              - {name: OTHER, cost: 1}
            alienDeployments:
              - {type: FIRST_ESCORT_SITE, markerName: FIRST_ESCORT_SITE, duration: [0, 0], points: 0, unlockedResearchOnDespawn: CORE}
            {{{(crafts.Length == 0 ? "" : "crafts:\n" + crafts)}}}
            """);

    private static CampaignSnapshot ResearchSnapshot(RuntimeContent content, ulong seed, bool hunt)
    {
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Speed = 0,
                Fuel = 300,
                Longitude = hunt ? Degrees(8) + WorldGeometry.Nautical(100) : Degrees(20),
                Latitude = Degrees(2),
                Destination = WorldTargetReference.ForWaypoint(5, new(Degrees(30), Degrees(2))),
            },
        });
        return initial with { World = initial.World with { MissionSites = [new(7, "MISSION_SITE", "FIRST_ESCORT_SITE", "RACE_A", Degrees(8), Degrees(2), 0)] } };
    }
}
