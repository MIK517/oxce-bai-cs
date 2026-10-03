using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
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
}
