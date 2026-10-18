namespace Oxce.Gameplay.Campaigns;

/// <summary>Score and eligible funds supplied to the ordered monthly world-script phases.</summary>
internal readonly record struct WorldSchedulingInputs(int Score, long Funds);

/// <summary>SavedGame::getCurrentScore and GeoscapeState::determineAlienMissions arithmetic.</summary>
internal static class WorldMonthlySchedulingMath
{
    internal static WorldSchedulingInputs Calculate(int month, int researchScore,
        IReadOnlyList<int> xcomActivity, IReadOnlyList<int> alienActivity, long currentFunds,
        int countryFunding, int baseMaintenance, double performanceBonusFactor, bool isNewMonth)
    {
        if (month < 0 || xcomActivity.Count != alienActivity.Count)
            throw new InvalidDataException("Monthly world score history is inconsistent.");
        if (!double.IsFinite(performanceBonusFactor))
            throw new InvalidDataException("Performance bonus factor must be finite.");
        var score = checked(researchScore + (month > 1 ? 400 : 0));
        for (var index = 0; index < xcomActivity.Count; index++)
            score = checked(score + checked(xcomActivity[index] - alienActivity[index]));
        // An out-of-range bonus converts as x86 does and pays nothing (ADR 0036).
        var performanceBonus = Math.Max(0, MonthlyCycle.PerformanceBonus(score, performanceBonusFactor));
        var funds = isNewMonth
            ? checked(currentFunds + countryFunding + performanceBonus - baseMaintenance)
            : currentFunds;
        return new(score, funds);
    }
}

public sealed partial class CampaignState
{
    /// <summary>Called only after month advancement for monthly scheduling, or at the current month for adhoc events.</summary>
    internal WorldSchedulingInputs WorldSchedulingInputs(bool isNewMonth, int? schedulingMonth = null)
    {
        var index = _funds.Count - 1;
        if (index >= _researchScores.Count || _regions.Any(region =>
                index >= region.ActivityXcom.Count || index >= region.ActivityAlien.Count))
            throw new InvalidDataException("Monthly world score history is inconsistent.");
        var xcom = _regions.Select(region => region.ActivityXcom[index]).ToArray();
        var alien = _regions.Select(region => region.ActivityAlien[index]).ToArray();
        if (isNewMonth)
            foreach (var owner in _bases)
                foreach (var personal in owner.Soldiers.Select(soldier => soldier.Personal)
                             .Concat(owner.Transfers.Where(transfer => !transfer.Delivered && transfer.Soldier is not null)
                                 .Select(transfer => transfer.Soldier!.Personal)).OfType<SoldierPersonalState>())
                    if (!_content.RuntimeRules.Armors.TryGet(personal.Armor, out _))
                        throw new InvalidDataException("Monthly world accounting requires a known soldier armor rule.");
        var countryFunding = isNewMonth
            ? checked((int)_countries.Sum(country => (long)country.Funding[^1])) : 0;
        var baseMaintenance = isNewMonth
            ? checked((int)_bases.Sum(owner => CalculateMaintenance(owner).Total)) : 0;
        return WorldMonthlySchedulingMath.Calculate(schedulingMonth ?? MonthsPassed, _researchScores[index], xcom, alien,
            _funds[index], countryFunding, baseMaintenance,
            _content.RuntimeRules.Campaign.PerformanceBonusFactor, isNewMonth);
    }
}
