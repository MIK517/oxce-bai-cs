using Oxce.Scripting.Api;

namespace Oxce.Scripting.Runtime;

public readonly record struct ScriptBindingResult(bool Succeeded, string? Error)
{
    public static ScriptBindingResult Success { get; } = new(true, null);

    public static ScriptBindingResult Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new ScriptBindingResult(false, error);
    }
}

public delegate ScriptBindingResult ScriptBindingHandler(Span<int> arguments);

public sealed class ScriptHostBindings
{
    private readonly Dictionary<int, ScriptBindingHandler> _handlers;

    public ScriptHostBindings(IEnumerable<KeyValuePair<ScriptBindingId, ScriptBindingHandler>> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToDictionary(static item => item.Key.Value, static item => item.Value);
    }

    public static ScriptHostBindings Empty { get; } = new([]);

    public bool TryGet(ScriptBindingId id, out ScriptBindingHandler? handler) =>
        _handlers.TryGetValue(id.Value, out handler);
}

public sealed class ScriptHostBindingsBuilder
{
    private readonly Dictionary<int, ScriptBindingHandler> _handlers = [];

    public void Add(ScriptBindingId id, ScriptBindingHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(id.Value, handler))
        {
            throw new ArgumentException($"A provider for script binding {id.Value} is already installed.", nameof(id));
        }
    }

    public ScriptHostBindings Build() => new(
        _handlers.Select(static item =>
            new KeyValuePair<ScriptBindingId, ScriptBindingHandler>(new ScriptBindingId(item.Key), item.Value)));
}
