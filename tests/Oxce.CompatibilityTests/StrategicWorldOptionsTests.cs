using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>Options::load of fixedUserOptions (Mod::loadAll), the SavedGame options snapshot and craftLaunchAlways.</summary>
public sealed class StrategicWorldOptionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModFixedUserOptionsOverrideNewAndRestoredCampaigns(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world-fixed-options.rul", fromCache);
        var expected = new CampaignOptions(CanSellLiveAliens: false, UfoLandingAlert: true,
            AggressiveRetaliation: false, CraftLaunchAlways: true);

        var created = TestFixtures.CreateLogisticsCampaign(content, "Fixed options");
        Assert.Equal(expected, created.Options);

        var snapshot = created.Capture() with
        {
            Options = new CampaignOptions(CanSellLiveAliens: true, UfoLandingAlert: false,
                AggressiveRetaliation: true, CraftLaunchAlways: false, AllowBuildingQueue: true),
        };
        var restored = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(1));
        Assert.Equal(expected with { AllowBuildingQueue = true }, restored.Options);
        var loaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(snapshot), content,
            name: "fixed-options.sav").Campaign;
        Assert.Equal(restored.Options, loaded.Options);
    }

    [Fact]
    public void BooleanFixedOptionsFollowBoolalpha()
    {
        var options = new CampaignOptions(StorageLimitsEnforced: true).WithFixedUserOptions(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["storageLimitsEnforced"] = "True",
                ["allowBuildingQueue"] = "true",
                ["unknownOption"] = "true",
            });

        Assert.False(options.StorageLimitsEnforced);
        Assert.True(options.AllowBuildingQueue);
        Assert.Equal(new CampaignOptions(AllowBuildingQueue: true), options);
    }

    [Theory]
    [InlineData(" true", true)]
    [InlineData("true ", true)]
    [InlineData("truegarbage", true)]
    [InlineData("\t\r\n\f\vtrue tail", true)]
    [InlineData("false tail", false)]
    [InlineData("True", false)]
    [InlineData("tru", false)]
    [InlineData("1", false)]
    [InlineData("\u00a0true", false)]
    [InlineData(" \t", false)]
    [InlineData("", false)]
    public void FixedBooleanUsesClassicWhitespaceAndConsumesOnlyItsPrefix(string value, bool expected)
    {
        // OptionInfo::load uses formatted std::boolalpha extraction, verified with MSVC.
        // Empty/whitespace-only input deliberately becomes false in the port.
        foreach (var initial in new[] { false, true })
        {
            var options = new CampaignOptions(CraftLaunchAlways: initial).WithFixedUserOptions(
                new Dictionary<string, string> { ["craftLaunchAlways"] = value });
            Assert.Equal(expected, options.CraftLaunchAlways);
        }
    }

    [Theory]
    [InlineData("false", "true", false, true)]
    [InlineData("true", "false", true, false)]
    [InlineData("[broken]", "7", true, false)]
    public void ReferenceSaveOptionsSnapshotSeedsSessionOptions(string aggressive, string landingAlert,
        bool expectedAggressive, bool expectedLandingAlert)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var yaml = OxceSaveAdapter.EmitNewCampaign(CreateSnapshot(content, "Reference options"));
        var lines = yaml.Split('\n').ToList();
        var start = lines.FindIndex(line => line == "oxcePortOptions:");
        Assert.True(start >= 0);
        var end = lines.FindIndex(start + 1, line => line.Length == 0 || !line.StartsWith(' '));
        if (end < 0) end = lines.Count;
        // SavedGame::save writes this debugging snapshot; the port has no oxcePortOptions yet.
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, ["options:", $"  aggressiveRetaliation: {aggressive}",
            $"  oxceUfoLandingAlert: {landingAlert}", "  craftLaunchAlways: true"]);

        var loaded = TestFixtures.LoadLogisticsSave(string.Join('\n', lines), content, name: "reference-options.sav");

        Assert.Equal(expectedAggressive, loaded.Campaign.Options.AggressiveRetaliation);
        Assert.Equal(expectedLandingAlert, loaded.Campaign.Options.UfoLandingAlert);
        Assert.True(loaded.Campaign.Options.CraftLaunchAlways);
        Assert.Contains("craftLaunchAlways: true",
            OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftLaunchAlwaysLetsAServicedCraftDepart(bool launchAlways)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Launch always").WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 10, Weapons = [null, null] },
        });
        var campaign = CampaignState.Restore(snapshot with
        {
            Options = snapshot.Options with { CraftLaunchAlways = launchAlways },
        }, content, new SplitMix64RandomSource(2));
        var baseId = snapshot.Bases[0].Id;

        var result = Assert.Single(campaign.Execute(new DispatchCraftToWaypoint(baseId, "SHIP", 1, 0.3, 0.1)).Events);

        if (!launchAlways)
        {
            Assert.Equal("The craft is not ready to depart.", Assert.IsType<CampaignActionBlocked>(result).Reason);
            return;
        }
        Assert.IsType<CraftDestinationChanged>(result);
        var craft = Assert.Single(campaign.Capture().Bases[0].Crafts, item => item.RuleId == "SHIP").Logistics!;
        Assert.Equal("STR_OUT", craft.Status);
        Assert.Equal(60, craft.Takeoff);
    }
}
