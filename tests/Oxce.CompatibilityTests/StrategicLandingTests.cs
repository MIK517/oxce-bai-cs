using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S6: a craft that may land reaching a landed UFO queues a <c>ConfirmLandingState</c> with the
/// site's texture and shade. The queue pauses time and is not saved; No and Patrol answer it, and Yes
/// checks the starting condition and records the deployment request (D4).
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
    public void YesRecordsTheDeploymentRequestThatHoldsTheCampaignUntilTheBattle()
    {
        var (content, campaign) = Create();
        var requested = AssertLandingRequested(campaign);
        var result = campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Yes));
        var request = Assert.IsType<DeploymentRequested>(Assert.Single(result.Events)).Request;
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        var soldier = campaign.Capture().Bases[0].Soldiers[0].Id;
        Assert.Equal(("STR_UFO_GROUND_ASSAULT", "RACE_A", 10, 10, requested.Shade, "SHIP", 1),
            (request.MissionType, request.Race, request.MissionTexture, request.GlobeTexture, request.Shade, request.CraftTypeId, request.CraftId));
        Assert.Equal((WorldTargetKind.Ufo, ufo.UniqueId), (request.Target.Kind, request.Target.UniqueId));
        Assert.Equal([soldier], request.Soldiers);
        Assert.Equal(request, Landings(campaign).QueryDeploymentRequest());
        // The craft keeps its pre-battle state; the campaign waits for the battle.
        Assert.Equal(WorldTargetKind.Ufo, Ship(campaign).Destination!.Kind);
        AssertTimeBlocked(campaign, WorldCommandReasons.DeploymentRequestPending);
        // Every other command waits too, so nothing changes the craft or the world before the battle.
        var before = campaign.Capture();
        Assert.Equal(WorldCommandReasons.DeploymentRequestPending, Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new RecallCraft(request.BaseId, "SHIP", 1)).Events)).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);

        // The request survives a reload, and the Phase 7 seam takes it exactly once.
        var reloaded = Reload(content, campaign, 17, "deployment.sav");
        Assert.Equivalent(request, Landings(reloaded).QueryDeploymentRequest(), strict: true);
        Assert.Equivalent(request, Assert.IsType<DeploymentRequestTaken>(Assert.Single(reloaded.Execute(new TakeDeploymentRequest()).Events)).Request,
            strict: true);
        Assert.Equal(WorldCommandReasons.NoDeploymentRequest, Assert.IsType<CampaignActionBlocked>(Assert.Single(
            reloaded.Execute(new TakeDeploymentRequest()).Events)).Reason);
        AssertTimeBlocked(reloaded, WorldCommandReasons.DeploymentRequestPending);
    }

    [Fact]
    public void APassingConditionWithDestroyRequiredItemsConsumesThemBeforeTheRequest()
    {
        const string rules = """
            alienDeployments:
              - type: UFO_SCOUT
                startingCondition: SC_KEY
            startingConditions:
              - type: SC_KEY
                requiredItems: {SUPPLY: 1}
                destroyRequiredItems: true

            """;
        var (_, campaign) = Create(rules: rules);
        var snapshot = campaign.Capture().WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Items = new Dictionary<string, int> { ["SUPPLY"] = 3 } },
        });
        campaign = CampaignState.Restore(snapshot, StrategicReadinessTestContent.Load(["strategic-world.rul"], rules), new SplitMix64RandomSource(17));
        var requested = AssertLandingRequested(campaign);
        var request = Assert.IsType<DeploymentRequested>(Assert.Single(
            campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Yes)).Events)).Request;
        Assert.Equal(2, request.Items["SUPPLY"]);
        Assert.Equal(2, Ship(campaign).Items["SUPPLY"]);
    }

    private static GeoscapeClient Client(RuntimeContent content, CampaignState campaign) =>
        StrategicWorldGeoscapeScenarioTests.Client(content, CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            save: static () => { }, load: static () => throw new InvalidOperationException("No save to load."));

    private static void Press(GeoscapeClient client, uint key, InputKeyModifiers modifiers = InputKeyModifiers.None) =>
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, modifiers));

    [Fact]
    public void TheGeoscapeAsksToLandAndControlEscapeAnswersPatrol()
    {
        var (content, campaign) = Create();
        var client = Client(content, campaign);
        var before = Ship(campaign);
        client.Tick(GeoscapeTimeController.GeoClock);
        var popup = Assert.Single(client.Popups);
        Assert.Equal(["STR_YES", "STR_NO"], popup.Actions.Select(static action => action.Label));
        // ConfirmLandingState::togglePatrolButton: held Ctrl turns No into Patrol, and Esc presses it.
        Press(client, 0x400000E0, InputKeyModifiers.LeftControl);
        Press(client, 27, InputKeyModifiers.LeftControl);
        Assert.Empty(client.Popups);
        Assert.Null(Ship(campaign).Destination);
        Assert.Equal(before.Speed / 2, Ship(campaign).Speed);
    }

    [Fact]
    public void TheGlobeShadesSeasonsAsTheCampaignOptionSays()
    {
        var (plainContent, plain) = Create();
        Assert.False(Client(plainContent, plain).Options.Seasons);
        var (content, campaign) = Create(seasons: true);
        Assert.True(Client(content, campaign).Options.Seasons);
    }

    [Fact]
    public void TheGeoscapeShowsTheBattleRequestAndReopensItAfterALoad()
    {
        var (content, campaign) = Create();
        var client = Client(content, campaign);
        client.Tick(GeoscapeTimeController.GeoClock);
        Press(client, 13);
        var popup = Assert.Single(client.Popups);
        Assert.Equal("Tactical combat is not available yet", popup.Title);
        Assert.Equal("STR_UFO_GROUND_ASSAULT", popup.Lines[0]);
        var reloaded = Reload(content, campaign, 17, "deployment-ui.sav");
        Assert.Equal("Tactical combat is not available yet", Assert.Single(Client(content, reloaded).Popups).Title);
    }
}

/// <summary>Branch 5 S7: the Cydonia button and ConfirmCydoniaState record the final mission's battle.</summary>
public sealed class StrategicFinalMissionTests
{
    private const string Rules = """
        crafts:
          - type: SHIP
            spacecraft: true
            fuelMax: 100
        research:
          - {name: FINAL_TOPIC, cost: 1, unlockFinalMission: true}
        alienDeployments:
          - type: STR_FINAL_ASSAULT
            finalDestination: true
            race: RACE_B

        """;

    private static (RuntimeContent Content, CampaignSnapshot Snapshot) CreateCrewedSpacecraft()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], Rules);
        var snapshot = CreateSnapshot(content, "Final mission").WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_READY", Fuel = 100, Weapons = [null, null], Items = new Dictionary<string, int>() },
        });
        var owner = snapshot.Bases[0];
        return (content, snapshot with
        {
            Bases = [owner with { Soldiers = [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = 1 } },
                .. owner.Soldiers.Skip(1)] }],
        });
    }

    [Fact]
    public void ASpacecraftWithCrewFliesTheFinalMissionOnceItsResearchIsDone()
    {
        var (content, snapshot) = CreateCrewedSpacecraft();
        var owner = snapshot.Bases[0];
        var launch = new LaunchFinalMission(owner.Id, "SHIP", 1);

        var early = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(3));
        Assert.Equal(WorldCommandReasons.FinalMissionUnavailable, Assert.IsType<CampaignActionBlocked>(Assert.Single(early.Execute(launch).Events)).Reason);

        var campaign = CampaignState.Restore(snapshot with { CompletedResearch = [.. snapshot.CompletedResearch, "FINAL_TOPIC"] }, content,
            new SplitMix64RandomSource(3));
        var request = Assert.IsType<DeploymentRequested>(Assert.Single(campaign.Execute(launch).Events)).Request;
        Assert.Equal(("STR_FINAL_ASSAULT", "RACE_B", "SHIP", 1, -1), (request.MissionType, request.Race, request.CraftTypeId, request.CraftId,
            request.MissionTexture));
        Assert.Equal([owner.Soldiers[0].Id], request.Soldiers);
        AssertTimeBlocked(campaign, WorldCommandReasons.DeploymentRequestPending);
    }

    [Fact]
    public void TheCydoniaButtonAppearsForTheReadySpacecraftAndItsConfirmationLaunches()
    {
        var (content, snapshot) = CreateCrewedSpacecraft();
        var early = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(3));
        Assert.False(early.GetQuery<ICampaignWorldQuery>()!.QueryWorld().Crafts.Single(static craft => craft.TypeId == "SHIP").CanFlyFinalMission);
        var campaign = CampaignState.Restore(snapshot with { CompletedResearch = [.. snapshot.CompletedResearch, "FINAL_TOPIC"] }, content,
            new SplitMix64RandomSource(3));
        var client = StrategicWorldGeoscapeScenarioTests.Client(content, CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            save: static () => { }, load: static () => throw new InvalidOperationException("No save to load."));
        Assert.True(client.World.Crafts.Single(static craft => craft.TypeId == "SHIP").CanFlyFinalMission);

        // InterceptState launches the ship into SelectDestinationState; its Cydonia button asks ConfirmCydoniaState.
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, 'i', InputKeyModifiers.None));
        var intercept = client.Popups[0].Actions;
        client.Choose(intercept.ToList().FindIndex(static action => action.Intent is InterceptRowIntent { Craft.CraftTypeId: "SHIP" }));
        Assert.NotNull(client.SelectingWing);
        client.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, GeoscapeClient.GlobeWidth + 10, 90, 1, 1));
        var confirm = Assert.Single(client.Popups);
        Assert.Equal("STR_ARE_YOU_SURE_CYDONIA", confirm.Title);
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, 13, InputKeyModifiers.None));
        Assert.Null(client.SelectingWing);
        Assert.Equal("Tactical combat is not available yet", Assert.Single(client.Popups).Title);
        Assert.Equal("STR_FINAL_ASSAULT", campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest()!.MissionType);
    }
}
