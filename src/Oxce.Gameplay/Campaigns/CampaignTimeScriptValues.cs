namespace Oxce.Gameplay.Campaigns;

/// <summary>GameTime getters registered by SavedGame::ScriptRegister for price and detection scripts.</summary>
internal static class CampaignTimeScriptValues
{
    internal static bool Supports(string name) => name is
        "Time.getDay" or "Time.getMonth" or "Time.getYear" or "Time.getHour" or "Time.getMinute" or
        "Time.getSecond" or "Time.getSecondsPastMidnight" or "Time.getDaysPastEpoch";

    internal static int Value(string name, CampaignTime time) => name switch
    {
        "Time.getDay" => time.Day,
        "Time.getMonth" => time.Month,
        "Time.getYear" => time.Year,
        "Time.getHour" => time.Hour,
        "Time.getMinute" => time.Minute,
        "Time.getSecond" => time.Second,
        "Time.getSecondsPastMidnight" => time.Hour * 3600 + time.Minute * 60 + time.Second,
        "Time.getDaysPastEpoch" => DaysPastEpoch(time),
        _ => throw new InvalidOperationException("Time script provider was not preflighted."),
    };

    /// <summary>
    /// getDaysPastEpochScript: <c>(int)(std::mktime(&amp;tm) / 86400)</c>. Native mktime reads
    /// the host's local zone; the port fixes it to UTC so results do not depend on the
    /// machine (identical to the reference on a UTC host). Division truncates toward
    /// zero and the final narrowing wraps, as in C++.
    /// </summary>
    internal static int DaysPastEpoch(CampaignTime time)
    {
        var seconds = DaysFromCivil(time.Year, time.Month, time.Day) * 86400L +
            time.Hour * 3600L + time.Minute * 60L + time.Second;
        return unchecked((int)(seconds / 86400));
    }

    // Proleptic Gregorian day number relative to 1970-01-01 (H. Hinnant's days_from_civil).
    private static long DaysFromCivil(long year, int month, int day)
    {
        year -= month <= 2 ? 1 : 0;
        var era = (year >= 0 ? year : year - 399) / 400;
        var yearOfEra = year - era * 400;
        var dayOfYear = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
        var dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear;
        return era * 146097 + dayOfEra - 719468;
    }
}
