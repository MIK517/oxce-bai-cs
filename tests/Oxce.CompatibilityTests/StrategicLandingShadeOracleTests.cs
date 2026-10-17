using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S6: the landing-site shade (Globe::getPolygonTextureAndShade with noise 0) matches the
/// reference shading compiled verbatim, with and without globeSeasons, over dates, times and positions.
/// </summary>
public sealed class StrategicLandingShadeOracleTests
{
    [Fact]
    public void LandingShadeMatchesTheReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-landing-shade");
        var rows = TestFixtures.Rows(oracle.RootElement, "rows").ToArray();
        Assert.Equal(2 * 6 * 8 * 8 * 7, rows.Length);
        var shades = new HashSet<int>();
        foreach (var row in rows)
        {
            var time = new CampaignTime(1, row[3].GetInt32(), row[2].GetInt32(), row[1].GetInt32(),
                row[4].GetInt32(), row[5].GetInt32(), row[6].GetInt32());
            var expected = row[9].GetInt32();
            Assert.True(expected == WorldShading.LandingShade(time, row[7].GetDouble(), row[8].GetDouble(), row[0].GetInt32() != 0),
                row.GetRawText());
            shades.Add(expected);
        }
        // The grid covers full daylight, full night and the twilight steps between them.
        Assert.Contains(0, shades);
        Assert.Contains(15, shades);
        Assert.True(shades.Count > 8);
    }
}
