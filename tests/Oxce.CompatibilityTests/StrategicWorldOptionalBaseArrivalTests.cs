using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseArrivalTests
{
    [Fact]
    public void OptionalArrivalComposesWithOrderedEscortsAndHunterCancellation()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var takeoff in new[] { 0, 5 })
            foreach (var leaderFirst in new[] { false, true })
                foreach (var itemFuel in new[] { false, true })
                {
                    var content = StrategicWorldOptionalBaseCraftTests.Content(itemFuel: itemFuel, speedMaximum: 2100, escortSpeedMaximum: 2100);
                    var deletedContent = StrategicWorldOptionalBaseCraftTests.Content("CORE", itemFuel, speedMaximum: 2100, escortSpeedMaximum: 2100);
                    var survivedContent = StrategicWorldOptionalBaseCraftTests.Content("", itemFuel, speedMaximum: 2100, escortSpeedMaximum: 2100);
                    HashSet<bool> outcomes = [];
                    for (ulong seed = 1; seed <= 32; seed++)
                    {
                        var initial = StrategicWorldOptionalBaseAirborneCraftTests.Snapshot(content, seed, takeoff, false, true)
                            .WithCraft("SHIP", craft => craft with
                            {
                                Logistics = craft.Logistics! with { Longitude = 0.4, Latitude = 0.2, Fuel = 1000, Speed = 1000 },
                            });
                        var owner = initial.Bases[0];
                        var leader = owner.Crafts[0];
                        var follower = leader with
                        {
                            RuleId = "OPTIONAL_NORMAL_ESCORT",
                            Id = 2,
                            Logistics = leader.Logistics! with
                            {
                                Speed = 2100,
                                Takeoff = 0,
                                MissionComplete = true,
                                Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.4, 0.2),
                            },
                        };
                        initial = initial with
                        {
                            Bases = [owner with { Longitude = 0.4, Latitude = 0.2, Crafts = leaderFirst ? [leader, follower] : [follower, leader] }],
                        };
                        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                        AdvanceUnblocked(campaign, 1);
                        var after = campaign.Capture();
                        var deleted = after.CompletedResearch.Contains("BONUS");
                        outcomes.Add(deleted);
                        var lead = after.Bases[0].Crafts.Single(craft => craft.Id == 1).Logistics!;
                        var escort = after.Bases[0].Crafts.Single(craft => craft.Id == 2).Logistics!;
                        var hunter = Assert.Single(after.World.Ufos);
                        var expected = TestFixtures.Rows(oracle.RootElement, "optionalCraftArrivalRows").Single(row =>
                            row[0].GetBoolean() == deleted && row[1].GetInt32() == takeoff &&
                            row[2].GetBoolean() == leaderFirst && row[3].GetBoolean() == itemFuel);
                        var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                        {
                    deleted, takeoff, leaderFirst, itemFuel,
                    lead.Fuel, lead.Status, lead.Speed, lead.Takeoff, DestinationKind(lead),
                    escort.Fuel, escort.Status, escort.Speed, escort.Takeoff, DestinationKind(escort),
                    hunter.Hunting, hunter.OriginalWaypoint is not null,
                        });
                        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
                        var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
                        AdvanceUnblocked(control, 1);
                        Assert.Equivalent(control.Capture(), after, strict: true);
                        for (var rewrite = 0; rewrite < 3; rewrite++)
                        {
                            campaign = Reload(content, campaign, seed, "optional-base-arrival.sav");
                            Assert.Equivalent(after, campaign.Capture(), strict: true);
                        }
                        AdvanceUnblocked(campaign, 1);
                        AdvanceUnblocked(control, 1);
                        Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
                    }
                    Assert.Equal(2, outcomes.Count);
                }
    }

    [Fact]
    public void SurvivingSourceStillGuardsLandingBeforeResearch()
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(speedMaximum: 2100);
        var initial = StrategicWorldOptionalBaseAirborneCraftTests.Snapshot(content, 17, 0, false, false)
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Longitude = 0.4, Latitude = 0.2, Speed = 1000 } });
        var owner = initial.Bases[0];
        var soldier = CreateSnapshot(content, "Landing crew").Bases[0].Soldiers[0];
        initial = initial with
        {
            Bases = [owner with
            {
                Longitude = 0.4,
                Latitude = 0.2,
                Soldiers = [soldier with { Personal = soldier.Personal! with { CraftType = "SHIP", CraftId = 1 } }],
            }],
        };
        AssertLandingRequested(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)));
    }

    private static int DestinationKind(CraftLogisticsState state) => state.Destination?.Kind switch
    {
        null => 0,
        WorldTargetKind.Base => 1,
        WorldTargetKind.AlienBase => 2,
        _ => 3,
    };
}
