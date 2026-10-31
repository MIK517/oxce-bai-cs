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
        var (state, generated, inputs, terrain, id, deployment) = GenerateMap(campaign, request, loader);
        var rules = campaign.Content.RuntimeRules;
        var random = campaign.Random;
        var battle = deployment.Battle;
        var map = state.Map;
        // run: the deployment's environmental effects, else the terrain's.
        var enviro = rules.Battle.EnviroArmorTransformations.ContainsKey(battle.EnviroEffects) ? battle.EnviroEffects : terrain.EnviroEffects;
        // deployXCOM: applyEnviroEffects, before any unit exists: the armor transformations and, with enviro effects, a
        // draw per faction for its environmental condition.
        state.ArmorTransformations = rules.Battle.EnviroArmorTransformations.GetValueOrDefault(enviro);
        if (rules.Battle.EnviroGlobalChances.TryGetValue(enviro, out var chances))
        {
            state.EnviroEffectsId = enviro;
            var friendly = RandomChance.Percent(random, chances.Friendly);
            var hostile = RandomChance.Percent(random, chances.Hostile);
            state.EnvironmentalConditions = (friendly, hostile, RandomChance.Percent(random, chances.Neutral));
        }
        CreateMapItems(state, rules, random, generated.Items);
        SetupObjectives(state, battle, rules);
        var unitSequence = XcomDeployment.Deploy(campaign, request, state, generated, inputs.StartingCondition, enviro);
        // run: the alien item level is the month, capped at the last month the mod lists.
        var levels = rules.Campaign.AlienItemLevels;
        state.AlienItemLevel = levels.Count == 0 ? campaign.MonthsPassed : Math.Min(campaign.MonthsPassed, levels.Count - 1);
        // Civilians come before the aliens for a positive civilian spawn node rank, else after them.
        if (battle.CivilianSpawnNodeRank > 0) unitSequence = CivilianDeployment.Deploy(state, rules, random, battle, terrain, unitSequence);
        // run: the alien custom deployment when it has data, else the battle's deployment.
        var (alienId, alienDeployment) = request.AlienCustomDeploymentId.Length != 0 &&
            rules.AlienDeployments.TryGet(request.AlienCustomDeploymentId, out var custom) && rules.AlienDeployments[custom].Value.Battle.Data.Count != 0
            ? (request.AlienCustomDeploymentId, rules.AlienDeployments[custom].Value) : (id, deployment);
        unitSequence = AlienDeployment.Deploy(campaign, request, state, alienId, alienDeployment, unitSequence);
        if (battle.CivilianSpawnNodeRank == 0) CivilianDeployment.Deploy(state, rules, random, battle, terrain, unitSequence);
        if (state.CraftInventoryTile is { } pile && battle.NoWeaponPile) SendItemsToLimbo(state, pile);
        if (generated.GenerateFuel) FuelPowerSources(state, rules, random);
        // setMusic: one of the deployment's tracks, else one of the terrain's.
        if (battle.Music.Count != 0) state.Music = battle.Music[random.NextInclusive(0, battle.Music.Count - 1)];
        else if (terrain.Music.Count != 0) state.Music = terrain.Music[random.NextInclusive(0, terrain.Music.Count - 1)];
        // explodePowerSources and explodeOtherJunk need explosions and terrain destruction.
        if (inputs.UfoCrashed && ObjectTiles(map, PowerSource).Any()) throw new MapGenerationException(CrashedPowerSources);
        if (state.Items.Any(item => item.Tile is not null && !item.XcomProperty && !item.IsSpecialWeapon && item.FuseTimer == -2 &&
                rules.Items.GetRequiredValue(item.ItemType).BattleType == GrenadeBattleType && !rules.Battle.Equipment.Items[item.ItemType].Spawns))
            throw new MapGenerationException(PrimedJunk);
        return state;
    }

    /// <summary>The battle with its map, before any unit or item is placed, and what the rest of the generation needs.</summary>
    internal sealed record GeneratedMap(BattleState State, BattleMapResult Generated, BattleMapInputs Inputs, RuntimeTerrain Terrain,
        string DeploymentId, RuntimeAlienDeploymentRule Deployment);

    /// <summary>run up to generateMap: the terrain, depth, shade and script, then the map (throws as <see cref="Generate"/>).</summary>
    internal static GeneratedMap GenerateMap(CampaignState campaign, CampaignDeploymentRequest request, BattleTerrainLoader loader)
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
        IRandomSource mapRandom = random;
        if (request.MissionType == BaseDefenseMissionType) (inputs, mapRandom) = WithBase(campaign, request, inputs, random);
        var generated = BattleMapGenerator.Generate(rules, loader, mapRandom, request.MissionType, terrain, globeTerrain, script,
            battle.Width, battle.Length, battle.Height, inputs);
        var map = generated.Map;
        if (map.Width == 0 || map.Length == 0 || map.Height == 0)
            throw new MapGenerationException($"Map script {scriptId} leaves an empty {map.Width}x{map.Length}x{map.Height} map.");
        var state = new BattleState(request.MissionType, id, map, battle.TurnLimit, battle.ChronoTrigger, battle.CheatTurn)
        {
            TerrainId = terrain.Id,
            MapScriptId = scriptId,
            StartingConditionId = inputs.StartingConditionId,
            AlienCustomDeploy = request.AlienCustomDeploymentId,
            AlienCustomMission = request.AlienCustomMissionId,
            Depth = depth,
            GlobalShade = shade,
            StorageSpace = generated.StorageSpace,
            BaseModules = generated.BaseModules,
            // generateBaseMap's inventory tile, which deployXCOM keeps.
            CraftInventoryTile = generated.BaseInventoryTile,
            ScriptValues = BattleState.EmptyScriptValues(campaign.Content),
            Rules = rules,
        };
        // generateMap: the terrain's ambience. Its first delay draws from RNG::seedless, a stream of its own, so the
        // game's draws are unchanged; the port seeds it from the campaign stream's state without drawing (ADR 0039).
        // The same stream then draws the units' presentation values (BattleUnit's recolor and a civilian's unified rank).
        state.Seedless = new SplitMix64RandomSource(random.State ^ SeedlessSalt);
        state.SetAmbience(terrain, state.Seedless);
        return new(state, generated, inputs, terrain, id, deployment);
    }

    private const string BaseDefenseMissionType = "STR_BASE_DEFENSE";

    /// <summary>Separates the seedless stream from the campaign stream it is seeded from.</summary>
    private const ulong SeedlessSalt = 0x5EED_1E55_A3B1_E7CEUL;

    /// <summary>
    /// generateMap's base defense inputs: the base's facilities and, with <c>baseDefenseMapFromLocation: 1</c>, the base
    /// terrain from the base's globe texture and a map drawn from a stream seeded by the base's position. The reference
    /// reseeds its one stream and restores it after attachNodeLinks; the port draws the map from a separate stream, which
    /// leaves the campaign's stream as the reference leaves its own (ADR 0038).
    /// </summary>
    private static (BattleMapInputs Inputs, IRandomSource Random) WithBase(CampaignState campaign, CampaignDeploymentRequest request,
        BattleMapInputs inputs, IRandomSource random)
    {
        var rules = campaign.Content.RuntimeRules;
        var owner = campaign.BaseById(request.BaseId)
            ?? throw new MapGenerationException($"The deployment request's base {request.BaseId} no longer exists.");
        var layout = new BattleBaseLayout(Array.AsReadOnly(owner.Facilities.Select(facility => new BattleBaseFacility(
            rules.Facilities.GetExternalId(facility.Rule), rules.Facilities[facility.Rule].Value, facility.X, facility.Y,
            facility.BuildTime == 0 || facility.HadPreviousFacility)).ToArray()));
        inputs = inputs with { Base = layout };
        if (rules.Campaign.BaseDefenseMapFromLocation != 1) return (inputs, random);
        var seeded = new SplitMix64RandomSource(BaseSeed(owner.Longitude, owner.Latitude));
        // The reference dereferences a missing texture.
        var texture = Texture(rules, request.GlobeTexture)
            ?? throw new MapGenerationException($"The base's globe texture {request.GlobeTexture} does not exist, so it has no base terrain.");
        var name = RuntimeTerrainCriterion.Pick(texture.BaseTerrains, owner.Longitude, owner.Latitude, seeded.NextInclusive);
        var baseTerrain = rules.Battle.Terrains.GetValueOrDefault(name)
            ?? throw new MapGenerationException(
                $"Map generator encountered an error: base terrain '{name}' of globe texture {request.GlobeTexture} does not exist.");
        return (inputs with { BaseTerrain = baseTerrain }, seeded);
    }

    /// <summary>
    /// generateMap's base seed, <c>uint64_t baseSeed = baseLon * baseLat * 1e6</c> with a zero coordinate counting as 1.
    /// A negative product, undefined in C++, wraps as the usual x86-64 conversion through a signed integer does.
    /// </summary>
    internal static ulong BaseSeed(double longitude, double latitude)
    {
        if (longitude == 0) longitude = 1.0;
        if (latitude == 0) latitude = 1.0;
        return unchecked((ulong)(long)(longitude * latitude * 1e6));
    }

    internal const string TextureTerrainWithoutTarget =
        "A base defense deployment without terrains would pick from its globe texture's terrains at no target, where the reference crashes.";

    internal const string CrashedPowerSources =
        "A crashed UFO's power sources explode when the battle starts; explosions arrive with combat (Phase 7 branch 5).";
    internal const string PrimedJunk =
        "Map grenades primed to explode at the start (fuse timer -2) need explosions, which arrive with combat (Phase 7 branch 5).";

    // SpecialTileType UFO_POWER_SOURCE and BattleType BT_GRENADE.
    private const int PowerSource = 2;
    private const int GrenadeBattleType = 4;

    /// <summary>
    /// BattlescapeGenerator::setupObjectives: bug hunt off, its minimum turn (the deployment's when set), the VIP rules,
    /// and the objective type and count when the map has map parts of that special type (all of them, or the required
    /// number when fewer).
    /// </summary>
    private static void SetupObjectives(BattleState state, RuntimeDeploymentBattle battle, RuntimeRuleCatalog rules)
    {
        state.BughuntMode = false;
        state.BughuntMinTurn = battle.BughuntMinTurn > 0 ? battle.BughuntMinTurn : rules.Campaign.BughuntMinTurn;
        state.VipSurvivalPercentage = battle.VipSurvivalPercentage;
        state.VipEscapeType = battle.EscapeType;
        if (battle.ObjectiveType <= -1) return;
        var map = state.Map;
        var count = 0;
        for (var tile = 0; tile < map.TileCount; tile++)
            foreach (var part in Enum.GetValues<BattleTilePart>())
                if (map.MapData(tile, part) is { } data && data.SpecialType == battle.ObjectiveType) count++;
        if (count == 0) return;
        state.ObjectiveType = battle.ObjectiveType;
        state.ObjectiveCount = count < battle.ObjectivesRequired || battle.ObjectivesRequired == 0 ? count : battle.ObjectivesRequired;
    }

    /// <summary>The tiles (in tile order) whose object part has the special type (Tile::getObjectSpecialTileType).</summary>
    private static IEnumerable<BattlePosition> ObjectTiles(BattleMap map, int specialType)
    {
        for (var tile = 0; tile < map.TileCount; tile++)
            if (map.MapData(tile, BattleTilePart.ObjectPart)?.SpecialType == specialType)
                yield return map.PositionOf(tile);
    }

    /// <summary>BattlescapeGenerator::fuelPowerSources: an alien fuel item on each power source.</summary>
    private static void FuelPowerSources(BattleState state, RuntimeRuleCatalog rules, IRandomSource random)
    {
        var equipment = new UnitEquipment(state, rules, random);
        foreach (var tile in ObjectTiles(state.Map, PowerSource).ToArray())
        {
            if (!rules.Items.TryGet(rules.Campaign.AlienFuelName, out _))
                throw new MapGenerationException($"The map has a UFO power source but the alien fuel '{rules.Campaign.AlienFuelName}' is no item.");
            equipment.CreateItemForTile(rules.Campaign.AlienFuelName, tile);
        }
    }

    /// <summary>BattlescapeGenerator::sendItemsToLimbo: the craft inventory tile's items and their loaded ammo go home unused.</summary>
    private static void SendItemsToLimbo(BattleState state, BattlePosition pile)
    {
        var leaving = new List<BattleItem>();
        foreach (var item in state.Items.Where(item => item.Tile == pile).ToArray())
        {
            leaving.Add(item);
            foreach (var slot in item.AmmoSlots)
                if (slot is { } id && id != item.Id) leaving.Add(state.Items.First(ammo => ammo.Id == id));
        }
        state.SendToLimbo(leaving);
    }

    /// <summary>The map blocks' items, created in loadMAP's order (their fuse timers, ammo quantities and loaded ammo).</summary>
    private static void CreateMapItems(BattleState state, RuntimeRuleCatalog rules, IRandomSource random, IReadOnlyList<BattleMapItem> items)
    {
        var equipment = new UnitEquipment(state, rules, random);
        var created = new List<BattleItem>(items.Count);
        foreach (var placement in items)
        {
            var item = equipment.CreateItemForTile(placement.ItemType, placement.Tile);
            if (placement.FuseTimer is { } fuse) item.SetFuseTimer(fuse, equipment.BattleItemRule(item.ItemType));
            if (placement.AmmoQuantity is { } quantity) item.AmmoQuantity = quantity;
            if (placement.LoadInto >= 0)
            {
                // BattleItem::setAmmoForSlot: the ammo leaves the tile and goes into the weapon.
                created[placement.LoadInto].AmmoSlots[placement.Slot] = item.Id;
                UnitEquipment.MoveToOwner(item, null);
                item.Slot = "";
                item.IsAmmo = true;
            }
            created.Add(item);
        }
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
        // The texture's pick is at the UFO or the mission site; a base defense has neither (a null target in the reference).
        else if (request.Target.Kind == WorldTargetKind.Base)
            throw new MapGenerationException(TextureTerrainWithoutTarget);
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
        var snapshot = request.Target.Kind == WorldTargetKind.Ufo
            ? campaign.Capture().World.Ufos.FirstOrDefault(candidate => candidate.UniqueId == request.Target.UniqueId)
            : null;
        var ufo = snapshot?.RuleId ?? "";
        var crashed = snapshot?.Status == UfoStatus.Crashed;
        var conditionId = deployment.StartingCondition.Length != 0 && rules.StartingConditions.TryGet(deployment.StartingCondition, out _)
            ? deployment.StartingCondition : Texture(rules, request.MissionTexture)?.StartingCondition ?? "";
        var condition = conditionId.Length != 0 && rules.StartingConditions.TryGet(conditionId, out var handle)
            ? rules.StartingConditions[handle].Value : null;
        var skin = campaign.BaseById(request.BaseId) is { } owner
            ? campaign.CraftById(owner, request.CraftId, request.CraftTypeId)?.Logistics?.SkinIndex ?? 0 : 0;
        return new BattleMapInputs(craft, ufo, deployment.Battle.CustomUfo, condition)
        {
            StartingConditionId = condition is null ? "" : conditionId,
            UfoCrashed = crashed,
            CraftSkin = skin,
        };
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
