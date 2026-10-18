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
    public void StagePointsNeedALaterLevelAndTransientCopiesMustMatchByName()
    {
        // ADR 0035: a stage point at five seconds would have nothing left to check.
        var registry = new CampaignCapabilityRegistry();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            registry.StagePoint(CampaignTimeTrigger.FiveSeconds, "nothing left", static (_, _) => true));
        registry.StagePoint(CampaignTimeTrigger.OneDay, "day", static (_, highest) => highest == CampaignTimeTrigger.OneMonth);
        registry.StagePoint(CampaignTimeTrigger.ThirtyMinutes, "half hour", static (_, _) => true);
        var original = new List<int> { 3 };
        registry.Transient("numbers", original, static (source, shadow) => shadow.AddRange(source));
        Assert.Throws<InvalidOperationException>(() => registry.Transient("numbers", original, static (_, _) => { }));
        var table = registry.Build();

        // Points above the tick's highest level are not offered; a point that does not apply is not set.
        Assert.Equal(1 << (int)CampaignTimeTrigger.ThirtyMinutes, table.StagePoints(default, CampaignTimeTrigger.OneHour));
        Assert.Equal(1 << (int)CampaignTimeTrigger.ThirtyMinutes, table.StagePoints(default, CampaignTimeTrigger.OneDay));
        Assert.Equal(1 << (int)CampaignTimeTrigger.OneDay | 1 << (int)CampaignTimeTrigger.ThirtyMinutes,
            table.StagePoints(default, CampaignTimeTrigger.OneMonth));
        Assert.Equal(0, table.StagePoints(default, CampaignTimeTrigger.TenMinutes));

        var copy = new List<int>();
        var shadowRegistry = new CampaignCapabilityRegistry();
        shadowRegistry.Transient("numbers", copy, static (source, shadow) => shadow.AddRange(source));
        table.CopyTransientTo(shadowRegistry.Build());
        Assert.Equal([3], copy);

        var renamed = new CampaignCapabilityRegistry();
        renamed.Transient("other numbers", new List<int>(), static (_, _) => { });
        Assert.Throws<InvalidOperationException>(() => table.CopyTransientTo(renamed.Build()));
        Assert.Throws<InvalidOperationException>(() => table.CopyTransientTo(new CampaignCapabilityRegistry().Build()));
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

        Assert.Equal<string>(["months passed", "purchase limits", "monthly world scheduling", "monthly psi training", "monthly funding",
            "monthly report", "monthly alien base detection"], table.HandlerNames(CampaignTimeTrigger.OneMonth));
        Assert.Equal<string>(["days passed", "base daily loop", "alien mission research interruption",
            "alien base activity and facility discovery", "dogfight experience cache"],
            table.HandlerNames(CampaignTimeTrigger.OneDay));
        Assert.Equal<string>(["craft servicing", "transfers", "production", "mission site discovery"],
            table.HandlerNames(CampaignTimeTrigger.OneHour));
        Assert.Equal<string>(["alien mission countdown", "crashed UFO expiry", "craft refuelling", "world craft auto-patrol",
            "UFO activity and base detection", "mission site activity and expiry", "strategic event countdowns"],
            table.HandlerNames(CampaignTimeTrigger.ThirtyMinutes));
        Assert.Equal<string>(["world craft fuel", "UFO detection of XCOM bases", "alien base hunting timers",
            "UFO hunting and escort retargeting"],
            table.HandlerNames(CampaignTimeTrigger.TenMinutes));
        Assert.Equal<string>(["fast UFO retargeting and target repair", "campaign ending", "world UFO movement", "world craft movement",
            "interception start", "destroyed UFO interception cleanup", "destroyed UFO cleanup", "interception waits"],
            table.HandlerNames(CampaignTimeTrigger.FiveSeconds));
        Assert.Equal<string>(
            ["restrictions", "monthly report", "event windows", "interceptions", "landing confirmations", "destroyed crafts", "world simulation", "craft movement", "monthly world scheduling", "craft servicing",
                "research and production", "craft target removal", "transfers"],
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
