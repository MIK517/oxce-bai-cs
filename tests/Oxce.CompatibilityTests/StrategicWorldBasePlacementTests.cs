using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>BuildNewBaseState, ConfirmNewBaseState, BaseNameState and PlaceLiftState on the geoscape.</summary>
public sealed class StrategicWorldBasePlacementTests
{
    [Fact]
    public void ANewBaseIsChosenOnTheGlobeConfirmedNamedAndOpened()
    {
        var content = StrategicReadinessTestContent.Load();
        var (campaign, client) = Open(content, funds: 20_000);
        var site = campaign.QueryBaseSites(false).First(candidate => !candidate.FakeUnderwater);

        Key(client, 'n');
        Assert.Equal(false, client.PlacingBase);
        Assert.False(client.TimeCanRun);
        Key(client, 27);
        Assert.Null(client.PlacingBase);

        Key(client, 'n');
        client.View.Center(site.Longitude, site.Latitude);
        Click(client, 256, 200);
        // ConfirmNewBaseState: the region's cost and area; Esc returns to choosing a site.
        var confirm = Assert.Single(client.Popups);
        Assert.Equal("STR_COST_", confirm.Title);
        Key(client, 27);
        Assert.Equal(false, client.PlacingBase);
        Click(client, 256, 200);
        Key(client, 13);
        Assert.Equal("", client.BaseName);
        client.HandleInput(GameInputEvent.TextEntry(0, 0, "Beta"));
        Key(client, 13);
        if (client.Popups is [{ Title: "STR_ACCESS_LIFT" } lifts])
            client.Choose(lifts.Actions.ToList().FindIndex(action => action.Label == "LIFT"));

        Assert.Null(client.PlacingBase);
        var created = Assert.Single(campaign.Capture().Bases, owner => owner.Name == "Beta");
        Assert.Equal("LIFT", Assert.Single(created.Facilities).RuleId);
        Assert.Equal(20_000 - site.Cost, campaign.QueryOverview().Funds);
        // PlaceLiftState continues to the new base's screens.
        Assert.True(client.BasesOpen);
    }

    [Fact]
    public void ABaseNeedsTheRegionCostAndLandAndTheBaseScreensHandOverToTheGlobe()
    {
        var content = StrategicReadinessTestContent.Load();
        var (campaign, client) = Open(content, funds: 10);
        var site = campaign.QueryBaseSites(false).First(candidate => !candidate.FakeUnderwater);
        var before = campaign.Capture();

        // Basescape's "Build new base" closes the base screens and chooses the site on the globe.
        Key(client, 'b');
        Key(client, 'n');
        Assert.False(client.BasesOpen);
        Assert.Equal(false, client.PlacingBase);
        client.View.Center(site.Longitude, site.Latitude);
        Click(client, 256, 200);
        Key(client, 13);
        Assert.Equal("STR_NOT_ENOUGH_MONEY", Assert.Single(client.Popups).Title);
        Key(client, 13);
        Assert.Equal(false, client.PlacingBase);
        Assert.Null(client.BaseName);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void TheRadarPreviewFollowsThePointerAndReplacesBaseRadars()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var (_, client) = Open(content, funds: 0);
        var world = client.World;
        var ranges = content.RuntimeRules.Facilities.Rules.Select(entry => entry.Value.RadarRange).Where(range => range > 0)
            .Distinct().ToArray();
        Assert.NotEmpty(ranges);

        var hover = new GlobeBaseHover(1.0, 0.3);
        var scene = GlobeScene.Build(world, content.RuntimeRules, null, hover);
        Assert.Equal(ranges.Length, scene.Radars.Count(circle => circle.Longitude == 1.0 && circle.Latitude == 0.3));
        var owner = Assert.Single(world.Bases);
        Assert.Equal(ranges.Length, scene.Radars.Count(circle => circle.Longitude == owner.Longitude));
        var own = GlobeScene.Build(world, content.RuntimeRules, null, hover with { AllRadarsOnBases = false });
        Assert.Equal(GlobeScene.Build(world, content.RuntimeRules).Radars.Count(circle => circle.Longitude == owner.Longitude),
            own.Radars.Count(circle => circle.Longitude == owner.Longitude));
    }

    private static (CampaignState Campaign, GeoscapeClient Client) Open(RuntimeContent content, long funds)
    {
        var original = TestFixtures.CreateLogisticsCampaign(content, "Bases");
        var campaign = CampaignState.Restore(original.Capture() with { Funds = [funds], Incomes = [0], Expenditures = [0] },
            content, new SplitMix64RandomSource(42));
        // A base at exactly (0, 0) is hidden on the globe, so start elsewhere.
        var start = campaign.QueryBaseSites(true).First(site => site.Longitude != 0 || site.Latitude != 0);
        Assert.IsType<StartingBasePlaced>(Assert.Single(campaign.Execute(
            new PlaceStartingBase(0, "Alpha", start.Longitude, start.Latitude)).Events));
        var texture = new IndexedSurface(32, 32);
        texture.Clear(0x41);
        var assets = new GeoscapeAssets(IndexedPalette.CreateGrayscale(),
            new Dictionary<int, IndexedSurface> { [0] = texture, [1] = texture, [2] = texture },
            new Dictionary<int, IndexedSurface>(), RuntimeGlobeColors.Default);
        var client = new GeoscapeClient(new GeoscapeClientSetup(CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            content.RuntimeRules, assets, IndexedInterfaceFont.Create(), static text => text)
        {
            Stopwatch = static () => TimeSpan.Zero,
        });
        Assert.Null(client.PlacingBase);
        return (campaign, client);
    }

    private static void Key(GeoscapeClient client, uint key) =>
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None));

    private static void Click(GeoscapeClient client, int x, int y) =>
        client.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, 1, 1));
}
