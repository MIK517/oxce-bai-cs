using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// BattlescapeGenerator::run: builds the battle a deployment request asks for. <see cref="Refusal"/> checks what can be
/// checked before drawing anything; <see cref="Generate"/> then either returns the battle or throws
/// <see cref="MapGenerationException"/> where the reference throws, and the caller adopts nothing (ADR 0038).
/// </summary>
internal static class BattleGenerator
{
    /// <summary>Why no battle can be generated from the request, or null.</summary>
    public static CampaignReason? Refusal(CampaignState campaign, CampaignDeploymentRequest request) =>
        Deployment(campaign, request) is null ? CampaignBattles.NoBattleDeployment : null;

    public static BattleState Generate(CampaignState campaign, CampaignDeploymentRequest request, BattleTerrainLoader loader)
    {
        var (id, deployment) = Deployment(campaign, request)
            ?? throw new InvalidOperationException("The deployment request was not checked before generation.");
        var rules = campaign.Content.RuntimeRules;
        var random = campaign.Random;
        var battle = deployment.Battle;
        if (!BattleMap.IsSupportedSize(Math.Max(battle.Width, 1), Math.Max(battle.Length, 1), Math.Max(battle.Height, 1)))
            throw new MapGenerationException($"Deployment {id} asks for a {battle.Width}x{battle.Length}x{battle.Height} map, outside the supported size.");
        var terrain = PickTerrain(rules, request, battle, random);
        var depth = PickDepth(battle, terrain, random);
        var shade = battle.Shade != -1 ? battle.Shade
            : battle.MinShade != -1 && request.Shade < battle.MinShade ? battle.MinShade
            : battle.MaxShade != -1 && request.Shade > battle.MaxShade ? battle.MaxShade
            : request.Shade;
        var terrainScript = PickOne(terrain.MapScripts, terrain.MapScript, random);
        var script = rules.Battle.MapScripts.GetValueOrDefault(terrainScript);
        var scriptId = terrainScript;
        var deployScript = PickOne(battle.MapScripts, battle.MapScript, random);
        if (rules.Battle.MapScripts.TryGetValue(deployScript, out var deploymentScript))
        {
            script = deploymentScript;
            scriptId = deployScript;
        }
        else if (deployScript.Length != 0)
        {
            throw new MapGenerationException($"Map generator encountered an error: {deployScript} script not found.");
        }
        if (script is null) throw new MapGenerationException($"Map generator encountered an error: {terrainScript} script not found.");
        var globeTerrain = GlobeTerrain(rules, request, terrain, random);
        var inputs = Inputs(campaign, request, deployment);
        var generated = BattleMapGenerator.Generate(rules, loader, random, request.MissionType, terrain, globeTerrain, script,
            battle.Width, battle.Length, battle.Height, inputs);
        var map = generated.Map;
        if (map.Width == 0 || map.Length == 0 || map.Height == 0)
            throw new MapGenerationException($"Map script {scriptId} leaves an empty {map.Width}x{map.Length}x{map.Height} map.");
        var state = new BattleState(request.MissionType, id, map, battle.TurnLimit, battle.ChronoTrigger, battle.CheatTurn)
        {
            TerrainId = terrain.Id,
            MapScriptId = scriptId,
            Depth = depth,
            GlobalShade = shade,
        };
        // run: the deployment's environmental effects, else the terrain's.
        var enviro = rules.Battle.EnviroArmorTransformations.ContainsKey(battle.EnviroEffects) ? battle.EnviroEffects : terrain.EnviroEffects;
        XcomDeployment.Deploy(campaign, request, state, generated, inputs.StartingCondition, enviro);
        return state;
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

    /// <summary>
    /// run's terrain choice: one of the deployment's terrains when it lists any or there is no mission texture with
    /// terrains (the first terrain of the mod when the deployment lists none either), else the texture's weighted
    /// pick at the target.
    /// </summary>
    private static RuntimeTerrain PickTerrain(RuntimeRuleCatalog rules, CampaignDeploymentRequest request, RuntimeDeploymentBattle battle,
        IStatefulRandomSource random)
    {
        var texture = Texture(rules, request.MissionTexture);
        string name;
        if (texture is null || texture.Terrains.Count == 0 || battle.Terrains.Count != 0)
            name = battle.Terrains.Count != 0 ? battle.Terrains[random.NextInclusive(0, battle.Terrains.Count - 1)]
                : rules.Battle.TerrainOrder.Count != 0 ? rules.Battle.TerrainOrder[0]
                : throw new MapGenerationException("Map generator encountered an error: No valid terrain found.");
        else
            name = RuntimeTerrainCriterion.Pick(texture.Terrains, request.Target.Longitude, request.Target.Latitude, random.NextInclusive);
        return rules.Battle.Terrains.GetValueOrDefault(name)
            ?? throw new MapGenerationException($"Map generator encountered an error: terrain '{name}' does not exist.");
    }

    /// <summary>
    /// The craft (BattlescapeGenerator::setCraft), the target UFO (setUfo, UFO targets only), the deployment's custom UFO
    /// and the starting condition: the deployment's, else the mission texture's (run).
    /// </summary>
    private static BattleMapInputs Inputs(CampaignState campaign, CampaignDeploymentRequest request, RuntimeAlienDeploymentRule deployment)
    {
        var rules = campaign.Content.RuntimeRules;
        var craft = request.CraftTypeId.Length != 0 && rules.Crafts.TryGet(request.CraftTypeId, out _) ? request.CraftTypeId : "";
        var ufo = request.Target.Kind == WorldTargetKind.Ufo
            ? campaign.Capture().World.Ufos.FirstOrDefault(candidate => candidate.UniqueId == request.Target.UniqueId)?.RuleId ?? ""
            : "";
        var conditionId = deployment.StartingCondition.Length != 0 && rules.StartingConditions.TryGet(deployment.StartingCondition, out _)
            ? deployment.StartingCondition : Texture(rules, request.MissionTexture)?.StartingCondition ?? "";
        var condition = conditionId.Length != 0 && rules.StartingConditions.TryGet(conditionId, out var handle)
            ? rules.StartingConditions[handle].Value : null;
        return new BattleMapInputs(craft, ufo, deployment.Battle.CustomUfo, condition);
    }

    /// <summary>BattlescapeGenerator::setDepth: the deployment's depth range wins over the terrain's.</summary>
    private static int PickDepth(RuntimeDeploymentBattle battle, RuntimeTerrain terrain, IRandomSource random) =>
        battle.MaximumDepth > 0 ? MonthlyCycle.Generate(random, battle.MinimumDepth, battle.MaximumDepth)
        : terrain.MaximumDepth > 0 ? MonthlyCycle.Generate(random, terrain.MinimumDepth, terrain.MaximumDepth)
        : 0;

    /// <summary>getRandomMapScript: one of the list when it is not empty, else the single script.</summary>
    private static string PickOne(IReadOnlyList<string> choices, string single, IRandomSource random) =>
        choices.Count != 0 ? choices[random.NextInclusive(0, choices.Count - 1)] : single;

    /// <summary>generateMap's globe terrain: with a craft, the globe texture's pick at the craft, if that terrain exists.</summary>
    private static RuntimeTerrain GlobeTerrain(RuntimeRuleCatalog rules, CampaignDeploymentRequest request, RuntimeTerrain terrain,
        IRandomSource random)
    {
        if (request.CraftTypeId.Length == 0 || Texture(rules, request.GlobeTexture) is not { } texture) return terrain;
        // The craft has landed at its target.
        var name = RuntimeTerrainCriterion.Pick(texture.Terrains, request.Target.Longitude, request.Target.Latitude, random.NextInclusive);
        return rules.Battle.Terrains.GetValueOrDefault(name) ?? terrain;
    }

    private static RuntimeGlobeTexture? Texture(RuntimeRuleCatalog rules, int id) =>
        id >= 0 && rules.Campaign.Globe.Textures.TryGetValue(id, out var texture) ? texture : null;
}
