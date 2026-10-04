namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Result domains guaranteed by native getters, validated clocks and proven null receivers.</summary>
internal static class WorldDetectionBoundedQueries
{
    internal static (int Min, int Max)? Range(string name) => name switch
    {
        "Ufo.getDetected" or "Ufo.getHyperDetected" or "Ufo.isHunterKiller" or "Ufo.isHunting" or "Ufo.isEscorting" or
            "Country.getPact" or "RuleItem.hasCategory" or "GeoscapeGame.isResearched" => (0, 1),
        "Ufo.getAltitude" => (0, WorldAltitudes.All.Count - 1),
        "Ufo.getStatus" => (0, (int)UfoStatus.IgnoreMe),
        "Time.getDay" => (0, 31),
        "Time.getMonth" => (0, 12),
        "Time.getHour" => (0, 23),
        "Time.getMinute" or "Time.getSecond" => (0, 59),
        "Time.getSecondsPastMidnight" => (0, 86399),
        _ => null,
    };

    internal static (int Min, int Max) CallerRange(string parser, string name) =>
        parser is "detectUfoFromBase" or "detectUfoFromCraft" ? name switch
        {
            "already_tracked" => (0, 1),
            // Actual callers convert acos globe distance (0..pi radians) to nautical miles.
            "distance" => (0, 10800),
            _ => WorldDetectionScalarRanges.Full,
        } : WorldDetectionScalarRanges.Full;

    internal static bool NullReturnsZero(string name) => Range(name) is not null || name is
        "Ufo.getAltitude" or "Ufo.getStatus" or "Ufo.getVisibility" or "Ufo.getDamage" or "Ufo.getDamageMax" or
        "Ufo.getDamagePercentage" or "Ufo.getShield" or "Ufo.getShieldMax" or "Ufo.getShieldPercentage" or
        "Ufo.getHuntMode" or "Ufo.getHuntBehavior" or "Craft.getDamage" or "Craft.getDamageMax" or
        "Craft.getDamagePercentage" or "Craft.getShield" or "Craft.getShieldMax" or "Craft.getShieldPercentage" or
        "Country.getCurrentFunding" or "Country.getCurrentActivityXcom" or "Country.getCurrentActivityAlien" or
        "GeoscapeGame.difficultyLevel" or "GeoscapeGame.getDaysPassed" or "GeoscapeGame.getMonthsPassed" or "Time.getYear" ||
        name.StartsWith("Ufo.", StringComparison.Ordinal) && CampaignWorld.DetectionUfoStatIndex(name) >= 0 ||
        name.StartsWith("Craft.", StringComparison.Ordinal) && CampaignWorld.DetectionCraftStatKey(name) is not null;
}
