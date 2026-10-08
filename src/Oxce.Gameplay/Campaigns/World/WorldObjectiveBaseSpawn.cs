using Oxce.Core.Random;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>AlienMission::think's base objective and chooseAlienBaseType, without state effects.</summary>
internal static class WorldObjectiveBaseSpawn
{
    internal static bool RevealAfterMissionThink(bool created, bool discovered, bool show) => created && !discovered && show;

    internal static bool CreatesOnLanding(RuntimeAlienMissionRule rule, RuntimeMissionWave wave, int currentZone,
        bool allowed, bool conversion) => allowed && !conversion && rule.Objective == RuntimeMissionObjective.Base &&
        wave.ObjectiveOnTheLandingSite && currentZone == rule.SpawnZone;

    internal readonly record struct Selection(WorldPosition Position, RuleHandle<AlienDeploymentRuleFamily> Deployment);

    internal static RuleHandle<AlienDeploymentRuleFamily> Deployment(
        RuntimeRuleCatalog rules, RuntimeAlienMissionRule mission, MissionArea area, IRandomSource random)
    {
        if (mission.SiteTypeId.Length != 0) return rules.AlienDeployments.GetRequired(mission.SiteTypeId);
        if (rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture) && texture.Deployments.Count != 0)
        {
            var id = texture.Deployments.Count == 1 ? texture.Deployments.First().Key :
                WorldAlienMissionGeneration.Choose(texture.Deployments, random);
            // A nonempty texture table is a required lookup, even if its weights
            // produce an empty ID. It does not use the emergency fallback.
            return rules.AlienDeployments.GetRequired(id);
        }
        return rules.AlienDeployments.GetRequired("STR_ALIEN_BASE_ASSAULT");
    }

    internal static Selection? Select(RuntimeRuleCatalog rules, RuntimeAlienMissionRule mission,
        RuntimeRegionRule region, IRandomSource random) =>
        Select(region, mission.SpawnZone, mission.SiteTypeId.Length == 0, rules.Campaign.AllowAlienBasesOnWrongTextures,
            random, area => Deployment(rules, mission, area, random),
            handle => rules.AlienDeployments[handle].Value.FakeUnderwaterSpawnChance,
            point => WorldGeometry.InsideLand(rules.Campaign.Globe, point),
            point => WorldGeometry.InsideFakeUnderwaterTexture(rules.Campaign.Globe, point));

    internal static Selection? Select(RuntimeRegionRule region, int zone, bool dynamic,
        bool allowWrongTextures, IRandomSource random,
        Func<MissionArea, RuleHandle<AlienDeploymentRuleFamily>> deployment,
        Func<RuleHandle<AlienDeploymentRuleFamily>, int> fakeChance,
        Func<WorldPosition, bool> insideLand, Func<WorldPosition, bool> insideFakeWater, bool ignoreTechnicalRegion = true)
    {
        var areas = WorldGeometry.MissionAreas(region, zone);
        RuleHandle<AlienDeploymentRuleFamily> selected = default;
        var wantsFake = false;
        for (var attempt = 1; attempt <= 100; attempt++)
        {
            var area = areas[random.NextInclusive(0, areas.Count - 1)];
            var point = new WorldPosition(
                WorldGeometry.RandomDouble(random, Math.Min(area.LongitudeMinimum, area.LongitudeMaximum),
                    Math.Max(area.LongitudeMinimum, area.LongitudeMaximum)),
                WorldGeometry.RandomDouble(random, Math.Min(area.LatitudeMinimum, area.LatitudeMaximum),
                    Math.Max(area.LatitudeMinimum, area.LatitudeMaximum)));
            // Objective search chooses its type AFTER coordinates, and repeats
            // both deployment selection and preference for every dynamic attempt.
            if (attempt == 1 || dynamic)
            {
                selected = deployment(area);
                wantsFake = RandomChance.Percent(random, fakeChance(selected));
            }
            if (attempt == 100) return allowWrongTextures ? new(point, selected) : null;
            if (insideLand(point) && WorldGeometry.InsideRegion(region, point, ignoreTechnicalRegion) &&
                insideFakeWater(point) == wantsFake) return new(point, selected);
        }
        throw new InvalidOperationException("Base objective search exceeded its finite bound.");
    }
}
