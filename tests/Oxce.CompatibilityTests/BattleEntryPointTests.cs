using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.BattleTestTerrain;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 1 S6: the battles of the entry points other than a UFO and a base defense. A mission site
/// (<c>ConfirmLandingState::btnYesClick</c> with <c>setMissionSite</c>), an alien base (<c>setAlienBase</c>, the race's
/// base custom deployment and mission, no mission texture) and the final mission (<c>ConfirmCydoniaState::btnYesClick</c>)
/// generate their battles through <c>BattlescapeGenerator::run</c>.
/// </summary>
public sealed class BattleEntryPointTests
{
    // Loaded after strategic-world.rul, so these merge into its deployments, races and texture 10.
    private const string EntryRules = """
        terrains:
          - name: SITE_TERRAIN
            mapDataSets: [BLANKS, TESTSET]
            mapBlocks:
              - {name: BIG, width: 20, length: 10, groups: [0]}
              - {name: SMALL, width: 10, length: 10, groups: [0, 1]}
            script: BASIC
          - name: FAR_TERRAIN
            mapDataSets: [BLANKS, TESTSET]
            mapBlocks:
              - {name: BIG, width: 20, length: 10, groups: [0]}
              - {name: SMALL, width: 10, length: 10, groups: [0, 1]}
            script: BASIC
        mapScripts:
          - type: ENTRY_CRAFTED
            commands:
              - {type: addCraft}
              - {type: fillArea}
        globe:
          textures:
            - id: 10
              terrain:
                - {name: FAR_TERRAIN, weight: 1000, area: [180, 190, 60, 70]}
                - {name: SITE_TERRAIN, weight: 1}
        alienRaces:
          - id: RACE_A
            baseCustomDeploy: BASE_ALIENS
            baseCustomMission: BASE_MISSION
        alienDeployments:
          - type: SITE_DEPLOY
            width: 20
            length: 20
            height: 2
            data: [{customUnitType: ALIEN, lowQty: 1, highQty: 1, itemSets: [[]]}]
          - type: STR_TERROR_MISSION
            race: ""
            data: [{customUnitType: ALIEN, lowQty: 2, highQty: 2, itemSets: [[]]}]
          - type: ALIEN_BASE_DEPLOY
            width: 20
            length: 20
            height: 2
            terrains: [FAR_TERRAIN]
            data: [{customUnitType: ALIEN, lowQty: 1, highQty: 1, itemSets: [[]]}]
          - type: BASE_MISSION
            width: 20
            length: 20
            height: 2
            data: [{customUnitType: ALIEN, lowQty: 1, highQty: 1, itemSets: [[]]}]
          - type: BASE_ALIENS
            data: [{customUnitType: ALIEN, lowQty: 2, highQty: 2, itemSets: [[]]}]
          - type: STR_FINAL_ASSAULT
            finalDestination: true
            race: RACE_B
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: ENTRY_CRAFTED
            data: [{customUnitType: ALIEN, lowQty: 1, highQty: 1, itemSets: [[]]}]
        crafts:
          - type: SHIP
            spacecraft: true
            fuelMax: 100
        research:
          - {name: FINAL_TOPIC, cost: 1, unlockFinalMission: true}

        """;

    // The landed UFO's place taken by a detected site (race RACE_B, custom deployment STR_TERROR_MISSION) or a discovered
    // alien base (race RACE_A); the craft is ordered there.
    private static (CampaignState Campaign, CampaignDeploymentRequest Request) Landed(bool alienBase, string baseRace = "RACE_A")
    {
        var (campaign, result) = Landing(alienBase, baseRace);
        return (campaign, Assert.IsType<DeploymentRequested>(Assert.Single(result.Events)).Request);
    }

    private static (CampaignState Campaign, CampaignCommandResult Result) Landing(bool alienBase, string baseRace)
    {
        var (content, campaign) = StrategicLandingTests.Create(rules: BattleStartTests.Rules, files: RequiredFiles("BLANKS", "TESTSET"),
            finalRules: EntryRules);
        var snapshot = campaign.Capture();
        var ufo = snapshot.World.Ufos[0];
        var target = alienBase
            ? new WorldTargetReference(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 5, ufo.Longitude, ufo.Latitude)
            : new WorldTargetReference(WorldTargetKind.MissionSite, "STR_TERROR_SITE", 5, ufo.Longitude, ufo.Latitude);
        snapshot = (snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [],
                MissionSites = alienBase ? snapshot.World.MissionSites
                    : [new MissionSiteSnapshot(5, "MISSION_SCOUT", "SITE_DEPLOY", "RACE_B", ufo.Longitude, ufo.Latitude, 3600)
                    { Detected = true, CustomDeploymentId = "STR_TERROR_MISSION" }],
                AlienBases = alienBase ? [new AlienBaseSnapshot(5, "ALIEN_BASE_DEPLOY", baseRace, ufo.Longitude, ufo.Latitude) { Discovered = true }]
                    : snapshot.World.AlienBases,
            },
        }).WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Destination = target } });
        campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        campaign.BattleTerrainFiles = BattleStartTests.TerrainFiles();
        var requested = AssertLandingRequested(campaign);
        return (campaign, campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Yes)));
    }

    private static BattleState Started(CampaignState campaign, CampaignDeploymentRequest request)
    {
        var events = campaign.Execute(new StartBattle()).Events;
        Assert.Equal("", events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason.ToString() ?? "");
        Assert.Equal(request.MissionType, Assert.IsType<BattleStarted>(Assert.Single(events)).MissionType);
        Assert.Null(campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());
        return campaign.GetQuery<IBattleQuery>() is CampaignBattles battles ? battles.Battle! : throw new InvalidOperationException();
    }

    private static int Count(BattleState battle, BattleFaction faction) => battle.Units.Count(unit => unit.Faction == faction);

    [Fact]
    public void AMissionSitesBattleTakesItsTerrainFromTheTextureAtTheSiteAndItsAliensFromTheCustomDeployment()
    {
        var (campaign, request) = Landed(alienBase: false);
        Assert.Equal(("SITE_DEPLOY", "STR_TERROR_MISSION", "", 10), (request.MissionType, request.AlienCustomDeploymentId,
            request.AlienCustomMissionId, request.MissionTexture));

        var battle = Started(campaign, request);
        // run: the site's deployment (the mission type) and, as it lists no terrains, the texture's pick at the site
        // (Target *target = _ufo; if (_mission) target = _mission;): FAR_TERRAIN's area does not hold the site.
        Assert.Equal(("SITE_DEPLOY", "SITE_DEPLOY", "SITE_TERRAIN", "BASIC"), (battle.MissionType, battle.DeploymentId, battle.TerrainId,
            battle.MapScriptId));
        Assert.Equal(("STR_TERROR_MISSION", ""), (battle.AlienCustomDeploy, battle.AlienCustomMission));
        // deployAliens: the custom deployment has data, so its aliens come instead of the site's; without its own race,
        // the site's race (setAlienRace) is the one.
        Assert.Equal(2, Count(battle, BattleFaction.Hostile));
        Assert.Equal(("STR_TERROR_MISSION", "RACE_B"), (battle.ReinforcementsDeployment, battle.ReinforcementsRace));
        Assert.Equal(1, Count(battle, BattleFaction.Player));
        // setCraft and setMissionSite: the craft and the site are in the battlescape, the base is not.
        var snapshot = campaign.Capture();
        Assert.Equal((true, true, false), (Assert.Single(snapshot.World.MissionSites).InBattlescape, ShipInBattlescape(snapshot),
            snapshot.Bases[0].InBattlescape));
    }

    private static bool ShipInBattlescape(CampaignSnapshot snapshot) =>
        snapshot.Bases[0].Crafts.Single(static craft => craft.RuleId == "SHIP").Logistics!.InBattlescape;

    [Fact]
    public void AnAlienBasesBattleIsTheRacesCustomMissionWithoutAMissionTexture()
    {
        var (campaign, request) = Landed(alienBase: true);
        Assert.Equal(("ALIEN_BASE_DEPLOY", "BASE_ALIENS", "BASE_MISSION", -1, 10), (request.MissionType, request.AlienCustomDeploymentId,
            request.AlienCustomMissionId, request.MissionTexture, request.GlobeTexture));

        var battle = Started(campaign, request);
        // run: the race's base custom mission replaces the base's deployment (and its FAR_TERRAIN), but the mission type
        // stays the base's. setWorldTexture(0, ...) leaves no texture to pick from, so a deployment without terrains takes
        // the mod's first terrain, not texture 10's SITE_TERRAIN.
        Assert.Equal(("ALIEN_BASE_DEPLOY", "BASE_MISSION", "TEST_TERRAIN"), (battle.MissionType, battle.DeploymentId, battle.TerrainId));
        Assert.Equal(("BASE_ALIENS", "BASE_MISSION"), (battle.AlienCustomDeploy, battle.AlienCustomMission));
        // The race's base custom deployment brings the aliens; the base's race fights.
        Assert.Equal(2, Count(battle, BattleFaction.Hostile));
        Assert.Equal(("BASE_ALIENS", "RACE_A"), (battle.ReinforcementsDeployment, battle.ReinforcementsRace));
        // setCraft and setAlienBase.
        var snapshot = campaign.Capture();
        Assert.Equal((true, true, false), (Assert.Single(snapshot.World.AlienBases).InBattlescape, ShipInBattlescape(snapshot),
            snapshot.Bases[0].InBattlescape));
    }

    [Fact]
    public void AnAlienBaseWithoutARaceCustomMissionFightsItsOwnDeployment()
    {
        // RACE_B has no base custom mission or deployment: the base's deployment, its terrain and its race.
        var (campaign, request) = Landed(alienBase: true, baseRace: "RACE_B");
        Assert.Equal(("ALIEN_BASE_DEPLOY", "", ""), (request.MissionType, request.AlienCustomDeploymentId, request.AlienCustomMissionId));
        var battle = Started(campaign, request);
        Assert.Equal(("ALIEN_BASE_DEPLOY", "FAR_TERRAIN"), (battle.DeploymentId, battle.TerrainId));
        Assert.Equal(("ALIEN_BASE_DEPLOY", "RACE_A"), (battle.ReinforcementsDeployment, battle.ReinforcementsRace));
        Assert.Equal(1, Count(battle, BattleFaction.Hostile));
    }

    [Fact]
    public void YesAtAnAlienBaseOfAnUnknownRaceIsRefused()
    {
        // checkStartingCondition and btnYesClick read the race's base custom mission and deployment from a null AlienRace.
        var (campaign, result) = Landing(alienBase: true, baseRace: "RACE_GONE");
        Assert.Equal(WorldCommandReasons.AlienBaseRaceUnknown, Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Reason);
        Assert.Null(campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());
        Assert.Single(campaign.GetQuery<ICampaignLandingQuery>()!.QueryLandingConfirmations());
    }

    [Fact]
    public void TheFinalMissionsBattleIsTheFinalDeploymentsWithItsRaceAndTheCraftOnTheMap()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], BattleStartTests.Rules, finalRules: EntryRules,
            files: RequiredFiles("BLANKS", "TESTSET"));
        var snapshot = CreateSnapshot(content, "Final battle").WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_READY", Fuel = 100, Weapons = [null, null], Items = new Dictionary<string, int>() },
        });
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            CompletedResearch = [.. snapshot.CompletedResearch, "FINAL_TOPIC"],
            Bases = [owner with { Soldiers = [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = 1 } },
                .. owner.Soldiers.Skip(1)] }],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(3));
        campaign.BattleTerrainFiles = BattleStartTests.TerrainFiles();
        var request = Assert.IsType<DeploymentRequested>(Assert.Single(campaign.Execute(new LaunchFinalMission(owner.Id, "SHIP", 1)).Events))
            .Request;

        var battle = Started(campaign, request);
        // ConfirmCydoniaState: the first final-destination deployment is the mission type, with no texture and the
        // generator's default world shade (0).
        Assert.Equal(("STR_FINAL_ASSAULT", "STR_FINAL_ASSAULT", "TEST_TERRAIN", "ENTRY_CRAFTED", 0),
            (battle.MissionType, battle.DeploymentId, battle.TerrainId, battle.MapScriptId, battle.GlobalShade));
        Assert.Equal(("", ""), (battle.AlienCustomDeploy, battle.AlienCustomMission));
        Assert.Equal(("STR_FINAL_ASSAULT", "RACE_B"), (battle.ReinforcementsDeployment, battle.ReinforcementsRace));
        Assert.Equal(1, Count(battle, BattleFaction.Hostile));
        // setCraft: the craft lands and its crew starts on its start tiles.
        var soldier = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal(owner.Soldiers[0].Id, soldier.SoldierId);
        var craft = Enumerable.Range(0, 9).Select(cell => (X: cell % 3, Y: cell / 3))
            .Single(cell => battle.Map.TerrainName(cell.X, cell.Y) == "SHIP");
        var first = new BattlePosition(craft.X * 10, craft.Y * 10, 0);
        Assert.Equal((first, first), (soldier.Position, battle.CraftInventoryTile));
        // ConfirmCydoniaState sets only the craft (setCraft); the launch base is no world target in the battlescape.
        var captured = campaign.Capture();
        Assert.Equal((true, false), (ShipInBattlescape(captured), captured.Bases[0].InBattlescape));
        Assert.DoesNotContain(captured.World.Ufos, static ufo => ufo.InBattlescape);
    }
}
