using Oxce.Gameplay.Campaigns;
using Oxce.Core.Random;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicLogisticsFixtureTests
{
    [Fact]
    public void StartingCampaignMatchesReferenceCrewAwardAndWeaponRemovalOrder()
    {
        var content = TestFixtures.LoadStrategicLogistics();
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "Starting fixture", CampaignDifficulty.Veteran);
        var snapshot = campaign.Capture();
        // Mod::newSave returns both launchers and floor(8/3) clips before assigning crew.
        Assert.Equal(4, snapshot.Bases[0].Items["SUPPLY"]);
        Assert.Equal(2, snapshot.Bases[0].Items["BULKY"]);
        Assert.Equal<string>(["INTERCEPTOR", "CARRIER", "CARRIER", ""], snapshot.Bases[0].Soldiers.Select(s => s.Personal!.CraftType));
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot);
        Assert.Contains("noun: NoNoun", yaml, StringComparison.Ordinal);
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, seed: 1, name: "starting.sav");
        var first = loaded.Campaign.Capture();
        Assert.Equivalent(snapshot, first, strict: true);
        var second = TestFixtures.LoadLogisticsSave(
            OxceSaveAdapter.EmitLoadedCampaign(first, loaded.Source), content, seed: 2, name: "starting.sav");
        Assert.Equivalent(first, second.Campaign.Capture(), strict: true);
    }

    [Fact]
    public void LogisticsArithmeticMatchesExtractedReferenceMethods()
    {
        using var expected = TestFixtures.ReadVerifiedExpected("strategic-logistics");
        foreach (var row in TestFixtures.Rows(expected.RootElement, "prices"))
            Assert.Equal(row[2].GetInt32(), StrategicLogisticsMath.AdjustedItemPrice(row[0].GetInt32(), row[1].GetInt32()));
        foreach (var row in TestFixtures.Rows(expected.RootElement, "stores"))
            Assert.Equal(row[1].GetInt32() != 0, StrategicLogisticsMath.StoresOverfull(10, row[0].GetDouble()));
        foreach (var row in TestFixtures.Rows(expected.RootElement, "transfers"))
        {
            var distance = StrategicLogisticsMath.TransferDistance(0, 0, row[0].GetDouble(), 0);
            Assert.Equal(row[1].GetDouble(), distance, 10);
            Assert.Equal(row[2].GetInt32(), StrategicLogisticsMath.TransferHours(distance));
            Assert.Equal(row[3].GetInt32(), StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Item));
            Assert.Equal(row[4].GetInt32(), StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Soldier));
            Assert.Equal(row[5].GetInt32(), StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Craft));
        }
        var rules = TestFixtures.LoadStrategicLogistics().RuntimeRules;
        var rule = rules.Soldiers[rules.Soldiers.GetRequired("TEMPLATE_RECRUIT")].Value;
        var original = SoldierGeneration.Generate(rule, "ARMOR", 0, new HashSet<string>(StringComparer.Ordinal), new SplitMix64RandomSource(42));
        foreach (var row in TestFixtures.Rows(expected.RootElement, "bonuses"))
        {
            var random = new MinimumRandom();
            var template = rule.SpawnedTemplate! with
            {
                TransformationBonusesCount = row[0].GetInt32(),
                CurrentStats = new Dictionary<string, short>(), // Keep mana unchanged to isolate bonus draws.
            };
            var result = SoldierGeneration.ApplyTemplate(original, template, rule, rules, random);
            Assert.Equal(row[1].GetInt32(), random.Calls);
            Assert.Equal(row[2].GetInt32(), result.TransformationBonuses["BONUS_A"]);
            Assert.Equal(row[3].GetInt32(), result.TransformationBonuses.GetValueOrDefault("BONUS_B"));
            Assert.False(result.TransformationBonuses.ContainsKey(""));
            Assert.False(result.TransformationBonuses.ContainsKey("DISABLED_BONUS"));
        }
    }

    private sealed class MinimumRandom : IRandomSource
    {
        public int Calls { get; private set; }
        public int NextInclusive(int minimum, int maximum) { Calls++; return minimum; }
        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException("Unexpected name/look generation.");
        public double NextUnit() => throw new InvalidOperationException("Unexpected floating-point random draw.");
    }
}
