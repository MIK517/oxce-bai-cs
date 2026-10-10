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

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void UnconsumedScopedMaterialsPermitNativeRandomVariants(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, ScopedMaterialRules(mode));
        var rows = TestFixtures.Rows(oracle.RootElement, "productionScopedMaterialRows").Where(row => row[0].GetInt32() == mode).ToArray();
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = ScopedMaterialSnapshot(content, mode) with { RandomState = seed };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var choice = after.Bases[0].Items.GetValueOrDefault("INERT_B") == 2 ? 2 : 1;
            choices.Add(choice);
            var first = Assert.Single(result.Events.OfType<CampaignProductionProgress>(), progress => progress.RuleId == "VARIANT_PRODUCT");
            var other = result.Events.OfType<CampaignProductionProgress>().SingleOrDefault(progress => progress.RuleId == "OTHER_MATERIAL");
            var craft = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "VARIANT_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                mode, choice, first.Produced, NativeProductionStop(first.StopReason), other?.Produced ?? 0, mode == 0 ? -1 : NativeProductionStop(other?.StopReason),
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.Bases[0].Items.GetValueOrDefault("INERT_A"), after.Bases[0].Items.GetValueOrDefault("INERT_B"),
                mode == 1 ? Assert.Single(after.Bases[0].Productions, project => project.RuleId == "OTHER_MATERIAL").Assigned : 1,
                craft.Status, craft.Fuel, after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(Assert.Single(rows, row => row[1].GetInt32() == choice))), CanonicalJson.Normalize(actual));
            Assert.Equal(mode != 0, after.Bases[0].Productions.Any(project => project.RuleId == "VARIANT_PRODUCT"));
            Assert.Equal(mode == 1, after.Bases[0].Productions.Any(project => project.RuleId == "OTHER_MATERIAL"));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "scoped-random-material.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public void ContinuingScopedMaterialsRetainFallbackAwareGuard(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionScopedMaterialRows").Where(row => row[0].GetInt32() == mode).ToArray();
        Assert.Equal(2, rows.Select(row => row[5].GetInt32()).Distinct().Count());
        var content = Load(cache, ScopedMaterialRules(mode));
        var campaign = Reload(content, CampaignState.Restore(ScopedMaterialSnapshot(content, mode), content, new SplitMix64RandomSource(81)), 81, "scoped-material-guard.sav");
        AssertTimeBlocked(campaign, "UFO retargeting requires production fuel-stock forecasts.");
    }

    private static int NativeProductionStop(string? reason) => reason switch
    { "STR_PRODUCTION_COMPLETE" => 1, "STR_NOT_ENOUGH_MATERIALS" => 3, null => 0, _ => throw new InvalidOperationException(reason) };

    private static string ScopedMaterialRules(int mode)
    {
        var overlay = ScopedVariantRules();
        if (mode == 0) return overlay.Replace("requiredItems: {SUPPLY: 2}", "requiredItems: {INERT_A: 1}", StringComparison.Ordinal);
        return overlay.Replace("manufacture: [", "manufacture: [{name: OTHER_MATERIAL, time: 2, requiredItems: {INERT_A: 1}, producedItems: {SUPPLY: 0}}, ", StringComparison.Ordinal);
    }

    private static CampaignSnapshot ScopedMaterialSnapshot(RuntimeContent content, int mode)
    {
        var initial = VariantProductionSnapshot(content, false);
        var owner = initial.Bases[0];
        if (mode == 0) return initial with { Bases = [owner with { Productions = [owner.Productions[0] with { Amount = 1, Infinite = false }] }] };
        return initial with
        {
            Bases = [owner with
            {
                Engineers = mode == 4 ? 1 : 0,
                Productions = [.. owner.Productions, new("OTHER_MATERIAL", mode is 1 or 4 ? 0 : 1, 1, mode == 2 ? 1 : 3, false, false, mode == 4, new Dictionary<string, int>())],
            }],
        };
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
