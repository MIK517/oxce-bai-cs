using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets;

public sealed class RulePropertyReader
{
    private static readonly string[] LifecycleKeys =
        ["delete", "new", "update", "override", "ignore"];

    private readonly YamlMappingNode _node;
    private readonly RuleOperationSource _source;
    private readonly RuleSectionDefinition _section;
    private readonly string _ruleId;
    private readonly IDiagnosticSink _diagnostics;
    private readonly TypedRuleLoadOptions _options;
    private readonly PropertyNodeBudget _budget;
    private readonly List<DeferredRuleProperty> _deferredProperties;
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);
    private bool _completed;

    internal RulePropertyReader(
        YamlMappingNode node,
        RuleOperationSource source,
        RuleSectionDefinition section,
        string ruleId,
        IDiagnosticSink diagnostics,
        TypedRuleLoadOptions options,
        PropertyNodeBudget budget,
        List<DeferredRuleProperty> deferredProperties,
        bool consumeLifecycleKeys)
    {
        _node = node;
        _source = source;
        _section = section;
        _ruleId = ruleId;
        _diagnostics = diagnostics;
        _options = options;
        _budget = budget;
        _deferredProperties = deferredProperties;
        if (consumeLifecycleKeys)
        {
            _consumed.Add(section.IdentityKey);
            foreach (var key in LifecycleKeys)
            {
                _consumed.Add(key);
            }
        }
    }

    public RuleOperationSource Source => _source;

    public SourceSpan Span => _node.Span;

    public bool TryGet(string key, out YamlNode? value)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(key);
        if (!_node.TryGet(key, out value))
        {
            return false;
        }

        _consumed.Add(key);
        return true;
    }

    public string ReadString(string key, string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : defaultValue;
    }

    public int ReadInt32(string key, int defaultValue) =>
        TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : defaultValue;

    public float ReadSingle(string key, float defaultValue) =>
        TryGet(key, out var node) ? YamlValueReader.ReadSingle(node!) : defaultValue;

    public bool ReadBoolean(string key, bool defaultValue) =>
        TryGet(key, out var node) ? YamlValueReader.ReadBoolean(node!) : defaultValue;

    public void ApplyRefNode(Action<RulePropertyReader> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (!TryGet("refNode", out var node))
        {
            return;
        }

        if (node is not YamlMappingNode mapping)
        {
            throw new YamlFormatException(
                $"Rule '{_ruleId}' in section '{_section.Name}' has a non-mapping refNode.",
                node!.Span);
        }

        ApplyNested(mapping, apply);
    }

    public bool ApplyMapping(string key, Action<RulePropertyReader> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (!TryGet(key, out var node))
        {
            return false;
        }

        if (node is not YamlMappingNode mapping)
        {
            throw new YamlFormatException(
                $"Rule property '{key}' in rule '{_ruleId}' must be a mapping.",
                node!.Span);
        }

        ApplyNested(mapping, apply);
        return true;
    }

    public bool ApplyMappingSequence(string key, Action<RulePropertyReader> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (!TryGet(key, out var node))
        {
            return false;
        }

        if (node is not YamlSequenceNode sequence)
        {
            throw new YamlFormatException(
                $"Rule property '{key}' in rule '{_ruleId}' must be a sequence.",
                node!.Span);
        }

        foreach (var item in sequence.Items)
        {
            if (item is not YamlMappingNode mapping)
            {
                throw new YamlFormatException(
                    $"Entries in rule property '{key}' for rule '{_ruleId}' must be mappings.",
                    item.Span);
            }

            ApplyNested(mapping, apply);
        }

        return true;
    }

    public bool Defer(string key, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!TryGet(key, out var node))
        {
            return false;
        }

        _deferredProperties.Add(new DeferredRuleProperty(key, node!, reason, _source));
        _diagnostics.Report(new DiagnosticEvent(
            ModDiagnosticCodes.DeferredRuleProperty,
            DiagnosticSeverity.Warning,
            $"Rule property '{key}' is preserved but deferred: {reason}",
            node!.Span,
            Context()));
        return true;
    }

    public void DeferRemaining(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureOpen();
        foreach (var entry in _node.Entries)
        {
            var key = entry.ScalarKey;
            if (key is null || _consumed.Contains(key))
            {
                continue;
            }

            _consumed.Add(key);
            _deferredProperties.Add(new DeferredRuleProperty(key, entry.Value, reason, _source));
            _diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.DeferredRuleProperty,
                DiagnosticSeverity.Warning,
                $"Rule property '{key}' is preserved but deferred: {reason}",
                entry.Key.Span,
                Context()));
        }
    }

    internal void Complete()
    {
        EnsureOpen();
        _completed = true;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _node.Entries)
        {
            _budget.Count(entry.Key.Span);
            var key = entry.ScalarKey;
            if (key is not null && _consumed.Contains(key))
            {
                continue;
            }

            var displayName = key ?? "<complex-key>";
            if (!reported.Add(displayName))
            {
                continue;
            }

            _diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.UnconsumedRuleProperty,
                _options.UnconsumedPropertySeverity,
                $"Rule '{_ruleId}' in section '{_section.Name}' has unconsumed property '{displayName}'.",
                entry.Key.Span,
                Context()));
        }
    }

    private DiagnosticContext Context() => new(
        LayerId: _source.LayerId,
        ModId: _source.ModId,
        RuleType: _section.Name,
        RuleId: _ruleId);

    private void ApplyNested(YamlMappingNode mapping, Action<RulePropertyReader> apply)
    {
        var nested = new RulePropertyReader(
            mapping,
            _source,
            _section,
            _ruleId,
            _diagnostics,
            _options,
            _budget,
            _deferredProperties,
            consumeLifecycleKeys: false);
        apply(nested);
        nested.Complete();
    }

    private void EnsureOpen()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The rule property reader has already completed.");
        }
    }

    internal sealed class PropertyNodeBudget
    {
        private readonly int _maximum;
        private int _count;

        public PropertyNodeBudget(int maximum) => _maximum = maximum;

        public void Count(SourceSpan span)
        {
            _count = checked(_count + 1);
            if (_count > _maximum)
            {
                throw new YamlFormatException(
                    $"Typed rule loading exceeds the {_maximum}-property node limit.",
                    span);
            }
        }
    }
}

public sealed record DeferredRuleProperty(
    string Key,
    YamlNode Node,
    string Reason,
    RuleOperationSource Source);
