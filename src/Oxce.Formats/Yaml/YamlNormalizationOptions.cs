namespace Oxce.Formats.Yaml;

public sealed record YamlNormalizationOptions
{
    public int MaxDepth { get; init; } = 128;

    public int MaxOutputBytes { get; init; } = 64 * 1024 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxOutputBytes);
    }
}
