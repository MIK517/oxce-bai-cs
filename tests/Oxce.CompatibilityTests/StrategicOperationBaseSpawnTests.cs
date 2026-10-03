using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.TestSupport;
using Oxce.FixtureSupport;
using System.Text.Json;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicOperationBaseSpawnTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrongTextureFallbackDefaultsTrueAndExplicitFalseSurvivesCache(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-operation-base-spawn");
        var defaults = StrategicReadinessTestContent.Load("strategic-world.rul", cache);
        Assert.True(defaults.RuntimeRules.Campaign.AllowAlienBasesOnWrongTextures);
        var configured = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache,
            "allowAlienBasesOnWrongTextures: false\n");
        Assert.False(configured.RuntimeRules.Campaign.AllowAlienBasesOnWrongTextures);
    }
}
