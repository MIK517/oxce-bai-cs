using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldHuntingTests
{
    [Theory]
    [InlineData(false, "unused")]
    [InlineData(true, "unused")]
    [InlineData(false, "other-base")]
    [InlineData(true, "other-base")]
    [InlineData(false, "future-transfer")]
    [InlineData(true, "future-transfer")]
    public void RandomOutputFuelSensitivityUsesOnlyThisHoursPossibleBaseCraft(bool cache, string context)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, ScopedVariantRules());
        var rows = TestFixtures.Rows(oracle.RootElement, "productionVariantRows").Where(row => !row[0].GetBoolean() && row[1].GetInt32() == 0 && row[2].GetInt32() == 0).ToArray();
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = ScopedVariantSnapshot(content, context) with { RandomState = seed };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var history = Assert.Single(after.Bases[0].Productions).RandomProductionInfo;
            var choice = history.GetValueOrDefault("INERT_B") == 2 ? 2 : 1;
            choices.Add(choice);
            AssertScopedVariantResult(after, result.Events, Assert.Single(rows, row => row[3].GetInt32() == choice));
            if (context == "other-base")
            {
                Assert.Equal(initial.Bases[1].Items["INERT_A"], after.Bases[1].Items["INERT_A"]);
                Assert.Equal("STR_READY", Assert.Single(after.Bases[1].Crafts).Logistics!.Status);
            }
            if (context == "future-transfer") Assert.Equal(1, Assert.Single(after.Bases[0].Transfers).Hours);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "scoped-random-production.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false, "due-transfer")]
    [InlineData(true, "due-transfer")]
    [InlineData(false, "produced")]
    [InlineData(true, "produced")]
    public void DueAndPotentialProducedCraftKeepRandomFuelSensitivity(bool cache, string context)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var overlay = ScopedVariantRules();
        if (context == "produced") overlay = overlay.Replace("manufacture: [", "manufacture: [{name: OTHER_FUEL_PRODUCT, category: STR_CRAFT, time: 1, producedItems: {OTHER_FUEL: 1}}, ", StringComparison.Ordinal);
        var content = Load(cache, overlay);
        var initial = ScopedVariantSnapshot(content, context);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "scoped-fuel-guard.sav");
        AssertTimeBlocked(campaign, "UFO retargeting requires production fuel-stock forecasts.");
    }

    private static string ScopedVariantRules() => VariantProductionRules(0, 0).Replace("crafts: [",
        "crafts: [{type: OTHER_FUEL, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: INERT_A}, ", StringComparison.Ordinal);

    private static CampaignSnapshot ScopedVariantSnapshot(RuntimeContent content, string context)
    {
        var initial = VariantProductionSnapshot(content, false);
        var owner = initial.Bases[0];
        var other = Assert.Single(owner.Crafts, craft => craft.RuleId == "VARIANT_HUNT") with
        {
            RuleId = "OTHER_FUEL",
            Id = 97,
            Logistics = Assert.Single(owner.Crafts, craft => craft.RuleId == "VARIANT_HUNT").Logistics! with
            { Status = "STR_READY", Fuel = 100, IsAutoPatrolling = false, Destination = null },
        };
        if (context == "other-base") return initial with
        {
            Bases = [owner, owner with
            {
                Id = owner.Id + 1, Name = "Other fuel base", Crafts = [other], Transfers = [], Research = [], Productions = [],
                Items = new Dictionary<string, int> { ["INERT_A"] = 7 },
            }],
        };
        if (context is "future-transfer" or "due-transfer") return initial with
        {
            Bases = [owner with { Transfers = [new(17, context == "future-transfer" ? 2 : 1, CampaignTransferKind.Craft, "OTHER_FUEL", 1, Craft: other)] }],
        };
        if (context == "produced") return initial with
        {
            Bases = [owner with { Productions = [.. owner.Productions, new("OTHER_FUEL_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())] }],
        };
        return initial;
    }

    private static void AssertScopedVariantResult(CampaignSnapshot after, IReadOnlyList<ICampaignEvent> events, JsonElement row)
    {
        var progress = Assert.Single(events.OfType<CampaignProductionProgress>());
        var history = Assert.Single(after.Bases[0].Productions).RandomProductionInfo;
        var craft = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "VARIANT_HUNT").Logistics!;
        var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
        {
            progress.Produced, progress.StopReason == "STR_NOT_ENOUGH_MATERIALS" ? 3 : 0,
            after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.Bases[0].Items.GetValueOrDefault("INERT_A"), after.Bases[0].Items.GetValueOrDefault("INERT_B"),
            after.Bases[0].Transfers.Count(transfer => transfer.Kind == CampaignTransferKind.Item), history.GetValueOrDefault("SUPPLY"), history.GetValueOrDefault("INERT_A"),
            history.GetValueOrDefault("INERT_B"), craft.Status, craft.Fuel, after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
        });
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(4).ToArray())), CanonicalJson.Normalize(actual));
    }
}
