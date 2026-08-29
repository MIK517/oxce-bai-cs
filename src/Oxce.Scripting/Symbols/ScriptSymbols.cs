using Oxce.Scripting.Types;

namespace Oxce.Scripting.Symbols;

public enum ScriptSymbolKind
{
    Parameter,
    Local,
    Constant,
    Label,
}

public sealed record ScriptSymbol(
    string Name,
    ScriptSymbolKind Kind,
    ScriptTypeRef Type,
    int? RegisterOffset = null,
    object? ConstantValue = null)
{
    public ScriptSymbol Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (RegisterOffset is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RegisterOffset));
        }
        if (Kind is (ScriptSymbolKind.Parameter or ScriptSymbolKind.Local) && RegisterOffset is null)
        {
            throw new ArgumentException("Register-backed symbols require an offset.", nameof(RegisterOffset));
        }
        if (Kind == ScriptSymbolKind.Constant && ConstantValue is null)
        {
            throw new ArgumentException("Constants require a value.", nameof(ConstantValue));
        }
        return this;
    }
}

public sealed class ScriptSymbolTable
{
    private readonly List<Dictionary<string, ScriptSymbol>> _scopes =
        [new Dictionary<string, ScriptSymbol>(StringComparer.Ordinal)];

    public int ScopeDepth => _scopes.Count;

    public void PushScope() => _scopes.Add(new Dictionary<string, ScriptSymbol>(StringComparer.Ordinal));

    public void PopScope()
    {
        if (_scopes.Count == 1)
        {
            throw new InvalidOperationException("The root script scope cannot be removed.");
        }
        _scopes.RemoveAt(_scopes.Count - 1);
    }

    public bool TryDeclare(ScriptSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        symbol.Validate();
        if (TryResolve(symbol.Name, out _))
        {
            return false;
        }
        _scopes[^1].Add(symbol.Name, symbol);
        return true;
    }

    public bool TryResolve(string name, out ScriptSymbol? symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        for (var index = _scopes.Count - 1; index >= 0; index--)
        {
            if (_scopes[index].TryGetValue(name, out symbol))
            {
                return true;
            }
        }
        symbol = null;
        return false;
    }
}
