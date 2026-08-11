using Oxce.Formats.Yaml;
using Xunit;

namespace Oxce.UnitTests.Formats;

public sealed class YamlCompatibilityWriterTests
{
    [Fact]
    public void EmitIsDeterministicAndRoundTripsMultipleDocuments()
    {
        const string yaml = """
            defaults: &defaults
              enabled: true
              label: "null"
            copy: *defaults
            emptyMap: {}
            emptySequence: []
            ---
            name: Commander
            """;
        var documents = YamlCompatibilityReader.Parse(yaml, "roundtrip.yml");

        var first = YamlCompatibilityWriter.Emit(documents);
        var second = YamlCompatibilityWriter.Emit(documents);
        var reparsed = YamlCompatibilityReader.Parse(first, "emitted.yml");

        Assert.Equal(first, second);
        Assert.Equal(2, reparsed.Documents.Count);
        Assert.Contains("---\n", first, StringComparison.Ordinal);
        var root = Assert.IsType<YamlMappingNode>(reparsed.Documents[0].Root);
        var defaults = Assert.IsType<YamlMappingNode>(Required(root, "defaults"));
        var copy = Assert.IsType<YamlMappingNode>(Required(root, "copy"));
        Assert.Same(defaults, copy);
        Assert.Equal("null", YamlValueReader.ReadString(Required(defaults, "label")));
    }

    [Fact]
    public void EmitQuotesAmbiguousAndControlScalars()
    {
        const string yaml = "plain: value\nambiguous: \"null\"\ncontrol: \"line\\nfeed\"\n";
        var documents = YamlCompatibilityReader.Parse(yaml, "quotes.yml");

        var emitted = YamlCompatibilityWriter.Emit(documents);

        Assert.Contains("plain: value\n", emitted, StringComparison.Ordinal);
        Assert.Contains("ambiguous: \"null\"\n", emitted, StringComparison.Ordinal);
        Assert.Contains("control: \"line\\nfeed\"\n", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitEnforcesByteAndDepthLimits()
    {
        var flat = YamlCompatibilityReader.Parse("value: abc\n", "flat.yml");
        var nested = YamlCompatibilityReader.Parse("outer:\n  inner:\n    value: 1\n", "nested.yml");

        Assert.Throws<YamlFormatException>(() => YamlCompatibilityWriter.Emit(
            flat,
            new YamlWriteOptions { MaxBytes = 4 }));
        Assert.Throws<YamlFormatException>(() => YamlCompatibilityWriter.Emit(
            nested,
            new YamlWriteOptions { MaxDepth = 2 }));
    }

    private static YamlNode Required(YamlMappingNode mapping, string key)
    {
        Assert.True(mapping.TryGet(key, out var value));
        return Assert.IsAssignableFrom<YamlNode>(value);
    }
}
