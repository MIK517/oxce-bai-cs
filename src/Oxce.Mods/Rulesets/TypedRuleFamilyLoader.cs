using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;

namespace Oxce.Mods.Rulesets;

public abstract class TypedRuleFamilyLoader<TBuilder, TRule>
    where TBuilder : notnull
    where TRule : notnull
{
    protected TypedRuleFamilyLoader(RuleSectionDefinition section)
    {
        ArgumentNullException.ThrowIfNull(section);
        Section = section;
    }

    public RuleSectionDefinition Section { get; }

    protected abstract TBuilder Create(UnresolvedRule rule);

    protected abstract void Apply(TBuilder builder, RulePropertyReader reader);

    protected abstract TRule Freeze(TBuilder builder);

    public TypedRuleSection<TRule> Load(
        UnresolvedRuleSection unresolved,
        IDiagnosticSink? diagnostics = null,
        TypedRuleLoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(unresolved);
        if (!string.Equals(unresolved.Definition.Name, Section.Name, StringComparison.Ordinal) ||
            !string.Equals(unresolved.Definition.IdentityKey, Section.IdentityKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Loader for section '{Section.Name}' cannot load section '{unresolved.Definition.Name}'.",
                nameof(unresolved));
        }

        diagnostics ??= NullDiagnosticSink.Instance;
        options ??= new TypedRuleLoadOptions();
        options.Validate();
        var budget = new RulePropertyReader.PropertyNodeBudget(options.MaximumPropertyNodes);
        var rules = new List<TypedRule<TRule>>(unresolved.Rules.Count);
        foreach (var unresolvedRule in unresolved.Rules)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            var builder = Create(unresolvedRule);
            var deferredProperties = new List<DeferredRuleProperty>();
            foreach (var operation in unresolvedRule.Operations)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                var reader = new RulePropertyReader(
                    operation.Node,
                    operation.Source,
                    Section,
                    unresolvedRule.Id,
                    diagnostics,
                    options,
                    budget,
                    deferredProperties,
                    consumeLifecycleKeys: true);
                Apply(builder, reader);
                reader.Complete();
            }

            rules.Add(new TypedRule<TRule>(
                unresolvedRule.Id,
                Freeze(builder),
                unresolvedRule.CreationSource,
                unresolvedRule.LastUpdateSource,
                new RuleCompatibilityData(deferredProperties)));
        }

        return new TypedRuleSection<TRule>(Section, rules);
    }
}

public abstract class IdOnlyTypedRuleFamilyLoader<TBuilder, TRule> :
    TypedRuleFamilyLoader<TBuilder, TRule>
    where TBuilder : notnull
    where TRule : notnull
{
    protected IdOnlyTypedRuleFamilyLoader(RuleSectionDefinition section)
        : base(section)
    {
    }

    protected sealed override TBuilder Create(UnresolvedRule rule) => Create(rule.Id);

    protected abstract TBuilder Create(string id);
}

public sealed class RuleCompatibilityData
{
    [System.Text.Json.Serialization.JsonConstructor]
    internal RuleCompatibilityData(IReadOnlyList<DeferredRuleProperty> deferredProperties)
    {
        DeferredProperties = Array.AsReadOnly(deferredProperties.ToArray());
    }

    public IReadOnlyList<DeferredRuleProperty> DeferredProperties { get; }
}

public sealed record TypedRule<TRule>(
    string Id,
    TRule Value,
    RuleOperationSource CreationSource,
    RuleOperationSource LastUpdateSource,
    RuleCompatibilityData CompatibilityData)
    where TRule : notnull
{
    public IReadOnlyList<DeferredRuleProperty> DeferredProperties => CompatibilityData.DeferredProperties;
}

public sealed class TypedRuleSection<TRule>
    where TRule : notnull
{
    private readonly ReadOnlyDictionary<string, TypedRule<TRule>> _byId;

    [System.Text.Json.Serialization.JsonConstructor]
    internal TypedRuleSection(RuleSectionDefinition definition, IReadOnlyList<TypedRule<TRule>> rules)
    {
        Definition = definition;
        var ordered = rules.ToArray();
        Rules = new ReadOnlyCollection<TypedRule<TRule>>(ordered);
        _byId = new ReadOnlyDictionary<string, TypedRule<TRule>>(
            ordered.ToDictionary(rule => rule.Id, StringComparer.Ordinal));
        Capabilities = ContentLoadCapabilities.Composed.AdvanceTo(ContentLoadStage.Typed);
    }

    public RuleSectionDefinition Definition { get; }

    public IReadOnlyList<TypedRule<TRule>> Rules { get; }

    public ContentLoadCapabilities Capabilities { get; }

    public bool TryGet(string id, out TypedRule<TRule>? rule)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.TryGetValue(id, out rule);
    }
}
