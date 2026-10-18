using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 6 S4: <see cref="SoldierCommendations"/> replays every <c>strategic-commendations</c> scenario (the reference
/// SoldierDiary::manageCommendations with its totals compiled verbatim) after a month of service: every award, level
/// and noun, in the diary's order. The campaign's acknowledgement is pinned by StrategicMonthlyCycleTests.
/// </summary>
public sealed class StrategicCommendationOracleTests
{
    public static TheoryData<string> Scenarios()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-commendations");
        var data = new TheoryData<string>();
        foreach (var scenario in TestFixtures.Rows(oracle.RootElement, "scenarios"))
            data.Add(scenario.GetProperty("name").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void CommendationsMatchTheReference(string name)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-commendations");
        var expected = TestFixtures.Rows(oracle.RootElement, "scenarios").Single(s => s.GetProperty("name").GetString() == name);
        var input = expected.GetProperty("input");
        // Mod::_commendations and RuleCommendations::_criteria are std::maps: UTF-8 byte order, as the linker projects.
        var commendations = input.GetProperty("commendations").EnumerateArray()
            .Select(static c => (Id: c.GetProperty("type").GetString()!, Rule: new RuntimeCommendationRule([])
            {
                Criteria = [.. c.GetProperty("criteria").EnumerateArray()
                    .Select(static pair => KeyValuePair.Create(pair[0].GetString()!, (IReadOnlyList<int>)Ints(pair[1])))
                    .OrderBy(static pair => Encoding.UTF8.GetBytes(pair.Key), Utf8)],
                KillCriteria = [.. c.GetProperty("killCriteria").EnumerateArray().Select(static or =>
                    (IReadOnlyList<CommendationKillCriterion>)[.. or.EnumerateArray().Select(static block =>
                        new CommendationKillCriterion(block[0].GetInt32(), Strings(block[1])))])],
                MissionTypes = Strings(c.GetProperty("missionTypes")),
                MissionMarkers = Strings(c.GetProperty("missionMarkers")),
                Requires = Strings(c.GetProperty("requires")),
                Units = Strings(c.GetProperty("units")),
            }))
            .OrderBy(static c => Encoding.UTF8.GetBytes(c.Id), Utf8).ToArray();
        var items = input.GetProperty("items").EnumerateArray().ToDictionary(static i => i[0].GetString()!,
            static i => new CommendationItem(i[1].GetInt32(), i[2].GetInt32(), i[3].GetInt32()), StringComparer.Ordinal);
        var researched = Strings(input.GetProperty("researched")).ToHashSet(StringComparer.Ordinal);
        var statistics = input.GetProperty("statistics").EnumerateArray().Select(static m => new MissionStatisticSnapshot(m[0].GetInt32(),
            m[1].GetString()!, m[2].GetString()!, m[3].GetString()!, m[4].GetString()!, m[5].GetString()!, m[6].GetBoolean(), m[7].GetInt32(),
            m[8].GetInt32(), m[9].GetBoolean(), 0)).ToArray();
        var counters = input.GetProperty("counters").EnumerateArray().ToDictionary(static p => p[0].GetString()!, static p => p[1].GetInt32(),
            StringComparer.Ordinal);
        var diary = new SoldierDiaryState
        {
            Counters = new ReadOnlyDictionary<string, int>(counters.Where(static p => p.Key is not ("ufosShotDownTotal" or "ufosDamageTotal"))
                .ToDictionary(static p => p.Key, static p => p.Value, StringComparer.Ordinal)),
            Kills = [.. input.GetProperty("kills").EnumerateArray().Select(static k => new SoldierKill(k[0].GetString()!, k[1].GetString()!,
                k[2].GetString()!, k[3].GetString()!, k[4].GetInt32(), k[5].GetInt32(), k[6].GetInt32(), k[7].GetInt32(), k[8].GetInt32(), k[9].GetInt32()))],
            MissionIds = Ints(input.GetProperty("missions")),
        };
        var owned = input.GetProperty("owned").EnumerateArray()
            .Select(static o => new SoldierCommendation(o[0].GetString()!, o[1].GetString()!, o[2].GetInt32())).ToList();
        // addMonthlyService, then manageCommendations.
        diary = diary.WithCounter("monthsService", diary.Counter("monthsService") + 1);
        var personal = new SoldierPersonalState("Soldier", "", 0, 0, 0, 0, "", new Dictionary<string, short>(), new Dictionary<string, short>())
        {
            Diary = diary,
            Commendations = owned.AsReadOnly(),
            UfosShotDownTotal = counters.GetValueOrDefault("ufosShotDownTotal"),
            UfosDamageTotal = counters.GetValueOrDefault("ufosDamageTotal"),
        };

        var awarded = SoldierCommendations.Manage(input.GetProperty("soldierType").GetString()!, personal, owned,
            new CommendationInputs(commendations, research => research.All(researched.Contains), statistics,
                id => items.TryGetValue(id, out var item) ? item : null, input.GetProperty("maxDarkness").GetInt32()));

        Assert.Equal(expected.GetProperty("awarded").GetBoolean(), awarded);
        Assert.Equal(expected.GetProperty("monthsService").GetInt32(), diary.Counter("monthsService"));
        Assert.Equal(expected.GetProperty("commendations").EnumerateArray()
            .Select(static c => new SoldierCommendation(c[0].GetString()!, c[1].GetString()!, c[2].GetInt32())), owned);
    }

    private static readonly Comparer<byte[]> Utf8 = Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right));

    private static string[] Strings(JsonElement element) => [.. element.EnumerateArray().Select(static v => v.GetString()!)];

    private static int[] Ints(JsonElement element) => [.. element.EnumerateArray().Select(static v => v.GetInt32())];
}
