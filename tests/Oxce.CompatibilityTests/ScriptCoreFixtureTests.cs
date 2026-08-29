using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Scripting.Lexing;
using Oxce.Scripting.Syntax;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class ScriptCoreFixtureTests
{
    [Fact]
    public void CapturedReferenceCasesProvideCompilerAndExecutionOracle()
    {
        var root = FindRepositoryRoot();
        var manifest = FixtureManifestLoader.Load(
            Path.Combine(root, "fixtures", "manifests", "script-core.json"));
        FixtureManifestVerifier.VerifyFiles(manifest, root);

        using var document = JsonDocument.Parse(
            File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root)));
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();

        Assert.Equal(21, cases.Length);
        AssertCase(cases, "return-only", compiled: true, result: 7);
        AssertCase(cases, "set-output", compiled: true, result: 42);
        AssertCase(cases, "locals-and-arithmetic", compiled: true, result: 42);
        AssertCase(cases, "conditional", compiled: true, result: 11);
        AssertCase(cases, "comment", compiled: true, result: 15);
        AssertRejected(cases, "forward-label", "invalid operation");
        AssertCase(cases, "counted-loop", compiled: true, result: 6);
        AssertCase(cases, "nested-scope", compiled: true, result: 5);
        AssertRejected(cases, "missing-return", "need to end with return statement");
        AssertRejected(cases, "unknown-operation", "Invalid operation");
        AssertRejected(cases, "unreachable", "unreachable code after return");
        AssertRejected(cases, "invalid-number", "invalid argument");
        AssertRejected(cases, "missing-semicolon", "invalid line");
        AssertRejected(cases, "invalid-punctuation", "invalid argument");
        AssertRejected(cases, "invalid-text-escape", "invalid argument");
        AssertCase(cases, "integer-overflow", compiled: true, result: int.MinValue);
        AssertRejected(cases, "break-outside-loop", "outside 'loop'");
        AssertRejected(cases, "variable-after-operation", "invalid variable definition");
        AssertRejected(cases, "too-many-arguments", "invalid line");
        AssertRejected(cases, "unclosed-block", "missed 'end;'");
        AssertRejected(cases, "duplicate-label", "invalid label");
    }

    [Fact]
    public void ManagedFrontEndMatchesCapturedLexicalOutcomes()
    {
        var cases = ReadCases();

        Assert.True(Parse(cases, "comment").IsValid);
        Assert.True(Parse(cases, "integer-overflow").IsValid);
        Assert.False(Parse(cases, "invalid-number").IsValid);
        Assert.False(Parse(cases, "missing-semicolon").IsValid);
        Assert.False(Parse(cases, "invalid-punctuation").IsValid);
        Assert.False(Parse(cases, "invalid-text-escape").IsValid);
        Assert.False(Parse(cases, "too-many-arguments").IsValid);

        var overflow = ScriptLexer.Tokenize(Source(cases, "integer-overflow"));
        Assert.Contains(overflow.Tokens,
            token => token.Kind == ScriptTokenKind.Numeric && token.NumericValue == int.MinValue);
    }

    private static JsonElement[] ReadCases()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "fixtures", "expected", "scripting", "script-core.expected.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static ScriptSyntaxTree Parse(JsonElement[] cases, string name) =>
        ScriptSyntaxParser.Parse(Source(cases, name), name);

    private static string Source(JsonElement[] cases, string name) =>
        Assert.Single(cases, value => value.GetProperty("name").GetString() == name)
            .GetProperty("source").GetString()!;

    private static void AssertCase(JsonElement[] cases, string name, bool compiled, int result)
    {
        var item = Assert.Single(cases, value => value.GetProperty("name").GetString() == name);
        Assert.Equal(compiled, item.GetProperty("compiled").GetBoolean());
        Assert.Equal(result, item.GetProperty("result").GetInt32());
        Assert.Empty(item.GetProperty("diagnostics").EnumerateArray());
    }

    private static void AssertRejected(JsonElement[] cases, string name, string message)
    {
        var item = Assert.Single(cases, value => value.GetProperty("name").GetString() == name);
        Assert.False(item.GetProperty("compiled").GetBoolean());
        Assert.Contains(item.GetProperty("diagnostics").EnumerateArray(),
            value => value.GetString()!.Contains(message, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
