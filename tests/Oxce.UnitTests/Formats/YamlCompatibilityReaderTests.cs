using Oxce.Formats.Yaml;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class YamlCompatibilityReaderTests
{
    [Fact]
    public void ParsePreservesDocumentsNullsDuplicatesAndResolvedMerges()
    {
        const string yaml = """
            defaults: &defaults
              enabled: true
              optional: null
              quotedNull: "null"
            item:
              <<: *defaults
              enabled: false
            duplicate: first
            duplicate: second
            ---
            name: Commander
            """;

        var documents = YamlCompatibilityReader.Parse(yaml, "semantics.yml");

        Assert.Equal(2, documents.Documents.Count);
        var first = Assert.IsType<YamlMappingNode>(documents.Documents[0].Root);
        var item = Assert.IsType<YamlMappingNode>(Required(first, "item"));
        Assert.False(YamlValueReader.ReadBoolean(Required(item, "enabled")));
        Assert.IsType<YamlNullNode>(Required(item, "optional"));
        Assert.Equal("null", YamlValueReader.ReadString(Required(item, "quotedNull")));
        Assert.Equal(
            ["first", "second"],
            first.GetAll("duplicate").Select(YamlValueReader.ReadString));
        Assert.Equal("first", YamlValueReader.ReadString(Required(first, "duplicate")));
    }

    [Fact]
    public void ParseReportsOneBasedSourceLocationForMalformedInput()
    {
        const string yaml = "items:\n  - type: STR_RIFLE\n    cost: [100, 200\n";

        var exception = Assert.Throws<YamlFormatException>(
            () => YamlCompatibilityReader.Parse(yaml, "broken.rul"));

        Assert.Equal("broken.rul", exception.Span.SourceName);
        Assert.True(exception.Span.Start.Line >= 3);
        Assert.True(exception.Span.Start.Column >= 1);
    }

    [Theory]
    [InlineData("~")]
    [InlineData("null")]
    [InlineData("Null")]
    [InlineData("NULL")]
    public void ParseRecognizesReferenceNullSpellings(string spelling)
    {
        var documents = YamlCompatibilityReader.Parse($"value: {spelling}\n", "null.yml");
        var mapping = Assert.IsType<YamlMappingNode>(documents.Documents[0].Root);

        Assert.IsType<YamlNullNode>(Required(mapping, "value"));
    }

    [Fact]
    public void ParseDoesNotTreatQuotedNullAsNull()
    {
        var documents = YamlCompatibilityReader.Parse("value: \"null\"\n", "quoted.yml");
        var mapping = Assert.IsType<YamlMappingNode>(documents.Documents[0].Root);

        Assert.IsType<YamlScalarNode>(Required(mapping, "value"));
    }

    [Fact]
    public void ParseEnforcesResourceLimits()
    {
        Assert.Throws<YamlFormatException>(() => YamlCompatibilityReader.Parse(
            "value: too-large\n",
            "bytes.yml",
            new YamlReadOptions { MaxBytes = 4 }));
        Assert.Throws<YamlFormatException>(() => YamlCompatibilityReader.Parse(
            "a: { b: 1 }\n",
            "depth.yml",
            new YamlReadOptions { MaxDepth = 2 }));
        Assert.Throws<YamlFormatException>(() => YamlCompatibilityReader.Parse(
            "a: b\n",
            "nodes.yml",
            new YamlReadOptions { MaxNodes = 2 }));
        Assert.Throws<YamlFormatException>(() => YamlCompatibilityReader.Parse(
            "a: 1\n---\nb: 2\n",
            "documents.yml",
            new YamlReadOptions { MaxDocuments = 1 }));
        Assert.Throws<YamlFormatException>(() => YamlCompatibilityReader.Parse(
            "a: &a 1\nb: *a\n",
            "aliases.yml",
            new YamlReadOptions { MaxAliases = 0 }));
    }

    [Fact]
    public void ValueReaderDistinguishesMissingDefaultsFromExplicitNull()
    {
        var documents = YamlCompatibilityReader.Parse("count: 7\noptional: null\n", "values.yml");
        var mapping = Assert.IsType<YamlMappingNode>(documents.Documents[0].Root);

        Assert.Equal(7, YamlValueReader.ReadInt32(mapping, "count", 42));
        Assert.Equal(42, YamlValueReader.ReadInt32(mapping, "missing", 42));
        Assert.True(YamlValueReader.IsExplicitNull(Required(mapping, "optional")));
        Assert.Throws<YamlFormatException>(() => YamlValueReader.ReadInt32(Required(mapping, "optional")));
    }

    private static YamlNode Required(YamlMappingNode mapping, string key)
    {
        Assert.True(mapping.TryGet(key, out var value));
        return Assert.IsAssignableFrom<YamlNode>(value);
    }
}
