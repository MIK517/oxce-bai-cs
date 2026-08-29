using System.Text.Json;
using Oxce.Scripting.Types;

namespace Oxce.Scripting.Api;

public static class ReferenceScriptApiCatalog
{
    private const string ResourceName = "Oxce.Scripting.script-api-catalog.json";
    private static readonly ScriptReferenceLocation Reference = new("src/Engine/Script.cpp", 3906);
    private static readonly Lazy<ScriptApiCatalog> Catalog = new(LoadCore);

    public static ScriptApiCatalog Instance => Catalog.Value;

    private static ScriptApiCatalog LoadCore()
    {
        using var stream = typeof(ReferenceScriptApiCatalog).Assembly.GetManifestResourceStream(ResourceName) ??
            throw new InvalidOperationException($"Embedded script API catalog '{ResourceName}' is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        RequireCount(root, "unresolved", 0);

        var typeTokens = root.GetProperty("typeTokens").EnumerateArray()
            .Select(static token => token.GetString() ?? throw new InvalidDataException("Null script type token."))
            .ToArray();
        var types = CreateTypes(typeTokens);
        var typesByName = types.ToDictionary(static type => type.Name, StringComparer.Ordinal);

        var bindings = root.GetProperty("bindings").EnumerateArray().Select(binding =>
        {
            var parameters = binding.GetProperty("parameters").EnumerateArray().Select((parameter, index) =>
            {
                var type = ParseType(parameter.GetString()!, typesByName);
                return new ScriptBindingParameter($"argument{index}", type, type.IsWritable);
            });
            return new ScriptBindingDeclaration(
                new ScriptBindingId(binding.GetProperty("id").GetInt32()),
                binding.GetProperty("name").GetString()!,
                parameters,
                Strings(binding, "parsers"),
                Reference);
        }).ToArray();

        var constants = root.GetProperty("constants").EnumerateArray().Select(constant =>
            new ScriptConstantDeclaration(
                constant.GetProperty("name").GetString()!,
                constant.GetProperty("value").GetInt32(),
                Strings(constant, "parsers"),
                Reference)).ToArray();

        var parsers = root.GetProperty("parsers").EnumerateArray().Select(parser =>
            new ScriptParserDeclaration(
                parser.GetProperty("name").GetString()!,
                parser.GetProperty("group").GetString()!,
                Values(parser, "outputs", typesByName),
                Values(parser, "inputs", typesByName),
                parser.GetProperty("supportsEvents").GetBoolean(),
                Reference)).ToArray();

        RequireCount(root, "parsers", parsers.Length);
        RequireCount(root, "bindings", bindings.Length);
        RequireCount(root, "constants", constants.Length);
        RequireCount(root, "typeTokens", typeTokens.Length);
        return new ScriptApiCatalog(bindings, constants, parsers, types);
    }

    private static ScriptTypeDefinition[] CreateTypes(IEnumerable<string> tokens)
    {
        var customNames = tokens.Select(BaseTypeName)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var types = new List<ScriptTypeDefinition>
        {
            new(ScriptPrimitiveTypes.Null, "null", IntPtr.Size, IntPtr.Size),
            new(ScriptPrimitiveTypes.Scalar, "int", sizeof(int), sizeof(int)),
            new(ScriptPrimitiveTypes.Label, "label", sizeof(int), sizeof(int)),
            new(ScriptPrimitiveTypes.Text, "text", IntPtr.Size, IntPtr.Size),
            new(ScriptPrimitiveTypes.Separator, "__", sizeof(int), sizeof(int)),
        };
        for (var index = 0; index < customNames.Length; index++)
        {
            var name = customNames[index];
            var size = name == "Position" ? 3 * sizeof(int) : sizeof(int);
            types.Add(new ScriptTypeDefinition(
                new ScriptTypeId(checked((ushort)(ScriptPrimitiveTypes.FirstCustomTypeValue + index))),
                name,
                size,
                sizeof(int)));
        }
        return types.ToArray();
    }

    private static ScriptNamedValueDeclaration[] Values(
        JsonElement owner,
        string property,
        IReadOnlyDictionary<string, ScriptTypeDefinition> types) =>
        owner.GetProperty(property).EnumerateArray().Select(value => new ScriptNamedValueDeclaration(
            value.GetProperty("name").GetString()!,
            ParseType(value.GetProperty("type").GetString()!, types))).ToArray();

    private static ScriptTypeRef ParseType(
        string token,
        IReadOnlyDictionary<string, ScriptTypeDefinition> types)
    {
        var remaining = token;
        var modifiers = ScriptTypeModifier.None;
        if (remaining.StartsWith("var ", StringComparison.Ordinal))
        {
            modifiers |= ScriptTypeModifier.Register | ScriptTypeModifier.Writable;
            remaining = remaining[4..];
        }
        if (remaining.StartsWith("ptre ", StringComparison.Ordinal))
        {
            modifiers |= ScriptTypeModifier.Reference | ScriptTypeModifier.EditableReference;
            remaining = remaining[5..];
        }
        else if (remaining.StartsWith("ptr ", StringComparison.Ordinal))
        {
            modifiers |= ScriptTypeModifier.Reference;
            remaining = remaining[4..];
        }

        var id = remaining switch
        {
            "" or "null" => ScriptPrimitiveTypes.Null,
            "int" => ScriptPrimitiveTypes.Scalar,
            "text" => ScriptPrimitiveTypes.Text,
            "__" => ScriptPrimitiveTypes.Separator,
            _ when types.TryGetValue(remaining, out var definition) => definition.Id,
            _ => throw new InvalidDataException($"Unknown script type token '{token}'."),
        };
        return new ScriptTypeRef(id, modifiers);
    }

    private static string? BaseTypeName(string token)
    {
        var remaining = token.StartsWith("var ", StringComparison.Ordinal) ? token[4..] : token;
        if (remaining.StartsWith("ptre ", StringComparison.Ordinal))
        {
            remaining = remaining[5..];
        }
        else if (remaining.StartsWith("ptr ", StringComparison.Ordinal))
        {
            remaining = remaining[4..];
        }
        return remaining is "" or "null" or "int" or "text" or "__" ? null : remaining;
    }

    private static string[] Strings(JsonElement owner, string property) =>
        owner.GetProperty(property).EnumerateArray().Select(static item => item.GetString()!).ToArray();

    private static void RequireCount(JsonElement root, string property, int actual)
    {
        var expected = root.GetProperty("counts").GetProperty(property).GetInt32();
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Script API catalog count '{property}' is {actual}; expected {expected}.");
        }
    }
}
