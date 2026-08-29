using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Scripting.Api;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Types;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class ScriptApiCatalogFixtureTests
{
    [Fact]
    public void RuntimeCatalogMatchesPinnedTemplateResolvedMetadata()
    {
        var root = FindRepositoryRoot();
        var manifest = FixtureManifestLoader.Load(
            Path.Combine(root, "fixtures", "manifests", "script-api-catalog.json"));
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root)));
        var counts = document.RootElement.GetProperty("counts");
        var catalog = ReferenceScriptApiCatalog.Instance;

        Assert.Equal(counts.GetProperty("parsers").GetInt32(), catalog.Parsers.Count);
        Assert.Equal(counts.GetProperty("bindings").GetInt32(), catalog.Bindings.Count);
        Assert.Equal(counts.GetProperty("constants").GetInt32(), catalog.Constants.Count);
        Assert.Equal(0, counts.GetProperty("unresolved").GetInt32());
        Assert.Equal(Enumerable.Range(10_000, 755), catalog.Bindings.Select(static binding => binding.Id.Value));

        Assert.True(catalog.TryGetParser("visibilityUnit", out var parser));
        Assert.Equal("unit", parser!.Group);
        Assert.Equal(["current_visibility", "visibility_mode"], parser.OutputNames);
        Assert.Contains(parser.Inputs, static input => input.Name == "rules" && input.Type.IsReference);

        var binding = Assert.Single(catalog.Bindings, static item => item.Id.Value == 10_562);
        Assert.Equal("RuleMod.getMaxViewDistance", binding.Name);
        Assert.Collection(
            binding.Parameters,
            parameter => Assert.True(parameter.Type.IsReference),
            parameter => Assert.True(parameter.Type.IsWritable));
        Assert.Contains(catalog.Bindings.SelectMany(static item => item.Parameters),
            static parameter => parameter.Type.Id == ScriptPrimitiveTypes.Separator);
    }

    [Fact]
    public void CatalogParserLowersTypedReceiverToStableBinding()
    {
        var definition = ScriptParserDefinition.FromCatalog("visibilityUnit");

        var compiled = ScriptCompiler.Compile(
            "rules.getMaxViewDistance current_visibility; return current_visibility visibility_mode;",
            definition);

        Assert.True(compiled.Succeeded);
        var binding = Assert.Single(compiled.Program!.Bindings);
        Assert.Equal(10_562, binding.Id.Value);
        Assert.Equal("RuleMod.getMaxViewDistance", binding.Name);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ??
            throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
