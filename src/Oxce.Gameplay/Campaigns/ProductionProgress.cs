namespace Oxce.Gameplay.Campaigns;

/// <summary>Production::getAmountProduced and the completion count at Production::step.</summary>
public static class ProductionProgress
{
    public static int Produced(int time, int spent, int amount) => time > 0 ? spent / time : amount;

    public static int Requested(int time, int spent, int assigned, int amount, bool infinite)
    {
        if (time < 0 || spent < 0 || assigned < 0 || amount < 0)
            throw new InvalidDataException("Production progress values cannot be negative.");
        var before = Produced(time, spent, amount);
        var after = Produced(time, checked(spent + assigned), amount);
        return Math.Max(0, (infinite ? after : Math.Min(after, amount)) - before);
    }
}
