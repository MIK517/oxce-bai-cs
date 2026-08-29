using System.Collections.ObjectModel;
using System.Numerics;
using Oxce.Core.Diagnostics;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Api;

namespace Oxce.Scripting.Runtime;

public sealed record ScriptExecutionOptions
{
    public int MaximumSteps { get; init; } = ScriptLimits.DefaultMaximumExecutionSteps;

    public bool CaptureTrace { get; init; }

    public int MaximumTraceEntries { get; init; } = ScriptLimits.DefaultMaximumTraceEntries;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSteps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTraceEntries);
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

public static class ScriptVm
{
    public static ScriptExecutionResult Execute(
        ScriptProgram program,
        IReadOnlyDictionary<string, int>? initialOutputs = null,
        ScriptExecutionOptions? options = null,
        ScriptHostBindings? hostBindings = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        options ??= new ScriptExecutionOptions();
        options.Validate();
        return new Execution(program, initialOutputs, options, hostBindings ?? ScriptHostBindings.Empty).Run();
    }

    private sealed class Execution
    {
        private readonly ScriptProgram _program;
        private readonly ScriptExecutionOptions _options;
        private readonly int[] _registers;
        private readonly ScriptRegisterDefinition[] _outputs;
        private readonly ScriptHostBindings _hostBindings;
        private readonly Dictionary<int, ScriptBindingDeclaration> _bindingDeclarations;
        private readonly List<DiagnosticEvent> _diagnostics = [];
        private readonly List<ScriptTraceEntry> _trace = [];
        private int _instructionIndex;
        private int _steps;
        private ScriptExecutionStatus _operationFailureStatus = ScriptExecutionStatus.RuntimeError;
        private string _operationFailureCode = ScriptDiagnosticCodes.RuntimeOperationFailed;
        private string? _operationFailureMessage;

        public Execution(
            ScriptProgram program,
            IReadOnlyDictionary<string, int>? initialOutputs,
            ScriptExecutionOptions options,
            ScriptHostBindings hostBindings)
        {
            _program = program;
            _options = options;
            _hostBindings = hostBindings;
            _bindingDeclarations = program.Bindings.ToDictionary(static binding => binding.Id.Value);
            _registers = new int[checked((program.RegisterBytes + sizeof(int) - 1) / sizeof(int))];
            _outputs = program.Registers.Where(static register => register.IsOutput).ToArray();
            initialOutputs ??= new Dictionary<string, int>();
            foreach (var item in initialOutputs)
            {
                var output = _outputs.SingleOrDefault(register =>
                    string.Equals(register.Name, item.Key, StringComparison.Ordinal));
                if (output is null)
                {
                    throw new ArgumentException($"Unknown script output '{item.Key}'.", nameof(initialOutputs));
                }
                Write(output.Offset, item.Value);
            }
        }

        public ScriptExecutionResult Run()
        {
            while (_instructionIndex < _program.Instructions.Count)
            {
                if (_steps >= _options.MaximumSteps)
                {
                    return Fail(
                        ScriptExecutionStatus.ExecutionLimit,
                        ScriptDiagnosticCodes.ExecutionLimitExceeded,
                        $"Script execution exceeded the {_options.MaximumSteps}-step limit.",
                        _program.Instructions[_instructionIndex].Source);
                }
                if (_options.CaptureTrace && _trace.Count >= _options.MaximumTraceEntries)
                {
                    return Fail(
                        ScriptExecutionStatus.TraceLimit,
                        ScriptDiagnosticCodes.TraceLimitExceeded,
                        $"Script trace exceeded the {_options.MaximumTraceEntries}-entry limit.",
                        _program.Instructions[_instructionIndex].Source);
                }

                var currentIndex = _instructionIndex;
                var instruction = _program.Instructions[currentIndex];
                var operation = (CoreScriptOperation)instruction.Operation.Value;
                _instructionIndex++;
                _steps++;
                var succeeded = ExecuteInstruction(operation, instruction, out var destinationValue);
                if (_options.CaptureTrace)
                {
                    _trace.Add(new ScriptTraceEntry(
                        _steps,
                        currentIndex,
                        operation,
                        instruction.Source,
                        destinationValue,
                        succeeded));
                }
                if (!succeeded)
                {
                    return Fail(
                        _operationFailureStatus,
                        _operationFailureCode,
                        _operationFailureMessage ??
                            $"Script operation '{CoreScriptOperationNames.Get(operation)}' failed.",
                        instruction.Source);
                }
                if (operation == CoreScriptOperation.Return)
                {
                    return Complete();
                }
            }
            return Complete();
        }

        private bool ExecuteInstruction(
            CoreScriptOperation operation,
            ScriptInstruction instruction,
            out int? destinationValue)
        {
            destinationValue = null;
            var operands = instruction.Operands;
            switch (operation)
            {
                case CoreScriptOperation.Jump:
                    _instructionIndex = operands[0].Scalar;
                    return true;
                case CoreScriptOperation.BranchCondition:
                    if (!EvaluateCondition(operands))
                    {
                        _instructionIndex = operands[^1].Scalar;
                    }
                    return true;
                case CoreScriptOperation.Return:
                    var values = operands.Select(Read).ToArray();
                    for (var index = 0; index < values.Length; index++)
                    {
                        Write(_outputs[index].Offset, values[index]);
                    }
                    return true;
                case CoreScriptOperation.Set:
                    return WriteResult(operands[0], Read(operands[1]), out destinationValue);
                case CoreScriptOperation.Clear:
                    return WriteResult(operands[0], 0, out destinationValue);
                case CoreScriptOperation.Swap:
                    var left = Read(operands[0]);
                    var right = Read(operands[1]);
                    Write(operands[0].Scalar, right);
                    Write(operands[1].Scalar, left);
                    destinationValue = right;
                    return true;
                case CoreScriptOperation.Add:
                    return UnaryUpdate(operands, static (leftValue, rightValue) =>
                        unchecked(leftValue + rightValue), out destinationValue);
                case CoreScriptOperation.Subtract:
                    return UnaryUpdate(operands, static (leftValue, rightValue) =>
                        unchecked(leftValue - rightValue), out destinationValue);
                case CoreScriptOperation.Multiply:
                    return UnaryUpdate(operands, static (leftValue, rightValue) =>
                        unchecked(leftValue * rightValue), out destinationValue);
                case CoreScriptOperation.Aggregate:
                    return WriteResult(operands[0],
                        unchecked(Read(operands[0]) + Read(operands[1]) * Read(operands[2])),
                        out destinationValue);
                case CoreScriptOperation.Offset:
                    return WriteResult(operands[0],
                        unchecked(Read(operands[0]) * Read(operands[1]) + Read(operands[2])),
                        out destinationValue);
                case CoreScriptOperation.OffsetModulo:
                    return OffsetModulo(operands, out destinationValue);
                case CoreScriptOperation.Divide:
                    return Divide(operands, modulo: false, out destinationValue);
                case CoreScriptOperation.Modulo:
                    return Divide(operands, modulo: true, out destinationValue);
                case CoreScriptOperation.MultiplyDivide:
                    return MultiplyDivide(operands, out destinationValue);
                case CoreScriptOperation.ShiftLeft:
                    return UnaryUpdate(operands, static (leftValue, rightValue) =>
                        unchecked(leftValue << rightValue), out destinationValue);
                case CoreScriptOperation.ShiftRight:
                    return UnaryUpdate(operands, static (leftValue, rightValue) =>
                        leftValue >> rightValue, out destinationValue);
                case CoreScriptOperation.BitAnd:
                    return UnaryUpdate(operands, static (leftValue, rightValue) =>
                        leftValue & rightValue, out destinationValue);
                case CoreScriptOperation.BitOr:
                    return UnaryUpdate(operands, static (leftValue, rightValue) =>
                        leftValue | rightValue, out destinationValue);
                case CoreScriptOperation.BitXor:
                    return UnaryUpdate(operands, static (leftValue, rightValue) =>
                        leftValue ^ rightValue, out destinationValue);
                case CoreScriptOperation.BitNot:
                    return WriteResult(operands[0], ~Read(operands[0]), out destinationValue);
                case CoreScriptOperation.BitCount:
                    return WriteResult(operands[0],
                        BitOperations.PopCount((uint)Read(operands[0])), out destinationValue);
                case CoreScriptOperation.Power:
                    return WriteResult(operands[0],
                        unchecked((int)Math.Pow(Read(operands[0]), Math.Max(0, Read(operands[1])))),
                        out destinationValue);
                case CoreScriptOperation.SquareRoot:
                    var squareValue = Read(operands[0]);
                    return WriteResult(operands[0],
                        squareValue > 0 ? (int)Math.Sqrt(squareValue) : 0, out destinationValue);
                case CoreScriptOperation.Absolute:
                    var absoluteValue = Read(operands[0]);
                    return WriteResult(operands[0],
                        absoluteValue == int.MinValue ? int.MinValue : Math.Abs(absoluteValue), out destinationValue);
                case CoreScriptOperation.Limit:
                    return WriteResult(operands[0],
                        Math.Max(Math.Min(Read(operands[0]), Read(operands[2])), Read(operands[1])),
                        out destinationValue);
                case CoreScriptOperation.LimitUpper:
                    return WriteResult(operands[0],
                        Math.Min(Read(operands[0]), Read(operands[1])), out destinationValue);
                case CoreScriptOperation.LimitLower:
                    return WriteResult(operands[0],
                        Math.Max(Read(operands[0]), Read(operands[1])), out destinationValue);
                case CoreScriptOperation.WaveRectangle:
                case CoreScriptOperation.WaveSaw:
                case CoreScriptOperation.WaveTriangle:
                case CoreScriptOperation.WaveSine:
                case CoreScriptOperation.WaveCosine:
                    return Wave(operation, operands, out destinationValue);
                case CoreScriptOperation.GetColor:
                    return WriteResult(operands[0], Read(operands[1]) >> 4, out destinationValue);
                case CoreScriptOperation.SetColor:
                    return WriteResult(operands[0],
                        (Read(operands[0]) & 0xF) | (Read(operands[1]) << 4), out destinationValue);
                case CoreScriptOperation.GetShade:
                    return WriteResult(operands[0], Read(operands[1]) & 0xF, out destinationValue);
                case CoreScriptOperation.SetShade:
                    return WriteResult(operands[0],
                        (Read(operands[0]) & 0xF0) | (Read(operands[1]) & 0xF), out destinationValue);
                case CoreScriptOperation.AddShade:
                    return WriteResult(operands[0], AddShade(Read(operands[0]), Read(operands[1])),
                        out destinationValue);
                case CoreScriptOperation.HostCall:
                    return InvokeHost(operands, out destinationValue);
                default:
                    return false;
            }
        }

        private bool InvokeHost(IReadOnlyList<ScriptOperand> operands, out int? destinationValue)
        {
            destinationValue = null;
            var id = new ScriptBindingId(operands[0].Scalar);
            if (!_bindingDeclarations.TryGetValue(id.Value, out var declaration))
            {
                _operationFailureMessage = $"Compiled script binding {id.Value} has no declaration.";
                return false;
            }
            if (!_hostBindings.TryGet(id, out var handler))
            {
                _operationFailureStatus = ScriptExecutionStatus.MissingCapability;
                _operationFailureCode = ScriptDiagnosticCodes.MissingBindingProvider;
                _operationFailureMessage =
                    $"Script binding '{declaration.Name}' is declared but no runtime provider is installed.";
                return false;
            }

            Span<int> arguments = stackalloc int[declaration.Parameters.Count];
            for (var index = 0; index < arguments.Length; index++)
            {
                arguments[index] = Read(operands[index + 1]);
            }
            var result = handler!(arguments);
            if (!result.Succeeded)
            {
                _operationFailureCode = ScriptDiagnosticCodes.BindingOperationFailed;
                _operationFailureMessage =
                    $"Script binding '{declaration.Name}' failed: {result.Error ?? "unspecified provider error"}";
                return false;
            }
            for (var index = 0; index < arguments.Length; index++)
            {
                if (declaration.Parameters[index].Writable)
                {
                    Write(operands[index + 1].Scalar, arguments[index]);
                    destinationValue = arguments[index];
                }
            }
            return true;
        }

        private bool EvaluateCondition(IReadOnlyList<ScriptOperand> operands)
        {
            var aggregate = (ScriptConditionKind)operands[0].Scalar;
            var result = aggregate == ScriptConditionKind.All;
            for (var index = 1; index < operands.Count - 1; index += 3)
            {
                var clause = Compare(
                    (ScriptConditionKind)operands[index].Scalar,
                    Read(operands[index + 1]),
                    Read(operands[index + 2]));
                if (aggregate == ScriptConditionKind.All)
                {
                    result &= clause;
                    if (!result)
                    {
                        return false;
                    }
                }
                else
                {
                    result |= clause;
                    if (result)
                    {
                        return true;
                    }
                }
            }
            return result;
        }

        private static bool Compare(ScriptConditionKind condition, int left, int right) => condition switch
        {
            ScriptConditionKind.Equal => left == right,
            ScriptConditionKind.NotEqual => left != right,
            ScriptConditionKind.LessThanOrEqual => left <= right,
            ScriptConditionKind.GreaterThan => left > right,
            ScriptConditionKind.GreaterThanOrEqual => left >= right,
            ScriptConditionKind.LessThan => left < right,
            _ => false,
        };

        private bool OffsetModulo(IReadOnlyList<ScriptOperand> operands, out int? destinationValue)
        {
            var modulo = Read(operands[3]);
            if (modulo == 0)
            {
                destinationValue = null;
                return false;
            }
            var value = (long)Read(operands[0]) * Read(operands[1]) + Read(operands[2]);
            return WriteResult(operands[0], unchecked((int)((value % modulo + modulo) % modulo)),
                out destinationValue);
        }

        private bool Divide(
            IReadOnlyList<ScriptOperand> operands,
            bool modulo,
            out int? destinationValue)
        {
            var divisor = Read(operands[1]);
            if (divisor == 0)
            {
                destinationValue = null;
                return false;
            }
            var dividend = Read(operands[0]);
            var value = modulo ? (long)dividend % divisor : (long)dividend / divisor;
            return WriteResult(operands[0], unchecked((int)value), out destinationValue);
        }

        private bool MultiplyDivide(IReadOnlyList<ScriptOperand> operands, out int? destinationValue)
        {
            var divisor = Read(operands[2]);
            if (divisor == 0)
            {
                destinationValue = null;
                return false;
            }
            var value = (long)Read(operands[0]) * Read(operands[1]) / divisor;
            return WriteResult(operands[0], unchecked((int)value), out destinationValue);
        }

        private bool Wave(
            CoreScriptOperation operation,
            IReadOnlyList<ScriptOperand> operands,
            out int? destinationValue)
        {
            var value = Read(operands[0]);
            var period = Read(operands[1]);
            if (period <= 0)
            {
                destinationValue = null;
                return false;
            }
            var size = Read(operands[2]);
            int result;
            if (operation is CoreScriptOperation.WaveSine or CoreScriptOperation.WaveCosine)
            {
                var angle = 2.0 * Math.PI * value / period;
                result = unchecked((int)(size * (operation == CoreScriptOperation.WaveSine
                    ? Math.Sin(angle)
                    : Math.Cos(angle))));
            }
            else
            {
                value %= period;
                if (value < 0)
                {
                    value += period;
                }
                var maximum = Read(operands[3]);
                result = operation switch
                {
                    CoreScriptOperation.WaveRectangle => value > size ? 0 : maximum,
                    CoreScriptOperation.WaveSaw => value > size ? 0 : Math.Min(value, maximum),
                    CoreScriptOperation.WaveTriangle => Triangle(value, size, maximum),
                    _ => value,
                };
            }
            return WriteResult(operands[0], result, out destinationValue);
        }

        private static int Triangle(int value, int size, int maximum)
        {
            if (value > size)
            {
                return 0;
            }
            if (value > size / 2)
            {
                value = size - value;
            }
            return Math.Min(value, maximum);
        }

        private static int AddShade(int value, int addition)
        {
            var shade = (value & 0xF) + addition;
            if (shade > 0xF)
            {
                return 0xF;
            }
            if (shade > 0)
            {
                return (value & 0xF0) | shade;
            }
            value &= 0xF0;
            return value == 0 || shade < 0 ? 0x01 : value;
        }

        private bool UnaryUpdate(
            IReadOnlyList<ScriptOperand> operands,
            Func<int, int, int> operation,
            out int? destinationValue) =>
            WriteResult(operands[0], operation(Read(operands[0]), Read(operands[1])), out destinationValue);

        private bool WriteResult(ScriptOperand destination, int value, out int? destinationValue)
        {
            Write(destination.Scalar, value);
            destinationValue = value;
            return true;
        }

        private int Read(ScriptOperand operand) => operand.Kind switch
        {
            ScriptOperandKind.Register => Read(operand.Scalar),
            ScriptOperandKind.Scalar => operand.Scalar,
            _ => throw new InvalidOperationException($"Operand '{operand.Kind}' is not an integer value."),
        };

        private int Read(int offset) => _registers[RegisterIndex(offset)];

        private void Write(int offset, int value) => _registers[RegisterIndex(offset)] = value;

        private int RegisterIndex(int offset)
        {
            if (offset % sizeof(int) != 0 || offset < 0 || offset >= _program.RegisterBytes)
            {
                throw new InvalidOperationException($"Invalid compiled register offset {offset}.");
            }
            return offset / sizeof(int);
        }

        private ScriptExecutionResult Complete() =>
            new(ScriptExecutionStatus.Completed, CaptureOutputs(), _diagnostics, _trace, _steps);

        private ScriptExecutionResult Fail(
            ScriptExecutionStatus status,
            string code,
            string message,
            SourceSpan span)
        {
            _diagnostics.Add(new DiagnosticEvent(code, DiagnosticSeverity.Error, message, span));
            return new ScriptExecutionResult(status, CaptureOutputs(), _diagnostics, _trace, _steps);
        }

        private Dictionary<string, int> CaptureOutputs() =>
            _outputs.ToDictionary(output => output.Name, output => Read(output.Offset), StringComparer.Ordinal);
    }
}
