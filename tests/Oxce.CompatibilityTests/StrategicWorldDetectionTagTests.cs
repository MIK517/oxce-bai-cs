using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Scripting.Globals;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionTagTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UfoInstanceTagAndAbsentTagExecuteAcrossReloads(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var value = TestFixtures.Rows(oracle.RootElement, "tagValues")[2][1].GetInt32();
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-tags.rul"], fromCache);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_SCRIPT_TAGS", 2200).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [snapshot.World.Ufos[0] with { ScriptValues = [new("DETECT_UFO", "int", value)] }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(101));
        campaign = Reload(content, campaign, 101, "ufo-detection-tags-before.sav");

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).HyperDetected);
        Reload(content, campaign, 101, "ufo-detection-tags-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftInstanceTagsExecuteAndPersist(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-tags.rul"], fromCache);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_CRAFT_TAGS", 2200).Capture()
            .WithCraft("SHIP", craft => craft with
            {
                ScriptValues = [new("DETECT_CRAFT", "int", 37)],
                Logistics = craft.Logistics! with { Status = "STR_OUT", Destination = null, Speed = 0 },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(102));
        campaign = Reload(content, campaign, 102, "craft-detection-tags-before.sav");

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 102, "craft-detection-tags-after.sav");
    }

    [Theory]
    [InlineData("unknown", "int")]
    [InlineData("DETECT_CRAFT", "text")]
    public void InvalidCraftTagStateFailsIntentionally(string name, string type)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-tags.rul"]);
        var snapshot = CreateSnapshot(content, "Invalid craft tag").WithCraft("SHIP",
            craft => craft with { ScriptValues = [new(name, type, 1)] });

        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(103)));
    }

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);
}
