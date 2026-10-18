using Oxce.Engine.Geoscape;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class GeoscapeNotificationsTests
{
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["STR_PRODUCTION_OF_ITEM_AT_BASE_IS_COMPLETE"] = "Production of {0} at {1} is complete",
        ["STR_NOT_ENOUGH_MONEY_TO_PRODUCE_ITEM_AT_BASE"] = "No money for {0} at {1}",
        ["STR_NOT_ENOUGH_ITEM_TO_REFUEL_CRAFT_AT_BASE"] = "Not enough {0} to refuel {1} at {2}",
        ["STR_CRAFT_IS_READY"] = "{0} at {1} is ready",
        ["STR_STATUS_"] = "Status: {0}",
        ["STR_INTERCEPTING_UFO"] = "Intercepting UFO-{0}",
        ["STR_NOT_ENOUGH_ITEM_TO_REARM_FACILITY_AT_BASE"] = "Not enough {0} to rearm {1} at {2}",
        ["STR_FUEL_ITEM"] = "Elerium",
        ["STR_LASER_DEFENSE"] = "Laser Defense",
        ["STR_CRAFT_READY_TO_LAND_NEAR_DESTINATION"] = "{0} ready to land near {1}",
        ["STR_BASE_UNDER_ATTACK"] = "{0} is under attack",
        ["STR_ALIEN_MISSILES_HAVE_DESTROYED_OUR_BASE"] = "Missiles destroyed {0}",
        ["STR_THE_ALIENS_HAVE_DESTROYED_THE_UNDEFENDED_BASE"] = "Aliens destroyed {0}",
        ["STR_MONTH"] = "Month> {0} {1}",
        ["STR_JAN"] = "January",
        ["STR_MONTHLY_RATING"] = "Rating> {0} {1}",
        ["STR_COUNTRY_IS_PARTICULARLY_PLEASED"] = "{0} is pleased",
        ["STR_COUNTRIES_HAVE_SIGNED_A_SECRET_PACT"] = "{0} signed pacts",
        ["STR_COUNTRIES_AND"] = "{0} and {1}",
        ["STR_COUNTRIES_COMMA"] = "{0}, {1}",
        ["STR_CITY_EVENT"] = "Riots in {0}",
        ["STR_CITY_EVENT_DESC"] = "Trouble in {0}",
    };

    private static readonly CampaignWorldOverview World = new(
    [
        new(WorldTargetKind.Ufo, 7, "STR_SMALL_SCOUT", "UFO-7", 0.5, 0.2)
        {
            Detected = true,
            Reference = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 7, 0.5, 0.2) { UniqueId = 70 },
        },
    ], 0, 0)
    {
        Bases = [new(1, "Alpha", 0.1, 0.1)],
        Crafts = [new(1, "STR_INTERCEPTOR", 2, "INTERCEPTOR-2", "STR_OUT", 0.2, 0.2, 10, null, null, null)],
    };

    private static string Tr(string key) => Texts.GetValueOrDefault(key, key);

    private static GeoscapePopup? Describe(ICampaignEvent notification) =>
        GeoscapeNotifications.Describe(notification, World, Tr);

    [Fact]
    public void TheMonthlyReportShowsItsLedgerAndCouncilAndItsOkAcknowledges()
    {
        // Branch 6 S7: MonthlyReportState's lines; OK sends the acknowledgement that applies the report's effects.
        var report = new CampaignMonthlyReport(1, 1999, 450, "STR_RATING_GOOD", 700_000, 25_000, 300_000, 4_500, 1_204_500,
            "STR_COUNCIL_IS_GENERALLY_SATISFIED", true, ["STR_USA"], [], ["STR_RUSSIA", "STR_CHINA", "STR_INDIA"], []);
        var popup = Describe(new MonthlyReportReady(report))!;

        Assert.Equal("STR_XCOM_PROJECT_MONTHLY_REPORT", popup.Title);
        Assert.Equal(
        [
            "Month> January 1999", "Rating> 450 STR_RATING_GOOD", "STR_INCOME> $700,000 (+$25,000)", "STR_MAINTENANCE> $300,000",
            "STR_PERFORMANCE_BONUS> $4,500", "STR_BALANCE> $1,204,500", "STR_COUNCIL_IS_GENERALLY_SATISFIED", "STR_COUNCIL_REDUCE_DEBTS",
            "STR_USA is pleased", "STR_RUSSIA, STR_CHINA and STR_INDIA signed pacts",
        ], popup.Lines);
        Assert.Equal([new AcknowledgeMonthlyReport()], popup.Actions.Select(static action => action.Command));
        // A failed month shows the failure screen; its OK plays the lose cutscene.
        var failed = Describe(new MonthlyReportReady(report with { CouncilText = "STR_YOU_HAVE_NOT_SUCCEEDED" } is var r ? r with { GameOver = 1 } : r))!;
        Assert.Equal(("STR_YOU_HAVE_FAILED", "STR_YOU_HAVE_NOT_SUCCEEDED"), (failed.Title, failed.Lines[0]));
        Assert.Equal("Cutscene unavailable", Describe(new MonthlyReportAcknowledged("loseGame"))!.Title);
        Assert.Null(Describe(new MonthlyReportAcknowledged(null)));
    }

    [Fact]
    public void AnEventWindowNamesItsPlaceAndItemsAndItsOkOpensItsFollowingWindowsInOrder()
    {
        var window = new CampaignEventWindow("STR_CITY_EVENT", "STR_NORTH_AMERICA", "Ottawa",
            [new("STR_RIFLE", 3), new("STR_GRENADE", -2)], "", "");
        // Without the rules the description is unknown and the region names the place.
        var popup = Describe(new CampaignEventOpened(window))!;
        Assert.Equal("Riots in STR_NORTH_AMERICA", popup.Title);
        Assert.Equal(["", "STR_ITEMS_ARRIVING", "STR_RIFLE 3", "STR_GRENADE -2"], popup.Lines);
        Assert.Equal([new AcknowledgeCampaignEvent()], popup.Actions.Select(static action => action.Command));

        // btnOkClick pushes the cutscene, the sell screen, then the articles: the research article shows first.
        var ok = GeoscapeNotifications.DescribeAll(new CampaignEventAcknowledged("STR_CITY_EVENT", "winVideo", true, "STR_BONUS", "STR_PLASMA"), World, Tr);
        Assert.Equal(["STR_PLASMA", "STR_BONUS", "STR_STORAGE_EXCEEDED", "Cutscene unavailable"], ok.Select(static p => p.Title));
        Assert.Equal(new BaseIntent(1), ok[2].Actions[0].Intent);
        Assert.Empty(GeoscapeNotifications.DescribeAll(new CampaignEventAcknowledged("STR_CITY_EVENT", null, false, "", ""), World, Tr));
    }

    [Fact]
    public void TheFundingWindowListsEachCountrysFundingAndChangeAndTheTotal()
    {
        var popup = GeoscapeNotifications.Funding([new("STR_USA", 600_000, 50_000), new("STR_RUSSIA", 400_000, -20_000)], Tr);
        Assert.Equal("STR_INTERNATIONAL_RELATIONS", popup.Title);
        Assert.Equal(["STR_COUNTRY | STR_FUNDING | STR_CHANGE", "STR_USA | $600,000 | +$50,000", "STR_RUSSIA | $400,000 | -$20,000",
            "STR_TOTAL_UC | $1,000,000 | +$30,000"], popup.Lines);
    }

    [Fact]
    public void MedalsThePsiReminderAndTheEndingVideoHaveTheirWindows()
    {
        var medals = Describe(new SoldiersCommended([new(4, "STR_MEDAL_SERVICE", "noNoun", 0), new(5, "STR_RACE", "STR_SECTOID", 1)]))!;
        Assert.Equal("STR_MEDALS", medals.Title);
        Assert.Equal(["#4 STR_MEDAL_SERVICE STR_AWARD_0", "#5 STR_RACE STR_SECTOID STR_AWARD_1"], medals.Lines);
        var psi = Describe(new PsiTrainingReminder())!;
        Assert.Equal("STR_PSIONIC_TRAINING", psi.Title);
        Assert.Equal(["Alpha"], psi.Lines);
        Assert.Equal("Cutscene unavailable", Describe(new CampaignCutscenePlayed("winVideo"))!.Title);
        Assert.Equal(["Cutscene: loseGame"], Describe(new CampaignEnded(2) { Cutscene = "loseGame" })!.Lines);
        // The host makes requested saves; they open no window.
        Assert.Null(Describe(new CampaignSaveRequested(CampaignSaveKind.Autosave)));
    }

    [Fact]
    public void ProductionPopsOnlyWhenAProjectEndsWithItsFormattedMessage()
    {
        Assert.Null(Describe(new CampaignProductionProgress(1, "STR_LASER", 3, null)));
        Assert.Equal("Production of STR_LASER at Alpha is complete",
            Describe(new CampaignProductionProgress(1, "STR_LASER", 1, "STR_PRODUCTION_COMPLETE"))!.Title);
        Assert.Equal("No money for STR_LASER at Alpha",
            Describe(new CampaignProductionProgress(1, "STR_LASER", 0, "STR_NOT_ENOUGH_MONEY"))!.Title);
    }

    [Fact]
    public void ServiceMessagesNameTheirItemCraftFacilityAndBase()
    {
        Assert.Equal("Not enough Elerium to refuel INTERCEPTOR-2 at Alpha", Describe(new CraftArrivalServiceMessage(1,
            "STR_INTERCEPTOR", 2, "STR_NOT_ENOUGH_ITEM_TO_REFUEL_CRAFT_AT_BASE")
        { ItemId = "STR_FUEL_ITEM" })!.Title);
        Assert.Equal("INTERCEPTOR-2 at Alpha is ready",
            Describe(new CraftArrivalServiceMessage(1, "STR_INTERCEPTOR", 2, "STR_CRAFT_IS_READY"))!.Title);
        Assert.Equal("Not enough Elerium to rearm Laser Defense at Alpha", Describe(new FacilityServiceMessage(1, 2, 3,
            "STR_NOT_ENOUGH_ITEM_TO_REARM_FACILITY_AT_BASE")
        { ItemId = "STR_FUEL_ITEM", FacilityRuleId = "STR_LASER_DEFENSE" })!.Title);
    }

    [Fact]
    public void ButtonsBindTheReferenceKeysAndSpeedResets()
    {
        // LowFuelState: Enter is "OK - 5 seconds" (timerReset), Esc is OK.
        var fuel = Describe(new CraftLowFuel(1, "STR_INTERCEPTOR", 2))!;
        Assert.Equal("INTERCEPTOR-2", fuel.Title);
        Assert.Equal([(false, false, true), (true, true, false)],
            fuel.Actions.Select(action => (action.ResetSpeed, action.OkKey, action.CancelKey)));

        // UfoDetectedState binds only Cancel; Intercept and Centre reset the speed and centre on the UFO.
        var detected = Describe(new UfoContactDetected(70, Hyperwave: true))!;
        Assert.Equal("UFO-7", detected.Title);
        Assert.Contains("STR_HYPER_WAVE_TRANSMISSIONS_ARE_DECODED", detected.Lines);
        Assert.Equal(["STR_INTERCEPT", "STR_CENTER_ON_UFO_TIME_5_SECONDS", "STR_CANCEL_UC", "STR_IGNORE_UC"],
            detected.Actions.Select(action => action.Label));
        Assert.All(detected.Actions.Take(2), action =>
        {
            Assert.Equal(new WorldPosition(0.5, 0.2), action.Center);
            Assert.True(action.ResetSpeed);
        });
        Assert.Equal(WorldTargetKind.Ufo, Assert.IsType<InterceptIntent>(detected.Actions[0].Intent).Target!.Kind);
        Assert.DoesNotContain(detected.Actions, action => action.OkKey);
        Assert.True(detected.Actions[2].CancelKey);

        // CraftPatrolState: Esc is OK; Enter (Redirect) opens the craft screen without a timer reset.
        var patrol = Describe(new CraftArrivedAtWaypoint(1, "STR_INTERCEPTOR", 2, 5))!;
        Assert.Equal([(false, true), (true, false)], patrol.Actions.Select(action => (action.OkKey, action.CancelKey)));
        Assert.False(patrol.Actions[1].ResetSpeed);
        Assert.Equal(new CraftKey(1, "STR_INTERCEPTOR", 2), Assert.IsType<CraftIntent>(patrol.Actions[1].Intent).Craft);

        // ItemsArrivingState: Enter goes to the base and resets the speed. ProductionCompleteState's
        // base button resets the speed but has no key.
        var goTo = Describe(new SuppliesArrived(1, []))!.Actions[1];
        Assert.Equal((true, true, false, 1), (goTo.ResetSpeed, goTo.OkKey, goTo.CancelKey,
            Assert.IsType<BaseIntent>(goTo.Intent).BaseId));
        var allocate = Describe(new CampaignConstructionCompleted(1, "STR_LAB", 0, 0))!.Actions[1];
        Assert.Equal((true, false, false), (allocate.ResetSpeed, allocate.OkKey, allocate.CancelKey));

        Assert.True(Describe(new CampaignResearchCompleted(1, "STR_LASER", []))!.ResetsSpeedWhenShown);
        var lost = Describe(new UfoContactLost(70))!;
        Assert.True(lost.Actions[0].OkKey && lost.Actions[0].CancelKey);
        Assert.Null(Describe(new CraftDestinationChanged(1, "STR_INTERCEPTOR", 2, null)));
        Assert.Null(Describe(new CampaignArcUnlocked("STR_ARC", "STR_ARTICLE")));
        // SavedGame::spawnEvent only schedules: the popup and reset come when the event fires.
        Assert.Null(Describe(new CampaignStrategicEventRequested("STR_EVENT", "research")));
        Assert.Null(Describe(new CampaignTransformationEventSelected("STR_EVENT")));
    }

    [Fact]
    public void TheUfoAlertListsItsDetailsAndCanIgnoreTheContact()
    {
        var world = World with
        {
            Targets = [World.Targets[0] with
            {
                Altitude = "STR_HIGH_UP",
                Ufo = new CampaignWorldUfoDetails("STR_SMALL", "STR_NORTH_EAST", 2200, "STR_SECTOID", "STR_ALIEN_RESEARCH", "STR_EUROPA"),
            }],
        };
        var plain = GeoscapeNotifications.Describe(new UfoContactDetected(70, Hyperwave: false), world, Tr)!;
        Assert.Equal("STR_DETECTED", plain.Lines[0]);
        Assert.Contains("STR_HEADING . . STR_NORTH_EAST", plain.Lines);
        Assert.Contains("STR_SPEED . . 2,200", plain.Lines);
        Assert.DoesNotContain("STR_RACE . . STR_SECTOID", plain.Lines);
        var ignore = Assert.Single(plain.Actions, action => action.Command is not null);
        Assert.Equal(new IgnoreUfoContact(70, 7), ignore.Command);
        Assert.False(ignore.CancelKey);

        var decoded = GeoscapeNotifications.Describe(new UfoContactDetected(70, Hyperwave: true), world, Tr)!;
        Assert.Contains("STR_RACE . . STR_SECTOID", decoded.Lines);
        Assert.Contains("STR_ZONE . . STR_EUROPA", decoded.Lines);
        var ignored = world with { Targets = [world.Targets[0] with { DetectionAlertIgnored = true }] };
        Assert.DoesNotContain(GeoscapeNotifications.Describe(new UfoContactDetected(70, false), ignored, Tr)!.Actions,
            action => action.Command is not null);
    }

    [Fact]
    public void ALostUfoOffersItsLastPositionAndOtherLostTargetsStaySilent()
    {
        var lostUfo = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 9, 0.7, 0.3) { UniqueId = 90 };
        Assert.Null(Describe(new CraftTargetLost(1, "STR_INTERCEPTOR", 2, lostUfo, ReturningToBase: true)));
        Assert.Null(Describe(new CraftTargetLost(1, "STR_INTERCEPTOR", 2,
            new WorldTargetReference(WorldTargetKind.MissionSite, "STR_TERROR_SITE", 3, 0.7, 0.3), ReturningToBase: false)));

        // A UFO that left radar offers its last position even when the craft is returning (it was destroyed).
        var screen = Describe(new CraftTargetLost(1, "STR_INTERCEPTOR", 2, lostUfo, ReturningToBase: true)
        {
            OffersLastPosition = true,
        })!;
        Assert.Equal("INTERCEPTOR-2", screen.Title);
        Assert.Equal("Status: Intercepting UFO-9", screen.Lines[0]);
        var goTo = screen.Actions[^1];
        Assert.Equal("STR_GO_TO_LAST_KNOWN_UFO_POSITION", goTo.Label);
        Assert.True(goTo.CancelKey);
        Assert.Equal(new DispatchCraftToWaypoint(1, "STR_INTERCEPTOR", 2, 0.7, 0.3), goTo.Command);
    }

    [Fact]
    public void AnAlienBaseIsLocatedByCountryAndRegion()
    {
        CampaignWorldOverview With(string country, string region, string message) => World with
        {
            Targets = [new(WorldTargetKind.AlienBase, 4, "ALIEN_BASE_DEPLOY", "STR_ALIEN_BASE_", 0.4, 0.4)
            {
                Detected = true,
                Reference = new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 4, 0.4, 0.4),
                CountryId = country,
                RegionId = region,
                DiscoveredMessage = message,
            }],
        };
        static string Located(string key) => key switch
        {
            "STR_XCOM_AGENTS_HAVE_LOCATED_AN_ALIEN_BASE_IN_REGION" => "Base in {0}",
            "STR_COUNTRIES_COMMA" => "{0}, {1}",
            "STR_CUSTOM" => "{1} found in {0}",
            _ => key,
        };
        string Title(CampaignWorldOverview world) =>
            GeoscapeNotifications.Describe(new AlienBaseDiscovered(4, "STR_ALIEN_BASE"), world, Located)!.Title;

        Assert.Equal("Base in STR_FRANCE, STR_EUROPA", Title(With("STR_FRANCE", "STR_EUROPA", "")));
        Assert.Equal("Base in STR_EUROPA", Title(With("", "STR_EUROPA", "")));
        Assert.Equal("Base in STR_UNKNOWN", Title(With("", "", "")));
        Assert.Equal("STR_ALIEN_BASE found in STR_EUROPA", Title(With("", "STR_EUROPA", "STR_CUSTOM")));
    }

    [Fact]
    public void MarkersAreNamedByTheirLanguageKeyAndId()
    {
        static string Names(string key) => key switch
        {
            "STR_UFO_" => "UFO-{0}",
            "STR_WAY_POINT_" => "Way point-{0}",
            _ => key,
        };
        var ufo = World.Targets[0] with { Label = "STR_UFO_" };
        Assert.Equal("UFO-7", GeoscapeScreens.TargetLabel(ufo, Names));
        // Target::getName returns a custom name untranslated, even one that is a language key.
        Assert.Equal("STR_UFO_", GeoscapeScreens.TargetLabel(ufo with { HasCustomName = true }, Names));
        var world = World with { Targets = [ufo] };
        Assert.Equal("UFO-7", GeoscapeNotifications.Describe(new UfoContactDetected(70, false), world, Names)!.Title);
        // A UFO already off the radar is named by the marker id it had when lost.
        Assert.Equal("UFO-4", GeoscapeNotifications.Describe(new UfoContactLost(55) { MarkerId = 4 }, world, Names)!.Title);
        var waypoint = new CampaignWorldTarget(WorldTargetKind.Waypoint, 6, WorldTargetReference.WaypointType, "STR_WAY_POINT_", 0.1, 0.1)
        {
            Detected = true,
        };
        Assert.Equal("Way point-6", Assert.Single(GlobeTargets.Build(World with { Targets = [waypoint] }, Names).All,
            target => target.Kind == WorldTargetKind.Waypoint).Label);
    }

    [Fact]
    public void LandingConfirmationAnswersYesOrNoAndPatrolWhileControlIsHeld()
    {
        var target = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 7, 0.5, 0.2) { UniqueId = 70 };
        var popup = Describe(new LandingConfirmationRequested(4, 1, "STR_INTERCEPTOR", 2, target, 3, 3, 0))!;
        Assert.Equal("INTERCEPTOR-2 ready to land near UFO-7", popup.Title);
        Assert.Equal(["STR_BEGIN_MISSION"], popup.Lines);
        Assert.Equal([("STR_YES", true, false), ("STR_NO", false, true)],
            popup.Actions.Select(action => (action.Label, action.OkKey, action.CancelKey)));
        Assert.Equal(new AnswerLanding(4, LandingAnswer.Yes), popup.Actions[0].Command);
        Assert.Equal(new AnswerLanding(4, LandingAnswer.No), popup.Actions[1].Command);
        var patrol = popup.Actions[1].ControlAlternative!;
        Assert.Equal(("STR_PATROL", true, new AnswerLanding(4, LandingAnswer.Patrol)), (patrol.Label, patrol.CancelKey, patrol.Command));
    }

    [Fact]
    public void BaseAttackWindowsFireOrSkipAndReportTheOutcome()
    {
        var defense = Describe(new BaseDefenseStarted(1, 70, InstaHyper: false))!;
        Assert.Equal("Alpha is under attack", defense.Title);
        Assert.Equal(["STR_BASE_DEFENSES_INITIATED", "UFO-7"], defense.Lines);
        Assert.Equal([new ResolveBaseDefense(1, Fire: true), new ResolveBaseDefense(1, Fire: false)],
            defense.Actions.Select(action => action.Command));
        Assert.Equal(["STR_UFO_DESTROYED"], Describe(new BaseDefenseResolved(1, 70, true, false))!.Lines);
        Assert.Null(Describe(new BaseDefenseResolved(1, 70, false, false)));
        Assert.Equal("Missiles destroyed Alpha", Describe(new BaseDestroyed(1, "Alpha", Missiles: true))!.Title);
        Assert.Equal("Aliens destroyed Alpha", Describe(new BaseDestroyed(1, "Alpha"))!.Title);
        Assert.Equal("The campaign is lost", Describe(new CampaignEnded(2))!.Title);
        Assert.Equal("STR_ARMOR_NOT_ALLOWED_ONBOARD",
            Describe(new LandingRefused(1, "STR_INTERCEPTOR", 2, "STR_ARMOR_NOT_ALLOWED_ONBOARD"))!.Title);
    }

    [Fact]
    public void AMissileDamagedBaseListsItsLostFacilitiesAndSendsAnOverfullStoreToTheBase()
    {
        var damaged = Describe(new BaseDamagedByMissiles(1, "Alpha", [new("STR_LASER_DEFENSE", 2)], StoresOverfull: false))!;
        Assert.Equal("STR_ALIEN_MISSILES_HAVE_DAMAGED_OUR_BASE", damaged.Title);
        Assert.Equal(["Laser Defense 2"], damaged.Lines);
        Assert.Null(damaged.Actions.Single().Intent);
        var overfull = Describe(new BaseDamagedByMissiles(1, "Alpha", [], StoresOverfull: true))!;
        Assert.Equal(["STR_STORAGE_EXCEEDED"], overfull.Lines);
        Assert.Equal(new BaseIntent(1), overfull.Actions.Single().Intent);
    }
}
