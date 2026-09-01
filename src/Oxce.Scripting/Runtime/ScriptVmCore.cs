using System.Numerics;
using Oxce.Scripting.Api;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Diagnostics;
using Oxce.Scripting.Types;

namespace Oxce.Scripting.Runtime;

public static partial class ScriptVm
{
    public static ScriptExecutionOutcome Execute(
        ScriptProgram program,
        ReadOnlySpan<ScriptRuntimeValue> initialOutputs,
        Span<ScriptRuntimeValue> outputs,
        ScriptExecutionFrame frame,
        ScriptExecutionOptions? options = null,
        ScriptHostBindings? hostBindings = null,
        IScriptTraceSink? traceSink = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(frame);
        options ??= ScriptExecutionOptions.Default;
        options.Validate();
        return ExecuteCore(
            program,
            initialOutputs,
            outputs,
            frame,
            options,
            hostBindings ?? ScriptHostBindings.Empty,
            traceSink,
            commitOnFailure: false);
    }

    public static ScriptExecutionOutcome ExecuteScalar(
        ScriptProgram program,
        ReadOnlySpan<int> initialOutputs,
        Span<int> outputs,
        ScriptExecutionFrame frame,
        ScriptExecutionOptions? options = null,
        ScriptHostBindings? hostBindings = null,
        IScriptTraceSink? traceSink = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(frame);
        options ??= ScriptExecutionOptions.Default;
        options.Validate();
        if (initialOutputs.Length > ScriptLimits.MaximumOutputs || outputs.Length > ScriptLimits.MaximumOutputs)
        {
            throw new ArgumentOutOfRangeException(nameof(outputs));
        }
        if (frame.CallDepth >= options.MaximumCallDepth || frame.CallDepth >= frame.MaximumCallDepth)
        {
            return new ScriptExecutionOutcome(
                ScriptExecutionStatus.ExecutionLimit,
                0,
                DiagnosticCode: ScriptDiagnosticCodes.CallDepthExceeded,
                FailureMessage: $"Script call depth exceeds the {options.MaximumCallDepth}-frame limit.");
        }
        frame.GetConversionScratch(initialOutputs.Length, outputs.Length, out var initial, out var result);
        for (var index = 0; index < initial.Length; index++)
        {
            initial[index] = ScriptRuntimeValue.FromScalar(initialOutputs[index]);
        }
        var outcome = Execute(program, initial, result, frame, options, hostBindings, traceSink);
        if (outcome.Succeeded)
        {
            for (var index = 0; index < result.Length; index++)
            {
                outputs[index] = result[index].Scalar;
            }
        }
        return outcome;
    }

    internal static ScriptExecutionOutcome ExecuteCore(
        ScriptProgram program,
        ReadOnlySpan<ScriptRuntimeValue> initialOutputs,
        Span<ScriptRuntimeValue> outputs,
        ScriptExecutionFrame frame,
        ScriptExecutionOptions options,
        ScriptHostBindings hostBindings,
        IScriptTraceSink? traceSink,
        bool commitOnFailure)
    {
        var outputDefinitions = program.OutputRegisters;
        if (initialOutputs.Length != 0 && initialOutputs.Length != outputDefinitions.Length)
        {
            throw new ArgumentException("Initial output count must be zero or match the program output count.",
                nameof(initialOutputs));
        }
        if (outputs.Length < outputDefinitions.Length)
        {
            throw new ArgumentException("The output span is smaller than the program output count.", nameof(outputs));
        }
        if (!frame.TryEnter(program.RegisterSlotCount, options.MaximumCallDepth, out var registers))
        {
            return new ScriptExecutionOutcome(
                ScriptExecutionStatus.ExecutionLimit,
                0,
                DiagnosticCode: ScriptDiagnosticCodes.CallDepthExceeded,
                FailureMessage: $"Script call depth exceeds the {options.MaximumCallDepth}-frame limit.");
        }

        try
        {
            for (var index = 0; index < initialOutputs.Length; index++)
            {
                registers[RegisterIndex(program, outputDefinitions[index].Offset)] = initialOutputs[index];
            }
            var execution = new PackedExecution(program, registers, options, hostBindings, frame, traceSink);
            var outcome = execution.Run();
            if (outcome.Succeeded || commitOnFailure)
            {
                for (var index = 0; index < outputDefinitions.Length; index++)
                {
                    outputs[index] = registers[RegisterIndex(program, outputDefinitions[index].Offset)];
                }
            }
            return outcome;
        }
        finally
        {
            frame.Exit(registers);
        }
    }

    private ref struct PackedExecution
    {
        private readonly ScriptProgram _program;
        private readonly Span<ScriptRuntimeValue> _registers;
        private readonly ScriptExecutionOptions _options;
        private readonly ScriptHostBindings _hostBindings;
        private readonly ScriptExecutionFrame _frame;
        private readonly IScriptTraceSink? _traceSink;
        private int _instructionIndex;
        private int _steps;
        private ScriptExecutionStatus _failureStatus;
        private string _failureCode;
        private string? _failureMessage;

        public PackedExecution(
            ScriptProgram program,
            Span<ScriptRuntimeValue> registers,
            ScriptExecutionOptions options,
            ScriptHostBindings hostBindings,
            ScriptExecutionFrame frame,
            IScriptTraceSink? traceSink)
        {
            _program = program;
            _registers = registers;
            _options = options;
            _hostBindings = hostBindings;
            _frame = frame;
            _traceSink = traceSink;
            _failureStatus = ScriptExecutionStatus.RuntimeError;
            _failureCode = ScriptDiagnosticCodes.RuntimeOperationFailed;
        }

        public ScriptExecutionOutcome Run()
        {
            while (_instructionIndex < _program.InstructionCount)
            {
                if (_steps >= _options.MaximumSteps)
                {
                    return Failure(
                        ScriptExecutionStatus.ExecutionLimit,
                        ScriptDiagnosticCodes.ExecutionLimitExceeded,
                        $"Script execution exceeded the {_options.MaximumSteps}-step limit.",
                        _instructionIndex);
                }
                if (_options.CaptureTrace && _steps >= _options.MaximumTraceEntries)
                {
                    return Failure(
                        ScriptExecutionStatus.TraceLimit,
                        ScriptDiagnosticCodes.TraceLimitExceeded,
                        $"Script trace exceeded the {_options.MaximumTraceEntries}-entry limit.",
                        _instructionIndex);
                }

                var currentIndex = _instructionIndex++;
                var instruction = _program.GetPackedInstruction(currentIndex);
                var operation = (CoreScriptOperation)instruction.Operation.Value;
                var operands = _program.GetPackedOperands(instruction);
                _steps++;
                var succeeded = ExecuteInstruction(operation, operands, out var destinationValue);
                if (_options.CaptureTrace)
                {
                    var trace = new ScriptTraceValue(
                        _steps,
                        currentIndex,
                        operation,
                        _program.GetSource(currentIndex),
                        destinationValue,
                        succeeded);
                    _traceSink?.Record(in trace);
                }
                if (!succeeded)
                {
                    return Failure(
                        _failureStatus,
                        _failureCode,
                        _failureMessage ?? $"Script operation '{CoreScriptOperationNames.Get(operation)}' failed.",
                        currentIndex);
                }
                if (operation is CoreScriptOperation.Return or CoreScriptOperation.Exit)
                {
                    break;
                }
            }
            return new ScriptExecutionOutcome(ScriptExecutionStatus.Completed, _steps);
        }

        private bool ExecuteInstruction(
            CoreScriptOperation operation,
            ReadOnlySpan<ScriptOperand> operands,
            out int? destinationValue)
        {
            destinationValue = null;
            switch (operation)
            {
                case CoreScriptOperation.Exit:
                    return true;
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
                    var values = _frame.GetValueScratch(operands.Length);
                    for (var index = 0; index < values.Length; index++)
                    {
                        values[index] = ReadValue(operands[index]);
                    }
                    var outputs = _program.OutputRegisters;
                    for (var index = 0; index < values.Length; index++)
                    {
                        WriteValue(outputs[index].Offset, values[index]);
                    }
                    values.Clear();
                    return true;
                case CoreScriptOperation.Set:
                    var setValue = ReadValue(operands[1]);
                    WriteValue(operands[0].Scalar, setValue);
                    destinationValue = setValue.Kind == ScriptRuntimeValueKind.Scalar ? setValue.Scalar : null;
                    return true;
                case CoreScriptOperation.Clear:
                    return WriteResult(operands[0], 0, out destinationValue);
                case CoreScriptOperation.Swap:
                    var left = ReadValue(operands[0]);
                    var right = ReadValue(operands[1]);
                    WriteValue(operands[0].Scalar, right);
                    WriteValue(operands[1].Scalar, left);
                    destinationValue = right.Kind == ScriptRuntimeValueKind.Scalar ? right.Scalar : null;
                    return true;
                case CoreScriptOperation.Add:
                    return Update(operands, static (leftValue, rightValue) => unchecked(leftValue + rightValue),
                        out destinationValue);
                case CoreScriptOperation.Subtract:
                    return Update(operands, static (leftValue, rightValue) => unchecked(leftValue - rightValue),
                        out destinationValue);
                case CoreScriptOperation.Multiply:
                    return Update(operands, static (leftValue, rightValue) => unchecked(leftValue * rightValue),
                        out destinationValue);
                case CoreScriptOperation.Aggregate:
                    return WriteResult(operands[0],
                        unchecked(ReadScalar(operands[0]) + ReadScalar(operands[1]) * ReadScalar(operands[2])),
                        out destinationValue);
                case CoreScriptOperation.Offset:
                    return WriteResult(operands[0],
                        unchecked(ReadScalar(operands[0]) * ReadScalar(operands[1]) + ReadScalar(operands[2])),
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
                    return Update(operands, static (leftValue, rightValue) => unchecked(leftValue << rightValue),
                        out destinationValue);
                case CoreScriptOperation.ShiftRight:
                    return Update(operands, static (leftValue, rightValue) => leftValue >> rightValue,
                        out destinationValue);
                case CoreScriptOperation.BitAnd:
                    return Update(operands, static (leftValue, rightValue) => leftValue & rightValue,
                        out destinationValue);
                case CoreScriptOperation.BitOr:
                    return Update(operands, static (leftValue, rightValue) => leftValue | rightValue,
                        out destinationValue);
                case CoreScriptOperation.BitXor:
                    return Update(operands, static (leftValue, rightValue) => leftValue ^ rightValue,
                        out destinationValue);
                case CoreScriptOperation.BitNot:
                    return WriteResult(operands[0], ~ReadScalar(operands[0]), out destinationValue);
                case CoreScriptOperation.BitCount:
                    return WriteResult(operands[0], BitOperations.PopCount((uint)ReadScalar(operands[0])),
                        out destinationValue);
                case CoreScriptOperation.Power:
                    return WriteResult(operands[0],
                        unchecked((int)Math.Pow(ReadScalar(operands[0]), Math.Max(0, ReadScalar(operands[1])))),
                        out destinationValue);
                case CoreScriptOperation.SquareRoot:
                    var squareValue = ReadScalar(operands[0]);
                    return WriteResult(operands[0], squareValue > 0 ? (int)Math.Sqrt(squareValue) : 0,
                        out destinationValue);
                case CoreScriptOperation.Absolute:
                    var absoluteValue = ReadScalar(operands[0]);
                    return WriteResult(operands[0],
                        absoluteValue == int.MinValue ? int.MinValue : Math.Abs(absoluteValue), out destinationValue);
                case CoreScriptOperation.Limit:
                    return WriteResult(operands[0],
                        Math.Max(Math.Min(ReadScalar(operands[0]), ReadScalar(operands[2])), ReadScalar(operands[1])),
                        out destinationValue);
                case CoreScriptOperation.LimitUpper:
                    return WriteResult(operands[0], Math.Min(ReadScalar(operands[0]), ReadScalar(operands[1])),
                        out destinationValue);
                case CoreScriptOperation.LimitLower:
                    return WriteResult(operands[0], Math.Max(ReadScalar(operands[0]), ReadScalar(operands[1])),
                        out destinationValue);
                case CoreScriptOperation.WaveRectangle:
                case CoreScriptOperation.WaveSaw:
                case CoreScriptOperation.WaveTriangle:
                case CoreScriptOperation.WaveSine:
                case CoreScriptOperation.WaveCosine:
                    return Wave(operation, operands, out destinationValue);
                case CoreScriptOperation.GetColor:
                    return WriteResult(operands[0], ReadScalar(operands[1]) >> 4, out destinationValue);
                case CoreScriptOperation.SetColor:
                    return WriteResult(operands[0],
                        (ReadScalar(operands[0]) & 0xF) | (ReadScalar(operands[1]) << 4), out destinationValue);
                case CoreScriptOperation.GetShade:
                    return WriteResult(operands[0], ReadScalar(operands[1]) & 0xF, out destinationValue);
                case CoreScriptOperation.SetShade:
                    return WriteResult(operands[0],
                        (ReadScalar(operands[0]) & 0xF0) | (ReadScalar(operands[1]) & 0xF), out destinationValue);
                case CoreScriptOperation.AddShade:
                    return WriteResult(operands[0], AddShade(ReadScalar(operands[0]), ReadScalar(operands[1])),
                        out destinationValue);
                case CoreScriptOperation.HostCall:
                    return InvokeHost(operands, out destinationValue);
                default:
                    return false;
            }
        }

        private bool InvokeHost(ReadOnlySpan<ScriptOperand> operands, out int? destinationValue)
        {
            destinationValue = null;
            var declaration = _program.GetBindingSlot(operands[0].Scalar);
            if (!_hostBindings.TryGet(declaration.Id, out var provider))
            {
                _failureStatus = ScriptExecutionStatus.MissingCapability;
                _failureCode = ScriptDiagnosticCodes.MissingBindingProvider;
                _failureMessage =
                    $"Script binding '{declaration.Name}' is declared but no runtime provider is installed.";
                return false;
            }

            var argumentCount = operands.Length - 1;
            var arguments = _frame.GetValueScratch(argumentCount);
            for (var index = 0; index < argumentCount; index++)
            {
                arguments[index] = ReadValue(operands[index + 1]);
            }
            ScriptBindingResult result;
            if (provider.ContextHandler is not null)
            {
                result = provider.ContextHandler(
                    new ScriptBindingContext(_frame, _options, _hostBindings, _traceSink),
                    arguments);
            }
            else
            {
                var scalarArguments = _frame.GetScalarScratch(argumentCount);
                for (var index = 0; index < argumentCount; index++)
                {
                    scalarArguments[index] = arguments[index].Scalar;
                }
                result = provider.ScalarHandler!(scalarArguments);
                if (result.Succeeded)
                {
                    for (var index = 0; index < argumentCount; index++)
                    {
                        arguments[index] = ScriptRuntimeValue.FromScalar(scalarArguments[index]);
                    }
                }
            }
            if (!result.Succeeded)
            {
                _failureStatus = result.FailureStatus ?? ScriptExecutionStatus.RuntimeError;
                _failureCode = result.DiagnosticCode ?? ScriptDiagnosticCodes.BindingOperationFailed;
                _failureMessage =
                    $"Script binding '{declaration.Name}' failed: {result.Error ?? "unspecified provider error"}";
                arguments.Clear();
                return false;
            }

            var runtimeParameterIndex = 0;
            for (var parameterIndex = 0; parameterIndex < declaration.Parameters.Count; parameterIndex++)
            {
                var parameter = declaration.Parameters[parameterIndex];
                if (parameter.Type.Id == ScriptPrimitiveTypes.Separator)
                {
                    continue;
                }
                if (parameter.Writable)
                {
                    WriteValue(operands[runtimeParameterIndex + 1].Scalar, arguments[runtimeParameterIndex]);
                    if (arguments[runtimeParameterIndex].Kind == ScriptRuntimeValueKind.Scalar)
                    {
                        destinationValue = arguments[runtimeParameterIndex].Scalar;
                    }
                }
                runtimeParameterIndex++;
            }
            arguments.Clear();
            return true;
        }

        private bool EvaluateCondition(ReadOnlySpan<ScriptOperand> operands)
        {
            var aggregate = (ScriptConditionKind)operands[0].Scalar;
            var result = aggregate == ScriptConditionKind.All;
            for (var index = 1; index < operands.Length - 1; index += 3)
            {
                var clause = Compare(
                    (ScriptConditionKind)operands[index].Scalar,
                    ReadValue(operands[index + 1]),
                    ReadValue(operands[index + 2]));
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

        private static bool Compare(
            ScriptConditionKind condition,
            ScriptRuntimeValue left,
            ScriptRuntimeValue right)
        {
            if (left.Kind != ScriptRuntimeValueKind.Scalar || right.Kind != ScriptRuntimeValueKind.Scalar)
            {
                var equal = left.Kind == right.Kind && left.Kind switch
                {
                    ScriptRuntimeValueKind.Text => string.Equals(left.Text, right.Text, StringComparison.Ordinal),
                    ScriptRuntimeValueKind.Reference => ReferenceEquals(left.Reference, right.Reference),
                    _ => false,
                };
                return condition switch
                {
                    ScriptConditionKind.Equal => equal,
                    ScriptConditionKind.NotEqual => !equal,
                    _ => false,
                };
            }
            return condition switch
            {
                ScriptConditionKind.Equal => left.Scalar == right.Scalar,
                ScriptConditionKind.NotEqual => left.Scalar != right.Scalar,
                ScriptConditionKind.LessThanOrEqual => left.Scalar <= right.Scalar,
                ScriptConditionKind.GreaterThan => left.Scalar > right.Scalar,
                ScriptConditionKind.GreaterThanOrEqual => left.Scalar >= right.Scalar,
                ScriptConditionKind.LessThan => left.Scalar < right.Scalar,
                _ => false,
            };
        }

        private bool OffsetModulo(ReadOnlySpan<ScriptOperand> operands, out int? destinationValue)
        {
            var modulo = ReadScalar(operands[3]);
            if (modulo == 0)
            {
                destinationValue = null;
                return false;
            }
            var value = (long)ReadScalar(operands[0]) * ReadScalar(operands[1]) + ReadScalar(operands[2]);
            return WriteResult(operands[0], unchecked((int)((value % modulo + modulo) % modulo)),
                out destinationValue);
        }

        private bool Divide(ReadOnlySpan<ScriptOperand> operands, bool modulo, out int? destinationValue)
        {
            var divisor = ReadScalar(operands[1]);
            if (divisor == 0)
            {
                destinationValue = null;
                return false;
            }
            var dividend = ReadScalar(operands[0]);
            var value = modulo ? (long)dividend % divisor : (long)dividend / divisor;
            return WriteResult(operands[0], unchecked((int)value), out destinationValue);
        }

        private bool MultiplyDivide(ReadOnlySpan<ScriptOperand> operands, out int? destinationValue)
        {
            var divisor = ReadScalar(operands[2]);
            if (divisor == 0)
            {
                destinationValue = null;
                return false;
            }
            var value = (long)ReadScalar(operands[0]) * ReadScalar(operands[1]) / divisor;
            return WriteResult(operands[0], unchecked((int)value), out destinationValue);
        }

        private bool Wave(
            CoreScriptOperation operation,
            ReadOnlySpan<ScriptOperand> operands,
            out int? destinationValue)
        {
            var value = ReadScalar(operands[0]);
            var period = ReadScalar(operands[1]);
            if (period <= 0)
            {
                destinationValue = null;
                return false;
            }
            var size = ReadScalar(operands[2]);
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
                var maximum = ReadScalar(operands[3]);
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

        private bool Update(
            ReadOnlySpan<ScriptOperand> operands,
            Func<int, int, int> operation,
            out int? destinationValue) =>
            WriteResult(operands[0], operation(ReadScalar(operands[0]), ReadScalar(operands[1])),
                out destinationValue);

        private bool WriteResult(ScriptOperand destination, int value, out int? destinationValue)
        {
            WriteValue(destination.Scalar, ScriptRuntimeValue.FromScalar(value));
            destinationValue = value;
            return true;
        }

        private ScriptRuntimeValue ReadValue(ScriptOperand operand) => operand.Kind switch
        {
            ScriptOperandKind.Register => _registers[RegisterIndex(_program, operand.Scalar)],
            ScriptOperandKind.Scalar => ScriptRuntimeValue.FromScalar(operand.Scalar),
            ScriptOperandKind.Text => ScriptRuntimeValue.FromText(operand.Text),
            _ => throw new InvalidOperationException($"Operand '{operand.Kind}' is not a runtime value."),
        };

        private int ReadScalar(ScriptOperand operand)
        {
            var value = ReadValue(operand);
            if (value.Kind != ScriptRuntimeValueKind.Scalar)
            {
                throw new InvalidOperationException($"Operand '{operand.Kind}' does not contain a scalar value.");
            }
            return value.Scalar;
        }

        private void WriteValue(int offset, ScriptRuntimeValue value) =>
            _registers[RegisterIndex(_program, offset)] = value;

        private ScriptExecutionOutcome Failure(
            ScriptExecutionStatus status,
            string code,
            string message,
            int instructionIndex) =>
            new(status, _steps, instructionIndex, code, message);
    }

    private static int RegisterIndex(ScriptProgram program, int offset)
    {
        if (offset % sizeof(int) != 0 || offset < 0 || offset >= program.RegisterBytes)
        {
            throw new InvalidOperationException($"Invalid compiled register offset {offset}.");
        }
        return offset / sizeof(int);
    }
}
