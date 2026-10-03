using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.TestSupport;
using Oxce.FixtureSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicInfiltrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountryEligibilityAndOptionDefaultsMatchPinnedReference(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-infiltration");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "countries"))
        {
            var pact = row[0].GetInt32() != 0;
            var pending = row[1].GetInt32() != 0;
            var cancel = row[2].GetInt32() != 0;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new[] {
                pact ? 1 : 0, pending ? 1 : 0, cancel ? 1 : 0,
                WorldInfiltration.CanInfiltrate(pact, pending, cancel) ? 1 : 0,
                pact ? 1 : 0, 1, 0 });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
        var defaults = StrategicReadinessTestContent.Load("strategic-world.rul", cache).RuntimeRules.Campaign;
        Assert.False(defaults.BuildInfiltrationBaseCloseToTheCountry);
        Assert.False(defaults.InfiltrateRandomCountryInTheRegion);
        var enabled = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache,
            "buildInfiltrationBaseCloseToTheCountry: true\ninfiltrateRandomCountryInTheRegion: true\n").RuntimeRules.Campaign;
        Assert.True(enabled.BuildInfiltrationBaseCloseToTheCountry);
        Assert.True(enabled.InfiltrateRandomCountryInTheRegion);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InfiltrationRegionalSearchMatchesPinnedReference(bool cache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-objective-base.rul"], cache);
        var rules = content.RuntimeRules;
        var template = rules.AlienMissions[rules.AlienMissions.GetRequired("BASE_OBJECTIVE")].Value;
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-infiltration-region");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var success = row[0].GetInt32();
            var geometry = row[1].GetInt32();
            var kind = row[2].GetInt32();
            var chance = row[3].GetInt32();
            var fake = row[4].GetInt32() != 0;
            var allow = row[5].GetInt32() != 0;
            var x = geometry == 0 ? 0.2 : 2.0;
            var region = new RuntimeRegionRule(0, geometry == 2 ? [] : [new(0, 1, -0.5, 0.5)],
                [new([new(x, x, 0.1, 0.1, 100 + kind, "")])], new Dictionary<string, ulong>(), 0, "", null, [], []);
            var mission = template with { SiteTypeId = kind == 0 ? "EXPLICIT" : "" };
            var random = new TestFixtures.CountingMinimumRandomSource();
            var landCalls = 0;
            var error = false;
            WorldObjectiveBaseSpawn.Selection? selected = null;
            try
            {
                if (WorldGeometry.InsideRegion(region, new(0.2, 0.1)))
                    selected = WorldObjectiveBaseSpawn.Select(region, 0, kind != 0, allow, random,
                    area => WorldObjectiveBaseSpawn.Deployment(rules, mission, area, random),
                    _ => chance, _ => ++landCalls >= success && success != 0, _ => fake, ignoreTechnicalRegion: false);
            }
            catch (KeyNotFoundException) { error = true; }
            var id = selected is { } value ? rules.AlienDeployments.GetExternalId(value.Deployment) : "";
            if (selected is { } result) Assert.Equal(new WorldPosition(x, 0.1), result.Position);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                success, geometry, kind, chance, fake ? 1 : 0, allow ? 1 : 0,
                selected is null ? 0 : 1, error ? 1 : 0, random.IntegerCalls, random.UnitCalls, landCalls, id,
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegionalCompletionCreatesOnlyPendingPactAndPersistsItsBase(bool cache)
    {
        var content = Load(cache);
        var initial = Initial(content);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        campaign = Reload(content, campaign, 17, "before-infiltration.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var country = after.Countries.Single(country => country.RuleId == "INF_A");
        Assert.False(country.Pact);
        Assert.True(country.NewPact);
        Assert.False(country.CancelPact);
        var alienBase = Assert.Single(after.World.AlienBases);
        Assert.Equal("INF_A", alienBase.PactCountryId);
        Assert.Equal("RACE_B", alienBase.Race);
        Assert.Equal(1UL, after.World.AlienBaseBirthSequence);
        Assert.Equal(initial.Regions.Sum(region => (long)region.ActivityAlien[^1]),
            after.Regions.Sum(region => (long)region.ActivityAlien[^1]));
        Assert.Empty(after.World.Missions);
        Assert.Empty(after.World.Events);
        campaign = Reload(content, campaign, 17, "infiltration-pending.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Single(campaign.Capture().World.AlienBases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PactEligibilityIsLiveAcrossComposedCompletionsAndRecapture(bool cache)
    {
        var content = Load(cache);
        var initial = Initial(content);
        initial = initial with { World = initial.World with {
            Missions = [initial.World.Missions[0], initial.World.Missions[0] with { Id = 5 }] } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(["INF_A", "INF_B"], campaign.Capture().World.AlienBases.Select(item => item.PactCountryId));
        Reload(content, campaign, 17, "two-pacts.sav");
        initial = Initial(content) with {
            Countries = Initial(content).Countries.Select(country => country with {
                Pact = true, CancelPact = country.RuleId == "INF_B", NewPact = false }).ToArray() };
        campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal("INF_B", Assert.Single(campaign.Capture().World.AlienBases).PactCountryId);
        Assert.True(campaign.Capture().Countries.Single(country => country.RuleId == "INF_B").Pact);
        Assert.False(campaign.Capture().Countries.Single(country => country.RuleId == "INF_B").CancelPact);
        Reload(content, campaign, 17, "recaptured-pact.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppressedSearchAndIneligibleCountriesDoNotCreatePacts(bool cache)
    {
        var content = Load(cache, "allowAlienBasesOnWrongTextures: false\nalienDeployments:\n  - {type: INF_BASE, fakeUnderwaterSpawnChance: 100}\n");
        var campaign = CampaignState.Restore(Initial(content), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.AlienBases);
        Assert.All(campaign.Capture().Countries, country => Assert.False(country.NewPact));
        Reload(content, campaign, 17, "suppressed-infiltration.sav");
        var initial = Initial(content) with {
            Countries = Initial(content).Countries.Select(country => country with { NewPact = true }).ToArray() };
        campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.AlienBases);
        Assert.Equal(initial.RandomState, campaign.Capture().RandomState);
        Reload(content, campaign, 17, "no-eligible-infiltration.sav");
    }

    [Fact]
    public void InfiltrationBirthOverflowStopsBeforeAnyCountryMutation()
    {
        var content = Load(false);
        var initial = Initial(content).WithNextId("INF_BASE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Base objective IDs are exhausted or collide.");
    }

    private static RuntimeContent Load(bool cache, string extra = "") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-infiltration.rul"], cache, extra);

    private static CampaignSnapshot Initial(RuntimeContent content)
    {
        var initial = CreateSnapshot(content, "Infiltration");
        return initial with {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Countries = initial.Countries.Where(country => country.RuleId.StartsWith("INF_", StringComparison.Ordinal)).ToArray(),
            World = initial.World with { Missions = [new(4, "INFILTRATE", "INF_REGION", "RACE_B", 0, 0, 0, 0, -1)] },
        };
    }
}
