using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S3: open interceptions in a campaign. Each step runs the dogfight engine
/// (<see cref="StrategicDogfightOracleTests"/>) against the craft and UFO state, writes the saved fields
/// back, and refuses before any change when an outcome needs world consequences that are not implemented
/// yet. Combat itself is never saved, as in the reference.
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
        bool tractor = false, int ufoLandId = 0)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], Rules);
        var snapshot = CreateSnapshot(content, "Interception");
        var ufo = new UfoSnapshot(9, "UFO_SCOUT", 4, "TRAJ_PATROL", 0, 0.4, 0.2, UfoStatus.Flying, WorldAltitudes.High)
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
                Longitude = 0.4,
                Latitude = 0.2,
                Weapons = [new CraftWeaponSnapshot("DOGFIGHT_CANNON", 200, false, false),
                    tractor ? new CraftWeaponSnapshot("DOGFIGHT_TRACTOR", 0, false, false) : null],
                Items = new Dictionary<string, int>(),
                Destination = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 },
            },
        });
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

    [Fact]
    public void ACrashOutcomeIsRefusedBeforeAnythingChanges()
    {
        // One more hit of 5-10 damage crashes the 50-point scout; its consequences belong to a later slice.
        var (_, campaign, world) = Create(ufoDamage: 24);
        var number = Start(campaign, world);
        campaign.Execute(new SetInterceptionMode(number, InterceptionMode.Aggressive));
        CampaignCommandResult result;
        do
        {
            var before = campaign.Capture();
            var distance = Single(world).Distance;
            result = campaign.Execute(new AdvanceInterceptions());
            if (result.Events.OfType<CampaignActionBlocked>().Any())
            {
                Assert.Equivalent(before, campaign.Capture(), strict: true);
                Assert.Equal(distance, Single(world).Distance);
            }
        }
        while (!result.Events.OfType<CampaignActionBlocked>().Any());
        Assert.Equal(WorldCommandReasons.InterceptionOutcomeBoundary, Assert.Single(result.Events.OfType<CampaignActionBlocked>()).Reason);
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
    public void TimeStopsWhileAnInterceptionIsOpen()
    {
        var (_, campaign, world) = Create();
        var number = Start(campaign, world);
        AssertTimeBlocked(campaign, WorldCommandReasons.InterceptionTimeBoundary);
        campaign.Execute(new SetInterceptionMode(number, InterceptionMode.Disengage));
        while (world.QueryInterceptions().Count != 0) campaign.Execute(new AdvanceInterceptions());
        AdvanceOne(campaign);
    }

    [Fact]
    public void AForcedLandingOfAUfoWithALandingIdIsRefusedToo()
    {
        // 1100 power x 200% (small UFO) cancels the scout's 2200 speed: the UFO lands without needing a new ID.
        var (_, campaign, world) = Create(tractor: true, ufoLandId: 5);
        var number = Start(campaign, world);
        campaign.Execute(new SetInterceptionMode(number, InterceptionMode.Aggressive));
        CampaignCommandResult result;
        do result = campaign.Execute(new AdvanceInterceptions());
        while (!result.Events.OfType<CampaignActionBlocked>().Any() && Single(world).Distance > 64);
        Assert.Equal(WorldCommandReasons.InterceptionOutcomeBoundary, Assert.Single(result.Events.OfType<CampaignActionBlocked>()).Reason);
        Assert.Equal(UfoStatus.Flying, Assert.Single(campaign.Capture().World.Ufos).Status);
    }
}
