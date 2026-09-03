namespace Oxce.Gameplay.Campaigns;

public enum CampaignTimeTrigger
{
    FiveSeconds,
    TenMinutes,
    ThirtyMinutes,
    OneHour,
    OneDay,
    OneMonth,
}

public readonly record struct CampaignTime(
    int Weekday,
    int Day,
    int Month,
    int Year,
    int Hour,
    int Minute,
    int Second)
{
    public CampaignTime Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Weekday, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Weekday, 7);
        ArgumentOutOfRangeException.ThrowIfLessThan(Month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Month, 12);
        ArgumentOutOfRangeException.ThrowIfNegative(Year);
        ArgumentOutOfRangeException.ThrowIfNegative(Hour);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Hour, 23);
        ArgumentOutOfRangeException.ThrowIfNegative(Minute);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Minute, 59);
        ArgumentOutOfRangeException.ThrowIfNegative(Second);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Second, 59);
        ArgumentOutOfRangeException.ThrowIfLessThan(Day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Day, DaysInMonth(Year, Month));
        return this;
    }

    public CampaignTime Advance(out CampaignTimeTrigger trigger)
    {
        Validate();
        trigger = CampaignTimeTrigger.FiveSeconds;
        var second = Second + 5;
        var minute = Minute;
        var hour = Hour;
        var weekday = Weekday;
        var day = Day;
        var month = Month;
        var year = Year;
        if (second >= 60)
        {
            minute++;
            second = 0;
            if (minute % 10 == 0) trigger = CampaignTimeTrigger.TenMinutes;
            if (minute % 30 == 0) trigger = CampaignTimeTrigger.ThirtyMinutes;
        }
        if (minute >= 60)
        {
            hour++;
            minute = 0;
            trigger = CampaignTimeTrigger.OneHour;
        }
        if (hour >= 24)
        {
            day++;
            weekday++;
            hour = 0;
            trigger = CampaignTimeTrigger.OneDay;
        }
        if (weekday > 7) weekday = 1;
        if (day > DaysInMonth(year, month))
        {
            day = 1;
            month++;
            trigger = CampaignTimeTrigger.OneMonth;
        }
        if (month > 12)
        {
            month = 1;
            year++;
        }
        return new CampaignTime(weekday, day, month, year, hour, minute, second);
    }

    private static int DaysInMonth(int year, int month) => month switch
    {
        2 when year % 4 == 0 && (year % 100 != 0 || year % 400 == 0) => 29,
        2 => 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };
}
