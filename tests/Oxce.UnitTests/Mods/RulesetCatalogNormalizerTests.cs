using System.Text.Json;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class RulesetCatalogNormalizerTests
{
    [Fact]
    public void EmitsStableSchemaOrderProvenanceAndYamlNodeKinds()
    {
        using var fixture = new TemporaryRulesetMod("items: [{type: ITEM_A, values: [1, null]}]");
        var discovery = ModDiscovery.ScanDirectory(fixture.Root);
        var catalog = ModCatalog.Create(discovery.Mods);
        var plan = ModLoadPlanner.Create(
            catalog,
            [new ModActivation("fixture", true)],
            "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
        var rules = RulesetComposer.Compose(plan, [new RuleSectionDefinition("items", "type")]);

        Assert.True(rules.Capabilities.Has(ContentLoadStage.Composed));
        Assert.False(rules.Capabilities.Has(ContentLoadStage.Typed));

        var json = RulesetCatalogNormalizer.NormalizeToJson(
            rules,
            new RulesetCatalogNormalizationOptions { NormalizeSourceName = Path.GetFileName });
        using var document = JsonDocument.Parse(json);

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("composed", document.RootElement.GetProperty("stage").GetString());
        var rule = document.RootElement.GetProperty("sections")[0].GetProperty("rules")[0];
        Assert.Equal("ITEM_A", rule.GetProperty("id").GetString());
        Assert.Equal("fixture.rul", rule.GetProperty("creationSource").GetProperty("path").GetString());
        Assert.Equal("mapping", rule.GetProperty("operations")[0].GetProperty("node").GetProperty("kind").GetString());
    }

    private sealed class TemporaryRulesetMod : IDisposable
    {
        public TemporaryRulesetMod(string yaml)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-rule-normalizer-test-{Guid.NewGuid():N}");
            var mod = Path.Combine(Root, "fixture");
            var ruleset = Path.Combine(mod, "Ruleset");
            Directory.CreateDirectory(ruleset);
            File.WriteAllText(
                Path.Combine(mod, "metadata.yml"),
                "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\nreservedSpace: 0\n");
            File.WriteAllText(Path.Combine(ruleset, "fixture.rul"), yaml);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
