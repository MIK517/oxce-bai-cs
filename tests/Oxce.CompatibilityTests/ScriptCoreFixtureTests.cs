using System.Text.Json;
using Oxce.FixtureSupport;
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

        Assert.Equal(9, cases.Length);
        AssertCase(cases, "return-only", compiled: true, result: 7);
        AssertCase(cases, "set-output", compiled: true, result: 42);
        AssertCase(cases, "locals-and-arithmetic", compiled: true, result: 42);
        AssertCase(cases, "conditional", compiled: true, result: 11);
        AssertCase(cases, "comment", compiled: true, result: 15);
        AssertRejected(cases, "missing-return", "need to end with return statement");
        AssertRejected(cases, "unknown-operation", "Invalid operation");
        AssertRejected(cases, "unreachable", "unreachable code after return");
        AssertRejected(cases, "invalid-number", "invalid argument");
    }

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
