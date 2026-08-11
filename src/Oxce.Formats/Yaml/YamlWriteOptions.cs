namespace Oxce.Formats.Yaml;

public sealed record YamlWriteOptions
{
    public int MaxBytes { get; init; } = YamlReadOptions.DefaultMaxBytes;

    public int MaxDepth { get; init; } = 128;

    public int IndentSize { get; init; } = 2;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(IndentSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(IndentSize, 8);
    }
}
