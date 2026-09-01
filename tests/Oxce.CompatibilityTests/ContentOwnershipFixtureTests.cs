using System.Text.Json;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Content;
using Oxce.Scripting.Runtime;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class ContentOwnershipFixtureTests
{
    [Fact]
    public void MultiModFileScopesMatchPinnedBehavior()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "content-ownership");
        using var expectedDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "fixtures", "expected", "mods", "content-ownership.expected.json")));
        var expected = expectedDocument.RootElement;
        var discovery = ModDiscovery.ScanDirectory(fixture);
        var plan = ModLoadPlanner.Create(
            ModCatalog.Create(discovery.Mods),
            [new ModActivation("ownership-master", true), new ModActivation("ownership-addon", true)],
            "ownership-master",
            new ModEngineIdentity("Extended", "8.6.1.0"));

        var snapshot = ContentSnapshotBuilder.Build(plan);

        Assert.Equal(expected.GetProperty("parsedFiles").GetInt32(), snapshot.Content.ParsedFileCount);
        Assert.Equal(
            expected.GetProperty("tagNames").EnumerateArray().Select(static item => item.GetString()),
            snapshot.Tags.Tags.Select(static tag => tag.Name));
        foreach (var property in expected.GetProperty("initialValues").EnumerateObject())
        {
            var separator = property.Name.IndexOf('/', StringComparison.Ordinal);
            var owner = property.Name[..separator];
            var tag = property.Name[(separator + 1)..];
            Assert.Contains(snapshot.InitialValues,
                value => value.OwnerId == owner && value.TagName == tag && value.Value == property.Value.GetInt32());
        }
        foreach (var property in expected.GetProperty("scriptResults").EnumerateObject())
        {
            var separator = property.Name.IndexOf('/', StringComparison.Ordinal);
            var owner = property.Name[..separator];
            var parser = property.Name[(separator + 1)..];
            var artifact = Assert.Single(snapshot.Scripts,
                script => script.OwnerId == owner && script.ParserName == parser);
            var result = ScriptVm.Execute(
                artifact.Program,
                new Dictionary<string, int> { ["sprite_index"] = 0 });
            Assert.True(result.Succeeded);
            Assert.Equal(property.Value.GetInt32(), result.Outputs["sprite_index"]);
        }
        Assert.True(snapshot.Content.Catalog.Items.Items.TryGet("SHARED_ITEM", out var shared));
        Assert.Equal(
            expected.GetProperty("sharedItemDeferredProperties").GetInt32(),
            shared!.CompatibilityData.DeferredProperties.Count);
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
