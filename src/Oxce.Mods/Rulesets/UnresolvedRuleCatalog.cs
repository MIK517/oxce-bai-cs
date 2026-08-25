using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets;

public enum RuleOperationKind
{
    Default,
    New,
    Override,
    Update,
}

public sealed record RuleOperationSource(string LayerId, string ModId, string SourcePath, SourceSpan Span);

public sealed record UnresolvedRuleOperation(
    RuleOperationKind Kind,
    YamlMappingNode Node,
    RuleOperationSource Source);

public sealed class UnresolvedRule
{
    internal UnresolvedRule(string id, IEnumerable<UnresolvedRuleOperation> operations)
    {
        Id = id;
        Operations = new ReadOnlyCollection<UnresolvedRuleOperation>(operations.ToArray());
    }

    public string Id { get; }

    public IReadOnlyList<UnresolvedRuleOperation> Operations { get; }

    public RuleOperationSource CreationSource => Operations[0].Source;

    public RuleOperationSource LastUpdateSource => Operations[^1].Source;
}

public sealed class UnresolvedRuleSection
{
    private readonly ReadOnlyDictionary<string, UnresolvedRule> _byId;

    internal UnresolvedRuleSection(RuleSectionDefinition definition, IEnumerable<UnresolvedRule> rules)
    {
        Definition = definition;
        var ordered = rules.ToArray();
        Rules = new ReadOnlyCollection<UnresolvedRule>(ordered);
        _byId = new ReadOnlyDictionary<string, UnresolvedRule>(
            ordered.ToDictionary(rule => rule.Id, StringComparer.Ordinal));
    }

    public RuleSectionDefinition Definition { get; }

    public IReadOnlyList<UnresolvedRule> Rules { get; }

    public bool TryGet(string id, out UnresolvedRule? rule)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.TryGetValue(id, out rule);
    }
}

public sealed class UnresolvedRuleCatalog
{
    private readonly ReadOnlyDictionary<string, UnresolvedRuleSection> _byName;
    private readonly ContentLoadCapabilities _capabilities;

    internal UnresolvedRuleCatalog(IEnumerable<UnresolvedRuleSection> sections)
    {
        var ordered = sections.ToArray();
        Sections = new ReadOnlyCollection<UnresolvedRuleSection>(ordered);
        _byName = new ReadOnlyDictionary<string, UnresolvedRuleSection>(
            ordered.ToDictionary(section => section.Definition.Name, StringComparer.Ordinal));
        _capabilities = ContentLoadCapabilities.Composed;
    }

    public IReadOnlyList<UnresolvedRuleSection> Sections { get; }

    public ContentLoadCapabilities Capabilities => _capabilities;

    public bool TryGetSection(string name, out UnresolvedRuleSection? section)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out section);
    }
}
