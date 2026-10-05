namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private static void Account(long delta, ref long funds, ref long income, ref long spending)
    {
        funds = checked(funds + delta);
        if (delta > 0) income = checked(income + delta);
        else spending = checked(spending - delta);
    }

    private bool TryStageAccounting(long delta, out AccountingState accounting)
    {
        return TryStageAccounting(delta, new(_funds[^1], _incomes[^1], _expenditures[^1]), out accounting);
    }

    private static bool TryStageAccounting(long delta, AccountingState current, out AccountingState accounting)
    {
        var funds = current.Funds;
        var income = current.Income;
        var spending = current.Spending;
        try { Account(delta, ref funds, ref income, ref spending); }
        catch (OverflowException)
        {
            accounting = default;
            return false;
        }
        accounting = new(funds, income, spending);
        return true;
    }

    private void PublishAccounting(AccountingState accounting)
    {
        _funds[^1] = accounting.Funds;
        _incomes[^1] = accounting.Income;
        _expenditures[^1] = accounting.Spending;
    }

    private readonly record struct AccountingState(long Funds, long Income, long Spending);
}
