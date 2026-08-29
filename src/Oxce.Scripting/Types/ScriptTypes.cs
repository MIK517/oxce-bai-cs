namespace Oxce.Scripting.Types;

public readonly record struct ScriptTypeId(ushort Value)
{
    public bool IsValid => Value != 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public static class ScriptPrimitiveTypes
{
    public static ScriptTypeId Null { get; } = new(1);
    public static ScriptTypeId Scalar { get; } = new(2);
    public static ScriptTypeId Label { get; } = new(3);
    public static ScriptTypeId Text { get; } = new(4);
    public static ScriptTypeId Separator { get; } = new(5);

    public const ushort FirstCustomTypeValue = 6;
}

[Flags]
public enum ScriptTypeModifier : byte
{
    None = 0,
    Register = 1 << 0,
    Writable = 1 << 1,
    Reference = 1 << 2,
    EditableReference = 1 << 3,
}

public readonly record struct ScriptTypeRef
{
    public ScriptTypeRef(ScriptTypeId id, ScriptTypeModifier modifiers = ScriptTypeModifier.None)
    {
        if (!id.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }
        if ((modifiers & ~AllModifiers) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modifiers));
        }
        if (modifiers.HasFlag(ScriptTypeModifier.Writable) && !modifiers.HasFlag(ScriptTypeModifier.Register))
        {
            throw new ArgumentException("Writable script values must be registers.", nameof(modifiers));
        }
        if (modifiers.HasFlag(ScriptTypeModifier.EditableReference) && !modifiers.HasFlag(ScriptTypeModifier.Reference))
        {
            throw new ArgumentException("Editable script references must be references.", nameof(modifiers));
        }

        Id = id;
        Modifiers = modifiers;
    }

    public ScriptTypeId Id { get; }

    public ScriptTypeModifier Modifiers { get; }

    public bool IsRegister => Modifiers.HasFlag(ScriptTypeModifier.Register);

    public bool IsWritable => Modifiers.HasFlag(ScriptTypeModifier.Writable);

    public bool IsReference => Modifiers.HasFlag(ScriptTypeModifier.Reference);

    public bool IsEditableReference => Modifiers.HasFlag(ScriptTypeModifier.EditableReference);

    private const ScriptTypeModifier AllModifiers = ScriptTypeModifier.Register | ScriptTypeModifier.Writable |
        ScriptTypeModifier.Reference | ScriptTypeModifier.EditableReference;
}

public sealed record ScriptTypeDefinition(
    ScriptTypeId Id,
    string Name,
    int Size,
    int Alignment)
{
    public ScriptTypeDefinition Validate()
    {
        if (!Id.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(Id));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Size);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Alignment);
        if ((Alignment & (Alignment - 1)) != 0)
        {
            throw new ArgumentException("Script type alignment must be a power of two.", nameof(Alignment));
        }
        return this;
    }
}
