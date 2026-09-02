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
        var expected = document.RootElement;
        var counts = expected.GetProperty("counts");
        var catalog = ReferenceScriptApiCatalog.Instance;
        var typeNames = catalog.Types.ToDictionary(static type => type.Id, static type => type.Name);

        Assert.Equal(counts.GetProperty("parsers").GetInt32(), catalog.Parsers.Count);
        Assert.Equal(counts.GetProperty("bindings").GetInt32(), catalog.Bindings.Count);
        Assert.Equal(counts.GetProperty("constants").GetInt32(), catalog.Constants.Count);
        Assert.Equal(0, counts.GetProperty("unresolved").GetInt32());

        AssertParsers(expected.GetProperty("parsers"), catalog, typeNames);
        AssertBindings(expected.GetProperty("bindings"), catalog, typeNames);
        AssertConstants(expected.GetProperty("constants"), catalog);
        AssertTypes(expected.GetProperty("typeTokens"), catalog);
    }

    private static void AssertParsers(
        JsonElement expected,
        ScriptApiCatalog catalog,
        IReadOnlyDictionary<ScriptTypeId, string> typeNames)
    {
        var entries = expected.EnumerateArray().ToArray();
        Assert.Equal(entries.Length, catalog.Parsers.Count);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var parser = catalog.Parsers[index];
            Assert.Equal(entry.GetProperty("name").GetString(), parser.Name);
            Assert.Equal(entry.GetProperty("group").GetString(), parser.Group);
            Assert.Equal(entry.GetProperty("supportsEvents").GetBoolean(), parser.SupportsEvents);
            AssertNamedValues(entry.GetProperty("outputs"), parser.Outputs, typeNames);
            AssertNamedValues(entry.GetProperty("inputs"), parser.Inputs, typeNames);
        }
    }

    private static void AssertBindings(
        JsonElement expected,
        ScriptApiCatalog catalog,
        IReadOnlyDictionary<ScriptTypeId, string> typeNames)
    {
        var entries = expected.EnumerateArray().ToArray();
        Assert.Equal(entries.Length, catalog.Bindings.Count);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var binding = catalog.Bindings[index];
            Assert.Equal(entry.GetProperty("id").GetInt32(), binding.Id.Value);
            Assert.Equal(entry.GetProperty("name").GetString(), binding.Name);
            Assert.Equal(
                entry.GetProperty("parameters").EnumerateArray().Select(static item => item.GetString()),
                binding.Parameters.Select(parameter => FormatType(parameter.Type, typeNames)));
            Assert.Equal(
                entry.GetProperty("parsers").EnumerateArray().Select(static item => item.GetString()),
                binding.ParserGroups);
        }
    }

    private static void AssertConstants(JsonElement expected, ScriptApiCatalog catalog)
    {
        var entries = expected.EnumerateArray().ToArray();
        Assert.Equal(entries.Length, catalog.Constants.Count);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var constant = catalog.Constants[index];
            Assert.Equal(entry.GetProperty("name").GetString(), constant.Name);
            Assert.Equal(entry.GetProperty("value").GetInt32(), constant.Value);
            Assert.Equal(
                entry.GetProperty("parsers").EnumerateArray().Select(static item => item.GetString()),
                constant.ParserGroups);
        }
    }

    private static void AssertNamedValues(
        JsonElement expected,
        IReadOnlyList<ScriptNamedValueDeclaration> actual,
        IReadOnlyDictionary<ScriptTypeId, string> typeNames)
    {
        var entries = expected.EnumerateArray().ToArray();
        Assert.Equal(entries.Length, actual.Count);
        for (var index = 0; index < entries.Length; index++)
        {
            Assert.Equal(entries[index].GetProperty("name").GetString(), actual[index].Name);
            Assert.Equal(entries[index].GetProperty("type").GetString(), FormatType(actual[index].Type, typeNames));
        }
    }

    private static void AssertTypes(
        JsonElement expected,
        ScriptApiCatalog catalog)
    {
        var expectedCustomTypes = expected.EnumerateArray()
            .Select(static item => BaseTypeName(item.GetString()!))
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var actualCustomTypes = catalog.Types
            .Where(static type => type.Id.Value >= ScriptPrimitiveTypes.FirstCustomTypeValue)
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(expectedCustomTypes, actualCustomTypes);
    }

    private static string? BaseTypeName(string token)
    {
        var remaining = token.StartsWith("var ", StringComparison.Ordinal) ? token[4..] : token;
        if (remaining.StartsWith("ptre ", StringComparison.Ordinal))
        {
            remaining = remaining[5..];
        }
        else if (remaining.StartsWith("ptr ", StringComparison.Ordinal))
        {
            remaining = remaining[4..];
        }

        return remaining is "" or "null" or "int" or "label" or "text" or "__" ? null : remaining;
    }

    private static string FormatType(
        ScriptTypeRef type,
        IReadOnlyDictionary<ScriptTypeId, string> typeNames)
    {
        var register = type.IsRegister ? "var " : string.Empty;
        var reference = type.IsEditableReference ? "ptre " : type.IsReference ? "ptr " : string.Empty;
        var name = type.Id == ScriptPrimitiveTypes.Null && type.IsReference
            ? string.Empty
            : typeNames[type.Id];
        return string.Concat(register, reference, name);
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
