using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchQuantityIdentityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CommonDailyCountersAllocateNativeCraftAndSoldierBirthsDespiteVariableStocks(bool cache, bool person)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(cache, person);
        var counter = person ? "STR_SOLDIER" : "PRODUCED_CRAFT";
        foreach (var start in new[] { 1, 20, int.MaxValue - 1 })
        {
            HashSet<int> seen = [];
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var initial = Snapshot(content, seed, counter, start);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                long strictWork = 0;
                Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref strictWork, out _));
                long work = 0;
                Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var daily));
                Assert.Equal(6, daily.CounterChanges.Count);
                Assert.Equivalent(initial, campaign.Capture(), strict: true);
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
                seen.Add(choice);
                var identity = person ? Assert.Single(after.Bases[0].Transfers).Soldier!.Id : Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "PRODUCED_CRAFT").Id;
                var expected = TestFixtures.Rows(oracle.RootElement, "researchQuantityCounterBirthRows").Single(row => row[0].GetInt32() == (person ? 1 : 0) && row[1].GetInt32() == start && row[2].GetInt32() == choice);
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { person ? 1 : 0, start, choice, identity, identity, after.NextIds[counter],
                    after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.Bases[0].Transfers.Count, after.ResearchScores[^1] - initial.ResearchScores[^1] });
                Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
                Assert.Equal(1, after.NextIds["EQUIV_COUNTER"]);
                Assert.Equal("STR_OUT", after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Status);
                Assert.True(Assert.Single(after.World.Ufos).Hunting);
                Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
                for (var rewrite = 0; rewrite < 3; rewrite++)
                {
                    campaign = Reload(content, campaign, seed, "daily-variable-identity.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
            }
            Assert.Equal(2, seen.Count);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CommonCountersStillRefuseOverflowAndInsufficientBirthCapacityAtomically(bool cache, bool person)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, person);
        var counter = person ? "STR_SOLDIER" : "PRODUCED_CRAFT";
        var initial = Snapshot(content, 17, counter, int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            person ? "Research custom counters exceed the supported range." : "Craft production exhausts the craft identity range.");
        initial = initial with { Bases = [initial.Bases[0] with { Productions = [] }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Research custom counters exceed the supported range.");
        initial = Snapshot(content, 17, counter, int.MaxValue - 1);
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Productions = [initial.Bases[0].Productions[0] with { Assigned = 2, Amount = 2 }] }],
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            person ? "Soldier production exhausts the soldier identity range." : "Craft production exhausts the craft identity range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpperDailyArrivalQuantityCannotOverflowSharedStock(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchQuantityTests.Content(cache, false, int.MaxValue - 1, int.MaxValue);
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, 17, false);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Research arrivals exceed the supported stock range.");
    }

    internal static RuntimeContent Content(bool cache, bool person)
    {
        var counter = person ? "STR_SOLDIER" : "PRODUCED_CRAFT";
        return StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$"""
            crafts:
              - {type: SHIP, fuelMax: 1000, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}
              - {type: PRODUCED_CRAFT, fuelMax: 0}
            ufos: [{type: UFO_HUNTER, radarRange: 20000}]
            facilities:
              - {type: EQUIV_LAB, mapName: LOGISTICS, labs: 2}
              - {type: QUANTITY_WORKSHOP, mapName: LOGISTICS, labs: 2, workshops: 2, personnel: 20}
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, points: 1, getOneFree: [EQUIV_A, EQUIV_B], spawnedItem: SUPPLY, spawnedItemCount: 1, increaseCounter: [{{counter}}], decreaseCounter: [{{counter}}, {{counter}}]}
              - {name: EQUIV_A, cost: 1, points: 1, spawnedItem: SUPPLY, spawnedItemCount: 2, increaseCounter: [{{counter}}, {{counter}}], decreaseCounter: [{{counter}}]}
              - {name: EQUIV_B, cost: 1, points: 1, spawnedItem: SUPPLY, spawnedItemCount: 3, increaseCounter: [{{counter}}, {{counter}}], decreaseCounter: [{{counter}}]}
            manufacture:
              - {name: QUANTITY_BIRTH, time: 1, {{(person ? "producedItems: {SUPPLY: 0}, spawnedPersonType: RECRUIT" : "category: STR_CRAFT, producedItems: {PRODUCED_CRAFT: 1}")}}}
            """, extraRulesAfterOverlays: true);
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, string counter, int start)
    {
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, seed, false);
        return initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { [counter] = start },
            Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("QUANTITY_WORKSHOP", 2, 1, 0, 0, false, false, false)],
                Productions = [new("QUANTITY_BIRTH", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
            }],
        };
    }
}
