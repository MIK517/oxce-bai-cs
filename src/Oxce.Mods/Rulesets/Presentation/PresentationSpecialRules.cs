using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.Presentation;

public sealed record ExtraSpriteDeclaration(
    string Type,
    int Width,
    int Height,
    bool SingleImage,
    int SubX,
    int SubY,
    IReadOnlyDictionary<int, string> Files,
    RuleOperationSource Source);

public sealed record ExtraSoundDeclaration(
    string Type,
    IReadOnlyDictionary<int, string> Files,
    RuleOperationSource Source);

public sealed class PresentationSpecialRules
{
    internal PresentationSpecialRules(
        IDictionary<string, SortedDictionary<string, string>> strings,
        IDictionary<string, List<ExtraSpriteDeclaration>> sprites,
        IEnumerable<ExtraSoundDeclaration> sounds)
    {
        Strings = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(
            strings.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, string>)new ReadOnlyDictionary<string, string>(pair.Value),
                StringComparer.Ordinal));
        Sprites = new ReadOnlyDictionary<string, IReadOnlyList<ExtraSpriteDeclaration>>(
            sprites.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<ExtraSpriteDeclaration>)pair.Value.AsReadOnly(),
                StringComparer.Ordinal));
        Sounds = Array.AsReadOnly(sounds.ToArray());
    }

    [System.Text.Json.Serialization.JsonConstructor]
    internal PresentationSpecialRules(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> strings,
        IReadOnlyDictionary<string, IReadOnlyList<ExtraSpriteDeclaration>> sprites,
        IReadOnlyList<ExtraSoundDeclaration> sounds)
    {
        Strings = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(strings, StringComparer.Ordinal));
        Sprites = new ReadOnlyDictionary<string, IReadOnlyList<ExtraSpriteDeclaration>>(
            new Dictionary<string, IReadOnlyList<ExtraSpriteDeclaration>>(sprites, StringComparer.Ordinal));
        Sounds = Array.AsReadOnly(sounds.ToArray());
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<ExtraSpriteDeclaration>> Sprites { get; }

    public IReadOnlyList<ExtraSoundDeclaration> Sounds { get; }
}
