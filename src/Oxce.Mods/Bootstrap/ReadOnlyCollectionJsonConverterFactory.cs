using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oxce.Mods.Bootstrap;

/// <summary>
/// Restores <see cref="IReadOnlyList{T}"/>, <see cref="IReadOnlyCollection{T}"/> and
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> members of cached content as read-only wrappers, matching
/// the fresh build. The default deserializer would substitute mutable lists and dictionaries that remain
/// reachable through the published runtime rules. The JSON shape is unchanged.
/// </summary>
internal sealed class ReadOnlyCollectionJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType) return false;
        var definition = typeToConvert.GetGenericTypeDefinition();
        return definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) ||
            definition == typeof(IReadOnlyDictionary<,>);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var arguments = typeToConvert.GetGenericArguments();
        var converter = typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
            ? typeof(DictionaryConverter<,>).MakeGenericType(arguments)
            : typeof(ListConverter<,>).MakeGenericType(typeToConvert, arguments[0]);
        return (JsonConverter)Activator.CreateInstance(converter)!;
    }

    private sealed class ListConverter<TCollection, TItem> : JsonConverter<TCollection>
        where TCollection : class, IReadOnlyCollection<TItem>
    {
        public override TCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected a cached list.");
            var converter = Element<TItem>.Info(options);
            var items = new List<TItem>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                items.Add(Element<TItem>.Read(ref reader, converter, options));
            return (TCollection)(object)new ReadOnlyCollection<TItem>(items);
        }

        public override void Write(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value) JsonSerializer.Serialize(writer, item, options);
            writer.WriteEndArray();
        }
    }

    private sealed class DictionaryConverter<TKey, TValue> : JsonConverter<IReadOnlyDictionary<TKey, TValue>>
        where TKey : notnull
    {
        public override IReadOnlyDictionary<TKey, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected a cached dictionary.");
            var converter = Element<TValue>.Info(options);
            var items = new Dictionary<TKey, TValue>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var key = ParseKey(reader.GetString()!);
                reader.Read();
                items.Add(key, Element<TValue>.Read(ref reader, converter, options));
            }
            return new ReadOnlyDictionary<TKey, TValue>(items);
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<TKey, TValue> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, item) in value)
            {
                writer.WritePropertyName(Convert.ToString(key, CultureInfo.InvariantCulture)!);
                JsonSerializer.Serialize(writer, item, options);
            }
            writer.WriteEndObject();
        }

        private static TKey ParseKey(string text) => typeof(TKey).IsEnum
            ? (TKey)Enum.Parse(typeof(TKey), text)
            : (TKey)Convert.ChangeType(text, typeof(TKey), CultureInfo.InvariantCulture);
    }

    /// <summary>Element reads through the resolved element converter, with direct primitive paths.</summary>
    private static class Element<T>
    {
        // A single reference keeps the cached pair consistent across concurrent loads.
        private static Cached? _cached;

        public static JsonConverter<T>? Info(JsonSerializerOptions options)
        {
            if (typeof(T) == typeof(string) || typeof(T) == typeof(int) || typeof(T) == typeof(ulong) ||
                typeof(T) == typeof(short) || typeof(T) == typeof(bool)) return null;
            if (_cached is { } cached && ReferenceEquals(cached.Options, options)) return cached.Converter;
            var converter = (JsonConverter<T>)options.GetConverter(typeof(T));
            _cached = new Cached(options, converter);
            return converter;
        }

        private sealed record Cached(JsonSerializerOptions Options, JsonConverter<T> Converter);

        public static T Read(ref Utf8JsonReader reader, JsonConverter<T>? converter, JsonSerializerOptions options)
        {
            if (converter is not null)
                return reader.TokenType == JsonTokenType.Null && !converter.HandleNull ? default! : converter.Read(ref reader, typeof(T), options)!;
            if (typeof(T) == typeof(string)) return (T)(object)reader.GetString()!;
            if (typeof(T) == typeof(int)) return (T)(object)reader.GetInt32();
            if (typeof(T) == typeof(ulong)) return (T)(object)reader.GetUInt64();
            if (typeof(T) == typeof(short)) return (T)(object)reader.GetInt16();
            return (T)(object)reader.GetBoolean();
        }
    }
}
