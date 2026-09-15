using Oxce.FixtureSupport;
using System.Text.Json;
using Oxce.Mods.Rulesets;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class RulesetCatalogNormalizerTests
{
    [Fact]
    public void EmitsStableSchemaOrderProvenanceAndYamlNodeKinds()
    {
        using var fixture = new TemporaryModFixture("items: [{type: ITEM_A, values: [1, null]}]");
        var plan = TestFixtures.CreatePlan(fixture.Root);
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
}
