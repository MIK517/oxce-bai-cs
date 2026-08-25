namespace Oxce.Mods.Rulesets;

public sealed record RulesetCatalogNormalizationOptions
{
    public int MaximumDepth { get; init; } = 128;

    public int MaximumOutputBytes { get; init; } = 64 * 1024 * 1024;

    public Func<string, string>? NormalizeSourceName { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumOutputBytes);
    }
}
