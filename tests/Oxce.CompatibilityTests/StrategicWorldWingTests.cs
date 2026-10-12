using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldWingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWingLaunchesTogetherBehindItsLeader(bool followLeader)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var (campaign, owner) = Ready(content);
        var target = new WorldPosition(0.3, 0.1);

        var result = campaign.Execute(new DispatchCraftWing(
            [new(owner, "SHIP", 1), new(owner, "INTERCEPTOR", 1)], null, target, followLeader));

        var changes = result.Events.Cast<CraftDestinationChanged>().ToArray();
        Assert.Equal(2, changes.Length);
        var waypoint = Assert.Single(campaign.Capture().World.Waypoints);
        Assert.Equal((target.Longitude, target.Latitude), (waypoint.Longitude, waypoint.Latitude));
        Assert.Equal((WorldTargetKind.Waypoint, waypoint.Id), (changes[0].Destination!.Kind, changes[0].Destination!.Id));
        var follower = changes[1].Destination!;
        if (followLeader) Assert.Equal((WorldTargetKind.Craft, "SHIP", 1), (follower.Kind, follower.TypeId, follower.Id));
        else Assert.Equal((WorldTargetKind.Waypoint, waypoint.Id), (follower.Kind, follower.Id));
        Assert.All(campaign.Capture().Bases[0].Crafts.Where(craft => craft.RuleId is "SHIP" or "INTERCEPTOR"),
            craft => Assert.Equal("STR_OUT", craft.Logistics!.Status));
        AdvanceOne(campaign);
        Reload(content, campaign, 31, "wing.sav");
    }

    [Fact]
    public void AnAirborneCraftCannotBeSoldAndItsTransferIsANamedRestriction()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var (campaign, owner) = Ready(content, secondBase: true);
        Assert.All(campaign.Execute(new DispatchCraftToWaypoint(owner, "SHIP", 1, 0.3, 0.1)).Events,
            evt => Assert.IsNotType<CampaignActionBlocked>(evt));

        // SellState lists only grounded craft. TransferItemsState lists airborne craft only with
        // canTransferCraftsWhileAirborne, which the port does not support; the rows stay visible with a reason.
        Assert.Equal("Airborne craft cannot be sold.", Row(LogisticsOperation.Sell, null).UnavailableReason);
        Assert.Equal("Airborne craft cannot be transferred (canTransferCraftsWhileAirborne is not supported).",
            Row(LogisticsOperation.Transfer, 2).UnavailableReason);

        LogisticsRow Row(LogisticsOperation operation, int? destination)
        {
            var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(
                campaign.Execute(new PrepareLogisticsQuote(owner, operation, destination)).Events)).Quote;
            var row = Assert.Single(quote.Rows, row => row.Kind == CampaignTransferKind.Craft && row.RuleId == "SHIP");
            Assert.Equal(0, row.MaximumQuantity);
            return row;
        }
    }

    [Fact]
    public void ALeaderTargetingItselfPatrolsAndItsWingFollows()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var (campaign, owner) = Ready(content);
        var leader = new WorldTargetReference(WorldTargetKind.Craft, "SHIP", 1, 0, 0);
        var wing = new DispatchCraftWing([new(owner, "SHIP", 1), new(owner, "INTERCEPTOR", 1)], leader, null, true);
        var before = campaign.Capture();

        // A grounded craft is not a globe target (Globe::getTargets), so it cannot be picked.
        var grounded = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(wing).Events));
        Assert.StartsWith("target.", grounded.Code, StringComparison.Ordinal);
        Assert.Equivalent(before, campaign.Capture(), strict: true);

        Assert.All(campaign.Execute(new DispatchCraftToWaypoint(owner, "SHIP", 1, 0.3, 0.1)).Events,
            evt => Assert.IsNotType<CampaignActionBlocked>(evt));
        var events = campaign.Execute(wing).Events;
        Assert.Equal(2, events.OfType<CraftDestinationChanged>().Count());
        Assert.Empty(events.OfType<CampaignActionBlocked>());

        var crafts = campaign.Capture().Bases[0].Crafts;
        var ship = crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(("STR_OUT", (WorldTargetReference?)null), (ship.Status, ship.Destination));
        Assert.Equal(WorldTargetKind.Craft, crafts.Single(craft => craft.RuleId == "INTERCEPTOR").Logistics!.Destination!.Kind);
        Assert.Single(campaign.Capture().World.Waypoints);
    }

    [Fact]
    public void AnyRefusalLeavesTheWholeWingUntouchedWithAClassifiedReason()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var (campaign, owner) = Ready(content, interceptorStatus: "STR_REFUELLING");
        var before = campaign.Capture();

        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new DispatchCraftWing(
            [new(owner, "SHIP", 1), new(owner, "INTERCEPTOR", 1)], null, new(0.3, 0.1), true)).Events));
        Assert.Equal("craft.not-ready", blocked.Code);
        Assert.Null(blocked.TextKey);
        Assert.Equivalent(before, campaign.Capture(), strict: true);

        Assert.Equal("wing.size", Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new DispatchCraftWing(
            [new(owner, "SHIP", 1), new(owner, "SHIP", 1)], null, new(0.3, 0.1), true)).Events)).Code);
        Assert.Equal("wing.size", Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new DispatchCraftWing(
            [], null, new(0.3, 0.1), true)).Events)).Code);
        Assert.Equal("wing.size", Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new DispatchCraftWing(
            [.. Enumerable.Range(1, 5).Select(id => new CraftKey(owner, "SHIP", id))], null, new(0.3, 0.1), true)).Events)).Code);
        Assert.Equal("craft.not-ready", Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(
            new DispatchCraftToWaypoint(owner, "INTERCEPTOR", 1, 0.3, 0.1)).Events)).Code);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Throws<ArgumentException>(() => campaign.Execute(new DispatchCraftWing(
            [new(owner, "SHIP", 1)], new(WorldTargetKind.Base, WorldTargetReference.BaseType, owner, 0, 0), new(0.3, 0.1), true)));
    }

    [Theory]
    [InlineData("pilots")]
    [InlineData("ids")]
    public void ALateRefusalOfAFollowerLeavesTheWholeWingUntouched(string gate)
    {
        var content = gate == "pilots"
            ? StrategicReadinessTestContent.Load(["strategic-world.rul"], "crafts: [{type: INTERCEPTOR, pilots: 2}]\n", extraRulesAfterOverlays: true)
            : StrategicReadinessTestContent.Load("strategic-world.rul");
        if (gate == "pilots")
            // One soldier is on board the interceptor; two pilots are required.
            Assert.Equal(2, content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("INTERCEPTOR")].Value.Pilots);
        var (campaign, owner) = Ready(content, nextWaypointId: gate == "ids" ? int.MaxValue : null);
        var before = campaign.Capture();

        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new DispatchCraftWing(
            [new(owner, "SHIP", 1), new(owner, "INTERCEPTOR", 1)], null, new(0.3, 0.1), true)).Events));

        Assert.Equal(gate == "pilots" ? ("craft.no-pilots", "STR_NOT_ENOUGH_PILOTS") : ("waypoint.ids", null),
            (blocked.Code, blocked.TextKey));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void DispatchCancelsAutoPatrolOnlyForCraftThatCanUseIt()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], "crafts: [{type: SHIP, autoPatrol: true}]\n",
            extraRulesAfterOverlays: true);
        Assert.True(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value.AutoPatrol);
        Assert.False(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("INTERCEPTOR")].Value.AutoPatrol);
        var (campaign, owner) = Ready(content, autoPatrolling: true);

        campaign.Execute(new DispatchCraftWing([new(owner, "SHIP", 1), new(owner, "INTERCEPTOR", 1)], null, new(0.3, 0.1), false));

        var crafts = campaign.Capture().Bases[0].Crafts;
        Assert.False(crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.IsAutoPatrolling);
        Assert.True(crafts.Single(craft => craft.RuleId == "INTERCEPTOR").Logistics!.IsAutoPatrolling);
    }

    [Fact]
    public void TheWorldQueryDescribesCraftForTheCraftScreen()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var (campaign, _) = Ready(content, interceptorStatus: "STR_REFUELLING");
        var world = campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld();
        var ship = Assert.Single(world.Crafts, craft => craft.TypeId == "SHIP");
        var rules = content.RuntimeRules;
        var rule = rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value;
        Assert.Equal((100, rule.DamageMaximum, 0), (ship.FuelMaximum, ship.DamageMaximum, ship.Damage));
        Assert.Equal(rule.AutoPatrol, ship.CanAutoPatrol);
        Assert.Null(ship.DispatchUnavailable);
        Assert.Equal("craft.not-airborne", ship.PatrolRecallUnavailable!.Code);
        var interceptor = Assert.Single(world.Crafts, craft => craft.TypeId == "INTERCEPTOR");
        Assert.Equal("craft.not-ready", interceptor.DispatchUnavailable!.Code);
        Assert.Equal(interceptor.DispatchUnavailableReason, interceptor.DispatchUnavailable.Text);
        Assert.All(WorldCommandReasons.All, reason => Assert.NotEqual(CampaignReason.Unclassified, reason.Code));
        Assert.Equal(WorldCommandReasons.All.Count, WorldCommandReasons.All.Select(reason => reason.Code).Distinct().Count());
    }

    private static (CampaignState Campaign, int Owner) Ready(Oxce.Mods.Rulesets.Content.RuntimeContent content,
        string interceptorStatus = "STR_READY", int? nextWaypointId = null, bool autoPatrolling = false, bool secondBase = false)
    {
        var snapshot = CreateSnapshot(content, "Wing")
            .WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Status = "STR_READY",
                    Fuel = 100,
                    Weapons = [],
                    Items = new Dictionary<string, int>(),
                    IsAutoPatrolling = autoPatrolling,
                },
            })
            .WithCraft("INTERCEPTOR", craft => craft with
            {
                Logistics = craft.Logistics! with { Status = interceptorStatus, Fuel = 10, IsAutoPatrolling = autoPatrolling },
            });
        if (nextWaypointId is { } id) snapshot = snapshot.WithNextId(WorldTargetReference.WaypointType, id);
        if (secondBase)
        {
            var first = snapshot.Bases[0];
            snapshot = snapshot with
            {
                Bases = [first, first with { Id = 2, Name = "Beta", Crafts = [], Soldiers = [], Transfers = [] }],
            };
        }
        return (CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(31)), snapshot.Bases[0].Id);
    }
}
