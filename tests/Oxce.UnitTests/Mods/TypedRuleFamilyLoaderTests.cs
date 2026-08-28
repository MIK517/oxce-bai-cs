using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class TypedRuleFamilyLoaderTests
{
    private static readonly RuleSectionDefinition ProbeSection = new("probeRules", "type");

    [Fact]
    public void ReplaysOperationsAndFamilyOwnedRefNodeInSourceOrder()
    {
        const string yaml = """
            probeRules:
              - type: RULE_A
                refNode:
                  type: INHERITED_IDENTITY
                  label: inherited
                  value: 10
                  values: [1, 2]
                value: 20
              - update: RULE_A
                values: [3]
                enabled: true
            """;

        var diagnostics = new DiagnosticCollector();

        var typed = Load(yaml, diagnostics);

        var rule = Assert.Single(typed.Rules).Value;
        Assert.Equal("inherited", rule.Label);
        Assert.Equal(20, rule.Value);
        Assert.Equal([3], rule.Values);
        Assert.True(rule.Enabled);
        Assert.DoesNotContain(diagnostics.Snapshot(), item =>
            item.Code == ModDiagnosticCodes.UnconsumedRuleProperty &&
            item.Message.Contains("'type'", StringComparison.Ordinal));
        Assert.True(typed.Capabilities.Has(ContentLoadStage.Composed | ContentLoadStage.Typed));
        Assert.False(typed.Capabilities.Has(ContentLoadStage.Linked));
    }

    [Fact]
    public void ReportsUnconsumedAndExplicitlyDeferredPropertiesWithProvenance()
    {
        const string yaml = """
            probeRules:
              - type: RULE_A
                unknown: 1
                script: return 1;
            """;
        var diagnostics = new DiagnosticCollector();

        var typed = Load(yaml, diagnostics);

        var events = diagnostics.Snapshot();
        var unknown = Assert.Single(events, item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
        Assert.Equal(DiagnosticSeverity.Error, unknown.Severity);
        Assert.Equal("fixture", unknown.Context.ModId);
        Assert.Equal("probeRules", unknown.Context.RuleType);
        Assert.Equal("RULE_A", unknown.Context.RuleId);
        var deferred = Assert.Single(events, item => item.Code == ModDiagnosticCodes.DeferredRuleProperty);
        Assert.Contains("Phase 4", deferred.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(events, item =>
            item.Code == ModDiagnosticCodes.UnconsumedRuleProperty && item.Message.Contains("script", StringComparison.Ordinal));
        var deferredProperty = Assert.Single(Assert.Single(typed.Rules).DeferredProperties);
        Assert.Equal("script", deferredProperty.Key);
        Assert.Equal("return 1;", YamlValueReader.ReadString(deferredProperty.Node));
        Assert.Equal("fixture", deferredProperty.Source.ModId);
    }

    [Fact]
    public void PropertyNodesAreBoundedAcrossAWholeFamily()
    {
        const string yaml = "probeRules: [{type: RULE_A, value: 1}]";
        var options = new TypedRuleLoadOptions { MaximumPropertyNodes = 1 };

        Assert.Throws<YamlFormatException>(() => Load(yaml, options: options));
    }

    [Fact]
    public void NestedMappingReadersAuditTheirOwnProperties()
    {
        const string yaml = """
            probeRules:
              - type: RULE_A
                nested:
                  known: 7
                  unknownNested: 9
            """;
        var diagnostics = new DiagnosticCollector();

        var typed = Load(yaml, diagnostics);

        Assert.Equal(7, Assert.Single(typed.Rules).Value.NestedValue);
        Assert.Contains(
            diagnostics.Snapshot(),
            item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty &&
                item.Message.Contains("unknownNested", StringComparison.Ordinal));
    }

    [Fact]
    public void FamilyCanPreserveAllRemainingDynamicProperties()
    {
        const string yaml = "probeRules: [{type: RULE_A, dynamicScriptValue: 7}]";
        var diagnostics = new DiagnosticCollector();
        using var fixture = new TemporaryRulesetMod(yaml);
        var unresolved = Compose(fixture.Root, ProbeSection, diagnostics);

        var typed = new DynamicProbeLoader().Load(Assert.Single(unresolved.Sections), diagnostics);

        Assert.Equal("dynamicScriptValue", Assert.Single(Assert.Single(typed.Rules).DeferredProperties).Key);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
    }

    [Fact]
    public void LoaderRejectsASectionWithDifferentIdentityContract()
    {
        using var fixture = new TemporaryRulesetMod("wrong: [{id: A}]");
        var unresolved = Compose(fixture.Root, new RuleSectionDefinition("wrong", "id"));

        Assert.Throws<ArgumentException>(() => new ProbeLoader().Load(Assert.Single(unresolved.Sections)));
    }

    private static TypedRuleSection<ProbeRule> Load(
        string yaml,
        IDiagnosticSink? diagnostics = null,
        TypedRuleLoadOptions? options = null)
    {
        using var fixture = new TemporaryRulesetMod(yaml);
        var unresolved = Compose(fixture.Root, ProbeSection, diagnostics);
        return new ProbeLoader().Load(Assert.Single(unresolved.Sections), diagnostics, options);
    }

    private static UnresolvedRuleCatalog Compose(
        string root,
        RuleSectionDefinition section,
        IDiagnosticSink? diagnostics = null)
    {
        var discovery = ModDiscovery.ScanDirectory(root);
        var catalog = ModCatalog.Create(discovery.Mods);
        var plan = ModLoadPlanner.Create(
            catalog,
            [new ModActivation("fixture", true)],
            "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
        return RulesetComposer.Compose(plan, [section], diagnostics);
    }

    private sealed class ProbeLoader : TypedRuleFamilyLoader<ProbeBuilder, ProbeRule>
    {
        public ProbeLoader() : base(ProbeSection)
        {
        }

        protected override ProbeBuilder Create(string id) => new(id);

        protected override void Apply(ProbeBuilder builder, RulePropertyReader reader)
        {
            reader.ApplyRefNode(parent => Apply(builder, parent));
            builder.Label = reader.ReadString("label", builder.Label);
            builder.Value = reader.ReadInt32("value", builder.Value);
            builder.Enabled = reader.ReadBoolean("enabled", builder.Enabled);
            if (reader.TryGet("values", out var values))
            {
                builder.Values = YamlValueReader.ReadSequence(values!, YamlValueReader.ReadInt32);
            }

            reader.ApplyMapping("nested", nested =>
            {
                builder.NestedValue = nested.ReadInt32("known", builder.NestedValue);
            });

            reader.Defer("script", "compatible script compilation belongs to Phase 4");
        }

        protected override ProbeRule Freeze(ProbeBuilder builder) =>
            new(
                builder.Id,
                builder.Label,
                builder.Value,
                builder.Enabled,
                Array.AsReadOnly(builder.Values),
                builder.NestedValue);
    }

    private sealed class ProbeBuilder(string id)
    {
        public string Id { get; } = id;

        public string Label { get; set; } = string.Empty;

        public int Value { get; set; }

        public bool Enabled { get; set; }

        public int[] Values { get; set; } = [];

        public int NestedValue { get; set; }
    }

    private sealed record ProbeRule(
        string Id,
        string Label,
        int Value,
        bool Enabled,
        IReadOnlyList<int> Values,
        int NestedValue);

    private sealed class DynamicProbeLoader : TypedRuleFamilyLoader<ProbeBuilder, ProbeRule>
    {
        public DynamicProbeLoader() : base(ProbeSection) { }
        protected override ProbeBuilder Create(string id) => new(id);
        protected override void Apply(ProbeBuilder builder, RulePropertyReader reader) =>
            reader.DeferRemaining("dynamic script values require Phase 4 registration");
        protected override ProbeRule Freeze(ProbeBuilder builder) =>
            new(builder.Id, builder.Label, builder.Value, builder.Enabled, builder.Values, builder.NestedValue);
    }

    private sealed class TemporaryRulesetMod : IDisposable
    {
        public TemporaryRulesetMod(string yaml)
        {
            Root = Path.Combine(Path.GetTempPath(), $"oxce-typed-rule-test-{Guid.NewGuid():N}");
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
