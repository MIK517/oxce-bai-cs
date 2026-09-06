using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
using Oxce.UnitTests.Gameplay;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class CampaignLogisticsClientTests
{
    [Fact]
    public void KeyboardPurchaseConfirmsOnceAndCanSaveLoadDuringTransit()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var campaign = CampaignFactory.Create(content, new(new(Guid.NewGuid()), "UI", "logistics", ["logistics"],
            CampaignDifficulty.Beginner), new SplitMix64RandomSource(42), SystemCampaignClock.Instance);
        string? saved = null;
        var client = new CampaignLogisticsClient(new(campaign, campaign), save: () => saved = OxceSaveAdapter.EmitNewCampaign(campaign.Capture()),
            load: () =>
            {
                campaign = OxceSaveAdapter.Load(saved!, "ui.sav", content, new SplitMix64RandomSource(0),
                    new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" })).Campaign;
                return new(campaign, campaign);
            });
        Key('b');
        var quote = client.Screen!.Quote!;
        var supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        for (var i = 0; i < supply.Id; i++) Key(0x40000051);
        Key('+');
        Key('+');
        Assert.Equal(2, client.Screen.Quantity(supply.Id));
        Assert.Equal(quote.Id, client.Screen.Quote!.Id);
        Assert.Empty(campaign.Capture().Bases[0].Transfers);
        Key(13);
        Assert.True(client.Screen.ConfirmationPending);
        Key(27);
        Assert.False(client.Screen.ConfirmationPending);
        Assert.Empty(campaign.Capture().Bases[0].Transfers);
        Key(13);
        Key('y');
        Assert.Single(campaign.Capture().Bases[0].Transfers);
        Key('y');
        Assert.Single(campaign.Capture().Bases[0].Transfers);
        Key(0x4000003e);
        Assert.NotNull(saved);
        Key(' ');
        Assert.Empty(campaign.Capture().Bases[0].Transfers);
        Key(0x40000042);
        Assert.Empty(campaign.Capture().Bases[0].Transfers);
        Key('y');
        Assert.Single(campaign.Capture().Bases[0].Transfers);
        Key('i');
        Assert.Equal(2, client.Screen!.Stores.Items.Single(i => i.RuleId == "SUPPLY").Incoming);
        Key(' ');
        Assert.Equal(4, campaign.Capture().Bases[0].Items["SUPPLY"]);
        Assert.Contains(client.Frame.Pixels.ToArray(), color => color >= 16);

        void Key(uint code) => client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, code, InputKeyModifiers.None));
    }

    [Fact]
    public void OpaqueCraftInventoryIsReportedInsteadOfAssumedEmpty()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var campaign = CampaignFactory.Create(content, new(new(Guid.NewGuid()), "UI", "logistics", ["logistics"],
            CampaignDifficulty.Beginner), new SplitMix64RandomSource(42), SystemCampaignClock.Instance);
        var snapshot = campaign.Capture();
        campaign = CampaignState.Restore(snapshot with { Bases = [snapshot.Bases[0] with { Crafts = [new("SHIP", 1)] }] },
            content, new SplitMix64RandomSource(0));
        var screen = new CampaignLogisticsScreen(campaign, campaign, 0);
        Assert.NotNull(screen.Stores.CapacityLimitation);
        var before = campaign.Capture();
        Assert.False(screen.OpenOrder(LogisticsOperation.Purchase));
        Assert.Contains("Craft inventory", screen.Message, StringComparison.Ordinal);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void StoresQueriesAndRedrawsDoNotConsumeRandomOrMutateOrders()
    {
        var content = CampaignLogisticsTests.LoadFixture();
        var campaign = CampaignFactory.Create(content, new(new(Guid.NewGuid()), "UI", "logistics", ["logistics"],
            CampaignDifficulty.Beginner), new SplitMix64RandomSource(42), SystemCampaignClock.Instance);
        var before = campaign.Capture();
        var screen = new CampaignLogisticsScreen(campaign, campaign, 0);
        Assert.Equal(2, screen.Stores.Items.Single(i => i.RuleId == "SUPPLY").Stored);
        for (var i = 0; i < 10; i++) screen.Cancel();
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.False(screen.Confirm());
        Assert.False(screen.RequestConfirmation());
    }
}
