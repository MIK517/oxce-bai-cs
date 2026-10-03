using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
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
}
