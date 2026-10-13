using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S3-S5: open interceptions in a campaign. Each step runs the dogfight engine
/// (<see cref="StrategicDogfightOracleTests"/>) against the craft and UFO state, writes the saved fields
/// back and applies the world consequences of its outcomes. Combat itself is never saved, as in the
/// reference.
/// </summary>
public sealed class StrategicInterceptionTests
{
    private const string Rules = """
        crafts:
          - type: SHIP
            weapons: 2
        craftWeapons:
          - type: DOGFIGHT_CANNON
            launcher: SUPPLY
            clip: BULKY
            ammoMax: 200
            rearmRate: 200
            damage: 10
            range: 10
            accuracy: 25
            reloadCautious: 24
            reloadStandard: 16
            reloadAggressive: 8
            projectileType: 2
            projectileSpeed: 8
          - type: DOGFIGHT_TRACTOR
            launcher: SUPPLY
            range: 20
            tractorBeamPower: 1100
        """;

    private static (RuntimeContent Content, CampaignState Campaign, CampaignWorld World) Create(int ufoDamage = 0, ulong seed = 7,
        bool tractor = false, int ufoLandId = 0, string shipRules = "", double longitude = 0.4, double latitude = 0.2, int craftDamage = 0,
        string extraRules = "", bool singlePilot = false)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], Rules.Replace("    weapons: 2\n", "    weapons: 2\n" + shipRules, StringComparison.Ordinal) + extraRules);
        var snapshot = CreateSnapshot(content, "Interception");
        var ufo = new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_PATROL", 0, longitude, latitude, UfoStatus.Flying, WorldAltitudes.High)
        {
            Id = 3,
            Speed = 1000,
            MissionWaveNumber = 0,
            Detected = true,
            Damage = ufoDamage,
            LandId = ufoLandId,
            Destination = new WorldTargetReference(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0, 1.0, 0.6),
        };
        snapshot = (snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 0, 5),
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_SCOUT", "REGION", "RACE_A", 1, 0, 1500, 1, -1)],
                Ufos = [ufo],
            },
        }).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 100,
                Speed = 6000,
                Longitude = longitude,
                Latitude = latitude,
                Damage = craftDamage,
                Weapons = [new CraftWeaponSnapshot("DOGFIGHT_CANNON", 200, false, false),
                    tractor ? new CraftWeaponSnapshot("DOGFIGHT_TRACTOR", 0, false, false) : null],
                Items = new Dictionary<string, int>(),
                Destination = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, longitude, latitude) { UniqueId = 9 },
            },
        });
        if (singlePilot)
        {
            // The first soldier is the ship's only crew member and pilot.
            var owner = snapshot.Bases[0];
            snapshot = snapshot with
            {
                Bases = [owner with
                {
                    Soldiers = [.. owner.Soldiers.Select((soldier, index) => soldier with
                    {
                        Personal = soldier.Personal! with { CraftType = index == 0 ? "SHIP" : "", CraftId = index == 0 ? 1 : 0 },
                    })],
                }, .. snapshot.Bases.Skip(1)],
            };
        }
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(seed));
        var world = (CampaignWorld)campaign.GetQuery<ICampaignInterceptionQuery>()!;
        return (content, campaign, world);
    }

    private static int Start(CampaignState campaign, CampaignWorld world)
    {
        var owner = campaign.BaseStates[0];
        var index = owner.Crafts.FindIndex(craft => craft.Logistics?.Status == "STR_OUT");
        return world.StartInterception(owner, index, 9, ufoIsAttacking: false);
    }

    private static CampaignInterception Single(CampaignWorld world) => Assert.Single(world.QueryInterceptions());

    [Fact]
    public void AStartedInterceptionApproachesAndFightsInTheChosenMode()
    {
        var (_, campaign, world) = Create();
        var number = Start(campaign, world);
        Assert.Equal(1, number);
        var opened = Single(world);
        Assert.Equal((InterceptionMode.Standoff, 640, 560, false), (opened.Mode, opened.Distance, opened.TargetDistance, opened.UfoAttacking));
        Assert.Equal([InterceptionMode.Standoff, InterceptionMode.Cautious, InterceptionMode.Standard, InterceptionMode.Aggressive,
            InterceptionMode.Disengage], opened.AvailableModes);
        // The constructor drew the escape countdown and stored it on the saved UFO.
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).EscapeCountdown > 0);

        for (var step = 0; step < 10; step++) Assert.Empty(campaign.Execute(new AdvanceInterceptions()).Events);
        Assert.Equal(620, Single(world).Distance);

        Assert.Empty(campaign.Execute(new SetInterceptionMode(number, InterceptionMode.Aggressive)).Events);
        Assert.Equal((InterceptionMode.Aggressive, 64, "STR_AGGRESSIVE_ATTACK"),
            (Single(world).Mode, Single(world).TargetDistance, Single(world).Status));

        // At aggressive range the cannon fires and the ammunition is written back to the craft.
        while (Single(world).Distance > 80) campaign.Execute(new AdvanceInterceptions());
        campaign.Execute(new AdvanceInterceptions());
        var craft = campaign.Capture().Bases[0].Crafts.Single(c => c.Logistics?.Status == "STR_OUT");
        Assert.Equal(199, craft.Logistics!.Weapons[0]!.Ammo);
        Assert.Equal(199, Single(world).Weapons[0].Ammo);
    }

    // Steps the window in aggressive mode until it ends.
    private static void FightToTheEnd(CampaignState campaign, CampaignWorld world, int number)
    {
        campaign.Execute(new SetInterceptionMode(number, InterceptionMode.Aggressive));
        for (var step = 0; step < 5000; step++)
        {
            var result = campaign.Execute(new AdvanceInterceptions());
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            if (result.Events.OfType<InterceptionEnded>().Any())
            {
                Assert.Empty(world.QueryInterceptions());
                return;
            }
        }
        Assert.Fail("The interception did not end.");
    }

    [Fact]
    public void AScoutShotDownAtSeaIsDestroyedDelaysItsMissionAndTheCraftReturns()
    {
        // One more hit of 5-10 damage crashes the 50-point scout over the ocean, where it cannot survive.
        var (_, campaign, world) = Create(ufoDamage: 24);
        var number = Start(campaign, world);
        FightToTheEnd(campaign, world, number);

        var captured = campaign.Capture();
        var ufo = Assert.Single(captured.World.Ufos);
        Assert.Equal((UfoStatus.Destroyed, 0, 0), (ufo.Status, ufo.Speed, ufo.CrashId));
        // AlienMission::ufoShotDown: wave 1 of 2 is still to come, so it waits 30 * (48..448) seconds longer.
        var mission = captured.World.Missions.Single(m => m.Id == 4);
        Assert.InRange(mission.SpawnCountdown - 1500, 30 * 48, 30 * 448);
        Assert.Equal(0, (mission.SpawnCountdown - 1500) % 30);
        var craft = captured.Bases[0].Crafts.Single(c => c.Logistics?.Status == "STR_OUT").Logistics!;
        Assert.Equal(WorldTargetKind.Base, craft.Destination!.Kind);

        // The next tick removes the UFO and releases its mission's live count.
        campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Equal(0, campaign.Capture().World.Missions.Single(m => m.Id == 4).LiveUfos);
    }

    [Fact]
    public void AScoutShotDownOverLandCrashesWithACrashSiteAndCraftsThatCannotLandGoHome()
    {
        var (content, campaign, world) = Create(ufoDamage: 24, longitude: Degrees(10), latitude: 0);
        var number = Start(campaign, world);
        FightToTheEnd(campaign, world, number);

        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal((UfoStatus.Crashed, WorldAltitudes.Ground, 1, 0), (ufo.Status, ufo.Altitude, ufo.CrashId, ufo.Speed));
        Assert.Null(ufo.Destination);
        Assert.InRange(ufo.SecondsRemaining, 24 * 3600, 96 * 3600);
        Assert.Equal(0, ufo.SecondsRemaining % 3600);
        Assert.Equal(2, campaign.Capture().NextIds["STR_CRASH_SITE"]);
        // The crew-less ship cannot land at the crash site and returns to base.
        var craft = campaign.Capture().Bases[0].Crafts.Single(c => c.Logistics?.Status == "STR_OUT").Logistics!;
        Assert.Equal(WorldTargetKind.Base, craft.Destination!.Kind);

        // The crash site survives a reload and keeps counting down. Ufo::load gives the crashed UFO a
        // waypoint at its own position for the destination it no longer has, as the reference does.
        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(campaign.Capture()), content, 7, "crash.sav").Campaign;
        Assert.Equivalent(ufo with { Destination = WorldTargetReference.ForWaypoint(0, ufo.Position) }, Assert.Single(reloaded.Capture().World.Ufos), strict: true);
        // Its timer runs in time30Minutes: 1:00:05 reaches 1:30 after 359 ticks.
        AdvanceUnblocked(reloaded, 359);
        Assert.Equal(ufo.SecondsRemaining - 1800, Assert.Single(reloaded.Capture().World.Ufos).SecondsRemaining);
    }

    [Fact]
    public void PilotsOfTheWinningCraftGainExperienceRecordedInTheirDiaryAndTodaysCache()
    {
        const string pilotRules = "\nsoldiers:\n  - type: RECRUIT\n    dogfightExperience: {firing: 100, reactions: 100, bravery: 100}\n" +
            "    statCaps: {firing: 100, reactions: 100, bravery: 100}\n";
        var (content, campaign, world) = Create(ufoDamage: 24, shipRules: "    soldiers: 1\n    pilots: 1\n", extraRules: pilotRules,
            singlePilot: true);
        var before = campaign.Capture().Bases[0].Soldiers[0].Personal!;
        var number = Start(campaign, world);
        FightToTheEnd(campaign, world, number);

        var pilot = campaign.Capture().Bases[0].Soldiers[0].Personal!;
        Assert.Equal((before.CurrentStats["firing"] + 1, before.CurrentStats["reactions"] + 1, before.CurrentStats["bravery"] + 10),
            ((int)pilot.CurrentStats["firing"], (int)pilot.CurrentStats["reactions"], (int)pilot.CurrentStats["bravery"]));
        Assert.Equal(new SoldierDogfightExperience(1, 1, 10), pilot.DailyDogfightExperience);
        // SoldierDiary::addUfoShotDown records the UFO's maximum damage.
        Assert.Equal((1, 50), (pilot.UfosShotDownTotal, pilot.UfosDamageTotal));
        Assert.Equal([campaign.Capture().Bases[0].Soldiers[0].Id], campaign.Capture().Bases[0].Crafts.Single(c => c.RuleId == "SHIP").Logistics!.Pilots);

        // The next tick removes the destroyed UFO, whose cleared destination a reload would not keep.
        AdvanceUnblocked(campaign, 1);
        var reloaded = Reload(content, campaign, 7, "pilot.sav");
        Assert.Equal(new SoldierDogfightExperience(1, 1, 10), reloaded.Capture().Bases[0].Soldiers[0].Personal!.DailyDogfightExperience);
        // GeoscapeState::time1Day clears the cache.
        AdvanceUnblocked(reloaded, 23 * 720);
        var cleared = reloaded.Capture().Bases[0].Soldiers[0].Personal!;
        Assert.Equal((SoldierDogfightExperience.None, 1), (cleared.DailyDogfightExperience, cleared.UfosShotDownTotal));
    }

    [Fact]
    public void ATractorBeamForcesALandingThatKeepsItsLandingId()
    {
        // 1100 power x 200% (small UFO) cancels the scout's 2200 speed: the UFO lands without a new ID.
        var (_, campaign, world) = Create(tractor: true, ufoLandId: 5, longitude: Degrees(10), latitude: 0);
        var number = Start(campaign, world);
        FightToTheEnd(campaign, world, number);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal((UfoStatus.Landed, WorldAltitudes.Ground, 5, 0), (ufo.Status, ufo.Altitude, ufo.LandId, ufo.Speed));
        Assert.InRange(ufo.SecondsRemaining, 30 * 60, 120 * 60);
        Assert.Equal(0, ufo.SecondsRemaining % 60);
        Assert.False(campaign.Capture().NextIds.ContainsKey("STR_LANDING_SITE"));
    }

    [Fact]
    public void ACraftDestroyedInAnInterceptionIsLostOnTheNextTick()
    {
        var (content, campaign, _) = Create(craftDamage: 100);
        var before = campaign.Capture().Bases[0].Crafts.Count;
        campaign.Execute(new AdvanceCampaignTime(1));
        var captured = campaign.Capture();
        Assert.Equal(before - 1, captured.Bases[0].Crafts.Count);
        Assert.DoesNotContain(captured.Bases[0].Crafts, craft => craft.Logistics?.Status == "STR_OUT");
        Assert.Equal(1, captured.World.CraftLostDogfight);
        // Craft::evacuateCrew: with the default 100% survival chance the crew travel home as personnel.
        Assert.DoesNotContain(captured.Bases[0].Soldiers, soldier => soldier.Personal!.CraftType == "SHIP" && soldier.Personal.CraftId == 1);
        Assert.Equal(1, Reload(content, campaign, 7, "lost.sav").Capture().World.CraftLostDogfight);
    }

    [Fact]
    public void DisengagingReturnsTheCraftToBaseAndEndsTheInterception()
    {
        var (_, campaign, world) = Create();
        var number = Start(campaign, world);
        Assert.Empty(campaign.Execute(new SetInterceptionMode(number, InterceptionMode.Disengage)).Events);
        Assert.Equal(800, Single(world).TargetDistance);

        CampaignCommandResult result;
        do result = campaign.Execute(new AdvanceInterceptions());
        while (!result.Events.OfType<InterceptionEnded>().Any());

        Assert.Equal(new InterceptionEnded(number, campaign.Capture().Bases[0].Id, "SHIP", 1, 9), Assert.Single(result.Events));
        Assert.Empty(world.QueryInterceptions());
        var state = campaign.Capture().Bases[0].Crafts.Single(c => c.Logistics?.Status == "STR_OUT").Logistics!;
        Assert.Equal(WorldTargetKind.Base, state.Destination!.Kind);
        Assert.Equal(0, state.InterceptionOrder);
    }

    [Fact]
    public void MinimizeWorksOnlyAtStandoffAndAWeaponToggleIsRememberedWhenTheOptionIsOn()
    {
        var (_, campaign, world) = Create();
        var number = Start(campaign, world);
        Assert.Empty(campaign.Execute(new MinimizeInterception(number)).Events);
        Assert.True(Single(world).Minimized);
        Assert.NotEmpty(campaign.Execute(new SetInterceptionMode(number, InterceptionMode.Aggressive)).Events);
        // A minimized window does not move: GeoscapeState lets time run instead.
        campaign.Execute(new AdvanceInterceptions());
        Assert.Equal(640, Single(world).Distance);
        Assert.Empty(campaign.Execute(new RestoreInterception(number)).Events);

        campaign.Execute(new SetInterceptionMode(number, InterceptionMode.Aggressive));
        for (var step = 0; step < 50; step++) campaign.Execute(new AdvanceInterceptions());
        Assert.Empty(campaign.Execute(new MinimizeInterception(number)).Events);
        Assert.False(Single(world).Minimized);
        Assert.Equal("STR_MINIMISE_AT_STANDOFF_RANGE_ONLY", Single(world).Status);

        Assert.Empty(campaign.Execute(new ToggleInterceptionWeapon(number, 0)).Events);
        Assert.False(Single(world).Weapons[0].Enabled);
        Assert.False(campaign.Capture().Bases[0].Crafts.Single(c => c.Logistics?.Status == "STR_OUT").Logistics!.Weapons[0]!.Disabled);
        Assert.NotEmpty(campaign.Execute(new ToggleInterceptionWeapon(number, 1)).Events);
    }

    [Fact]
    public void RememberedWeaponTogglesPersistAndAReloadDropsTheInterception()
    {
        var (content, campaign, world) = Create();
        var snapshot = campaign.Capture() with { Options = campaign.Options with { RememberDisabledCraftWeapons = true } };
        campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(7));
        world = (CampaignWorld)campaign.GetQuery<ICampaignInterceptionQuery>()!;
        var number = Start(campaign, world);
        campaign.Execute(new ToggleInterceptionWeapon(number, 0));
        for (var step = 0; step < 5; step++) campaign.Execute(new AdvanceInterceptions());

        var reloaded = Reload(content, campaign, 7, "interception.sav");
        Assert.Empty(reloaded.GetQuery<ICampaignInterceptionQuery>()!.QueryInterceptions());
        var ufo = Assert.Single(reloaded.Capture().World.Ufos);
        Assert.Equal(Assert.Single(campaign.Capture().World.Ufos).EscapeCountdown, ufo.EscapeCountdown);
        Assert.True(reloaded.Capture().Bases[0].Crafts.Single(c => c.Logistics?.Status == "STR_OUT").Logistics!.Weapons[0]!.Disabled);
    }

    [Fact]
    public void TimeRunsOnlyWhileEveryInterceptionIsMinimized()
    {
        var (_, campaign, world) = Create();
        var number = Start(campaign, world);
        AssertTimeBlocked(campaign, WorldCommandReasons.InterceptionsOpen);
        Assert.Empty(campaign.Execute(new MinimizeInterception(number)).Events);
        AdvanceOne(campaign);
        Assert.Empty(campaign.Execute(new RestoreInterception(number)).Events);
        AssertTimeBlocked(campaign, WorldCommandReasons.InterceptionsOpen);
    }

    [Fact]
    public void ACraftReachingItsUfoOpensAWindowThatAWaterOnlyCraftMustWaitIn()
    {
        // Branch 5 S4: the craft pass opens the window; a water-only craft below the UFO's altitude gets
        // DogfightErrorState's depth message and a minimized, waiting window, so time keeps running.
        var (_, campaign, world) = Create(shipRules: "    maxAltitude: 1\n");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        var started = Assert.Single(result.Events.OfType<InterceptionStarted>());
        Assert.False(started.UfoAttacking);
        Assert.Equal(new InterceptionWaiting(started.Interception, "STR_UNABLE_TO_ENGAGE_DEPTH"), Assert.Single(result.Events.OfType<InterceptionWaiting>()));
        Assert.True(Single(world).Minimized);
        AdvanceOne(campaign);
        Assert.Single(world.QueryInterceptions());
    }

    [Fact]
    public void ACraftReachingItsUfoOpensAnOrdinaryWindow()
    {
        var (_, campaign, world) = Create();
        var started = AssertInterceptionStarts(campaign, ufoAttacking: false);
        Assert.Equal(1, started.Interception);
        Assert.Equal((InterceptionMode.Standoff, false), (Single(world).Mode, Single(world).Minimized));
        AssertTimeBlocked(campaign, WorldCommandReasons.InterceptionsOpen);
    }

    [Fact]
    public void TheGeoscapeStepsAnOpenWindowAndRunsTimeOnlyOnceItIsMinimized()
    {
        var (content, campaign, world) = Create();
        Start(campaign, world);
        var client = StrategicWorldGeoscapeScenarioTests.Client(content, CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            save: static () => { }, load: static () => throw new InvalidOperationException("No save to load."));
        void Click(int x, int y) =>
            client.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, 1, 1));
        Assert.Equal(640, Assert.Single(client.Interceptions).Distance);
        Assert.False(client.TimeCanRun);

        // GeoscapeState::think: the dogfight timer steps the open window; game time waits.
        var time = campaign.Capture().Time;
        for (var step = 0; step < 10; step++) client.Tick(GeoscapeClient.DogfightClock);
        Assert.Equal(620, Assert.Single(client.Interceptions).Distance);
        Assert.Equal(time, campaign.Capture().Time);

        // The first window's mode column: Aggressive, then back to Standoff, then Minimise.
        Click(165, 36 + 3 * 16 + 5);
        Assert.Equal(InterceptionMode.Aggressive, client.Interceptions[0].Mode);
        Click(165, 36 + 5);
        Click(165, 36 + 5 * 16 + 6 + 5);
        Assert.True(client.Interceptions[0].Minimized);
        Assert.True(client.TimeCanRun);
        client.Tick(GeoscapeTimeController.GeoClock);
        Assert.NotEqual(time, campaign.Capture().Time);

        // The minimized icon restores the window, which stops the clock again.
        Click(10, GeoscapeClient.Height - 20);
        Assert.False(client.Interceptions[0].Minimized);
        Assert.False(client.TimeCanRun);
    }

    /// <summary>
    /// Branch 5 S9: the whole chain through the geoscape client. The crewed ship reaches its UFO, the
    /// interception window opens and fights it down over land, the ship then asks to land at the crash
    /// site, Yes records the battle request, and a reload reopens its window.
    /// </summary>
    [Fact]
    public void AnInterceptionShootsAUfoDownAndTheLandingRequestsItsBattleThroughTheGeoscape()
    {
        var (content, campaign, _) = Create(ufoDamage: 24, longitude: Degrees(10), latitude: 0, singlePilot: true);
        var client = StrategicWorldGeoscapeScenarioTests.Client(content, CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            save: static () => { }, load: static () => throw new InvalidOperationException("No save to load."));
        void Click(int x, int y) =>
            client.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, 1, 1));
        // Closes the popups that only inform; returns the first one that asks something.
        GeoscapePopup? Settle()
        {
            while (client.Popups.Count != 0)
            {
                var popup = client.Popups[0];
                if (popup.Actions.Any(static action => action.Command is not null)) return popup;
                client.Choose(popup.Actions.ToList().FindIndex(static action => action.Intent is null && action.Disabled is null));
            }
            return null;
        }

        // The first tick brings the ship to its UFO and opens the window.
        client.Tick(GeoscapeTimeController.GeoClock);
        Assert.Null(Settle());
        var window = Assert.Single(client.Interceptions);
        Assert.Equal(("SHIP", 9), (window.CraftTypeId, window.UfoUniqueId));

        // Aggressive, then let the dogfight timer fight it out.
        Click(165, 36 + 3 * 16 + 5);
        for (var frame = 0; frame < 20_000 && client.Interceptions.Count != 0; frame++)
        {
            Assert.Null(Settle());
            client.Tick(GeoscapeClient.DogfightClock);
        }
        Assert.Empty(client.Interceptions);
        Assert.Equal(UfoStatus.Crashed, Assert.Single(campaign.Capture().World.Ufos).Status);

        // Time runs again; the crewed ship over the crash site asks to land, and Yes requests the battle.
        GeoscapePopup? landing = null;
        for (var frame = 0; frame < 1_000 && landing is null; frame++)
        {
            landing = Settle();
            if (landing is null) client.Tick(GeoscapeTimeController.GeoClock);
        }
        Assert.Equal(["STR_YES", "STR_NO"], landing!.Actions.Select(static action => action.Label));
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, 13, InputKeyModifiers.None));
        Assert.Equal("Tactical combat is not available yet", Assert.Single(client.Popups).Title);
        var request = campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest()!;
        Assert.Equal(("STR_UFO_CRASH_RECOVERY", WorldTargetKind.Ufo, 9), (request.MissionType, request.Target.Kind, request.Target.UniqueId));

        // Ufo::load gives the crashed UFO a waypoint for its missing destination, so only the request is compared.
        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(campaign.Capture()), content, 7,
            "interception-scenario.sav").Campaign;
        Assert.Equivalent(request, reloaded.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest(), strict: true);
        var again = StrategicWorldGeoscapeScenarioTests.Client(content, CampaignUiSession.ForCampaign(reloaded, reloaded, reloaded),
            save: static () => { }, load: static () => throw new InvalidOperationException("No save to load."));
        Assert.Equal("Tactical combat is not available yet", Assert.Single(again.Popups).Title);
    }

    [Fact]
    public void TheGeoscapeShowsTheDepthMessageAndLetsTimeRunBesideTheWaitingWindow()
    {
        // TFTD depth: a water-only craft below the UFO's altitude gets DogfightErrorState's message; its
        // window waits minimized, so the clock keeps running.
        var (content, campaign, _) = Create(shipRules: "    maxAltitude: 1\n");
        var client = StrategicWorldGeoscapeScenarioTests.Client(content, CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            save: static () => { }, load: static () => throw new InvalidOperationException("No save to load."));
        client.Tick(GeoscapeTimeController.GeoClock);
        Assert.Equal("STR_UNABLE_TO_ENGAGE_DEPTH", Assert.Single(client.Popups).Title);
        client.Choose(0);
        Assert.True(Assert.Single(client.Interceptions).Minimized);
        Assert.True(client.TimeCanRun);
        var time = campaign.Capture().Time;
        client.Tick(GeoscapeTimeController.GeoClock);
        Assert.NotEqual(time, campaign.Capture().Time);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AShotDownUfoRetaliatesAgainstTheCraftsBaseUnlessNoRegionHoldsIt(bool outsideEveryRegion)
    {
        // Retaliation is certain and always targets the craft's base region. For a base outside every region
        // the reference dereferences the missing region; the port starts no retaliation (ADR 0034).
        var (content, campaign, _) = Create(ufoDamage: 24, longitude: Degrees(10), latitude: 0, extraRules: """

            alienMissions:
              - type: MISSION_SCOUT
                retaliationOdds: 100
            difficultyBasedRetaliationDelay: [0, 0, 0, 0, 0]
            difficultyCoefficientOverrides:
              retaliationBaseRegionOdds: [100, 100, 100, 100, 100]

            """);
        var snapshot = campaign.Capture();
        if (outsideEveryRegion)
            snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Longitude = 3.5, Latitude = -1.2 }, .. snapshot.Bases.Skip(1)] };
        campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(7));
        var world = (CampaignWorld)campaign.GetQuery<ICampaignInterceptionQuery>()!;
        FightToTheEnd(campaign, world, Start(campaign, world));
        var retaliations = campaign.Capture().World.Missions.Count(static mission => mission.RuleId == "MISSION_RETALIATION");
        Assert.Equal(outsideEveryRegion ? 0 : 1, retaliations);
    }
}
