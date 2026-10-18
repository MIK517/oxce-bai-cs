using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 1 S1: <c>StartBattle</c> runs the battle generator on the pending deployment request
/// (<c>ConfirmLandingState::btnYesClick</c>, <c>BattlescapeGenerator::run</c>). The request is consumed only when
/// the battle exists, and the campaign then holds every strategic command and time (ADR 0038).
/// </summary>
public sealed class BattleStartTests
{
    private const string ScoutDeployment = """
        alienDeployments:
          - type: UFO_SCOUT
            width: 40
            length: 50
            height: 4
            turnLimit: 15
            chronoTrigger: 2
            cheatTurn: 12
            shade: 9
            minShade: 3
            maxShade: 11
        """;

    private static (CampaignState Campaign, CampaignDeploymentRequest Request) Requested(string? rules)
    {
        var (_, campaign) = StrategicLandingTests.Create(rules: rules);
        var requested = AssertLandingRequested(campaign);
        var result = campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Yes));
        return (campaign, Assert.IsType<DeploymentRequested>(Assert.Single(result.Events)).Request);
    }

    private static string Refusal(CampaignState campaign, ICampaignCommand command) =>
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(command).Events)).Reason;

    [Fact]
    public void StartBattleConsumesTheRequestAndHoldsTheCampaignUntilTheDebriefing()
    {
        var (campaign, request) = Requested(ScoutDeployment);
        Assert.Equal(("STR_UFO_GROUND_ASSAULT", "UFO_SCOUT"), (request.MissionType, request.DeploymentId));
        Assert.Null(campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer));

        var started = Assert.IsType<BattleStarted>(Assert.Single(campaign.Execute(new StartBattle()).Events));
        Assert.Equal(new BattleStarted("STR_UFO_GROUND_ASSAULT", "UFO_SCOUT"), started);
        Assert.Null(campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());

        // SavedBattleGame before the briefing: turn 0, the player's side, the deployment's size and limits.
        var battle = campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer)!;
        Assert.Equal(("STR_UFO_GROUND_ASSAULT", "UFO_SCOUT", 40, 50, 4, 0, BattleFaction.Player, 15),
            (battle.MissionType, battle.DeploymentId, battle.Width, battle.Length, battle.Height, battle.Turn, battle.Side,
                battle.TurnLimit));

        // The geoscape is frozen: time, other commands, a second start and saving are refused.
        AssertTimeBlocked(campaign, CampaignBattles.BattleInProgress.Text);
        var before = campaign.Capture();
        Assert.Equal(CampaignBattles.BattleInProgress.Text, Refusal(campaign, new RecallCraft(request.BaseId, "SHIP", 1)));
        Assert.Equal(CampaignBattles.BattleInProgress.Text, Refusal(campaign, new StartBattle()));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Equal(CampaignBattles.BattleSaveUnsupported.Text, campaign.QuerySaveRefusal());
        Assert.Null(campaign.CaptureForSave(out var refusal));
        Assert.Equal(CampaignBattles.BattleSaveUnsupported.Text, refusal);
    }

    [Fact]
    public void ARequestWithoutAnAlienDeploymentIsRefusedAndStaysPending()
    {
        // Mod::getDeployment(type, true) throws in the reference; the port refuses before any change.
        var (campaign, request) = Requested(null);
        Assert.Equal("", request.DeploymentId);
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new StartBattle()).Events));
        Assert.Equal((CampaignBattles.NoBattleDeployment.Code, CampaignBattles.NoBattleDeployment.Text), (blocked.Code, blocked.Reason));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Equal(request, campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());
        Assert.Null(campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer));
    }

    [Fact]
    public void WithoutARequestThereIsNoBattleToStart()
    {
        var (_, campaign) = StrategicLandingTests.Create(rules: ScoutDeployment);
        Assert.Equal(WorldCommandReasons.NoDeploymentRequest, Refusal(campaign, new StartBattle()));
    }

    [Fact]
    public void DeploymentBattlePropertiesAreLinkedWithTheReferenceDefaults()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], ScoutDeployment);
        var deployments = content.RuntimeRules.AlienDeployments;
        Assert.Equal(new RuntimeDeploymentBattle(40, 50, 4, 15, 2, 12, 9, 3, 11), deployments.GetRequiredValue("UFO_SCOUT").Battle);
        // AlienDeployment's constructor: no size, no turn limit, FORCE_LOSE, cheat turn 20, no shade overrides.
        Assert.Equal(RuntimeDeploymentBattle.Default, deployments.GetRequiredValue("SITE_DEPLOY").Battle);
        Assert.Equal(new RuntimeDeploymentBattle(0, 0, 0, 0, 0, 20, -1, -1, -1), RuntimeDeploymentBattle.Default);
    }
}
