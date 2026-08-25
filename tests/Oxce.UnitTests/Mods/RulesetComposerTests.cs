using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class RulesetComposerTests
{
    private static readonly RuleSectionDefinition Items = new("items", "type");

    [Fact]
    public void OperationsPreserveUpdatesAndReferenceIndexOrdering()
    {
        const string yaml = """
            items:
              - type: A
                value: 1
              - type: B
              - update: A
                value: 2
              - delete: B
              - new: C
              - delete: A
              - type: A
                value: 3
            """;

        var catalog = Compose(yaml);

        var section = Assert.Single(catalog.Sections);
        Assert.Equal(["C", "A"], section.Rules.Select(rule => rule.Id));
        Assert.Single(section.Rules[0].Operations);
        Assert.Single(section.Rules[1].Operations);
        Assert.Equal(RuleOperationKind.New, section.Rules[0].Operations[0].Kind);
        Assert.True(section.TryGet("A", out var recreated));
        Assert.Equal("fixture", recreated!.CreationSource.ModId);
    }

    [Fact]
    public void DefaultMarkerUpdatesExistingRuleWithoutReplacingItsHistory()
    {
        const string yaml = """
            items:
              - type: A
                value: 1
              - type: A
                value: 2
            """;

        var rule = Assert.Single(Assert.Single(Compose(yaml).Sections).Rules);

        Assert.Equal(2, rule.Operations.Count);
        Assert.All(rule.Operations, operation => Assert.Equal(RuleOperationKind.Default, operation.Kind));
    }

    [Fact]
    public void ConditionalMarkersMatchReferenceSoftFailureBehavior()
    {
        const string yaml = """
            items:
              - type: A
              - new: A
              - override: MISSING
              - update: MISSING
              - ignore: ANY_VALUE
            """;
        var diagnostics = new DiagnosticCollector();

        var catalog = Compose(yaml, diagnostics);

        Assert.Single(Assert.Single(catalog.Sections).Rules);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DuplicateNewRule);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingOverrideRule);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingUpdateRule);
    }

    [Theory]
    [InlineData("items: [{type: A, delete: A}]")]
    [InlineData("items: [{value: 1}]")]
    [InlineData("items: [A]")]
    [InlineData("items: {type: A}")]
    [InlineData("[items]")]
    [InlineData("items: [{type: null}]")]
    public void MalformedRuleShapesFailIntentionally(string yaml)
    {
        Assert.Throws<YamlFormatException>(() => Compose(yaml));
    }

    [Fact]
    public void MultipleDocumentsAreRejectedLikeReferenceRulesetReader()
    {
        Assert.Throws<YamlFormatException>(() => Compose("items: []\n---\nitems: []\n"));
    }

    [Fact]
    public void RefNodeMustBeMappingAndIsDepthBounded()
    {
        Assert.Throws<YamlFormatException>(() => Compose("items: [{type: A, refNode: PARENT}]"));

        var nested = "{}";
        for (var index = 0; index < 66; index++)
        {
            nested = $"{{refNode: {nested}}}";
        }

        Assert.Throws<YamlFormatException>(() => Compose($"items: [{{type: A, refNode: {nested}}}]"));
    }

    [Fact]
    public void TotalRuleOperationsAreBounded()
    {
        var options = new RulesetCompositionOptions { MaximumRuleOperations = 1 };

        Assert.Throws<YamlFormatException>(() => Compose("items: [{type: A}, {type: B}]", options: options));
    }

    private static UnresolvedRuleCatalog Compose(
        string yaml,
        IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? options = null)
    {
        using var fixture = new TemporaryRulesetMod(yaml);
        var discovery = ModDiscovery.ScanDirectory(fixture.Root);
        var catalog = ModCatalog.Create(discovery.Mods);
        var plan = ModLoadPlanner.Create(
            catalog,
            [new ModActivation("fixture", true)],
            "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
        return RulesetComposer.Compose(plan, [Items], diagnostics, options);
    }

    private sealed class TemporaryRulesetMod : IDisposable
    {
        public TemporaryRulesetMod(string yaml)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-ruleset-test-{Guid.NewGuid():N}");
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
