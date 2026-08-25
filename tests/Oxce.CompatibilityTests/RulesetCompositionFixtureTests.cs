using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class RulesetCompositionFixtureTests
{
    [Fact]
    public void NamedRuleOperationsMatchReferenceLoadRuleBehavior()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "rule-operations");
        var diagnostics = new DiagnosticCollector();
        var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var modCatalog = ModCatalog.Create(discovery.Mods, diagnostics);
        var plan = ModLoadPlanner.Create(
            modCatalog,
            [new ModActivation("fixture-master", true)],
            "fixture-master",
            new ModEngineIdentity("Extended", "8.6.1.0"),
            diagnostics);

        var catalog = RulesetComposer.Compose(
            plan,
            [new RuleSectionDefinition("items", "type")],
            diagnostics);

        var section = Assert.Single(catalog.Sections);
        Assert.Equal(["ITEM_A", "ITEM_NEW"], section.Rules.Select(rule => rule.Id));
        Assert.False(section.TryGet("ITEM_REMOVED", out _));

        var itemA = section.Rules[0];
        Assert.Equal(3, itemA.Operations.Count);
        Assert.Equal(["30-base.rul", "20-patch.rul", "10-final.rul"],
            itemA.Operations.Select(operation => Path.GetFileName(operation.Source.SourcePath)));
        Assert.Equal(130, ReadInt(itemA.Operations[^1].Node, "costBuy"));

        var itemNew = section.Rules[1];
        Assert.Single(itemNew.Operations);
        Assert.Equal(75, ReadInt(itemNew.Operations[0].Node, "costBuy"));
        Assert.Equal("10-final.rul", Path.GetFileName(itemNew.CreationSource.SourcePath));

        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.DuplicateNewRule);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingOverrideRule);
    }

    private static int ReadInt(YamlMappingNode mapping, string key)
    {
        Assert.True(mapping.TryGet(key, out var node));
        return YamlValueReader.ReadInt32(node!);
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
}
