using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private static bool CompletesInfiltration(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave) =>
        rule.Objective == RuntimeMissionObjective.Infiltration &&
        mission.NextWave + 1 == rule.Waves.Count && (ulong)mission.NextUfoCounter + 1 >= wave.UfoCount;

    private string? InfiltrationReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        CampaignTimeTrigger highest, Dictionary<string, int> markers, ref long work)
    {
        if (rule.EndlessInfiltration) return "Endless infiltration requires repeating wave forecasts.";
        var rules = campaign.Content.RuntimeRules;
        if (rules.Campaign.BuildInfiltrationBaseCloseToTheCountry)
            return "Infiltration country-border search is unavailable.";
        if (rules.Campaign.InfiltrateRandomCountryInTheRegion)
            return "Infiltration country shuffle is unavailable.";
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        work += (long)campaign.CountryStates.Count * (region.Areas.Count + 1L);
        if (work > MaximumAlienBaseChecks) return "Infiltration country search exceeds the supported work limit.";
        var eligible = campaign.CountryStates.Count(country => Eligible(country, region));
        if (eligible == 0) return null;
        var areas = WorldGeometry.MissionAreas(region, rule.SpawnZone);
        long search = region.Areas.Count + 1L;
        foreach (var polygon in rules.Campaign.Globe.Polygons) search += 2L * polygon.Points.Count;
        foreach (var area in areas)
            search += 1L + (rule.SiteTypeId.Length == 0 &&
                rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture) ? 3L * texture.Deployments.Count : 1);
        // Suppression can repeat all 100 attempts for every eligible country.
        work += eligible * 100L * search;
        if (work > MaximumAlienBaseChecks) return "Infiltration country search exceeds the supported work limit.";
        // Regional searches have the same possible birth regardless of country.
        // Only one country may sign per mission completion.
        return ObjectiveBaseSpawnReason(mission, rule with { Points = 0 }, highest, markers, ref work);
    }

    private bool Eligible(CampaignState.CountryState country, RuntimeRegionRule region) =>
        WorldInfiltration.CanInfiltrate(country.Pact, country.NewPact, country.CancelPact) &&
        WorldGeometry.InsideRegion(region, new(campaign.Content.RuntimeRules.Countries[country.Rule].Value.LabelLongitude,
            campaign.Content.RuntimeRules.Countries[country.Rule].Value.LabelLatitude));

    private void CompleteInfiltration(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        foreach (var country in campaign.CountryStates)
        {
            if (!Eligible(country, region)) continue;
            if (WorldInfiltration.SelectRegion(rules, rule, region, campaign.Random) is not { } selected) continue;
            // Country::setNewPact does not sign the pact or execute an event.
            country.NewPact = true;
            country.CancelPact = false;
            SpawnMissionAlienBase(mission, rule, selected.Deployment, selected.Position,
                rules.Countries.GetExternalId(country.Rule));
            break;
        }
    }
}
