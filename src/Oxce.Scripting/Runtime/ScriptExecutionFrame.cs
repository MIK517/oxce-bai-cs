using Oxce.Core.Diagnostics;
using Oxce.Scripting.Compilation;

namespace Oxce.Scripting.Runtime;

public enum ScriptRuntimeValueKind : byte
{
    Scalar,
    Text,
    Reference,
}

public struct ScriptRuntimeValue
{
    private string? _text;
    private object? _reference;

    public ScriptRuntimeValueKind Kind { get; private set; }

    public int Scalar { get; private set; }

    public readonly string? Text => Kind == ScriptRuntimeValueKind.Text ? _text : null;

    public readonly object? Reference => Kind == ScriptRuntimeValueKind.Reference ? _reference : null;

    public static ScriptRuntimeValue FromScalar(int value) => new()
    {
        Kind = ScriptRuntimeValueKind.Scalar,
        Scalar = value,
    };

    public static ScriptRuntimeValue FromText(string? value) => new()
    {
        Kind = ScriptRuntimeValueKind.Text,
        _text = value,
    };

    public static ScriptRuntimeValue FromReference(object? value) => new()
    {
        Kind = ScriptRuntimeValueKind.Reference,
        _reference = value,
    };
}

public readonly record struct ScriptExecutionOutcome(
    ScriptExecutionStatus Status,
    int Steps,
    int FailureInstructionIndex = -1,
    string? DiagnosticCode = null,
    string? FailureMessage = null)
{
    public bool Succeeded => Status == ScriptExecutionStatus.Completed;
}

public readonly record struct ScriptTraceValue(
    int Step,
    int InstructionIndex,
    CoreScriptOperation Operation,
    SourceSpan Source,
    int? DestinationValue,
    bool Succeeded);

public interface IScriptTraceSink
{
    void Record(in ScriptTraceValue value);
}

public sealed class ScriptExecutionFrame
{
    private readonly ScriptRuntimeValue[][] _registerFrames;
    private readonly ScriptRuntimeValue[][] _valueScratchFrames;
    private readonly int[][] _scalarScratchFrames;
    private readonly ScriptRuntimeValue[][] _conversionInputFrames;
    private readonly ScriptRuntimeValue[][] _conversionOutputFrames;
    private readonly ScriptRuntimeValue[][] _eventValueFrames;
    private int _depth;

    public ScriptExecutionFrame(int maximumCallDepth = ScriptLimits.DefaultMaximumCallDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCallDepth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumCallDepth, ScriptLimits.MaximumCallDepth);
        _registerFrames = new ScriptRuntimeValue[maximumCallDepth][];
        _valueScratchFrames = new ScriptRuntimeValue[maximumCallDepth][];
        _scalarScratchFrames = new int[maximumCallDepth][];
        _conversionInputFrames = new ScriptRuntimeValue[maximumCallDepth][];
        _conversionOutputFrames = new ScriptRuntimeValue[maximumCallDepth][];
        _eventValueFrames = new ScriptRuntimeValue[maximumCallDepth][];
    }

    public int MaximumCallDepth => _registerFrames.Length;

    public int CallDepth => _depth;

    public void Prepare(ScriptProgram program, int depth = 0)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(depth, _registerFrames.Length);
        EnsureCapacity(depth, program.RegisterSlotCount);
        _valueScratchFrames[depth] ??= new ScriptRuntimeValue[ScriptLimits.MaximumArguments];
        _scalarScratchFrames[depth] ??= new int[ScriptLimits.MaximumArguments];
        _conversionInputFrames[depth] ??= new ScriptRuntimeValue[ScriptLimits.MaximumOutputs];
        _conversionOutputFrames[depth] ??= new ScriptRuntimeValue[ScriptLimits.MaximumOutputs];
        _eventValueFrames[depth] ??= new ScriptRuntimeValue[ScriptLimits.MaximumOutputs];
    }

    internal bool TryEnter(int registerSlotCount, int maximumCallDepth, out Span<ScriptRuntimeValue> registers)
    {
        if (_depth >= maximumCallDepth || _depth >= _registerFrames.Length)
        {
            registers = default;
            return false;
        }
        var depth = _depth++;
        EnsureCapacity(depth, registerSlotCount);
        registers = _registerFrames[depth].AsSpan(0, registerSlotCount);
        registers.Clear();
        return true;
    }

    internal void Exit(Span<ScriptRuntimeValue> registers)
    {
        registers.Clear();
        _depth--;
    }

    internal Span<ScriptRuntimeValue> GetValueScratch(int length)
    {
        var depth = Math.Max(0, _depth - 1);
        if (_valueScratchFrames[depth] is not { } scratch || scratch.Length < length)
        {
            scratch = _valueScratchFrames[depth] = new ScriptRuntimeValue[ScriptLimits.MaximumArguments];
        }
        var result = scratch.AsSpan(0, length);
        result.Clear();
        return result;
    }

    internal Span<int> GetScalarScratch(int length)
    {
        var depth = Math.Max(0, _depth - 1);
        if (_scalarScratchFrames[depth] is not { } scratch || scratch.Length < length)
        {
            scratch = _scalarScratchFrames[depth] = new int[ScriptLimits.MaximumArguments];
        }
        var result = scratch.AsSpan(0, length);
        result.Clear();
        return result;
    }

    internal void GetConversionScratch(
        int inputLength,
        int outputLength,
        out Span<ScriptRuntimeValue> input,
        out Span<ScriptRuntimeValue> output)
    {
        var depth = _depth;
        if (_conversionInputFrames[depth] is not { } inputArray || inputArray.Length < inputLength)
        {
            inputArray = _conversionInputFrames[depth] = new ScriptRuntimeValue[ScriptLimits.MaximumOutputs];
        }
        if (_conversionOutputFrames[depth] is not { } outputArray || outputArray.Length < outputLength)
        {
            outputArray = _conversionOutputFrames[depth] = new ScriptRuntimeValue[ScriptLimits.MaximumOutputs];
        }
        input = inputArray.AsSpan(0, inputLength);
        output = outputArray.AsSpan(0, outputLength);
        input.Clear();
        output.Clear();
    }

    internal Span<ScriptRuntimeValue> GetEventScratch(int length)
    {
        var depth = _depth;
        if (depth >= _eventValueFrames.Length)
        {
            return default;
        }
        if (_eventValueFrames[depth] is not { } values || values.Length < length)
        {
            values = _eventValueFrames[depth] = new ScriptRuntimeValue[ScriptLimits.MaximumOutputs];
        }
        var result = values.AsSpan(0, length);
        result.Clear();
        return result;
    }

    private void EnsureCapacity(int depth, int registerSlotCount)
    {
        if (_registerFrames[depth] is not { } current || current.Length < registerSlotCount)
        {
            _registerFrames[depth] = new ScriptRuntimeValue[registerSlotCount];
        }
    }
}
