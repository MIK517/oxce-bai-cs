using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldHuntingTests
{
    [Theory]
    [InlineData("unused")]
    [InlineData("other-base")]
    [InlineData("future-transfer")]
    public void RandomOutputFuelSensitivityUsesOnlyThisHoursPossibleBaseCraft(string context)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(ScopedVariantRules());
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
            campaign = ReloadRepeatedly(content, campaign, seed, "scoped-random-production.sav", 3, after);
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData("due-transfer")]
    [InlineData("produced")]
    public void DueAndPotentialProducedCraftKeepRandomFuelSensitivity(string context)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var overlay = ScopedVariantRules();
        if (context == "produced") overlay = overlay.Replace("manufacture: [", "manufacture: [{name: OTHER_FUEL_PRODUCT, category: STR_CRAFT, time: 1, producedItems: {OTHER_FUEL: 1}}, ", StringComparison.Ordinal);
        var content = Load(overlay);
        var initial = ScopedVariantSnapshot(content, context);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "scoped-fuel-guard.sav");
        AssertTimeBlocked(campaign, "UFO retargeting requires production fuel-stock forecasts.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnconsumedScopedMaterialsPermitNativeRandomVariants(int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(ScopedMaterialRules(mode));
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
            TestFixtures.AssertJsonEqual(Assert.Single(rows, row => row[1].GetInt32() == choice), actual);
            Assert.Equal(mode != 0, after.Bases[0].Productions.Any(project => project.RuleId == "VARIANT_PRODUCT"));
            Assert.Equal(mode == 1, after.Bases[0].Productions.Any(project => project.RuleId == "OTHER_MATERIAL"));
            campaign = ReloadRepeatedly(content, campaign, seed, "scoped-random-material.sav", 3, after);
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void ContinuingScopedMaterialsRetainFallbackAwareGuard(int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "productionScopedMaterialRows").Where(row => row[0].GetInt32() == mode).ToArray();
        Assert.Equal(2, rows.Select(row => row[5].GetInt32()).Distinct().Count());
        var content = Load(ScopedMaterialRules(mode));
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnreachedAndDelayedScopedCraftBirthsDoNotConsumeCurrentFuel(int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var overlay = ScopedVariantRules().Replace("manufacture: [", "manufacture: [{name: OTHER_FUEL_PRODUCT, category: STR_CRAFT, time: " +
            (mode == 1 ? 1 : 100) + ", producedItems: {OTHER_FUEL: 1}, transferTimes: [0, 0, " + (mode == 1 ? 2 : 0) + "]}, ", StringComparison.Ordinal);
        var content = Load(overlay);
        var rows = TestFixtures.Rows(oracle.RootElement, "productionScopedCraftRows").Where(row => row[0].GetInt32() == mode).ToArray();
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = ScopedVariantSnapshot(content, "produced") with { RandomState = seed };
            var owner = initial.Bases[0];
            initial = initial with
            {
                Bases = [owner with
                {
                    Engineers = mode == 2 ? 2 : 0,
                    Productions = [owner.Productions[0], owner.Productions[1] with { Assigned = mode == 2 ? 0 : 1, IsFallback = mode == 2 }],
                }],
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var choice = after.Bases[0].Items.GetValueOrDefault("INERT_B") == 2 ? 2 : 1;
            choices.Add(choice);
            var first = Assert.Single(result.Events.OfType<CampaignProductionProgress>(), progress => progress.RuleId == "VARIANT_PRODUCT");
            var other = result.Events.OfType<CampaignProductionProgress>().SingleOrDefault(progress => progress.RuleId == "OTHER_FUEL_PRODUCT");
            var craft = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "VARIANT_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                mode, choice, first.Produced, NativeProductionStop(first.StopReason), other?.Produced ?? 0, NativeProductionStop(other?.StopReason),
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.Bases[0].Items.GetValueOrDefault("INERT_A"), after.Bases[0].Items.GetValueOrDefault("INERT_B"),
                mode == 1 ? 1 : Assert.Single(after.Bases[0].Productions, project => project.RuleId == "OTHER_FUEL_PRODUCT").Assigned,
                after.Bases[0].Crafts.Count(otherCraft => otherCraft.RuleId == "OTHER_FUEL"), after.Bases[0].Transfers.Count,
                mode == 1 ? Assert.Single(after.Bases[0].Transfers).Hours : 0, craft.Status, craft.Fuel,
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            TestFixtures.AssertJsonEqual(Assert.Single(rows, row => row[1].GetInt32() == choice), actual);
            if (mode == 1)
            {
                var born = Assert.Single(after.Bases[0].Transfers).Craft!;
                Assert.Equal("STR_REFUELLING", born.Logistics!.Status);
                Assert.Equal(0, born.Logistics.Fuel);
                Assert.Equal(born.Id + 1, after.NextIds["OTHER_FUEL"]);
            }
            campaign = ReloadRepeatedly(content, campaign, seed, "scoped-craft-production.sav", 3, after);
            if (mode == 1)
            {
                // The intervening hour has no completion (spent becomes three).
                // A fresh hunter must consume the arrival forecast; the previous
                // hunter can retain its already selected target without that query.
                var due = after with
                {
                    Time = new(1, 1, 1, 1999, 3, 59, 55),
                    World = after.World with { Ufos = initial.World.Ufos },
                    Bases = [after.Bases[0] with
                    {
                        Transfers = [after.Bases[0].Transfers[0] with { Hours = 1 }],
                        Productions = [after.Bases[0].Productions[0] with { Spent = 3 }],
                    }],
                };
                AssertTimeBlocked(CampaignState.Restore(due, content, new SplitMix64RandomSource(seed)), "UFO retargeting requires production fuel-stock forecasts.");
            }
        }
        Assert.Equal(2, choices.Count);
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
        TestFixtures.AssertJsonEqual(row.EnumerateArray().Skip(4).ToArray(), actual);
    }
}
