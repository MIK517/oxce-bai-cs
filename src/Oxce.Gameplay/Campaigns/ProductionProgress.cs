namespace Oxce.Gameplay.Campaigns;

/// <summary>Production::getAmountProduced and the completion count at Production::step.</summary>
public static class ProductionProgress
{
    public static int Produced(int time, int spent, int amount) => time > 0 ? spent / time : amount;

    /// <summary>Production::step fallback staffing, including queued-only space.</summary>
    public static long FallbackAdditional(int spent, int assigned, int space, long freeEngineers, long freeWorkshops)
    {
        if (spent < 0 || assigned < 0 || space < 0)
            throw new InvalidDataException("Fallback production values cannot be negative.");
        if (spent == 0 && assigned == 0) freeWorkshops -= space;
        return freeEngineers > 0 && freeWorkshops > 0 ? Math.Min(freeEngineers, freeWorkshops) : 0;
    }

    public static int Requested(int time, int spent, int assigned, int amount, bool infinite)
    {
        if (time < 0 || spent < 0 || assigned < 0 || amount < 0)
            throw new InvalidDataException("Production progress values cannot be negative.");
        var before = Produced(time, spent, amount);
        var after = Produced(time, checked(spent + assigned), amount);
        return Math.Max(0, (infinite ? after : Math.Min(after, amount)) - before);
    }
}
