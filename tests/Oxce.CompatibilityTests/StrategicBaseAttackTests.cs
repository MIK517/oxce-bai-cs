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
/// defenses the battle is requested at once. A base without defenders is refused until base loss lands.
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

    private static (RuntimeContent Content, CampaignState Campaign, int BaseId) Create(bool defended, bool defenders = true)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], Defense);
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
    public void ABaseWithoutDefendersIsRefusedUntilBaseLossLands()
    {
        var (_, campaign, _) = Create(defended: false, defenders: false);
        AssertTimeBlocked(campaign, WorldCommandReasons.BaseLossBoundary);
    }
}
