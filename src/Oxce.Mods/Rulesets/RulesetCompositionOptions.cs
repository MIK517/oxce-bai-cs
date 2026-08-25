using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets;

public sealed record RulesetCompositionOptions
{
    public const int DefaultMaximumRuleOperations = 1_000_000;

    public int MaximumRuleOperations { get; init; } = DefaultMaximumRuleOperations;

    public YamlReadOptions Yaml { get; init; } = new();

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumRuleOperations);
        ArgumentNullException.ThrowIfNull(Yaml);
    }
}
