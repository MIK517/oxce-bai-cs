using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class CampaignTimeSpeedTests
{
    [Theory]
    [InlineData(CampaignTimeSpeed.FiveSeconds)]
    [InlineData(CampaignTimeSpeed.OneMinute)]
    [InlineData(CampaignTimeSpeed.FiveMinutes)]
    [InlineData(CampaignTimeSpeed.ThirtyMinutes)]
    [InlineData(CampaignTimeSpeed.OneHour)]
    [InlineData(CampaignTimeSpeed.OneDay)]
    public void SelectedSpeedIsExplicitAndDoesNotReplaceTheRequestedTickCount(CampaignTimeSpeed speed)
    {
        var content = TestFixtures.LoadStrategicLogistics();
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "Selected speed");
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        var individual = CampaignState.Restore(campaign.Capture(), content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(12) { SelectedSpeed = speed });
        var advanced = Assert.Single(result.Events.OfType<CampaignTimeAdvanced>());
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(12, advanced.Summary.TickCount);
        Assert.Equal(speed, advanced.SelectedSpeed);
        for (var index = 0; index < 12; index++)
            individual.Execute(new AdvanceCampaignTime(1) { SelectedSpeed = speed });
        Assert.Equivalent(individual.Capture(), campaign.Capture(), strict: true);
        Assert.Equal(CampaignTimeSpeed.FiveSeconds,
            Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignTimeAdvanced>()).SelectedSpeed);
    }

    [Fact]
    public void InvalidSpeedRejectsWithoutChangingTimeOrRandomness()
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(TestFixtures.LoadStrategicLogistics(), "Invalid speed");
        var before = campaign.Capture();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            campaign.Execute(new AdvanceCampaignTime(1) { SelectedSpeed = (CampaignTimeSpeed)99 }));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void RegisteredPreflightAndTimedHandlersReceiveTheSameExplicitContext()
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(TestFixtures.LoadStrategicLogistics(), "Handler speed");
        var registry = new CampaignCapabilityRegistry();
        List<CampaignTimeSpeed> seen = [];
        registry.Preflight(1, "speed", (_, _, speed) => { seen.Add(speed); return null; });
        registry.Timed(CampaignTimeTrigger.FiveSeconds, 1, "speed", effects => seen.Add(effects.SelectedSpeed));
        var table = registry.Build();
        var effects = new CampaignState.TimeEffects(campaign, CampaignTimeSpeed.OneHour);
        Assert.Null(table.Preflight(campaign.Time, CampaignTimeTrigger.FiveSeconds, effects.SelectedSpeed));
        table.Apply(CampaignTimeTrigger.FiveSeconds, effects);
        Assert.Equal<CampaignTimeSpeed>([CampaignTimeSpeed.OneHour, CampaignTimeSpeed.OneHour], seen);
    }
}
