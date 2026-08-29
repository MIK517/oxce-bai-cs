using Oxce.Core.Diagnostics;
using Oxce.Scripting.Binding;
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
    public ScriptProgram(
        string parserName,
        IEnumerable<ScriptInstruction> instructions,
        IEnumerable<ScriptTypeRef> outputs,
        int registerBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parserName);
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentOutOfRangeException.ThrowIfNegative(registerBytes);
        ParserName = parserName;
        Instructions = Array.AsReadOnly(instructions.ToArray());
        Outputs = Array.AsReadOnly(outputs.ToArray());
        RegisterBytes = registerBytes;
        if (Outputs.Count > ScriptLimits.MaximumOutputs)
        {
            throw new ArgumentOutOfRangeException(nameof(outputs));
        }
        ArgumentOutOfRangeException.ThrowIfGreaterThan(registerBytes, ScriptLimits.MaximumRegisterBytes);
    }

    public string ParserName { get; }

    public IReadOnlyList<ScriptInstruction> Instructions { get; }

    public IReadOnlyList<ScriptTypeRef> Outputs { get; }

    public int RegisterBytes { get; }
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
