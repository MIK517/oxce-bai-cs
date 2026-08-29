using Oxce.Scripting.Types;

namespace Oxce.Scripting.Compilation;

public sealed class ScriptRegisterLayout
{
    private readonly int _maximumBytes;
    private readonly Stack<int> _scopeStarts = new();

    public ScriptRegisterLayout(int? maximumBytes = null)
    {
        _maximumBytes = maximumBytes ?? ScriptLimits.MaximumRegisterBytes;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_maximumBytes);
    }

    public int UsedBytes { get; private set; }

    public int ScopeDepth => _scopeStarts.Count + 1;

    public void PushScope() => _scopeStarts.Push(UsedBytes);

    public void PopScope()
    {
        if (!_scopeStarts.TryPop(out var start))
        {
            throw new InvalidOperationException("The root register scope cannot be removed.");
        }
        UsedBytes = start;
    }

    public bool TryAllocate(ScriptTypeDefinition type, bool useReferenceLayout, out int offset)
    {
        ArgumentNullException.ThrowIfNull(type);
        type.Validate();
        var size = useReferenceLayout ? IntPtr.Size : type.Size;
        var alignment = useReferenceLayout ? IntPtr.Size : type.Alignment;
        var aligned = checked((UsedBytes + alignment - 1) & ~(alignment - 1));
        if (aligned > _maximumBytes - size)
        {
            offset = -1;
            return false;
        }

        offset = aligned;
        UsedBytes = aligned + size;
        return true;
    }
}
