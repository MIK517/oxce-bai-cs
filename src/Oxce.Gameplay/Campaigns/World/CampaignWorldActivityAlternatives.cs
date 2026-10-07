namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    // Generated candidates are collected in source/caller order. Each source
    // creates one mission; candidates are choices, while waves are cumulative.
    private sealed class GeneratedActivityAlternatives
    {
        private (WorldTargetReference Source, bool Hunting)? _source;
        private ActivityRange[]? _regions;
        private ActivityRange[]? _countries;

        public string? Begin(WorldTargetReference source, bool hunting, ActivityRange[] regions, ActivityRange[] countries,
            ref long work, out ActivityRange[] candidateRegions, out ActivityRange[] candidateCountries)
        {
            candidateRegions = regions;
            candidateCountries = countries;
            work += 4L * (regions.Length + countries.Length);
            if (work > MaximumAlienBaseChecks) return "Alien mission activity forecasts exceed the supported work limit.";
            var key = (source, hunting);
            if (_source != key)
            {
                _source = key;
                _regions = (ActivityRange[])regions.Clone();
                _countries = (ActivityRange[])countries.Clone();
            }
            candidateRegions = (ActivityRange[])_regions!.Clone();
            candidateCountries = (ActivityRange[])_countries!.Clone();
            return null;
        }

        public static void Complete(ActivityRange[] regions, ActivityRange[] countries,
            ActivityRange[] candidateRegions, ActivityRange[] candidateCountries)
        {
            for (var index = 0; index < regions.Length; index++) regions[index].IncludeAlternative(candidateRegions[index]);
            for (var index = 0; index < countries.Length; index++) countries[index].IncludeAlternative(candidateCountries[index]);
        }
    }
}
