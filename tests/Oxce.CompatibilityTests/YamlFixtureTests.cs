using System.Text.Json;
using System.Globalization;
using Oxce.FixtureSupport;
using Oxce.Formats.Yaml;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class YamlFixtureTests
{
    private static readonly string[] BooleanColumns = ["name", "value"];
    private static readonly string[] FloatingColumns = ["name", "float", "double"];
    private static readonly string[] EnumColumns = ["name", "success", "value"];
    private static readonly string[] Base64Columns = ["name", "valid", "hex"];
    private static readonly string[] HexFloatingColumns = ["name", "float", "double"];
    private static readonly string[] IntegerColumns =
        ["name", "int8", "uint8", "int32", "uint32", "int64", "uint64"];

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

    [Fact]
    public void ScalarConversionsMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "yaml-scalar-conversions.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var documents = YamlCompatibilityReader.ParseFile(fixturePath);
        var document = Assert.IsType<YamlMappingNode>(documents.Documents[0].Root);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            booleanColumns = BooleanColumns,
            booleans = Cases(document, "booleans")
                .Select(testCase => new object?[]
                {
                    YamlValueReader.ReadString(Required(testCase, "name")),
                    YamlValueReader.TryReadBoolean(Required(testCase, "value"), out var value) ? value : null,
                })
                .ToArray(),
            floating = Cases(document, "floating")
                .Select(testCase => new object?[]
                {
                    YamlValueReader.ReadString(Required(testCase, "name")),
                    YamlValueReader.TryReadSingle(Required(testCase, "value"), out var single)
                        ? FloatingText(single)
                        : null,
                    YamlValueReader.TryReadDouble(Required(testCase, "value"), out var doubleValue)
                        ? FloatingText(doubleValue)
                        : null,
                })
                .ToArray(),
            floatingColumns = FloatingColumns,
            integerColumns = IntegerColumns,
            integers = Cases(document, "integers").Select(ProjectIntegers).ToArray(),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    [Fact]
    public void ContainerConversionsMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "yaml-container-conversions.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var document = Assert.IsType<YamlMappingNode>(
            YamlCompatibilityReader.ParseFile(fixturePath).Documents[0].Root);
        var sequences = Assert.IsType<YamlMappingNode>(Required(document, "sequences"));
        var maps = Assert.IsType<YamlMappingNode>(Required(document, "maps"));

        var pair = YamlValueReader.ReadPair(
            Required(document, "pair"),
            YamlValueReader.ReadInt32,
            YamlValueReader.ReadInt32);
        var tuple = YamlValueReader.ReadTuple(
            Required(document, "tuple"),
            YamlValueReader.ReadInt32,
            YamlValueReader.ReadBoolean,
            YamlValueReader.ReadString);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            array = YamlValueReader.ReadFixedArray(
                Required(document, "array"), 3, YamlValueReader.ReadInt32).Select(IntegerText).ToArray(),
            enumColumns = EnumColumns,
            enums = Cases(document, "enums").Select(ProjectEnum).ToArray(),
            maps = new
            {
                duplicate = ProjectMap(YamlValueReader.ReadMap(
                    Required(maps, "duplicate"),
                    YamlValueReader.ReadString,
                    YamlValueReader.ReadInt32,
                    StringComparer.Ordinal)),
                integerString = ProjectMap(YamlValueReader.ReadMap(
                    Required(maps, "integerString"),
                    YamlValueReader.ReadInt32,
                    YamlValueReader.ReadString)),
                stringInteger = ProjectMap(YamlValueReader.ReadMap(
                    Required(maps, "stringInteger"),
                    YamlValueReader.ReadString,
                    YamlValueReader.ReadInt32,
                    StringComparer.Ordinal)),
            },
            pair = new[] { IntegerText(pair.First), IntegerText(pair.Second) },
            sequences = new
            {
                booleans = YamlValueReader.ReadSequence(
                    Required(sequences, "booleans"), YamlValueReader.ReadBoolean),
                integers = YamlValueReader.ReadSequence(
                    Required(sequences, "integers"), YamlValueReader.ReadInt32).Select(IntegerText).ToArray(),
                mappingValues = YamlValueReader.ReadSequence(
                    Required(sequences, "mappingValues"), YamlValueReader.ReadInt32).Select(IntegerText).ToArray(),
                scalar = YamlValueReader.ReadSequence(
                    Required(sequences, "scalar"), YamlValueReader.ReadInt32).Select(IntegerText).ToArray(),
            },
            tuple = new object[] { IntegerText(tuple.First), tuple.Second, tuple.Third },
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    [Fact]
    public void RepresentativeStructuresNormalizeLikeCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "yaml-representative-normalization.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var documents = YamlCompatibilityReader.ParseFile(fixturePath);

        var actual = YamlSemanticNormalizer.NormalizeToUtf8Json(documents);
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    [Fact]
    public void SpecialScalarConversionsMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "yaml-special-scalar-conversions.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var document = Assert.IsType<YamlMappingNode>(
            YamlCompatibilityReader.ParseFile(fixturePath).Documents[0].Root);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            base64 = Cases(document, "base64").Select(ProjectBase64).ToArray(),
            base64Columns = Base64Columns,
            hexFloating = Cases(document, "hexFloating").Select(testCase =>
            {
                var value = Required(testCase, "value");
                return new object?[]
                {
                    YamlValueReader.ReadString(Required(testCase, "name")),
                    YamlValueReader.TryReadSingle(value, out var single) ? FloatingText(single) : null,
                    YamlValueReader.TryReadDouble(value, out var doubleValue) ? FloatingText(doubleValue) : null,
                };
            }).ToArray(),
            hexFloatingColumns = HexFloatingColumns,
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    private static object?[] ProjectBase64(YamlMappingNode testCase)
    {
        var node = Required(testCase, "value");
        try
        {
            var bytes = YamlValueReader.ReadBase64(node);
            return
            [
                YamlValueReader.ReadString(Required(testCase, "name")),
                true,
                Convert.ToHexStringLower(bytes),
            ];
        }
        catch (YamlFormatException)
        {
            return
            [
                YamlValueReader.ReadString(Required(testCase, "name")),
                false,
                null,
            ];
        }
    }

    private static object?[] ProjectEnum(YamlMappingNode testCase)
    {
        var success = YamlValueReader.TryReadEnum(Required(testCase, "value"), out ProbeEnum value);
        return
        [
            YamlValueReader.ReadString(Required(testCase, "name")),
            success,
            success ? IntegerText((int)value) : null,
        ];
    }

    private static string[][] ProjectMap<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> values)
        where TKey : IFormattable
        where TValue : IFormattable => values
            .Select(pair => new[] { IntegerText(pair.Key), IntegerText(pair.Value) })
            .ToArray();

    private static string[][] ProjectMap(IEnumerable<KeyValuePair<string, int>> values) => values
        .Select(pair => new[] { pair.Key, IntegerText(pair.Value) })
        .ToArray();

    private static string[][] ProjectMap(IEnumerable<KeyValuePair<int, string>> values) => values
        .Select(pair => new[] { IntegerText(pair.Key), pair.Value })
        .ToArray();

    private static object?[] ProjectIntegers(YamlMappingNode testCase)
    {
        var node = Required(testCase, "value");
        return
        [
            YamlValueReader.ReadString(Required(testCase, "name")),
            YamlValueReader.TryReadInt8(node, out var int8) ? IntegerText(int8) : null,
            YamlValueReader.TryReadUInt8(node, out var uint8) ? IntegerText(uint8) : null,
            YamlValueReader.TryReadInt32(node, out var int32) ? IntegerText(int32) : null,
            YamlValueReader.TryReadUInt32(node, out var uint32) ? IntegerText(uint32) : null,
            YamlValueReader.TryReadInt64(node, out var int64) ? IntegerText(int64) : null,
            YamlValueReader.TryReadUInt64(node, out var uint64) ? IntegerText(uint64) : null,
        ];
    }

    private static IEnumerable<YamlMappingNode> Cases(YamlMappingNode document, string key) =>
        Assert.IsType<YamlSequenceNode>(Required(document, key)).Items
            .Select(Assert.IsType<YamlMappingNode>);

    private static string IntegerText<T>(T value)
        where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);

    private static string FloatingText(float value) => value switch
    {
        _ when float.IsNaN(value) => ".nan",
        float.PositiveInfinity => ".inf",
        float.NegativeInfinity => "-.inf",
        _ => value.ToString("G9", CultureInfo.InvariantCulture),
    };

    private static string FloatingText(double value) => value switch
    {
        _ when double.IsNaN(value) => ".nan",
        double.PositiveInfinity => ".inf",
        double.NegativeInfinity => "-.inf",
        _ => value.ToString("G17", CultureInfo.InvariantCulture),
    };

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

    private enum ProbeEnum
    {
        Zero = 0,
    }
}
