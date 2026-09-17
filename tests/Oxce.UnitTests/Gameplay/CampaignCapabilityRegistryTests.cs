using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class CampaignCapabilityRegistryTests
{
    [Fact]
    public void TimeHandlersRunInExplicitOrderRegardlessOfRegistrationOrder()
    {
        var registry = new CampaignCapabilityRegistry();
        registry.Timed(CampaignTimeTrigger.OneHour, 300, "third", static _ => { });
        registry.Timed(CampaignTimeTrigger.OneHour, 100, "first", static _ => { });
        registry.Timed(CampaignTimeTrigger.OneHour, 200, "second", static _ => { });
        registry.Preflight(20, "later", static (_, _) => "later");
        registry.Preflight(10, "earlier", static (_, _) => null);

        var table = registry.Build();

        Assert.Equal<string>(["first", "second", "third"], table.HandlerNames(CampaignTimeTrigger.OneHour));
        Assert.Empty(table.HandlerNames(CampaignTimeTrigger.OneDay));
        Assert.Equal<string>(["earlier", "later"], table.PreflightNames);
        Assert.Equal("later", table.Preflight(default, CampaignTimeTrigger.FiveSeconds));
    }

    [Fact]
    public void AmbiguousRegistrationsAreRejected()
    {
        var registry = new CampaignCapabilityRegistry();
        registry.Command<AdvanceCampaignTime>(static _ => new([]));
        registry.Timed(CampaignTimeTrigger.OneDay, 100, "daily", static _ => { });
        registry.Query<ICampaignQuery>(new NullQuery());

        Assert.Throws<InvalidOperationException>(() => registry.Command<AdvanceCampaignTime>(static _ => new([])));
        Assert.Throws<InvalidOperationException>(() =>
            registry.Timed(CampaignTimeTrigger.OneDay, 100, "other", static _ => { }));
        Assert.Throws<InvalidOperationException>(() =>
            registry.Timed(CampaignTimeTrigger.OneDay, 200, "daily", static _ => { }));
        Assert.Throws<InvalidOperationException>(() => registry.Query<ICampaignQuery>(new NullQuery()));
        Assert.Throws<ArgumentException>(() => registry.Query(new NullQuery()));
    }

    [Fact]
    public void InstalledCampaignKeepsReferenceHandlerOrder()
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(TestFixtures.LoadStrategicLogistics(), "Order");
        var table = campaign.Handlers;

        Assert.Equal<string>(["months passed", "purchase limits"], table.HandlerNames(CampaignTimeTrigger.OneMonth));
        Assert.Equal<string>(["days passed", "base daily loop"], table.HandlerNames(CampaignTimeTrigger.OneDay));
        Assert.Equal<string>(["craft servicing", "transfers", "production"], table.HandlerNames(CampaignTimeTrigger.OneHour));
        Assert.Equal<string>(["craft refuelling"], table.HandlerNames(CampaignTimeTrigger.ThirtyMinutes));
        Assert.Equal<string>(
            ["restrictions", "craft movement", "month boundary", "craft servicing", "research and production", "transfers"],
            table.PreflightNames);
        var commandTypes = typeof(ICampaignCommand).Assembly.GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false } && typeof(ICampaignCommand).IsAssignableFrom(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal);
        Assert.Equal(commandTypes, table.CommandTypes.OrderBy(static type => type.FullName, StringComparer.Ordinal));
    }

    [Fact]
    public void UnregisteredCommandIsRejected()
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(TestFixtures.LoadStrategicLogistics(), "Unknown");
        Assert.Throws<ArgumentException>(() => campaign.Execute(new UnknownCommand()));
    }

    private sealed record UnknownCommand : ICampaignCommand;

    private sealed class NullQuery : ICampaignQuery
    {
        public CampaignOverview QueryOverview() => throw new NotSupportedException();
        public CampaignStores QueryStores(int baseId) => throw new NotSupportedException();
    }
}
