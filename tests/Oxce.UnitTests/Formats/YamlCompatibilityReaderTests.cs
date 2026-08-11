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

    [Theory]
    [InlineData("256", 0)]
    [InlineData("2147483648", 0)]
    [InlineData("0b101", 5)]
    [InlineData("0o17", 15)]
    [InlineData("0x10", 16)]
    [InlineData("+7", 7)]
    public void IntegerConversionsMatchReferenceWrapping(string text, sbyte expected)
    {
        var node = Scalar(text);

        Assert.Equal(expected, YamlValueReader.ReadInt8(node));
    }

    [Fact]
    public void IntegerConversionsWrapAtEveryDestinationWidth()
    {
        var intBoundary = Scalar("2147483648");
        var ulongBoundary = Scalar("18446744073709551616");

        Assert.Equal(int.MinValue, YamlValueReader.ReadInt32(intBoundary));
        Assert.Equal(2147483648U, YamlValueReader.ReadUInt32(intBoundary));
        Assert.Equal(0UL, YamlValueReader.ReadUInt64(ulongBoundary));
        Assert.False(YamlValueReader.TryReadUInt32(Scalar("-1"), out _));
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    [InlineData("2", true)]
    [InlineData("4294967296", false)]
    public void BooleanConversionsMatchReferenceSpellingsAndNumericFallback(string text, bool expected)
    {
        Assert.Equal(expected, YamlValueReader.ReadBoolean(Scalar(text)));
    }

    [Fact]
    public void FloatingConversionsSupportSpecialValuesAndNumericPrefixes()
    {
        Assert.True(float.IsNaN(YamlValueReader.ReadSingle(Scalar(".NaN"))));
        Assert.Equal(float.PositiveInfinity, YamlValueReader.ReadSingle(Scalar(".Inf")));
        Assert.Equal(double.NegativeInfinity, YamlValueReader.ReadDouble(Scalar("-.INF")));
        Assert.Equal(34D, YamlValueReader.ReadDouble(Scalar("34junk")));
    }

    [Fact]
    public void StringConversionReturnsExplicitNullSpellingLikeReferenceReader()
    {
        var documents = YamlCompatibilityReader.Parse("value: null\n", "null-string.yml");
        var mapping = Assert.IsType<YamlMappingNode>(documents.Documents[0].Root);

        Assert.Equal("null", YamlValueReader.ReadString(Required(mapping, "value")));
    }

    [Fact]
    public void EnumConversionUsesWrappedInt32WithoutRejectingUnknownValues()
    {
        Assert.Equal(ProbeEnum.Known, YamlValueReader.ReadEnum<ProbeEnum>(Scalar("1")));
        Assert.Equal((ProbeEnum)(-1), YamlValueReader.ReadEnum<ProbeEnum>(Scalar("4294967295")));
        Assert.False(YamlValueReader.TryReadEnum(Scalar("named"), out ProbeEnum _));
    }

    [Fact]
    public void SequenceConversionMatchesReferenceChildIteration()
    {
        var document = ParseMapping("""
            sequence: [1, 2]
            mapping: {first: 3, second: 4}
            scalar: ignored
            """);

        Assert.Equal(
            [1, 2],
            YamlValueReader.ReadSequence(Required(document, "sequence"), YamlValueReader.ReadInt32));
        Assert.Equal(
            [3, 4],
            YamlValueReader.ReadSequence(Required(document, "mapping"), YamlValueReader.ReadInt32));
        Assert.Empty(YamlValueReader.ReadSequence(Required(document, "scalar"), YamlValueReader.ReadInt32));
    }

    [Fact]
    public void MapConversionSortsKeysAndKeepsFirstDuplicate()
    {
        var document = ParseMapping("""
            values:
              zeta: 1
              alpha: 2
              alpha: 3
            """);

        var values = YamlValueReader.ReadMap(
            Required(document, "values"),
            YamlValueReader.ReadString,
            YamlValueReader.ReadInt32,
            StringComparer.Ordinal);

        Assert.Equal(["alpha", "zeta"], values.Keys);
        Assert.Equal(2, values["alpha"]);
        Assert.Equal(1, values["zeta"]);
    }

    [Fact]
    public void FixedArityContainersRequireSequencesOfExactLength()
    {
        var pair = Assert.IsType<YamlSequenceNode>(YamlCompatibilityReader.Parse("[1, true]\n", "pair.yml")
            .Documents[0].Root);
        var triple = Assert.IsType<YamlSequenceNode>(YamlCompatibilityReader.Parse("[1, true, text]\n", "tuple.yml")
            .Documents[0].Root);

        Assert.Equal((1, true), YamlValueReader.ReadPair(
            pair,
            YamlValueReader.ReadInt32,
            YamlValueReader.ReadBoolean));
        Assert.Equal((1, true, "text"), YamlValueReader.ReadTuple(
            triple,
            YamlValueReader.ReadInt32,
            YamlValueReader.ReadBoolean,
            YamlValueReader.ReadString));
        Assert.Equal(
            [1, 1, 0],
            YamlValueReader.ReadFixedArray(triple, 3, node =>
                YamlValueReader.TryReadBoolean(node, out var value) && value ? 1 : 0));
        Assert.Throws<YamlFormatException>(() => YamlValueReader.ReadPair(
            triple,
            YamlValueReader.ReadInt32,
            YamlValueReader.ReadInt32));
        Assert.Throws<YamlFormatException>(() => YamlValueReader.ReadFixedArray(
            pair,
            3,
            YamlValueReader.ReadString));
    }

    private static YamlNode Scalar(string value)
    {
        var documents = YamlCompatibilityReader.Parse($"value: '{value}'\n", "scalar.yml");
        var mapping = Assert.IsType<YamlMappingNode>(documents.Documents[0].Root);
        return Required(mapping, "value");
    }

    private static YamlMappingNode ParseMapping(string yaml) =>
        Assert.IsType<YamlMappingNode>(YamlCompatibilityReader.Parse(yaml, "mapping.yml").Documents[0].Root);

    private static YamlNode Required(YamlMappingNode mapping, string key)
    {
        Assert.True(mapping.TryGet(key, out var value));
        return Assert.IsAssignableFrom<YamlNode>(value);
    }

    private enum ProbeEnum
    {
        Known = 1,
    }
}
