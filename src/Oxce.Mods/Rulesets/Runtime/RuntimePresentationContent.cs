using System.Collections.Frozen;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed class RuntimePresentationContent
{
    internal RuntimePresentationContent(PresentationSpecialRules source)
    {
        FontName = source.FontName;
        Strings = source.Strings.ToFrozenDictionary(p => p.Key,
            p => (IReadOnlyDictionary<string, string>)p.Value.ToFrozenDictionary(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    public string FontName { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings { get; }
}
