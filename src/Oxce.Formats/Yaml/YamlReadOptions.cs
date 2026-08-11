namespace Oxce.Formats.Yaml;

public sealed record YamlReadOptions
{
    public const int DefaultMaxBytes = 16 * 1024 * 1024;

    public int MaxBytes { get; init; } = DefaultMaxBytes;

    public int MaxDepth { get; init; } = 128;

    public int MaxNodes { get; init; } = 1_000_000;

    public int MaxDocuments { get; init; } = 16;

    public int MaxAliases { get; init; } = 4096;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxNodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDocuments);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxAliases);
    }
}
