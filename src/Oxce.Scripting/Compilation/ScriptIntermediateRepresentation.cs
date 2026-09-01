using Oxce.Core.Diagnostics;
using Oxce.Scripting.Binding;
using Oxce.Scripting.Api;
using Oxce.Scripting.Types;

namespace Oxce.Scripting.Compilation;

public enum ScriptOperandKind
{
    Register,
    Scalar,
    Text,
    Label,
    Binding,
}

public readonly record struct ScriptOperand
{
    private ScriptOperand(ScriptOperandKind kind, int scalar, string? text)
    {
        Kind = kind;
        Scalar = scalar;
        Text = text;
    }

    public ScriptOperandKind Kind { get; }

    public int Scalar { get; }

    public string? Text { get; }

    public static ScriptOperand Register(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return new ScriptOperand(ScriptOperandKind.Register, offset, null);
    }

    public static ScriptOperand IntegerValue(int value) =>
        new(ScriptOperandKind.Scalar, value, null);

    public static ScriptOperand TextValue(string value) =>
        new(ScriptOperandKind.Text, 0, value ?? throw new ArgumentNullException(nameof(value)));

    public static ScriptOperand Label(int instructionIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(instructionIndex);
        return new ScriptOperand(ScriptOperandKind.Label, instructionIndex, null);
    }

    public static ScriptOperand Binding(int bindingId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bindingId);
        return new ScriptOperand(ScriptOperandKind.Binding, bindingId, null);
    }

    internal static ScriptOperand BindingSlot(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        return new ScriptOperand(ScriptOperandKind.Binding, slot, null);
    }
}

public sealed class ScriptInstruction
{
    public ScriptInstruction(
        ScriptOperationId operation,
        IEnumerable<ScriptOperand> operands,
        SourceSpan source)
    {
        ArgumentNullException.ThrowIfNull(operands);
        Operation = operation;
        Operands = Array.AsReadOnly(operands.ToArray());
        Source = source;
    }

    public ScriptOperationId Operation { get; }

    public IReadOnlyList<ScriptOperand> Operands { get; }

    public SourceSpan Source { get; }
}

public sealed class ScriptProgram
{
    private readonly PackedInstruction[] _instructionStorage;
    private readonly ScriptOperand[] _operandStorage;
    private readonly SourceSpan[] _sourceMap;
    private readonly ScriptBindingDeclaration[] _bindingSlots;
    private readonly ScriptRegisterDefinition[] _outputRegisters;

    public ScriptProgram(
        string parserName,
        IEnumerable<ScriptInstruction> instructions,
        IEnumerable<ScriptTypeRef> outputs,
        int registerBytes,
        IEnumerable<ScriptRegisterDefinition>? registers = null,
        IEnumerable<ScriptBindingDeclaration>? bindings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parserName);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentOutOfRangeException.ThrowIfNegative(registerBytes);
        ParserName = parserName;
        var instructionArray = instructions.ToArray();
        Outputs = Array.AsReadOnly(outputs.ToArray());
        Registers = Array.AsReadOnly((registers ?? []).ToArray());
        _outputRegisters = Registers.Where(static register => register.IsOutput).ToArray();
        _bindingSlots = (bindings ?? []).ToArray();
        Bindings = Array.AsReadOnly(_bindingSlots);
        RegisterBytes = registerBytes;
        if (Outputs.Count > ScriptLimits.MaximumOutputs)
        {
            throw new ArgumentOutOfRangeException(nameof(outputs));
        }
        ArgumentOutOfRangeException.ThrowIfGreaterThan(registerBytes, ScriptLimits.MaximumRegisterBytes);

        var bindingSlotsById = _bindingSlots
            .Select(static (binding, slot) => (binding.Id.Value, Slot: slot))
            .ToDictionary(static item => item.Value, static item => item.Slot);
        _instructionStorage = new PackedInstruction[instructionArray.Length];
        _sourceMap = new SourceSpan[instructionArray.Length];
        var operandBuilder = new List<ScriptOperand>(
            instructionArray.Sum(static instruction => instruction.Operands.Count));
        for (var instructionIndex = 0; instructionIndex < instructionArray.Length; instructionIndex++)
        {
            var instruction = instructionArray[instructionIndex];
            _sourceMap[instructionIndex] = instruction.Source;
            var operandOffset = operandBuilder.Count;
            ScriptBindingDeclaration? hostBinding = null;
            for (var operandIndex = 0; operandIndex < instruction.Operands.Count; operandIndex++)
            {
                var operand = instruction.Operands[operandIndex];
                if (operand.Kind == ScriptOperandKind.Binding)
                {
                    if (!bindingSlotsById.TryGetValue(operand.Scalar, out var slot))
                    {
                        throw new ArgumentException(
                            $"Instruction {instructionIndex} references undeclared binding {operand.Scalar}.",
                            nameof(instructions));
                    }
                    hostBinding = _bindingSlots[slot];
                    operand = ScriptOperand.BindingSlot(slot);
                }
                else if (hostBinding is not null &&
                    hostBinding.Parameters[operandIndex - 1].Type.Id == ScriptPrimitiveTypes.Separator)
                {
                    continue;
                }
                operandBuilder.Add(operand);
            }
            _instructionStorage[instructionIndex] = new PackedInstruction(
                instruction.Operation,
                operandOffset,
                checked((ushort)(operandBuilder.Count - operandOffset)));
        }
        _operandStorage = operandBuilder.ToArray();
        Instructions = new PackedInstructionList(this);
    }

    public string ParserName { get; }

    public IReadOnlyList<ScriptInstruction> Instructions { get; }

    public IReadOnlyList<ScriptTypeRef> Outputs { get; }

    public int RegisterBytes { get; }

    public IReadOnlyList<ScriptRegisterDefinition> Registers { get; }

    public IReadOnlyList<ScriptBindingDeclaration> Bindings { get; }

    internal int InstructionCount => _instructionStorage.Length;

    internal PackedInstruction GetPackedInstruction(int index) => _instructionStorage[index];

    internal ReadOnlySpan<ScriptOperand> GetPackedOperands(PackedInstruction instruction) =>
        _operandStorage.AsSpan(instruction.OperandOffset, instruction.OperandCount);

    internal SourceSpan GetSource(int instructionIndex) => _sourceMap[instructionIndex];

    internal ScriptBindingDeclaration GetBindingSlot(int slot) => _bindingSlots[slot];

    internal ReadOnlySpan<ScriptRegisterDefinition> OutputRegisters => _outputRegisters;

    public int RegisterSlotCount => checked((RegisterBytes + sizeof(int) - 1) / sizeof(int));

    private ScriptInstruction MaterializeInstruction(int index)
    {
        var packed = _instructionStorage[index];
        var operands = GetPackedOperands(packed).ToArray();
        for (var operandIndex = 0; operandIndex < operands.Length; operandIndex++)
        {
            if (operands[operandIndex].Kind == ScriptOperandKind.Binding)
            {
                operands[operandIndex] = ScriptOperand.Binding(_bindingSlots[operands[operandIndex].Scalar].Id.Value);
            }
        }
        return new ScriptInstruction(packed.Operation, operands, _sourceMap[index]);
    }

    internal readonly record struct PackedInstruction(
        ScriptOperationId Operation,
        int OperandOffset,
        ushort OperandCount);

    private sealed class PackedInstructionList(ScriptProgram owner) : IReadOnlyList<ScriptInstruction>
    {
        public int Count => owner._instructionStorage.Length;

        public ScriptInstruction this[int index] => owner.MaterializeInstruction(index);

        public IEnumerator<ScriptInstruction> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public sealed record ScriptRegisterDefinition(
    string Name,
    ScriptTypeRef Type,
    int Offset,
    bool IsOutput)
{
    public ScriptRegisterDefinition Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentOutOfRangeException.ThrowIfNegative(Offset);
        return this;
    }
}

public sealed class ScriptCompileResult
{
    public ScriptCompileResult(ScriptProgram? program, IEnumerable<DiagnosticEvent> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Program = program;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        if (program is not null && Diagnostics.Any(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error))
        {
            throw new ArgumentException("A successful script compile result cannot contain error diagnostics.", nameof(diagnostics));
        }
    }

    public ScriptProgram? Program { get; }

    public IReadOnlyList<DiagnosticEvent> Diagnostics { get; }

    public bool Succeeded => Program is not null;
}
