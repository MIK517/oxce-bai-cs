using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Country::canBeInfiltrated; pending pacts do not sign until month-end.</summary>
internal static class WorldInfiltration
{
    internal static bool CanInfiltrate(bool pact, bool newPact, bool cancelPact) =>
        !pact && !newPact || pact && cancelPact;
    internal static WorldObjectiveBaseSpawn.Selection? SelectRegion(RuntimeRuleCatalog rules,
        RuntimeAlienMissionRule mission, RuntimeRegionRule region, IRandomSource random) =>
        WorldObjectiveBaseSpawn.Select(region, mission.SpawnZone, mission.SiteTypeId.Length == 0,
            rules.Campaign.AllowAlienBasesOnWrongTextures, random,
            area => WorldObjectiveBaseSpawn.Deployment(rules, mission, area, random),
            handle => rules.AlienDeployments[handle].Value.FakeUnderwaterSpawnChance,
            point => WorldGeometry.InsideLand(rules.Campaign.Globe, point),
            point => WorldGeometry.InsideFakeUnderwaterTexture(rules.Campaign.Globe, point),
            ignoreTechnicalRegion: false);
}
