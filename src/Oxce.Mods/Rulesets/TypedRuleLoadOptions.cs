using Oxce.Core.Diagnostics;

namespace Oxce.Mods.Rulesets;

public sealed record TypedRuleLoadOptions
{
    public int MaximumPropertyNodes { get; init; } = 1_000_000;

    public DiagnosticSeverity UnconsumedPropertySeverity { get; init; } = DiagnosticSeverity.Error;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPropertyNodes);
        if (!Enum.IsDefined(UnconsumedPropertySeverity))
        {
            throw new ArgumentOutOfRangeException(nameof(UnconsumedPropertySeverity));
        }
    }
}
