using Oxce.Gameplay.Campaigns.World;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    // SavedGame::isItemObtained/isFacilityBuilt/isSoldierTypeHired and locateRegion/Country.
    // Transfers, worn armor and installed craft weapons do not count as obtained items.
    internal WorldScriptContext WorldScriptContext(int score, long funds, IReadOnlyDictionary<string, int> missionRuns)
    {
        var rules = _content.RuntimeRules;
        var items = new HashSet<string>(StringComparer.Ordinal);
        var facilities = new HashSet<string>(StringComparer.Ordinal);
        var soldiers = new HashSet<string>(StringComparer.Ordinal);
        var regions = new HashSet<string>(StringComparer.Ordinal);
        var countries = new HashSet<string>(StringComparer.Ordinal);
        var pacts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in _bases)
        {
            foreach (var item in owner.Items)
                if (item.Value > 0) items.Add(rules.Items.GetExternalId(item.Key));
            foreach (var craft in owner.Crafts)
                if (craft.Logistics is { } state)
                    foreach (var item in state.Items)
                        if (item.Value > 0 && rules.Items.TryGet(item.Key, out _)) items.Add(item.Key);
            foreach (var facility in owner.Facilities)
                if (facility.BuildTime == 0) facilities.Add(rules.Facilities.GetExternalId(facility.Rule));
            foreach (var soldier in owner.Soldiers) soldiers.Add(rules.Soldiers.GetExternalId(soldier.Rule));
            var position = new WorldPosition(owner.Longitude, owner.Latitude);
            foreach (var region in _regions)
                if (WorldGeometry.InsideRegion(rules.Regions[region.Rule].Value, position))
                { regions.Add(rules.Regions.GetExternalId(region.Rule)); break; }
            foreach (var country in _countries)
                if (WorldGeometry.InsideCountry(rules.Countries[country.Rule].Value, position))
                { countries.Add(rules.Countries.GetExternalId(country.Rule)); break; }
        }
        foreach (var country in _countries)
            if (country.Pact) pacts.Add(rules.Countries.GetExternalId(country.Rule));
        return new(MonthsPassed, (int)Difficulty, score, funds, _debugMode, _completedResearch,
            items, facilities, soldiers, regions, countries, pacts, missionRuns, _nextIds);
    }
}
