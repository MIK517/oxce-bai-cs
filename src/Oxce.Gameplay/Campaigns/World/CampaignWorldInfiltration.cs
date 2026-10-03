using Oxce.Mods.Rulesets.Runtime;
using Oxce.Mods.Rulesets.CampaignStart;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private static bool CompletesInfiltration(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave) =>
        rule.Objective == RuntimeMissionObjective.Infiltration &&
        mission.NextWave + 1 == rule.Waves.Count && (ulong)mission.NextUfoCounter + 1 >= wave.UfoCount;

    private string? InfiltrationReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        CampaignTimeTrigger highest, Dictionary<string, int> markers, ref long work)
    {
        try { return InfiltrationPreparationReason(mission, rule, highest, markers, ref work); }
        catch (InvalidDataException error) { return error.Message; }
    }

    private string? InfiltrationPreparationReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        CampaignTimeTrigger highest, Dictionary<string, int> markers, ref long work)
    {
        if (rule.EndlessInfiltration)
        {
            work += _ufos.Count;
            if (work > MaximumAlienBaseChecks) return "Infiltration country search exceeds the supported work limit.";
            if (rule.Waves.Count > 1 && _ufos.Any(ufo => ufo.MissionId == mission.Id && ufo.MissionWaveNumber == -1))
                return "Endless infiltration completion requires legacy UFO wave forecasts.";
            var timer = rule.Waves[0].SpawnTimer;
            if (timer > int.MaxValue || (timer / 30 / 2 + timer / 30) * 30 > int.MaxValue)
                return "Endless infiltration restart timer exceeds the supported range.";
        }
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        work += (long)campaign.CountryStates.Count * (region.Areas.Count + 1L);
        if (work > MaximumAlienBaseChecks) return "Infiltration country search exceeds the supported work limit.";
        var eligible = campaign.CountryStates.Count(country => Eligible(country, region));
        if (eligible == 0) return null;
        if (rules.Campaign.BuildInfiltrationBaseCloseToTheCountry)
            return CountryInfiltrationReason(mission, rule, region, highest, markers, ref work);
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

    private string? CountryInfiltrationReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        RuntimeRegionRule region, CampaignTimeTrigger highest, Dictionary<string, int> markers, ref long work)
    {
        var rules = campaign.Content.RuntimeRules;
        long geometry = 1;
        foreach (var polygon in rules.Campaign.Globe.Polygons) geometry += polygon.Points.Count;
        var areas = new List<MissionArea>();
        foreach (var country in campaign.CountryStates)
        {
            if (!Eligible(country, region)) continue;
            var countryRule = rules.Countries[country.Rule].Value;
            if (countryRule.Areas.Count == 0) return "Infiltration country has no spawn areas.";
            work += 100L * (2 * geometry + countryRule.Areas.Count + 1L);
            if (work > MaximumAlienBaseChecks) return "Infiltration country search exceeds the supported work limit.";
            foreach (var area in countryRule.Areas)
            {
                if (area.LongitudeMinimum <= area.LongitudeMaximum)
                    areas.Add(new(area.LongitudeMinimum, area.LongitudeMaximum, area.LatitudeMinimum, area.LatitudeMaximum, 0, ""));
                else
                {
                    areas.Add(new(0, area.LongitudeMaximum, area.LatitudeMinimum, area.LatitudeMaximum, 0, ""));
                    areas.Add(new(area.LongitudeMinimum, 359.99 * Math.PI / 180, area.LatitudeMinimum, area.LatitudeMaximum, 0, ""));
                }
            }
        }
        // One birth, but any eligible country's area may win after suppressed retries.
        return ReserveObjectiveBase(rule with { Points = 0 }, areas, highest, markers, ref work);
    }

    private bool Eligible(CampaignState.CountryState country, RuntimeRegionRule region) =>
        WorldInfiltration.CanInfiltrate(country.Pact, country.NewPact, country.CancelPact) &&
        WorldGeometry.InsideRegion(region, new(campaign.Content.RuntimeRules.Countries[country.Rule].Value.LabelLongitude,
            campaign.Content.RuntimeRules.Countries[country.Rule].Value.LabelLatitude));

    private void CompleteInfiltration(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        foreach (var country in WorldInfiltration.CountryOrder(campaign.CountryStates,
            rules.Campaign.InfiltrateRandomCountryInTheRegion, campaign.Random))
        {
            if (!Eligible(country, region)) continue;
            var selection = rules.Campaign.BuildInfiltrationBaseCloseToTheCountry
                ? WorldInfiltration.SelectCountry(rules, rule, rules.Countries[country.Rule].Value, campaign.Random)
                : WorldInfiltration.SelectRegion(rules, rule, region, campaign.Random);
            if (selection is not { } selected) continue;
            // Country::setNewPact does not sign the pact or execute an event.
            country.NewPact = true;
            country.CancelPact = false;
            SpawnMissionAlienBase(mission, rule, selected.Deployment, selected.Position,
                rules.Countries.GetExternalId(country.Rule));
            break;
        }
    }
}
