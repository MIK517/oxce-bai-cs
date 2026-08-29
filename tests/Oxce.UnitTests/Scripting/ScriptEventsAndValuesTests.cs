using Oxce.Core.Diagnostics;
using Oxce.Scripting;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Events;
using Oxce.Scripting.Globals;
using Oxce.Scripting.Runtime;
using Xunit;

namespace Oxce.UnitTests.Scripting;

public sealed class ScriptEventsAndValuesTests
{
    [Fact]
    public void EventsComposeByOffsetAroundCurrentAndUpdatesPreservePosition()
    {
        var composed = ScriptEventComposer.Compose(
        [
            Event(ScriptEventMutationKind.New, "early", -200, "add result 1; return result;", 1),
            Event(ScriptEventMutationKind.New, "late", 100, "add result 10; return result;", 2),
            Event(ScriptEventMutationKind.Update, "early", -100, "add result 2; return result;", 3),
            Event(ScriptEventMutationKind.Ignore, "ignored", -50, "add result 100; return result;", 4),
        ]);
        Assert.True(composed.Succeeded);

        var result = ScriptEventRunner.Execute(
            composed.Plan!,
            Program("mul result 2; return result;"),
            new Dictionary<string, int> { ["result"] = 1 });

        Assert.Equal(ScriptExecutionStatus.Completed, result.Status);
        Assert.Equal(16, result.Outputs["result"]);
        Assert.Equal(3, result.Executions.Count);
    }

    [Fact]
    public void UnknownUpdateAndDeleteWarnWhileUnknownOverrideFails()
    {
        var warnings = ScriptEventComposer.Compose(
        [
            Event(ScriptEventMutationKind.Update, "missing", 100, "return result;", 1),
            new ScriptEventMutation(ScriptEventMutationKind.Delete, "alsoMissing", 0, null, "probe.yml", 2),
        ]);
        Assert.True(warnings.Succeeded);
        Assert.Equal(2, warnings.Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));

        var failure = ScriptEventComposer.Compose(
            [Event(ScriptEventMutationKind.Override, "missing", 100, "return result;", 3)]);
        Assert.False(failure.Succeeded);
        Assert.Contains(failure.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.InvalidEventDefinition);
    }

    [Fact]
    public void EventExecutionCountIsBoundedBeforeMutation()
    {
        var composed = ScriptEventComposer.Compose(
            [Event(ScriptEventMutationKind.Append, "", -100, "return result;", 1)]);

        var result = ScriptEventRunner.Execute(
            composed.Plan!,
            Program("return result;"),
            maximumEventExecutions: 1);

        Assert.Equal(ScriptExecutionStatus.ExecutionLimit, result.Status);
        Assert.Empty(result.Executions);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.EventLimitExceeded);
    }

    [Fact]
    public void EventCompositionTruncatesAtReferencePayloadLimit()
    {
        var program = Program("return result;");
        var mutations = Enumerable.Range(1, ScriptLimits.MaximumGlobalEvents - 1)
            .Select(index => new ScriptEventMutation(
                ScriptEventMutationKind.Append,
                string.Empty,
                index,
                program,
                "probe.yml",
                index));

        var composed = ScriptEventComposer.Compose(mutations);

        Assert.True(composed.Accepted);
        Assert.False(composed.Succeeded);
        Assert.Equal(ScriptLimits.MaximumGlobalEvents - 2, composed.Plan!.Count);
    }

    [Fact]
    public void TagsAreOneBasedGloballyNamedAndValuesCaptureOnlyNonZeroSlots()
    {
        var (catalog, unitType) = CreateTags();
        Assert.True(catalog.TryGetTag(unitType, "Tag.score", out var score));
        Assert.Equal(1, score!.Index);

        var state = new ScriptValueState(catalog, unitType);
        state.Set("score", 42);
        state.Set("enabled", 0);

        var entry = Assert.Single(state.Capture());
        Assert.Equal(new ScriptValueEntry("score", "int", 42), entry);
        Assert.Equal(0, state.Get("missing"));
    }

    [Fact]
    public void ScriptValueRestoreIsTransactional()
    {
        var (catalog, unitType) = CreateTags();

        var restored = ScriptValueState.TryRestore(
            catalog,
            unitType,
            [new ScriptValueEntry("score", "RuleList", 2)],
            out var state,
            out var diagnostics);

        Assert.False(restored);
        Assert.Null(state);
        Assert.Contains(diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.InvalidScriptValueState);
    }

    private static ScriptEventMutation Event(
        ScriptEventMutationKind kind,
        string name,
        int offset,
        string source,
        int line) => new(kind, name, offset, Program(source), "probe.yml", line);

    private static ScriptProgram Program(string source)
    {
        var result = ScriptCompiler.Compile(source, new ScriptParserDefinition("Probe", ["result"]));
        Assert.True(result.Succeeded);
        return result.Program!;
    }

    private static (ScriptTagCatalog Catalog, ScriptTagTypeId UnitType) CreateTags()
    {
        var unitType = new ScriptTagTypeId(1);
        var builder = new ScriptTagCatalogBuilder();
        builder.AddType(new ScriptTagTypeDefinition(unitType, "BattleUnit", ushort.MaxValue));
        builder.AddValueType("RuleList");
        builder.AddTag(unitType, "score", "int", "mod-a.rul");
        builder.AddTag(unitType, "enabled", "int", "mod-b.rul");
        return (builder.Build(), unitType);
    }
}
