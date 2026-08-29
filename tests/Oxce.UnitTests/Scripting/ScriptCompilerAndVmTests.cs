using Oxce.Scripting.Compilation;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Runtime;
using Xunit;

namespace Oxce.UnitTests.Scripting;

public sealed class ScriptCompilerAndVmTests
{
    [Fact]
    public void ConditionalElseIfChainsSelectFirstMatchingBranch()
    {
        const string source =
            "if eq result 1; set result 10; else eq result 2; set result 20; " +
            "else; set result 30; end; return result;";

        var result = Execute(source, new Dictionary<string, int> { ["result"] = 2 });

        Assert.Equal(ScriptExecutionStatus.Completed, result.Status);
        Assert.Equal(20, result.Outputs["result"]);
    }

    [Fact]
    public void ReturnReadsAllValuesBeforeWritingOutputs()
    {
        var definition = new ScriptParserDefinition("SwapOutputs", ["left", "right"]);
        var compiled = ScriptCompiler.Compile("return right left;", definition);
        Assert.True(compiled.Succeeded);

        var result = ScriptVm.Execute(compiled.Program!, new Dictionary<string, int>
        {
            ["left"] = 1,
            ["right"] = 2,
        });

        Assert.Equal(2, result.Outputs["left"]);
        Assert.Equal(1, result.Outputs["right"]);
    }

    [Fact]
    public void NestedBlockRegistersReuseStorageButPreservePeakSize()
    {
        const string source =
            "begin; var int first 1; set result first; end; " +
            "begin; var int second 2; set result second; end; return result;";

        var compiled = Compile(source);

        Assert.Equal(8, compiled.Program!.RegisterBytes);
        var locals = compiled.Program.Registers.Where(static register => !register.IsOutput).ToArray();
        Assert.Equal(2, locals.Length);
        Assert.Equal(locals[0].Offset, locals[1].Offset);
    }

    [Fact]
    public void CompilerStopsAtConfiguredInstructionLimit()
    {
        var compiled = ScriptCompiler.Compile(
            "set result 1; return result;",
            Definition,
            options: new ScriptCompilerOptions { MaximumInstructions = 1 });

        Assert.False(compiled.Succeeded);
        Assert.Contains(compiled.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.ProgramLimitExceeded);
    }

    [Fact]
    public void ExecutionLimitStopsLongRunningCountedLoop()
    {
        var compiled = Compile("loop var i 100; end; return result;");

        var result = ScriptVm.Execute(compiled.Program!, options: new ScriptExecutionOptions
        {
            MaximumSteps = 5,
        });

        Assert.Equal(ScriptExecutionStatus.ExecutionLimit, result.Status);
        Assert.Equal(5, result.Steps);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.ExecutionLimitExceeded);
    }

    [Fact]
    public void TraceRecordsStableInstructionOrderAndDestinationValues()
    {
        var compiled = Compile("set result 4; add result 3; return result;");

        var result = ScriptVm.Execute(compiled.Program!, options: new ScriptExecutionOptions
        {
            CaptureTrace = true,
        });

        Assert.Equal([CoreScriptOperation.Set, CoreScriptOperation.Add, CoreScriptOperation.Return],
            result.Trace.Select(static entry => entry.Operation));
        Assert.Equal([4, 7, null], result.Trace.Select(static entry => entry.DestinationValue));
        Assert.All(result.Trace, static entry => Assert.True(entry.Succeeded));
    }

    [Fact]
    public void TraceAndRuntimeFailuresAreStructuredAndBounded()
    {
        var compiled = Compile("set result 9; div result 0; return result;");
        var runtimeFailure = ScriptVm.Execute(compiled.Program!, options: new ScriptExecutionOptions
        {
            CaptureTrace = true,
        });

        Assert.Equal(ScriptExecutionStatus.RuntimeError, runtimeFailure.Status);
        Assert.Equal(9, runtimeFailure.Outputs["result"]);
        Assert.False(runtimeFailure.Trace[^1].Succeeded);
        Assert.Contains(runtimeFailure.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.RuntimeOperationFailed);

        var traceLimited = ScriptVm.Execute(
            Compile("set result 1; add result 1; return result;").Program!,
            options: new ScriptExecutionOptions { CaptureTrace = true, MaximumTraceEntries = 2 });
        Assert.Equal(ScriptExecutionStatus.TraceLimit, traceLimited.Status);
        Assert.Equal(2, traceLimited.Trace.Count);
        Assert.Contains(traceLimited.Diagnostics,
            diagnostic => diagnostic.Code == ScriptDiagnosticCodes.TraceLimitExceeded);
    }

    private static readonly ScriptParserDefinition Definition = new("Probe", ["result"]);

    private static ScriptCompileResult Compile(string source)
    {
        var compiled = ScriptCompiler.Compile(source, Definition);
        Assert.True(compiled.Succeeded, string.Join(Environment.NewLine,
            compiled.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return compiled;
    }

    private static ScriptExecutionResult Execute(
        string source,
        IReadOnlyDictionary<string, int>? initialOutputs = null) =>
        ScriptVm.Execute(Compile(source).Program!, initialOutputs);
}
