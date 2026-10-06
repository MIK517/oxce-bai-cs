using System.Text;
using BenchmarkDotNet.Attributes;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using static Oxce.Benchmarks.PublicCampaignFixtures;

namespace Oxce.Benchmarks;

/// <summary>
/// A purchase quote prices every catalog item, so scripted <c>buyCostItem</c> prices run once per item.
/// Setup verifies that every generated item received its scripted price.
/// </summary>
[MemoryDiagnoser]
public class PriceScriptBenchmarks
{
    private const int Items = 400;
    private const string Code =
        "var int value; var int count; " +
        "loop var country geoscape_game.getCountries.list; country.getCurrentFunding value; add count 1; end; " +
        "item_rule.getWeight value; add cost_current count; add cost_current value; return cost_current;";

    private RuntimeContent _content = null!;
    private CampaignState _campaign = null!;
    private int _baseId;

    /// <summary>A global event script shared by every item, or one item script per item.</summary>
    [Params(false, true)]
    public bool PerItemScripts { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rules = new StringBuilder("items:\n");
        for (var index = 0; index < Items; index++)
        {
            rules.Append("  - type: BENCH_PRICE_").Append(index).Append("\n    costBuy: 10\n    weight: 3\n");
            if (PerItemScripts) rules.Append("    scripts:\n      buyCostItem: |\n        ").Append(Code).Append('\n');
        }
        if (!PerItemScripts)
            rules.Append("extended:\n  scripts:\n    buyCostItem:\n      - new: benchmark-price\n        offset: 1\n        code: |\n          ")
                .Append(Code).Append('\n');
        _content = LoadContent(["strategic-world.rul"], rules.ToString());
        var snapshot = PlacedCampaign(_content, new CampaignTime(1, 1, 1, 1999, 1, 0, 0));
        _baseId = snapshot.Bases[0].Id;
        _campaign = CampaignState.Restore(snapshot, _content, new SplitMix64RandomSource(1));
        var quote = Quote();
        var priced = quote.Rows.Where(row => row.RuleId.StartsWith("BENCH_PRICE_", StringComparison.Ordinal)).ToArray();
        if (priced.Length != Items || priced.Any(row => row.UnavailableReason is not null || row.UnitCost <= 10))
            throw new InvalidOperationException("Scripted purchase prices did not apply to every generated item: " +
                string.Join("; ", priced.Select(row => row.UnavailableReason).Distinct().Take(3)));
    }

    [Benchmark]
    public LogisticsQuote Quote()
    {
        var result = _campaign.Execute(new PrepareLogisticsQuote(_baseId, LogisticsOperation.Purchase));
        return result.Events[0] is LogisticsQuoted quoted ? quoted.Quote
            : throw new InvalidOperationException("The purchase quote was refused.");
    }
}
