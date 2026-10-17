using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 6 S3: <see cref="MonthlyCycle"/> replays every <c>strategic-monthly-report</c> scenario (the reference
/// time1Month, monthlyFunding and MonthlyReportState compiled verbatim): the histories, every country's funding,
/// activity, pact and satisfaction, the rating, funding change, bonus, debt warning, game over, the pact events and
/// every random call. The order around them (scheduling, psi, base detection) is pinned by StrategicMonthlyCycleTests.
/// </summary>
public sealed class StrategicMonthlyReportOracleTests
{
    public static TheoryData<string> Scenarios()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-monthly-report");
        var data = new TheoryData<string>();
        foreach (var scenario in TestFixtures.Rows(oracle.RootElement, "scenarios"))
            data.Add(scenario.GetProperty("name").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void MonthRolloverMatchesTheReference(string name)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-monthly-report");
        var expected = TestFixtures.Rows(oracle.RootElement, "scenarios").Single(s => s.GetProperty("name").GetString() == name);
        var input = expected.GetProperty("input");
        var ledger = new MonthlyLedger(Longs(input, "funds"), Longs(input, "maintenance"), Longs(input, "incomes"),
            Longs(input, "expenditures"), Ints(input, "researchScores"));
        var regions = input.GetProperty("regions").EnumerateArray()
            .Select(region => new MonthlyRegion(Ints(region, "xcom"), Ints(region, "alien"))).ToArray();
        var scripts = new Dictionary<string, CountryMonthOutputs>(StringComparer.Ordinal);
        var events = new Dictionary<string, (string Signed, string Rejoined)>(StringComparer.Ordinal);
        var countries = input.GetProperty("countries").EnumerateArray().Select(country =>
        {
            var type = country.GetProperty("type").GetString()!;
            if (country.GetProperty("script") is { ValueKind: JsonValueKind.Array } script)
            {
                var values = script.EnumerateArray().Select(static v => v.GetInt32()).ToArray();
                scripts[type] = new(values[0], values[1], values[2] != 0, values[3] != 0);
            }
            events[type] = (country.GetProperty("signedPactEvent").GetString()!, country.GetProperty("rejoinedXcomEvent").GetString()!);
            return new MonthlyCountry(type, country.GetProperty("fundingCap").GetInt32(), Ints(country, "funding"), Ints(country, "xcom"),
                Ints(country, "alien"), country.GetProperty("pact").GetBoolean(), country.GetProperty("newPact").GetBoolean(),
                country.GetProperty("cancelPact").GetBoolean());
        }).ToArray();
        // Mod::_alienMissions is a std::map: missions in name order.
        var infiltrations = input.GetProperty("infiltrations").EnumerateArray()
            .OrderBy(static mission => mission.GetProperty("type").GetString(), StringComparer.Ordinal)
            .Select(static mission => new MonthlyInfiltration(mission.GetProperty("points").GetInt32(),
                mission.GetProperty("weights").EnumerateArray().ToDictionary(static pair => pair[0].GetUInt64(), static pair => pair[1].GetInt32())))
            .ToArray();
        var difficulty = input.GetProperty("difficulty").GetInt32();
        var rules = new MonthlyReportRules(input.GetProperty("defeatScore").GetInt32(), input.GetProperty("defeatFunds").GetInt32(),
            Ints(input, "monthlyRatingThresholds"),
            [.. input.GetProperty("monthlyRatings").EnumerateArray().Select(static pair => new CampaignMonthlyRating(pair[0].GetInt32(), pair[1].GetString()!))],
            input.GetProperty("performanceBonusFactor").GetDouble(), CampaignInterceptionSettings.Default.DifficultyCoefficient(difficulty), difficulty);
        var random = new ScriptedRandom([.. input.GetProperty("rng").EnumerateArray().Select(static v => v.GetUInt64())]);
        var warned = input.GetProperty("warned").GetBoolean();
        // time1Month: addMonth, then funding with the countries' funding before their decisions.
        var monthsPassed = input.GetProperty("monthsPassed").GetInt32() + 1;
        MonthlyCycle.MonthlyFunding(ledger, MonthlyCycle.CountryFunding(countries), Ints(input, "baseMaintenance").Sum());

        var evaluation = MonthlyCycle.Evaluate(ledger, regions, countries, infiltrations, monthsPassed, rules, random, ref warned,
            (country, _, _, current) => scripts.GetValueOrDefault(country.Type, current));

        Assert.Equal(Longs(expected, "funds"), ledger.Funds);
        Assert.Equal(Longs(expected, "maintenance"), ledger.Maintenance);
        Assert.Equal(Longs(expected, "incomes"), ledger.Incomes);
        Assert.Equal(Longs(expected, "expenditures"), ledger.Expenditures);
        Assert.Equal(Ints(expected, "researchScores"), ledger.ResearchScores);
        var expectedRegions = expected.GetProperty("regions").EnumerateArray().ToArray();
        for (var index = 0; index < regions.Length; index++)
        {
            Assert.Equal(Ints(expectedRegions[index], "xcom"), regions[index].ActivityXcom);
            Assert.Equal(Ints(expectedRegions[index], "alien"), regions[index].ActivityAlien);
        }
        var expectedCountries = expected.GetProperty("countries").EnumerateArray().ToArray();
        for (var index = 0; index < countries.Length; index++)
        {
            var row = expectedCountries[index];
            Assert.Equal(Ints(row, "funding"), countries[index].Funding);
            Assert.Equal(Ints(row, "xcom"), countries[index].ActivityXcom);
            Assert.Equal(Ints(row, "alien"), countries[index].ActivityAlien);
            Assert.Equal((row.GetProperty("pact").GetBoolean(), row.GetProperty("newPact").GetBoolean(), row.GetProperty("cancelPact").GetBoolean(),
                row.GetProperty("satisfaction").GetInt32()), (countries[index].Pact, countries[index].NewPact, countries[index].CancelPact,
                (int)countries[index].ReportedSatisfaction));
        }
        Assert.Equal((expected.GetProperty("ratingTotal").GetInt32(), expected.GetProperty("lastMonthsRating").GetInt32(),
            expected.GetProperty("fundingDiff").GetInt32(), expected.GetProperty("gameOver").GetInt32(), expected.GetProperty("warned").GetBoolean()),
            (evaluation.RatingTotal, evaluation.LastMonthsRating, evaluation.FundingDiff, evaluation.GameOver, warned));
        // STR_MONTHLY_RATING{total}{rating}
        Assert.Equal($"STR_MONTHLY_RATING{{{evaluation.RatingTotal}}}{{{evaluation.RatingText}}}", expected.GetProperty("rating").GetString());
        var bonusText = expected.GetProperty("bonus").GetString()!;
        Assert.Equal(bonusText.Length == 0 ? 0 : int.Parse(bonusText[(bonusText.IndexOf('$') + 1)..], System.Globalization.CultureInfo.InvariantCulture),
            evaluation.PerformanceBonus > 0 ? evaluation.PerformanceBonus : 0);
        Assert.Equal(expected.GetProperty("description").GetString(), Description(evaluation));
        // The pact events are spawned for the signed and cancelled pacts, in list order.
        var spawned = evaluation.Pacts.Select(type => events[type].Signed).Concat(evaluation.CancelledPacts.Select(type => events[type].Rejoined))
            .Where(static id => id.Length != 0);
        Assert.Equal(expected.GetProperty("spawnedEvents").EnumerateArray().Select(static v => v.GetString()), spawned);
        // A negative bound draws over the whole int range (ADR 0036); the reference's truncation of the raw value
        // is what the scripted source returns for it.
        var expectedCalls = expected.GetProperty("rngCalls").EnumerateArray()
            .Select(static call => call.EnumerateArray().Select(static v => v.GetInt32()).ToArray())
            .Select(static call => call[1] < call[0] ? new[] { int.MinValue, int.MaxValue, call[2] } : call)
            // The monthly alien-base roll is the tick's last draw and not part of the report.
            .Take(random.Calls.Count).ToArray();
        Assert.Equal(JsonSerializer.Serialize(expectedCalls), JsonSerializer.Serialize(random.Calls));
        Assert.Equal(input.GetProperty("alienBasesDiscovered").GetArrayLength() > 0 ? 1 : 0,
            expected.GetProperty("rngCalls").GetArrayLength() - random.Calls.Count);
    }

    /// <summary>The report text the probe records: the council text, then the debt warning and the country lists.</summary>
    private static string Description(MonthlyEvaluation evaluation)
    {
        var text = evaluation.CouncilText + (evaluation.DebtWarning ? "\n\nSTR_COUNCIL_REDUCE_DEBTS" : "");
        text += List(evaluation.Happy, "STR_COUNTRY_IS_PARTICULARLY_PLEASED", "STR_COUNTRIES_ARE_PARTICULARLY_HAPPY");
        text += List(evaluation.Sad, "STR_COUNTRY_IS_UNHAPPY_WITH_YOUR_ABILITY", "STR_COUNTRIES_ARE_UNHAPPY_WITH_YOUR_ABILITY");
        text += List(evaluation.Pacts, "STR_COUNTRY_HAS_SIGNED_A_SECRET_PACT", "STR_COUNTRIES_HAVE_SIGNED_A_SECRET_PACT");
        text += List(evaluation.CancelledPacts, "STR_COUNTRY_HAS_CANCELLED_A_SECRET_PACT", "STR_COUNTRIES_HAVE_CANCELLED_A_SECRET_PACT");
        return text;

        static string List(IReadOnlyList<string> countries, string singular, string plural)
        {
            if (countries.Count == 0) return "";
            if (countries.Count == 1) return $"\n\n{singular}{{{countries[0]}}}";
            var list = countries[0];
            for (var index = 1; index < countries.Count - 1; index++) list = $"STR_COUNTRIES_COMMA{{{list}}}{{{countries[index]}}}";
            return $"\n\n{plural}{{STR_COUNTRIES_AND{{{list}}}{{{countries[^1]}}}}}";
        }
    }

    private static List<long> Longs(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(static v => v.GetInt64())];

    private static List<int> Ints(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(static v => v.GetInt32())];

    // RandomState::generate over the scripted raw values; zero once the script is exhausted. A whole-int-range
    // request stands for the reference's negative-bound draw, which truncates the raw value.
    private sealed class ScriptedRandom(ulong[] script) : IRandomSource
    {
        private int _next;
        public List<int[]> Calls { get; } = [];

        public int NextInclusive(int minimum, int maximum)
        {
            var raw = _next < script.Length ? script[_next] : 0;
            _next++;
            var result = minimum == int.MinValue && maximum == int.MaxValue ? unchecked((int)raw)
                : unchecked((int)(raw % (ulong)(long)(maximum - minimum + 1) + (ulong)(long)minimum));
            Calls.Add([minimum, maximum, result]);
            return result;
        }

        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException("The month draws inclusive ranges only.");
        public double NextUnit() => throw new InvalidOperationException("The month draws inclusive ranges only.");
    }
}
