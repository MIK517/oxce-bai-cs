using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S7: a retaliation assault UFO reaching the X-COM base. With defenses the base defense window
/// opens; firing or skipping it ends in a shot-down UFO or the defenders' battle request; without
/// defenses the battle is requested at once. A base without defenders is destroyed, and the last base's
/// loss ends the campaign.
/// </summary>
public sealed class StrategicBaseAttackTests
{
    private const string Defense = """
        facilities:
          - type: DEF_CANNON
            mapName: LOGISTICS
            defense: 2000
            hitRatio: 100
            ammoNeeded: 0

        """;

    private static string Missiles(int power, int stopChance = 0) => $$"""

        ufos:
          - type: UFO_HUNTER
            missilePower: {{power}}
            missileStopChance: {{stopChance}}

        """;

    private static (RuntimeContent Content, CampaignState Campaign, int BaseId) Create(bool defended, bool defenders = true, string ufoRules = "",
        IReadOnlyDictionary<string, int>? stores = null)
    {
        var content = stores is null
            ? StrategicReadinessTestContent.Load(["strategic-world.rul"], Defense + ufoRules)
            : StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-bases.rul"], Defense + VehicleAmmo + ufoRules);
        var snapshot = CreateSnapshot(content, "Base attack");
        var owner = snapshot.Bases[0];
        var position = new WorldPosition(owner.Longitude, owner.Latitude);
        var ufo = new UfoSnapshot(9, "UFO_HUNTER", 4, RuntimeTrajectory, 0, owner.Longitude - 0.0001, owner.Latitude,
            UfoStatus.Flying, WorldAltitudes.High)
        {
            Id = 3,
            Speed = 1000,
            MissionWaveNumber = 0,
            Shield = 0,
            Destination = WorldTargetReference.ForWaypoint(0, position),
        };
        owner = owner with
        {
            Facilities = defended
                ? [.. owner.Facilities, new FacilitySnapshot("DEF_CANNON", 5, 5, 0, 0, false, false, false)]
                : owner.Facilities,
            Soldiers = defenders ? owner.Soldiers : [],
            Items = stores is null ? owner.Items : new Dictionary<string, int>(owner.Items, StringComparer.Ordinal).Concat(stores)
                .GroupBy(static pair => pair.Key).ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.Ordinal),
            Crafts = defenders ? owner.Crafts : [.. owner.Crafts.Select(craft => craft.Logistics is null ? craft
                : craft with { Logistics = craft.Logistics with { Vehicles = [] } })],
        };
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 0, 5),
            Bases = [owner, .. snapshot.Bases.Skip(1)],
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_RETALIATION", "REGION", "RACE_A", 1, 0, 1500, 1, -1)],
                Ufos = [ufo],
            },
        };
        return (content, CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(11)), owner.Id);
    }

    private const string RuntimeTrajectory = "__RETALIATION_ASSAULT_RUN";

    private static ICampaignBaseDefenseQuery Query(CampaignState campaign) => campaign.GetQuery<ICampaignBaseDefenseQuery>()!;

    [Fact]
    public void ADefendedBaseOpensTheDefenseWindowAndItsCannonShootsTheUfoDown()
    {
        var (_, campaign, baseId) = Create(defended: true);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, static item => item is CampaignActionBlocked);
        Assert.Equal(new BaseDefenseStarted(baseId, 9, false), Assert.Single(result.Events.OfType<BaseDefenseStarted>()));
        Assert.Equal((1, 0), (Query(campaign).QueryBaseDefense()!.Defenses, Query(campaign).QueryBaseDefense()!.GravShields));
        var captured = campaign.Capture();
        var ufo = Assert.Single(captured.World.Ufos);
        Assert.Equal((false, (WorldTargetReference?)null, WorldAltitudes.Ground), (ufo.Detected, ufo.Destination, ufo.Altitude));
        // The next wave waits 30 * (48..448) seconds; the base remembers the retaliation mission.
        Assert.InRange(Assert.Single(captured.World.Missions).SpawnCountdown, 30 * 48, 30 * 448);
        Assert.Equal(4, captured.Bases[0].RetaliationMissionId);
        AssertTimeBlocked(campaign, WorldCommandReasons.BaseDefenseOpen);
        Assert.Equal(WorldCommandReasons.BaseDefenseOpen, Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new RecallCraft(baseId, "SHIP", 1)).Events)).Reason);

        var resolved = campaign.Execute(new ResolveBaseDefense(baseId, Fire: true)).Events;
        Assert.Equal(new BaseDefenseResolved(baseId, 9, true, false), Assert.Single(resolved));
        Assert.Null(Query(campaign).QueryBaseDefense());
        Assert.Equal(UfoStatus.Destroyed, Assert.Single(campaign.Capture().World.Ufos).Status);
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Ufos);
    }

    [Fact]
    public void SkippingTheDefensesSendsTheUfoIntoTheDefendersBattle()
    {
        var (_, campaign, baseId) = Create(defended: true);
        campaign.Execute(new AdvanceCampaignTime(1));
        var resolved = campaign.Execute(new ResolveBaseDefense(baseId, Fire: false)).Events;
        Assert.Equal(new BaseDefenseResolved(baseId, 9, false, false), resolved[0]);
        var request = Assert.IsType<DeploymentRequested>(resolved[1]).Request;
        Assert.Equal(("STR_BASE_DEFENSE", "RACE_A", WorldTargetKind.Base, baseId, ""), (request.MissionType, request.Race, request.Target.Kind,
            request.BaseId, request.CraftTypeId));
        Assert.NotEmpty(request.Soldiers);
        Assert.Equal(UfoStatus.Destroyed, Assert.Single(campaign.Capture().World.Ufos).Status);
        AssertTimeBlocked(campaign, WorldCommandReasons.DeploymentRequestPending);
    }

    [Fact]
    public void AnUndefendedBaseGoesStraightToTheDefendersBattle()
    {
        var (content, campaign, baseId) = Create(defended: false);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        var request = Assert.IsType<DeploymentRequested>(Assert.Single(result.Events.OfType<DeploymentRequested>())).Request;
        Assert.Equal(("STR_BASE_DEFENSE", baseId), (request.MissionType, request.BaseId));
        Assert.Empty(result.Events.OfType<BaseDefenseStarted>());
        // The spent UFO has no destination, which Ufo::load turns into a waypoint, so compare the request only.
        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(campaign.Capture()), content, 11, "base-defense.sav").Campaign;
        Assert.Equivalent(request, reloaded.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest(), strict: true);
    }

    [Fact]
    public void ABaseWithoutDefendersIsDestroyedAndLosingTheLastBaseEndsTheCampaign()
    {
        var (content, campaign, baseId) = Create(defended: false, defenders: false);
        var name = campaign.Capture().Bases[0].Name;
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(new BaseDestroyed(baseId, name), Assert.Single(result.Events.OfType<BaseDestroyed>()));
        var captured = campaign.Capture();
        Assert.Empty(captured.Bases);
        // BaseDestroyedState deletes the retaliation mission and its UFOs.
        Assert.Empty(captured.World.Missions);
        Assert.Empty(captured.World.Ufos);
        Assert.Equal(0, captured.Ending);

        // The next five-second tick finds no base: END_LOSE (D5), and the campaign accepts no more commands.
        var reloaded = Reload(content, campaign, 11, "base-lost.sav");
        Assert.Equal(new CampaignEnded(2), Assert.Single(reloaded.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignEnded>()));
        Assert.Equal(2, reloaded.Capture().Ending);
        Assert.Equal("This campaign has ended.", Assert.IsType<CampaignActionBlocked>(Assert.Single(
            reloaded.Execute(new AdvanceCampaignTime(1)).Events)).Reason);
    }

    [Fact]
    public void ANuclearMissileDestroysAGarrisonedBase()
    {
        var (_, campaign, baseId) = Create(defended: false, ufoRules: Missiles(-1, stopChance: 100));
        var name = campaign.Capture().Bases[0].Name;
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<DeploymentRequested>());
        Assert.Equal(new BaseDestroyed(baseId, name, Missiles: true), Assert.Single(result.Events.OfType<BaseDestroyed>()));
        var captured = campaign.Capture();
        Assert.Empty(captured.Bases);
        // The retaliation mission went with the base, so the stop chance has nothing left to interrupt.
        Assert.Empty(captured.World.Missions);
        Assert.Empty(captured.World.Ufos);
    }

    [Fact]
    public void MissilesThatDamageFacilitiesStopTheClockBeforeTheAttack()
    {
        var (_, campaign, _) = Create(defended: false, ufoRules: Missiles(3));
        AssertTimeBlocked(campaign, WorldCommandReasons.MissileBaseAttack);
    }

    private const string InstantRetaliation = """

        alienMissions:
          - type: MISSION_INSTANT
            objective: 6
            spawnUfo: UFO_HUNTER
            raceWeights:
              0: {RACE_A: 1}
            waves:
              - {ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_PATROL, timer: 0}

        """;

    // An instant retaliation due at the next half-hour, instead of the saved assault UFO.
    private static CampaignState CreateInstant(bool defended, string ufoRules = "")
    {
        var (content, campaign, _) = Create(defended, ufoRules: InstantRetaliation + ufoRules);
        var snapshot = campaign.Capture();
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(5, "MISSION_INSTANT", "REGION", "RACE_A", 0, 0, 30, 0, -1)],
                Ufos = [],
            },
        };
        return CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(11));
    }

    [Fact]
    public void AnInstantRetaliationIsBornOverTheBaseAndOpensItsDefenseInTheSameTick()
    {
        var campaign = CreateInstant(defended: true);
        var owner = campaign.Capture().Bases[0];
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        var started = Assert.Single(result.Events.OfType<BaseDefenseStarted>());
        Assert.Equal(owner.Id, started.BaseId);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal(("UFO_HUNTER", RuntimeTrajectory, 1, WorldAltitudes.Ground, owner.Longitude, owner.Latitude),
            (ufo.RuleId, ufo.TrajectoryId, ufo.TrajectoryPoint, ufo.Altitude, ufo.Longitude, ufo.Latitude));
        // The instant mission spawns one ship only; skipping the defenses spends it on the garrison's battle.
        var resolved = campaign.Execute(new ResolveBaseDefense(owner.Id, Fire: false)).Events;
        Assert.IsType<DeploymentRequested>(resolved[1]);
        Assert.True(Assert.Single(campaign.Capture().World.Missions).Interrupted);
    }

    [Fact]
    public void AnInstantRetaliationWithFacilityMissilesStopsTheClockBeforeItsBirth()
    {
        var campaign = CreateInstant(defended: false, ufoRules: Missiles(3));
        AssertTimeBlocked(campaign, WorldCommandReasons.MissileBaseAttack);
    }

    // Both test vehicles fire VEHICLE_AMMO and load two clips of it.
    private const string VehicleAmmo = """

        items:
          - type: VEHICLE_AMMO
            clipSize: 2
          - type: VEHICLE
            compatibleAmmo: [VEHICLE_AMMO]
            vehicleFixedAmmoSlot: 0
          - type: VEHICLE_B
            compatibleAmmo: [VEHICLE_AMMO]
            vehicleFixedAmmoSlot: 0

        """;

    [Fact]
    public void StoredVehiclesWithTheirClipsDefendAnUngarrisonedBase()
    {
        // Three clips load one vehicle of two; the stores themselves stay for battle generation to move.
        var stores = new Dictionary<string, int> { ["VEHICLE"] = 2, ["VEHICLE_AMMO"] = 3 };
        var (_, campaign, baseId) = Create(defended: false, defenders: false, stores: stores);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        var request = Assert.Single(result.Events.OfType<DeploymentRequested>()).Request;
        Assert.Equal(("STR_BASE_DEFENSE", baseId), (request.MissionType, request.BaseId));
        Assert.Empty(request.Soldiers);
        Assert.Equal(["VEHICLE"], request.Vehicles);
        var items = campaign.Capture().Bases[0].Items;
        Assert.Equal((2, 3), (items["VEHICLE"], items["VEHICLE_AMMO"]));
    }

    [Fact]
    public void StoredVehiclesCompetingForTooFewClipsStopTheClock()
    {
        var stores = new Dictionary<string, int> { ["VEHICLE"] = 1, ["VEHICLE_B"] = 1, ["VEHICLE_AMMO"] = 3 };
        var (_, campaign, _) = Create(defended: false, defenders: false, stores: stores);
        AssertTimeBlocked(campaign, WorldCommandReasons.StoredVehicleDefense);
    }
}
