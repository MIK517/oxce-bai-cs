using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// BattlescapeGenerator: builds the battle a deployment request asks for. <see cref="Refusal"/> checks the request
/// against the content before anything changes; <see cref="Generate"/> then cannot fail on valid input (ADR 0038).
/// </summary>
internal static class BattleGenerator
{
    /// <summary>Why no battle can be generated from the request, or null.</summary>
    public static CampaignReason? Refusal(CampaignState campaign, CampaignDeploymentRequest request) =>
        Deployment(campaign, request) is null ? CampaignBattles.NoBattleDeployment : null;

    public static BattleState Generate(CampaignState campaign, CampaignDeploymentRequest request)
    {
        var (id, deployment) = Deployment(campaign, request)
            ?? throw new InvalidOperationException("The deployment request was not checked before generation.");
        var battle = deployment.Battle;
        return new BattleState(request.MissionType, id, battle.Width, battle.Length, battle.Height, battle.TurnLimit,
            battle.ChronoTrigger, battle.CheatTurn);
    }

    /// <summary>
    /// BattlescapeGenerator::run's <c>ruleDeploy</c>: the alien custom mission (which also covers fake underwater
    /// UFOs), else the UFO's deployment for a UFO target, else the deployment named by the mission type.
    /// </summary>
    private static (string Id, RuntimeAlienDeploymentRule Rule)? Deployment(CampaignState campaign, CampaignDeploymentRequest request)
    {
        var id = request.AlienCustomMissionId.Length != 0 ? request.AlienCustomMissionId
            : request.Target.Kind == WorldTargetKind.Ufo ? request.DeploymentId
            : request.MissionType;
        var deployments = campaign.Content.RuntimeRules.AlienDeployments;
        return id.Length != 0 && deployments.TryGet(id, out var handle) ? (id, deployments[handle].Value) : null;
    }
}
