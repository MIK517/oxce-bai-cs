using Oxce.Mods.Rulesets.Runtime;
using Oxce.Mods.Rulesets.CampaignStart;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private static bool CompletesInfiltration(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave) =>
        rule.Objective == RuntimeMissionObjective.Infiltration &&
        mission.NextWave + 1 == rule.Waves.Count && (ulong)mission.NextUfoCounter + 1 >= wave.UfoCount;

    private string? InfiltrationReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        CampaignTimeTrigger highest, Dictionary<string, int> markers, ref long work,
        GeneratedTargetReservations? generated = null, WorldTargetReference? source = null)
    {
        try { return InfiltrationPreparationReason(mission, rule, highest, markers, ref work, generated, source); }
        catch (InvalidDataException error) { return error.Message; }
    }

    private string? InfiltrationPreparationReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        CampaignTimeTrigger highest, Dictionary<string, int> markers, ref long work,
        GeneratedTargetReservations? generated, WorldTargetReference? source)
    {
        if (rule.EndlessInfiltration)
        {
            var timer = rule.Waves[0].SpawnTimer;
            if (timer > int.MaxValue || (timer / 30 / 2 + timer / 30) * 30 > int.MaxValue)
                return "Endless infiltration restart timer exceeds the supported range.";
        }
        var rules = campaign.Content.RuntimeRules;
        if (PrepareInfiltrationCountries(highest, ref work) is { } countryReason) return countryReason;
        var region = rules.Regions.GetRequiredValue(mission.RegionId);
        work += (long)campaign.CountryStates.Count * (region.Areas.Count + 1L);
        if (work > MaximumWorldWork) return ForecastLimits.InfiltrationCountrySearchWork;
        var eligible = campaign.CountryStates.Count(country => EligibleAfterDailyDestruction(country, region));
        if (eligible == 0) return null;
        if (rules.Campaign.BuildInfiltrationBaseCloseToTheCountry)
            return CountryInfiltrationReason(mission, rule, region, highest, markers, ref work, generated, source);
        var areas = WorldGeometry.MissionAreas(region, rule.SpawnZone);
        long search = region.Areas.Count + 1L;
        foreach (var polygon in rules.Campaign.Globe.Polygons) search += 2L * polygon.Points.Count;
        foreach (var area in areas)
            search += 1L + (rule.SiteTypeId.Length == 0 &&
                rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture) ? 3L * texture.Deployments.Count : 1);
        // Suppression can repeat all 100 attempts for every eligible country.
        work += eligible * 100L * search;
        if (work > MaximumWorldWork) return ForecastLimits.InfiltrationCountrySearchWork;
        // Regional searches have the same possible birth regardless of country.
        // Only one country may sign per mission completion.
        return ObjectiveBaseSpawnReason(mission, rule with { Points = 0 }, highest, markers, ref work, generated, source);
    }

    private string? CountryInfiltrationReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        RuntimeRegionRule region, CampaignTimeTrigger highest, Dictionary<string, int> markers, ref long work,
        GeneratedTargetReservations? generated, WorldTargetReference? source)
    {
        var rules = campaign.Content.RuntimeRules;
        long geometry = 1;
        foreach (var polygon in rules.Campaign.Globe.Polygons) geometry += polygon.Points.Count;
        var areas = new List<MissionArea>();
        foreach (var country in campaign.CountryStates)
        {
            if (!EligibleAfterDailyDestruction(country, region)) continue;
            var countryRule = rules.Countries[country.Rule].Value;
            if (countryRule.Areas.Count == 0) return "Infiltration country has no spawn areas.";
            work += 100L * (2 * geometry + countryRule.Areas.Count + 1L);
            if (work > MaximumWorldWork) return ForecastLimits.InfiltrationCountrySearchWork;
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
        return ReserveObjectiveBase(rule with { Points = 0 }, areas, highest, markers, ref work, generated: generated, source: source);
    }

    private bool Eligible(CampaignState.CountryState country, RuntimeRegionRule region) =>
        WorldInfiltration.CanInfiltrate(country.Pact, country.NewPact, country.CancelPact) &&
        WorldGeometry.InsideRegion(region, new(campaign.Content.RuntimeRules.Countries[country.Rule].Value.LabelLongitude,
            campaign.Content.RuntimeRules.Countries[country.Rule].Value.LabelLatitude));

    private string? PrepareInfiltrationCountries(CampaignTimeTrigger highest, ref long work)
    {
        if (_pass.InfiltrationCountries is not null) return null;
        var rules = campaign.Content.RuntimeRules;
        var destruction = highest >= CampaignTimeTrigger.OneDay && rules.Campaign.AllowCountriesToCancelAlienPact;
        work += campaign.CountryStates.Count * (destruction ? 3L * _alienBases.Count + 1 : 1L);
        if (work > MaximumWorldWork) return ForecastLimits.InfiltrationCountrySearchWork;
        var eligible = new HashSet<CampaignState.CountryState>(ReferenceEqualityComparer.Instance);
        foreach (var country in campaign.CountryStates)
        {
            var newPact = country.NewPact;
            var cancelPact = country.CancelPact;
            var possibleCancellation = false;
            if (destruction)
            {
                var id = rules.Countries.GetExternalId(country.Rule);
                foreach (var alienBase in _alienBases)
                    if (alienBase.PactCountryId == id)
                    {
                        if (BaseDestroyedAfterDailyResearch(alienBase, _pass.DailyResearchForecast))
                        {
                            newPact = false;
                            cancelPact = country.Pact;
                            break;
                        }
                        possibleCancellation |= _pass.PossibleDailyBaseDeletions?.Contains((alienBase.DeploymentId, alienBase.Id)) == true;
                    }
            }
            // The selected deletion applies Country::setCancelPact before every
            // later infiltration caller. Reserve both eligibility outcomes; no
            // country fields or random country order are changed during proof.
            if (WorldInfiltration.CanInfiltrate(country.Pact, newPact, cancelPact) ||
                possibleCancellation && WorldInfiltration.CanInfiltrate(country.Pact, false, country.Pact)) eligible.Add(country);
        }
        _pass.InfiltrationCountries = eligible;
        return null;
    }

    private bool EligibleAfterDailyDestruction(CampaignState.CountryState country, RuntimeRegionRule region) =>
        _pass.InfiltrationCountries!.Contains(country) &&
        WorldGeometry.InsideRegion(region, new(campaign.Content.RuntimeRules.Countries[country.Rule].Value.LabelLongitude,
            campaign.Content.RuntimeRules.Countries[country.Rule].Value.LabelLatitude));

    private void CompleteInfiltration(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions.GetRequiredValue(mission.RegionId);
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
