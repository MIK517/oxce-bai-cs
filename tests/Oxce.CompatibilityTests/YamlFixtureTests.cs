using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Yaml;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class YamlFixtureTests
{
    [Fact]
    public void YamlSemanticsMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "yaml-reference-semantics.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var documents = YamlCompatibilityReader.ParseFile(fixturePath);

        var first = Assert.IsType<YamlMappingNode>(documents.Documents[0].Root);
        var item = Assert.IsType<YamlMappingNode>(Required(first, "item"));
        var second = Assert.IsType<YamlMappingNode>(documents.Documents[1].Root);
        var sequence = Assert.IsType<YamlSequenceNode>(Required(first, "sequence"));
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            documentCount = documents.Documents.Count,
            duplicateLookup = YamlValueReader.ReadString(Required(first, "duplicate")),
            duplicateValues = first.GetAll("duplicate").Select(YamlValueReader.ReadString).ToArray(),
            item = new
            {
                count = YamlValueReader.ReadString(Required(item, "count")),
                enabled = YamlValueReader.ReadString(Required(item, "enabled")),
                optionalIsNull = YamlValueReader.IsExplicitNull(Required(item, "optional")),
                quotedNullIsNull = YamlValueReader.IsExplicitNull(Required(item, "quotedNull")),
            },
            secondDocument = new
            {
                ironman = YamlValueReader.ReadString(Required(second, "ironman")),
                name = YamlValueReader.ReadString(Required(second, "name")),
            },
            sequenceNulls = sequence.Items.Select(YamlValueReader.IsExplicitNull).ToArray(),
        });
        var expected = File.ReadAllText(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(expected, CanonicalJson.Normalize(actual));
    }

    [Fact]
    public void MalformedFixtureReportsItsSourceLocation()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "fixtures", "public", "yaml", "malformed-location.yml");

        var exception = Assert.Throws<YamlFormatException>(() => YamlCompatibilityReader.ParseFile(path));

        Assert.Equal(Path.GetFullPath(path), exception.Span.SourceName);
        Assert.True(exception.Span.Start.Line >= 5);
    }

    private static YamlNode Required(YamlMappingNode mapping, string key)
    {
        Assert.True(mapping.TryGet(key, out var value));
        return Assert.IsAssignableFrom<YamlNode>(value);
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
