using BenchmarkDotNet.Attributes;
using Oxce.Core.Diagnostics;
using Oxce.Scripting.Api;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Events;
using Oxce.Scripting.Runtime;
using Oxce.Scripting.Types;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class ScriptVmBenchmarks
{
    private ScriptProgram _scalar = null!;
    private ScriptProgram _host = null!;
    private ScriptProgram _event = null!;
    private ScriptEventPlan _events = null!;
    private ScriptHostBindings _hostBindings = null!;
    private IReadOnlyDictionary<string, int> _initial = null!;
    private ScriptExecutionFrame _scalarFrame = null!;
    private ScriptExecutionFrame _hostFrame = null!;
    private ScriptExecutionFrame _eventFrame = null!;
    private int[] _scalarInput = null!;
    private int[] _scalarOutput = null!;
    private ScriptRuntimeValue[] _eventInput = null!;
    private ScriptRuntimeValue[] _eventOutput = null!;

    [GlobalSetup]
    public void Setup()
    {
        _scalar = Compile("set result 1; loop var i 32; add result i; end; return result;", Definition());
        var bindingId = new ScriptBindingId(20_001);
        var writable = new ScriptTypeRef(
            ScriptPrimitiveTypes.Scalar,
            ScriptTypeModifier.Register | ScriptTypeModifier.Writable);
        var scalar = new ScriptTypeRef(ScriptPrimitiveTypes.Scalar);
        var reference = new ScriptReferenceLocation("benchmarks/ScriptVmBenchmarks.cs", 1);
        var catalog = new ScriptApiCatalog(
            [new ScriptBindingDeclaration(
                bindingId,
                "adjust",
                [new ScriptBindingParameter("target", writable, true), new ScriptBindingParameter("delta", scalar, false)],
                ["Probe"],
                reference)]);
        _host = Compile(
            "loop var i 16; adjust result i; end; return result;",
            new ScriptParserDefinition("Probe", ["result"], catalog, ["Probe"]));
        var providers = new ScriptHostBindingsBuilder();
        providers.Add(bindingId, static arguments =>
        {
            arguments[0] += arguments[1];
            return ScriptBindingResult.Success;
        });
        _hostBindings = providers.Build();
        _event = Compile("add result 3; return result;", Definition());
        _events = ScriptEventComposer.Compose(
        [
            new ScriptEventMutation(ScriptEventMutationKind.Append, string.Empty, -100,
                Compile("add result 1; return result;", Definition()), "benchmark", 1),
            new ScriptEventMutation(ScriptEventMutationKind.Append, string.Empty, 100,
                Compile("mul result 2; return result;", Definition()), "benchmark", 2),
        ]).Plan!;
        _initial = new Dictionary<string, int>(StringComparer.Ordinal) { ["result"] = 1 };
        _scalarFrame = new ScriptExecutionFrame();
        _scalarFrame.Prepare(_scalar);
        _hostFrame = new ScriptExecutionFrame();
        _hostFrame.Prepare(_host);
        _eventFrame = new ScriptExecutionFrame();
        _eventFrame.Prepare(_event);
        foreach (var item in _events.Before.Concat(_events.After))
        {
            _eventFrame.Prepare(item.Program);
        }
        _scalarInput = [1];
        _scalarOutput = new int[1];
        _eventInput = [ScriptRuntimeValue.FromScalar(1)];
        _eventOutput = new ScriptRuntimeValue[1];
        ScriptVm.ExecuteScalar(_scalar, _scalarInput, _scalarOutput, _scalarFrame);
        ScriptVm.ExecuteScalar(_host, _scalarInput, _scalarOutput, _hostFrame,
            hostBindings: _hostBindings);
        ScriptEventRunner.Execute(_events, _event, _eventInput, _eventOutput, _eventFrame);
    }

    [Benchmark(Baseline = true)]
    public ScriptExecutionResult ScalarExecution() => ScriptVm.Execute(_scalar, _initial);

    [Benchmark]
    public ScriptExecutionResult HostCallExecution() =>
        ScriptVm.Execute(_host, _initial, hostBindings: _hostBindings);

    [Benchmark]
    public ScriptEventExecutionResult EventExecution() =>
        ScriptEventRunner.Execute(_events, _event, _initial);

    [Benchmark]
    public ScriptExecutionOutcome ReusableScalarFrame() =>
        ScriptVm.ExecuteScalar(_scalar, _scalarInput, _scalarOutput, _scalarFrame);

    [Benchmark]
    public ScriptExecutionOutcome ReusableHostFrame() =>
        ScriptVm.ExecuteScalar(_host, _scalarInput, _scalarOutput, _hostFrame,
            hostBindings: _hostBindings);

    [Benchmark]
    public ScriptEventExecutionOutcome ReusableEventFrame() =>
        ScriptEventRunner.Execute(_events, _event, _eventInput, _eventOutput, _eventFrame);

    private static ScriptParserDefinition Definition() => new("Probe", ["result"]);

    private static ScriptProgram Compile(string source, ScriptParserDefinition definition)
    {
        var result = ScriptCompiler.Compile(source, definition);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        }
        return result.Program!;
    }
}
