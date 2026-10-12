using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S6: a craft that may land reaching a landed UFO queues a <c>ConfirmLandingState</c> with the
/// site's texture and shade. The queue pauses time, is not saved, and No or Patrol answer it.
/// </summary>
public sealed class StrategicLandingTests
{
    private static (RuntimeContent Content, CampaignState Campaign) Create(bool seasons = false, string? rules = null)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], rules);
        var snapshot = CreateLifecycleSnapshot(content);
        var ufo = snapshot.World.Ufos[0] with { SecondsRemaining = 3600, Detected = true };
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Options = snapshot.Options with { GlobeSeasons = seasons },
            World = snapshot.World with { Ufos = [ufo] },
            Bases = [owner with
            {
                Crafts = [.. owner.Crafts.Select(craft => craft.RuleId != "SHIP" ? craft : craft with
                {
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_OUT",
                        Fuel = 100,
                        Speed = 6000,
                        Longitude = ufo.Longitude,
                        Latitude = ufo.Latitude,
                        Destination = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, ufo.Id,
                            ufo.Longitude, ufo.Latitude) { UniqueId = ufo.UniqueId },
                    },
                })],
                Soldiers = [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = 1 } },
                    .. owner.Soldiers.Skip(1).Select(soldier => soldier with { Personal = soldier.Personal! with { CraftType = "", CraftId = 0 } })],
            }],
        };
        return (content, CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)));
    }

    private static CraftLogisticsState Ship(CampaignState campaign) =>
        campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;

    private static ICampaignLandingQuery Landings(CampaignState campaign) => campaign.GetQuery<ICampaignLandingQuery>()!;

    [Fact]
    public void ACraftReachingALandedUfoAsksToLandWithTheSitesTextureAndShade()
    {
        var (_, campaign) = Create();
        var requested = AssertLandingRequested(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal((WorldTargetKind.Ufo, ufo.UniqueId, "SHIP", 1), (requested.Target.Kind, requested.Target.UniqueId, requested.CraftTypeId, requested.CraftId));
        // The UFO sits on the land polygon with texture 10; Globe::getPolygonTextureAndShade gives the shade.
        Assert.Equal((10, 10), (requested.MissionTexture, requested.GlobeTexture));
        Assert.Equal(WorldShading.LandingShade(campaign.Capture().Time, ufo.Longitude, ufo.Latitude, false), requested.Shade);
        Assert.Equal(requested.Confirmation, Assert.Single(Landings(campaign).QueryLandingConfirmations()).Confirmation);

        // The confirmation holds the clock until it is answered.
        AssertTimeBlocked(campaign, WorldCommandReasons.LandingConfirmationsOpen);
        Assert.Equal(WorldCommandReasons.NoLandingConfirmation, Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new AnswerLanding(requested.Confirmation + 1, LandingAnswer.No)).Events)).Reason);

        // No: Craft::returnToBase.
        Assert.IsType<CraftDestinationChanged>(Assert.Single(campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.No)).Events));
        Assert.Equal(WorldTargetKind.Base, Ship(campaign).Destination!.Kind);
        Assert.Empty(Landings(campaign).QueryLandingConfirmations());
        AdvanceUnblocked(campaign, 1);
    }

    [Fact]
    public void PatrolLeavesTheCraftCirclingAtHalfSpeed()
    {
        var (_, campaign) = Create();
        var requested = AssertLandingRequested(campaign);
        var before = Ship(campaign);
        Assert.Single(campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Patrol)).Events);
        var after = Ship(campaign);
        Assert.Null(after.Destination);
        Assert.Equal(before.Speed / 2, after.Speed);
        AdvanceUnblocked(campaign, 1);
    }

    [Fact]
    public void AReloadDropsTheConfirmationAndTheCraftStillAtItsTargetAsksAgain()
    {
        var (content, campaign) = Create(seasons: true);
        var first = AssertLandingRequested(campaign);
        var reloaded = Reload(content, campaign, 17, "landing.sav");
        Assert.True(reloaded.Options.GlobeSeasons);
        Assert.Empty(Landings(reloaded).QueryLandingConfirmations());
        var again = AssertLandingRequested(reloaded);
        Assert.Equal(first with { Shade = again.Shade }, again);
        var ufo = Assert.Single(reloaded.Capture().World.Ufos);
        Assert.Equal(WorldShading.LandingShade(reloaded.Capture().Time, ufo.Longitude, ufo.Latitude, true), again.Shade);
    }

    [Fact]
    public void ACraftWithoutUnitsDoesNotAskAndItsCrashedTargetSendsItHome()
    {
        var (_, campaign) = Create();
        var snapshot = campaign.Capture();
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            World = snapshot.World with { Ufos = [snapshot.World.Ufos[0] with { Status = UfoStatus.Crashed, SecondsRemaining = 7200 }] },
            Bases = [owner with { Soldiers = [.. owner.Soldiers.Select(s => s with { Personal = s.Personal! with { CraftType = "", CraftId = 0 } })] }],
        };
        campaign = CampaignState.Restore(snapshot, StrategicReadinessTestContent.Load("strategic-world.rul"), new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(Landings(campaign).QueryLandingConfirmations());
        Assert.Equal(WorldTargetKind.Base, Ship(campaign).Destination!.Kind);
    }

    // The UFO deployment names a starting condition; a texture condition applies only without one.
    private const string Conditions = """
        alienDeployments:
          - type: UFO_SCOUT
            startingCondition: SC_DEPLOYMENT
        startingConditions:
          - type: SC_DEPLOYMENT
            requiredItems: {SUPPLY: 1000}
          - type: SC_TEXTURE
            forbiddenCraft: [SHIP]
        globe:
          textures:
            - id: 10
              startingCondition: SC_TEXTURE

        """;

    [Fact]
    public void YesChecksTheDeploymentsStartingConditionAndAFailureSendsTheCraftHome()
    {
        var (_, campaign) = Create(rules: Conditions);
        var requested = AssertLandingRequested(campaign);
        var result = campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Yes));
        Assert.Equal(new LandingRefused(requested.BaseId, "SHIP", 1, "STR_STARTING_CONDITION_ITEM"), Assert.Single(result.Events));
        Assert.Equal(WorldTargetKind.Base, Ship(campaign).Destination!.Kind);
        Assert.Empty(Landings(campaign).QueryLandingConfirmations());
    }

    [Fact]
    public void WithoutADeploymentConditionTheMissionTexturesConditionApplies()
    {
        var (_, campaign) = Create(rules: Conditions.Replace("    startingCondition: SC_DEPLOYMENT\n", "", StringComparison.Ordinal));
        var requested = AssertLandingRequested(campaign);
        var result = campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Yes));
        Assert.Equal("STR_STARTING_CONDITION_CRAFT", Assert.IsType<LandingRefused>(Assert.Single(result.Events)).TextKey);
    }

    [Fact]
    public void APassingConditionStillWaitsForTheDeploymentRequest()
    {
        var (_, campaign) = Create();
        var requested = AssertLandingRequested(campaign);
        var before = campaign.Capture();
        Assert.Equal(WorldCommandReasons.LandingBoundary, Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Yes)).Events)).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Single(Landings(campaign).QueryLandingConfirmations());
    }
}
