using Oxce.FixtureSupport;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class RuntimeGlobeProjectionTests
{
    [Fact]
    public void GlobeWithoutPresentationFieldsKeepsModDefaults()
    {
        var globe = Link("globe:\n  polygons:\n    - [0, -15, -15, 15, -15, 15, 15]\n");
        Assert.Empty(globe.Polylines);
        Assert.Equal(RuntimeGlobeColors.Default, globe.Colors);
        Assert.Equal(new RuntimeGlobeColors(192, true, 239, 138, 133, 162), globe.Colors);
    }

    [Fact]
    public void PolylinesAndColorsFollowRuleGlobeLoad()
    {
        var globe = Link("""
            globe:
              oceanPalette: 1
              oceanShading: false
              countryColor: 245
              cityColor: 239
              baseColor: 238
              lineColor: 111
              polylines:
                - [10, 20, -30.5, -40]
                - [359, 89]
            """);

        Assert.Equal(new RuntimeGlobeColors(16, false, 245, 239, 238, 111), globe.Colors);
        Assert.Equal(2, globe.Polylines.Count);
        var first = globe.Polylines[0].Points;
        Assert.Equal(2, first.Count);
        Assert.Equal(10 * Math.PI / 180, first[0].Longitude, 15);
        Assert.Equal(20 * Math.PI / 180, first[0].Latitude, 15);
        Assert.Equal(-30.5 * Math.PI / 180, first[1].Longitude, 15);
        Assert.Equal(-40 * Math.PI / 180, first[1].Latitude, 15);
        Assert.Single(globe.Polylines[1].Points);
    }

    [Theory]
    [InlineData("  polylines:\n    - [10, 20, 30]\n")]
    [InlineData("  polylines:\n    - []\n")]
    [InlineData("  polylines:\n    - [10, 91]\n")]
    [InlineData("  countryColor: 256\n")]
    [InlineData("  lineColor: -1\n")]
    [InlineData("  oceanPalette: 16\n")]
    public void MalformedPresentationFieldsFailLikeMalformedPolygons(string field)
    {
        using var fixture = new TemporaryModFixture("globe:\n" + field);
        Assert.Throws<InvalidDataException>(() => ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root)));
    }

    private static RuntimeGlobe Link(string rules)
    {
        using var fixture = new TemporaryModFixture(rules);
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));
        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked),
            string.Join(Environment.NewLine, snapshot.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return snapshot.Content.RuntimeRules.Campaign.Globe;
    }
}
