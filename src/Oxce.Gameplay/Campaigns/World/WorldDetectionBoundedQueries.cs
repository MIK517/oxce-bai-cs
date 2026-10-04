namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Result domains guaranteed by native boolean getters and validated campaign clocks, including null zero.</summary>
internal static class WorldDetectionBoundedQueries
{
    internal static (int Min, int Max)? Range(string name) => name switch
    {
        "Ufo.getDetected" or "Ufo.getHyperDetected" or "Ufo.isHunterKiller" or "Ufo.isHunting" or "Ufo.isEscorting" or
            "Country.getPact" or "RuleItem.hasCategory" or "GeoscapeGame.isResearched" => (0, 1),
        "Time.getDay" => (0, 31),
        "Time.getMonth" => (0, 12),
        "Time.getHour" => (0, 23),
        "Time.getMinute" or "Time.getSecond" => (0, 59),
        "Time.getSecondsPastMidnight" => (0, 86399),
        _ => null,
    };
}
