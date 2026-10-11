using Oxce.Core.Random;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Country::canBeInfiltrated; pending pacts do not sign until month-end.</summary>
internal static class WorldInfiltration
{
    internal static bool CanInfiltrate(bool pact, bool newPact, bool cancelPact) =>
        !pact && !newPact || pact && cancelPact;
    internal static AlienMissionSnapshot Restart(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, IRandomSource random) =>
        mission with { NextWave = 0, SpawnCountdown = WorldTrajectory.SpawnCountdown(checked((int)rule.Waves[0].SpawnTimer), random) };

    internal static IReadOnlyList<T> CountryOrder<T>(IReadOnlyList<T> countries, bool shuffle, IRandomSource random)
    {
        if (!shuffle) return countries;
        var result = countries.ToArray();
        for (var index = result.Length - 1; index > 0; index--)
        {
            var other = random.NextInclusive(0, index);
            (result[index], result[other]) = (result[other], result[index]);
        }
        return result;
    }

    internal static WorldObjectiveBaseSpawn.Selection? SelectRegion(RuntimeRuleCatalog rules,
        RuntimeAlienMissionRule mission, RuntimeRegionRule region, IRandomSource random) =>
        WorldObjectiveBaseSpawn.Select(region, mission.SpawnZone, mission.SiteTypeId.Length == 0,
            rules.Campaign.AllowAlienBasesOnWrongTextures, random,
            area => WorldObjectiveBaseSpawn.Deployment(rules, mission, area, random),
            handle => rules.AlienDeployments[handle].Value.FakeUnderwaterSpawnChance,
            point => WorldGeometry.InsideLand(rules.Campaign.Globe, point),
            point => WorldGeometry.InsideFakeUnderwaterTexture(rules.Campaign.Globe, point),
            ignoreTechnicalRegion: false);

    internal static WorldObjectiveBaseSpawn.Selection? SelectCountry(RuntimeRuleCatalog rules,
        RuntimeAlienMissionRule mission, RuntimeCountryRule country, IRandomSource random) =>
        SelectCountry(country.Areas, rules.Campaign.AllowAlienBasesOnWrongTextures, random,
            () => WorldObjectiveBaseSpawn.Deployment(rules, mission, new(0, 0, 0, 0, 0, ""), random),
            handle => rules.AlienDeployments[handle].Value.FakeUnderwaterSpawnChance,
            point => WorldGeometry.InsideLand(rules.Campaign.Globe, point),
            point => WorldGeometry.InsideCountry(country, point),
            point => WorldGeometry.InsideFakeUnderwaterTexture(rules.Campaign.Globe, point));

    internal static WorldObjectiveBaseSpawn.Selection? SelectCountry(IReadOnlyList<GeographicArea> areas,
        bool allowWrongTextures, IRandomSource random, Func<RuleHandle<AlienDeploymentRuleFamily>> deployment,
        Func<RuleHandle<AlienDeploymentRuleFamily>, int> fakeChance,
        Func<WorldPosition, bool> insideLand, Func<WorldPosition, bool> insideCountry,
        Func<WorldPosition, bool> insideFakeWater)
    {
        if (areas.Count == 0) throw new InvalidDataException("Infiltration country has no spawn areas.");
        var selected = deployment();
        var wantsFake = RandomChance.Percent(random, fakeChance(selected));
        for (var attempt = 1; attempt <= 100; attempt++)
        {
            var area = areas[random.NextInclusive(0, areas.Count - 1)];
            var minimum = area.LongitudeMinimum;
            var maximum = area.LongitudeMaximum;
            if (minimum > maximum)
            {
                if (RandomChance.Percent(random, 50)) minimum = 0;
                else maximum = 359.99 * Math.PI / 180;
            }
            var point = new WorldPosition(
                WorldGeometry.RandomDouble(random, Math.Min(minimum, maximum), Math.Max(minimum, maximum)),
                WorldGeometry.RandomDouble(random, Math.Min(area.LatitudeMinimum, area.LatitudeMaximum),
                    Math.Max(area.LatitudeMinimum, area.LatitudeMaximum)));
            if (attempt == 100) return allowWrongTextures ? new(point, selected) : null;
            if (insideLand(point) && insideCountry(point) && insideFakeWater(point) == wantsFake)
                return new(point, selected);
        }
        throw new InvalidOperationException("Infiltration country search exceeded its finite bound.");
    }
}
