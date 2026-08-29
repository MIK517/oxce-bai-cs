using Oxce.Core.Diagnostics;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Runtime;

namespace Oxce.Scripting.Events;

public enum ScriptEventMutationKind
{
    Append,
    New,
    Update,
    Override,
    Delete,
    Ignore,
}

public sealed record ScriptEventMutation(
    ScriptEventMutationKind Kind,
    string Name,
    int Offset,
    ScriptProgram? Program,
    string SourceFile,
    int SourceLine)
{
    public static int ScaleOffset(double offset) => unchecked((int)(offset * ScriptLimits.EventOffsetScale));
}

public sealed record ScriptGlobalEvent(string Name, int Offset, ScriptProgram Program, int Sequence);

public sealed class ScriptEventPlan
{
    internal ScriptEventPlan(IEnumerable<ScriptGlobalEvent> before, IEnumerable<ScriptGlobalEvent> after)
    {
        Before = Array.AsReadOnly(before.ToArray());
        After = Array.AsReadOnly(after.ToArray());
    }

    public IReadOnlyList<ScriptGlobalEvent> Before { get; }
    public IReadOnlyList<ScriptGlobalEvent> After { get; }
    public int Count => Before.Count + After.Count;
}

public sealed record ScriptEventCompositionResult(
    ScriptEventPlan? Plan,
    IReadOnlyList<DiagnosticEvent> Diagnostics)
{
    public bool Accepted => Plan is not null;

    public bool Succeeded => Plan is not null &&
        Diagnostics.All(static diagnostic => diagnostic.Severity < DiagnosticSeverity.Error);
}

public static class ScriptEventComposer
{
    public static ScriptEventCompositionResult Compose(IEnumerable<ScriptEventMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        var definitions = new List<ScriptGlobalEvent>();
        var diagnostics = new List<DiagnosticEvent>();
        var sequence = 0;
        var fatal = false;
        foreach (var mutation in mutations)
        {
            ArgumentNullException.ThrowIfNull(mutation);
            var source = Source(mutation);
            if (mutation.Kind != ScriptEventMutationKind.Append && string.IsNullOrWhiteSpace(mutation.Name))
            {
                Error(diagnostics, $"Event mutation '{mutation.Kind}' requires a non-empty name.", source);
                fatal = true;
                continue;
            }
            var index = definitions.FindIndex(item =>
                string.Equals(item.Name, mutation.Name, StringComparison.Ordinal));
            if (mutation.Kind == ScriptEventMutationKind.Delete)
            {
                if (index >= 0)
                {
                    definitions.RemoveAt(index);
                }
                else
                {
                    Warning(diagnostics, $"Unknown script name '{mutation.Name}' for delete.", source);
                }
                continue;
            }
            if (mutation.Offset == 0 || mutation.Offset >= ScriptLimits.MaximumEventOffset ||
                mutation.Offset <= -ScriptLimits.MaximumEventOffset)
            {
                Error(diagnostics, $"Invalid global script offset {mutation.Offset}.", source);
                continue;
            }
            if (mutation.Program is null)
            {
                Error(diagnostics, "A non-delete global script requires compiled code.", source);
                fatal = true;
                continue;
            }
            if (mutation.Kind == ScriptEventMutationKind.Ignore)
            {
                continue;
            }

            var item = new ScriptGlobalEvent(mutation.Name, mutation.Offset, mutation.Program, sequence++);
            switch (mutation.Kind)
            {
                case ScriptEventMutationKind.New when index >= 0:
                    Error(diagnostics, $"Script name '{mutation.Name}' is already used.", source);
                    fatal = true;
                    break;
                case ScriptEventMutationKind.Update when index < 0:
                    Warning(diagnostics, $"Unknown script name '{mutation.Name}' for update.", source);
                    break;
                case ScriptEventMutationKind.Override when index < 0:
                    Error(diagnostics, $"Unknown script name '{mutation.Name}' for override.", source);
                    fatal = true;
                    break;
                case ScriptEventMutationKind.Update or ScriptEventMutationKind.Override:
                    definitions[index] = item with { Sequence = definitions[index].Sequence };
                    break;
                default:
                    definitions.Add(item);
                    break;
            }
        }

        if (definitions.Count > ScriptLimits.MaximumGlobalEvents - 2)
        {
            Error(diagnostics,
                $"Global script count exceeds the {ScriptLimits.MaximumGlobalEvents - 2}-event payload limit.",
                null);
        }
        if (fatal)
        {
            return new ScriptEventCompositionResult(null, diagnostics.AsReadOnly());
        }

        var ordered = definitions.OrderBy(static item => item.Offset).ThenBy(static item => item.Sequence)
            .Take(ScriptLimits.MaximumGlobalEvents - 2)
            .ToArray();
        return new ScriptEventCompositionResult(
            new ScriptEventPlan(
                ordered.Where(static item => item.Offset < 0),
                ordered.Where(static item => item.Offset > 0)),
            diagnostics.AsReadOnly());
    }

    private static SourceSpan Source(ScriptEventMutation mutation) => new(
        mutation.SourceFile,
        new SourcePosition(mutation.SourceLine, 1, 0),
        new SourcePosition(mutation.SourceLine, 1, 0));

    private static void Warning(List<DiagnosticEvent> diagnostics, string message, SourceSpan? source) =>
        diagnostics.Add(new DiagnosticEvent(
            ScriptDiagnosticCodes.UnknownEventDefinition,
            DiagnosticSeverity.Warning,
            message,
            source));

    private static void Error(List<DiagnosticEvent> diagnostics, string message, SourceSpan? source) =>
        diagnostics.Add(new DiagnosticEvent(
            ScriptDiagnosticCodes.InvalidEventDefinition,
            DiagnosticSeverity.Error,
            message,
            source));
}

public sealed record ScriptEventExecutionResult(
    ScriptExecutionStatus Status,
    IReadOnlyDictionary<string, int> Outputs,
    IReadOnlyList<ScriptExecutionResult> Executions,
    IReadOnlyList<DiagnosticEvent> Diagnostics)
{
    public bool Succeeded => Status == ScriptExecutionStatus.Completed;
}

public static class ScriptEventRunner
{
    public static ScriptEventExecutionResult Execute(
        ScriptEventPlan plan,
        ScriptProgram current,
        IReadOnlyDictionary<string, int>? initialOutputs = null,
        ScriptExecutionOptions? executionOptions = null,
        ScriptHostBindings? hostBindings = null,
        int maximumEventExecutions = ScriptLimits.DefaultMaximumEventExecutions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEventExecutions);
        var programs = plan.Before.Select(static item => item.Program)
            .Append(current)
            .Concat(plan.After.Select(static item => item.Program))
            .ToArray();
        var outputs = initialOutputs is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(initialOutputs, StringComparer.Ordinal);
        var executions = new List<ScriptExecutionResult>();
        if (programs.Length > maximumEventExecutions)
        {
            var diagnostic = new DiagnosticEvent(
                ScriptDiagnosticCodes.EventLimitExceeded,
                DiagnosticSeverity.Error,
                $"Script event execution exceeds the {maximumEventExecutions}-event limit.");
            return new ScriptEventExecutionResult(
                ScriptExecutionStatus.ExecutionLimit,
                outputs,
                executions.AsReadOnly(),
                Array.AsReadOnly([diagnostic]));
        }

        foreach (var program in programs)
        {
            var result = ScriptVm.Execute(program, outputs, executionOptions, hostBindings);
            executions.Add(result);
            outputs = new Dictionary<string, int>(result.Outputs, StringComparer.Ordinal);
            if (!result.Succeeded)
            {
                return new ScriptEventExecutionResult(
                    result.Status,
                    result.Outputs,
                    executions.AsReadOnly(),
                    result.Diagnostics);
            }
        }
        return new ScriptEventExecutionResult(
            ScriptExecutionStatus.Completed,
            outputs,
            executions.AsReadOnly(),
            Array.Empty<DiagnosticEvent>());
    }
}
