using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class ContentSnapshotTests
{
    [Fact]
    public void EmptyGraphCompilesReferenceDefaultsAndReachesScriptsCompiled()
    {
        using var fixture = new TemporaryMod("{}");

        var snapshot = ContentSnapshotBuilder.Build(CreatePlan(fixture.Root));

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled), Diagnostics(snapshot));
        Assert.Equal(7, snapshot.Scripts.Count(script => script.Scope == ContentScriptScope.Default));
        Assert.Equal(snapshot.Content.ParsedFileCount, snapshot.Content.Documents.ParsedFileCount);
    }

    [Fact]
    public void PreservedDocumentsCompileTagsEventsRuleScriptsStatsAndInitialValues()
    {
        const string rules = """
            extended:
              tags:
                RuleMod:
                  GLOBAL_POWER: int
                RuleItem:
                  POWER: int
              globals:
                GLOBAL_POWER: 3
              scripts:
                newTurnItem:
                  - new: fixture-event
                    offset: 1.25
                    code: return;
            items:
              - type: ITEM
                damageBonus:
                  strength: 0.25
                  healthCurrent: [0.1, 0.2]
                tags:
                  POWER: 7
                scripts:
                  newTurnItem: return;
            """;
        using var fixture = new TemporaryMod(rules);

        var snapshot = ContentSnapshotBuilder.Build(CreatePlan(fixture.Root));

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled), Diagnostics(snapshot));
        Assert.Collection(snapshot.Tags.Tags, static _ => { }, static _ => { });
        Assert.Contains(snapshot.Scripts, script => script.Scope == ContentScriptScope.GlobalEvent &&
            script.ParserName == "newTurnItem");
        Assert.Contains(snapshot.Scripts, script => script.Scope == ContentScriptScope.Rule &&
            script.OwnerId == "ITEM" && script.ParserName == "newTurnItem");
        Assert.Contains(snapshot.Scripts, script => script.Scope == ContentScriptScope.StatBonus &&
            script.OwnerId == "ITEM" && script.ParserName == "damageBonusBonusStats");
        Assert.Single(snapshot.EventPlans);
        Assert.Collection(snapshot.InitialValues, static _ => { }, static _ => { });
        var value = Assert.Single(snapshot.InitialValues, item => item.OwnerId == "ITEM");
        Assert.Equal("Tag.POWER", value.TagName);
        Assert.Equal(7, value.Value);
        Assert.Contains(snapshot.InitialValues,
            item => item.OwnerSection == "extended" && item.TagName == "Tag.GLOBAL_POWER" && item.Value == 3);
    }

    [Fact]
    public void ScriptErrorsAndDroppedDiagnosticsCannotPublishFalseSuccess()
    {
        const string rules = """
            items:
              - type: FIRST
                scripts: {newTurnItem: "missing_operation; return;"}
              - type: SECOND
                scripts: {newTurnItem: "also_missing; return;"}
            """;
        using var fixture = new TemporaryMod(rules);

        var snapshot = ContentSnapshotBuilder.Build(
            CreatePlan(fixture.Root),
            options: new ContentSnapshotOptions { MaximumDiagnostics = 1 });

        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled));
        Assert.True(snapshot.ReportedDiagnosticCount > 1);
        Assert.Single(snapshot.Diagnostics);
        Assert.True(snapshot.DroppedDiagnosticCount > 0);
    }

    [Fact]
    public void MissingTagsFileFailsIntentionally()
    {
        using var fixture = new TemporaryMod("extended: {tagsFile: Ruleset/missing.rul}");

        var snapshot = ContentSnapshotBuilder.Build(CreatePlan(fixture.Root));

        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.ScriptsCompiled));
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Code == ModDiagnosticCodes.InvalidScriptContent &&
            diagnostic.Message.Contains("Unknown file name", StringComparison.Ordinal));
    }

    private static string Diagnostics(ContentSnapshot snapshot) => string.Join(
        Environment.NewLine,
        snapshot.Diagnostics.Select(static diagnostic => diagnostic.Message));

    private static ModLoadPlan CreatePlan(string root)
    {
        var discovery = ModDiscovery.ScanDirectory(root);
        return ModLoadPlanner.Create(
            ModCatalog.Create(discovery.Mods),
            [new ModActivation("fixture", true)],
            "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
    }

    private sealed class TemporaryMod : IDisposable
    {
        public TemporaryMod(string rules)
        {
            Root = Path.Combine(Path.GetTempPath(), "oxce-content-snapshot-" + Guid.NewGuid().ToString("N"));
            var mod = Path.Combine(Root, "fixture");
            Directory.CreateDirectory(Path.Combine(mod, "Ruleset"));
            File.WriteAllText(Path.Combine(mod, "metadata.yml"),
                "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\n");
            File.WriteAllText(Path.Combine(mod, "Ruleset", "fixture.rul"), rules);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
