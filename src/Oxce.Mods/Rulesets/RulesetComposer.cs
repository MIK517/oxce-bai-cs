using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Files;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets;

public static class RulesetComposer
{
    private const int MaximumRefNodeDepth = 64;

    private static readonly string[] OperationKeys =
        ["delete", "new", "update", "override", "ignore"];

    public static UnresolvedRuleCatalog Compose(
        ModLoadPlan plan,
        IEnumerable<RuleSectionDefinition> sections,
        IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sections);
        if (!plan.IsValid)
        {
            throw new ArgumentException("Cannot compose rules from an invalid mod load plan.", nameof(plan));
        }

        diagnostics ??= NullDiagnosticSink.Instance;
        options ??= new RulesetCompositionOptions();
        options.Validate();

        var definitions = sections.ToArray();
        var duplicate = definitions.GroupBy(item => item.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Rule section '{duplicate.Key}' is registered more than once.", nameof(sections));
        }

        var states = definitions.Select(definition => new SectionState(definition)).ToArray();
        var operationCount = 0;
        foreach (var group in plan.Groups)
        {
            foreach (var file in group.Rulesets)
            {
                using var input = file.OpenRead();
                var documents = YamlCompatibilityReader.Parse(input, file.SourcePath, options.Yaml);
                if (documents.Documents.Count != 1)
                {
                    throw Error(
                        documents.Documents.Count == 0 ? UnknownSpan(file.SourcePath) : documents.Documents[1].Span,
                        "Ruleset files must contain exactly one YAML document.");
                }

                var root = documents.Documents[0].Root;
                if (root is YamlNullNode)
                {
                    continue;
                }

                if (root is not YamlMappingNode mapping)
                {
                    throw Error(root.Span, "Ruleset document root must be a mapping.");
                }

                foreach (var state in states)
                {
                    if (!mapping.TryGet(state.Definition.Name, out var sectionNode))
                    {
                        continue;
                    }

                    if (sectionNode is not YamlSequenceNode sequence)
                    {
                        throw Error(sectionNode!.Span, $"Rule section '{state.Definition.Name}' must be a sequence.");
                    }

                    foreach (var item in sequence.Items)
                    {
                        operationCount = checked(operationCount + 1);
                        if (operationCount > options.MaximumRuleOperations)
                        {
                            throw Error(item.Span, $"Ruleset input exceeds the {options.MaximumRuleOperations}-operation limit.");
                        }

                        if (item is not YamlMappingNode ruleNode)
                        {
                            throw Error(item.Span, $"Entries in rule section '{state.Definition.Name}' must be mappings.");
                        }

                        Apply(state, ruleNode, group.Mod.Metadata.Id, file, diagnostics);
                    }
                }
            }
        }

        return new UnresolvedRuleCatalog(states.Select(static state => state.Freeze()));
    }

    private static void Apply(
        SectionState state,
        YamlMappingNode node,
        string modId,
        VirtualFileEntry file,
        IDiagnosticSink diagnostics)
    {
        var markers = new List<(string Key, YamlNode Value)>();
        if (node.TryGet(state.Definition.IdentityKey, out var defaultValue))
        {
            markers.Add((state.Definition.IdentityKey, defaultValue!));
        }

        foreach (var key in OperationKeys)
        {
            if (node.TryGet(key, out var value))
            {
                markers.Add((key, value!));
            }
        }

        if (markers.Count == 0)
        {
            throw Error(node.Span, $"Rule in section '{state.Definition.Name}' is missing its main node.");
        }

        if (markers.Count > 1)
        {
            throw Error(
                markers[1].Value.Span,
                $"Rule in section '{state.Definition.Name}' has conflicting main nodes " +
                $"'{markers[0].Key}' and '{markers[1].Key}'.");
        }

        var marker = markers[0];
        if (string.Equals(marker.Key, "ignore", StringComparison.Ordinal))
        {
            return;
        }

        var id = ReadRuleId(marker.Value, marker.Key);
        if (string.Equals(marker.Key, "delete", StringComparison.Ordinal))
        {
            state.Delete(id);
            return;
        }

        var kind = marker.Key switch
        {
            "new" => RuleOperationKind.New,
            "override" => RuleOperationKind.Override,
            "update" => RuleOperationKind.Update,
            _ => RuleOperationKind.Default,
        };
        var exists = state.TryGet(id, out var rule);
        if (kind == RuleOperationKind.New && exists)
        {
            Report(diagnostics, ModDiagnosticCodes.DuplicateNewRule, DiagnosticSeverity.Error,
                $"Rule '{id}' already exists; 'new' was ignored.", state, id, modId, file, marker.Value.Span);
            return;
        }

        if (kind == RuleOperationKind.Override && !exists)
        {
            Report(diagnostics, ModDiagnosticCodes.MissingOverrideRule, DiagnosticSeverity.Error,
                $"Rule '{id}' does not exist; 'override' was ignored.", state, id, modId, file, marker.Value.Span);
            return;
        }

        if (kind == RuleOperationKind.Update && !exists)
        {
            Report(diagnostics, ModDiagnosticCodes.MissingUpdateRule, DiagnosticSeverity.Information,
                $"Rule '{id}' does not exist; 'update' was ignored.", state, id, modId, file, marker.Value.Span);
            return;
        }

        ValidateRefNodes(node, id, 0);
        var operation = new UnresolvedRuleOperation(
            kind,
            node,
            new RuleOperationSource(file.Provenance.LayerId, modId, file.SourcePath, node.Span));
        if (exists)
        {
            rule!.Operations.Add(operation);
        }
        else
        {
            state.Add(id, operation);
        }
    }

    private static string ReadRuleId(YamlNode node, string marker)
    {
        if (node is YamlNullNode)
        {
            throw Error(node.Span, $"Main node '{marker}' has an empty rule name.");
        }

        var id = YamlValueReader.ReadString(node);
        if (id.Length == 0 || id == "\0")
        {
            throw Error(node.Span, $"Main node '{marker}' has an empty rule name.");
        }

        return id;
    }

    private static void ValidateRefNodes(YamlMappingNode node, string id, int depth)
    {
        if (depth > MaximumRefNodeDepth)
        {
            throw Error(node.Span, $"Rule '{id}' exceeds the {MaximumRefNodeDepth}-level refNode limit.");
        }

        if (!node.TryGet("refNode", out var parent))
        {
            return;
        }

        if (parent is not YamlMappingNode parentMapping)
        {
            throw Error(parent!.Span, $"Rule '{id}' has a non-mapping refNode at depth {depth}.");
        }

        ValidateRefNodes(parentMapping, id, checked(depth + 1));
    }

    private static void Report(
        IDiagnosticSink diagnostics,
        string code,
        DiagnosticSeverity severity,
        string message,
        SectionState state,
        string id,
        string modId,
        VirtualFileEntry file,
        SourceSpan span) => diagnostics.Report(new DiagnosticEvent(
            code,
            severity,
            message,
            span,
            new DiagnosticContext(
                LayerId: file.Provenance.LayerId,
                ModId: modId,
                RuleType: state.Definition.Name,
                RuleId: id)));

    private static YamlFormatException Error(SourceSpan span, string message) => new(message, span);

    private static SourceSpan UnknownSpan(string sourcePath)
    {
        var position = new SourcePosition(1, 1, 0);
        return new SourceSpan(sourcePath, position, position);
    }

    private sealed class SectionState
    {
        private readonly Dictionary<string, MutableRule> _byId = new(StringComparer.Ordinal);
        private readonly List<MutableRule> _ordered = [];

        public SectionState(RuleSectionDefinition definition) => Definition = definition;

        public RuleSectionDefinition Definition { get; }

        public void Add(string id, UnresolvedRuleOperation operation)
        {
            var rule = new MutableRule(id, operation);
            _byId.Add(id, rule);
            _ordered.Add(rule);
        }

        public void Delete(string id)
        {
            if (!_byId.Remove(id, out var rule))
            {
                return;
            }

            _ordered.Remove(rule);
        }

        public bool TryGet(string id, out MutableRule? rule) => _byId.TryGetValue(id, out rule);

        public UnresolvedRuleSection Freeze() => new(
            Definition,
            _ordered.Select(rule => new UnresolvedRule(rule.Id, rule.Operations)));
    }

    private sealed class MutableRule
    {
        public MutableRule(string id, UnresolvedRuleOperation operation)
        {
            Id = id;
            Operations = [operation];
        }

        public string Id { get; }

        public List<UnresolvedRuleOperation> Operations { get; }
    }
}
