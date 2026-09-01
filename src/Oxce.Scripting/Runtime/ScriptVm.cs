using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Diagnostics;

namespace Oxce.Scripting.Runtime;

public sealed record ScriptExecutionOptions
{
    public static ScriptExecutionOptions Default { get; } = new();

    public int MaximumSteps { get; init; } = ScriptLimits.DefaultMaximumExecutionSteps;

    public bool CaptureTrace { get; init; }

    public int MaximumTraceEntries { get; init; } = ScriptLimits.DefaultMaximumTraceEntries;

    public int MaximumCallDepth { get; init; } = ScriptLimits.DefaultMaximumCallDepth;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSteps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTraceEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumCallDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumCallDepth, ScriptLimits.MaximumCallDepth);
    }
}

public enum ScriptExecutionStatus
{
    Completed,
    RuntimeError,
    ExecutionLimit,
    TraceLimit,
    MissingCapability,
}

public sealed record ScriptTraceEntry(
    int Step,
    int InstructionIndex,
    CoreScriptOperation Operation,
    SourceSpan Source,
    int? DestinationValue,
    bool Succeeded);

public sealed class ScriptExecutionResult
{
    internal ScriptExecutionResult(
        ScriptExecutionStatus status,
        IDictionary<string, int> outputs,
        IEnumerable<DiagnosticEvent> diagnostics,
        IEnumerable<ScriptTraceEntry> trace,
        int steps)
    {
        Status = status;
        Outputs = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(outputs, StringComparer.Ordinal));
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        Trace = Array.AsReadOnly(trace.ToArray());
        Steps = steps;
    }

    public ScriptExecutionStatus Status { get; }

    public IReadOnlyDictionary<string, int> Outputs { get; }

    public IReadOnlyList<DiagnosticEvent> Diagnostics { get; }

    public IReadOnlyList<ScriptTraceEntry> Trace { get; }

    public int Steps { get; }

    public bool Succeeded => Status == ScriptExecutionStatus.Completed;
}

public static partial class ScriptVm
{
    public static ScriptExecutionResult Execute(
        ScriptProgram program,
        IReadOnlyDictionary<string, int>? initialOutputs = null,
        ScriptExecutionOptions? options = null,
        ScriptHostBindings? hostBindings = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        options ??= ScriptExecutionOptions.Default;
        options.Validate();
        var outputDefinitions = program.OutputRegisters;
        var values = new ScriptRuntimeValue[outputDefinitions.Length];
        if (initialOutputs is not null)
        {
            foreach (var item in initialOutputs)
            {
                var index = -1;
                for (var outputIndex = 0; outputIndex < outputDefinitions.Length; outputIndex++)
                {
                    if (string.Equals(outputDefinitions[outputIndex].Name, item.Key, StringComparison.Ordinal))
                    {
                        index = outputIndex;
                        break;
                    }
                }
                if (index < 0)
                {
                    throw new ArgumentException($"Unknown script output '{item.Key}'.", nameof(initialOutputs));
                }
                values[index] = ScriptRuntimeValue.FromScalar(item.Value);
            }
        }
        var frame = new ScriptExecutionFrame(options.MaximumCallDepth);
        var trace = options.CaptureTrace ? new TraceCollector() : null;
        var outcome = ExecuteCore(
            program,
            values,
            values,
            frame,
            options,
            hostBindings ?? ScriptHostBindings.Empty,
            trace,
            commitOnFailure: true);
        var outputs = new Dictionary<string, int>(outputDefinitions.Length, StringComparer.Ordinal);
        for (var index = 0; index < outputDefinitions.Length; index++)
        {
            outputs.Add(outputDefinitions[index].Name, values[index].Scalar);
        }
        var diagnostics = outcome.Succeeded
            ? Array.Empty<DiagnosticEvent>()
            : new[]
            {
                new DiagnosticEvent(
                    outcome.DiagnosticCode ?? ScriptDiagnosticCodes.RuntimeOperationFailed,
                    DiagnosticSeverity.Error,
                    outcome.FailureMessage ?? "Script execution failed.",
                    outcome.FailureInstructionIndex >= 0
                        ? program.GetSource(outcome.FailureInstructionIndex)
                        : null),
            };
        return new ScriptExecutionResult(
            outcome.Status,
            outputs,
            diagnostics,
            trace?.Entries ?? [],
            outcome.Steps);
    }

    private sealed class TraceCollector : IScriptTraceSink
    {
        public List<ScriptTraceEntry> Entries { get; } = [];

        public void Record(in ScriptTraceValue value) => Entries.Add(new ScriptTraceEntry(
            value.Step,
            value.InstructionIndex,
            value.Operation,
            value.Source,
            value.DestinationValue,
            value.Succeeded));
    }
}
