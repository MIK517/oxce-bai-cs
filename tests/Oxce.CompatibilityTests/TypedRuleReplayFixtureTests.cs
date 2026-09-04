using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.FixtureSupport;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class TypedRuleReplayFixtureTests
{
    private static readonly RuleSectionDefinition ProbeSection = new("probeRules", "type");

    [Fact]
    public void TypedReplayMatchesPinnedReferenceFixture()
    {
        var root = FindRepositoryRoot();
        var manifest = FixtureManifestLoader.Load(
            Path.Combine(root, "fixtures", "manifests", "typed-rule-replay.json"));
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "typed-rule-replay");
        var expectedPath = Path.GetFullPath(manifest.Expected, root);
        var diagnostics = new DiagnosticCollector();
        var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var catalog = ModCatalog.Create(discovery.Mods, diagnostics);
        var plan = ModLoadPlanner.Create(
            catalog,
            [new ModActivation("fixture", true)],
            "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"),
            diagnostics);
        var unresolved = RulesetComposer.Compose(plan, [ProbeSection], diagnostics);

        var actual = new ProbeLoader().Load(Assert.Single(unresolved.Sections), diagnostics);

        using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));
        var expectedRules = expected.RootElement.GetProperty("rules");
        Assert.Equal(expectedRules.GetArrayLength(), actual.Rules.Count);
        for (var index = 0; index < actual.Rules.Count; index++)
        {
            var expectedRule = expectedRules[index];
            var actualRule = actual.Rules[index];
            Assert.Equal(expectedRule.GetProperty("id").GetString(), actualRule.Id);
            Assert.Equal(expectedRule.GetProperty("label").GetString(), actualRule.Value.Label);
            Assert.Equal(expectedRule.GetProperty("value").GetInt32(), actualRule.Value.Value);
            Assert.Equal(expectedRule.GetProperty("enabled").GetBoolean(), actualRule.Value.Enabled);
            Assert.Equal(
                expectedRule.GetProperty("values").EnumerateArray().Select(value => value.GetInt32()),
                actualRule.Value.Values);
            Assert.Equal(
                expectedRule.GetProperty("mapping").EnumerateArray()
                    .Select(pair => new KeyValuePair<string, int>(pair[0].GetString()!, pair[1].GetInt32())),
                actualRule.Value.Mapping);
        }

        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DeferredRuleProperty);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
        var deferred = Assert.Single(actual.Rules[0].DeferredProperties);
        Assert.Equal("script", deferred.Key);
        Assert.Equal("return 1;", YamlValueReader.ReadString(deferred.Node));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class ProbeLoader : IdOnlyTypedRuleFamilyLoader<ProbeBuilder, ProbeRule>
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

            if (reader.TryGet("mapping", out var mapping))
            {
                builder.Mapping = YamlValueReader.ReadMap(
                    mapping!,
                    YamlValueReader.ReadString,
                    YamlValueReader.ReadInt32,
                    StringComparer.Ordinal);
            }

            reader.Defer("script", "compatible script compilation belongs to Phase 4");
        }

        protected override ProbeRule Freeze(ProbeBuilder builder) =>
            new(
                builder.Label,
                builder.Value,
                builder.Enabled,
                Array.AsReadOnly(builder.Values),
                builder.Mapping.ToArray());
    }

    private sealed class ProbeBuilder(string id)
    {
        public string Id { get; } = id;

        public string Label { get; set; } = string.Empty;

        public int Value { get; set; }

        public bool Enabled { get; set; }

        public int[] Values { get; set; } = [];

        public SortedDictionary<string, int> Mapping { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed record ProbeRule(
        string Label,
        int Value,
        bool Enabled,
        IReadOnlyList<int> Values,
        IReadOnlyList<KeyValuePair<string, int>> Mapping);
}
