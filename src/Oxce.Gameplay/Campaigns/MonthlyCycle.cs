using Oxce.Core.Random;
using Oxce.Mods.Rulesets.CampaignStart;

namespace Oxce.Gameplay.Campaigns;

/// <summary>The campaign histories the month rollover writes; the lists are the campaign's own.</summary>
internal sealed class MonthlyLedger(List<long> funds, List<long> maintenance, List<long> incomes, List<long> expenditures,
    List<int> researchScores)
{
    public List<long> Funds { get; } = funds;
    public List<long> Maintenance { get; } = maintenance;
    public List<long> Incomes { get; } = incomes;
    public List<long> Expenditures { get; } = expenditures;
    public List<int> ResearchScores { get; } = researchScores;

    /// <summary>SavedGame::setFunds: a rise counts as income, a fall as expenditure of the current month.</summary>
    public void SetFunds(long funds)
    {
        if (Funds[^1] > funds) Expenditures[^1] = checked(Expenditures[^1] + (Funds[^1] - funds));
        else Incomes[^1] = checked(Incomes[^1] + (funds - Funds[^1]));
        Funds[^1] = funds;
    }
}

/// <summary>A region's activity histories (the campaign's own lists).</summary>
internal sealed record MonthlyRegion(List<int> ActivityXcom, List<int> ActivityAlien);

/// <summary>A country as Country::newMonth sees it; the lists are the campaign's own, the flags are written back.</summary>
internal sealed class MonthlyCountry(string type, int fundingCap, List<int> funding, List<int> activityXcom, List<int> activityAlien,
    bool pact, bool newPact, bool cancelPact)
{
    public string Type { get; } = type;
    public int FundingCap { get; } = fundingCap;
    public List<int> Funding { get; } = funding;
    public List<int> ActivityXcom { get; } = activityXcom;
    public List<int> ActivityAlien { get; } = activityAlien;
    public bool Pact { get; set; } = pact;
    public bool NewPact { get; set; } = newPact;
    public bool CancelPact { get; set; } = cancelPact;
    /// <summary>Country::_satisfaction; not saved, so it is only this month's.</summary>
    public MonthlySatisfaction Satisfaction { get; set; } = MonthlySatisfaction.Satisfied;
    /// <summary>Country::getSatisfaction: a pact overrides the stored value.</summary>
    public MonthlySatisfaction ReportedSatisfaction => Pact ? MonthlySatisfaction.AlienPact : Satisfaction;
}

/// <summary>Country::Satisfaction, with the reference's script values.</summary>
public enum MonthlySatisfaction
{
    AlienPact = 0,
    Unhappy = 1,
    Satisfied = 2,
    Happy = 3,
}

/// <summary>The four outputs of <c>newMonthCountry</c>: funding change, satisfaction, new pact, cancelled pact.</summary>
internal record struct CountryMonthOutputs(int FundingChange, int Satisfaction, bool NewPact, bool CancelPact);

/// <summary>An alien mission that can pick the pact score: its points and month-keyed weights (std::map order).</summary>
internal sealed record MonthlyInfiltration(int Points, IReadOnlyDictionary<ulong, int> Weights);

/// <summary>The global rules the report reads.</summary>
internal sealed record MonthlyReportRules(int DefeatScore, int DefeatFunds, IReadOnlyList<int> RatingThresholds,
    IReadOnlyList<CampaignMonthlyRating> Ratings, double PerformanceBonusFactor, int DifficultyCoefficient, int Difficulty);

/// <summary>What <c>MonthlyReportState</c> decides and shows; the texts are string IDs.</summary>
internal sealed record MonthlyEvaluation(int RatingTotal, int LastMonthsRating, int FundingDiff, string RatingText,
    int PerformanceBonus, string CouncilText, bool DebtWarning, int GameOver, IReadOnlyList<string> Happy, IReadOnlyList<string> Sad,
    IReadOnlyList<string> Pacts, IReadOnlyList<string> CancelledPacts);

/// <summary>
/// The month rollover's arithmetic: <c>SavedGame::monthlyFunding</c>, <c>MonthlyReportState::calculateChanges</c>,
/// <c>Region::newMonth</c>, <c>Country::newMonth</c>, <c>Mod::getRandomMission</c> and the report constructor's
/// rating, performance bonus, debt warning and game-over decisions. Pinned by the <c>strategic-monthly-report</c>
/// oracle; integer overflow the reference leaves undefined fails with <see cref="OverflowException"/>.
/// </summary>
internal static class MonthlyCycle
{
    public const int HistoryLength = 12;

    /// <summary>SavedGame::monthlyFunding with the funding and maintenance it reads.</summary>
    public static void MonthlyFunding(MonthlyLedger ledger, int countryFunding, int baseMaintenance)
    {
        ledger.Funds[^1] = checked(ledger.Funds[^1] + checked(countryFunding - baseMaintenance));
        ledger.Funds.Add(ledger.Funds[^1]);
        ledger.Maintenance[^1] = baseMaintenance;
        ledger.Maintenance.Add(0);
        ledger.Incomes.Add(countryFunding);
        ledger.Expenditures.Add(baseMaintenance);
        ledger.ResearchScores.Add(0);
        Trim(ledger.Incomes);
        Trim(ledger.Expenditures);
        Trim(ledger.ResearchScores);
        Trim(ledger.Funds);
        Trim(ledger.Maintenance);
    }

    /// <summary>
    /// The monthly report's work, run when the report is created: country decisions, pacts, funding, the rating, the
    /// performance bonus (credited to funds), the debt warning and game over. <paramref name="warned"/> is
    /// SavedGame::_warned. <paramref name="script"/> is the country's newMonthCountry call.
    /// </summary>
    public static MonthlyEvaluation Evaluate(MonthlyLedger ledger, IReadOnlyList<MonthlyRegion> regions,
        IReadOnlyList<MonthlyCountry> countries, IReadOnlyList<MonthlyInfiltration> infiltrations, int monthsPassed,
        MonthlyReportRules rules, IRandomSource random, ref bool warned,
        Func<MonthlyCountry, int, int, CountryMonthOutputs, CountryMonthOutputs> script)
    {
        var lastMonthsRating = 0;
        var xcomSubTotal = 0;
        var alienTotal = 0;
        var monthOffset = ledger.Funds.Count - 2;
        var lastMonthOffset = ledger.Funds.Count - 3;
        if (lastMonthOffset < 0) lastMonthOffset += 2;
        foreach (var region in regions)
        {
            region.ActivityXcom.Add(0);
            region.ActivityAlien.Add(0);
            Trim(region.ActivityAlien);
            Trim(region.ActivityXcom);
            if (region.ActivityXcom.Count > 2)
                lastMonthsRating = checked(lastMonthsRating + checked(At(region.ActivityXcom, lastMonthOffset) - At(region.ActivityAlien, lastMonthOffset)));
            xcomSubTotal = checked(xcomSubTotal + At(region.ActivityXcom, monthOffset));
            alienTotal = checked(alienTotal + At(region.ActivityAlien, monthOffset));
        }
        // The council is more lenient after the first month.
        if (monthsPassed > 1) ledger.ResearchScores[monthOffset] = checked(At(ledger.ResearchScores, monthOffset) + 400);
        var xcomTotal = checked(At(ledger.ResearchScores, monthOffset) + xcomSubTotal);
        if (ledger.ResearchScores.Count > 2) lastMonthsRating = checked(lastMonthsRating + At(ledger.ResearchScores, lastMonthOffset));

        var pactScore = RandomInfiltration(infiltrations, monthsPassed, random)?.Points ?? 0;
        var countryFunding = CountryFunding(countries);
        // countryFunding / countries.size() / 1000 * 1000 divides as size_t and converts back to int. No country would
        // divide by zero; nothing reads the average then (ADR 0036).
        var averageFunding = countries.Count == 0 ? 0
            : unchecked((int)((ulong)(long)countryFunding / (ulong)countries.Count / 1000 * 1000));
        var fundingDiff = 0;
        List<string> happy = [], sad = [], pacts = [], cancelled = [];
        foreach (var country in countries)
        {
            var wasInPact = country.Pact;
            NewMonth(country, xcomTotal, alienTotal, pactScore, averageFunding, random, script);
            fundingDiff = checked(fundingDiff + checked(country.Funding[^1] - country.Funding[^2]));
            if (!wasInPact && country.Pact) pacts.Add(country.Type);
            else if (wasInPact && !country.Pact) cancelled.Add(country.Type);
            switch (country.ReportedSatisfaction)
            {
                case MonthlySatisfaction.Unhappy: sad.Add(country.Type); break;
                case MonthlySatisfaction.Happy: happy.Add(country.Type); break;
            }
        }
        var ratingTotal = checked(xcomTotal - alienTotal);

        var threshold = checked(rules.DefeatScore + checked(100 * rules.DifficultyCoefficient));
        // Only negative custom thresholds are allowed.
        if (rules.RatingThresholds.Count > rules.Difficulty && rules.RatingThresholds[rules.Difficulty] < 0)
            threshold = rules.RatingThresholds[rules.Difficulty];
        var rating = "STR_RATING_TERRIBLE";
        if (ratingTotal > threshold - 300L) rating = "STR_RATING_POOR";
        if (ratingTotal > threshold) rating = "STR_RATING_OK";
        if (ratingTotal > 0) rating = "STR_RATING_GOOD";
        if (ratingTotal > 500) rating = "STR_RATING_EXCELLENT";
        if (rules.Ratings.Count != 0)
        {
            rating = "";
            var best = int.MinValue;
            foreach (var entry in rules.Ratings)
                if (entry.Score > best && entry.Score <= ratingTotal)
                {
                    best = entry.Score;
                    rating = entry.Text;
                }
        }

        var bonus = PerformanceBonus(ratingTotal, rules.PerformanceBonusFactor);
        if (bonus > 0) ledger.SetFunds(checked(ledger.Funds[^1] + bonus));

        var council = "STR_COUNCIL_IS_DISSATISFIED";
        var resetWarning = true;
        var debtWarning = false;
        var gameOver = 0;
        if (ratingTotal > threshold) council = "STR_COUNCIL_IS_GENERALLY_SATISFIED";
        if (ratingTotal > 500) council = "STR_COUNCIL_IS_VERY_PLEASED";
        if (lastMonthsRating <= threshold && ratingTotal <= threshold)
        {
            council = "STR_YOU_HAVE_NOT_SUCCEEDED";
            gameOver = 1;
        }
        if (gameOver == 0 && ledger.Funds[^1] <= rules.DefeatFunds)
        {
            if (warned)
            {
                council = "STR_YOU_HAVE_NOT_SUCCEEDED";
                gameOver = 2;
            }
            else
            {
                debtWarning = true;
                warned = true;
                resetWarning = false;
            }
        }
        if (resetWarning && warned) warned = false;
        if (gameOver != 0)
        {
            happy.Clear();
            sad.Clear();
            pacts.Clear();
            cancelled.Clear();
        }
        return new(ratingTotal, lastMonthsRating, fundingDiff, rating, bonus, council, debtWarning, gameOver,
            happy.AsReadOnly(), sad.AsReadOnly(), pacts.AsReadOnly(), cancelled.AsReadOnly());
    }

    /// <summary>SavedGame::getCountryFunding.</summary>
    public static int CountryFunding(IReadOnlyList<MonthlyCountry> countries)
    {
        var total = 0;
        foreach (var country in countries) total = checked(total + country.Funding[^1]);
        return total;
    }

    /// <summary>Mod::getPerformanceBonus: <c>(int)(score * factor)</c>.</summary>
    public static int PerformanceBonus(int score, double factor)
    {
        var product = Math.Truncate(score * factor);
        if (!double.IsFinite(product) || product < int.MinValue || product > int.MaxValue)
            throw new OverflowException("The monthly performance bonus exceeds the supported range.");
        return (int)product;
    }

    /// <summary>Mod::getRandomMission(OBJECTIVE_INFILTRATION, monthsPassed) over missions in std::map name order.</summary>
    public static MonthlyInfiltration? RandomInfiltration(IReadOnlyList<MonthlyInfiltration> missions, int monthsPassed,
        IRandomSource random)
    {
        var total = 0;
        List<(int Weight, MonthlyInfiltration Mission)> possibilities = [];
        foreach (var mission in missions)
        {
            var weight = Weight(mission.Weights, monthsPassed);
            if (weight <= 0) continue;
            total = checked(total + weight);
            possibilities.Add((total, mission));
        }
        if (total <= 0) return null;
        var pick = random.NextInclusive(1, total);
        foreach (var possibility in possibilities)
            if (pick <= possibility.Weight) return possibility.Mission;
        return null;
    }

    /// <summary>RuleAlienMission::getWeight: 1 without weights, else the last weight whose month has started.</summary>
    public static int Weight(IReadOnlyDictionary<ulong, int> weights, int monthsPassed)
    {
        if (weights.Count == 0) return 1;
        var months = (ulong)(long)monthsPassed;
        var weight = 0;
        foreach (var pair in weights.OrderBy(static pair => pair.Key))
        {
            if (pair.Key > months) break;
            weight = pair.Value;
        }
        return weight;
    }

    /// <summary>
    /// RNG::generate(min, max). The reference's unsigned modulus makes a negative bound draw any int, and a bound of
    /// -1 divide by zero; the port draws over the whole int range for both (ADR 0036).
    /// </summary>
    public static int Generate(IRandomSource random, int minimum, int maximum) =>
        maximum < minimum ? random.NextInclusive(int.MinValue, int.MaxValue) : random.NextInclusive(minimum, maximum);

    private static void NewMonth(MonthlyCountry country, int xcomTotal, int alienTotal, int pactScore, int averageFunding,
        IRandomSource random, Func<MonthlyCountry, int, int, CountryMonthOutputs, CountryMonthOutputs> script)
    {
        country.Satisfaction = MonthlySatisfaction.Satisfied;
        var funding = country.Funding[^1];
        var good = checked(xcomTotal / 10 + country.ActivityXcom[^1]);
        var bad = checked(alienTotal / 20 + country.ActivityAlien[^1]);
        var oldFunding = funding / 1000;
        var newFunding = checked(checked(oldFunding * random.NextInclusive(5, 20)) / 100 * 1000);
        // Increase by at least 1000.
        if (newFunding == 0) newFunding = 1000;
        if (bad <= good + 30L)
        {
            if (good > bad + 30L && Generate(random, 0, good) > bad)
            {
                // Don't go over the cap.
                var cap = checked(country.FundingCap * 1000);
                if (checked(funding + newFunding) > cap) newFunding = checked(cap - funding);
                if (newFunding != 0) country.Satisfaction = MonthlySatisfaction.Happy;
            }
        }
        else if (Generate(random, 0, bad) > good && newFunding != 0)
        {
            newFunding = -newFunding;
            // Don't go below zero.
            if (checked(funding + newFunding) < 0) newFunding = checked(0 - funding);
            if (newFunding != 0) country.Satisfaction = MonthlySatisfaction.Unhappy;
        }
        if (country.Satisfaction == MonthlySatisfaction.Satisfied) newFunding = 0;
        if (country.CancelPact && oldFunding <= 0)
        {
            // Satisfied, not happy or unhappy.
            country.Satisfaction = MonthlySatisfaction.Satisfied;
            newFunding = averageFunding;
        }

        var outputs = script(country, xcomTotal, alienTotal,
            new CountryMonthOutputs(newFunding, (int)country.Satisfaction, country.NewPact, country.CancelPact));
        newFunding = outputs.FundingChange;
        country.Satisfaction = (MonthlySatisfaction)outputs.Satisfaction;
        country.NewPact = outputs.NewPact;
        country.CancelPact = outputs.CancelPact;

        if (country.NewPact)
        {
            country.Pact = true;
            country.ActivityAlien[^1] = checked(country.ActivityAlien[^1] + pactScore);
        }
        else if (country.CancelPact) country.Pact = false;
        country.NewPact = false;
        country.CancelPact = false;
        // Funding in a pact is zero, hardcoded.
        country.Funding.Add(country.Pact ? 0 : checked(funding + newFunding));
        country.ActivityAlien.Add(0);
        country.ActivityXcom.Add(0);
        Trim(country.ActivityAlien);
        Trim(country.ActivityXcom);
        Trim(country.Funding);
    }

    private static int At(List<int> values, int index) =>
        (uint)index < (uint)values.Count ? values[index]
            : throw new InvalidDataException("Monthly score history is shorter than the funds history.");

    private static void Trim<T>(List<T> values)
    {
        if (values.Count > HistoryLength) values.RemoveAt(0);
    }
}
