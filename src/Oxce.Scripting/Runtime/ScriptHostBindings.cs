using Oxce.Scripting.Api;

namespace Oxce.Scripting.Runtime;

public readonly record struct ScriptBindingResult(
    bool Succeeded,
    string? Error,
    ScriptExecutionStatus? FailureStatus = null,
    string? DiagnosticCode = null)
{
    public static ScriptBindingResult Success { get; } = new(true, null);

    public static ScriptBindingResult Failure(
        string error,
        ScriptExecutionStatus? status = null,
        string? diagnosticCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new ScriptBindingResult(false, error, status, diagnosticCode);
    }
}

public delegate ScriptBindingResult ScriptBindingHandler(Span<int> arguments);

public delegate ScriptBindingResult ScriptContextBindingHandler(
    ScriptBindingContext context,
    Span<ScriptRuntimeValue> arguments);

public readonly struct ScriptBindingContext
{
    private readonly ScriptExecutionFrame _frame;
    private readonly ScriptExecutionOptions _options;
    private readonly ScriptHostBindings _hostBindings;
    private readonly IScriptTraceSink? _traceSink;

    internal ScriptBindingContext(
        ScriptExecutionFrame frame,
        ScriptExecutionOptions options,
        ScriptHostBindings hostBindings,
        IScriptTraceSink? traceSink)
    {
        _frame = frame;
        _options = options;
        _hostBindings = hostBindings;
        _traceSink = traceSink;
    }

    public int CallDepth => _frame.CallDepth;

    public ScriptExecutionOutcome ExecuteNested(
        Compilation.ScriptProgram program,
        ReadOnlySpan<ScriptRuntimeValue> initialOutputs,
        Span<ScriptRuntimeValue> outputs) =>
        ScriptVm.Execute(program, initialOutputs, outputs, _frame, _options, _hostBindings, _traceSink);

    public ScriptExecutionOutcome ExecuteNestedScalar(
        Compilation.ScriptProgram program,
        ReadOnlySpan<int> initialOutputs,
        Span<int> outputs) =>
        ScriptVm.ExecuteScalar(program, initialOutputs, outputs, _frame, _options, _hostBindings, _traceSink);
}

internal readonly record struct ScriptBindingProvider(
    ScriptBindingHandler? ScalarHandler,
    ScriptContextBindingHandler? ContextHandler);

public sealed class ScriptHostBindings
{
    private readonly Dictionary<int, ScriptBindingProvider> _handlers;

    internal ScriptHostBindings(IEnumerable<KeyValuePair<ScriptBindingId, ScriptBindingProvider>> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToDictionary(static item => item.Key.Value, static item => item.Value);
    }

    public static ScriptHostBindings Empty { get; } = new([]);

    internal bool TryGet(ScriptBindingId id, out ScriptBindingProvider provider) =>
        _handlers.TryGetValue(id.Value, out provider);
}

public sealed class ScriptHostBindingsBuilder
{
    private readonly Dictionary<int, ScriptBindingProvider> _handlers = [];

    public void Add(ScriptBindingId id, ScriptBindingHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(id.Value, new ScriptBindingProvider(handler, null)))
        {
            throw new ArgumentException($"A provider for script binding {id.Value} is already installed.", nameof(id));
        }
    }

    public void Add(ScriptBindingId id, ScriptContextBindingHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(id.Value, new ScriptBindingProvider(null, handler)))
        {
            throw new ArgumentException($"A provider for script binding {id.Value} is already installed.", nameof(id));
        }
    }

    public ScriptHostBindings Build() => new(
        _handlers.Select(static item =>
            new KeyValuePair<ScriptBindingId, ScriptBindingProvider>(new ScriptBindingId(item.Key), item.Value)));
}
