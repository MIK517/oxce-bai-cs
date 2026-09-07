using System.Text.Json;
using Oxce.Gameplay.Campaigns;
using Oxce.Core.Random;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicLogisticsFixtureTests
{
    [Fact]
    public void StartingCampaignMatchesReferenceCrewAwardAndWeaponRemovalOrder()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Oxce.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var discovery = ModDiscovery.ScanDirectory(Path.Combine(root.FullName, "fixtures/public/mods/strategic-logistics"));
        var plan = ModLoadPlanner.Create(ModCatalog.Create(discovery.Mods), [new("logistics", true)], "logistics", new("Extended", "8.6.1.0"));
        var content = ContentSnapshotBuilder.Build(plan).Content;
        var campaign = CampaignFactory.Create(content, new(new(Guid.NewGuid()), "Starting fixture", "logistics", ["logistics"], CampaignDifficulty.Veteran),
            new SplitMix64RandomSource(42), SystemCampaignClock.Instance);
        var snapshot = campaign.Capture();
        // Mod::newSave returns both launchers and floor(8/3) clips before assigning crew.
        Assert.Equal(4, snapshot.Bases[0].Items["SUPPLY"]);
        Assert.Equal(2, snapshot.Bases[0].Items["BULKY"]);
        Assert.Equal<string>(["INTERCEPTOR", "CARRIER", "CARRIER", ""], snapshot.Bases[0].Soldiers.Select(s => s.Personal!.CraftType));
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot);
        Assert.Contains("noun: NoNoun", yaml, StringComparison.Ordinal);
        var loaded = OxceSaveAdapter.Load(yaml, "starting.sav", content, new SplitMix64RandomSource(1),
            new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        var first = loaded.Campaign.Capture();
        Assert.Equivalent(snapshot, first, strict: true);
        var second = OxceSaveAdapter.Load(OxceSaveAdapter.EmitLoadedCampaign(first, loaded.Source), "starting.sav", content,
            new SplitMix64RandomSource(2), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equivalent(first, second.Campaign.Capture(), strict: true);
    }

    [Fact]
    public void LogisticsArithmeticMatchesExtractedReferenceMethods()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Oxce.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "fixtures", "expected", "savegames", "strategic-logistics.expected.json")));
        foreach (var row in expected.RootElement.GetProperty("prices").EnumerateArray())
            Assert.Equal(row[2].GetInt32(), StrategicLogisticsMath.AdjustedItemPrice(row[0].GetInt32(), row[1].GetInt32()));
        foreach (var row in expected.RootElement.GetProperty("stores").EnumerateArray())
            Assert.Equal(row[1].GetInt32() != 0, StrategicLogisticsMath.StoresOverfull(10, row[0].GetDouble()));
        foreach (var row in expected.RootElement.GetProperty("transfers").EnumerateArray())
        {
            var distance = StrategicLogisticsMath.TransferDistance(0, 0, row[0].GetDouble(), 0);
            Assert.Equal(row[1].GetDouble(), distance, 10);
            Assert.Equal(row[2].GetInt32(), StrategicLogisticsMath.TransferHours(distance));
            Assert.Equal(row[3].GetInt32(), StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Item));
            Assert.Equal(row[4].GetInt32(), StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Soldier));
            Assert.Equal(row[5].GetInt32(), StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Craft));
        }
        var discovery = ModDiscovery.ScanDirectory(Path.Combine(root.FullName, "fixtures/public/mods/strategic-logistics"));
        var plan = ModLoadPlanner.Create(ModCatalog.Create(discovery.Mods), [new("logistics", true)], "logistics", new("Extended", "8.6.1.0"));
        var rules = ContentSnapshotBuilder.Build(plan).Content.RuntimeRules;
        var rule = rules.Soldiers[rules.Soldiers.GetRequired("TEMPLATE_RECRUIT")].Value;
        var original = SoldierGeneration.Generate(rule, "ARMOR", 0, new HashSet<string>(StringComparer.Ordinal), new SplitMix64RandomSource(42));
        foreach (var row in expected.RootElement.GetProperty("bonuses").EnumerateArray())
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
