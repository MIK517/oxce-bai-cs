using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Events;
using Oxce.Scripting.Runtime;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class ScriptEventFixtureTests
{
    [Fact]
    public void ManagedEventCompositionMatchesCapturedReferenceOutcomes()
    {
        var cases = ReadCases();
        Assert.Equal(5, cases.Length);

        AssertCase(cases, "ordered-update", accepted: true, result: 16);
        AssertCase(cases, "unknown-update-delete", accepted: true, result: 2);
        AssertCase(cases, "unknown-override", accepted: false, result: 1);
        AssertCase(cases, "delete-existing", accepted: true, result: 2);
        AssertCase(cases, "ignore-and-zero-offset", accepted: true, result: 2);

        var ordered = ScriptEventComposer.Compose(
        [
            Event(ScriptEventMutationKind.New, "early", -200, "add result 1; return result;"),
            Event(ScriptEventMutationKind.New, "late", 100, "add result 10; return result;"),
            Event(ScriptEventMutationKind.Update, "early", -100, "add result 2; return result;"),
        ]);
        var execution = ScriptEventRunner.Execute(
            ordered.Plan!,
            Program("mul result 2; return result;"),
            new Dictionary<string, int> { ["result"] = 1 });
        Assert.Equal(Value(cases, "ordered-update"), execution.Outputs["result"]);
        var frame = new ScriptExecutionFrame();
        var directInitial = new[] { ScriptRuntimeValue.FromScalar(1) };
        var directOutput = new ScriptRuntimeValue[1];
        var direct = ScriptEventRunner.Execute(
            ordered.Plan!,
            Program("mul result 2; return result;"),
            directInitial,
            directOutput,
            frame);
        Assert.True(direct.Succeeded);
        Assert.Equal(Value(cases, "ordered-update"), directOutput[0].Scalar);

        var unknownOverride = ScriptEventComposer.Compose(
            [Event(ScriptEventMutationKind.Override, "missing", 100, "return result;")]);
        Assert.Equal(Accepted(cases, "unknown-override"), unknownOverride.Accepted);

        var zeroOffset = ScriptEventComposer.Compose(
            [Event(ScriptEventMutationKind.New, "zero", 0, "add result 1000; return result;")]);
        Assert.Equal(Accepted(cases, "ignore-and-zero-offset"), zeroOffset.Accepted);
        Assert.False(zeroOffset.Succeeded);
    }

    private static ScriptEventMutation Event(
        ScriptEventMutationKind kind,
        string name,
        int offset,
        string source) => new(kind, name, offset, Program(source), "probe", 1);

    private static ScriptProgram Program(string source) =>
        ScriptCompiler.Compile(source, new ScriptParserDefinition("Probe", ["result"])).Program!;

    private static JsonElement[] ReadCases()
    {
        var root = FindRepositoryRoot();
        var manifest = FixtureManifestLoader.Load(Path.Combine(root, "fixtures", "manifests", "script-events.json"));
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root)));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(static item => item.Clone()).ToArray();
    }

    private static void AssertCase(JsonElement[] cases, string name, bool accepted, int result)
    {
        Assert.Equal(accepted, Accepted(cases, name));
        Assert.Equal(result, Value(cases, name));
    }

    private static bool Accepted(JsonElement[] cases, string name) =>
        Assert.Single(cases, item => item.GetProperty("name").GetString() == name)
            .GetProperty("accepted").GetBoolean();

    private static int Value(JsonElement[] cases, string name) =>
        Assert.Single(cases, item => item.GetProperty("name").GetString() == name)
            .GetProperty("result").GetInt32();

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
