using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicGenerationRegionForecastTests
{
    [Fact]
    public void RectanglePartitionPreservesOrderedContainmentAndRejectsHoles()
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var template = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
        var left = template with { Areas = [new(0, 0.5, -0.2, 0.2)] };
        var right = template with { Areas = [new(0.5, 1, -0.2, 0.2)] };
        var area = new MissionArea(0.2, 0.8, -0.1, 0.1, 0, "");
        long work = 0;
        var regions = new (string, RuntimeRegionRule)[] { ("LEFT", left), ("RIGHT", right) };
        var candidates = WorldGenerationRegions.Candidates(area, regions, ref work);
        Assert.Equal(["LEFT", "RIGHT"], candidates.Select(candidate => candidate.Id));
        Assert.All(candidates, candidate => Assert.True(WorldGeometry.InsideRegion(
            candidate.Id == "LEFT" ? left : right, candidate.Position)));
        work = 0;
        Assert.Equal("New base hunting cannot locate every possible source region.",
            Assert.Throws<InvalidDataException>(() => WorldGenerationRegions.Candidates(area, [("LEFT", left)], ref work)).Message);
        work = 0;
        Assert.Equal("LEFT", Assert.Single(WorldGenerationRegions.Candidates(
            area with { LongitudeMaximum = 0.4 }, [("LEFT", left), ("SHADOWED", left)], ref work)).Id);
        work = 999_999;
        Assert.Equal("New base hunting region forecasts exceed the supported work limit.",
            Assert.Throws<InvalidDataException>(() => WorldGenerationRegions.Candidates(area, regions, ref work)).Message);
    }
}
