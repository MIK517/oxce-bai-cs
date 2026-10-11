using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Deterministic dump of every instance field reachable from an object graph. Used to prove that content
/// restored from the compiled cache is indistinguishable from a fresh build without relying on the cache's own
/// serializer. Content generation identities are excluded because each load intentionally receives a new one.
/// </summary>
internal static class ContentStateDump
{
    public static string Of(object? root)
    {
        var writer = new Writer();
        writer.Write(root, 0);
        return writer.ToString();
    }

    private sealed class Writer
    {
        private readonly StringBuilder _text = new();
        // Shared references are dumped structurally (restored content does not keep reference identity);
        // completed objects are memoized and ancestors still being written are reported as cycles.
        private readonly Dictionary<object, string> _done;
        private readonly HashSet<object> _active;

        public Writer() : this(new(ReferenceEqualityComparer.Instance), new(ReferenceEqualityComparer.Instance)) { }

        private Writer(Dictionary<object, string> done, HashSet<object> active) { _done = done; _active = active; }

        public override string ToString() => _text.ToString();

        public void Write(object? value, int depth)
        {
            if (depth > 200) throw new InvalidOperationException("Content dump exceeded the supported depth.");
            switch (value)
            {
                case null: _text.Append("null"); return;
                case string text: _text.Append('"').Append(text).Append('"'); return;
                case double number: _text.Append(number.ToString("R", CultureInfo.InvariantCulture)); return;
                case float number: _text.Append(number.ToString("R", CultureInfo.InvariantCulture)); return;
                case IFormattable formattable when value.GetType().IsPrimitive || value is decimal || value is Enum:
                    _text.Append(formattable.ToString(null, CultureInfo.InvariantCulture)); return;
                case bool flag: _text.Append(flag ? "true" : "false"); return;
                case char character: _text.Append('\'').Append(character).Append('\''); return;
                case Type type: _text.Append("typeof(").Append(type.FullName).Append(')'); return;
                case Delegate: _text.Append("delegate"); return;
            }
            var runtimeType = value.GetType();
            if (runtimeType.Name == "ContentGenerationId") { _text.Append("generation"); return; }
            if (runtimeType.IsValueType) { WriteBody(value, runtimeType, depth); return; }
            if (_done.TryGetValue(value, out var known)) { _text.Append(known); return; }
            if (!_active.Add(value)) { _text.Append("<cycle:").Append(runtimeType.Name).Append('>'); return; }
            var nested = new Writer(_done, _active);
            nested.WriteBody(value, runtimeType, depth);
            _active.Remove(value);
            var dumped = nested.ToString();
            _done[value] = dumped;
            _text.Append(dumped);
        }

        private void WriteBody(object value, Type runtimeType, int depth)
        {
            // Concrete collection types are part of the contract: restored content must not expose mutable
            // collections where the fresh build publishes read-only wrappers.
            _text.Append(runtimeType.Name);
            if (value is IDictionary dictionary)
            {
                var entries = new List<(string Key, object? Value)>();
                foreach (DictionaryEntry entry in dictionary) entries.Add((Nested(entry.Key, depth), entry.Value));
                _text.Append('{');
                foreach (var (key, item) in entries.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
                {
                    _text.Append(key).Append(':');
                    Write(item, depth + 1);
                    _text.Append(',');
                }
                _text.Append('}');
                return;
            }
            if (value is IEnumerable sequence && !IsOwnType(runtimeType))
            {
                var items = new List<object?>();
                foreach (var item in sequence) items.Add(item);
                // Sets and dictionaries exposed only through generic interfaces have no stable order.
                var unordered = runtimeType.GetInterfaces().Any(static type => type.IsGenericType &&
                    (type.GetGenericTypeDefinition() == typeof(IReadOnlySet<>) || type.GetGenericTypeDefinition() == typeof(ISet<>) ||
                     type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
                _text.Append('[');
                if (unordered)
                    foreach (var text in items.Select(item => Nested(item, depth)).Order(StringComparer.Ordinal))
                        _text.Append(text).Append(',');
                else
                    foreach (var item in items) { Write(item, depth + 1); _text.Append(','); }
                _text.Append(']');
                return;
            }
            if (!IsOwnType(runtimeType) && !runtimeType.IsGenericType)
            {
                _text.Append('(').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(')');
                return;
            }
            _text.Append('{');
            for (var type = runtimeType; type is not null && type != typeof(object); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .OrderBy(static field => field.Name, StringComparer.Ordinal))
                {
                    _text.Append(field.Name).Append('=');
                    Write(field.GetValue(value), depth + 1);
                    _text.Append(';');
                }
            _text.Append('}');
        }

        private string Nested(object? value, int depth)
        {
            var nested = new Writer(_done, _active);
            nested.Write(value, depth + 1);
            return nested.ToString();
        }

        private static bool IsOwnType(Type type) => type.Namespace?.StartsWith("Oxce.", StringComparison.Ordinal) == true;
    }
}
